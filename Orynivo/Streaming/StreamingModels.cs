namespace Orynivo.Streaming;

public enum StreamingProvider
{
    Qobuz
}

public sealed record StreamingArtist(
    string ProviderArtistId,
    string Name,
    Uri? ImageUri = null);

public sealed record StreamingAlbum(
    string ProviderAlbumId,
    string Title,
    string Artist,
    int? Year = null,
    Uri? ArtworkUri = null);

public sealed record StreamingTrack(
    string ProviderTrackId,
    string Title,
    string Artist,
    string? Album,
    TimeSpan? Duration,
    Uri? ArtworkUri = null);

public sealed record StreamingSearchResult(
    IReadOnlyList<StreamingArtist> Artists,
    IReadOnlyList<StreamingAlbum> Albums,
    IReadOnlyList<StreamingTrack> Tracks);

public sealed record StreamingPlaybackSource(
    Uri StreamUri,
    string? Codec,
    int? SampleRate,
    int? BitDepth,
    IReadOnlyDictionary<string, string> RequestHeaders,
    DateTimeOffset? ExpiresAt);

public sealed record StreamingCredential(
    string? ClientSecret,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? AccessTokenExpiresAt);
