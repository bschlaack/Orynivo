using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Orynivo.Audio;

/// <summary>
/// Plays local or HTTP-range-streamed uncompressed stereo DFF through direct
/// ALSA, preferring native DSD and falling back to DSD over PCM.
/// </summary>
public sealed class DffDopAudioPlayer : IAudioPlayer
{
    private const byte MarkerA = 0x05;
    private const byte MarkerB = 0xFA;
    private const int ReadSize = 1024 * 1024;

    private readonly IDffSource _source;
    private readonly nint _pcm;
    private readonly int _dsdSampleRate;
    private readonly bool _usesNativeDsd;
    private readonly long _dataStart;
    private readonly long _dataEnd;
    private readonly TimeSpan _duration;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _seekGate = new(1, 1);
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pumpTask;
    private long _position;
    private long _seekGeneration;
    private volatile bool _paused;
    private bool _disposed;

    private DffDopAudioPlayer(IDffSource source, nint pcm, DffHeader header, bool usesNativeDsd)
    {
        _source = source;
        _pcm = pcm;
        _dsdSampleRate = header.SampleRate;
        _usesNativeDsd = usesNativeDsd;
        _dataStart = header.DataStart;
        _dataEnd = header.DataEnd;
        _duration = TimeSpan.FromSeconds((double)(_dataEnd - _dataStart) * 8 / 2 / _dsdSampleRate);
        _position = _dataStart;
        _pumpTask = Task.Run(PumpAsync);
    }

