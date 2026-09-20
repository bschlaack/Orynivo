using System.Security.Cryptography;
using System.Text;

namespace Orynivo.Library;

/// <summary>One downloaded podcast episode tracked by the local cache.</summary>
/// <param name="Path">Downloaded file path.</param>
/// <param name="Bytes">File size in bytes.</param>
/// <param name="LastUsedAtUnix">Unix timestamp of the last download or playback.</param>
public sealed record PodcastDownloadEntry(string Path, long Bytes, long LastUsedAtUnix);

/// <summary>
/// Pure naming and retention decisions for the local podcast download cache.
/// </summary>
public static class PodcastDownloadCache
{
    /// <summary>Fallback extension used when the audio URL exposes no usable one.</summary>
    public const string FallbackExtension = ".audio";

    /// <summary>
    /// Builds the cache file name for one episode. The name derives from the podcast
    /// and episode identities, so the same episode always maps to the same file, and
    /// it keeps the audio extension when the URL exposes a safe one.
    /// </summary>
    /// <param name="podcastId">Apple podcast collection identifier.</param>
    /// <param name="episodeKey">Stable feed episode key.</param>
    /// <param name="audioUrl">Episode audio URL, or <see langword="null"/>.</param>
    /// <returns>A file name without a directory.</returns>
    public static string BuildCacheFileName(string podcastId, string episodeKey, string? audioUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(podcastId);
        ArgumentException.ThrowIfNullOrWhiteSpace(episodeKey);
        var identity = $"{podcastId}\u001f{episodeKey}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16]
            .ToLowerInvariant();
        return $"episode-{hash}{ResolveExtension(audioUrl)}";
    }

    /// <summary>
    /// Selects the downloads to remove so the cache fits the size limit. Least
    /// recently used entries are removed first, and the most recently used entry is
    /// always kept so one oversized download cannot empty the cache.
    /// </summary>
    /// <param name="entries">Downloaded episodes.</param>
    /// <param name="maximumBytes">Maximum total cache size in bytes; zero or less disables eviction.</param>
    /// <returns>The paths to delete, least recently used first.</returns>
    public static IReadOnlyList<string> SelectForEviction(
        IReadOnlyList<PodcastDownloadEntry> entries,
        long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (maximumBytes <= 0 || entries.Count <= 1)
            return [];

        var ordered = entries
            .OrderBy(entry => entry.LastUsedAtUnix)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();
        var total = ordered.Sum(entry => Math.Max(0L, entry.Bytes));
        if (total <= maximumBytes)
            return [];

        var evicted = new List<string>();
        for (var index = 0; index < ordered.Count - 1 && total > maximumBytes; index++)
        {
            evicted.Add(ordered[index].Path);
            total -= Math.Max(0L, ordered[index].Bytes);
        }
        return evicted;
    }

    private static string ResolveExtension(string? audioUrl)
    {
        if (string.IsNullOrWhiteSpace(audioUrl) ||
            !Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri))
        {
            return FallbackExtension;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (extension.Length is < 2 or > 6)
            return FallbackExtension;
        foreach (var character in extension)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '.')
                return FallbackExtension;
        }
        return extension.ToLowerInvariant();
    }
}
