using System.Buffers.Binary;

namespace Orynivo.Audio;

/// <summary>
/// Plays a stereo DSF file through direct ALSA by encapsulating the unchanged
/// DSD payload in standard DSD-over-PCM marker frames.
/// </summary>
public sealed class DsfDopAudioPlayer : IAudioPlayer
{
    private const int DsfHeaderSize = 28;
    private const int FormatChunkSize = 52;
    private const byte MarkerA = 0x05;
    private const byte MarkerB = 0xFA;

    private readonly FileStream _file;
    private readonly nint _pcm;
    private readonly int _dsdSampleRate;
    private readonly int _blockSizePerChannel;
    private readonly bool _isLeastSignificantBitFirst;
    private readonly bool _usesNativeDsd;
    private readonly long _dataStart;
    private readonly long _dataLength;
    private readonly TimeSpan _duration;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _seekGate = new(1, 1);
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pumpTask;
    private long _seekGeneration;
    private volatile bool _paused;
    private bool _disposed;

    private DsfDopAudioPlayer(FileStream file, nint pcm, DsfHeader header, bool usesNativeDsd)
    {
        _file = file;
        _pcm = pcm;
        _dsdSampleRate = header.DsdSampleRate;
        _blockSizePerChannel = header.BlockSizePerChannel;
        _isLeastSignificantBitFirst = header.IsLeastSignificantBitFirst;
        _usesNativeDsd = usesNativeDsd;
        _dataStart = header.DataStart;
        _dataLength = header.DataLength;
        _duration = header.Info.Duration;
        _pumpTask = Task.Run(PumpAsync);
    }

    /// <summary>Opens a local stereo DSF file for bit-perfect DoP output through direct ALSA.</summary>
    /// <param name="filePath">Absolute DSF file path.</param>
    /// <param name="deviceId">Direct ALSA device ID prefixed with <c>alsa:</c>.</param>
    /// <param name="cancellationToken">Cancellation token for startup.</param>
    /// <returns>The running DoP player and its DSD source/output information.</returns>
    public static async Task<(DsfDopAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string filePath,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!deviceId.StartsWith("alsa:", StringComparison.Ordinal))
            throw new InvalidOperationException(Localization.LocalizationManager.Current.DopRequiresDirectAlsa);

        var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        nint pcm = nint.Zero;
        try
        {
            var header = await ReadHeaderAsync(file, cancellationToken).ConfigureAwait(false);
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
            var player = new DsfDopAudioPlayer(file, pcm, header, usesNativeDsd);
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
            file.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public TimeSpan Duration => _duration;

    /// <inheritdoc/>
    public TimeSpan Position => TimeSpan.FromSeconds(
        Math.Clamp(
            (double)Math.Max(0, _file.Position - _dataStart) * 8 / 2 / _dsdSampleRate,
            0,
            Duration.TotalSeconds));

    /// <inheritdoc/>
    public bool IsPaused => _paused;

    /// <inheritdoc/>
    public bool CanSeek => true;

    /// <summary>Gets whether ALSA opened the DAC's native DSD format instead of DoP.</summary>
    public bool UsesNativeDsd => _usesNativeDsd;

    /// <inheritdoc/>
    public float Volume
    {
        get => 1.0f;
        set { }
    }

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
        var byteOffsetPerChannel =
            (long)(Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds) * _dsdSampleRate / 8);
        byteOffsetPerChannel -= byteOffsetPerChannel % _blockSizePerChannel;
        await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            AlsaNative.Drop(_pcm);
            if (AlsaNative.Prepare(_pcm) < 0)
                throw new IOException(Localization.LocalizationManager.Current.AlsaPrepareFailed);
            _file.Position = _dataStart + byteOffsetPerChannel * 2;
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
        var planar = new byte[_blockSizePerChannel * 2];
        var outputBuffer = new byte[_blockSizePerChannel * 4];
        var dataEnd = _dataStart + _dataLength;
        var marker = MarkerA;

