namespace Orynivo.Streaming;

/// <summary>
/// Persists credentials for streaming providers in a secure store.
/// </summary>
public interface IStreamingCredentialStore
{
    /// <summary>Loads the stored credentials for a provider.</summary>
    /// <param name="provider">The streaming provider whose credentials to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored credentials, or <see langword="null"/> if none are present.</returns>
    Task<StreamingCredential?> LoadAsync(
        StreamingProvider provider,
        CancellationToken cancellationToken = default);

    /// <summary>Saves or updates credentials for a provider.</summary>
    /// <param name="provider">The streaming provider to save credentials for.</param>
    /// <param name="credential">Credentials to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(
        StreamingProvider provider,
        StreamingCredential credential,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the stored credentials for a provider.</summary>
    /// <param name="provider">The streaming provider whose credentials to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(
        StreamingProvider provider,
        CancellationToken cancellationToken = default);
}
