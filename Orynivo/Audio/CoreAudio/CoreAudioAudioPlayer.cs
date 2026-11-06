using System.Runtime.InteropServices;
using Orynivo.Localization;
using Orynivo.Visualization;

namespace Orynivo.Audio.CoreAudio;

/// <summary>Owned PCM decoder used by native output and isolated workflow tests.</summary>
internal interface ICoreAudioPcmSource : IDisposable
{
    /// <summary>Reads incremental raw stereo S32 bytes.</summary>
    /// <param name="destination">Borrowed worker buffer.</param>
    /// <param name="token">Decoder epoch cancellation.</param>
    /// <returns>The byte count, or zero at EOF.</returns>
    ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken token);
}

/// <summary>
/// Single-track macOS PCM player using AUHAL, an exact-rate device lease, and the
/// existing segment-aware FFmpeg decoder. Ordinary queue advancement creates the
/// next session, so mixed-rate tracks never inherit an earlier track's rate.
/// </summary>
internal sealed class CoreAudioAudioPlayer : IAudioPlayer, IEqualizerAudioPlayer
{
    private readonly CoreAudioDeviceLease _lease;
    private readonly ICoreAudioDeviceControl _device;
    private readonly ICoreAudioPcmOutput _output;
    private readonly CoreAudioOutputOptions _options;
    private readonly AudioFileInfo _info;
    private readonly ParametricEqualizer _equalizer;
    private readonly Func<int, TimeSpan, CancellationToken, Task<ICoreAudioPcmSource>> _createSource;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _controlLock = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _monitor;
    private readonly Task _pump;
    private CancellationTokenSource? _decodeCancellation;
    private SeekRequest? _seek;
    private EqualizerRequest? _equalizerRequest;
    private bool _equalizerEnabled;
    private volatile float _volume;
    private volatile float _gain;
    private long _positionBase;
    private int _fatal;
    private int _disposed;
    private volatile bool _cleanupFailed;

    private CoreAudioAudioPlayer(CoreAudioDeviceLease lease, ICoreAudioDeviceControl device,
        ICoreAudioPcmOutput output, CoreAudioOutputOptions options, AudioFileInfo info,
        Func<int, TimeSpan, CancellationToken, Task<ICoreAudioPcmSource>> createSource,
        bool equalizerEnabled, EqualizerProfile? profile, float volume, float gain)
    {
        _lease = lease;
        _device = device;
        _output = output;
        _options = options;
        _info = info;
        _createSource = createSource;
        _equalizerEnabled = equalizerEnabled;
        _equalizer = new ParametricEqualizer(info.OutputSampleRate, equalizerEnabled, profile);
        Volume = volume;
        ReplayGainFactor = gain;
        _monitor = Task.Run(MonitorAsync);
        _pump = Task.Run(PumpAsync);
    }

    /// <inheritdoc/>
    public TimeSpan Duration => _info.Duration;
    /// <inheritdoc/>
    public TimeSpan Position
    {
        get
        {
            var position = TimeSpan.FromSeconds(Math.Max(0,
                Interlocked.Read(ref _positionBase) + _output.RenderedFrames) / (double)_info.OutputSampleRate);
            return Duration > TimeSpan.Zero && position > Duration ? Duration : position;
        }
    }
    /// <inheritdoc/>
    public bool IsPaused => _output.Paused;
    /// <inheritdoc/>
    public bool CanSeek => Duration > TimeSpan.Zero;
    /// <summary>Gets whether the profile bypasses software gain and DSP.</summary>
    internal bool BypassesProcessing => _options.Bypass;
    /// <inheritdoc/>
    public float Volume
    {
        get => _options.Bypass ? 1 : _volume;
        set => _volume = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
    }
    /// <inheritdoc/>
    public float ReplayGainFactor
    {
        get => _options.Bypass ? 1 : _gain;
        set => _gain = float.IsFinite(value) ? Math.Clamp(value, 0, 64) : 1;
    }