        try
        {
            _started.TrySetResult();
            while (!_cts.IsCancellationRequested && _file.Position < dataEnd)
            {
                if (_paused)
                {
                    await Task.Delay(5, _cts.Token).ConfigureAwait(false);
                    continue;
                }

                await _seekGate.WaitAsync(_cts.Token).ConfigureAwait(false);
                int bytesRead;
                long seekGeneration;
                try
                {
                    seekGeneration = Interlocked.Read(ref _seekGeneration);
                    var remaining = dataEnd - _file.Position;
                    var wanted = (int)Math.Min(planar.Length, remaining);
                    wanted -= wanted % (_blockSizePerChannel * 2);
                    if (wanted == 0)
                        break;
                    bytesRead = await _file.ReadAtLeastAsync(
                        planar.AsMemory(0, wanted),
                        wanted,
                        throwOnEndOfStream: false,
                        _cts.Token).ConfigureAwait(false);
                }
                finally
                {
                    _seekGate.Release();
                }

                if (bytesRead != planar.Length)
                    break;

                var output = 0;
                var bytesPerSample = _usesNativeDsd ? 4 : 2;
                for (var byteIndex = 0; byteIndex + bytesPerSample - 1 < _blockSizePerChannel; byteIndex += bytesPerSample)
                {
                    for (var channel = 0; channel < 2; channel++)
                    {
                        var source = channel * _blockSizePerChannel + byteIndex;
                        if (_usesNativeDsd)
                        {
                            outputBuffer[output++] = DsfPayload.ToAlsa(planar[source], _isLeastSignificantBitFirst);
                            outputBuffer[output++] = DsfPayload.ToAlsa(planar[source + 1], _isLeastSignificantBitFirst);
                            outputBuffer[output++] = DsfPayload.ToAlsa(planar[source + 2], _isLeastSignificantBitFirst);
                            outputBuffer[output++] = DsfPayload.ToAlsa(planar[source + 3], _isLeastSignificantBitFirst);
                        }
                        else
                        {
                            outputBuffer[output++] = 0;
                            outputBuffer[output++] = DsfPayload.ToAlsa(planar[source], _isLeastSignificantBitFirst);
                            outputBuffer[output++] = DsfPayload.ToAlsa(planar[source + 1], _isLeastSignificantBitFirst);
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
            _file.Dispose();
        }
    }

    private static async Task<DsfHeader> ReadHeaderAsync(
        FileStream file,
        CancellationToken cancellationToken)
    {
        var dsdHeader = new byte[DsfHeaderSize];
        await file.ReadExactlyAsync(dsdHeader, cancellationToken).ConfigureAwait(false);
        if (!dsdHeader.AsSpan(0, 4).SequenceEqual("DSD "u8))
            throw new InvalidDataException("The file is not a valid DSF stream.");

        var format = new byte[FormatChunkSize];
        await file.ReadExactlyAsync(format, cancellationToken).ConfigureAwait(false);
        if (!format.AsSpan(0, 4).SequenceEqual("fmt "u8))
            throw new InvalidDataException("The DSF format chunk is missing.");

        var channels = BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(24, 4));
        var dsdRate = BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(28, 4));
        var bitsPerSample = BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(32, 4));
        var sampleCount = BinaryPrimitives.ReadInt64LittleEndian(format.AsSpan(36, 8));
        var blockSize = BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(44, 4));
        if (channels != 2)
            throw new NotSupportedException("DoP output currently supports stereo DSF files only.");
        if (bitsPerSample is not 1 and not 8 || dsdRate <= 0 || dsdRate % 16 != 0 || blockSize <= 0)
            throw new NotSupportedException("The DSF stream cannot be represented as standard DoP.");

        var dataHeader = new byte[12];
        await file.ReadExactlyAsync(dataHeader, cancellationToken).ConfigureAwait(false);
        if (!dataHeader.AsSpan(0, 4).SequenceEqual("data"u8))
            throw new InvalidDataException("The DSF data chunk is missing.");

        var dataLength = BinaryPrimitives.ReadInt64LittleEndian(dataHeader.AsSpan(4, 8)) - 12;
        var duration = TimeSpan.FromSeconds(sampleCount / (double)dsdRate);
        var dopRate = dsdRate / 16;
        var info = new AudioFileInfo(
            "dsd_lsbf",
            dsdRate,
            channels,
            dopRate,
            true,
            "dsf",
            duration);
        return new DsfHeader(
            info,
            dsdRate,
            dopRate,
            blockSize,
            bitsPerSample == 1,
            file.Position,
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
