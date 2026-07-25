using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;

namespace Orynivo.Audio;

/// <summary>
/// Streams a stereo DSF file from an HTTP range-capable endpoint and sends its
/// unchanged DSD payload to a direct ALSA device in DoP marker frames.
/// </summary>
public sealed class RemoteDsfDopAudioPlayer : IAudioPlayer
{
    private const int InitialHeaderSize = 92;
    private const int TargetReadSize = 1024 * 1024;
    private const byte MarkerA = 0x05;
    private const byte MarkerB = 0xFA;

    private readonly HttpClient _httpClient;
    private readonly Uri _uri;
    private readonly nint _pcm;
    private readonly int _dsdSampleRate;
    private readonly int _blockSizePerChannel;
    private readonly bool _isLeastSignificantBitFirst;
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

    private RemoteDsfDopAudioPlayer(
        HttpClient httpClient,
        Uri uri,
        nint pcm,
        DsfHeader header,
        bool usesNativeDsd)
    {
        _httpClient = httpClient;
        _uri = uri;
        _pcm = pcm;
        _dsdSampleRate = header.DsdSampleRate;
        _blockSizePerChannel = header.BlockSizePerChannel;
        _isLeastSignificantBitFirst = header.IsLeastSignificantBitFirst;
        _usesNativeDsd = usesNativeDsd;
        _dataStart = header.DataStart;
        _dataEnd = header.DataStart + header.DataLength;
        _duration = header.Info.Duration;
        _position = header.DataStart;
        _pumpTask = Task.Run(PumpAsync);
    }

    /// <summary>Opens a remote stereo DSF stream for bit-perfect DoP output through direct ALSA.</summary>
    /// <param name="streamUrl">Authenticated HTTP stream URL for the DSF file.</param>
    /// <param name="deviceId">Direct ALSA device ID prefixed with <c>alsa:</c>.</param>
    /// <param name="cancellationToken">Cancellation token for startup and header probing.</param>
    /// <returns>The running DoP player and its DSD source/output information.</returns>
    public static async Task<(RemoteDsfDopAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string streamUrl,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!deviceId.StartsWith("alsa:", StringComparison.Ordinal))
            throw new InvalidOperationException(Localization.LocalizationManager.Current.DopRequiresDirectAlsa);
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Remote DoP playback requires an HTTP stream URL.");

        var httpClient = new HttpClient();
        nint pcm = nint.Zero;
        try
        {
            var headerBytes = await ReadRangeAsync(
                httpClient,
                uri,
                0,
                InitialHeaderSize,
                cancellationToken).ConfigureAwait(false);
            var header = ReadHeader(headerBytes);
            var alsaDevice = deviceId["alsa:".Length..];
            var usesNativeDsd = true;
            try
            {
                pcm = AlsaNative.OpenExactNativeDsd(alsaDevice, header.DsdSampleRate / 32);
            }
            catch (IOException)
            {
                usesNativeDsd = false;
                pcm = AlsaNative.OpenExact(alsaDevice, header.DopSampleRate);
            }
            var player = new RemoteDsfDopAudioPlayer(httpClient, uri, pcm, header, usesNativeDsd);
            await player._started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            var info = usesNativeDsd
                ? header.Info with { OutputSampleRate = header.DsdSampleRate / 32 }
                : header.Info;
            return (player, info);
        }
        catch
        {
            if (pcm != nint.Zero)
                AlsaNative.Close(pcm);
            httpClient.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public TimeSpan Duration => _duration;

    /// <inheritdoc/>
    public TimeSpan Position => TimeSpan.FromSeconds(
        Math.Clamp(
            (double)Math.Max(0, Interlocked.Read(ref _position) - _dataStart) * 8 / 2 / _dsdSampleRate,
            0,
            Duration.TotalSeconds));

    /// <inheritdoc/>
    public bool IsPaused => _paused;

    /// <inheritdoc/>
    public bool CanSeek => true;

    /// <summary>Gets whether ALSA opened the DAC's native DSD format instead of DoP.</summary>
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
        var byteOffsetPerChannel =
            (long)(Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds) * _dsdSampleRate / 8);
        byteOffsetPerChannel -= byteOffsetPerChannel % _blockSizePerChannel;
        await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            AlsaNative.Drop(_pcm);
            if (AlsaNative.Prepare(_pcm) < 0)
                throw new IOException(Localization.LocalizationManager.Current.AlsaPrepareFailed);
            Interlocked.Exchange(ref _position, _dataStart + byteOffsetPerChannel * 2);
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
        var blocksPerRead = Math.Max(1, TargetReadSize / (_blockSizePerChannel * 2));
        var readSize = blocksPerRead * _blockSizePerChannel * 2;
        var dop = new byte[readSize * 2];
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

