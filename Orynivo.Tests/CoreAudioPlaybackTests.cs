using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Orynivo.Audio;
using Orynivo.Audio.CoreAudio;
using Xunit;

namespace Orynivo.Tests;

/// <summary>Checks bounded PCM transport and composed native-player workflows without audio devices.</summary>
public sealed class CoreAudioPlaybackTests
{
    private static readonly AudioFileInfo Info = new("flac", 96000, 2, 96000, false, "flac", TimeSpan.FromMinutes(1));
    private static readonly CoreAudioOutputOptions Ordinary = new(true, false, true, false);

    /// <summary>The ring preserves wrapped frames and excludes underflow silence from media time.</summary>
    [Fact]
    public void Ring_WrapsAndZeroFillsWithoutLosingFrames()
    {
        var ring = new CoreAudioPcmRing(3);
        byte[] input = Enumerable.Range(1, 40).Select(i => (byte)i).ToArray();
        Assert.Equal(24, ring.Write(input));
        var first = new byte[16];
        Assert.Equal(16, ring.Read(first));
        Assert.Equal(input[..16], first);
        Assert.Equal(16, ring.Write(input.AsSpan(24)));
        var final = new byte[32];
        Assert.Equal(24, ring.Read(final));
        Assert.Equal(input[16..], final[..24]);
        Assert.Equal(new byte[8], final[24..]);
        Assert.Equal(5, ring.RenderedFrames);
        Assert.Equal(0, ring.QueuedFrames);
        ring.Reset();
        Assert.Equal(0, ring.RenderedFrames);
    }

