namespace Orynivo.Streaming;

/// <summary>
/// Persists generic streaming-provider credentials in the shared current-user
/// application credential container.
/// </summary>
public sealed class WindowsStreamingCredentialStore : IStreamingCredentialStore
{
    private readonly ApplicationCredentialStore _store = new();

    /// <inheritdoc/>
    public Task<StreamingCredential?> LoadAsync(
        StreamingProvider provider,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => _store.LoadStreamingCredential(provider), cancellationToken);

    /// <inheritdoc/>
    public Task SaveAsync(
        StreamingProvider provider,
        StreamingCredential credential,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => _store.SaveStreamingCredential(provider, credential),
            cancellationToken);

    /// <inheritdoc/>
    public Task RemoveAsync(
        StreamingProvider provider,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => _store.RemoveStreamingCredential(provider), cancellationToken);
}
