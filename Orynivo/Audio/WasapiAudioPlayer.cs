using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Orynivo.Audio;

/// <summary>
/// Plays one or more files as continuous PCM through one exclusive WASAPI
/// session. Upcoming FFmpeg decoders are prefetched before track boundaries.
/// </summary>
public sealed class WasapiAudioPlayer : IGaplessAudioPlayer, IEqualizerAudioPlayer
{
    private const int MaximumPrematurePlexEofRetries = 3;
    private static readonly TimeSpan PrematurePlexEofTolerance = TimeSpan.FromSeconds(5);
    private readonly IReadOnlyList<GaplessPlaybackItem> _items;
    private readonly AudioFileInfo?[] _infos;
    private readonly long[] _trackStartFrames;
    private readonly long[] _trackPositionOffsetFrames;
    private readonly int[] _prematurePlexEofRetryCounts;
    private readonly WasapiOut _output;
    private readonly BufferedWaveProvider _bufferedProvider;
    private readonly PausableWaveProvider _playbackProvider;
    private readonly MMDevice _device;
    private readonly WasapiSelectedFormat _selectedFormat;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _decoderGate = new(1, 1);
    private readonly Task _pumpTask;
    private readonly object _stateLock = new();
    private readonly ParametricEqualizer _equalizer;
    private long _totalFramesWritten;
    private int _audibleTrackIndex;
    private int _writeTrackIndex;
    private FfmpegPcmDecoder? _activeDecoder;
    private int _restartPreparedFromIndex = -1;
    private int _decoderGeneration;
    private int _decoderReadPaused;
    private int _seekRequestGeneration;
    private CancellationTokenSource? _seekStartupCts;
    private EqualizerUpdateRequest? _pendingEqualizerUpdate;
    private int _equalizerResetRequested;
    private float _volume = 1.0f;
    private bool _disposed;

    private WasapiAudioPlayer(
        IReadOnlyList<GaplessPlaybackItem> items,
        AudioFileInfo firstInfo,
        FfmpegPcmDecoder firstDecoder,
        WasapiOut output,
        BufferedWaveProvider bufferedProvider,
        PausableWaveProvider playbackProvider,
        MMDevice device,
        WasapiSelectedFormat selectedFormat,
        bool equalizerEnabled,
        EqualizerProfile? equalizerProfile)
    {
        _items = items;
        _infos = new AudioFileInfo?[items.Count];
        _infos[0] = firstInfo;
        _trackStartFrames = new long[items.Count];
        _trackPositionOffsetFrames = new long[items.Count];
        _prematurePlexEofRetryCounts = new int[items.Count];
        _output = output;
        _bufferedProvider = bufferedProvider;
        _playbackProvider = playbackProvider;
        _device = device;
        _selectedFormat = selectedFormat;
        _equalizer = new ParametricEqualizer(
            selectedFormat.Format.SampleRate,
            equalizerEnabled,
            equalizerProfile);
        _activeDecoder = firstDecoder;
        _pumpTask = Task.Run(() => PumpAsync(firstDecoder));
    }

