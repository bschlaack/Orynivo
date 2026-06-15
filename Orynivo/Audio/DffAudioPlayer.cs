using System.Buffers.Binary;
using System.IO;

namespace Orynivo.Audio;

public sealed class DffAudioPlayer : IAudioPlayer
{
    private readonly SteinbergAsioStream _stream;
    private readonly FileStream _file;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pumpTask;
    private readonly long _dataEndPosition;
    private readonly long _dataStartPosition;
    private readonly int _channels;
    private readonly AudioFileInfo _info;
    private bool _paused;
    private bool _disposed;

    private DffAudioPlayer(
        SteinbergAsioStream stream,
        FileStream file,
        int channels, long dataStartPosition, long dataEndPosition, AudioFileInfo info)
    {
        _stream = stream;
        _file = file;
        _channels = channels;
        _dataStartPosition = dataStartPosition;
        _dataEndPosition = dataEndPosition;
        _info = info;
        _pumpTask = Task.Run(PumpAsync);
    }

    public static async Task<(DffAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string filePath,
        OutputBackend backend,
        string driverName,
        CancellationToken cancellationToken = default)
    {
        var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var header = await ReadHeaderAsync(file, cancellationToken);
            if (header.Channels != 2)
            {
                throw new NotSupportedException("Native DFF-Wiedergabe unterstützt derzeit nur Stereo-Dateien.");
            }

            var stream = new SteinbergAsioStream(
                backend,
                driverName,
                header.FileInfo.OutputSampleRate,
                header.Channels,
                dsd: true);
            stream.Start();
            return (new DffAudioPlayer(stream, file, header.Channels, header.DataStartPosition, header.DataEndPosition, header.FileInfo), header.FileInfo);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public async Task WaitForCompletionAsync() => await _pumpTask.ConfigureAwait(false);
    public TimeSpan Duration => _info.Duration;
    public TimeSpan Position => TimeSpan.FromSeconds((double)Math.Max(0, _file.Position - _dataStartPosition) * 8 / _channels / _info.SourceSampleRate);
    public bool IsPaused => _paused;
    public bool CanSeek => true;
    public float Volume { get; set; } = 1.0f;
    public void Pause() => _paused = true;
    public void Resume() => _paused = false;
    public Task SeekAsync(TimeSpan position)
    {
        var bytes = (long)(Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds) * _info.SourceSampleRate / 8) * _channels;
        bytes -= bytes % _channels;
        _file.Position = _dataStartPosition + bytes;
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
        var buffer = new byte[64 * 1024];

        while (!_cts.IsCancellationRequested && _file.Position < _dataEndPosition)
        {
            if (_paused)
            {
                await Task.Delay(10, _cts.Token).ConfigureAwait(false);
                continue;
            }
            var remaining = _dataEndPosition - _file.Position;
            var bytesToRead = (int)Math.Min(buffer.Length, remaining);
            var bytesRead = await _file.ReadAsync(buffer.AsMemory(0, bytesToRead), _cts.Token).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            bytesRead -= bytesRead % _channels;
            ReverseBitsInPlace(buffer.AsSpan(0, bytesRead));

            var accepted = _stream.WriteDsdInterleaved(buffer.AsSpan(0, bytesRead));
            while (accepted < bytesRead && !_cts.IsCancellationRequested)
            {
                await Task.Delay(2, _cts.Token).ConfigureAwait(false);
                accepted += _stream.WriteDsdInterleaved(buffer.AsSpan(accepted, bytesRead - accepted));
            }
        }
    }

