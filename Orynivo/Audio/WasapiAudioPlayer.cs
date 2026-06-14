using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Orynivo.Audio;

public sealed class WasapiAudioPlayer : IAudioPlayer
{
    private readonly string _filePath;
    private readonly WasapiOut _output;
    private readonly BufferedWaveProvider _bufferedProvider;
    private readonly PausableWaveProvider _playbackProvider;
    private readonly MMDevice _device;
    private readonly AudioFileInfo _info;
    private readonly WasapiSelectedFormat _selectedFormat;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _processGate = new(1, 1);
    private readonly Task _pumpTask;
    private Process _process;
    private long _framesWritten;
    private bool _disposed;

    private WasapiAudioPlayer(
        string filePath,
        Process process,
        WasapiOut output,
        BufferedWaveProvider bufferedProvider,
        PausableWaveProvider playbackProvider,
        MMDevice device,
        AudioFileInfo info,
        WasapiSelectedFormat selectedFormat)
    {
        _filePath = filePath;
        _process = process;
        _output = output;
        _bufferedProvider = bufferedProvider;
        _playbackProvider = playbackProvider;
        _device = device;
        _info = info;
        _selectedFormat = selectedFormat;
        _pumpTask = Task.Run(PumpAsync);
    }

    public TimeSpan Duration => _info.Duration;
    public TimeSpan Position
    {
        get
        {
            var framesWritten = Interlocked.Read(ref _framesWritten);
            var bufferedFrames = _bufferedProvider.BufferedBytes / _selectedFormat.BytesPerFrame;
            var playedFrames = Math.Max(0, framesWritten - bufferedFrames);
            return TimeSpan.FromSeconds((double)playedFrames / _info.OutputSampleRate);
        }
    }
    public bool IsPaused => _playbackProvider.IsPaused;
    public bool CanSeek => Duration > TimeSpan.Zero;
    public float Volume { get; set; } = 1.0f;

    public static async Task<(WasapiAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string filePath,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var info = await ProbeAsync(filePath, cancellationToken);
        if (info.IsDsd)
        {
            throw new NotSupportedException("Native DSD-Wiedergabe ist über WASAPI nicht implementiert. Bitte ASIO verwenden.");
        }

        var device = WasapiDeviceProvider.GetRenderDevice(deviceId);
        var selectedFormat = ChooseExclusiveFormat(device, info.OutputSampleRate);
        var provider = new BufferedWaveProvider(selectedFormat.Format)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = false
        };
        var playbackProvider = new PausableWaveProvider(provider);

        var output = new WasapiOut(device, AudioClientShareMode.Exclusive, useEventSync: true, latency: 100);
        output.Init(playbackProvider);
        output.Play();