    /// <inheritdoc/>
    public event EventHandler<GaplessTrackChangedEventArgs>? TrackChanged;
    /// <inheritdoc/>
    public string CurrentFilePath => _items[Volatile.Read(ref _audibleTrackIndex)].FilePath;
    /// <inheritdoc/>
    public AudioFileInfo CurrentInfo =>
        _infos[Volatile.Read(ref _audibleTrackIndex)]
        ?? throw new InvalidOperationException("Current track information is unavailable.");
    /// <inheritdoc/>
    public TimeSpan Duration => CurrentInfo.Duration;
    /// <inheritdoc/>
    public TimeSpan Position
    {
        get
        {
            var index = Volatile.Read(ref _audibleTrackIndex);
            var bufferedFrames = _bufferedProvider.BufferedBytes / _selectedFormat.BytesPerFrame;
            var playedFrames = Math.Max(0, Interlocked.Read(ref _totalFramesWritten) - bufferedFrames);
            var positionFrames = Math.Max(0, playedFrames - Volatile.Read(ref _trackStartFrames[index]));
            return TimeSpan.FromSeconds(
                Math.Min(
                    (positionFrames + Volatile.Read(ref _trackPositionOffsetFrames[index])) /
                    (double)CurrentInfo.OutputSampleRate,
                    Duration.TotalSeconds));
        }
    }
    /// <inheritdoc/>
    public bool IsPaused => _playbackProvider.IsPaused;
    /// <inheritdoc/>
    public bool CanSeek => Duration > TimeSpan.Zero;
    /// <inheritdoc/>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0f, 1.0f);
    }
    /// <inheritdoc/>
    public float ReplayGainFactor { get; set; } = 1.0f;

    /// <summary>Creates a continuous exclusive-mode WASAPI playback session.</summary>
    /// <param name="items">Tracks in playback order with their ReplayGain factors.</param>
    /// <param name="deviceId">MMDevice identifier of the output device.</param>
    /// <param name="equalizerEnabled">Whether the supplied equalizer profile is active.</param>
    /// <param name="equalizerProfile">Equalizer profile applied to PCM samples.</param>
    /// <param name="cancellationToken">Cancellation token for initial probing and decoder startup.</param>
    /// <returns>The player and technical information for the first track.</returns>
    public static async Task<(WasapiAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        IReadOnlyList<GaplessPlaybackItem> items,
        string deviceId,
        bool equalizerEnabled = false,
        EqualizerProfile? equalizerProfile = null,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
            throw new ArgumentException("At least one playback item is required.", nameof(items));

        var info = await ProbeAsync(items[0].PlaybackPath, cancellationToken);
        if (items[0].SegmentDuration is { } firstDuration)
            info = info with { Duration = firstDuration };
        else if (items[0].KnownDuration is { } knownDuration)
            info = info with { Duration = knownDuration };
        var device = WasapiDeviceProvider.GetRenderDevice(deviceId);
        try
        {
            var selectedFormat = ChooseExclusiveFormat(device, info);
            info = info with { OutputSampleRate = selectedFormat.Format.SampleRate };
            var provider = new BufferedWaveProvider(selectedFormat.Format)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = false
            };
            var playbackProvider = new PausableWaveProvider(provider);
            var output = new WasapiOut(device, AudioClientShareMode.Exclusive, useEventSync: true, latency: 100);
            output.Init(playbackProvider);
            var decoder = await FfmpegPcmDecoder.CreateAsync(
                items[0].PlaybackPaths,
                info.OutputSampleRate,
                selectedFormat.FfmpegSampleFormat,
                selectedFormat.FfmpegCodec,
                TimeSpan.Zero,
                items[0].SegmentStart,
                items[0].SegmentEnd,
                cancellationToken);
            output.Play();
            return (new WasapiAudioPlayer(
                items,
                info,
                decoder,
                output,
                provider,
                playbackProvider,
                device,
                selectedFormat,
                equalizerEnabled,
                equalizerProfile), info);
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    /// <summary>Creates a single-track exclusive-mode WASAPI playback session.</summary>
    /// <param name="filePath">Local path or supported stream URL.</param>
    /// <param name="deviceId">MMDevice identifier of the output device.</param>
    /// <param name="equalizerEnabled">Whether the supplied equalizer profile is active.</param>
    /// <param name="equalizerProfile">Equalizer profile applied to PCM samples.</param>
    /// <param name="cancellationToken">Cancellation token for initial probing and decoder startup.</param>
    /// <returns>The player and technical information for the track.</returns>
    public static Task<(WasapiAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string filePath,
        string deviceId,
        bool equalizerEnabled = false,
        EqualizerProfile? equalizerProfile = null,
        CancellationToken cancellationToken = default) =>
        CreateAsync(
            [new GaplessPlaybackItem(filePath, 1.0f)],
            deviceId,
            equalizerEnabled,
            equalizerProfile,
            cancellationToken);

    /// <inheritdoc/>
    public void UpdateEqualizer(bool enabled, EqualizerProfile? profile)
        => Interlocked.Exchange(
            ref _pendingEqualizerUpdate,
            new EqualizerUpdateRequest(enabled, profile?.Clone()));

    /// <inheritdoc/>
    public void Pause() => _playbackProvider.IsPaused = true;
    /// <inheritdoc/>
    public void Resume() => _playbackProvider.IsPaused = false;
    /// <inheritdoc/>
    public async Task SeekAsync(TimeSpan position)
    {
        position = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > Duration ? Duration : position;
        var audibleIndex = Volatile.Read(ref _audibleTrackIndex);
        var seekRequest = Interlocked.Increment(ref _seekRequestGeneration);
        var stopwatch = Stopwatch.StartNew();
        SeekDiagnostics.Log(
            "wasapi-player",
            $"seek-begin request={seekRequest} index={audibleIndex} target={position.TotalSeconds:F3}s input={SeekDiagnostics.SanitizeUrl(_items[audibleIndex].PlaybackPaths.FirstOrDefault() ?? string.Empty)}");
        var installedReplacement = false;
        CancellationTokenSource? seekStartupCts = null;
        await _decoderGate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            _seekStartupCts?.Cancel();
            _seekStartupCts = null;
            seekStartupCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _seekStartupCts = seekStartupCts;
            Interlocked.Exchange(ref _decoderReadPaused, 1);
            _activeDecoder?.Dispose();
            _activeDecoder = null;
            Interlocked.Increment(ref _decoderGeneration);
            _writeTrackIndex = audibleIndex;
            _prematurePlexEofRetryCounts[audibleIndex] = 0;
            Volatile.Write(ref _trackStartFrames[audibleIndex], 0);
            Volatile.Write(ref _restartPreparedFromIndex, audibleIndex);
            _bufferedProvider.ClearBuffer();
            Interlocked.Exchange(ref _equalizerResetRequested, 1);
            Interlocked.Exchange(ref _totalFramesWritten, 0);
            Volatile.Write(
                ref _trackPositionOffsetFrames[audibleIndex],
                (long)(position.TotalSeconds * CurrentInfo.OutputSampleRate));
            SeekDiagnostics.Log(
                "wasapi-player",
                $"old-decoder-discarded request={seekRequest} index={audibleIndex} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
        finally
        {
            _decoderGate.Release();
        }

        FfmpegPcmDecoder? replacement = null;
        try
        {
            replacement = await FfmpegPcmDecoder.CreateAsync(
                _items[audibleIndex].PlaybackPaths,
                CurrentInfo.OutputSampleRate,
                _selectedFormat.FfmpegSampleFormat,
                _selectedFormat.FfmpegCodec,
                position,
                _items[audibleIndex].SegmentStart,
                _items[audibleIndex].SegmentEnd,
                seekStartupCts.Token).ConfigureAwait(false);
            await _decoderGate.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                if (seekRequest == Volatile.Read(ref _seekRequestGeneration) &&
                    !seekStartupCts.IsCancellationRequested)
                {
                    _activeDecoder = replacement;
                    Interlocked.Increment(ref _decoderGeneration);
                    installedReplacement = true;
                    SeekDiagnostics.Log(
                        "wasapi-player",
                        $"replacement-installed request={seekRequest} index={audibleIndex} elapsedMs={stopwatch.ElapsedMilliseconds}");
                }
                else
                {
                    SeekDiagnostics.Log(
                        "wasapi-player",
                        $"replacement-stale request={seekRequest} index={audibleIndex} elapsedMs={stopwatch.ElapsedMilliseconds}");
                }
            }
            finally
            {
                if (seekRequest == Volatile.Read(ref _seekRequestGeneration))
                {
                    _seekStartupCts = null;
                    Interlocked.Exchange(ref _decoderReadPaused, 0);
                }

                _decoderGate.Release();
            }
        }
        finally
        {
            if (!installedReplacement)
            {
                replacement?.Dispose();
                if (seekRequest == Volatile.Read(ref _seekRequestGeneration))
                    Interlocked.Exchange(ref _decoderReadPaused, 0);
                SeekDiagnostics.Log(
                    "wasapi-player",
                    $"seek-aborted request={seekRequest} index={audibleIndex} elapsedMs={stopwatch.ElapsedMilliseconds}");
            }

            seekStartupCts?.Dispose();
        }
    }
    /// <inheritdoc/>
    public async Task WaitForCompletionAsync() => await _pumpTask.ConfigureAwait(false);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _cts.Cancel();
        try { _pumpTask.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        _output.Stop();
        _output.Dispose();
        _device.Dispose();
        _cts.Dispose();
        _decoderGate.Dispose();
        _disposed = true;
    }

    private async Task PumpAsync(FfmpegPcmDecoder firstDecoder)
    {
        var buffer = new byte[64 * 1024];
        var remainderBytes = 0;
        Task<(FfmpegPcmDecoder Decoder, AudioFileInfo Info)>? preparedNext = PrepareTrackAsync(1);

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var restartIndex = Interlocked.Exchange(ref _restartPreparedFromIndex, -1);
                if (restartIndex >= 0)
                {
                    if (preparedNext is not null)
                        _ = DisposePreparedAsync(preparedNext);
                    preparedNext = PrepareTrackAsync(restartIndex + 1);
                    remainderBytes = 0;
                }

                await _decoderGate.WaitAsync(_cts.Token).ConfigureAwait(false);
                int bytesRead;
                var decoderGeneration = Volatile.Read(ref _decoderGeneration);
                try
                {
                    if (Volatile.Read(ref _decoderReadPaused) != 0 ||
                        _activeDecoder is null)
                    {
                        bytesRead = -1;
                    }
                    else
                    {
                        bytesRead = await _activeDecoder.ReadAsync(
                        buffer.AsMemory(remainderBytes),
                        _cts.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _decoderGate.Release();
                }
                if (bytesRead < 0)
                {
                    await Task.Delay(2, _cts.Token).ConfigureAwait(false);
                    continue;
                }
                if (decoderGeneration != Volatile.Read(ref _decoderGeneration))
                {
                    remainderBytes = 0;
                    continue;
                }
                if (bytesRead == 0)
                {
                    remainderBytes = 0;
                    if (await TryResumePrematurePlexEofAsync(decoderGeneration).ConfigureAwait(false))
                        continue;

                    _activeDecoder?.Dispose();
                    var nextIndex = _writeTrackIndex + 1;
                    if (nextIndex >= _items.Count || preparedNext is null)
                        break;

                    var prepared = await preparedNext.ConfigureAwait(false);
                    _writeTrackIndex = nextIndex;
                    Volatile.Write(ref _trackStartFrames[nextIndex], Interlocked.Read(ref _totalFramesWritten));
                    _infos[nextIndex] = prepared.Info;
                    _activeDecoder = prepared.Decoder;
                    preparedNext = PrepareTrackAsync(nextIndex + 1);
                    continue;
                }

                var totalBytes = remainderBytes + bytesRead;
                bytesRead = totalBytes - (totalBytes % _selectedFormat.BytesPerFrame);
                remainderBytes = totalBytes - bytesRead;
                ProcessPcm(buffer.AsSpan(0, bytesRead), _items[_writeTrackIndex].ReplayGainFactor);
                var offset = 0;
                while (offset < bytesRead && !_cts.IsCancellationRequested)
                {
                    var writable = _bufferedProvider.BufferLength - _bufferedProvider.BufferedBytes;
                    if (writable <= 0)
                    {
                        UpdateAudibleTrack();
                        await Task.Delay(2, _cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    var toWrite = Math.Min(writable, bytesRead - offset);
                    toWrite -= toWrite % _selectedFormat.BytesPerFrame;
                    if (toWrite == 0)
                        continue;
                    _bufferedProvider.AddSamples(buffer, offset, toWrite);
                    offset += toWrite;
                }

                Interlocked.Add(ref _totalFramesWritten, bytesRead / _selectedFormat.BytesPerFrame);
                if (remainderBytes > 0)
                    buffer.AsSpan(bytesRead, remainderBytes).CopyTo(buffer);
                UpdateAudibleTrack();
            }

            while (_bufferedProvider.BufferedBytes > 0 && !_cts.IsCancellationRequested)
            {
                UpdateAudibleTrack();
                await Task.Delay(5, _cts.Token).ConfigureAwait(false);
            }
            UpdateAudibleTrack();
        }
        finally
        {
            _activeDecoder?.Dispose();
            if (preparedNext is not null)
            {
                try
                {
                    var prepared = await preparedNext.ConfigureAwait(false);
                    prepared.Decoder.Dispose();
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                }
            }
        }
    }

    private Task<(FfmpegPcmDecoder Decoder, AudioFileInfo Info)>? PrepareTrackAsync(int index) =>
        index >= _items.Count ? null : PrepareAsync(index);

    private async Task<(FfmpegPcmDecoder Decoder, AudioFileInfo Info)> PrepareAsync(int index)
    {
        var info = await ProbeAsync(_items[index].PlaybackPath, _cts.Token).ConfigureAwait(false);
        if (_items[index].SegmentDuration is { } segmentDuration)
            info = info with { Duration = segmentDuration };
        else if (_items[index].KnownDuration is { } knownDuration)
            info = info with { Duration = knownDuration };
        var decoder = await FfmpegPcmDecoder.CreateAsync(
            _items[index].PlaybackPaths,
            _selectedFormat.Format.SampleRate,
            _selectedFormat.FfmpegSampleFormat,
            _selectedFormat.FfmpegCodec,
            TimeSpan.Zero,
            _items[index].SegmentStart,
            _items[index].SegmentEnd,
            _cts.Token).ConfigureAwait(false);
        return (decoder, info with { OutputSampleRate = _selectedFormat.Format.SampleRate });
    }

    private async Task<bool> TryResumePrematurePlexEofAsync(int decoderGeneration)
    {
        var trackIndex = _writeTrackIndex;
        var item = _items[trackIndex];
        if (item.SourcePaths is not { Count: > 0 } ||
            item.KnownDuration is not { } knownDuration ||
            knownDuration <= TimeSpan.Zero)
        {
            return false;
        }

        var outputSampleRate = _selectedFormat.Format.SampleRate;
        var writtenTrackFrames = Math.Max(
            0,
            Interlocked.Read(ref _totalFramesWritten) -
            Volatile.Read(ref _trackStartFrames[trackIndex]));
        var resumeFrames = writtenTrackFrames +
            Volatile.Read(ref _trackPositionOffsetFrames[trackIndex]);
        var resumePosition = TimeSpan.FromSeconds(resumeFrames / (double)outputSampleRate);
        if (knownDuration - resumePosition <= PrematurePlexEofTolerance)
            return false;

        while (_prematurePlexEofRetryCounts[trackIndex] < MaximumPrematurePlexEofRetries)
        {
            var retry = ++_prematurePlexEofRetryCounts[trackIndex];
            await Task.Delay(TimeSpan.FromMilliseconds(150 * retry), _cts.Token).ConfigureAwait(false);

            FfmpegPcmDecoder replacement;
            try
            {
                replacement = await FfmpegPcmDecoder.CreateAsync(
                    item.PlaybackPaths,
                    outputSampleRate,
                    _selectedFormat.FfmpegSampleFormat,
                    _selectedFormat.FfmpegCodec,
                    resumePosition,
                    item.SegmentStart,
                    item.SegmentEnd,
                    _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                continue;
            }

            await _decoderGate.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                if (decoderGeneration != Volatile.Read(ref _decoderGeneration) ||
                    trackIndex != _writeTrackIndex)
                {
                    replacement.Dispose();
                    return true;
                }

                _activeDecoder?.Dispose();
                _activeDecoder = replacement;
                return true;
            }
            finally
            {
                _decoderGate.Release();
            }
        }

        return false;
    }

    private void UpdateAudibleTrack()
    {
        lock (_stateLock)
        {
            var bufferedFrames = _bufferedProvider.BufferedBytes / _selectedFormat.BytesPerFrame;
            var playedFrames = Math.Max(0, Interlocked.Read(ref _totalFramesWritten) - bufferedFrames);
            while (_audibleTrackIndex < _writeTrackIndex &&
                   playedFrames >= Volatile.Read(ref _trackStartFrames[_audibleTrackIndex + 1]))
            {
                _audibleTrackIndex++;
                ReplayGainFactor = _items[_audibleTrackIndex].ReplayGainFactor;
                TrackChanged?.Invoke(
                    this,
                    new GaplessTrackChangedEventArgs(
                        _items[_audibleTrackIndex].FilePath,
                        _infos[_audibleTrackIndex]!));
            }
        }
    }

    private void ProcessPcm(Span<byte> bytes, float trackReplayGain)
    {
        ApplyPendingEqualizerChanges();
        if (_selectedFormat.FfmpegSampleFormat == "f32le")
        {
            var samples = MemoryMarshal.Cast<byte, float>(bytes);
            for (var index = 0; index + 1 < samples.Length; index += 2)
            {
                var output = _equalizer.Process(
                    samples[index] * trackReplayGain,
                    samples[index + 1] * trackReplayGain);
                samples[index] = Math.Clamp(output.Left, -1.0f, 1.0f);
                samples[index + 1] = Math.Clamp(output.Right, -1.0f, 1.0f);
            }
        }
        else if (_selectedFormat.FfmpegSampleFormat == "s16le")
        {
            var samples = MemoryMarshal.Cast<byte, short>(bytes);
            for (var index = 0; index + 1 < samples.Length; index += 2)
            {
                var output = _equalizer.Process(
                    samples[index] / 32768.0f * trackReplayGain,
                    samples[index + 1] / 32768.0f * trackReplayGain);
                samples[index] = FloatToInt16(output.Left);
                samples[index + 1] = FloatToInt16(output.Right);
            }
        }
        else if (_selectedFormat.FfmpegSampleFormat == "s24le")
        {
            for (var offset = 0; offset + 5 < bytes.Length; offset += 6)
            {
                var output = _equalizer.Process(
                    ReadInt24(bytes, offset) / 8_388_608.0f * trackReplayGain,
                    ReadInt24(bytes, offset + 3) / 8_388_608.0f * trackReplayGain);
                WriteInt24(bytes, offset, output.Left);
                WriteInt24(bytes, offset + 3, output.Right);
            }
        }
        else
        {
            var samples = MemoryMarshal.Cast<byte, int>(bytes);
            for (var index = 0; index + 1 < samples.Length; index += 2)
            {
                var output = _equalizer.Process(
                    samples[index] / 2_147_483_648.0f * trackReplayGain,
                    samples[index + 1] / 2_147_483_648.0f * trackReplayGain);
                samples[index] = FloatToInt32(output.Left);
                samples[index + 1] = FloatToInt32(output.Right);
            }
        }
    }

    private void ApplyPendingEqualizerChanges()
    {
        if (Interlocked.Exchange(ref _equalizerResetRequested, 0) != 0)
            _equalizer.Reset();
        var update = Interlocked.Exchange(ref _pendingEqualizerUpdate, null);
        if (update is not null)
            _equalizer.Update(update.Enabled, update.Profile);
    }

    private sealed record EqualizerUpdateRequest(bool Enabled, EqualizerProfile? Profile);

    private static short FloatToInt16(float sample) =>
        (short)Math.Round(Math.Clamp(sample, -1.0f, 1.0f) * (sample < 0 ? 32768.0f : 32767.0f));

    private static int FloatToInt32(float sample) =>
        (int)Math.Round(Math.Clamp(sample, -1.0f, 1.0f) * (sample < 0 ? 2_147_483_648.0 : 2_147_483_647.0));

    private static int ReadInt24(ReadOnlySpan<byte> bytes, int offset)
    {
        var sample = bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
        return (sample & 0x0080_0000) != 0
            ? sample | unchecked((int)0xFF00_0000)
            : sample;
    }

    private static void WriteInt24(Span<byte> bytes, int offset, float sample)
    {
        var value = (int)Math.Round(
            Math.Clamp(sample, -1.0f, 1.0f) * (sample < 0 ? 8_388_608.0 : 8_388_607.0));
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
    }

    private static async Task DisposePreparedAsync(
        Task<(FfmpegPcmDecoder Decoder, AudioFileInfo Info)> preparedTask)
    {
        try
        {
            var prepared = await preparedTask.ConfigureAwait(false);
            prepared.Decoder.Dispose();
        }
        catch
        {
        }
    }

    private static async Task<AudioFileInfo> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        // Over HTTP, ffprobe otherwise blocks on its default 5 s / 5 MB analysis window,
        // which is the main reason the first play of a remote track is slow. Audio headers
        // are tiny, so cap the probe and add reconnect resilience for remote inputs.
        var isHttpInput = filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var httpInputOptions = isHttpInput
            ? "-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 2 -analyzeduration 500000 -probesize 500000 "
            : string.Empty;
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error {httpInputOptions}-select_streams a:0 -show_entries stream=codec_name,sample_rate,channels,duration -of json \"{filePath}\"",
            WorkingDirectory = FfmpegLocator.GetSafeWorkingDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("ffprobe konnte nicht gestartet werden.");
        var json = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var stream = document.RootElement.GetProperty("streams")[0];
        var codec = stream.GetProperty("codec_name").GetString() ?? "unknown";
        var rate = int.Parse(stream.GetProperty("sample_rate").GetString() ?? "0", CultureInfo.InvariantCulture);
        var channels = stream.GetProperty("channels").GetInt32();
        var isDsd = codec.Contains("dsd", StringComparison.OrdinalIgnoreCase);
        var duration = stream.TryGetProperty("duration", out var durationJson)
            ? ParseDuration(durationJson)
            : 0;
        return new AudioFileInfo(
            codec,
            rate,
            channels,
            NormalizePcmRate(rate),
            isDsd,
            Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
            TimeSpan.FromSeconds(duration));
    }

    private static int NormalizePcmRate(int sampleRate) =>
        sampleRate is >= 8_000 and <= 768_000 ? sampleRate : 192_000;

    private static double ParseDuration(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }

    private static WasapiSelectedFormat ChooseExclusiveFormat(MMDevice device, AudioFileInfo info)
    {
        int[] standardSampleRates =
        [
            8_000, 11_025, 12_000, 16_000, 22_050, 24_000, 32_000,
            44_100, 48_000, 88_200, 96_000, 176_400, 192_000,
            352_800, 384_000, 705_600, 768_000
        ];
        var sourceSampleRate = Math.Max(info.SourceSampleRate, info.OutputSampleRate);
        var sampleRates = standardSampleRates
            .Append(info.OutputSampleRate)
            .Where(static rate => rate > 0)
            .Distinct()
            .OrderBy(rate => rate <= sourceSampleRate ? 0 : 1)
            .ThenByDescending(rate => rate <= sourceSampleRate ? rate : 0)
            .ThenBy(rate => rate > sourceSampleRate ? rate : int.MaxValue);

        foreach (var sampleRate in sampleRates)
        {
            WasapiSelectedFormat[] candidates =
            [
                new(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2), "f32le", "pcm_f32le"),
                new(new WaveFormatExtensible(sampleRate, 24, 2), "s24le", "pcm_s24le"),
                new(new WaveFormat(sampleRate, 16, 2), "s16le", "pcm_s16le")
            ];
            foreach (var candidate in candidates)
                if (device.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, candidate.Format))
                    return candidate;
        }

        throw new NotSupportedException("Das WASAPI-Gerät unterstützt keine geeignete Stereo-PCM-Ausgabe im exklusiven Modus.");
    }

    private sealed record WasapiSelectedFormat(
        WaveFormat Format,
        string FfmpegSampleFormat,
        string FfmpegCodec)
    {
        public int BytesPerFrame => Format.BlockAlign;
    }

    private sealed class PausableWaveProvider(IWaveProvider source) : IWaveProvider
    {
        private volatile bool _isPaused;
        public WaveFormat WaveFormat => source.WaveFormat;
        public bool IsPaused { get => _isPaused; set => _isPaused = value; }
        public int Read(byte[] buffer, int offset, int count)
        {
            if (!_isPaused)
                return source.Read(buffer, offset, count);
            Array.Clear(buffer, offset, count);
            return count;
        }
    }
}
