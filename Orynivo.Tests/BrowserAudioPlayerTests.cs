using Orynivo.Audio;
using Orynivo.Remote;
using Xunit;

namespace Orynivo.Tests;

/// <summary>Exercises browser output without a window, hardware, personal files or an encoder process.</summary>
public sealed class BrowserAudioPlayerTests
{
    /// <summary>Seek and pause reject late feedback while retaining the active source and position.</summary>
    /// <returns>The asynchronous ownership regression.</returns>
    [Fact]
    public async Task FeedbackIsBoundToOwnerStreamAndControlRevision()
    {
        using var player = new BrowserAudioPlayer("page", new("synthetic.dsf", 1), TimeSpan.FromMinutes(3));
        var first = player.Snapshot();
        Assert.False(player.Report(new("another", first.StreamId!, first.ControlRevision, 15, false)));
        Assert.True(player.Report(new("page", first.StreamId!, first.ControlRevision, 15, false)));
        player.Pause();
        Assert.False(player.Report(new("page", first.StreamId!, first.ControlRevision, 25, true)));
        Assert.Equal(15, player.Position.TotalSeconds);
        await player.SeekAsync(TimeSpan.FromSeconds(40));
        var sought = player.Snapshot();
        Assert.NotEqual(first.StreamId, sought.StreamId);
        Assert.Equal(40, sought.StartSeconds);
        Assert.True(player.IsPaused);
        Assert.True(player.UserPaused);
        Assert.False(player.Report(new("page", first.StreamId!, sought.ControlRevision, 150, true, true)));
        Assert.False(player.WaitForCompletionAsync().IsCompleted);
        player.Resume();
        var resumed = player.Snapshot();
        Assert.True(player.Report(new("page", resumed.StreamId!, resumed.ControlRevision, 180, false, true)));
        await player.WaitForCompletionAsync();
    }

