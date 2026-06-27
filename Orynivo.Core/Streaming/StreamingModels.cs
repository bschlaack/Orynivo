namespace Orynivo.Streaming;

/// <summary>Identifies a supported streaming provider.</summary>
public enum StreamingProvider
{
    /// <summary>Qobuz high-resolution music streaming.</summary>
    Qobuz
}

/// <summary>Represents an artist from the streaming catalogue.</summary>
/// <param name="ProviderArtistId">Provider-specific artist ID.</param>
/// <param name="Name">Display name of the artist.</param>
/// <param name="ImageUri">Optional URL to the artist image.</param>
public sealed record StreamingArtist(
    string ProviderArtistId,
    string Name,
    Uri? ImageUri = null);

/// <summary>Represents an album from the streaming catalogue.</summary>
/// <param name="ProviderAlbumId">Provider-specific album ID.</param>
/// <param name="Title">Album title.</param>
/// <param name="Artist">Name of the primary artist.</param>
/// <param name="Year">Optional release year.</param>
/// <param name="ArtworkUri">Optional URL to the cover artwork.</param>
public sealed record StreamingAlbum(
    string ProviderAlbumId,
    string Title,
    string Artist,
    int? Year = null,
    Uri? ArtworkUri = null);

/// <summary>Represents a single track from the streaming catalogue.</summary>
/// <param name="ProviderTrackId">Provider-specific track ID.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist name.</param>
/// <param name="Album">Optional album title.</param>
/// <param name="Duration">Optional track duration.</param>
/// <param name="ArtworkUri">Optional URL to the cover artwork.</param>
public sealed record StreamingTrack(
    string ProviderTrackId,
    string Title,
    string Artist,
    string? Album,
    TimeSpan? Duration,
    Uri? ArtworkUri = null);

/// <summary>Contains the category-split results of a catalogue search query.</summary>
/// <param name="Artists">Matching artists.</param>
/// <param name="Albums">Matching albums.</param>
/// <param name="Tracks">Matching tracks.</param>
public sealed record StreamingSearchResult(
    IReadOnlyList<StreamingArtist> Artists,
    IReadOnlyList<StreamingAlbum> Albums,
    IReadOnlyList<StreamingTrack> Tracks);

/// <summary>
/// Time-limited source for playing back a streaming track.
/// </summary>
/// <param name="StreamUri">Direct stream URL.</param>
/// <param name="Codec">Optional codec identifier, e.g. <c>flac</c>.</param>
/// <param name="SampleRate">Optional sample rate in Hz.</param>
/// <param name="BitDepth">Optional bit depth.</param>
/// <param name="RequestHeaders">HTTP headers to send when fetching the stream.</param>
/// <param name="ExpiresAt">Optional expiry timestamp of the URL.</param>
public sealed record StreamingPlaybackSource(
    Uri StreamUri,
    string? Codec,
    int? SampleRate,
    int? BitDepth,
    IReadOnlyDictionary<string, string> RequestHeaders,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Credentials for a streaming provider, stored encrypted at rest.
/// </summary>
/// <param name="ClientSecret">Optional application client secret.</param>
/// <param name="AccessToken">Optional short-lived access token.</param>
/// <param name="RefreshToken">Optional long-lived refresh token.</param>
/// <param name="AccessTokenExpiresAt">Optional expiry timestamp of the access token.</param>
public sealed record StreamingCredential(
    string? ClientSecret,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? AccessTokenExpiresAt);

/// <summary>Connection settings for a single Plex Media Server entry.</summary>
public sealed class PlexServerSettings
{
    /// <summary>Gets or sets the unique server identifier (GUID, no dashes).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Gets or sets the user-chosen display name for this server.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the base URL of the Plex server (e.g. <c>http://192.168.1.10:32400</c>).</summary>
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>Persisted connection settings for one remote Orynivo Server instance.</summary>
public sealed class OrynivoServerSettings
{
    /// <summary>Gets or sets the unique server identifier (GUID, no dashes).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Gets or sets the user-chosen display name for this server.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the base URL of the server (e.g. <c>http://192.168.1.10:5280</c>).</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Gets or sets the pre-shared API key required by every request.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
