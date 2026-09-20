using Orynivo.Library;

namespace Orynivo;

/// <summary>
/// Local podcast download cache. Episodes are stored beneath the per-user data
/// directory under a hashed name, played from disk when present, and evicted least
/// recently used first once the configured size limit is exceeded.
/// </summary>
internal static class PodcastDownloadService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    /// <summary>Gets the cache directory.</summary>
    internal static string CacheDirectory => AppPaths.GetDataPath("podcast-downloads");

    /// <summary>Gets the cache file path of an episode, whether or not it exists.</summary>
    /// <param name="podcast">Podcast owning the episode.</param>
    /// <param name="episode">Episode to locate.</param>
    /// <returns>The absolute cache file path.</returns>
    internal static string GetPath(PodcastRecord podcast, PodcastEpisode episode) =>
        Path.Combine(
            CacheDirectory,
            PodcastDownloadCache.BuildCacheFileName(
                podcast.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                episode.EpisodeKey,
                episode.AudioUrl));

    /// <summary>Gets the downloaded file path of an episode.</summary>
    /// <param name="podcast">Podcast owning the episode.</param>
    /// <param name="episode">Episode to locate.</param>
    /// <returns>The local path when the episode is cached, otherwise <see langword="null"/>.</returns>
    internal static string? GetDownloadedPath(PodcastRecord podcast, PodcastEpisode episode)
    {
        var path = GetPath(podcast, episode);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Downloads an episode into the cache.</summary>
    /// <param name="podcast">Podcast owning the episode.</param>
    /// <param name="episode">Episode to download.</param>
    /// <param name="progress">Optional progress from zero through one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The local path on success, otherwise <see langword="null"/>.</returns>
    internal static async Task<string?> DownloadAsync(
        PodcastRecord podcast,
        PodcastEpisode episode,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var target = GetPath(podcast, episode);
        var temporary = target + ".tmp";
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            using var response = await Http.GetAsync(
                episode.AudioUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using (var source = await response.Content
                             .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = File.Create(temporary))
            {
                var buffer = new byte[81920];
                long written = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    written += read;
                    if (total is > 0)
                        progress?.Report(Math.Clamp(written / (double)total.Value, 0d, 1d));
                }
            }

            File.Move(temporary, target, overwrite: true);
            progress?.Report(1d);
            return target;
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporary);
            throw;
        }
        catch
        {
            // A failed download must never affect playback.
            TryDelete(temporary);
            return null;
        }
    }

    /// <summary>Deletes a downloaded episode.</summary>
    /// <param name="podcast">Podcast owning the episode.</param>
    /// <param name="episode">Episode to delete.</param>
    internal static void Delete(PodcastRecord podcast, PodcastEpisode episode) =>
        TryDelete(GetPath(podcast, episode));

    /// <summary>Records that a cached episode was just played so eviction keeps it.</summary>
    /// <param name="podcast">Podcast owning the episode.</param>
    /// <param name="episode">Episode that was played.</param>
    internal static void MarkUsed(PodcastRecord podcast, PodcastEpisode episode)
    {
        try
        {
            var path = GetPath(podcast, episode);
            if (File.Exists(path))
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Cache bookkeeping is best effort.
        }
    }

    /// <summary>Deletes cached episodes until the cache fits the size limit.</summary>
    /// <param name="maximumBytes">Maximum total cache size in bytes; zero or less disables eviction.</param>
    internal static void EnforceLimit(long maximumBytes)
    {
        try
        {
            if (!Directory.Exists(CacheDirectory))
                return;

            var entries = Directory
                .EnumerateFiles(CacheDirectory, "episode-*")
                .Select(path => new PodcastDownloadEntry(
                    path,
                    new FileInfo(path).Length,
                    new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToUnixTimeSeconds()))
                .ToList();
            foreach (var path in PodcastDownloadCache.SelectForEviction(entries, maximumBytes))
                TryDelete(path);
        }
        catch
        {
            // Cache maintenance is best effort.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
