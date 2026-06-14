namespace Orynivo.Streaming;

public interface IStreamingCatalog
{
    StreamingProvider Provider { get; }
    bool IsConfigured { get; }

    Task<StreamingSearchResult> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StreamingTrack>> GetAlbumTracksAsync(
        string providerAlbumId,
        CancellationToken cancellationToken = default);
}
