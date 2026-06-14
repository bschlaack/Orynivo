namespace Orynivo.Streaming;

public interface IStreamingPlaybackProvider
{
    StreamingProvider Provider { get; }
    bool IsPlaybackAvailable { get; }

    Task<StreamingPlaybackSource> GetPlaybackSourceAsync(
        string providerTrackId,
        CancellationToken cancellationToken = default);
}
