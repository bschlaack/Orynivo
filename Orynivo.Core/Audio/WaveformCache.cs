using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Orynivo.Audio;

/// <summary>Compact waveform peak data used by the transport progress view.</summary>
/// <param name="Version">Cache format version.</param>
/// <param name="DurationSeconds">Logical track duration in seconds.</param>
/// <param name="SampleCount">Number of peak buckets.</param>
/// <param name="Peaks">Normalized peak amplitudes in the range 0..1.</param>
public sealed record WaveformData(
    int Version,
    double DurationSeconds,
    int SampleCount,
    float[] Peaks);

/// <summary>Creates and caches compact waveform peak data for local audio files.</summary>
public static class WaveformCache
{
    private const int CacheVersion = 1;
    private const int AnalysisSampleRate = 8000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Returns cached waveform data, generating it through FFmpeg when needed.</summary>
    /// <param name="trackPath">Stable logical track path.</param>
    /// <param name="sourcePath">Physical source file path, or <see langword="null"/> to use <paramref name="trackPath"/>.</param>
    /// <param name="duration">Logical track duration.</param>
    /// <param name="bucketCount">Number of peak buckets to return.</param>
    /// <param name="segmentStart">Optional segment start within <paramref name="sourcePath"/>.</param>
    /// <param name="segmentEnd">Optional segment end within <paramref name="sourcePath"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Waveform peak data, or <see langword="null"/> when analysis is unavailable.</returns>
    public static async Task<WaveformData?> GetOrCreateAsync(
        string trackPath,
        string? sourcePath,
        TimeSpan duration,
        int bucketCount,
        TimeSpan? segmentStart = null,
        TimeSpan? segmentEnd = null,
        CancellationToken cancellationToken = default)
    {
        var physicalPath = string.IsNullOrWhiteSpace(sourcePath) ? trackPath : sourcePath;
        if (string.IsNullOrWhiteSpace(physicalPath) ||
            !File.Exists(physicalPath) ||
            duration <= TimeSpan.Zero ||
            bucketCount <= 0)
        {
            return null;
        }

        var info = new FileInfo(physicalPath);
        var cachePath = GetCachePath(
            trackPath,
            physicalPath,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
            duration,
            bucketCount,
            segmentStart,
            segmentEnd);

        var cached = await TryReadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
            return cached;

        var peaks = await AnalyzeAsync(
            physicalPath,
            duration,
            bucketCount,
            segmentStart,
            segmentEnd,
            cancellationToken).ConfigureAwait(false);
        if (peaks.Length == 0)
            return null;

        var data = new WaveformData(CacheVersion, duration.TotalSeconds, peaks.Length, peaks);
        await WriteCacheAsync(cachePath, data, cancellationToken).ConfigureAwait(false);
        return data;
    }

    /// <summary>Deletes cached waveform data for a known track record when the track leaves the library.</summary>
    /// <param name="trackPath">Stable logical track path.</param>
    /// <param name="sourcePath">Physical source file path, or <see langword="null"/> to use <paramref name="trackPath"/>.</param>
    /// <param name="duration">Logical track duration.</param>
    /// <param name="bucketCount">Number of peak buckets in the cached entry.</param>
    /// <param name="fileSize">Stored physical file size.</param>
    /// <param name="modifiedAt">Stored physical file modification timestamp.</param>
    /// <param name="segmentStart">Optional segment start within <paramref name="sourcePath"/>.</param>
    /// <param name="segmentEnd">Optional segment end within <paramref name="sourcePath"/>.</param>
    public static void DeleteCached(
        string trackPath,
        string? sourcePath,
        TimeSpan duration,
        int bucketCount,
        long? fileSize,
        long modifiedAt,
        TimeSpan? segmentStart = null,
        TimeSpan? segmentEnd = null)
    {
        if (duration <= TimeSpan.Zero)
            return;

        var physicalPath = string.IsNullOrWhiteSpace(sourcePath) ? trackPath : sourcePath;
        var cachePath = GetCachePath(
            trackPath,
            physicalPath,
            fileSize ?? 0,
            modifiedAt,
            duration,
            bucketCount,
            segmentStart,
            segmentEnd);
        try
        {
            if (File.Exists(cachePath))
                File.Delete(cachePath);
        }
        catch
        {
            // Cache cleanup is best-effort.
        }
    }

    private static async Task<WaveformData?> TryReadCacheAsync(
        string cachePath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(cachePath))
                return null;

