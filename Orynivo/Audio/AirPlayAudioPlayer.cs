using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Orynivo.Localization;

namespace Orynivo.Audio;

/// <summary>Metadata sent to an AirPlay receiver for its now-playing display.</summary>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Track artist.</param>
/// <param name="Album">Album title.</param>
/// <param name="Artwork">Optional JPEG or PNG artwork bytes.</param>
/// <param name="ArtworkMimeType">MIME type of <paramref name="Artwork"/>.</param>
internal sealed record AirPlayTrackMetadata(
    string? Title,
    string? Artist,
    string? Album,
    byte[]? Artwork,
    string? ArtworkMimeType);

/// <summary>Transport command received through the authenticated AirPlay event channel.</summary>
internal enum AirPlayRemoteCommand { Play, Pause, Next, Previous }

/// <summary>
/// Decodes one logical track to 44.1 kHz stereo PCM and feeds it to the native
/// AirPlay 2 bridge, with the classic RAOP helper retained as a fallback.
/// </summary>
internal sealed class AirPlayAudioPlayer : IAudioPlayer
{
    private const int SampleRate = 44_100;
    private readonly GaplessPlaybackItem _item;
    private readonly string _host;
    private readonly int _port;
    private readonly string? _deviceName;
    private readonly string? _deviceId;
    private readonly string? _senderPath;
    private AirPlayTrackMetadata _metadata = new(null, null, null, null, null);
    private Action<AirPlayRemoteCommand> _remoteCommand = static _ => { };
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _pipelineGate = new(1, 1);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _pipelineCts;
    private FfmpegPcmDecoder? _decoder;
    private Process? _sender;
    private AirPlay2NativeSession? _nativeSession;
    private Task? _pumpTask;
    private long _framesSent;
    private long _positionOffsetFrames;
    private volatile bool _paused;
    private float _volume = 1.0f;
    private float _replayGainFactor = 1.0f;
    private bool _disposed;

    private AirPlayAudioPlayer(
        GaplessPlaybackItem item,
        string host,
        int port,
        string? deviceName,
        string? deviceId,
        string? senderPath,
        AudioFileInfo info)
    {
        _item = item;
        _host = host;
        _port = port;
        _deviceName = deviceName;
        _deviceId = deviceId;
        _senderPath = senderPath;
        Info = info;
    }

    /// <summary>Gets the technical information for the decoded network stream.</summary>
    internal AudioFileInfo Info { get; }

    /// <inheritdoc/>
    public TimeSpan Duration => Info.Duration;

    /// <inheritdoc/>
    public TimeSpan Position => TimeSpan.FromSeconds(Math.Min(
        Duration.TotalSeconds,
        (Interlocked.Read(ref _positionOffsetFrames) + Interlocked.Read(ref _framesSent)) /
        (double)SampleRate));

    /// <inheritdoc/>
    public bool IsPaused => _paused;

    /// <inheritdoc/>
    public bool CanSeek => Duration > TimeSpan.Zero;

    /// <inheritdoc/>
    public float Volume
    {
        get => Volatile.Read(ref _volume);
        set => Volatile.Write(ref _volume, Math.Clamp(value, 0.0f, 1.0f));
    }

    /// <inheritdoc/>
    public float ReplayGainFactor
    {
        get => Volatile.Read(ref _replayGainFactor);
        set => Volatile.Write(ref _replayGainFactor, Math.Max(0.0f, value));
    }