    /// <summary>Consumer copies and silence filling allocate no managed objects after warmup.</summary>
    [Fact]
    public void Ring_CallbackPathDoesNotAllocate()
    {
        var ring = new CoreAudioPcmRing(8);
        var buffer = new byte[64];
        for (var i = 0; i < 100; i++) { ring.Write(buffer); ring.Read(buffer); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) { ring.Write(buffer); ring.Read(buffer); }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    /// <summary>Concurrent single producer and consumer preserve every frame across backpressure.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task Ring_ConcurrentWraparoundPreservesFrameSequence()
    {
        var ring = new CoreAudioPcmRing(37);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var producer = Task.Run(() =>
        {
            var bytes = new byte[8];
            for (long frame = 1; frame <= 10000; frame++)
            {
                MemoryMarshal.Write(bytes, in frame);
                while (ring.Write(bytes) == 0) { timeout.Token.ThrowIfCancellationRequested(); Thread.Yield(); }
            }
        });
        var consumer = Task.Run(() =>
        {
            var bytes = new byte[8];
            for (long frame = 1; frame <= 10000; frame++)
            {
                while (ring.Read(bytes) == 0) { timeout.Token.ThrowIfCancellationRequested(); Thread.Yield(); }
                Assert.Equal(frame, MemoryMarshal.Read<long>(bytes));
            }
        });
        await Task.WhenAll(producer, consumer).WaitAsync(timeout.Token);
        Assert.Equal(10000, ring.RenderedFrames);
        Assert.Equal(0, ring.QueuedFrames);
    }

    /// <summary>Ordinary PCM obeys exact matching, current-rate mode and a strict positive ceiling.</summary>
    /// <param name="match">Whether automatic matching is enabled.</param>
    /// <param name="cap">PCM ceiling.</param>
    /// <param name="expected">Selected rate or zero for rejection.</param>
    [Theory]
    [InlineData(true, 0, 96000)]
    [InlineData(true, 48000, 48000)]
    [InlineData(false, 0, 48000)]
    [InlineData(false, 44100, 0)]
    [InlineData(true, 4000, 0)]
    public void RatePolicy_RespectsModesAndCaps(bool match, int cap, int expected)
    {
        var options = Ordinary with { MatchRate = match };
        if (expected == 0) Assert.Throws<NotSupportedException>(() => options.SelectRate(Info, 48000, cap));
        else Assert.Equal(expected, options.SelectRate(Info, 48000, cap));
    }

    /// <summary>DSD conversion retains exact division families below a positive ceiling.</summary>
    [Fact]
    public void DsdConversion_UsesDivisibleRate()
    {
        var dsd = Info with { IsDsd = true, SourceSampleRate = 352800 };
        Assert.Equal(44100, Ordinary.SelectRate(dsd, 48000, 48000));
        Assert.Equal(176400, Ordinary.SelectRate(dsd, 48000, 0));
        Assert.Throws<NotSupportedException>(() => (Ordinary with { Bypass = true, Exclusive = true })
            .SelectRate(dsd, 48000, 0));
    }

    /// <summary>Bypass rejects resampling, shared output and channel conversion.</summary>
    [Fact]
    public void Bypass_RequiresMatchingExclusiveStereoPcm()
    {
        var bypass = Ordinary with { Bypass = true, Exclusive = true };
        Assert.Equal(96000, bypass.SelectRate(Info, 48000, 0));
        Assert.Throws<NotSupportedException>(() => bypass.SelectRate(Info, 48000, 48000));
        Assert.Throws<NotSupportedException>(() => (bypass with { MatchRate = false }).SelectRate(Info, 96000, 0));
        Assert.Throws<NotSupportedException>(() => (bypass with { Exclusive = false }).SelectRate(Info, 96000, 0));
        Assert.Throws<NotSupportedException>(() => bypass.SelectRate(Info with { Channels = 1 }, 48000, 0));
    }

    /// <summary>Old profiles retain defaults; a native profile and its options survive JSON roundtrip.</summary>
    [Fact]
    public void Profiles_RoundtripWithoutChangingExistingBackendValues()
    {
        var old = JsonSerializer.Deserialize<OutputProfile>("{\"Name\":\"old\",\"Backend\":1,\"SelectedWasapiDeviceId\":\"default\"}")!;
        Assert.Equal(OutputBackend.Wasapi, old.Backend);
        Assert.True(old.CoreAudioMatchRate);
        Assert.True(old.CoreAudioRestoreRate);
        Assert.False(old.CoreAudioExclusive);
        var profile = new OutputProfile { Name = "fixture", Backend = OutputBackend.CoreAudio,
            SelectedWasapiDeviceId = "fixture-uid", CoreAudioExclusive = true,
            CoreAudioMatchRate = false, CoreAudioRestoreRate = false, CoreAudioBypass = true };
        var copy = JsonSerializer.Deserialize<OutputProfile>(JsonSerializer.Serialize(profile))!;
        Assert.Equal(5, (int)copy.Backend);
        Assert.Equal(profile.SelectedWasapiDeviceId, copy.SelectedWasapiDeviceId);
        Assert.Equal(CoreAudioOutputOptions.FromProfile(profile), CoreAudioOutputOptions.FromProfile(copy));
        var frozen = CoreAudioOutputOptions.FromProfile(copy);
        copy.CoreAudioExclusive = false;
        Assert.True(frozen.Exclusive);
    }

    /// <summary>Arbitrary decoder fragmentation preserves integer PCM exactly at unity or bypass.</summary>
    /// <param name="bypass">Whether software processing is bypassed.</param>
    /// <returns>The asynchronous regression test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Player_FragmentedPcmIsByteExact(bool bypass)
    {
        var device = new Device();
        var output = new Output();
        var source = new Source(3);
        var samples = new[] { int.MinValue, int.MaxValue, 1, -1, 123456789, -123456789 };
        var bytes = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        source.Feed(bytes); source.Finish();
        var result = await Open(device, output, source, Ordinary with { Bypass = bypass, Exclusive = bypass },
            bypass ? .2f : 1, bypass ? 2 : 1);
        var player = result.AudioPlayer;
        try
        {
            await source.Eof.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var received = new byte[bytes.Length];
            Assert.Equal(bytes.Length, output.Consume(received));
            Assert.Equal(bytes, received);
            await player.WaitForCompletionAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(48000, device.SampleRate);
            Assert.True(output.Disposed);
            Assert.Equal(1, source.Disposals);
        }
        finally { player.Dispose(); }
    }

    /// <summary>Normal PCM applies software volume before entering the native callback ring.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task Player_OrdinaryVolumeIsApplied()
    {
        var source = new Source();
        int[] samples = [1073741824, -1073741824];
        source.Feed(MemoryMarshal.AsBytes(samples.AsSpan()).ToArray()); source.Finish();
        var output = new Output();
        var (player, _) = await Open(new Device(), output, source, Ordinary, .5f);
        try
        {
            await source.Eof.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var bytes = new byte[8];
            output.Consume(bytes);
            var values = MemoryMarshal.Cast<byte, int>(bytes).ToArray();
            Assert.Equal(new[] { 536870912, -536870912 }, values);
            await player.WaitForCompletionAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { player.Dispose(); }
    }

    /// <summary>Pause emits silence without losing queued media or moving media time.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task Player_PauseRetainsQueuedFramesAndOwnership()
    {
        var device = new Device(); var output = new Output(); var source = new Source();
        var bytes = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        source.Feed(bytes);
        var (player, _) = await Open(device, output, source);
        try
        {
            player.Pause();
            var silence = new byte[16];
            Assert.Equal(0, output.Consume(silence));
            Assert.Equal(new byte[16], silence);
            Assert.Equal(TimeSpan.Zero, player.Position);
            Assert.Equal(96000, device.SampleRate);
            player.Resume();
            Assert.Equal(16, output.Consume(silence));
            Assert.Equal(bytes, silence);
            Assert.Equal(TimeSpan.FromSeconds(2d / 96000), player.Position);
        }
        finally { player.Dispose(); }
        Assert.Equal(48000, device.SampleRate);
    }

    /// <summary>Seeking discards old queued audio, preserves pause and acknowledges the new decoder epoch.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task Player_SeekDiscardsOldPcmAndRetainsPause()
    {
        var first = new Source(); first.Feed(new byte[32]);
        var second = new Source();
        var output = new Output(); var device = new Device();
        TimeSpan? seekPosition = null;
        var calls = 0;
        var (player, _) = await CoreAudioAudioPlayer.PrepareAsync(device, Info, Ordinary, _ => output,
            (_, position, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1) return Task.FromResult<ICoreAudioPcmSource>(first);
                seekPosition = position;
                return Task.FromResult<ICoreAudioPcmSource>(second);
            }, false, null, 1, 1, 0, default);
        try
        {
            player.Pause();
            await player.SeekAsync(TimeSpan.FromSeconds(12)).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(TimeSpan.FromSeconds(12), seekPosition);
            Assert.Equal(TimeSpan.FromSeconds(12), player.Position);
            Assert.True(player.IsPaused);
            Assert.Equal(0, output.QueuedFrames);
            Assert.Equal(1, first.Disposals);
        }
        finally { player.Dispose(); }
        Assert.Equal(1, second.Disposals);
    }

    /// <summary>Preparation failure releases its rate/hog lease before permitting a retry.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task Player_RendererCreationFailureRestoresDevice()
    {
        var device = new Device();
        await Assert.ThrowsAsync<IOException>(() => CoreAudioAudioPlayer.PrepareAsync(device, Info,
            Ordinary with { Exclusive = true }, _ => throw new IOException(),
            (_, _, _) => throw new InvalidOperationException(), false, null, 1, 1, 0, default));
        Assert.Equal(48000, device.SampleRate);
        Assert.Equal(-1, device.HogOwner);
        await using var retry = await CoreAudioDeviceLease.OpenAsync(device, 48000, false);
    }

    /// <summary>Cancelled startup waits for a late decoder and complete output restoration.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task Player_CancelledPreparationDrainsLateDecoder()
    {
        var device = new Device(); var output = new Output(); var source = new Source();
        var entered = Signal();
        var held = new TaskCompletionSource<ICoreAudioPcmSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var preparing = CoreAudioAudioPlayer.PrepareAsync(device, Info, Ordinary, _ => output,
            (_, _, _) => { entered.TrySetResult(); return held.Task; }, false, null, 1, 1, 0, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        Assert.False(preparing.IsCompleted);
        held.SetResult(source);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparing.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(output.Disposed);
        Assert.Equal(1, source.Disposals);
        Assert.Equal(48000, device.SampleRate);
    }

    /// <summary>Loss of rate ownership cancels a held read and faults the audible lifetime with safe text.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task Player_ExternalRateChangeStopsOutputWithoutOverwritingIt()
    {
        var device = new Device(); var output = new Output(); var source = new Source(); source.Feed(new byte[8]);
        var (player, _) = await Open(device, output, source);
        device.SampleRate = 44100;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => player.WaitForCompletionAsync()
                .WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(output.Disposed);
            Assert.Equal(44100, device.SampleRate);
        }
        finally { try { player.Dispose(); } catch (InvalidOperationException) { } }
    }

    /// <summary>A successful keep-rate profile does not restore, but still releases hog ownership.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task Lease_KeepRateStillReleasesHog()
    {
        var device = new Device();
        var lease = await CoreAudioDeviceLease.OpenAsync(device, 96000, true, restoreSampleRate: false);
        await lease.DisposeAsync();
        Assert.Equal(96000, device.SampleRate);
        Assert.Equal(-1, device.HogOwner);
    }

    /// <summary>A later seek supersedes an ignored cancellation without leaking its decoder or acknowledging old work.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task Player_OverlappingSeeksRetireTheSupersededDecoder()
    {
        var first = new Source(); first.Feed(new byte[8]);
        var second = new Source(); var third = new Source();
        var entered = Signal();
        var held = new TaskCompletionSource<ICoreAudioPcmSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var (player, _) = await CoreAudioAudioPlayer.PrepareAsync(new Device(), Info, Ordinary, _ => new Output(),
            (_, _, _) => Interlocked.Increment(ref calls) switch
            {
                1 => Task.FromResult<ICoreAudioPcmSource>(first),
                2 => Hold(),
                _ => Task.FromResult<ICoreAudioPcmSource>(third)
            }, false, null, 1, 1, 0, default);
        Task<ICoreAudioPcmSource> Hold() { entered.TrySetResult(); return held.Task; }
        try
        {
            var oldSeek = player.SeekAsync(TimeSpan.FromSeconds(10));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var newSeek = player.SeekAsync(TimeSpan.FromSeconds(20));
            held.SetResult(second);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldSeek.WaitAsync(TimeSpan.FromSeconds(10)));
            await newSeek.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(TimeSpan.FromSeconds(20), player.Position);
            Assert.Equal(1, first.Disposals);
            Assert.Equal(1, second.Disposals);
        }
        finally { held.TrySetResult(second); player.Dispose(); }
        Assert.Equal(1, third.Disposals);
    }

