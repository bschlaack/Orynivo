using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Orynivo.Audio;

/// <summary>
/// Streams uncompressed DFF/DSDIFF files from an HTTP range-capable endpoint as native DSD.
/// </summary>
public sealed class RemoteDffAudioPlayer : IAudioPlayer
{
    private const int ChunkHeaderSize = 12;
    private const int FormHeaderSize = 16;
    private const int TargetReadSize = 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly Uri _uri;
    private readonly SteinbergAsioStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _seekGate = new(1, 1);
    private readonly Task _pumpTask;
    private readonly long _dataStartPosition;
    private readonly long _dataEndPosition;
    private readonly int _channels;
    private readonly AudioFileInfo _info;
    private long _position;
    private volatile bool _paused;
    private bool _disposed;

    private RemoteDffAudioPlayer(
        HttpClient httpClient,
        Uri uri,
        SteinbergAsioStream stream,
        DffHeader header)
    {
        _httpClient = httpClient;
        _uri = uri;
        _stream = stream;
        _channels = header.Channels;
        _dataStartPosition = header.DataStartPosition;
        _dataEndPosition = header.DataEndPosition;
        _position = header.DataStartPosition;
        _info = header.FileInfo;
        _pumpTask = Task.Run(PumpAsync);
    }

    /// <summary>
    /// Opens a remote DFF stream, validates its chunk metadata, and starts native DSD playback.
    /// </summary>
    /// <param name="streamUrl">Authenticated HTTP stream URL for the DFF file.</param>
    /// <param name="backend">ASIO backend to use.</param>
    /// <param name="driverName">ASIO driver name.</param>
    /// <param name="cancellationToken">Cancellation token for initial probing.</param>
    /// <returns>The player and technical information for the remote DFF stream.</returns>
    public static async Task<(RemoteDffAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string streamUrl,
        OutputBackend backend,
        string driverName,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Remote native DFF playback requires an HTTP stream URL.");
        }

        var httpClient = new HttpClient();
        try
        {
            var header = await ReadHeaderAsync(httpClient, uri, cancellationToken);
            if (header.Channels != 2)
            {
                throw new NotSupportedException("Native DFF-Wiedergabe unterstützt derzeit nur Stereo-Dateien.");
            }

            var stream = await OpenNativeStreamWithRetryAsync(
                backend,
                driverName,
                header,
                cancellationToken);
            return (new RemoteDffAudioPlayer(httpClient, uri, stream, header), header.FileInfo);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public TimeSpan Duration => _info.Duration;

    /// <inheritdoc/>
    public TimeSpan Position => TimeSpan.FromSeconds(
        (double)Math.Max(0, Interlocked.Read(ref _position) - _dataStartPosition) * 8 / _channels / _info.SourceSampleRate);

    /// <inheritdoc/>
    public bool IsPaused => _paused;

    /// <inheritdoc/>
    public bool CanSeek => true;

    /// <inheritdoc/>
    public float Volume { get; set; } = 1.0f;

    /// <inheritdoc/>
    public float ReplayGainFactor
    {
        get => 1.0f;
        set { }
    }

    /// <inheritdoc/>
    public void Pause() => _paused = true;

    /// <inheritdoc/>
    public void Resume() => _paused = false;

    /// <inheritdoc/>
    public async Task SeekAsync(TimeSpan position)
    {
        var bytes = (long)(Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds) * _info.SourceSampleRate / 8) * _channels;
        bytes -= bytes % _channels;
        await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            Interlocked.Exchange(ref _position, _dataStartPosition + bytes);
            _stream.ClearBuffer();
        }
        finally
        {
            _seekGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task WaitForCompletionAsync() => await _pumpTask.ConfigureAwait(false);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _pumpTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        _stream.Dispose();
        _httpClient.Dispose();
        _seekGate.Dispose();
        _cts.Dispose();
        _disposed = true;
    }

