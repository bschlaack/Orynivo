namespace Orynivo.Streaming;

/// <summary>
/// Persists Plex access tokens in the shared current-user application credential container.
/// </summary>
public sealed class WindowsPlexCredentialStore
{
    private readonly ApplicationCredentialStore _store = new();

    /// <summary>Loads all Plex access tokens asynchronously.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tokens keyed by Plex server ID.</returns>
    public Task<Dictionary<string, string>> LoadAllAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(LoadAll, cancellationToken);

    /// <summary>Loads all Plex access tokens.</summary>
    /// <returns>Tokens keyed by Plex server ID.</returns>
    public Dictionary<string, string> LoadAll() => _store.LoadPlexTokens();

    /// <summary>Replaces all Plex access tokens asynchronously.</summary>
    /// <param name="credentials">Tokens keyed by Plex server ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the save operation.</returns>
    public Task SaveAllAsync(
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => SaveAll(credentials), cancellationToken);

    /// <summary>Replaces all Plex access tokens.</summary>
    /// <param name="credentials">Tokens keyed by Plex server ID.</param>
    public void SaveAll(IReadOnlyDictionary<string, string> credentials) =>
        _store.SavePlexTokens(credentials);
}
