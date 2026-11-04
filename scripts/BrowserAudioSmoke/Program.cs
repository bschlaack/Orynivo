using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Orynivo.Audio;

// Real FFmpeg, synthetic sources only. Never opens an output device or a library.
var parent = Path.GetFullPath(Path.GetTempPath());
var root = Path.GetFullPath(Path.Combine(parent, "orynivo-browser-audio-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
try
{
    var wave = Path.Combine(root, "synthetic.wav");
    using (var writer = new BinaryWriter(File.Create(wave)))
    {
        const int frames = 48000;
        writer.Write("RIFF"u8); writer.Write(36 + frames * 4); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(48000);
        writer.Write(48000 * 4); writer.Write((short)4); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(frames * 4);
        for (var i = 0; i < frames; i++)
        {
            var sample = (short)(1000 * Math.Sin(i * 2 * Math.PI * 440 / 48000));
            writer.Write(sample); writer.Write(sample);
        }
    }
    var dsf = Path.Combine(root, "synthetic.dsf");
    using (var writer = new BinaryWriter(File.Create(dsf)))
    {
        const int samples = 282240, blocks = 9, blockSize = 4096;
        const int payload = blocks * blockSize * 2;
        writer.Write("DSD "u8); writer.Write((ulong)28); writer.Write((ulong)(92 + payload)); writer.Write((ulong)0);
        writer.Write("fmt "u8); writer.Write((ulong)52); writer.Write((uint)1); writer.Write((uint)0);
        writer.Write((uint)2); writer.Write((uint)2); writer.Write((uint)2822400); writer.Write((uint)1);
        writer.Write((ulong)samples); writer.Write((uint)blockSize); writer.Write((uint)0);
        writer.Write("data"u8); writer.Write((ulong)(12 + payload));
        writer.Write(Enumerable.Repeat((byte)0x69, payload).ToArray());
    }
    var items = new[]
    {
        new GaplessPlaybackItem(wave, 1),
        new GaplessPlaybackItem(dsf, 1),
        new GaplessPlaybackItem("cue://synthetic", 1, wave, TimeSpan.FromSeconds(.25), TimeSpan.FromSeconds(.75)),
        new GaplessPlaybackItem("multipart", 1, SourcePaths: [wave, wave], KnownDuration: TimeSpan.FromSeconds(2))
    };
    for (var i = 0; i < items.Length; i++)
    {
        var info = await BrowserAudioTranscoder.ProbeAsync(items[i], timeout.Token);
        if (i == 1 && !info.IsDsd) throw new InvalidOperationException("Synthetic DSD not recognized.");
        if (i == 2 && info.Duration != TimeSpan.FromSeconds(.5)) throw new InvalidOperationException("Segment duration changed.");
        var target = Path.Combine(root, "encoded-" + i + ".mp3");
        await using (var output = File.Create(target))
            await BrowserAudioTranscoder.WriteAsync(items[i], TimeSpan.Zero, 1, output, timeout.Token);
        using var probe = new Process { StartInfo = new ProcessStartInfo("ffprobe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true } };
        foreach (var arg in new[] { "-v", "quiet", "-select_streams", "a:0", "-show_entries", "stream=codec_name,sample_rate,channels", "-of", "json", target }) probe.StartInfo.ArgumentList.Add(arg);
        probe.Start();
        var json = await probe.StandardOutput.ReadToEndAsync(timeout.Token);
        await probe.WaitForExitAsync(timeout.Token);
        using var document = JsonDocument.Parse(json);
        var stream = document.RootElement.GetProperty("streams")[0];
        if (stream.GetProperty("codec_name").GetString() != "mp3" || stream.GetProperty("sample_rate").GetString() != "48000" || stream.GetProperty("channels").GetInt32() != 2)
            throw new InvalidOperationException("Browser output is not decodable stereo 48 kHz MP3.");
    }
    foreach (var choice in BrowserAudioEncoding.All.Where(c => c.Id != "original"))
    {
        var source = choice.Id == "dsd-pcm" ? items[1] : items[0];
        var target = Path.Combine(root, choice.Id + (choice.Kbps == 0 ? ".flac" : ".mp3"));
        await using (var output = File.Create(target))
            await BrowserAudioTranscoder.WriteEncodedAsync(source, choice.Id == "dsd-pcm" ? TimeSpan.Zero : TimeSpan.FromSeconds(.25), 1, output, timeout.Token, choice);
        using var probe = new Process { StartInfo = new ProcessStartInfo("ffprobe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true } };
        foreach (var arg in new[] { "-v", "quiet", "-select_streams", "a:0", "-show_entries", "stream=codec_name,sample_rate,channels,bits_per_raw_sample,bit_rate", "-of", "json", target }) probe.StartInfo.ArgumentList.Add(arg);
        probe.Start();
        var json = await probe.StandardOutput.ReadToEndAsync(timeout.Token);
        await probe.WaitForExitAsync(timeout.Token);
        using var document = JsonDocument.Parse(json);
        var stream = document.RootElement.GetProperty("streams")[0];
        if (stream.GetProperty("codec_name").GetString() != (choice.Kbps == 0 ? "flac" : "mp3") ||
            stream.GetProperty("sample_rate").GetString() != choice.Rate.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            stream.GetProperty("channels").GetInt32() != 2 ||
            (choice.Kbps == 0 && stream.GetProperty("bits_per_raw_sample").GetString() != choice.Bits.ToString(System.Globalization.CultureInfo.InvariantCulture)) ||
            (choice.Kbps > 0 && stream.GetProperty("bit_rate").GetString() != (choice.Kbps * 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)))
            throw new InvalidOperationException("Selected music signal format was not delivered.");
    }
    Console.WriteLine("PASS: all FLAC precision/rate and MP3 bitrate choices, DSD-to-FLAC and input-side seek produce the selected decodable signal.");
    using (var failedWrite = new FailedResponse())
    {
        try { await BrowserAudioTranscoder.WriteAsync(items[0], TimeSpan.Zero, 1, failedWrite, timeout.Token); throw new InvalidOperationException("Failed response accepted."); }
        catch (IOException) { }
    }
    using (var cancel = new CancellationTokenSource())
    using (var cancelledResponse = new CancelledResponse(cancel))
    {
        try { await BrowserAudioTranscoder.WriteAsync(items[0], TimeSpan.Zero, 1, cancelledResponse, cancel.Token); throw new InvalidOperationException("Cancelled response accepted."); }
        catch (OperationCanceledException) { }
    }
    await StreamingRegressions.RunAsync(root, timeout.Token);
    Console.WriteLine("PASS: real FFmpeg PCM/DSF/virtual-segment/multipart conversion to decodable stereo 48 kHz MP3; failed-write and cancellation cleanup. No audio devices or personal sources.");
}
finally
{
    if (!root.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("orynivo-browser-audio-", StringComparison.Ordinal))
        throw new InvalidOperationException("Unexpected fixture cleanup path.");
    Directory.Delete(root, recursive: true);
}

/// <summary>Fails a synthetic HTTP write to exercise actual encoder cleanup.</summary>
internal sealed class FailedResponse : MemoryStream
{
    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new IOException("Synthetic response failure."));
}

/// <summary>Cancels a synthetic response after the first encoded block.</summary>
/// <param name="cancellation">Owned cancellation source for the fixture.</param>
internal sealed class CancelledResponse(CancellationTokenSource cancellation) : MemoryStream
{
    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellation.Cancel();
        return ValueTask.FromCanceled(cancellationToken);
    }
}
