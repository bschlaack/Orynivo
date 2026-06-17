namespace Orynivo.Streaming;

/// <summary>
/// Inactive scaffold for Qobuz catalogue and playback access.
/// All operations throw until official partner API access is configured.
/// </summary>
public sealed class QobuzStreamingProvider : IStreamingCatalog, IStreamingPlaybackProvider
{
    private readonly string _applicationId;

    /// <summary>Initialises the provider with the application identifier from settings.</summary>
    /// <param name="applicationId">Non-secret Qobuz application identifier.</param>
    public QobuzStreamingProvider(string applicationId)
    {
        _applicationId = applicationId.Trim();
    }

    /// <inheritdoc/>
    public StreamingProvider Provider => StreamingProvider.Qobuz;

    /// <inheritdoc/>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_applicationId);

    /// <inheritdoc/>
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
