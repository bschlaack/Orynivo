namespace Orynivo;

public partial class MainWindow
{
    /// <summary>One resolved shared-library view retained for fast repeated navigation.</summary>
    /// <param name="Rows">Sorted and source-merged rows.</param>
    /// <param name="LastAccessUtc">Last access time used for bounded eviction.</param>
    private sealed record UnifiedLibraryViewCacheEntry(
        List<ContentRow> Rows,
        DateTimeOffset LastAccessUtc);

    private const int UnifiedLibraryViewCacheMaximumEntries = 3;
    private readonly object _unifiedLibraryViewCacheSync = new();
    private readonly Dictionary<string, UnifiedLibraryViewCacheEntry> _unifiedLibraryViewCache =
        new(StringComparer.Ordinal);
    private int _unifiedLibraryCatalogGeneration;

    /// <summary>Returns a cached unfiltered Artists, Albums, or Tracks view when available.</summary>
    /// <param name="tag">Shared library view tag.</param>
    /// <param name="rows">Cached rows when found.</param>
    /// <returns><see langword="true"/> when a current entry exists.</returns>
    private bool TryGetUnifiedLibraryViewCache(string tag, out List<ContentRow> rows)
    {
        rows = [];
        if (!CanCacheUnifiedLibraryView(tag))
            return false;
        var key = CreateUnifiedLibraryViewCacheKey(tag);
        lock (_unifiedLibraryViewCacheSync)
        {
            if (!_unifiedLibraryViewCache.TryGetValue(key, out var cached))
                return false;
            _unifiedLibraryViewCache[key] = cached with { LastAccessUtc = DateTimeOffset.UtcNow };
            rows = cached.Rows;
            return true;
        }
    }

    /// <summary>Stores one completed unfiltered shared-library view.</summary>
    /// <param name="tag">Shared library view tag.</param>
    /// <param name="rows">Sorted and merged rows.</param>
    private void StoreUnifiedLibraryViewCache(string tag, List<ContentRow> rows)
    {
        if (!CanCacheUnifiedLibraryView(tag))
            return;
        var key = CreateUnifiedLibraryViewCacheKey(tag);
        lock (_unifiedLibraryViewCacheSync)
        {
            _unifiedLibraryViewCache[key] = new UnifiedLibraryViewCacheEntry(rows, DateTimeOffset.UtcNow);
            while (_unifiedLibraryViewCache.Count > UnifiedLibraryViewCacheMaximumEntries)
            {
                var oldest = _unifiedLibraryViewCache.MinBy(pair => pair.Value.LastAccessUtc).Key;
                _unifiedLibraryViewCache.Remove(oldest);
            }
        }
    }

    /// <summary>Clears shared-library view snapshots after catalog or favorite changes.</summary>
    private void InvalidateUnifiedLibraryViewCache()
    {
        Interlocked.Increment(ref _unifiedLibraryCatalogGeneration);
        lock (_unifiedLibraryViewCacheSync)
            _unifiedLibraryViewCache.Clear();
    }

    /// <summary>Determines whether the current view state is safe to reuse as an unfiltered snapshot.</summary>
    /// <param name="tag">Shared library view tag.</param>
    /// <returns><see langword="true"/> for an unfiltered top-level catalog view.</returns>
    private bool CanCacheUnifiedLibraryView(string tag) =>
        (tag is "Artists" or "Albums" or "Tracks") &&
        !HasActiveFilters &&
        !_artistFavoritesOnly &&
        !_albumFavoritesOnly &&
        _activeArtistFilterId is null &&
        _activeAlbumFilterId is null;

    /// <summary>Builds a non-persisted cache key without retaining server URLs or credentials.</summary>
    /// <param name="tag">Shared library view tag.</param>
    /// <returns>The current view cache identity.</returns>
    private string CreateUnifiedLibraryViewCacheKey(string tag)
    {
        var servers = string.Join('|', (_settings.OrynivoServers ?? [])
            .OrderBy(server => server.Id, StringComparer.Ordinal)
            .Select(server => $"{server.Id}:{StringComparer.Ordinal.GetHashCode(server.BaseUrl ?? string.Empty)}"));
        var artworkMode = tag switch
        {
            "Albums" => _showAlbumArtworkView,
            "Artists" => _showArtistArtworkView,
            _ => false
        };
        return $"{Volatile.Read(ref _unifiedLibraryCatalogGeneration)};{tag};{artworkMode};{servers}";
    }
}
