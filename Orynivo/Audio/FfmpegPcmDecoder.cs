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
    /// <param name="filePaths">Ordered physical files or URLs forming one logical track.</param>
    /// <param name="outputSampleRate">Output sample rate in hertz.</param>
    /// <param name="sampleFormat">FFmpeg raw sample format.</param>
    /// <param name="codec">FFmpeg raw PCM codec.</param>
    /// <param name="position">Logical seek position within the track.</param>
    /// <param name="segmentStart">Optional segment start within the first physical source.</param>
    /// <param name="segmentEnd">Optional segment end within the first physical source.</param>
    /// <param name="cancellationToken">Cancellation token for decoder startup and prefetching.</param>
    /// <returns>The started PCM decoder.</returns>
    internal static async Task<FfmpegPcmDecoder> CreateAsync(
        IReadOnlyList<string> filePaths,
        int outputSampleRate,
        string sampleFormat,
        string codec,
        TimeSpan position,
        TimeSpan? segmentStart,
        TimeSpan? segmentEnd,
        CancellationToken cancellationToken)
    {
        if (filePaths.Count == 0)
            throw new ArgumentException("At least one decoder input path is required.", nameof(filePaths));
        var absolutePosition = (segmentStart ?? TimeSpan.Zero) + position;
        var duration = segmentEnd is { } end
            ? end - absolutePosition
            : (TimeSpan?)null;
        var durationArgument = duration is { } remaining && remaining > TimeSpan.Zero
            ? $"-t {remaining.TotalSeconds.ToString(CultureInfo.InvariantCulture)} "
            : string.Empty;
        var usesConcatInput = filePaths.Count > 1;
        var inputArgument = usesConcatInput
            ? $"-f concat -safe 0 -protocol_whitelist file,pipe,http,https,tcp,tls,crypto -i pipe:0"
            : $"-i \"{filePaths[0]}\"";
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v error -ss {absolutePosition.TotalSeconds.ToString(CultureInfo.InvariantCulture)} {inputArgument} {durationArgument}-vn -f {sampleFormat} -acodec {codec} -ac 2 -ar {outputSampleRate} pipe:1",
            RedirectStandardInput = usesConcatInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("ffmpeg konnte nicht gestartet werden.");

        try
        {
            if (usesConcatInput)
            {
                foreach (var filePath in filePaths)
                    await process.StandardInput.WriteLineAsync($"file '{EscapeConcatPath(filePath)}'");
                process.StandardInput.Close();
            }
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

    private static string EscapeConcatPath(string path) =>
        path.Replace("'", "'\\''", StringComparison.Ordinal);

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
