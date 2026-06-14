using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Orynivo.Audio;

public sealed class FfmpegAudioPlayer : IAudioPlayer
{
    private readonly SteinbergAsioStream _stream;
    private readonly string _filePath;
    private readonly AudioFileInfo _info;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _processGate = new(1, 1);
    private readonly Task _pumpTask;
    private Process _process;
    private bool _paused;
    private long _framesWritten;
    private bool _disposed;

    private FfmpegAudioPlayer(SteinbergAsioStream stream, Process process, string filePath, AudioFileInfo info)
    {
        _stream = stream;
        _process = process;
        _filePath = filePath;
        _info = info;
        _pumpTask = Task.Run(PumpAsync);
    }

    public TimeSpan Duration => _info.Duration;
    public TimeSpan Position => TimeSpan.FromSeconds((double)Interlocked.Read(ref _framesWritten) / _info.OutputSampleRate);
    public bool IsPaused => _paused;
    public bool CanSeek => Duration > TimeSpan.Zero;
    public float Volume { get; set; } = 1.0f;

    public static async Task<(FfmpegAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string filePath, string driverName, CancellationToken cancellationToken = default)
    {
        var info = await ProbeAsync(filePath, cancellationToken);
        var stream = new SteinbergAsioStream(driverName, info.OutputSampleRate, 2);
        stream.Start();
        return (new FfmpegAudioPlayer(stream, StartFfmpeg(filePath, info.OutputSampleRate, TimeSpan.Zero), filePath, info), info);
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    public async Task SeekAsync(TimeSpan position)
    {
        if (!CanSeek) return;
        position = Clamp(position);
        await _processGate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            _process.Dispose();
            _process = StartFfmpeg(_filePath, _info.OutputSampleRate, position);
            Interlocked.Exchange(ref _framesWritten, (long)(position.TotalSeconds * _info.OutputSampleRate));
        }
        finally { _processGate.Release(); }
    }

    public async Task WaitForCompletionAsync() => await _pumpTask.ConfigureAwait(false);

    public void Dispose()
    {
        if (_disposed) return;
        _cts.Cancel();
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        try { _pumpTask.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        _process.Dispose();
        _stream.Dispose();
        _cts.Dispose();
        _processGate.Dispose();
        _disposed = true;
    }

    private async Task PumpAsync()
    {
        var buffer = new byte[64 * 1024];
        var floatBuffer = new float[buffer.Length / sizeof(float)];
        while (!_cts.IsCancellationRequested)
        {
            if (_paused)
            {
                await Task.Delay(10, _cts.Token).ConfigureAwait(false);
                continue;
            }

            await _processGate.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                var bytesRead = await _process.StandardOutput.BaseStream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    if (_process.HasExited) break;
                    continue;
                }

                var usableBytes = bytesRead - (bytesRead % sizeof(float));
                Buffer.BlockCopy(buffer, 0, floatBuffer, 0, usableBytes);
                var samples = usableBytes / sizeof(float);
                ApplyVolume(floatBuffer.AsSpan(0, samples));
                var accepted = _stream.WriteInterleaved(floatBuffer.AsSpan(0, samples));
                while (accepted < samples && !_cts.IsCancellationRequested)
                {
                    await Task.Delay(2, _cts.Token).ConfigureAwait(false);
                    accepted += _stream.WriteInterleaved(floatBuffer.AsSpan(accepted, samples - accepted));
                }
                Interlocked.Add(ref _framesWritten, samples / 2);
            }
            finally { _processGate.Release(); }
        }
    }

    private static async Task<AudioFileInfo> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -select_streams a:0 -show_entries stream=codec_name,sample_rate,channels,duration -of json \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("ffprobe konnte nicht gestartet werden.");
        using (process)
        {
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
            return new AudioFileInfo(codec, rate, channels, isDsd ? 176_400 : NormalizePcmRate(rate), isDsd,
                Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(), TimeSpan.FromSeconds(duration));
        }
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
    private void ApplyVolume(Span<float> samples)
    {
        if (Math.Abs(Volume - 1.0f) < 0.0001f) return;
        for (var index = 0; index < samples.Length; index++) samples[index] *= Volume;
    }
    private static Process StartFfmpeg(string path, int rate, TimeSpan pos) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v error -ss {pos.TotalSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{path}\" -vn -f f32le -acodec pcm_f32le -ac 2 -ar {rate} pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("ffmpeg konnte nicht gestartet werden.");
    private TimeSpan Clamp(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value > Duration ? Duration : value;
}
