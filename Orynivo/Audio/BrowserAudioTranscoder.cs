using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Orynivo.Audio;

/// <summary>Streams the selected PCM music encoding without opening an audio device.</summary>
internal static class BrowserAudioTranscoder
{
    /// <summary>Reads technical metadata with cancellation-safe child cleanup.</summary>
    /// <param name="item">Desktop-resolved source, including virtual segment bounds.</param>
    /// <param name="ct">Preparation cancellation.</param>
    /// <returns>Source metadata with the browser's 48 kHz output rate.</returns>
    internal static async Task<AudioFileInfo> ProbeAsync(GaplessPlaybackItem item, CancellationToken ct)
    {
        if (item.TryCreateKnownAudioInfo() is { } known && item.KnownDuration is { } knownDuration)
            return known with { OutputSampleRate = 48000, Duration = item.SegmentDuration ?? knownDuration };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = StartInfo("ffprobe") };
        if (item.PlaybackPaths[0].StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            item.PlaybackPaths[0].StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            foreach (var arg in new[] { "-analyzeduration", "500000", "-probesize", "500000" }) process.StartInfo.ArgumentList.Add(arg);
        foreach (var arg in new[] { "-v", "quiet", "-select_streams", "a:0", "-show_entries",
            "stream=codec_name,sample_rate,channels,duration:format=duration", "-of", "json", item.PlaybackPaths[0] })
            process.StartInfo.ArgumentList.Add(arg);
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            process.Start();
            var json = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0 || json.Length > 65536) throw new InvalidOperationException("browser_audio_probe_failed");
            using var document = JsonDocument.Parse(json);
            var stream = document.RootElement.GetProperty("streams")[0];
            var codec = stream.GetProperty("codec_name").GetString() ?? "unknown";
            var rate = int.Parse(stream.GetProperty("sample_rate").GetString() ?? "48000", CultureInfo.InvariantCulture);
            var duration = item.SegmentDuration ?? item.KnownDuration;
            if (duration is null)
            {
                var value = stream.TryGetProperty("duration", out var streamDuration) ? streamDuration
                    : document.RootElement.GetProperty("format").TryGetProperty("duration", out var formatDuration) ? formatDuration : default;
                duration = value.ValueKind != JsonValueKind.Undefined && double.TryParse(value.ToString(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds > 0
                    ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
            }
            return new AudioFileInfo(codec, rate, stream.GetProperty("channels").GetInt32(), 48000,
                codec.Contains("dsd", StringComparison.OrdinalIgnoreCase), "mp3", duration.Value);
        }
        finally { await TerminateAsync(process).ConfigureAwait(false); }
    }

    /// <summary>Transcodes a desktop-resolved source to the response and awaits encoder cleanup on every exit.</summary>
    /// <param name="item">Original source and segment boundaries.</param>
    /// <param name="position">Logical track offset.</param>
    /// <param name="gain">PCM ReplayGain factor.</param>
    /// <param name="destination">Borrowed response stream.</param>
    /// <param name="ct">Request, revision and player-lifetime cancellation.</param>
    /// <returns>Completion after the encoder exits.</returns>
    internal static Task WriteAsync(GaplessPlaybackItem item, TimeSpan position, float gain, Stream destination, CancellationToken ct) =>
        WriteEncodedAsync(item, position, gain, destination, ct, BrowserAudioEncoding.Find("mp3-192")!);