    /// <summary>Prepares native PCM on a worker and exposes only sanitized failures.</summary>
    /// <param name="item">Desktop-resolved logical source.</param>
    /// <param name="uid">Selected stable native device UID.</param>
    /// <param name="options">Immutable selected profile options.</param>
    /// <param name="equalizerEnabled">Whether ordinary PCM EQ is active.</param>
    /// <param name="profile">Captured EQ configuration.</param>
    /// <param name="volume">Initial software volume, before the first native buffer.</param>
    /// <param name="maximumRate">Strict PCM rate ceiling or zero.</param>
    /// <param name="token">Owning preparation cancellation.</param>
    /// <returns>A ready player and the actual client PCM rate.</returns>
    internal static async Task<(CoreAudioAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        GaplessPlaybackItem item, string uid, CoreAudioOutputOptions options, bool equalizerEnabled,
        EqualizerProfile? profile, float volume, int maximumRate, CancellationToken token)
    {
        try
        {
            return await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                var device = CoreAudioNativeDevice.Resolve(uid);
                var info = item.TryCreateKnownAudioInfo() ??
                    await BrowserAudioTranscoder.ProbeAsync(item, token).ConfigureAwait(false);
                info = info with
                {
                    Duration = item.SegmentDuration ?? item.KnownDuration ?? info.Duration,
                    ContainerName = item.KnownInfo?.ContainerName ?? info.CodecName
                };
                return await PrepareAsync(device, info, options,
                    rate => CoreAudioHalOutput.Create(device, rate),
                    async (rate, position, ct) => new DecoderSource(await FfmpegPcmDecoder.CreateAsync(
                        item.PlaybackPaths, rate, "s32le", "pcm_s32le", position,
                        item.SegmentStart, item.SegmentEnd, ct).ConfigureAwait(false)),
                    equalizerEnabled, profile, volume, item.ReplayGainFactor, maximumRate, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { throw Failure(); }
    }

    /// <summary>Composes the production lease/pump with injectable renderer and decoder boundaries.</summary>
    /// <param name="device">UID-bound native device or isolated adapter.</param>
    /// <param name="info">Original source metadata.</param>
    /// <param name="options">Immutable output settings.</param>
    /// <param name="createOutput">Creates initialized native output after the lease is acquired.</param>
    /// <param name="createSource">Creates a segment-aware decoder for a seek epoch.</param>
    /// <param name="equalizerEnabled">Whether ordinary EQ is enabled.</param>
    /// <param name="profile">Captured equalizer profile.</param>
    /// <param name="volume">Initial software volume.</param>
    /// <param name="gain">Initial ReplayGain and PCM boost factor.</param>
    /// <param name="maximumRate">Strict positive PCM ceiling, or zero.</param>
    /// <param name="token">Owning preparation cancellation.</param>
    /// <returns>A ready output and delivered PCM information.</returns>
    internal static async Task<(CoreAudioAudioPlayer AudioPlayer, AudioFileInfo Info)> PrepareAsync(
        ICoreAudioDeviceControl device, AudioFileInfo info, CoreAudioOutputOptions options,
        Func<int, ICoreAudioPcmOutput> createOutput,
        Func<int, TimeSpan, CancellationToken, Task<ICoreAudioPcmSource>> createSource,
        bool equalizerEnabled, EqualizerProfile? profile, float volume, float gain,
        int maximumRate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var rate = options.SelectRate(info, device.SampleRate, maximumRate);
        var lease = await CoreAudioDeviceLease.OpenAsync(device, rate, options.Exclusive, token,
            options.RestoreRate).ConfigureAwait(false);
        ICoreAudioPcmOutput? output = null;
        CoreAudioAudioPlayer? player = null;
        try
        {
            token.ThrowIfCancellationRequested();
            output = createOutput(rate);
            token.ThrowIfCancellationRequested();
            info = info with { OutputSampleRate = rate };
            player = new CoreAudioAudioPlayer(lease, device, output, options, info, createSource,
                equalizerEnabled && !options.Bypass, profile, volume, gain);
            await player._started.Task.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return (player, info);
        }
        catch
        {
            if (player is not null)
            {
                player._cts.Cancel();
                try { await player._pump.ConfigureAwait(false); } catch { }
                player._cts.Dispose();
                if (player._cleanupFailed) throw Failure();
            }
            else
            {
                try { output?.Dispose(); }
                finally
                {
                    if (output is null || output.IsQuiescent)
                        await lease.DisposeAsync().ConfigureAwait(false);
                }
            }
            throw;
        }
    }

    /// <inheritdoc/>
    public void Pause() => _output.Paused = true;
    /// <inheritdoc/>
    public void Resume() => _output.Paused = false;
    /// <inheritdoc/>
    public void UpdateEqualizer(bool enabled, EqualizerProfile? profile) =>
        Interlocked.Exchange(ref _equalizerRequest, new EqualizerRequest(enabled, profile?.Clone()));

    /// <inheritdoc/>
    /// <returns>Completion after the owning seek epoch is prepared, or cancellation on supersession.</returns>
    public Task SeekAsync(TimeSpan position)
    {
        if (!CanSeek || Volatile.Read(ref _disposed) != 0 || _pump.IsCompleted) return Task.CompletedTask;
        position = position < TimeSpan.Zero ? TimeSpan.Zero : position > Duration ? Duration : position;
        var seek = new SeekRequest(position);
        lock (_controlLock)
        {
            if (_cts.IsCancellationRequested) return Task.CompletedTask;
            _seek?.Completion.TrySetCanceled();
            _seek = seek;
            _decodeCancellation?.Cancel();
        }
        return seek.Completion.Task;
    }

    /// <inheritdoc/>
    /// <returns>The media lifetime, including native retirement and rate restoration.</returns>
    public Task WaitForCompletionAsync() => _pump;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        try { _pump.GetAwaiter().GetResult(); }
        finally { _cts.Dispose(); }
    }

    private async Task MonitorAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(200, _cts.Token).ConfigureAwait(false);
                if (!_device.IsAvailable || _device.SampleRate != _info.OutputSampleRate ||
                    (_options.Exclusive && _device.HogOwner != Environment.ProcessId))
                    throw new IOException();
                _output.CheckHealth();
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch
        {
            Volatile.Write(ref _fatal, 1);
            _cts.Cancel();
        }
    }

