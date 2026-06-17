namespace Orynivo.Streaming;

/// <summary>
/// Abstraction for the playback side of a streaming provider.
/// Returns time-limited stream URLs for individual tracks.
/// </summary>
public interface IStreamingPlaybackProvider
{
    /// <summary>The streaming provider this implementation represents.</summary>
    StreamingProvider Provider { get; }

    /// <summary><see langword="true"/> when playback is currently available (licence and credentials present).</summary>
    bool IsPlaybackAvailable { get; }

    /// <summary>Resolves a time-limited playback source for the specified track.</summary>
    /// <param name="providerTrackId">Provider-specific track ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream URL together with required HTTP headers and an optional expiry timestamp.</returns>
    Task<StreamingPlaybackSource> GetPlaybackSourceAsync(
        string providerTrackId,
        CancellationToken cancellationToken = default);
}
