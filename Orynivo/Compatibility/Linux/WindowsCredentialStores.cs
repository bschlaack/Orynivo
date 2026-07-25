using System.Collections.Concurrent;

namespace Orynivo.Streaming;

/// <summary>
/// Holds Plex credentials in process memory on platforms where Windows DPAPI is
/// unavailable. Credentials are deliberately not written to disk.
/// </summary>
public sealed class WindowsPlexCredentialStore
{
    private static readonly ConcurrentDictionary<string, string> Credentials = new();

    /// <summary>Returns the credentials retained for this process.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A copy of the current in-memory credential map.</returns>
    public Task<Dictionary<string, string>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LoadAll());
    }

    /// <summary>Returns the credentials retained for this process.</summary>
    /// <returns>A copy of the current in-memory credential map.</returns>
    public Dictionary<string, string> LoadAll() =>
        new(Credentials, StringComparer.Ordinal);

    /// <summary>Replaces the process-local credentials without persisting secrets.</summary>
    /// <param name="credentials">Credentials to retain until the process exits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task SaveAllAsync(
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveAll(credentials);
        return Task.CompletedTask;
    }

    /// <summary>Replaces the process-local credentials without persisting secrets.</summary>
    /// <param name="credentials">Credentials to retain until the process exits.</param>
    public void SaveAll(IReadOnlyDictionary<string, string> credentials)
    {
        Credentials.Clear();
        foreach (var pair in credentials)
            Credentials[pair.Key] = pair.Value;
    }
}

/// <summary>
/// Holds generic streaming credentials in memory when Windows DPAPI is
/// unavailable. Values are never persisted in plaintext.
/// </summary>
public sealed class WindowsStreamingCredentialStore : IStreamingCredentialStore
{
    private static readonly ConcurrentDictionary<StreamingProvider, StreamingCredential> Credentials = new();

    /// <inheritdoc/>
    public Task<StreamingCredential?> LoadAsync(
        StreamingProvider provider,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Credentials.TryGetValue(provider, out var credential);
        return Task.FromResult<StreamingCredential?>(credential);
    }

    /// <inheritdoc/>
    public Task SaveAsync(
        StreamingProvider provider,
        StreamingCredential credential,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Credentials[provider] = credential;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveAsync(
        StreamingProvider provider,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Credentials.TryRemove(provider, out _);
        return Task.CompletedTask;
    }
}
