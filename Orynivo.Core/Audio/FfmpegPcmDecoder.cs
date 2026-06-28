using System.Diagnostics;
using System.Globalization;

namespace Orynivo.Audio;

/// <summary>
/// Owns one FFmpeg PCM decoder and an initial decoded block used to prepare the
/// next track before the current track reaches its end.
/// </summary>
public sealed class FfmpegPcmDecoder : IDisposable
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

        // FFmpeg's default stream analysis window is 5 s / 5 MB. Over HTTP it blocks on
        // that probe on every decoder start, so the first play and every seek of a remote
        // track stall for ~5 s. Audio container headers are tiny, so cap the probe and
        // add HTTP reconnect resilience for the single-URL remote/Plex case.
        var isHttpInput = !usesConcatInput &&
            (filePaths[0].StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             filePaths[0].StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        // Server-side seek: seeking a seektable-less file over HTTP makes the client
        // binary-search via many range round-trips (~5 s). An Orynivo Server stream
        // endpoint (`/api/stream/`) instead seeks the local file and transcodes from the
        // offset, so the client decodes the offset stream from position 0. Plex and other
        // HTTP sources keep client-side seeking.
        var useServerSideSeek = isHttpInput &&
            absolutePosition > TimeSpan.Zero &&
            filePaths[0].Contains("/api/stream/", StringComparison.OrdinalIgnoreCase);
        var localSeek = useServerSideSeek ? TimeSpan.Zero : absolutePosition;
        var singleInputUrl = useServerSideSeek
            ? AppendSeekQuery(filePaths[0], absolutePosition.TotalSeconds)
            : filePaths[0];

        var inputArgument = usesConcatInput
            ? $"-f concat -safe 0 -protocol_whitelist file,pipe,http,https,tcp,tls,crypto -i pipe:0"
            : $"-i \"{singleInputUrl}\"";
        var httpInputOptions = isHttpInput
            ? "-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 2 -analyzeduration 500000 -probesize 500000 "
            : string.Empty;

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v error {httpInputOptions}-ss {localSeek.TotalSeconds.ToString(CultureInfo.InvariantCulture)} {inputArgument} {durationArgument}-vn -f {sampleFormat} -acodec {codec} -ac 2 -ar {outputSampleRate} pipe:1",
            WorkingDirectory = FfmpegLocator.GetSafeWorkingDirectory(),
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

    /// <summary>Appends a server-side seek (<c>ss</c>) query parameter to a stream URL.</summary>
    /// <param name="url">Base stream URL, which may already carry a query string.</param>
    /// <param name="seconds">Seek offset in seconds.</param>
    /// <returns>The URL with the <c>ss</c> parameter appended.</returns>
    private static string AppendSeekQuery(string url, double seconds)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}ss={seconds.ToString("F6", CultureInfo.InvariantCulture)}";
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
