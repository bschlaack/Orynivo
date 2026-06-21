using System.Diagnostics;
using System.Globalization;

namespace Orynivo.Audio;

/// <summary>
/// Owns one FFmpeg PCM decoder and an initial decoded block used to prepare the
/// next track before the current track reaches its end.
/// </summary>
internal sealed class FfmpegPcmDecoder : IDisposable
{
    private readonly Process _process;
    private byte[]? _prefetchedBytes;
    private int _prefetchedOffset;
    private bool _disposed;

    private FfmpegPcmDecoder(Process process, byte[] prefetchedBytes)
    {
        _process = process;
        _prefetchedBytes = prefetchedBytes;
    }

    /// <summary>Starts FFmpeg and reads its first decoded PCM block.</summary>
    internal static async Task<FfmpegPcmDecoder> CreateAsync(
        string filePath,
        int outputSampleRate,
        string sampleFormat,
        string codec,
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v error -ss {position.TotalSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{filePath}\" -vn -f {sampleFormat} -acodec {codec} -ac 2 -ar {outputSampleRate} pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("ffmpeg konnte nicht gestartet werden.");

        try
        {
            var buffer = new byte[64 * 1024];
            var bytesRead = await process.StandardOutput.BaseStream
                .ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            return new FfmpegPcmDecoder(process, buffer.AsSpan(0, bytesRead).ToArray());
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    /// <summary>Reads decoded PCM, returning prefetched bytes before the process pipe.</summary>
    internal async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        if (_prefetchedBytes is not null)
        {
            var remaining = _prefetchedBytes.Length - _prefetchedOffset;
            if (remaining > 0)
            {
                var count = Math.Min(remaining, destination.Length);
                _prefetchedBytes.AsMemory(_prefetchedOffset, count).CopyTo(destination);
                _prefetchedOffset += count;
                if (_prefetchedOffset == _prefetchedBytes.Length)
                    _prefetchedBytes = null;
                return count;
            }

            _prefetchedBytes = null;
        }

        return await _process.StandardOutput.BaseStream
            .ReadAsync(destination, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);
        _process.Dispose();
        _disposed = true;
    }
}