    /// <summary>Audible completion includes native disposal and confirmed restoration before UID admission is released.</summary>
    /// <returns>The asynchronous regression test.</returns>
    [Fact]
    public async Task Player_CompletionAwaitsNativeRetirementAndRateRestoration()
    {
        var device = new Device(); var output = new Output(); var source = new Source();
        var entered = Signal(); var release = Signal();
        device.Wait = (rate, _) => rate == 48000 ? Restore() : Task.CompletedTask;
        Task Restore() { entered.TrySetResult(); return release.Task; }
        source.Feed(new byte[8]); source.Finish();
        var (player, _) = await Open(device, output, source, Ordinary with { Exclusive = true });
        try
        {
            await source.Eof.Task.WaitAsync(TimeSpan.FromSeconds(10));
            output.Consume(new byte[8]);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(output.Disposed);
            Assert.False(player.WaitForCompletionAsync().IsCompleted);
            Assert.Equal(Environment.ProcessId, device.HogOwner);
            await Assert.ThrowsAsync<InvalidOperationException>(() => CoreAudioDeviceLease.OpenAsync(device, 48000, false));
            release.SetResult();
            await player.WaitForCompletionAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(-1, device.HogOwner);
        }
        finally { release.TrySetResult(); player.Dispose(); }
    }

