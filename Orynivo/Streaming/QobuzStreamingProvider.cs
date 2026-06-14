namespace Orynivo.Streaming;

public sealed class QobuzStreamingProvider : IStreamingCatalog, IStreamingPlaybackProvider
{
    private readonly string _applicationId;

    public QobuzStreamingProvider(string applicationId)
    {
        _applicationId = applicationId.Trim();
    }

    public StreamingProvider Provider => StreamingProvider.Qobuz;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_applicationId);
    public bool IsPlaybackAvailable => false;

    public Task<StreamingSearchResult> SearchAsync(
        string query,
        CancellationToken cancellationToken = default) =>
        Task.FromException<StreamingSearchResult>(CreateUnavailableException());

    public Task<IReadOnlyList<StreamingTrack>> GetAlbumTracksAsync(
        string providerAlbumId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<StreamingTrack>>(CreateUnavailableException());

    public Task<StreamingPlaybackSource> GetPlaybackSourceAsync(
        string providerTrackId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<StreamingPlaybackSource>(CreateUnavailableException());

    private static InvalidOperationException CreateUnavailableException() =>
        new("Qobuz partner API access has not been configured.");
}
