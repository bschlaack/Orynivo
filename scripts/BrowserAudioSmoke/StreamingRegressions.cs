using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Orynivo.Audio;

/// <summary>Exercises never-finished HTTP inputs and actual progressive output without audio hardware.</summary>
internal static class StreamingRegressions
{
    /// <summary>Checks verbatim live relay, native range reads and MP3 bytes before upstream EOF.</summary>
    /// <param name="root">Owned synthetic fixture directory.</param>
    /// <param name="ct">Fixture cancellation.</param>
    /// <returns>Completion after all source requests and encoders are stopped.</returns>
    internal static async Task RunAsync(string root, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        var mp3 = await File.ReadAllBytesAsync(Path.Combine(root, "encoded-0.mp3"), ct);
        var wave = await File.ReadAllBytesAsync(Path.Combine(root, "synthetic.wav"), ct);
        var relayTail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var encodeTail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MapHeld("/live", "audio/mpeg", mp3, mp3.Length / 2, relayTail);
        MapHeld("/held.wav", "audio/wav", wave, 44 + 144000, encodeTail);
        app.MapGet("/range.mp3", () => Results.File(Path.Combine(root, "encoded-0.mp3"), "audio/mpeg", enableRangeProcessing: true));
        await app.StartAsync(ct);
        try
        {
            var address = app.Urls.Single();
            var encoderCalled = false;
            using (var live = new BrowserAudioPlayer("fixture", new(address + "/live", 1), TimeSpan.Zero,
                encode: (_, _, _, _, _) => { encoderCalled = true; throw new InvalidOperationException("Live relay invoked encoder."); },
                direct: true, directMime: "audio/mpeg", live: true))
            using (var output = new FirstPacket())
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var context = Context(output);
                var copy = live.WriteResponseAsync(live.Snapshot().StreamId!, context, cancel.Token);
                try
                {
                    await output.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                    if (copy.IsCompleted || encoderCalled || !output.ToArray().AsSpan().SequenceEqual(mp3.AsSpan(0, output.ToArray().Length)))
                        throw new InvalidOperationException("Live audio was buffered to EOF, transcoded or changed.");
                }
                finally { cancel.Cancel(); relayTail.TrySetResult(); }
                await copy.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
            using (var rangeOutput = new MemoryStream())
            {
                var context = Context(rangeOutput);
                context.Request.Headers.Range = "bytes=2-10";
                await BrowserAudioRelay.WriteAsync(address + "/range.mp3", "audio/mpeg", context, _ => { }, ct);
                if (context.Response.StatusCode != 206 || !rangeOutput.ToArray().AsSpan().SequenceEqual(mp3.AsSpan(2, 9)) ||
                    !context.Response.Headers.ContentRange.ToString().StartsWith("bytes 2-10/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Podcast range forwarding changed bytes or headers.");
            }
            using (var output = new FirstPacket())
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var item = new GaplessPlaybackItem(address + "/held.wav", 1, KnownDuration: TimeSpan.FromSeconds(1));
                var encoding = BrowserAudioTranscoder.WriteAsync(item, TimeSpan.Zero, 1, output, cancel.Token);
                try
                {
                    await output.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                    if (encoding.IsCompleted || output.Length == 0)
                        throw new InvalidOperationException("MP3 conversion did not stream before the source ended.");
                }
                finally { cancel.Cancel(); encodeTail.TrySetResult(); }
                try { await encoding.WaitAsync(TimeSpan.FromSeconds(10), ct); }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            }
            Console.WriteLine("PASS: live relay and real MP3 encoder emit bytes before held upstream EOF; direct media preserves bytes and 206 ranges.");
        }
        finally
        {
            relayTail.TrySetResult(); encodeTail.TrySetResult();
            await app.StopAsync(CancellationToken.None);
        }

        void MapHeld(string path, string mime, byte[] bytes, int first, TaskCompletionSource release)
        {
            app.MapGet(path, async (HttpContext context) =>
            {
                context.Response.ContentType = mime;
                context.Response.ContentLength = bytes.Length;
                try
                {
                    await context.Response.Body.WriteAsync(bytes.AsMemory(0, first), context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    await release.Task.WaitAsync(context.RequestAborted);
                    await context.Response.Body.WriteAsync(bytes.AsMemory(first), context.RequestAborted);
                }
                catch (OperationCanceledException) { }
            });
        }
    }

    private static DefaultHttpContext Context(Stream output)
    {
        var result = new DefaultHttpContext(); result.Response.Body = output; return result;
    }

    /// <summary>Observes the first actual audio bytes without sleeping or requiring input completion.</summary>
    private sealed class FirstPacket : MemoryStream
    {
        /// <summary>Gets completion when bytes reach the response.</summary>
        internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <inheritdoc/>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken);
            if (buffer.Length > 0) Arrived.TrySetResult();
        }
    }
}