    /// <summary>Creates and starts an AirPlay 2 or fallback classic AirPlay session.</summary>
    /// <param name="item">Logical source track and optional segment bounds.</param>
    /// <param name="host">Resolved receiver address.</param>
    /// <param name="port">Receiver RAOP port.</param>
    /// <param name="deviceName">Receiver display name.</param>
    /// <param name="deviceId">Stable DNS-SD service identifier.</param>
    /// <param name="initialVolume">Initial linear PCM volume.</param>
    /// <param name="initialReplayGain">Initial linear ReplayGain factor.</param>
    /// <param name="metadata">Track metadata displayed by the receiver.</param>
    /// <param name="remoteCommand">Handles receiver-originated transport commands.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>The player together with its decoded stream information.</returns>
    internal static async Task<(AirPlayAudioPlayer Player, AudioFileInfo Info)> CreateAsync(
        GaplessPlaybackItem item,
        string host,
        int port,
        string? deviceName,
        string? deviceId,
        float initialVolume,
        float initialReplayGain,
        AirPlayTrackMetadata metadata,
        Action<AirPlayRemoteCommand> remoteCommand,
        CancellationToken cancellationToken)
    {
        var senderPath = FindSenderPath();
        if (!AirPlay2NativeSession.IsAvailable && senderPath is null)
            throw new InvalidOperationException(LocalizationManager.Current.AirPlaySenderMissing);
        var info = item.TryCreateKnownAudioInfo() ?? await ProbeAsync(item.PlaybackPath, cancellationToken);
        if (item.SegmentDuration is { } segmentDuration)
            info = info with { Duration = segmentDuration };
        else if (item.KnownDuration is { } knownDuration)
            info = info with { Duration = knownDuration };
        info = info with { OutputSampleRate = SampleRate, Channels = 2 };

        var player = new AirPlayAudioPlayer(item, host, port, deviceName, deviceId, senderPath, info);
        player.Volume = initialVolume;
        player.ReplayGainFactor = initialReplayGain;
        player._metadata = metadata;
        player._remoteCommand = remoteCommand;
        await player.RestartPipelineAsync(TimeSpan.Zero, cancellationToken);
        return (player, info);
    }

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
        await RestartPipelineAsync(position, _lifetimeCts.Token).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task WaitForCompletionAsync() => _completion.Task;

