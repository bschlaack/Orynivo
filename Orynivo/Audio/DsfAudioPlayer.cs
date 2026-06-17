using System.Buffers.Binary;
using System.IO;

namespace Orynivo.Audio;

/// <summary>
/// Plays DSF files as native DSD via <see cref="SteinbergAsioStream"/>.
/// Reads the DSF header, de-interleaves the planar per-channel block layout into
/// interleaved DSD bytes, and streams them directly to the ASIO driver.
/// Use <see cref="CreateAsync"/> to construct an instance.
/// </summary>
public sealed class DsfAudioPlayer : IAudioPlayer
{
    private const int DsfHeaderSize = 28;
    private const int FormatChunkSize = 52;

    private readonly SteinbergAsioStream _stream;
    private readonly FileStream _file;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _seekGate = new(1, 1);
    private readonly Task _pumpTask;
    private readonly int _channels;
    private readonly int _blockSizePerChannel;
    private readonly long _dataStartPosition;
    private readonly long _dataLength;
    private readonly AudioFileInfo _info;
    private volatile bool _paused;
    private bool _disposed;

    private DsfAudioPlayer(
        SteinbergAsioStream stream,
        FileStream file,
        int channels, int blockSizePerChannel, long dataStartPosition, long dataLength, AudioFileInfo info)
    {
        _stream = stream;
        _file = file;
        _channels = channels;
        _blockSizePerChannel = blockSizePerChannel;
        _dataStartPosition = dataStartPosition;
        _dataLength = dataLength;
        _info = info;
        _pumpTask = Task.Run(PumpAsync);
    }

    /// <summary>
    /// Opens the DSF file, parses its header chunks, initialises an ASIO stream in DSD mode,
    /// and returns the ready-to-play player together with the probed file info.
    /// </summary>
    /// <param name="filePath">Absolute path to the <c>.dsf</c> file.</param>
    /// <param name="backend">ASIO backend to use (<see cref="OutputBackend.Asio"/> or <see cref="OutputBackend.CwAsio"/>).</param>
    /// <param name="driverName">Name of the ASIO driver as returned by <see cref="SteinbergAsioStream.GetDriverNames"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the async header read.</param>
    /// <exception cref="NotSupportedException">Thrown for non-stereo DSF files.</exception>
    public static async Task<(DsfAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string filePath,
        OutputBackend backend,
        string driverName,
        CancellationToken cancellationToken = default)
    {
        var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var info = await ReadHeaderAsync(file, cancellationToken);
            if (info.Channels != 2)
            {
                throw new NotSupportedException("Native DSD playback currently supports stereo DSF files only.");
            }

            var stream = new SteinbergAsioStream(
                backend,
                driverName,
                info.FileInfo.OutputSampleRate,
                info.Channels,
                dsd: true);
            stream.Start();
            return (new DsfAudioPlayer(stream, file, info.Channels, info.BlockSizePerChannel, info.DataStartPosition, info.DataLength, info.FileInfo), info.FileInfo);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task WaitForCompletionAsync()
    {
        await _pumpTask.ConfigureAwait(false);
    }
    /// <inheritdoc/>
    public TimeSpan Duration => _info.Duration;
    /// <inheritdoc/>
    public TimeSpan Position => TimeSpan.FromSeconds((double)Math.Max(0, _file.Position - _dataStartPosition) * 8 / _channels / _info.SourceSampleRate);
    /// <inheritdoc/>
    public bool IsPaused => _paused;
    /// <inheritdoc/>
    public bool CanSeek => true;
    /// <inheritdoc/>
    public float Volume { get; set; } = 1.0f;
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
            _file.Position = _dataStartPosition + (byteOffsetPerChannel * _channels);
        }
        finally
        {
            _seekGate.Release();
        }
    }

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

        _file.Dispose();
        _stream.Dispose();
        _seekGate.Dispose();
        _cts.Dispose();
        _disposed = true;
    }

    private async Task PumpAsync()
    {
        var planarBlock = new byte[_blockSizePerChannel * _channels];
        var interleaved = new byte[planarBlock.Length];
        var dataEndPosition = _dataStartPosition + _dataLength;

        while (!_cts.IsCancellationRequested && _file.Position < dataEndPosition)
        {
            if (_paused)
            {
                await Task.Delay(10, _cts.Token).ConfigureAwait(false);
                continue;
            }

            await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
            int bytesRead;
            try
            {
                if (dataEndPosition - _file.Position < planarBlock.Length)
                    break;
                bytesRead = await _file.ReadAtLeastAsync(
                    planarBlock,
                    planarBlock.Length,
                    throwOnEndOfStream: false,
                    _cts.Token).ConfigureAwait(false);
            }
            finally
            {
                _seekGate.Release();
            }

            if (bytesRead != planarBlock.Length)
            {
                break;
            }

            for (var byteIndex = 0; byteIndex < _blockSizePerChannel; byteIndex++)
            {
                for (var channel = 0; channel < _channels; channel++)
                {
                    interleaved[(byteIndex * _channels) + channel] =
                        planarBlock[(channel * _blockSizePerChannel) + byteIndex];
                }
            }

            var accepted = 0;
            while (accepted < interleaved.Length && !_cts.IsCancellationRequested)
            {
                var written = _stream.WriteDsdInterleaved(interleaved.AsSpan(accepted));
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

    private static async Task<DsfHeader> ReadHeaderAsync(FileStream file, CancellationToken cancellationToken)
    {
        var header = new byte[DsfHeaderSize];
        await file.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if (!header.AsSpan(0, 4).SequenceEqual("DSD "u8))
        {
            throw new InvalidOperationException("Keine gültige DSF-Datei.");
        }

        var fmt = new byte[FormatChunkSize];
        await file.ReadExactlyAsync(fmt, cancellationToken).ConfigureAwait(false);
        if (!fmt.AsSpan(0, 4).SequenceEqual("fmt "u8))
        {
            throw new InvalidOperationException("DSF fmt-Chunk fehlt.");
        }

        var channelCount = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(24, 4));
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(28, 4));
        var bitsPerSample = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(32, 4));
        var blockSizePerChannel = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(44, 4));
        if (bitsPerSample != 1)
        {
            throw new NotSupportedException($"DSF-Bittiefe {bitsPerSample} wird nicht unterstützt.");
        }

        var dataHeader = new byte[12];
        await file.ReadExactlyAsync(dataHeader, cancellationToken).ConfigureAwait(false);
        if (!dataHeader.AsSpan(0, 4).SequenceEqual("data"u8))
        {
            throw new InvalidOperationException("DSF data-Chunk fehlt.");
        }

        var dataStart = file.Position;
        var dataSize = BinaryPrimitives.ReadInt64LittleEndian(dataHeader.AsSpan(4, 8)) - 12;
        var duration = TimeSpan.FromSeconds((double)dataSize * 8 / channelCount / sampleRate);
        return new DsfHeader(
            new AudioFileInfo("dsd_lsbf", sampleRate, channelCount, sampleRate, true, "dsf", duration),
            channelCount, blockSizePerChannel, dataStart, dataSize);
    }

    private sealed record DsfHeader(AudioFileInfo FileInfo, int Channels, int BlockSizePerChannel, long DataStartPosition, long DataLength);
}
