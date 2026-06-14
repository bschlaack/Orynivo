using System.Buffers.Binary;
using System.IO;

namespace Orynivo.Audio;

public sealed class DsfAudioPlayer : IAudioPlayer
{
    private const int DsfHeaderSize = 28;
    private const int FormatChunkSize = 52;

    private readonly SteinbergAsioStream _stream;
    private readonly FileStream _file;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pumpTask;
    private readonly int _channels;
    private readonly int _blockSizePerChannel;
    private readonly long _dataStartPosition;
    private readonly long _dataLength;
    private readonly AudioFileInfo _info;
    private bool _paused;
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

    public static async Task<(DsfAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string filePath,
        string driverName,
        CancellationToken cancellationToken = default)
    {
        var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var info = await ReadHeaderAsync(file, cancellationToken).ConfigureAwait(false);
            if (info.Channels != 2)
            {
                throw new NotSupportedException("Native DSD playback currently supports stereo DSF files only.");
            }

            var stream = new SteinbergAsioStream(driverName, info.FileInfo.OutputSampleRate, info.Channels, dsd: true);
            stream.Start();
            return (new DsfAudioPlayer(stream, file, info.Channels, info.BlockSizePerChannel, info.DataStartPosition, info.DataLength, info.FileInfo), info.FileInfo);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public async Task WaitForCompletionAsync()
    {
        await _pumpTask.ConfigureAwait(false);
    }
    public TimeSpan Duration => _info.Duration;
    public TimeSpan Position => TimeSpan.FromSeconds((double)Math.Max(0, _file.Position - _dataStartPosition) * 8 / _channels / _info.SourceSampleRate);
    public bool IsPaused => _paused;
    public bool CanSeek => true;
    public float Volume { get; set; } = 1.0f;
    public void Pause() => _paused = true;
    public void Resume() => _paused = false;
    public Task SeekAsync(TimeSpan position)
    {
        var byteOffsetPerChannel = (long)(Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds) * _info.SourceSampleRate / 8);
        byteOffsetPerChannel -= byteOffsetPerChannel % _blockSizePerChannel;
        _file.Position = _dataStartPosition + (byteOffsetPerChannel * _channels);
        return Task.CompletedTask;
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
        _cts.Dispose();
        _disposed = true;
    }

    private async Task PumpAsync()
    {
        var planarBlock = new byte[_blockSizePerChannel * _channels];
        var interleaved = new byte[planarBlock.Length];

        while (!_cts.IsCancellationRequested)
        {
            if (_paused)
            {
                await Task.Delay(10, _cts.Token).ConfigureAwait(false);
                continue;
            }
            var bytesRead = await _file.ReadAsync(planarBlock, _cts.Token).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            var completeChannelBytes = bytesRead / _channels;
            if (completeChannelBytes == 0)
            {
                break;
            }

            for (var byteIndex = 0; byteIndex < completeChannelBytes; byteIndex++)
            {
                for (var channel = 0; channel < _channels; channel++)
                {
                    interleaved[(byteIndex * _channels) + channel] =
                        planarBlock[(channel * _blockSizePerChannel) + byteIndex];
                }
            }

            var usableBytes = completeChannelBytes * _channels;
            var accepted = _stream.WriteDsdInterleaved(interleaved.AsSpan(0, usableBytes));
            while (accepted < usableBytes && !_cts.IsCancellationRequested)
            {
                await Task.Delay(2, _cts.Token).ConfigureAwait(false);
                accepted += _stream.WriteDsdInterleaved(interleaved.AsSpan(accepted, usableBytes - accepted));
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