    private static async Task<DffHeader> ReadHeaderAsync(FileStream file, CancellationToken cancellationToken)
    {
        var formHeader = new byte[16];
        await file.ReadExactlyAsync(formHeader, cancellationToken).ConfigureAwait(false);
        if (!formHeader.AsSpan(0, 4).SequenceEqual("FRM8"u8) ||
            !formHeader.AsSpan(12, 4).SequenceEqual("DSD "u8))
        {
            throw new InvalidOperationException("Keine gültige DFF/DSDIFF-Datei.");
        }

        int? sampleRate = null;
        int? channels = null;
        string? compressionType = null;
        long? dataStart = null;
        long? dataEnd = null;

        while (file.Position + 12 <= file.Length)
        {
            var chunkHeader = new byte[12];
            await file.ReadExactlyAsync(chunkHeader, cancellationToken).ConfigureAwait(false);
            var chunkId = System.Text.Encoding.ASCII.GetString(chunkHeader, 0, 4);
            var chunkSize = BinaryPrimitives.ReadInt64BigEndian(chunkHeader.AsSpan(4, 8));
            var chunkDataStart = file.Position;
            var chunkDataEnd = checked(chunkDataStart + chunkSize);

            switch (chunkId)
            {
                case "PROP":
                    await ReadPropertyChunkAsync(file, chunkDataEnd, cancellationToken,
                        value => sampleRate = value,
                        value => channels = value,
                        value => compressionType = value).ConfigureAwait(false);
                    break;
                case "DSD ":
                    dataStart = chunkDataStart;
                    dataEnd = chunkDataEnd;
                    file.Position = chunkDataStart;
                    break;
                default:
                    file.Position = chunkDataEnd;
                    break;
            }

            if ((chunkSize & 1) != 0 && file.Position < file.Length)
            {
                file.Position++;
            }

            if (dataStart.HasValue && sampleRate.HasValue && channels.HasValue && compressionType is not null)
            {
                break;
            }
        }

        if (!sampleRate.HasValue || !channels.HasValue || compressionType is null || !dataStart.HasValue || !dataEnd.HasValue)
        {
            throw new InvalidOperationException("DFF-Metadaten oder DSD-Datenchunk fehlen.");
        }

        if (!string.Equals(compressionType, "DSD ", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"DFF-Kompression '{compressionType}' wird nativ nicht unterstützt.");
        }

        file.Position = dataStart.Value;
        return new DffHeader(
            new AudioFileInfo("dsd_msbf", sampleRate.Value, channels.Value, sampleRate.Value, true, "dff",
                TimeSpan.FromSeconds((double)(dataEnd.Value - dataStart.Value) * 8 / channels.Value / sampleRate.Value)),
            channels.Value, dataStart.Value, dataEnd.Value);
    }

    private static async Task ReadPropertyChunkAsync(
        FileStream file,
        long chunkEnd,
        CancellationToken cancellationToken,
        Action<int> setSampleRate,
        Action<int> setChannels,
        Action<string> setCompressionType)
    {
        var propertyType = new byte[4];
        await file.ReadExactlyAsync(propertyType, cancellationToken).ConfigureAwait(false);
        if (!propertyType.AsSpan().SequenceEqual("SND "u8))
        {
            file.Position = chunkEnd;
            return;
        }

        while (file.Position + 12 <= chunkEnd)
        {
            var header = new byte[12];
            await file.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            var id = System.Text.Encoding.ASCII.GetString(header, 0, 4);
            var size = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(4, 8));
            var dataStart = file.Position;
            var dataEnd = checked(dataStart + size);

            switch (id)
            {
                case "FS  ":
                    var rate = new byte[4];
                    await file.ReadExactlyAsync(rate, cancellationToken).ConfigureAwait(false);
                    setSampleRate(BinaryPrimitives.ReadInt32BigEndian(rate));
                    break;
                case "CHNL":
                    var channelCount = new byte[2];
                    await file.ReadExactlyAsync(channelCount, cancellationToken).ConfigureAwait(false);
                    setChannels(BinaryPrimitives.ReadUInt16BigEndian(channelCount));
                    break;
                case "CMPR":
                    var compression = new byte[4];
                    await file.ReadExactlyAsync(compression, cancellationToken).ConfigureAwait(false);
                    setCompressionType(System.Text.Encoding.ASCII.GetString(compression));
                    break;
            }

            file.Position = dataEnd;
            if ((size & 1) != 0 && file.Position < chunkEnd)
            {
                file.Position++;
            }
        }

        file.Position = chunkEnd;
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
}
