namespace Orynivo;

public partial class MainWindow
{
    private readonly LibraryViewCache<ContentRow> _unifiedLibraryViewCache = new(3);
    private LibraryLoadResult<ContentRow>? _unifiedLibraryLoadResult;

    /// <summary>Returns a cached unfiltered Artists, Albums, or Tracks view when available.</summary>
    /// <param name="tag">Shared library view tag.</param>
    /// <param name="result">Complete cached rows and source outcomes when found.</param>
    /// <returns><see langword="true"/> when a current entry exists.</returns>
    private bool TryGetUnifiedLibraryViewCache(string tag, out LibraryLoadResult<ContentRow>? result)
    {
        result = null;
        if (!CanCacheUnifiedLibraryView(tag))
            return false;
        var key = CreateUnifiedLibraryViewCacheKey(tag);
        return _unifiedLibraryViewCache.TryGet(key, _unifiedLibraryViewCache.Generation, out result);
    }

    /// <summary>Stores one completed unfiltered shared-library view.</summary>
    /// <param name="tag">Shared library view tag.</param>
    /// <param name="key">View identity captured before starting the load.</param>
    /// <param name="generation">Catalog generation captured before starting the load.</param>
    /// <param name="result">Sorted and merged rows with explicit source outcomes.</param>
    private void StoreUnifiedLibraryViewCache(string tag, string key, int generation, LibraryLoadResult<ContentRow> result)
    {
        if (!CanCacheUnifiedLibraryView(tag))
            return;
        if (!string.Equals(key, CreateUnifiedLibraryViewCacheKey(tag), StringComparison.Ordinal))
            return;
        _unifiedLibraryViewCache.TryStore(key, generation, result);
    }

    /// <summary>Clears catalog snapshots and abandons pending searches while retaining published search failures.</summary>
    private void InvalidateUnifiedLibraryViewCache()
    {
        CancelLibrarySearch(clearResult: false);
        _unifiedLibraryViewCache.Invalidate();
        InvalidateSimilarityFeatureCache();
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
        return $"{tag};{artworkMode};{servers}";
    }
}