        var process = StartFfmpeg(filePath, info.OutputSampleRate, selectedFormat, TimeSpan.Zero);
        return (new WasapiAudioPlayer(
            filePath, process, output, provider, playbackProvider, device, info, selectedFormat), info);
    }

    public void Pause() => _playbackProvider.IsPaused = true;
    public void Resume() => _playbackProvider.IsPaused = false;

    public async Task SeekAsync(TimeSpan position)
    {
        if (!CanSeek)
        {
            return;
        }

        position = Clamp(position);
        await _processGate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            _process.Dispose();
            _bufferedProvider.ClearBuffer();
            _process = StartFfmpeg(_filePath, _info.OutputSampleRate, _selectedFormat, position);
            Interlocked.Exchange(ref _framesWritten, (long)(position.TotalSeconds * _info.OutputSampleRate));
        }
        finally
        {
            _processGate.Release();
        }
    }

    public async Task WaitForCompletionAsync() => await _pumpTask.ConfigureAwait(false);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cts.Cancel();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        try
        {
            _pumpTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        _output.Stop();
        _output.Dispose();
        _process.Dispose();
        _device.Dispose();
        _cts.Dispose();
        _processGate.Dispose();
        _disposed = true;
    }

    private async Task PumpAsync()
    {
        var buffer = new byte[64 * 1024];
        while (!_cts.IsCancellationRequested)
        {
            await _processGate.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                var bytesRead = await _process.StandardOutput.BaseStream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    if (_process.HasExited)
                    {
                        break;
                    }

                    continue;
                }

                var offset = 0;
                ApplyVolumeIfNeeded(buffer.AsSpan(0, bytesRead));
                while (offset < bytesRead && !_cts.IsCancellationRequested)
                {
                    var writable = _bufferedProvider.BufferLength - _bufferedProvider.BufferedBytes;
                    if (writable <= 0)
                    {
                        await Task.Delay(2, _cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    var toWrite = Math.Min(writable, bytesRead - offset);
                    _bufferedProvider.AddSamples(buffer, offset, toWrite);
                    offset += toWrite;
                }

                Interlocked.Add(ref _framesWritten, bytesRead / _selectedFormat.BytesPerFrame);
            }
            finally
            {
                _processGate.Release();
            }
        }

        while (_bufferedProvider.BufferedBytes > 0 && !_cts.IsCancellationRequested)
        {
            await Task.Delay(10, _cts.Token).ConfigureAwait(false);
        }
    }

    private static async Task<AudioFileInfo> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -select_streams a:0 -show_entries stream=codec_name,sample_rate,channels,duration -of json \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("ffprobe konnte nicht gestartet werden.");

        var json = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Datei konnte nicht analysiert werden: {error}");
        }

        using var document = JsonDocument.Parse(json);
        var stream = document.RootElement.GetProperty("streams")[0];
        var codecName = stream.GetProperty("codec_name").GetString() ?? "unknown";
        var sampleRate = int.Parse(stream.GetProperty("sample_rate").GetString() ?? "0", CultureInfo.InvariantCulture);
        var channels = stream.GetProperty("channels").GetInt32();
        var isDsd = codecName.Contains("dsd", StringComparison.OrdinalIgnoreCase);
        var duration = stream.TryGetProperty("duration", out var durationJson)
            ? ParseDuration(durationJson)
            : 0;

        return new AudioFileInfo(
            codecName,
            sampleRate,
            channels,
            NormalizePcmRate(sampleRate),
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

    private void ApplyVolumeIfNeeded(Span<byte> bytes)
    {
        if (Math.Abs(Volume - 1.0f) < 0.0001f)
        {
            return;
        }

        if (_selectedFormat.FfmpegSampleFormat == "f32le")
        {
            var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes);
            for (var index = 0; index < samples.Length; index++) samples[index] *= Volume;
        }
        else if (_selectedFormat.FfmpegSampleFormat == "s16le")
        {
            var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(bytes);
            for (var index = 0; index < samples.Length; index++) samples[index] = (short)(samples[index] * Volume);
        }
        else
        {
            var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(bytes);
            for (var index = 0; index < samples.Length; index++) samples[index] = (int)(samples[index] * Volume);
        }
    }

    private static WasapiSelectedFormat ChooseExclusiveFormat(MMDevice device, int sampleRate)
    {
        WasapiSelectedFormat[] candidates =
        [
            new(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2), "f32le", "pcm_f32le", sizeof(float) * 2),
            new(new WaveFormatExtensible(sampleRate, 24, 2), "s32le", "pcm_s32le", sizeof(int) * 2),
            new(new WaveFormat(sampleRate, 16, 2), "s16le", "pcm_s16le", sizeof(short) * 2)
        ];

        foreach (var candidate in candidates)
        {
            if (device.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, candidate.Format))
            {
                return candidate;
            }
        }

        device.Dispose();
        throw new NotSupportedException($"Das WASAPI-Gerät unterstützt {sampleRate:N0} Hz stereo im exklusiven Modus nicht.");
    }

    private static Process StartFfmpeg(string filePath, int outputSampleRate, WasapiSelectedFormat format, TimeSpan position)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v error -ss {position.TotalSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{filePath}\" -vn -f {format.FfmpegSampleFormat} -acodec {format.FfmpegCodec} -ac 2 -ar {outputSampleRate} pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("ffmpeg konnte nicht gestartet werden.");
    }

    private TimeSpan Clamp(TimeSpan value) =>
        value < TimeSpan.Zero ? TimeSpan.Zero :
        value > Duration ? Duration : value;

    private sealed record WasapiSelectedFormat(
        WaveFormat Format,
        string FfmpegSampleFormat,
        string FfmpegCodec,
        int BytesPerFrame);

    private sealed class PausableWaveProvider(IWaveProvider source) : IWaveProvider
    {
        private volatile bool _isPaused;

        public WaveFormat WaveFormat => source.WaveFormat;

        public bool IsPaused
        {
            get => _isPaused;
            set => _isPaused = value;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (!_isPaused)
            {
                return source.Read(buffer, offset, count);
            }

            Array.Clear(buffer, offset, count);
            return count;
        }
    }
}
