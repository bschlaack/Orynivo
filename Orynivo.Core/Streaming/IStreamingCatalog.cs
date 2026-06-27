namespace Orynivo.Streaming;

/// <summary>
/// Provider-neutral abstraction for a streaming catalogue supporting search and album access.
/// </summary>
public interface IStreamingCatalog
{
    /// <summary>The streaming provider this implementation represents.</summary>
    StreamingProvider Provider { get; }

    /// <summary><see langword="true"/> when the required credentials are present.</summary>
    bool IsConfigured { get; }

    /// <summary>Searches the catalogue for artists, albums, and tracks.</summary>
    /// <param name="query">Free-text search query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results split by category.</returns>
    Task<StreamingSearchResult> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves all tracks of an album by its provider-specific album ID.</summary>
    /// <param name="providerAlbumId">Provider-specific album ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<StreamingTrack>> GetAlbumTracksAsync(
        string providerAlbumId,
        CancellationToken cancellationToken = default);
}
