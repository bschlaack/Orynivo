using System.IO;
using System.Security.Cryptography;
using System.Text;
using Orynivo.Streaming;

namespace Orynivo;

/// <summary>
/// Owns the client-side caches for remote Orynivo Server data — downloaded artwork
/// (<c>remote-artworks</c>), the cached full track list (<c>remote-track-cache</c>), and
/// the cached folder-tree track list (<c>remote-folder-cache</c>) — and provides total-size
/// reporting plus clearing for all servers or a single server. It is the single source of
/// truth for the per-server cache file paths.
/// </summary>
internal static class RemoteServerCache
{
    private static string ArtworkDir => AppPaths.GetDataPath("remote-artworks");
    private static string TrackCacheDir => AppPaths.GetDataPath("remote-track-cache");
    private static string FolderCacheDir => AppPaths.GetDataPath("remote-folder-cache");

    /// <summary>Returns the cache file path for a server's full track list.</summary>
    /// <param name="server">The remote server.</param>
    /// <returns>The absolute cache file path.</returns>
    public static string TrackListCachePath(OrynivoServerSettings server)
    {
        // The API key is part of the key: cached playback paths embed the key, so a key
        // change must invalidate the cache to avoid stale URLs.
        var key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{server.Id}|{server.BaseUrl}|{server.ApiKey}")));
        return Path.Combine(TrackCacheDir, $"{key}.json");
    }

    /// <summary>Returns the cache file path for a server's folder-tree track list.</summary>
    /// <param name="server">The remote server.</param>
    /// <returns>The absolute cache file path.</returns>
    public static string FolderTrackCachePath(OrynivoServerSettings server)
    {
        var key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{server.Id}|{server.BaseUrl}")));
        return Path.Combine(FolderCacheDir, $"{key}.json");
    }

    /// <summary>Returns the combined size of all remote-server caches, in bytes.</summary>
    /// <returns>The total cache size in bytes.</returns>
    public static long GetTotalSizeBytes() =>
        DirectorySize(ArtworkDir) + DirectorySize(TrackCacheDir) + DirectorySize(FolderCacheDir);

    /// <summary>Deletes every cached artwork, track list, and folder-tree entry for all servers.</summary>
    public static void ClearAll()
    {
        DeleteDirectoryContents(ArtworkDir);
        DeleteDirectoryContents(TrackCacheDir);
        DeleteDirectoryContents(FolderCacheDir);
    }

    /// <summary>
    /// Clears the cached data attributable to a single server: its track-list and folder-tree
    /// caches (matched by their hashed file names) and its server-keyed artwork files
    /// (now-playing track art and artist-info images). URL-hashed artwork shared across servers
    /// is left to <see cref="ClearAll"/>.
    /// </summary>
    /// <param name="server">The server whose caches should be cleared.</param>
    public static void ClearServer(OrynivoServerSettings server)
    {
        TryDeleteFile(TrackListCachePath(server));
        TryDeleteFile(FolderTrackCachePath(server));
        DeleteMatchingFiles(ArtworkDir, $"track-art-{server.Id}-*.img");
        DeleteMatchingFiles(ArtworkDir, $"artist-info-{server.Id}-*.img");
    }

    private static long DirectorySize(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return 0;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* Ignore files that vanish or cannot be read. */ }
            }
            return total;
        }
        catch { return 0; }
    }

    private static void DeleteDirectoryContents(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                TryDeleteFile(file);
        }
        catch { /* Best-effort clearing. */ }
    }

    private static void DeleteMatchingFiles(string directory, string searchPattern)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;
            foreach (var file in Directory.EnumerateFiles(directory, searchPattern))
                TryDeleteFile(file);
        }
        catch { /* Best-effort clearing. */ }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { /* A locked or missing file must not abort clearing. */ }
    }
}
