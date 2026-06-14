namespace Orynivo.Streaming;

public interface IStreamingCredentialStore
{
    Task<StreamingCredential?> LoadAsync(
        StreamingProvider provider,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        StreamingProvider provider,
        StreamingCredential credential,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        StreamingProvider provider,
        CancellationToken cancellationToken = default);
}