    /// <summary>Streams the selected PCM encoding with response backpressure and no whole-file conversion.</summary>
    /// <param name="item">Resolved media and segment bounds.</param>
    /// <param name="position">Logical source offset.</param>
    /// <param name="gain">PCM gain.</param>
    /// <param name="destination">Borrowed response.</param>
    /// <param name="ct">Owned cancellation.</param>
    /// <param name="encoding">Validated output choice.</param>
    /// <returns>Completion after child cleanup.</returns>
    internal static async Task WriteEncodedAsync(GaplessPlaybackItem item, TimeSpan position, float gain, Stream destination, CancellationToken ct, BrowserAudioEncoding encoding)
    {
        using var process = new Process { StartInfo = CreateStartInfo(item, position, gain, encoding) };
        try
        {
            ct.ThrowIfCancellationRequested();
            process.Start();
            if (item.PlaybackPaths.Count > 1)
            {
                foreach (var path in item.PlaybackPaths)
                {
                    if (path.Contains('\n') || path.Contains('\r')) throw new InvalidOperationException("invalid_concat_source");
                    await process.StandardInput.WriteLineAsync($"file '{path.Replace("'", "'\\''", StringComparison.Ordinal)}'".AsMemory(), ct).ConfigureAwait(false);
                }
                process.StandardInput.Close();
            }
            var buffer = new byte[16384];
            int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                await destination.FlushAsync(ct).ConfigureAwait(false);
            }
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException("browser_audio_encode_failed");
        }
        finally { await TerminateAsync(process).ConfigureAwait(false); }
    }

    /// <summary>Builds shell-free encoding arguments for PCM, DSD, streams and virtual segments.</summary>
    /// <param name="item">Desktop-owned source.</param>
    /// <param name="position">Logical playback offset.</param>
    /// <param name="gain">PCM ReplayGain factor.</param>
    /// <param name="encoding">Optional validated output choice.</param>
    /// <returns>A private process configuration, never logged or returned to a browser.</returns>
    internal static ProcessStartInfo CreateStartInfo(GaplessPlaybackItem item, TimeSpan position, float gain, BrowserAudioEncoding? encoding = null)
    {
        var info = StartInfo("ffmpeg");
        var args = info.ArgumentList;
        encoding ??= BrowserAudioEncoding.Find("mp3-192")!;
        foreach (var arg in new[] { "-v", "quiet", "-nostats", "-nostdin" }) args.Add(arg);
        var offset = (item.SegmentStart ?? TimeSpan.Zero) + position;
        if (offset > TimeSpan.Zero) { args.Add("-ss"); args.Add(offset.TotalSeconds.ToString(CultureInfo.InvariantCulture)); }
        foreach (var arg in new[] { "-analyzeduration", "100000", "-probesize", "32768" }) args.Add(arg);
        if (item.PlaybackPaths.Count > 1)
        {
            info.RedirectStandardInput = true;
            foreach (var arg in new[] { "-f", "concat", "-safe", "0", "-protocol_whitelist", "file,pipe,http,https,tcp,tls,crypto", "-i", "pipe:0" }) args.Add(arg);
        }
        else { args.Add("-i"); args.Add(item.PlaybackPath); }
        if (item.SegmentEnd is { } end)
        {
            args.Add("-t");
            args.Add(Math.Max(.001, (end - (item.SegmentStart ?? TimeSpan.Zero) - position).TotalSeconds).ToString(CultureInfo.InvariantCulture));
        }
        foreach (var arg in new[] { "-map", "0:a:0", "-vn", "-sn", "-dn", "-map_metadata", "-1", "-af",
            "volume=" + (float.IsFinite(gain) ? Math.Clamp(gain, 0, 16) : 1).ToString(CultureInfo.InvariantCulture),
            "-ac", "2", "-ar", encoding.Rate.ToString(CultureInfo.InvariantCulture) }) args.Add(arg);
        var codecArgs = encoding.Kbps == 0
            ? new[] { "-c:a", "flac", "-sample_fmt", encoding.Bits == 16 ? "s16" : "s32", "-bits_per_raw_sample", encoding.Bits.ToString(CultureInfo.InvariantCulture), "-compression_level", "0", "-f", "flac" }
            : new[] { "-c:a", "libmp3lame", "-b:a", encoding.Kbps.ToString(CultureInfo.InvariantCulture) + "k", "-f", "mp3", "-write_xing", "0", "-id3v2_version", "0" };
        foreach (var arg in codecArgs) args.Add(arg);
        foreach (var arg in new[] { "-flush_packets", "1", "pipe:1" }) args.Add(arg);
        return info;
    }

    /// <summary>Creates a hidden encoder/probe process without stderr or source logging.</summary>
    private static ProcessStartInfo StartInfo(string executable) => new()
    {
        FileName = executable, UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, WorkingDirectory = FfmpegLocator.GetSafeWorkingDirectory()
    };

    /// <summary>Terminates and awaits a started child without using a cancelled request token.</summary>
    private static async Task TerminateAsync(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { return; }
        catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
        try { await process.WaitForExitAsync().ConfigureAwait(false); }
        catch (InvalidOperationException) { }
    }
}