    private async Task PumpAsync()
    {
        ICoreAudioPcmSource? source = null;
        SeekRequest? activeSeek = null;
        var buffer = new byte[65536];
        var retained = 0;
        var eof = false;
        long? drainStarted = null;
        try
        {
            source = await NewSourceAsync(TimeSpan.Zero).ConfigureAwait(false);
            while (!_cts.IsCancellationRequested)
            {
                lock (_controlLock) { activeSeek = _seek; _seek = null; }
                if (activeSeek is not null)
                {
                    _output.Reset();
                    source?.Dispose();
                    source = null;
                    try { source = await NewSourceAsync(activeSeek.Position).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!_cts.IsCancellationRequested && HasPendingSeek())
                    {
                        activeSeek.Completion.TrySetCanceled();
                        activeSeek = null;
                        continue;
                    }
                    if (HasPendingSeek())
                    {
                        activeSeek.Completion.TrySetCanceled();
                        activeSeek = null;
                        continue;
                    }
                    Interlocked.Exchange(ref _positionBase, (long)(activeSeek.Position.TotalSeconds * _info.OutputSampleRate));
                    _equalizer.Reset();
                    retained = 0;
                    eof = false;
                    drainStarted = null;
                    activeSeek.Completion.TrySetResult();
                    activeSeek = null;
                }
                if (_output.Paused)
                {
                    drainStarted = null;
                    await Task.Delay(5, _cts.Token).ConfigureAwait(false);
                    continue;
                }
                if (eof)
                {
                    if (_output.QueuedFrames == 0)
                    {
                        drainStarted ??= Environment.TickCount64;
                        if (Environment.TickCount64 - drainStarted >= _output.DrainTail.TotalMilliseconds) break;
                    }
                    await Task.Delay(5, _cts.Token).ConfigureAwait(false);
                    continue;
                }
                int read;
                try
                {
                    read = await source!.ReadAsync(buffer.AsMemory(retained), _decodeCancellation!.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!_cts.IsCancellationRequested && HasPendingSeek()) { continue; }
                if (HasPendingSeek()) continue;
                if (read == 0)
                {
                    if (retained != 0) throw new InvalidDataException();
                    eof = true;
                    _started.TrySetResult();
                    continue;
                }
                var total = retained + read;
                var aligned = total - total % 8;
                ProcessPcm(buffer.AsSpan(0, aligned));
                var offset = 0;
                while (offset < aligned && !HasPendingSeek())
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    offset += _output.Write(buffer.AsSpan(offset, aligned - offset));
                    _output.Start();
                    _started.TrySetResult();
                    if (offset < aligned) await Task.Delay(5, _cts.Token).ConfigureAwait(false);
                }
                retained = total - aligned;
                buffer.AsSpan(aligned, retained).CopyTo(buffer);
            }
            if (Volatile.Read(ref _fatal) != 0) throw Failure();
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested && Volatile.Read(ref _fatal) == 0)
        {
            _started.TrySetCanceled();
        }
        catch
        {
            _started.TrySetException(Failure());
            throw Failure();
        }
        finally
        {
            try
            {
                _cts.Cancel();
                await _monitor.ConfigureAwait(false);
                activeSeek?.Completion.TrySetCanceled();
                lock (_controlLock)
                {
                    _seek?.Completion.TrySetCanceled();
                    _seek = null;
                    _decodeCancellation?.Dispose();
                    _decodeCancellation = null;
                }
                try { source?.Dispose(); }
                finally
                {
                    try { _output.Dispose(); }
                    finally
                    {
                        if (_output.IsQuiescent) await _lease.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            catch { _cleanupFailed = true; _started.TrySetException(Failure()); throw Failure(); }
        }
    }

    private async Task<ICoreAudioPcmSource> NewSourceAsync(TimeSpan position)
    {
        CancellationToken token;
        lock (_controlLock)
        {
            _decodeCancellation?.Dispose();
            _decodeCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            token = _decodeCancellation.Token;
        }
        return await _createSource(_info.OutputSampleRate, position, token).ConfigureAwait(false);
    }

    private bool HasPendingSeek()
    {
        lock (_controlLock) return _seek is not null;
    }

    private void ProcessPcm(Span<byte> pcm)
    {
        VisualizerAudioHub.Shared.PushPcm(pcm, "s32le", _info.OutputSampleRate);
        if (_options.Bypass) return;
        var update = Interlocked.Exchange(ref _equalizerRequest, null);
        if (update is not null)
        {
            _equalizerEnabled = update.Enabled;
            _equalizer.Update(update.Enabled, update.Profile);
        }
        var gain = _volume * _gain;
        if (!_equalizerEnabled && gain == 1) return; // Preserve integer bits at unity.
        var samples = MemoryMarshal.Cast<byte, int>(pcm);
        for (var index = 0; index + 1 < samples.Length; index += 2)
        {
            var result = _equalizer.Process(samples[index] / 2147483648f * gain,
                samples[index + 1] / 2147483648f * gain);
            samples[index] = ToInt32(result.Left);
            samples[index + 1] = ToInt32(result.Right);
        }
    }

    private static int ToInt32(float value) => value <= -1 ? int.MinValue : value >= 1
        ? int.MaxValue : (int)Math.Round(value * 2147483647d);

    private static InvalidOperationException Failure() => new(LocalizationManager.Current.CoreAudioFailed);

    /// <summary>Worker-owned FFmpeg adapter.</summary>
    /// <param name="decoder">The owned segment-aware decoder.</param>
    private sealed class DecoderSource(FfmpegPcmDecoder decoder) : ICoreAudioPcmSource
    {
        /// <inheritdoc/>
        public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken token) => decoder.ReadAsync(destination, token);
        /// <inheritdoc/>
        public void Dispose() => decoder.Dispose();
    }
    /// <summary>One pending seek acknowledgement.</summary>
    /// <param name="Position">Logical track offset.</param>
    private sealed record SeekRequest(TimeSpan Position)
    {
        /// <summary>Gets completion after old PCM is discarded and the new decoder is ready.</summary>
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    /// <summary>A worker-applied equalizer update.</summary>
    /// <param name="Enabled">Whether EQ is enabled.</param>
    /// <param name="Profile">Captured equalizer profile.</param>
    private sealed record EqualizerRequest(bool Enabled, EqualizerProfile? Profile);
}
