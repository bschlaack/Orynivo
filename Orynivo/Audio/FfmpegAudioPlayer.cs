using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Orynivo.Audio;

/// <summary>
/// Plays one or more files as continuous 32-bit float PCM through one ASIO
/// device session. The next FFmpeg decoder is started and prefetched while the
/// current track is playing.
/// </summary>
public sealed class FfmpegAudioPlayer : IGaplessAudioPlayer, IEqualizerAudioPlayer
{
    private const int MaximumPrematurePlexEofRetries = 3;
    private static readonly TimeSpan PrematurePlexEofTolerance = TimeSpan.FromSeconds(5);
    private readonly SteinbergAsioStream _stream;
    private readonly IReadOnlyList<GaplessPlaybackItem> _items;
    private readonly AudioFileInfo?[] _infos;
    private readonly long[] _trackStartFrames;
    private readonly long[] _trackPositionOffsetFrames;
    private readonly int[] _prematurePlexEofRetryCounts;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _decoderGate = new(1, 1);
    private readonly Task _pumpTask;
    private readonly object _stateLock = new();
    private readonly ParametricEqualizer _equalizer;
    private volatile bool _paused;
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

    private FfmpegAudioPlayer(
        SteinbergAsioStream stream,
        IReadOnlyList<GaplessPlaybackItem> items,
        AudioFileInfo firstInfo,
        FfmpegPcmDecoder firstDecoder,
        bool equalizerEnabled,
        EqualizerProfile? equalizerProfile)
    {
        _stream = stream;
        _items = items;
        _infos = new AudioFileInfo?[items.Count];
        _infos[0] = firstInfo;
        _trackStartFrames = new long[items.Count];
        _trackPositionOffsetFrames = new long[items.Count];
        _prematurePlexEofRetryCounts = new int[items.Count];
        _equalizer = new ParametricEqualizer(
            firstInfo.OutputSampleRate,
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
            var playedFrames = Math.Max(0, Interlocked.Read(ref _totalFramesWritten) - _stream.BufferedFrames);
            var positionFrames = Math.Max(0, playedFrames - Volatile.Read(ref _trackStartFrames[index]));
            return TimeSpan.FromSeconds(
                Math.Min(
                    (positionFrames + Volatile.Read(ref _trackPositionOffsetFrames[index])) /
                    (double)CurrentInfo.OutputSampleRate,
                    Duration.TotalSeconds));
        }
    }