    private async Task PumpAsync()
    {
        var buffer = new byte[TargetReadSize];

        while (!_cts.IsCancellationRequested)
        {
            if (_paused)
            {
                await Task.Delay(10, _cts.Token).ConfigureAwait(false);
                continue;
            }

            byte[] data;
            await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                var position = Interlocked.Read(ref _position);
                var remaining = _dataEndPosition - position;
                if (remaining <= 0)
                {
                    break;
                }

                var bytesToRead = (int)Math.Min(buffer.Length, remaining);
                bytesToRead -= bytesToRead % _channels;
                if (bytesToRead <= 0)
                {
                    break;
                }

                data = await ReadRangeAsync(_httpClient, _uri, position, bytesToRead, _cts.Token)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref _position, position + data.Length);
            }
            finally
            {
                _seekGate.Release();
            }

            ReverseBitsInPlace(data);
            var accepted = 0;
            while (accepted < data.Length && !_cts.IsCancellationRequested)
            {
                var written = _stream.WriteDsdInterleaved(data.AsSpan(accepted, data.Length - accepted));
                if (written < 0)
                {
                    throw new IOException();
                }

                if (written == 0)
                {
                    await Task.Delay(2, _cts.Token).ConfigureAwait(false);
                    continue;
                }

                accepted += written;
            }
        }
    }

    private static async Task<DffHeader> ReadHeaderAsync(
        HttpClient httpClient,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var formHeader = await ReadRangeAsync(httpClient, uri, 0, FormHeaderSize, cancellationToken);
        if (!formHeader.AsSpan(0, 4).SequenceEqual("FRM8"u8) ||
            !formHeader.AsSpan(12, 4).SequenceEqual("DSD "u8))
        {
            throw new InvalidOperationException("Keine gültige DFF/DSDIFF-Datei.");
        }

        var formSize = BinaryPrimitives.ReadInt64BigEndian(formHeader.AsSpan(4, 8));
        var formEnd = checked(12 + formSize);
        var position = (long)FormHeaderSize;
        int? sampleRate = null;
        int? channels = null;
        string? compressionType = null;
        long? dataStart = null;
        long? dataEnd = null;

        while (position + ChunkHeaderSize <= formEnd)
        {
            var chunkHeader = await ReadRangeAsync(httpClient, uri, position, ChunkHeaderSize, cancellationToken);
            var chunkId = Encoding.ASCII.GetString(chunkHeader, 0, 4);
            var chunkSize = BinaryPrimitives.ReadInt64BigEndian(chunkHeader.AsSpan(4, 8));
            var chunkDataStart = position + ChunkHeaderSize;
            var chunkDataEnd = checked(chunkDataStart + chunkSize);

            switch (chunkId)
            {
                case "PROP":
                    var property = await ReadPropertyChunkAsync(
                        httpClient,
                        uri,
                        chunkDataStart,
                        chunkDataEnd,
                        cancellationToken);
                    sampleRate ??= property.SampleRate;
                    channels ??= property.Channels;
                    compressionType ??= property.CompressionType;
                    break;
                case "DSD ":
                    dataStart = chunkDataStart;
                    dataEnd = chunkDataEnd;
                    break;
            }

            position = chunkDataEnd + ((chunkSize & 1) != 0 ? 1 : 0);
            if (dataStart.HasValue && sampleRate.HasValue && channels.HasValue && compressionType is not null)
            {
                break;
            }
        }

        if (!sampleRate.HasValue || !channels.HasValue || compressionType is null || !dataStart.HasValue || !dataEnd.HasValue)
        {
            throw new InvalidOperationException("DFF-Metadaten oder DSD-Datenchunk fehlen.");
        }

        SeekDiagnostics.Log(
            "remote-dff-player",
            $"header channels={channels.Value} sampleRate={sampleRate.Value} compression={compressionType} dataStart={dataStart.Value} dataEnd={dataEnd.Value}");
        if (!string.Equals(compressionType, "DSD ", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"DFF-Kompression '{compressionType}' wird nativ nicht unterstützt.");
        }

        return new DffHeader(
            new AudioFileInfo(
                "dsd_msbf",
                sampleRate.Value,
                channels.Value,
                sampleRate.Value,
                true,
                "dff",
                TimeSpan.FromSeconds((double)(dataEnd.Value - dataStart.Value) * 8 / channels.Value / sampleRate.Value)),
            channels.Value,
            dataStart.Value,
            dataEnd.Value);
    }

    private static async Task<DffPropertyInfo> ReadPropertyChunkAsync(
        HttpClient httpClient,
        Uri uri,
        long chunkStart,
        long chunkEnd,
        CancellationToken cancellationToken)
    {
        var propertyType = await ReadRangeAsync(httpClient, uri, chunkStart, 4, cancellationToken);
        if (!propertyType.AsSpan().SequenceEqual("SND "u8))
        {
            return new DffPropertyInfo(null, null, null);
        }

        var position = chunkStart + 4;
        int? sampleRate = null;
        int? channels = null;
        string? compressionType = null;
        while (position + ChunkHeaderSize <= chunkEnd)
        {
            var header = await ReadRangeAsync(httpClient, uri, position, ChunkHeaderSize, cancellationToken);
            var id = Encoding.ASCII.GetString(header, 0, 4);
            var size = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(4, 8));
            var dataStart = position + ChunkHeaderSize;
            var dataEnd = checked(dataStart + size);

            switch (id)
            {
                case "FS  ":
                    var rate = await ReadRangeAsync(httpClient, uri, dataStart, 4, cancellationToken);
                    sampleRate = BinaryPrimitives.ReadInt32BigEndian(rate);
                    break;
                case "CHNL":
                    var channelCount = await ReadRangeAsync(httpClient, uri, dataStart, 2, cancellationToken);
                    channels = BinaryPrimitives.ReadUInt16BigEndian(channelCount);
                    break;
                case "CMPR":
                    var compression = await ReadRangeAsync(httpClient, uri, dataStart, 4, cancellationToken);
                    compressionType = Encoding.ASCII.GetString(compression);
                    break;
            }

            position = dataEnd + ((size & 1) != 0 ? 1 : 0);
        }

        return new DffPropertyInfo(sampleRate, channels, compressionType);
    }

    private static async Task<SteinbergAsioStream> OpenNativeStreamWithRetryAsync(
        OutputBackend backend,
        string driverName,
        DffHeader header,
        CancellationToken cancellationToken)
    {
        const int attempts = 4;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            SteinbergAsioStream? stream = null;
            try
            {
                stream = new SteinbergAsioStream(
                    backend,
                    driverName,
                    header.FileInfo.OutputSampleRate,
                    header.Channels,
                    dsd: true);
                stream.Start();
                return stream;
            }
            catch (InvalidOperationException ex) when (IsDriverLoadRace(ex) && attempt < attempts)
            {
                stream?.Dispose();
                SeekDiagnostics.Log(
                    "remote-dff-player",
                    $"native-open-retry attempt={attempt} reason={ex.GetType().Name} message={ex.Message}");
                await Task.Delay(250 * attempt, cancellationToken);
            }
            catch
            {
                stream?.Dispose();
                throw;
            }
        }

        throw new InvalidOperationException("Could not open remote native DFF stream.");
    }

    private static bool IsDriverLoadRace(InvalidOperationException exception) =>
        exception.Message.Contains("open ASIO driver", StringComparison.OrdinalIgnoreCase) &&
        exception.Message.Contains("-2", StringComparison.Ordinal);

    private static async Task<byte[]> ReadRangeAsync(
        HttpClient httpClient,
        Uri uri,
        long start,
        int length,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(start, start + length - 1);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new InvalidOperationException("Remote native DFF playback requires HTTP byte-range support.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }

    private static void ReverseBitsInPlace(Span<byte> data)
    {
        for (var index = 0; index < data.Length; index++)
        {
            var value = data[index];
            value = (byte)(((value & 0xF0) >> 4) | ((value & 0x0F) << 4));
            value = (byte)(((value & 0xCC) >> 2) | ((value & 0x33) << 2));
            value = (byte)(((value & 0xAA) >> 1) | ((value & 0x55) << 1));
            data[index] = value;
        }
    }

    private sealed record DffHeader(AudioFileInfo FileInfo, int Channels, long DataStartPosition, long DataEndPosition);

    private sealed record DffPropertyInfo(int? SampleRate, int? Channels, string? CompressionType);
}
