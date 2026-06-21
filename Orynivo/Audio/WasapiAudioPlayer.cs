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
public sealed class WasapiAudioPlayer : IGaplessAudioPlayer
{
    private readonly IReadOnlyList<GaplessPlaybackItem> _items;
    private readonly AudioFileInfo?[] _infos;
    private readonly long[] _trackStartFrames;
    private readonly long[] _trackPositionOffsetFrames;
    private readonly WasapiOut _output;
    private readonly BufferedWaveProvider _bufferedProvider;
    private readonly PausableWaveProvider _playbackProvider;
    private readonly MMDevice _device;
    private readonly WasapiSelectedFormat _selectedFormat;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _decoderGate = new(1, 1);
    private readonly Task _pumpTask;
    private readonly object _stateLock = new();
    private long _totalFramesWritten;
    private int _audibleTrackIndex;
    private int _writeTrackIndex;
    private FfmpegPcmDecoder? _activeDecoder;
    private int _restartPreparedFromIndex = -1;
    private int _decoderGeneration;
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
        WasapiSelectedFormat selectedFormat)
    {
        _items = items;
        _infos = new AudioFileInfo?[items.Count];
        _infos[0] = firstInfo;
        _trackStartFrames = new long[items.Count];
        _trackPositionOffsetFrames = new long[items.Count];
        _output = output;
        _bufferedProvider = bufferedProvider;
        _playbackProvider = playbackProvider;
        _device = device;
        _selectedFormat = selectedFormat;
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
    /// <param name="cancellationToken">Cancellation token for initial probing and decoder startup.</param>
    /// <returns>The player and technical information for the first track.</returns>
    public static async Task<(WasapiAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        IReadOnlyList<GaplessPlaybackItem> items,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
            throw new ArgumentException("At least one playback item is required.", nameof(items));

        var info = await ProbeAsync(items[0].FilePath, cancellationToken);
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
                items[0].FilePath,
                info.OutputSampleRate,
                selectedFormat.FfmpegSampleFormat,
                selectedFormat.FfmpegCodec,
                TimeSpan.Zero,
                cancellationToken);
            output.Play();
            return (new WasapiAudioPlayer(
                items, info, decoder, output, provider, playbackProvider, device, selectedFormat), info);
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
    /// <param name="cancellationToken">Cancellation token for initial probing and decoder startup.</param>
    /// <returns>The player and technical information for the track.</returns>
    public static Task<(WasapiAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string filePath,
        string deviceId,
        CancellationToken cancellationToken = default) =>
        CreateAsync([new GaplessPlaybackItem(filePath, 1.0f)], deviceId, cancellationToken);

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
        var replacement = await FfmpegPcmDecoder.CreateAsync(
            CurrentFilePath,
            CurrentInfo.OutputSampleRate,
            _selectedFormat.FfmpegSampleFormat,
            _selectedFormat.FfmpegCodec,
            position,
            _cts.Token).ConfigureAwait(false);
        await _decoderGate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            _activeDecoder?.Dispose();
            _activeDecoder = replacement;
            Interlocked.Increment(ref _decoderGeneration);
            var audibleIndex = Volatile.Read(ref _audibleTrackIndex);
            _writeTrackIndex = audibleIndex;
            Volatile.Write(ref _trackStartFrames[audibleIndex], 0);
            Volatile.Write(ref _restartPreparedFromIndex, audibleIndex);
            _bufferedProvider.ClearBuffer();
            Interlocked.Exchange(ref _totalFramesWritten, 0);
            Volatile.Write(
                ref _trackPositionOffsetFrames[audibleIndex],
                (long)(position.TotalSeconds * CurrentInfo.OutputSampleRate));
        }
        finally
        {
            _decoderGate.Release();
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
                    bytesRead = await _activeDecoder!.ReadAsync(
                        buffer.AsMemory(remainderBytes),
                        _cts.Token).ConfigureAwait(false);
                }
                finally
                {
                    _decoderGate.Release();
                }
                if (decoderGeneration != Volatile.Read(ref _decoderGeneration))
                {
                    remainderBytes = 0;
                    continue;
                }
                if (bytesRead == 0)
                {
                    remainderBytes = 0;
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
                ApplyReplayGain(buffer.AsSpan(0, bytesRead), _items[_writeTrackIndex].ReplayGainFactor);
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
        var info = await ProbeAsync(_items[index].FilePath, _cts.Token).ConfigureAwait(false);
        var decoder = await FfmpegPcmDecoder.CreateAsync(
            _items[index].FilePath,
            _selectedFormat.Format.SampleRate,
            _selectedFormat.FfmpegSampleFormat,
            _selectedFormat.FfmpegCodec,
            TimeSpan.Zero,
            _cts.Token).ConfigureAwait(false);
        return (decoder, info with { OutputSampleRate = _selectedFormat.Format.SampleRate });
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

    private void ApplyReplayGain(Span<byte> bytes, float trackReplayGain)
    {
        var factor = trackReplayGain;
        if (Math.Abs(factor - 1.0f) < 0.0001f)
            return;

        if (_selectedFormat.FfmpegSampleFormat == "f32le")
        {
            var samples = MemoryMarshal.Cast<byte, float>(bytes);
            for (var index = 0; index < samples.Length; index++)
                samples[index] = Math.Clamp(samples[index] * factor, -1.0f, 1.0f);
        }
        else if (_selectedFormat.FfmpegSampleFormat == "s16le")
        {
            var samples = MemoryMarshal.Cast<byte, short>(bytes);
            for (var index = 0; index < samples.Length; index++)
                samples[index] = (short)Math.Clamp(samples[index] * factor, short.MinValue, short.MaxValue);
        }
        else
        {
            var samples = MemoryMarshal.Cast<byte, int>(bytes);
            for (var index = 0; index < samples.Length; index++)
                samples[index] = (int)Math.Clamp(samples[index] * (double)factor, int.MinValue, int.MaxValue);
        }
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
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -select_streams a:0 -show_entries stream=codec_name,sample_rate,channels,duration -of json \"{filePath}\"",
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
        var sampleRates = info.IsDsd
            ? new[] { 176_400, 88_200, 44_100, 192_000, 96_000, 48_000 }
            : new[] { info.OutputSampleRate };
        foreach (var sampleRate in sampleRates)
        {
            WasapiSelectedFormat[] candidates =
            [
                new(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2), "f32le", "pcm_f32le", sizeof(float) * 2),
                new(new WaveFormatExtensible(sampleRate, 24, 2), "s32le", "pcm_s32le", sizeof(int) * 2),
                new(new WaveFormat(sampleRate, 16, 2), "s16le", "pcm_s16le", sizeof(short) * 2)
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
        string FfmpegCodec,
        int BytesPerFrame);

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
