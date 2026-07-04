using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;

namespace Orynivo.Audio;

/// <summary>
/// Streams DSF files from an HTTP range-capable endpoint as native DSD via <see cref="SteinbergAsioStream"/>.
/// </summary>
public sealed class RemoteDsfAudioPlayer : IAudioPlayer
{
    private const int DsfHeaderSize = 28;
    private const int FormatChunkSize = 52;
    private const int DataChunkHeaderSize = 12;
    private const int InitialHeaderSize = DsfHeaderSize + FormatChunkSize + DataChunkHeaderSize;
    private const int TargetReadSize = 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly Uri _uri;
    private readonly SteinbergAsioStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _seekGate = new(1, 1);
    private readonly Task _pumpTask;
    private readonly int _channels;
    private readonly int _blockSizePerChannel;
    private readonly long _dataStartPosition;
    private readonly long _dataEndPosition;
    private readonly AudioFileInfo _info;
    private long _position;
    private volatile bool _paused;
    private bool _disposed;

    private RemoteDsfAudioPlayer(
        HttpClient httpClient,
        Uri uri,
        SteinbergAsioStream stream,
        DsfHeader header)
    {
        _httpClient = httpClient;
        _uri = uri;
        _stream = stream;
        _channels = header.Channels;
        _blockSizePerChannel = header.BlockSizePerChannel;
        _dataStartPosition = header.DataStartPosition;
        _dataEndPosition = header.DataStartPosition + header.DataLength;
        _position = header.DataStartPosition;
        _info = header.FileInfo;
        _pumpTask = Task.Run(PumpAsync);
    }

    /// <summary>
    /// Opens a remote DSF stream, validates its header through HTTP range reads, and starts native DSD playback.
    /// </summary>
    /// <param name="streamUrl">Authenticated HTTP stream URL for the DSF file.</param>
    /// <param name="backend">ASIO backend to use (<see cref="OutputBackend.Asio"/> or <see cref="OutputBackend.CwAsio"/>).</param>
    /// <param name="driverName">Name of the ASIO driver as returned by <see cref="SteinbergAsioStream.GetDriverNames"/>.</param>
    /// <param name="cancellationToken">Cancellation token for initial header probing.</param>
    /// <returns>The player and technical information for the remote DSF stream.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the endpoint does not support byte-range streaming or the response is not a DSF stream.</exception>
    /// <exception cref="NotSupportedException">Thrown for non-stereo DSF files.</exception>
    public static async Task<(RemoteDsfAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string streamUrl,
        OutputBackend backend,
        string driverName,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Remote native DSF playback requires an HTTP stream URL.");
        }