            await using var stream = File.OpenRead(cachePath);
            var data = await JsonSerializer.DeserializeAsync<WaveformData>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return data is { Version: CacheVersion, Peaks.Length: > 0 } &&
                data.SampleCount == data.Peaks.Length
                ? data
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteCacheAsync(
        string cachePath,
        WaveformData data,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var temporaryPath = cachePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        catch
        {
            // Waveform caching is opportunistic; playback must not depend on it.
        }
    }

    private static async Task<float[]> AnalyzeAsync(
        string sourcePath,
        TimeSpan duration,
        int bucketCount,
        TimeSpan? segmentStart,
        TimeSpan? segmentEnd,
        CancellationToken cancellationToken)
    {
        var buckets = new float[bucketCount];
        var logicalDuration = segmentStart is { } start && segmentEnd is { } end && end > start
            ? end - start
            : duration;
        var estimatedSamples = Math.Max(1, logicalDuration.TotalSeconds * AnalysisSampleRate);
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = BuildFfmpegArguments(sourcePath, segmentStart, segmentEnd),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = FfmpegLocator.GetSafeWorkingDirectory()
        });

        if (process is null)
            return [];

        using var _ = process;
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var buffer = new byte[8192];
        var totalSamples = 0L;
        try
        {
            while (true)
            {
                var bytesRead = await process.StandardOutput.BaseStream
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                    break;

                totalSamples = AccumulateSamples(
                    buffer,
                    bytesRead,
                    buckets,
                    totalSamples,
                    bucketCount,
                    estimatedSamples);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
        }
        catch
        {
            TryKill(process);
            throw;
        }

        if (totalSamples == 0 || process.ExitCode != 0)
            return [];

        Normalize(buckets);
        return buckets;
    }

    private static string BuildFfmpegArguments(
        string sourcePath,
        TimeSpan? segmentStart,
        TimeSpan? segmentEnd)
    {
        var start = segmentStart.GetValueOrDefault();
        var startArgument = start > TimeSpan.Zero
            ? $"-ss {start.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture)} "
            : string.Empty;
        var durationArgument = segmentStart is { } actualStart &&
            segmentEnd is { } actualEnd &&
            actualEnd > actualStart
            ? $"-t {(actualEnd - actualStart).TotalSeconds.ToString("F6", CultureInfo.InvariantCulture)} "
            : string.Empty;
        return $"-v error {startArgument}-i {Quote(sourcePath)} {durationArgument}-vn -ac 1 -ar {AnalysisSampleRate.ToString(CultureInfo.InvariantCulture)} -f f32le pipe:1";
    }

    private static long AccumulateSamples(
        byte[] buffer,
        int bytesRead,
        float[] buckets,
        long totalSamples,
        int bucketCount,
        double estimatedSamples)
    {
        var sampleCount = bytesRead / sizeof(float);
        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(
            buffer.AsSpan(0, sampleCount * sizeof(float)));
        for (var index = 0; index < samples.Length; index++)
        {
            var bucket = Math.Clamp(
                (int)(totalSamples * bucketCount / estimatedSamples),
                0,
                bucketCount - 1);
            var amplitude = Math.Abs(samples[index]);
            if (amplitude > buckets[bucket])
                buckets[bucket] = amplitude;
            totalSamples++;
        }

        return totalSamples;
    }

    private static void Normalize(float[] buckets)
    {
        var peak = buckets.Max();
        if (peak <= 0)
            return;

        for (var index = 0; index < buckets.Length; index++)
            buckets[index] = MathF.Sqrt(Math.Clamp(buckets[index] / peak, 0, 1));
    }

    private static string GetCachePath(
        string trackPath,
        string physicalPath,
        long fileSize,
        long modifiedAt,
        TimeSpan duration,
        int bucketCount,
        TimeSpan? segmentStart,
        TimeSpan? segmentEnd)
    {
        var key = string.Join(
            "|",
            CacheVersion.ToString(CultureInfo.InvariantCulture),
            trackPath,
            physicalPath,
            fileSize.ToString(CultureInfo.InvariantCulture),
            modifiedAt.ToString(CultureInfo.InvariantCulture),
            duration.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture),
            bucketCount.ToString(CultureInfo.InvariantCulture),
            segmentStart?.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture) ?? "",
            segmentEnd?.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture) ?? "");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return AppPaths.GetDataPath("waveforms", $"{hash}.json");
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