    /// <summary>Opens a local uncompressed stereo DFF for direct ALSA DSD output.</summary>
    /// <param name="filePath">Absolute DFF file path.</param>
    /// <param name="deviceId">Direct ALSA device ID prefixed with <c>alsa:</c>.</param>
    /// <param name="cancellationToken">Cancellation token for startup.</param>
    /// <returns>The running player and its DSD source/output information.</returns>
    public static Task<(DffDopAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateLocalAsync(
        string filePath,
        string deviceId,
        CancellationToken cancellationToken = default) =>
        CreateAsync(new FileDffSource(filePath), deviceId, cancellationToken);

    /// <summary>Opens an HTTP range-capable uncompressed stereo DFF for direct ALSA DSD output.</summary>
    /// <param name="streamUrl">Authenticated HTTP stream URL.</param>
    /// <param name="deviceId">Direct ALSA device ID prefixed with <c>alsa:</c>.</param>
    /// <param name="cancellationToken">Cancellation token for startup.</param>
    /// <returns>The running player and its DSD source/output information.</returns>
    public static Task<(DffDopAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateRemoteAsync(
        string streamUrl,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Remote DFF playback requires an HTTP stream URL.");
        return CreateAsync(new HttpDffSource(uri), deviceId, cancellationToken);
    }

    private static async Task<(DffDopAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        IDffSource source,
        string deviceId,
        CancellationToken cancellationToken)
    {
        if (!deviceId.StartsWith("alsa:", StringComparison.Ordinal))
        {
            source.Dispose();
            throw new InvalidOperationException(Localization.LocalizationManager.Current.DopRequiresDirectAlsa);
        }

        nint pcm = nint.Zero;
        try
        {
            var header = await ReadHeaderAsync(source, cancellationToken).ConfigureAwait(false);
            var alsaDevice = deviceId["alsa:".Length..];
            var usesNativeDsd = true;
            try
            {
                pcm = AlsaNative.OpenExactNativeDsd(alsaDevice, header.SampleRate / 32);
            }
            catch (IOException)
            {
                usesNativeDsd = false;
                pcm = AlsaNative.OpenExact(alsaDevice, header.SampleRate / 16);
            }

            var player = new DffDopAudioPlayer(source, pcm, header, usesNativeDsd);
            await player._started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            var outputRate = header.SampleRate / (usesNativeDsd ? 32 : 16);
            var info = new AudioFileInfo(
                "dsd_msbf",
                header.SampleRate,
                2,
                outputRate,
                true,
                "dff",
                player.Duration);
            return (player, info);
        }
        catch
        {
            if (pcm != nint.Zero)
                AlsaNative.Close(pcm);
            source.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public TimeSpan Duration => _duration;

    /// <inheritdoc/>
    public TimeSpan Position => TimeSpan.FromSeconds(
        Math.Clamp((double)Math.Max(0, Interlocked.Read(ref _position) - _dataStart) * 8 / 2 / _dsdSampleRate,
            0, Duration.TotalSeconds));

    /// <inheritdoc/>
    public bool IsPaused => _paused;

    /// <inheritdoc/>
    public bool CanSeek => true;

    /// <summary>Gets whether ALSA opened native DSD instead of DoP.</summary>
    public bool UsesNativeDsd => _usesNativeDsd;

    /// <inheritdoc/>
    public float Volume { get => 1.0f; set { } }

    /// <inheritdoc/>
    public float ReplayGainFactor { get => 1.0f; set { } }

    /// <inheritdoc/>
    public void Pause() => _paused = true;

    /// <inheritdoc/>
    public void Resume() => _paused = false;

    /// <inheritdoc/>
    public async Task SeekAsync(TimeSpan position)
    {
        var offset = (long)(Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds) *
                            _dsdSampleRate / 8) * 2;
        offset -= offset % 8;
        await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            AlsaNative.Drop(_pcm);
            if (AlsaNative.Prepare(_pcm) < 0)
                throw new IOException(Localization.LocalizationManager.Current.AlsaPrepareFailed);
            Interlocked.Exchange(ref _position, Math.Min(_dataEnd, _dataStart + offset));
            Interlocked.Increment(ref _seekGeneration);
        }
        finally
        {
            _seekGate.Release();
        }
    }

    /// <inheritdoc/>
    public Task WaitForCompletionAsync() => _pumpTask;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        try { _pumpTask.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _seekGate.Dispose();
        _cts.Dispose();
    }

    private async Task PumpAsync()
    {
        var input = new byte[ReadSize];
        var outputBuffer = new byte[ReadSize * 2];
        var marker = MarkerA;
        try
        {
            _started.TrySetResult();
            while (!_cts.IsCancellationRequested)
            {
                if (_paused)
                {
                    await Task.Delay(5, _cts.Token).ConfigureAwait(false);
                    continue;
                }

                int bytesRead;
                long seekGeneration;
                await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
                try
                {
                    seekGeneration = Interlocked.Read(ref _seekGeneration);
                    var position = Interlocked.Read(ref _position);
                    var remaining = _dataEnd - position;
                    if (remaining < 8)
                        break;
                    var wanted = (int)Math.Min(input.Length, remaining);
                    wanted -= wanted % 8;
                    bytesRead = await _source.ReadAsync(position, input.AsMemory(0, wanted), _cts.Token)
                        .ConfigureAwait(false);
                    bytesRead -= bytesRead % 8;
                    Interlocked.Exchange(ref _position, position + bytesRead);
                }
                finally
                {
                    _seekGate.Release();
                }

                if (bytesRead == 0)
                    break;

                var output = 0;
                var bytesPerChannel = _usesNativeDsd ? 4 : 2;
                for (var source = 0; source + bytesPerChannel * 2 <= bytesRead; source += bytesPerChannel * 2)
                {
                    for (var channel = 0; channel < 2; channel++)
                    {
                        if (_usesNativeDsd)
                        {
                            outputBuffer[output++] = input[source + channel];
                            outputBuffer[output++] = input[source + channel + 2];
                            outputBuffer[output++] = input[source + channel + 4];
                            outputBuffer[output++] = input[source + channel + 6];
                        }
                        else
                        {
                            outputBuffer[output++] = 0;
                            outputBuffer[output++] = input[source + channel];
                            outputBuffer[output++] = input[source + channel + 2];
                            outputBuffer[output++] = marker;
                        }
                    }
                    if (!_usesNativeDsd)
                        marker = marker == MarkerA ? MarkerB : MarkerA;
                }

                if (seekGeneration != Interlocked.Read(ref _seekGeneration))
                {
                    marker = MarkerA;
                    continue;
                }

                var byteOffset = 0;
                while (byteOffset < output && !_cts.IsCancellationRequested)
                {
                    await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
                    int frames;
                    try
                    {
                        if (seekGeneration != Interlocked.Read(ref _seekGeneration))
                        {
                            marker = MarkerA;
                            break;
                        }
                        frames = AlsaNative.Write(_pcm, outputBuffer.AsSpan(byteOffset, output - byteOffset));
                    }
                    finally
                    {
                        _seekGate.Release();
                    }
                    if (frames > 0)
                        byteOffset += frames * 8;
                }
            }

            if (!_cts.IsCancellationRequested)
                AlsaNative.Drain(_pcm);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _started.TrySetException(ex);
            throw;
        }
        finally
        {
            AlsaNative.Drop(_pcm);
            AlsaNative.Close(_pcm);
            _source.Dispose();
        }
    }

    private static async Task<DffHeader> ReadHeaderAsync(IDffSource source, CancellationToken cancellationToken)
    {
        var form = new byte[16];
        await source.ReadExactlyAsync(0, form, cancellationToken).ConfigureAwait(false);
        if (!form.AsSpan(0, 4).SequenceEqual("FRM8"u8) ||
            !form.AsSpan(12, 4).SequenceEqual("DSD "u8))
            throw new InvalidDataException("The stream is not a valid DFF/DSDIFF file.");

        var formEnd = checked(12 + BinaryPrimitives.ReadInt64BigEndian(form.AsSpan(4, 8)));
        long position = 16;
        int? sampleRate = null;
        int? channels = null;
        string? compression = null;
        long? dataStart = null;
        long? dataEnd = null;
        var header = new byte[12];

        while (position + 12 <= formEnd)
        {
            await source.ReadExactlyAsync(position, header, cancellationToken).ConfigureAwait(false);
            var id = Encoding.ASCII.GetString(header, 0, 4);
            var size = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(4, 8));
            var start = position + 12;
            var end = checked(start + size);
            if (id == "PROP")
                (sampleRate, channels, compression) =
                    await ReadPropertiesAsync(source, start, end, cancellationToken).ConfigureAwait(false);
            else if (id == "DSD ")
                (dataStart, dataEnd) = (start, end);
            position = end + (size & 1);
        }

        if (sampleRate is null || channels is null || compression is null ||
            dataStart is null || dataEnd is null)
            throw new InvalidDataException("DFF metadata or DSD data chunk is missing.");
        if (channels != 2)
            throw new NotSupportedException("Direct ALSA DFF output currently supports stereo files only.");
        if (!string.Equals(compression, "DSD ", StringComparison.Ordinal))
            throw new NotSupportedException($"DFF compression '{compression}' is not supported.");
        return new DffHeader(sampleRate.Value, dataStart.Value, dataEnd.Value);
    }

    private static async Task<(int? Rate, int? Channels, string? Compression)> ReadPropertiesAsync(
        IDffSource source, long start, long end, CancellationToken cancellationToken)
    {
        var type = new byte[4];
        await source.ReadExactlyAsync(start, type, cancellationToken).ConfigureAwait(false);
        if (!type.AsSpan().SequenceEqual("SND "u8))
            return (null, null, null);

        int? rate = null;
        int? channels = null;
        string? compression = null;
        var header = new byte[12];
        var position = start + 4;
        while (position + 12 <= end)
        {
            await source.ReadExactlyAsync(position, header, cancellationToken).ConfigureAwait(false);
            var id = Encoding.ASCII.GetString(header, 0, 4);
            var size = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(4, 8));
            var dataStart = position + 12;
            if (id == "FS  ")
            {
                var data = new byte[4];
                await source.ReadExactlyAsync(dataStart, data, cancellationToken).ConfigureAwait(false);
                rate = BinaryPrimitives.ReadInt32BigEndian(data);
            }
            else if (id == "CHNL")
            {
                var data = new byte[2];
                await source.ReadExactlyAsync(dataStart, data, cancellationToken).ConfigureAwait(false);
                channels = BinaryPrimitives.ReadUInt16BigEndian(data);
            }
            else if (id == "CMPR")
            {
                var data = new byte[4];
                await source.ReadExactlyAsync(dataStart, data, cancellationToken).ConfigureAwait(false);
                compression = Encoding.ASCII.GetString(data);
            }
            position = checked(dataStart + size + (size & 1));
        }
        return (rate, channels, compression);
    }