    /// <summary>Native profiles survive normalization and Settings cloning without fallback or option loss.</summary>
    [Fact]
    public void ProfileNormalizationAndCloning_PreserveNativeOptions()
    {
        var profile = new OutputProfile { Name = "Synthetic native", Backend = OutputBackend.CoreAudio,
            SelectedWasapiDeviceId = "synthetic-uid", CoreAudioExclusive = true, CoreAudioMatchRate = false,
            CoreAudioRestoreRate = false, CoreAudioBypass = false };
        var settings = new AppSettings { OutputProfiles = [profile], SelectedOutputProfileName = profile.Name };
        typeof(SettingsStore).GetMethod("NormalizeOutputProfiles", System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static)!.Invoke(null, [settings]);
        Assert.Equal(OutputBackend.CoreAudio, settings.OutputBackend);
        Assert.Equal(profile.SelectedWasapiDeviceId, settings.SelectedWasapiDeviceId);
        var clone = (OutputProfile)typeof(SettingsView).GetMethod("CloneOutputProfile",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [profile])!;
        Assert.NotSame(profile, clone);
        Assert.Equal(CoreAudioOutputOptions.FromProfile(profile), CoreAudioOutputOptions.FromProfile(clone));
        Assert.Single(settings.OutputProfiles);
    }