        var httpClient = new HttpClient();
        try
        {
            var headerBytes = await ReadRangeAsync(httpClient, uri, 0, InitialHeaderSize, cancellationToken);
            var header = ReadHeader(headerBytes);
            if (header.Channels != 2)
            {
                throw new NotSupportedException("Native DSD playback currently supports stereo DSF files only.");
            }

            var stream = await OpenNativeStreamWithRetryAsync(
                backend,
                driverName,
                header,
                cancellationToken);
            return (new RemoteDsfAudioPlayer(httpClient, uri, stream, header), header.FileInfo);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    private static async Task<SteinbergAsioStream> OpenNativeStreamWithRetryAsync(
        OutputBackend backend,
        string driverName,
        DsfHeader header,
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
                    "remote-dsf-player",
                    $"native-open-retry attempt={attempt} reason={ex.GetType().Name} message={ex.Message}");
                await Task.Delay(250 * attempt, cancellationToken);
            }
            catch
            {
                stream?.Dispose();
                throw;
            }
        }

        throw new InvalidOperationException("Could not open remote native DSF stream.");
    }

    private static bool IsDriverLoadRace(InvalidOperationException exception) =>
        exception.Message.Contains("open ASIO driver", StringComparison.OrdinalIgnoreCase) &&
        exception.Message.Contains("-2", StringComparison.Ordinal);

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
        var byteOffsetPerChannel = (long)(Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds) * _info.SourceSampleRate / 8);
        byteOffsetPerChannel -= byteOffsetPerChannel % _blockSizePerChannel;
        await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            Interlocked.Exchange(ref _position, _dataStartPosition + (byteOffsetPerChannel * _channels));
            _stream.ClearBuffer();
        }
        finally
        {
            _seekGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task WaitForCompletionAsync()
    {
        await _pumpTask.ConfigureAwait(false);
    }

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
        var blocksPerRead = Math.Max(1, TargetReadSize / (_blockSizePerChannel * _channels));
        var readSize = blocksPerRead * _blockSizePerChannel * _channels;
        var interleaved = new byte[readSize];

        while (!_cts.IsCancellationRequested)
        {
            if (_paused)
            {
                await Task.Delay(10, _cts.Token).ConfigureAwait(false);
                continue;
            }

            long position;
            byte[] planar;
            await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                position = Interlocked.Read(ref _position);
                var remaining = _dataEndPosition - position;
                if (remaining < _blockSizePerChannel * _channels)
                {
                    break;
                }

                var bytesToRead = (int)Math.Min(readSize, remaining - (remaining % (_blockSizePerChannel * _channels)));
                planar = await ReadRangeAsync(_httpClient, _uri, position, bytesToRead, _cts.Token)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref _position, position + planar.Length);
            }
            finally
            {
                _seekGate.Release();
            }

            var blockSize = _blockSizePerChannel * _channels;
            for (var blockOffset = 0; blockOffset < planar.Length; blockOffset += blockSize)
            {
                for (var byteIndex = 0; byteIndex < _blockSizePerChannel; byteIndex++)
                {
                    for (var channel = 0; channel < _channels; channel++)
                    {
                        interleaved[blockOffset + (byteIndex * _channels) + channel] =
                            planar[blockOffset + (channel * _blockSizePerChannel) + byteIndex];
                    }
                }
            }

            var accepted = 0;
            while (accepted < planar.Length && !_cts.IsCancellationRequested)
            {
                var written = _stream.WriteDsdInterleaved(interleaved.AsSpan(accepted, planar.Length - accepted));
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
            throw new InvalidOperationException("Remote native DSF playback requires HTTP byte-range support.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }

    private static DsfHeader ReadHeader(byte[] header)
    {
        if (!header.AsSpan(0, 4).SequenceEqual("DSD "u8))
        {
            throw new InvalidOperationException("Keine gültige DSF-Datei.");
        }

        var fmt = header.AsSpan(DsfHeaderSize, FormatChunkSize);
        if (!fmt[..4].SequenceEqual("fmt "u8))
        {
            throw new InvalidOperationException("DSF fmt-Chunk fehlt.");
        }

        var channelCount = BinaryPrimitives.ReadInt32LittleEndian(fmt.Slice(24, 4));
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(fmt.Slice(28, 4));
        var bitsPerSample = BinaryPrimitives.ReadInt32LittleEndian(fmt.Slice(32, 4));
        var blockSizePerChannel = BinaryPrimitives.ReadInt32LittleEndian(fmt.Slice(44, 4));
        SeekDiagnostics.Log(
            "remote-dsf-player",
            $"header channels={channelCount} sampleRate={sampleRate} bitsPerSample={bitsPerSample} blockSizePerChannel={blockSizePerChannel}");
        if (bitsPerSample is not 1 and not 8)
        {
            throw new NotSupportedException($"DSF-Bittiefe {bitsPerSample} wird nicht unterstützt.");
        }

        var dataHeader = header.AsSpan(DsfHeaderSize + FormatChunkSize, DataChunkHeaderSize);
        if (!dataHeader[..4].SequenceEqual("data"u8))
        {
            throw new InvalidOperationException("DSF data-Chunk fehlt.");
        }

        var dataStart = InitialHeaderSize;
        var dataSize = BinaryPrimitives.ReadInt64LittleEndian(dataHeader.Slice(4, 8)) - DataChunkHeaderSize;
        var duration = TimeSpan.FromSeconds((double)dataSize * 8 / channelCount / sampleRate);
        return new DsfHeader(
            new AudioFileInfo("dsd_lsbf", sampleRate, channelCount, sampleRate, true, "dsf", duration),
            channelCount,
            blockSizePerChannel,
            dataStart,
            dataSize);
    }

    private sealed record DsfHeader(AudioFileInfo FileInfo, int Channels, int BlockSizePerChannel, long DataStartPosition, long DataLength);
}