    private sealed record DffHeader(int SampleRate, long DataStart, long DataEnd);

    internal interface IDffSource : IDisposable
    {
        Task<int> ReadAsync(long position, Memory<byte> destination, CancellationToken cancellationToken);
    }

    private sealed class FileDffSource : IDffSource
    {
        private readonly FileStream _file;

        internal FileDffSource(string path) =>
            _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        public async Task<int> ReadAsync(long position, Memory<byte> destination, CancellationToken cancellationToken)
        {
            _file.Position = position;
            return await _file.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() => _file.Dispose();
    }

    private sealed class HttpDffSource : IDffSource
    {
        private readonly HttpClient _client = new();
        private readonly Uri _uri;

        internal HttpDffSource(Uri uri) => _uri = uri;

        public async Task<int> ReadAsync(long position, Memory<byte> destination, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
            request.Headers.Range = new RangeHeaderValue(position, position + destination.Length - 1);
            using var response = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.PartialContent)
                throw new InvalidOperationException("Remote DFF playback requires HTTP byte-range support.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var read = 0;
            while (read < destination.Length)
            {
                var count = await stream.ReadAsync(destination[read..], cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    break;
                read += count;
            }
            return read;
        }

        public void Dispose() => _client.Dispose();
    }
}

internal static class DffSourceExtensions
{
    /// <summary>Reads the requested DFF range completely or throws for a truncated stream.</summary>
    /// <param name="source">Random-access DFF source.</param>
    /// <param name="position">Absolute byte offset.</param>
    /// <param name="destination">Destination buffer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task ReadExactlyAsync(
        this DffDopAudioPlayer.IDffSource source,
        long position,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = await source.ReadAsync(
                position + total,
                destination[total..],
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException();
            total += read;
        }
    }
}