    /// <summary>Disposed output cancels its encoder and waits for its final cleanup before returning.</summary>
    /// <returns>The full-drain regression.</returns>
    [Fact]
    public async Task FullReleaseWaitsForCancelledStreamCleanup()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new BrowserAudioPlayer("page", new("synthetic.flac", 1), TimeSpan.FromSeconds(100),
            async (_, _, _, _, ct) =>
            {
                entered.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { cancelled.SetResult(); }
                await cleanup.Task;
            });
        using var output = new MemoryStream();
        var streaming = player.WriteAsync(player.Snapshot().StreamId!, output, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var retirement = new AudioPlayerRetirement(_ => { });
        var release = retirement.RetireAsync(player, waitForCompleteDisposal: true);
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(release.IsCompleted);
        }
        finally { cleanup.TrySetResult(); }
        await release.WaitAsync(TimeSpan.FromSeconds(10));
        await streaming;
        Assert.True(player.WaitForCompletionAsync().IsCanceled);
    }

    /// <summary>Replacing a stream cancels the previous encoder and cannot report its old completion.</summary>
    /// <returns>The seek/drain regression.</returns>
    [Fact]
    public async Task SeekCancelsOldEncoderAndOnlyBrowserEndCompletesTrack()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var player = new BrowserAudioPlayer("page", new("synthetic.flac", 1), TimeSpan.FromMinutes(1),
            async (_, _, _, _, ct) => { entered.SetResult(); try { await Task.Delay(Timeout.Infinite, ct); } finally { cancelled.SetResult(); } });
        using var output = new MemoryStream();
        var first = player.Snapshot();
        var writing = player.WriteAsync(first.StreamId!, output, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await player.SeekAsync(TimeSpan.FromSeconds(12));
        await writing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(cancelled.Task.IsCompleted);
        Assert.False(player.WaitForCompletionAsync().IsCompleted);
        Assert.False(player.Report(new("page", first.StreamId!, first.ControlRevision, 60, false, true)));
        Assert.Equal(12, player.Snapshot().StartSeconds);
    }

    /// <summary>Rejects non-finite feedback and preserves PCM conversion arguments for DSD and CUE sources.</summary>
    [Fact]
    public void DsdAndVirtualSegmentsUseEncodedPcmWithoutSourceMetadata()
    {
        var item = new GaplessPlaybackItem("cue://logical", 1, SourcePath: "synthetic.dsf",
            SegmentStart: TimeSpan.FromSeconds(10), SegmentEnd: TimeSpan.FromSeconds(30));
        var args = BrowserAudioTranscoder.CreateStartInfo(item, TimeSpan.FromSeconds(3), 1).ArgumentList.ToArray();
        Assert.Contains("libmp3lame", args);
        Assert.Contains("48000", args);
        Assert.Contains("192k", args);
        Assert.DoesNotContain("copy", args);
        Assert.Equal("13", args[Array.IndexOf(args, "-ss") + 1]);
        Assert.Equal("17", args[Array.IndexOf(args, "-t") + 1]);
        Assert.Equal("-1", args[Array.IndexOf(args, "-map_metadata") + 1]);
        using var player = new BrowserAudioPlayer("page", item, TimeSpan.FromSeconds(20));
        var state = player.Snapshot();
        Assert.False(player.Report(new("page", state.StreamId!, state.ControlRevision, double.NaN, true)));
        Assert.False(player.Report(new("page", state.StreamId!, state.ControlRevision, -1, true)));
    }

    /// <summary>A disconnected page pauses device-free output instead of reopening any physical device.</summary>
    [Fact]
    public void MissingPageFeedbackExpiresToPause()
    {
        var clock = new ManualClock();
        using var player = new BrowserAudioPlayer("page", new("synthetic.flac", 1), TimeSpan.FromMinutes(3), clock: clock);
        var state = player.Snapshot();
        Assert.True(player.Report(new("page", state.StreamId!, state.ControlRevision, 20, false)));
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(player.IsPaused);
        Assert.False(player.UserPaused);
        Assert.Equal(20, player.Position.TotalSeconds);
        Assert.Equal(state.StreamId, player.Snapshot().StreamId);
        Assert.False(player.WaitForCompletionAsync().IsCompleted);
    }

    /// <summary>Direct files use absolute media seeking; unsupported live codecs fall back without waiting for EOF.</summary>
    /// <returns>The asynchronous direct-seek regression.</returns>
    [Fact]
    public async Task DirectStreamsKeepTheirFormatAndUseNativeSeek()
    {
        using var podcast = new BrowserAudioPlayer("page", new("synthetic.mp3", 1), TimeSpan.Zero, direct: true, directMime: "audio/mpeg");
        var initial = podcast.Snapshot();
        Assert.True(podcast.Report(new("page", initial.StreamId!, initial.ControlRevision, 2, false, DurationSeconds: 300)));
        Assert.True(podcast.CanSeek);
        await podcast.SeekAsync(TimeSpan.FromSeconds(100));
        Assert.Equal(0, podcast.Snapshot().StartSeconds);
        Assert.Equal(100, podcast.Snapshot().SeekSeconds);
        Assert.Equal("MP3", podcast.OutputFormat);
        using var radio = new BrowserAudioPlayer("page", new("https://example.invalid/live", 1), TimeSpan.Zero,
            direct: true, directMime: "audio/aac", live: true);
        var live = radio.Snapshot();
        Assert.True(radio.Report(new("page", live.StreamId!, live.ControlRevision, 12, false, Failed: true, DurationSeconds: 30)));
        Assert.False(radio.CanSeek);
        Assert.False(radio.Snapshot().Direct);
        Assert.Equal(0, radio.Snapshot().StartSeconds);
        Assert.Contains("192 kbps", radio.OutputFormat);
        Assert.False(radio.WaitForCompletionAsync().IsCompleted);
        using var encoded = new BrowserAudioPlayer("page", new("synthetic.dsf", 1), TimeSpan.FromSeconds(300));
        await encoded.SeekAsync(TimeSpan.FromSeconds(100));
        var seek = encoded.Snapshot();
        Assert.True(encoded.Report(new("page", seek.StreamId!, seek.ControlRevision, 101, false, DurationSeconds: 200)));
        Assert.Equal(300, encoded.Duration.TotalSeconds);
    }

    /// <summary>Controls a single player's inactivity timer without sleeps.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _milliseconds;
        private Action? _tick;
        /// <inheritdoc/>
        public override long TimestampFrequency => 1000;
        /// <inheritdoc/>
        public override long GetTimestamp() => _milliseconds;
        /// <inheritdoc/>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _tick = () => callback(state); return new ManualTimer(); }
        /// <summary>Advances the fixture clock and invokes its timer.</summary>
        /// <param name="elapsed">Elapsed fixture time.</param>
        internal void Advance(TimeSpan elapsed) { _milliseconds += (long)elapsed.TotalMilliseconds; _tick?.Invoke(); }
    }

    /// <summary>Provides an inert owned test timer.</summary>
    private sealed class ManualTimer : ITimer
    {
        /// <inheritdoc/>
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        /// <inheritdoc/>
        public void Dispose() { }
        /// <inheritdoc/>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