    private async Task RestartPipelineAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        await _pipelineGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StopPipeline();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _pipelineCts = linkedCts;
            var decoder = await FfmpegPcmDecoder.CreateAsync(
                _item.PlaybackPaths,
                SampleRate,
                "s16le",
                "pcm_s16le",
                position,
                _item.SegmentStart,
                _item.SegmentEnd,
                cancellationToken).ConfigureAwait(false);
            Process? sender = null;
            AirPlay2NativeSession? nativeSession = null;
            try
            {
                if (AirPlay2NativeSession.IsAvailable)
                {
                    nativeSession = await AirPlay2NativeSession.CreateAsync(
                        _host, _port, _deviceName, _deviceId, _metadata, _remoteCommand,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    sender = StartSender();
                }
                _decoder = decoder;
                _sender = sender;
                _nativeSession = nativeSession;
                Interlocked.Exchange(ref _framesSent, 0);
                Interlocked.Exchange(ref _positionOffsetFrames, (long)(position.TotalSeconds * SampleRate));
                _pumpTask = Task.Run(() => PumpAsync(decoder, sender, nativeSession, linkedCts.Token));
            }
            catch
            {
                decoder.Dispose();
                sender?.Dispose();
                nativeSession?.Dispose();
                linkedCts.Dispose();
                _pipelineCts = null;
                throw;
            }
        }
        finally
        {
            _pipelineGate.Release();
        }
    }

    private Process StartSender()
    {
        if (_senderPath is null)
            throw new InvalidOperationException(LocalizationManager.Current.AirPlaySenderMissing);
        var startInfo = new ProcessStartInfo
        {
            FileName = _senderPath,
            WorkingDirectory = FfmpegLocator.GetSafeWorkingDirectory(),
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(_port.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("100");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(_host);
        startInfo.ArgumentList.Add("-");
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(LocalizationManager.Current.AirPlaySenderMissing);
        _ = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();
        return process;
    }

    private async Task PumpAsync(
        FfmpegPcmDecoder decoder,
        Process? sender,
        AirPlay2NativeSession? nativeSession,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_paused)
                {
                    await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var read = await decoder.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;
                ApplyGain(buffer.AsSpan(0, read));
                if (nativeSession is not null)
                    nativeSession.Write(buffer.AsSpan(0, read));
                else if (sender is not null)
                    await sender.StandardInput.BaseStream
                        .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                Interlocked.Add(ref _framesSent, read / 4);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                if (sender is not null)
                {
                    sender.StandardInput.Close();
                    await sender.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                _completion.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                _completion.TrySetException(ex);
        }
    }

    private void ApplyGain(Span<byte> pcm)
    {
        var gain = MapVolumeToLinearGain(Volume) * ReplayGainFactor;
        if (Math.Abs(gain - 1.0f) < 0.0001f)
            return;
        for (var offset = 0; offset + 1 < pcm.Length; offset += 2)
        {
            var sample = (short)(pcm[offset] | (pcm[offset + 1] << 8));
            var scaled = Math.Clamp((int)MathF.Round(sample * gain), short.MinValue, short.MaxValue);
            pcm[offset] = (byte)scaled;
            pcm[offset + 1] = (byte)(scaled >> 8);
        }
    }

    /// <summary>
    /// Maps the normalized AirPlay volume control to a perceptual PCM gain with
    /// additional resolution at normal listening levels.
    /// </summary>
    /// <param name="volume">Normalized volume from zero through one.</param>
    /// <returns>A cubic linear PCM gain from zero through one.</returns>
    internal static float MapVolumeToLinearGain(float volume)
    {
        var normalized = Math.Clamp(volume, 0.0f, 1.0f);
        return normalized * normalized * normalized;
    }

    private void StopPipeline()
    {
        _pipelineCts?.Cancel();
        _decoder?.Dispose();
        _decoder = null;
        if (_sender is { HasExited: false } sender)
            sender.Kill(entireProcessTree: true);
        _sender?.Dispose();
        _sender = null;
        _nativeSession?.Dispose();
        _nativeSession = null;
        _pipelineCts?.Dispose();
        _pipelineCts = null;
    }

    /// <summary>Gets whether the native AirPlay 2 bridge or compatible RAOP fallback is available.</summary>
    internal static bool IsSenderAvailable => AirPlay2NativeSession.IsAvailable || FindSenderPath() is not null;

    private static string? FindSenderPath()
    {
        var fileNames = OperatingSystem.IsWindows()
            ? new[] { "raop_play.exe", "raop_play" }
            : new[] { "raop_play" };
        foreach (var fileName in fileNames)
        {
            var besideApp = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(besideApp))
                return besideApp;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        foreach (var fileName in fileNames)
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static async Task<AudioFileInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            WorkingDirectory = FfmpegLocator.GetSafeWorkingDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "-v", "error", "-show_entries",
                     "stream=codec_name,sample_rate,channels:format=duration,format_name",
                     "-of", "json", path
                 })
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("ffprobe could not be started.");
        var jsonTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(await errorTask.ConfigureAwait(false));
        using var document = JsonDocument.Parse(await jsonTask.ConfigureAwait(false));
        var stream = document.RootElement.GetProperty("streams")[0];
        var format = document.RootElement.GetProperty("format");
        _ = int.TryParse(stream.GetProperty("sample_rate").GetString(), out var sampleRate);
        _ = int.TryParse(stream.GetProperty("channels").ToString(), out var channels);
        _ = double.TryParse(format.GetProperty("duration").GetString(), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var duration);
        var codec = stream.TryGetProperty("codec_name", out var codecElement)
            ? codecElement.GetString() ?? "unknown"
            : "unknown";
        var container = format.TryGetProperty("format_name", out var formatElement)
            ? formatElement.GetString() ?? string.Empty
            : string.Empty;
        return new AudioFileInfo(codec, sampleRate, channels, SampleRate,
            codec.Contains("dsd", StringComparison.OrdinalIgnoreCase), container,
            TimeSpan.FromSeconds(Math.Max(0, duration)));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetimeCts.Cancel();
        StopPipeline();
        _completion.TrySetCanceled();
        _pipelineGate.Dispose();
        _lifetimeCts.Dispose();
    }
}