    private static Task<(CoreAudioAudioPlayer AudioPlayer, AudioFileInfo Info)> Open(Device device,
        Output output, Source source, CoreAudioOutputOptions? options = null, float volume = 1, float gain = 1) =>
        CoreAudioAudioPlayer.PrepareAsync(device, Info, options ?? Ordinary, _ => output,
            (_, _, _) => Task.FromResult<ICoreAudioPcmSource>(source), false, null, volume, gain, 0, default);

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Synthetic rate/hog control with no native handles.</summary>
    private sealed class Device : ICoreAudioDeviceControl
    {
        /// <inheritdoc/>
        public string Uid { get; } = Guid.NewGuid().ToString("N");
        /// <inheritdoc/>
        public bool IsAvailable => true;
        /// <inheritdoc/>
        public double SampleRate { get; set; } = 48000;
        /// <inheritdoc/>
        public int HogOwner { get; set; } = -1;
        /// <summary>Gets or sets synthetic asynchronous property confirmation.</summary>
        internal Func<double, CancellationToken, Task>? Wait { get; set; }
        /// <inheritdoc/>
        public bool SupportsRate(double rate) => rate is >= 8000 and <= 768000;
        /// <inheritdoc/>
        public void SetSampleRate(double rate) => SampleRate = rate;
        /// <inheritdoc/>
        public void SetHogOwner(int pid) => HogOwner = pid;
        /// <inheritdoc/>
        public Task WaitForSampleRateAsync(double rate, CancellationToken cancellationToken) =>
            Wait?.Invoke(rate, cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>Renderer adapter consuming the production ring only when the test requests a callback.</summary>
    private sealed class Output : ICoreAudioPcmOutput
    {
        private readonly CoreAudioPcmRing _ring = new();
        /// <inheritdoc/>
        public long RenderedFrames => _ring.RenderedFrames;
        /// <inheritdoc/>
        public long QueuedFrames => _ring.QueuedFrames;
        /// <inheritdoc/>
        public TimeSpan DrainTail => TimeSpan.Zero;
        /// <inheritdoc/>
        public bool IsQuiescent => Disposed;
        /// <inheritdoc/>
        public bool Paused { get; set; }
        /// <summary>Gets whether native retirement was requested.</summary>
        internal bool Disposed { get; private set; }
        /// <inheritdoc/>
        public void Start() { }
        /// <inheritdoc/>
        public void Reset() => _ring.Reset();
        /// <inheritdoc/>
        public int Write(ReadOnlySpan<byte> pcm) => _ring.Write(pcm);
        /// <inheritdoc/>
        public void CheckHealth() { }
        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
        /// <summary>Runs a synthetic PCM consumption callback.</summary>
        /// <param name="destination">Output callback bytes.</param>
        /// <returns>The media bytes consumed.</returns>
        internal int Consume(Span<byte> destination)
        {
            if (!Paused) return _ring.Read(destination);
            destination.Clear(); return 0;
        }
    }

    /// <summary>Channel-backed decoder with arbitrary byte fragmentation and cancellable held reads.</summary>
    /// <param name="fragment">Maximum bytes returned by one read.</param>
    private sealed class Source(int fragment = 65536) : ICoreAudioPcmSource
    {
        private readonly Channel<byte[]> _input = Channel.CreateUnbounded<byte[]>();
        private byte[]? _current;
        private int _offset;
        /// <summary>Gets EOF observation independently of native media completion.</summary>
        internal TaskCompletionSource Eof { get; } = Signal();
        /// <summary>Gets the number of decoder retirements.</summary>
        internal int Disposals { get; private set; }
        /// <summary>Supplies synthetic PCM input.</summary>
        /// <param name="bytes">Input packet.</param>
        internal void Feed(byte[] bytes) => _input.Writer.TryWrite(bytes);
        /// <summary>Ends input after all queued packets.</summary>
        internal void Finish() => _input.Writer.TryComplete();
        /// <inheritdoc/>
        public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken token)
        {
            if (_current is null || _offset == _current.Length)
            {
                if (!await _input.Reader.WaitToReadAsync(token)) { Eof.TrySetResult(); return 0; }
                _current = await _input.Reader.ReadAsync(token); _offset = 0;
            }
            var count = Math.Min(fragment, Math.Min(destination.Length, _current.Length - _offset));
            _current.AsMemory(_offset, count).CopyTo(destination); _offset += count;
            return count;
        }
        /// <inheritdoc/>
        public void Dispose() => Disposals++;
    }
}