    /// <inheritdoc/>
    public bool IsPaused => _paused;
    /// <inheritdoc/>
    public bool CanSeek => Duration > TimeSpan.Zero;
    /// <inheritdoc/>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0f, 1.0f);
            _stream.SetVolume(_volume);
        }
    }
    /// <inheritdoc/>
    public float ReplayGainFactor { get; set; } = 1.0f;

    /// <summary>Creates a continuous ASIO PCM playback session.</summary>
    /// <param name="items">Tracks in playback order with their ReplayGain factors.</param>
    /// <param name="backend">ASIO bridge backend.</param>
    /// <param name="driverName">ASIO driver name.</param>
    /// <param name="equalizerEnabled">Whether the supplied equalizer profile is active.</param>
    /// <param name="equalizerProfile">Equalizer profile applied to PCM samples.</param>
    /// <param name="cancellationToken">Cancellation token for initial probing and decoder startup.</param>
    /// <returns>The player and technical information for the first track.</returns>
    public static async Task<(FfmpegAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        IReadOnlyList<GaplessPlaybackItem> items,
        OutputBackend backend,
        string driverName,
        bool equalizerEnabled = false,
        EqualizerProfile? equalizerProfile = null,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
            throw new ArgumentException("At least one playback item is required.", nameof(items));

        var info = items[0].TryCreateKnownAudioInfo()
                   ?? await ProbeAsync(items[0].PlaybackPath, cancellationToken);
        if (items[0].SegmentDuration is { } firstDuration)
            info = info with { Duration = firstDuration };
        else if (items[0].KnownDuration is { } knownDuration)
            info = info with { Duration = knownDuration };
        var supportedSampleRates = SteinbergAsioStream
            .GetDeviceInfo(backend, driverName)
            .SupportedPcmSampleRates
            .Where(static rate => rate > 0)
            .Distinct()
            .ToArray();
        if (supportedSampleRates.Length > 0)
        {
            var outputSampleRate = supportedSampleRates
                .Where(rate => rate <= info.OutputSampleRate)
                .DefaultIfEmpty(supportedSampleRates.Min())
                .Max();
            info = info with { OutputSampleRate = outputSampleRate };
        }
        var stream = new SteinbergAsioStream(backend, driverName, info.OutputSampleRate, 2);
        try
        {
            var decoder = await FfmpegPcmDecoder.CreateAsync(
                items[0].PlaybackPaths,
                info.OutputSampleRate,
                "f32le",
                "pcm_f32le",
                TimeSpan.Zero,
                items[0].SegmentStart,
                items[0].SegmentEnd,
                cancellationToken);
            stream.Start();
            return (new FfmpegAudioPlayer(
                stream,
                items,
                info,
                decoder,
                equalizerEnabled,
                equalizerProfile), info);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Creates a single-track ASIO PCM playback session.</summary>
    /// <param name="filePath">Local path or supported stream URL.</param>
    /// <param name="backend">ASIO bridge backend.</param>
    /// <param name="driverName">ASIO driver name.</param>
    /// <param name="equalizerEnabled">Whether the supplied equalizer profile is active.</param>
    /// <param name="equalizerProfile">Equalizer profile applied to PCM samples.</param>
    /// <param name="cancellationToken">Cancellation token for initial probing and decoder startup.</param>
    /// <returns>The player and technical information for the track.</returns>
    public static Task<(FfmpegAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string filePath,
        OutputBackend backend,
        string driverName,
        bool equalizerEnabled = false,
        EqualizerProfile? equalizerProfile = null,
        CancellationToken cancellationToken = default) =>
        CreateAsync(
            [new GaplessPlaybackItem(filePath, 1.0f)],
            backend,
            driverName,
            equalizerEnabled,
            equalizerProfile,
            cancellationToken);

    /// <inheritdoc/>
    public void UpdateEqualizer(bool enabled, EqualizerProfile? profile)
        => Interlocked.Exchange(
            ref _pendingEqualizerUpdate,
            new EqualizerUpdateRequest(enabled, profile?.Clone()));

    /// <inheritdoc/>
    public void Pause() => _paused = true;
    /// <inheritdoc/>
    public void Resume() => _paused = false;

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
            "asio-player",
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
            _stream.ClearBuffer();
            Interlocked.Exchange(ref _equalizerResetRequested, 1);
            Interlocked.Exchange(ref _totalFramesWritten, 0);
            Volatile.Write(
                ref _trackPositionOffsetFrames[audibleIndex],
                (long)(position.TotalSeconds * CurrentInfo.OutputSampleRate));
            SeekDiagnostics.Log(
                "asio-player",
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
                "f32le",
                "pcm_f32le",
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
                        "asio-player",
                        $"replacement-installed request={seekRequest} index={audibleIndex} elapsedMs={stopwatch.ElapsedMilliseconds}");
                }
                else
                {
                    SeekDiagnostics.Log(
                        "asio-player",
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
                    "asio-player",
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
        _stream.Dispose();
        _cts.Dispose();
        _decoderGate.Dispose();
        _disposed = true;
    }

    private async Task PumpAsync(FfmpegPcmDecoder firstDecoder)
    {
        var buffer = new byte[64 * 1024];
        var floatBuffer = new float[buffer.Length / sizeof(float)];
        var remainderBytes = 0;
        Task<(FfmpegPcmDecoder Decoder, AudioFileInfo Info)>? preparedNext =
            PrepareTrackAsync(1);

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

                if (_paused)
                {
                    UpdateAudibleTrack();
                    await Task.Delay(10, _cts.Token).ConfigureAwait(false);
                    continue;
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
                var usableBytes = totalBytes - (totalBytes % (sizeof(float) * 2));
                Buffer.BlockCopy(buffer, 0, floatBuffer, 0, usableBytes);
                remainderBytes = totalBytes - usableBytes;
                var samples = usableBytes / sizeof(float);
                ProcessPcm(
                    floatBuffer.AsSpan(0, samples),
                    _items[_writeTrackIndex].ReplayGainFactor);
                var accepted = _stream.WriteInterleaved(floatBuffer.AsSpan(0, samples));
                while (accepted < samples && !_cts.IsCancellationRequested)
                {
                    UpdateAudibleTrack();
                    await Task.Delay(2, _cts.Token).ConfigureAwait(false);
                    accepted += _stream.WriteInterleaved(floatBuffer.AsSpan(accepted, samples - accepted));
                }

                Interlocked.Add(ref _totalFramesWritten, samples / 2);
                if (remainderBytes > 0)
                    buffer.AsSpan(usableBytes, remainderBytes).CopyTo(buffer);
                UpdateAudibleTrack();
            }

            while (_stream.BufferedFrames > 0 && !_cts.IsCancellationRequested)
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

    private Task<(FfmpegPcmDecoder Decoder, AudioFileInfo Info)>? PrepareTrackAsync(int index)
    {
        if (index >= _items.Count)
            return null;
        return PrepareAsync(index);
    }

    private async Task<(FfmpegPcmDecoder Decoder, AudioFileInfo Info)> PrepareAsync(int index)
    {
        var info = _items[index].TryCreateKnownAudioInfo()
                   ?? await ProbeAsync(_items[index].PlaybackPath, _cts.Token).ConfigureAwait(false);
        if (_items[index].SegmentDuration is { } segmentDuration)
            info = info with { Duration = segmentDuration };
        else if (_items[index].KnownDuration is { } knownDuration)
            info = info with { Duration = knownDuration };
        var decoder = await FfmpegPcmDecoder.CreateAsync(
            _items[index].PlaybackPaths,
            _infos[0]!.OutputSampleRate,
            "f32le",
            "pcm_f32le",
            TimeSpan.Zero,
            _items[index].SegmentStart,
            _items[index].SegmentEnd,
            _cts.Token).ConfigureAwait(false);
        return (decoder, info with { OutputSampleRate = _infos[0]!.OutputSampleRate });
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

        var outputSampleRate = _infos[0]!.OutputSampleRate;
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
                    "f32le",
                    "pcm_f32le",
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
            var playedFrames = Math.Max(0, Interlocked.Read(ref _totalFramesWritten) - _stream.BufferedFrames);
            while (_audibleTrackIndex < _writeTrackIndex &&
                   playedFrames >= Volatile.Read(ref _trackStartFrames[_audibleTrackIndex + 1]))
            {
                _audibleTrackIndex++;
                ReplayGainFactor = _items[_audibleTrackIndex].ReplayGainFactor;
                var info = _infos[_audibleTrackIndex]!;
                TrackChanged?.Invoke(
                    this,
                    new GaplessTrackChangedEventArgs(_items[_audibleTrackIndex].FilePath, info));
            }
        }
    }

    private void ProcessPcm(Span<float> samples, float trackReplayGain)
    {
        ApplyPendingEqualizerChanges();
        for (var index = 0; index + 1 < samples.Length; index += 2)
        {
            var output = _equalizer.Process(
                samples[index] * trackReplayGain,
                samples[index + 1] * trackReplayGain);
            samples[index] = Math.Clamp(output.Left, -1.0f, 1.0f);
            samples[index + 1] = Math.Clamp(output.Right, -1.0f, 1.0f);
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
        var duration = stream.TryGetProperty("duration", out var durationJson)
            ? ParseDuration(durationJson)
            : 0;
        var isDsd = codec.Contains("dsd", StringComparison.OrdinalIgnoreCase);
        return new AudioFileInfo(
            codec,
            rate,
            channels,
            isDsd ? 176_400 : NormalizePcmRate(rate),
            isDsd,
            Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
            TimeSpan.FromSeconds(duration));
    }

    private static int NormalizePcmRate(int rate) => rate is >= 8_000 and <= 768_000 ? rate : 192_000;
    private static double ParseDuration(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }
}