                byte[] planar;
                long seekGeneration;
                await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
                try
                {
                    seekGeneration = Interlocked.Read(ref _seekGeneration);
                    var position = Interlocked.Read(ref _position);
                    var remaining = _dataEnd - position;
                    var blockBytes = _blockSizePerChannel * 2;
                    if (remaining < blockBytes)
                        break;
                    var bytesToRead = (int)Math.Min(readSize, remaining - remaining % blockBytes);
                    planar = await ReadRangeAsync(
                        _httpClient,
                        _uri,
                        position,
                        bytesToRead,
                        _cts.Token).ConfigureAwait(false);
                    Interlocked.Exchange(ref _position, position + planar.Length);
                }
                finally
                {
                    _seekGate.Release();
                }

                var output = 0;
                var bytesPerSample = _usesNativeDsd ? 4 : 2;
                var blockBytesRead = _blockSizePerChannel * 2;
                for (var blockOffset = 0; blockOffset < planar.Length; blockOffset += blockBytesRead)
                {
                    for (var byteIndex = 0;
                         byteIndex + bytesPerSample - 1 < _blockSizePerChannel;
                         byteIndex += bytesPerSample)
                    {
                        for (var channel = 0; channel < 2; channel++)
                        {
                            var source = blockOffset + channel * _blockSizePerChannel + byteIndex;
                            if (_usesNativeDsd)
                            {
                                dop[output++] = DsfPayload.ToAlsa(planar[source], _isLeastSignificantBitFirst);
                                dop[output++] = DsfPayload.ToAlsa(planar[source + 1], _isLeastSignificantBitFirst);
                                dop[output++] = DsfPayload.ToAlsa(planar[source + 2], _isLeastSignificantBitFirst);
                                dop[output++] = DsfPayload.ToAlsa(planar[source + 3], _isLeastSignificantBitFirst);
                            }
                            else
                            {
                                dop[output++] = 0;
                                dop[output++] = DsfPayload.ToAlsa(planar[source], _isLeastSignificantBitFirst);
                                dop[output++] = DsfPayload.ToAlsa(planar[source + 1], _isLeastSignificantBitFirst);
                                dop[output++] = marker;
                            }
                        }
                        if (!_usesNativeDsd)
                            marker = marker == MarkerA ? MarkerB : MarkerA;
                    }
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
                        frames = AlsaNative.Write(_pcm, dop.AsSpan(byteOffset, output - byteOffset));
                    }
                    finally
                    {
                        _seekGate.Release();
                    }
                    if (frames == 0)
                        continue;
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
            _httpClient.Dispose();
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
            throw new InvalidOperationException("Remote DoP playback requires HTTP byte-range support.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }

    private static DsfHeader ReadHeader(byte[] header)
    {
        if (!header.AsSpan(0, 4).SequenceEqual("DSD "u8))
            throw new InvalidDataException("The remote stream is not a valid DSF file.");

        var format = header.AsSpan(28, 52);
        if (!format[..4].SequenceEqual("fmt "u8))
            throw new InvalidDataException("The DSF format chunk is missing.");

        var channels = BinaryPrimitives.ReadInt32LittleEndian(format.Slice(24, 4));
        var dsdRate = BinaryPrimitives.ReadInt32LittleEndian(format.Slice(28, 4));
        var bitsPerSample = BinaryPrimitives.ReadInt32LittleEndian(format.Slice(32, 4));
        var sampleCount = BinaryPrimitives.ReadInt64LittleEndian(format.Slice(36, 8));
        var blockSize = BinaryPrimitives.ReadInt32LittleEndian(format.Slice(44, 4));
        if (channels != 2)
            throw new NotSupportedException("Remote DoP output currently supports stereo DSF files only.");
        if (bitsPerSample is not 1 and not 8 || dsdRate <= 0 || dsdRate % 16 != 0 || blockSize <= 0)
            throw new NotSupportedException("The remote DSF stream cannot be represented as standard DoP.");

        var dataHeader = header.AsSpan(80, 12);
        if (!dataHeader[..4].SequenceEqual("data"u8))
            throw new InvalidDataException("The DSF data chunk is missing.");
        var dataLength = BinaryPrimitives.ReadInt64LittleEndian(dataHeader.Slice(4, 8)) - 12;
        var dopRate = dsdRate / 16;
        var info = new AudioFileInfo(
            "dsd_lsbf",
            dsdRate,
            channels,
            dopRate,
            true,
            "dsf",
            TimeSpan.FromSeconds(sampleCount / (double)dsdRate));
        return new DsfHeader(
            info,
            dsdRate,
            dopRate,
            blockSize,
            bitsPerSample == 1,
            InitialHeaderSize,
            dataLength);
    }

    private sealed record DsfHeader(
        AudioFileInfo Info,
        int DsdSampleRate,
        int DopSampleRate,
        int BlockSizePerChannel,
        bool IsLeastSignificantBitFirst,
        long DataStart,
        long DataLength);
}
