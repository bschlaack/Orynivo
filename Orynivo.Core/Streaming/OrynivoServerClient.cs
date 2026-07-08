using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orynivo.Library;

namespace Orynivo.Streaming;

/// <summary>Artist entry returned by the Orynivo Server API.</summary>
/// <param name="Id">Database ID of the artist.</param>
/// <param name="Name">Display name of the artist.</param>
/// <param name="IsFavorite">Whether the artist is marked as favorite.</param>
/// <param name="Biography">Cached artist biography, or <see langword="null"/>.</param>
/// <param name="SourceUrl">Source URL for the cached biography, or <see langword="null"/>.</param>
/// <param name="ProfileLanguage">Language code of the cached biography, or <see langword="null"/>.</param>
/// <param name="ProfileFetchedAt">Unix timestamp of the cached profile refresh, or <see langword="null"/>.</param>
/// <param name="HasBiography">Whether a cached biography exists.</param>
/// <param name="HasImage">Whether a cached artist image exists.</param>
/// <param name="ImageIsManual">Whether the artist image was manually selected.</param>
public sealed record OrynivoArtistInfo(
    long Id,
    string Name,
    bool IsFavorite,
    string? Biography = null,
    string? SourceUrl = null,
    string? ProfileLanguage = null,
    long? ProfileFetchedAt = null,
    bool HasBiography = false,
    bool HasImage = false,
    bool ImageIsManual = false);

/// <summary>Album entry returned by the Orynivo Server API.</summary>
/// <param name="Id">Database ID of the album.</param>
/// <param name="Album">Album title.</param>
/// <param name="DisplayArtist">Display artist for the album, or <see langword="null"/>.</param>
/// <param name="Year">Album year, or <see langword="null"/>.</param>
/// <param name="ArtworkPath">Server-side 320-px artwork path, or <see langword="null"/> when the album has no cover.</param>
/// <param name="ThumbnailPath">Server-side 96-px thumbnail path, or <see langword="null"/> when the album has no cover.</param>
/// <param name="IsFavorite">Whether the album is marked as favorite on the server.</param>
/// <param name="ArtistId">Database ID of the album artist, or <see langword="null"/>.</param>
public sealed record OrynivoAlbumInfo(
    long Id,
    string Album,
    string? DisplayArtist,
    int? Year,
    string? ArtworkPath = null,
    string? ThumbnailPath = null,
    bool IsFavorite = false,
    long? ArtistId = null);

/// <summary>Recently added album entry returned by the Orynivo Server dashboard endpoint.</summary>
/// <param name="Id">Database album identifier.</param>
/// <param name="Title">Album title.</param>
/// <param name="Artist">Display artist name.</param>
/// <param name="ArtistId">Database album-artist identifier, or <see langword="null"/>.</param>
/// <param name="AddedAt">Unix timestamp of the most recently added track in the album.</param>
/// <param name="HasArtwork">Whether the album has server-side artwork.</param>
public sealed record OrynivoRecentAlbum(
    long Id,
    string Title,
    string Artist,
    long? ArtistId,
    long AddedAt,
    bool HasArtwork);

/// <summary>Track entry returned by the Orynivo Server API.</summary>
/// <param name="Id">Database ID of the track.</param>
/// <param name="Path">Playable library path.</param>
/// <param name="SourcePath">Physical source path used for file-name and folder display, or <see langword="null"/>.</param>
/// <param name="FileName">File name.</param>
/// <param name="Title">Track title, or <see langword="null"/>.</param>
/// <param name="Artist">Track artist, or <see langword="null"/>.</param>
/// <param name="AlbumArtist">Album artist, or <see langword="null"/>.</param>
/// <param name="Album">Album title, or <see langword="null"/>.</param>
/// <param name="SortTitle">Sort title, or <see langword="null"/>.</param>
/// <param name="Year">Track year, or <see langword="null"/>.</param>
/// <param name="TrackNumber">Track number, or <see langword="null"/>.</param>
/// <param name="TrackTotal">Track total, or <see langword="null"/>.</param>
/// <param name="DiscNumber">Disc number, or <see langword="null"/>.</param>
/// <param name="DiscTotal">Disc total, or <see langword="null"/>.</param>
/// <param name="Genre">Genre text, or <see langword="null"/>.</param>
/// <param name="Duration">Duration in seconds, or <see langword="null"/>.</param>
/// <param name="Bitrate">Bitrate in kbps, or <see langword="null"/>.</param>
/// <param name="SampleRate">Sample rate in Hz, or <see langword="null"/>.</param>
/// <param name="BitDepth">Bit depth, or <see langword="null"/>.</param>
/// <param name="Channels">Channel count, or <see langword="null"/>.</param>
/// <param name="Composer">Composer text, or <see langword="null"/>.</param>
/// <param name="Bpm">Beats per minute, or <see langword="null"/>.</param>
/// <param name="FileSize">File size in bytes, or <see langword="null"/>.</param>
/// <param name="AddedAt">Library-added timestamp in Unix seconds, or <see langword="null"/>.</param>
/// <param name="ReplayGainTrack">Track ReplayGain value, or <see langword="null"/>.</param>
/// <param name="ReplayGainAlbum">Album ReplayGain value, or <see langword="null"/>.</param>
/// <param name="Format">Audio format label, or <see langword="null"/>.</param>
/// <param name="IsFavorite">Whether the track is marked as favorite on the server.</param>
/// <param name="IsCueTrack">Whether the track is a CUE virtual track.</param>
/// <param name="ArtistId">Database ID of the primary artist, or <see langword="null"/>.</param>
public sealed record OrynivoTrackInfo(
    long Id,
    string Path,
    string? SourcePath,
    string FileName,
    string? Title,
    string? Artist,
    string? AlbumArtist,
    string? Album,
    string? SortTitle = null,
    int? Year = null,
    int? TrackNumber = null,
    int? TrackTotal = null,
    int? DiscNumber = null,
    int? DiscTotal = null,
    string? Genre = null,
    double? Duration = null,
    int? Bitrate = null,
    int? SampleRate = null,
    int? BitDepth = null,
    int? Channels = null,
    string? Composer = null,
    int? Bpm = null,
    long? FileSize = null,
    long? AddedAt = null,
    string? ReplayGainTrack = null,
    string? ReplayGainAlbum = null,
    string? Format = null,
    bool IsFavorite = false,
    bool IsCueTrack = false,
    long? ArtistId = null,
    long? AlbumId = null);

/// <summary>Lightweight remote track entry used for folder-tree construction.</summary>
/// <param name="Id">Database ID of the track.</param>
/// <param name="Path">Playable library path.</param>
/// <param name="SourcePath">Physical source path used for folder grouping.</param>
/// <param name="FileName">File name.</param>
/// <param name="Title">Optional track title.</param>
/// <param name="DiscNumber">Optional disc number.</param>
/// <param name="TrackNumber">Optional track number.</param>
/// <param name="Artist">Track artist, or <see langword="null"/>.</param>
/// <param name="AlbumArtist">Album artist, or <see langword="null"/>.</param>
/// <param name="Album">Album title, or <see langword="null"/>.</param>
/// <param name="Duration">Duration in seconds, or <see langword="null"/>.</param>
/// <param name="Format">Audio format label, or <see langword="null"/>.</param>
/// <param name="IsFavorite">Whether the track is marked as favorite on the server.</param>
/// <param name="ArtistId">Database ID of the primary artist, or <see langword="null"/>.</param>
/// <param name="AlbumId">Database ID of the album, or <see langword="null"/>.</param>
public sealed record OrynivoTrackLiteInfo(
    long Id,
    string Path,
    string SourcePath,
    string FileName,
    string? Title,
    int? DiscNumber,
    int? TrackNumber,
    string? Artist = null,
    string? AlbumArtist = null,
    string? Album = null,
    double? Duration = null,
    string? Format = null,
    bool IsFavorite = false,
    long? ArtistId = null,
    long? AlbumId = null);

/// <summary>Track search response returned by the Orynivo Server API.</summary>
/// <param name="Tracks">Matching track rows.</param>
public sealed record OrynivoTrackSearchResult(IReadOnlyList<OrynivoTrackInfo> Tracks);

/// <summary>Categorised search response (tracks, albums, artists) from the Orynivo Server API.</summary>
/// <param name="Tracks">Matching track rows.</param>
/// <param name="Albums">Matching album rows.</param>
/// <param name="Artists">Matching artist rows.</param>
public sealed record OrynivoFullSearchResult(
    IReadOnlyList<OrynivoTrackInfo> Tracks,
    IReadOnlyList<OrynivoAlbumInfo> Albums,
    IReadOnlyList<OrynivoArtistInfo> Artists);

/// <summary>Cached lyrics for a single track returned by the Orynivo Server API.</summary>
/// <param name="PlainLyrics">Unsynchronised plain-text lyrics, or <see langword="null"/>.</param>
/// <param name="SyncedLyrics">LRC-formatted synchronised lyrics, or <see langword="null"/>.</param>
/// <param name="FetchedAt">Unix-seconds timestamp of the last lyrics lookup, or <see langword="null"/>.</param>
public sealed record OrynivoLyrics(string? PlainLyrics, string? SyncedLyrics, long? FetchedAt = null);

/// <summary>Compact waveform peak data returned by a remote Orynivo Server.</summary>
/// <param name="Version">Cache format version.</param>
/// <param name="DurationSeconds">Logical track duration in seconds.</param>
/// <param name="SampleCount">Number of peak buckets.</param>
/// <param name="Peaks">Normalized peak amplitudes in the range 0..1.</param>
public sealed record OrynivoTrackWaveform(
    int Version,
    double DurationSeconds,
    int SampleCount,
    float[] Peaks);

/// <summary>Playlist entry returned by a remote Orynivo Server.</summary>
/// <param name="Id">Playlist database ID.</param>
/// <param name="Name">Playlist display name.</param>
/// <param name="Description">Optional playlist description.</param>
/// <param name="TrackCount">Number of tracks in the playlist.</param>
/// <param name="IsSmartPlaylist">Whether the playlist is backed by smart criteria.</param>
/// <param name="CreatedAt">Creation timestamp in Unix seconds.</param>
/// <param name="ModifiedAt">Modification timestamp in Unix seconds.</param>
/// <param name="FilterCriteria">Serialized smart-playlist criteria JSON, or <see langword="null"/> for a regular playlist.</param>
public sealed record OrynivoPlaylistInfo(
    long Id,
    string Name,
    string? Description,
    int TrackCount,
    bool IsSmartPlaylist,
    long CreatedAt,
    long ModifiedAt,
    string? FilterCriteria = null);

/// <summary>Track row inside a remote Orynivo Server playlist.</summary>
/// <param name="PlaylistEntryId">Playlist entry database ID.</param>
/// <param name="Position">One-based playlist position.</param>
/// <param name="Path">Stored server-side path.</param>
/// <param name="Track">Resolved track metadata, or <see langword="null"/> when the original file no longer exists.</param>
public sealed record OrynivoPlaylistTrackInfo(long PlaylistEntryId, int Position, string Path, OrynivoTrackInfo? Track);

/// <summary>Request body for creating a regular playlist on a remote Orynivo Server.</summary>
/// <param name="Name">Playlist display name.</param>
/// <param name="TrackIds">Initial server-side track IDs.</param>
public sealed record OrynivoPlaylistCreateRequest(string Name, IReadOnlyList<long> TrackIds);

/// <summary>Request body for appending tracks to a regular playlist on a remote Orynivo Server.</summary>
/// <param name="TrackIds">Server-side track IDs to append.</param>
public sealed record OrynivoPlaylistTrackAppendRequest(IReadOnlyList<long> TrackIds);

/// <summary>Request body for creating or updating a smart playlist on a remote Orynivo Server.</summary>
/// <param name="Name">Playlist display name.</param>
/// <param name="FilterCriteria">Serialized smart-playlist criteria JSON.</param>
public sealed record OrynivoSmartPlaylistSaveRequest(string Name, string FilterCriteria);

/// <summary>Request body for resolving a remote smart playlist using client-side favourites.</summary>
/// <param name="FavoriteTrackIds">Server-side track IDs the client treats as favourites.</param>
public sealed record OrynivoSmartPlaylistResolveRequest(IReadOnlyList<long> FavoriteTrackIds);

/// <summary>Server info returned by <c>/api/info</c>.</summary>
public sealed record OrynivoServerInfo(
    string Name,
    string Version,
    int ApiVersion);

/// <summary>
/// Feature-support flags probed on a remote Orynivo Server so the client can warn when an
/// older server lacks specific endpoints. Each value is <see langword="true"/> (supported),
/// <see langword="false"/> (missing), or <see langword="null"/> (probe inconclusive).
/// </summary>
/// <param name="TrackFacets">Whether the <c>/api/tracks/facets</c> endpoint exists.</param>
/// <param name="RecentAlbums">Whether the <c>/api/albums/recent</c> endpoint exists.</param>
/// <param name="Waveforms">Whether the <c>/api/tracks/{id}/waveform</c> endpoint exists.</param>
public sealed record OrynivoServerCapabilities(bool? TrackFacets, bool? RecentAlbums, bool? Waveforms);

/// <summary>Library path configuration returned by the Orynivo Server API.</summary>
public sealed record OrynivoLibraryPaths(IReadOnlyList<string> Paths);

/// <summary>Directory listing returned by the Orynivo Server API.</summary>
public sealed record OrynivoDirectoryListing(
    string Path,
    bool IsRoot,
    IReadOnlyList<OrynivoDirectoryEntry> Directories);

/// <summary>Single server-side directory entry returned by the Orynivo Server API.</summary>
public sealed record OrynivoDirectoryEntry(
    string Path,
    string Name,
    bool HasChildren);

/// <summary>Summary of a completed scan reported by a remote Orynivo Server.</summary>
public sealed record OrynivoScanResult(
    int Total,
    int Added,
    int Updated,
    int Removed,
    int Failed);

/// <summary>Current scan progress reported by a remote Orynivo Server.</summary>
/// <param name="IsRunning">Whether a scan is currently running.</param>
/// <param name="Path">Library root currently being scanned.</param>
/// <param name="Current">Number of processed files in the current root.</param>
/// <param name="Total">Total files discovered in the current root.</param>
/// <param name="CurrentFile">File currently being processed.</param>
/// <param name="LastResult">Summary of the last completed root scan.</param>
/// <param name="Error">Last scan error, if any.</param>
/// <param name="LibraryChangedAt">Unix timestamp of the last scan that changed indexed tracks.</param>
public sealed record OrynivoScanStatus(
    bool IsRunning,
    string? Path,
    int Current,
    int Total,
    string? CurrentFile,
    OrynivoScanResult? LastResult,
    string? Error,
    long? LibraryChangedAt = null);

/// <summary>Request body used to store a client-refreshed artist profile on a remote Orynivo Server.</summary>
/// <param name="Biography">Downloaded artist biography, or <see langword="null"/> when no biography was found.</param>
/// <param name="SourceUrl">Canonical source URL, or <see langword="null"/>.</param>
/// <param name="Language">Preferred profile language.</param>
/// <param name="ImageData">Optional client-downloaded artist image bytes.</param>
/// <param name="ImageMimeType">MIME type for <paramref name="ImageData"/>.</param>
public sealed record ArtistProfileUpdateRequest(
    string? Biography,
    string? SourceUrl,
    string Language,
    byte[]? ImageData,
    string? ImageMimeType);

/// <summary>Request body for renaming or merging an artist on a remote Orynivo Server.</summary>
/// <param name="ArtistName">Requested artist display name.</param>
/// <param name="PreferredArtistId">Artist ID whose profile should survive a merge, or <see langword="null"/> to only rename or detect a collision.</param>
public sealed record OrynivoArtistRenameRequest(string ArtistName, long? PreferredArtistId = null);

/// <summary>Response body for a remote artist rename or merge request.</summary>
/// <param name="Result">Committed rename result, or <see langword="null"/> when a matching artist must be confirmed first.</param>
/// <param name="MatchingArtist">Matching artist that would be merged, or <see langword="null"/> when the rename was committed.</param>
public sealed record OrynivoArtistRenameResponse(ArtistRenameResult? Result, OrynivoArtistInfo? MatchingArtist);

/// <summary>
/// HTTP client for browsing and streaming a remote Orynivo Server library.
/// Each instance is stateless; methods accept <see cref="OrynivoServerSettings"/> directly.
/// </summary>
public sealed class OrynivoServerClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    /// <summary>Initialises a new client with a shared <see cref="HttpClient"/> instance.</summary>
    public OrynivoServerClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    // ------------------------------------------------------------------
    // Connection test
    // ------------------------------------------------------------------

    /// <summary>
    /// Sends a request to <c>/api/info</c> to verify connectivity and the API key.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Server info on success; <see langword="null"/> on failure.</returns>
    public async Task<OrynivoServerInfo?> TestConnectionAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(server, "/api/info"));
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<OrynivoServerInfo>(JsonOptions, cancellationToken);
        }
        catch { return null; }
    }

    /// <summary>
    /// Probes whether the server supports the newer feature endpoints (track facets, recent
    /// albums, and per-track waveforms) so the client can concretely report what an older
    /// server is missing. Because every server currently reports the same API version, this
    /// tests the routes directly instead of relying on a version number.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The probed capability flags.</returns>
    public async Task<OrynivoServerCapabilities> GetCapabilitiesAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken = default)
    {
        var facets = ProbeRouteExistsAsync(server, "/api/tracks/facets", cancellationToken);
        var recent = ProbeRouteExistsAsync(server, "/api/albums/recent", cancellationToken);
        var waveform = ProbeRouteExistsAsync(server, "/api/tracks/0/waveform", cancellationToken);
        await Task.WhenAll(facets, recent, waveform).ConfigureAwait(false);
        return new OrynivoServerCapabilities(facets.Result, recent.Result, waveform.Result);
    }

    /// <summary>
    /// Tests whether a route exists by sending a method it does not implement: an existing
    /// route replies <c>405 Method Not Allowed</c>, a missing route replies <c>404</c>. This
    /// distinguishes "endpoint present" from "older server without the endpoint" without
    /// downloading any payload.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="path">API path to probe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the route exists, <see langword="false"/> when it is missing, or <see langword="null"/> when inconclusive.</returns>
    private async Task<bool?> ProbeRouteExistsAsync(
        OrynivoServerSettings server,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUrl(server, path));
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.MethodNotAllowed => true,
                System.Net.HttpStatusCode.NotFound => false,
                System.Net.HttpStatusCode.Unauthorized => (bool?)null,
                _ => true
            };
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Artists
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns all artists in the server's library, sorted alphabetically.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of artist entries, or empty on error.</returns>
    public async Task<List<OrynivoArtistInfo>> GetArtistsAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<List<OrynivoArtistInfo>>(server, "/api/artists", cancellationToken)
                   ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Returns the cached profile of a single artist on the remote server.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="artistId">Database ID of the artist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The artist entry, or <see langword="null"/> when missing or the request fails.</returns>
    public async Task<OrynivoArtistInfo?> GetArtistAsync(
        OrynivoServerSettings server,
        long artistId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<OrynivoArtistInfo>(
                server, $"/api/artists/{artistId}", cancellationToken);
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------
    // Albums
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns all albums belonging to a specific artist.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="artistId">Database ID of the artist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of album entries, or empty on error.</returns>
    public async Task<List<OrynivoAlbumInfo>> GetAlbumsByArtistAsync(
        OrynivoServerSettings server,
        long artistId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<List<OrynivoAlbumInfo>>(
                       server, $"/api/artists/{artistId}/albums", cancellationToken)
                   ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Returns all albums in the server's library.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of album entries, or empty on error.</returns>
    public async Task<List<OrynivoAlbumInfo>> GetAlbumsAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<List<OrynivoAlbumInfo>>(server, "/api/albums", cancellationToken)
                   ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Returns the most recently added albums for the dashboard's Recently Added widget.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="limit">Maximum number of albums to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recently added album entries, or empty on error or when the server is too old.</returns>
    public async Task<List<OrynivoRecentAlbum>> GetRecentAlbumsAsync(
        OrynivoServerSettings server,
        int limit = 12,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<List<OrynivoRecentAlbum>>(
                       server, $"/api/albums/recent?limit={limit}", cancellationToken)
                   ?? [];
        }
        catch { return []; }
    }

    /// <summary>Stores a client-refreshed artist profile on the remote server.</summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="artistId">Database ID of the artist.</param>
    /// <param name="biography">Downloaded artist biography, or <see langword="null"/>.</param>
    /// <param name="sourceUrl">Canonical source URL, or <see langword="null"/>.</param>
    /// <param name="language">Preferred profile language.</param>
    /// <param name="imageData">Optional client-downloaded artist image bytes.</param>
    /// <param name="imageMimeType">MIME type for <paramref name="imageData"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated artist entry, or <see langword="null"/> when the request fails.</returns>
    public async Task<OrynivoArtistInfo?> UpdateArtistProfileAsync(
        OrynivoServerSettings server,
        long artistId,
        string? biography,
        string? sourceUrl,
        string language,
        byte[]? imageData,
        string? imageMimeType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildUrl(server, $"/api/artists/{artistId}/profile"))
            {
                Content = JsonContent.Create(
                    new ArtistProfileUpdateRequest(biography, sourceUrl, language, imageData, imageMimeType),
                    options: JsonOptions)
            };
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<OrynivoArtistInfo>(JsonOptions, cancellationToken);
        }
        catch { return null; }
    }

    /// <summary>
    /// Renames an artist on a remote Orynivo Server, or merges two artists when a preferred survivor is supplied.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="artistId">Database ID of the artist being edited.</param>
    /// <param name="artistName">Requested artist display name.</param>
    /// <param name="preferredArtistId">Artist ID whose profile should survive a merge, or <see langword="null"/> to only rename or detect a collision.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rename response, or <see langword="null"/> when the request fails.</returns>
    public async Task<OrynivoArtistRenameResponse?> RenameArtistAsync(
        OrynivoServerSettings server,
        long artistId,
        string artistName,
        long? preferredArtistId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildUrl(server, $"/api/artists/{artistId}/rename"))
            {
                Content = JsonContent.Create(
                    new OrynivoArtistRenameRequest(artistName, preferredArtistId),
                    options: JsonOptions)
            };
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<OrynivoArtistRenameResponse>(JsonOptions, cancellationToken);
        }
        catch { return null; }
    }

    /// <summary>
    /// Uploads client-selected artwork bytes and assigns them to an album on the remote server.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="albumId">Database ID of the album.</param>
    /// <param name="imageData">Raw image bytes to upload.</param>
    /// <param name="mimeType">Optional image MIME type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the server accepted the artwork.</returns>
    public async Task<bool> UploadAlbumArtworkAsync(
        OrynivoServerSettings server,
        long albumId,
        byte[] imageData,
        string? mimeType,
        CancellationToken cancellationToken = default)
        => await UploadImageAsync(server, $"/api/artwork/album/{albumId}", imageData, mimeType, cancellationToken);

    // ------------------------------------------------------------------
    // Tracks
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns all tracks belonging to a specific album.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="albumId">Database ID of the album.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of track entries, or empty on error.</returns>
    public async Task<List<OrynivoTrackInfo>> GetTracksByAlbumAsync(
        OrynivoServerSettings server,
        long albumId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<List<OrynivoTrackInfo>>(
                       server, $"/api/albums/{albumId}/tracks", cancellationToken)
                   ?? [];
        }
        catch { return []; }
    }

    /// <summary>Returns playlists stored on the remote server.</summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Playlist rows, or an empty list on error.</returns>
    public async Task<List<OrynivoPlaylistInfo>> GetPlaylistsAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<List<OrynivoPlaylistInfo>>(server, "/api/playlists", cancellationToken) ?? [];
        }
        catch { return []; }
    }

    /// <summary>Returns resolved tracks for a remote playlist.</summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="playlistId">Remote playlist identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Playlist track rows, or an empty list on error.</returns>
    public async Task<List<OrynivoPlaylistTrackInfo>> GetPlaylistTracksAsync(
        OrynivoServerSettings server,
        long playlistId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<List<OrynivoPlaylistTrackInfo>>(
                server,
                $"/api/playlists/{playlistId}/tracks",
                cancellationToken) ?? [];
        }
        catch { return []; }
    }

    /// <summary>Creates a regular playlist on the remote server.</summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="name">Playlist name.</param>
    /// <param name="trackIds">Initial server-side track IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created playlist, or <see langword="null"/> on error.</returns>
    public async Task<OrynivoPlaylistInfo?> CreatePlaylistAsync(
        OrynivoServerSettings server,
        string name,
        IReadOnlyList<long> trackIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(server, "/api/playlists"))
            {
                Content = JsonContent.Create(new OrynivoPlaylistCreateRequest(name, trackIds), options: JsonOptions)
            };
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<OrynivoPlaylistInfo>(JsonOptions, cancellationToken);
        }
        catch { return null; }
    }

    /// <summary>Resolves a remote smart playlist using the client's favourite track IDs.</summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="playlistId">Remote smart playlist identifier.</param>
    /// <param name="favoriteTrackIds">Server-side track IDs the client treats as favourites.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved playlist track rows, or an empty list on error.</returns>
    public async Task<List<OrynivoPlaylistTrackInfo>> ResolveSmartPlaylistTracksAsync(
        OrynivoServerSettings server,
        long playlistId,
        IReadOnlyList<long> favoriteTrackIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(server, $"/api/playlists/{playlistId}/resolve"))
            {
                Content = JsonContent.Create(new OrynivoSmartPlaylistResolveRequest(favoriteTrackIds), options: JsonOptions)
            };
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return [];
            return await response.Content.ReadFromJsonAsync<List<OrynivoPlaylistTrackInfo>>(JsonOptions, cancellationToken) ?? [];
        }
        catch { return []; }
    }

    /// <summary>Creates a smart playlist on the remote server.</summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="name">Playlist name.</param>
    /// <param name="filterCriteria">Serialized smart-playlist criteria JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created playlist, or <see langword="null"/> on error.</returns>
    public async Task<OrynivoPlaylistInfo?> CreateSmartPlaylistAsync(
        OrynivoServerSettings server,
        string name,
        string filterCriteria,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(server, "/api/playlists/smart"))
            {
                Content = JsonContent.Create(new OrynivoSmartPlaylistSaveRequest(name, filterCriteria), options: JsonOptions)
            };
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<OrynivoPlaylistInfo>(JsonOptions, cancellationToken);
        }
        catch { return null; }
    }

    /// <summary>Updates a smart playlist's name and criteria on the remote server.</summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="playlistId">Remote playlist identifier.</param>
    /// <param name="name">New playlist name.</param>
    /// <param name="filterCriteria">Serialized smart-playlist criteria JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated playlist, or <see langword="null"/> on error.</returns>
    public async Task<OrynivoPlaylistInfo?> UpdateSmartPlaylistAsync(
        OrynivoServerSettings server,
        long playlistId,
        string name,
        string filterCriteria,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, BuildUrl(server, $"/api/playlists/{playlistId}/smart"))
            {
                Content = JsonContent.Create(new OrynivoSmartPlaylistSaveRequest(name, filterCriteria), options: JsonOptions)
            };
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<OrynivoPlaylistInfo>(JsonOptions, cancellationToken);
        }
        catch { return null; }
    }

    /// <summary>Appends tracks to a regular playlist on the remote server.</summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="playlistId">Remote playlist identifier.</param>
    /// <param name="trackIds">Server-side track IDs to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the server accepted the update.</returns>
    public async Task<bool> AddTracksToPlaylistAsync(
        OrynivoServerSettings server,
        long playlistId,
        IReadOnlyList<long> trackIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(server, $"/api/playlists/{playlistId}/tracks"))
            {
                Content = JsonContent.Create(new OrynivoPlaylistTrackAppendRequest(trackIds), options: JsonOptions)
            };
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Deletes a playlist on the remote server.</summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="playlistId">Remote playlist identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the server deleted the playlist.</returns>
    public async Task<bool> DeletePlaylistAsync(
        OrynivoServerSettings server,
        long playlistId,
        CancellationToken cancellationToken = default)
        => await SendNoContentAsync(server, HttpMethod.Delete, $"/api/playlists/{playlistId}", cancellationToken);

    /// <summary>Removes a playlist entry on the remote server.</summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="playlistEntryId">Playlist entry identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the server removed the entry.</returns>
    public async Task<bool> RemovePlaylistEntryAsync(
        OrynivoServerSettings server,
        long playlistEntryId,
        CancellationToken cancellationToken = default)
        => await SendNoContentAsync(server, HttpMethod.Delete, $"/api/playlist-tracks/{playlistEntryId}", cancellationToken);

    /// <summary>
    /// Returns a page of tracks from the server's library.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Number of tracks per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of track entries, or empty on error.</returns>
    public async Task<List<OrynivoTrackInfo>> GetTracksAsync(
        OrynivoServerSettings server,
        int page = 0,
        int pageSize = 500,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<List<OrynivoTrackInfo>>(
                       server, $"/api/tracks?page={page}&pageSize={pageSize}", cancellationToken)
                   ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Searches tracks on the remote server using its Lucene index.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum result count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching track entries, or empty on error.</returns>
    public async Task<List<OrynivoTrackInfo>> SearchTracksAsync(
        OrynivoServerSettings server,
        string query,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await GetJsonAsync<OrynivoTrackSearchResult>(
                server,
                $"/api/search?q={Uri.EscapeDataString(query)}&limit={limit}",
                cancellationToken);
            return result?.Tracks.ToList() ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Performs a categorised search (tracks, albums, artists) using the server's
    /// Lucene index, mirroring the local library's search result.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum result count per category.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching tracks, albums, and artists, or empty results on error.</returns>
    public async Task<OrynivoFullSearchResult> SearchFullAsync(
        OrynivoServerSettings server,
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await GetJsonAsync<OrynivoFullSearchResult>(
                server,
                $"/api/search/full?q={Uri.EscapeDataString(query)}&limit={limit}",
                cancellationToken);
            return result ?? new OrynivoFullSearchResult([], [], []);
        }
        catch
        {
            return new OrynivoFullSearchResult([], [], []);
        }
    }

    /// <summary>
    /// Returns lightweight facet rows (id, favorite, genre, format, bitrate) for every track,
    /// used to build the Tracks filter facets client-side.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Facet rows, or empty on error.</returns>
    public async Task<List<TrackFacetInfo>> GetTrackFacetsAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<List<TrackFacetInfo>>(server, "/api/tracks/facets", cancellationToken)
                   ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Returns full track rows for the specified track identifiers, preserving the requested order.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="ids">Track identifiers to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching track rows, or empty on error.</returns>
    public async Task<List<OrynivoTrackInfo>> GetTracksByIdsAsync(
        OrynivoServerSettings server,
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return [];
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildUrl(server, "/api/tracks/by-ids"))
            {
                Content = JsonContent.Create(ids, options: JsonOptions)
            };
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return [];
            return await response.Content.ReadFromJsonAsync<List<OrynivoTrackInfo>>(JsonOptions, cancellationToken)
                   ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Returns the server-cached lyrics for a track, if any.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="trackId">Database ID of the track.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cached lyrics, or <see langword="null"/> when none are stored or the request fails.</returns>
    public async Task<OrynivoLyrics?> GetTrackLyricsAsync(
        OrynivoServerSettings server,
        long trackId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<OrynivoLyrics>(
                server, $"/api/tracks/{trackId}/lyrics", cancellationToken);
        }
        catch { return null; }
    }

    /// <summary>Gets cached or newly generated waveform data for a remote server track.</summary>
    /// <param name="server">Remote server settings.</param>
    /// <param name="trackId">Server-side track ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Waveform peak data, or <see langword="null"/> when unavailable.</returns>
    public async Task<OrynivoTrackWaveform?> GetTrackWaveformAsync(
        OrynivoServerSettings server,
        long trackId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<OrynivoTrackWaveform>(
                server,
                $"/api/tracks/{trackId}/waveform",
                cancellationToken);
        }
        catch { return null; }
    }

    /// <summary>
    /// Uploads client-downloaded lyrics for a track so the server caches them.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="trackId">Database ID of the track.</param>
    /// <param name="plainLyrics">Unsynchronised plain-text lyrics, or <see langword="null"/>.</param>
    /// <param name="syncedLyrics">LRC-formatted synchronised lyrics, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the server accepted the lyrics.</returns>
    public async Task<bool> UploadTrackLyricsAsync(
        OrynivoServerSettings server,
        long trackId,
        string? plainLyrics,
        string? syncedLyrics,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Put,
                BuildUrl(server, $"/api/tracks/{trackId}/lyrics"))
            {
                Content = JsonContent.Create(
                    new OrynivoLyrics(plainLyrics, syncedLyrics),
                    options: JsonOptions)
            };
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns lightweight track rows for building a remote folder tree.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of lightweight track entries, or empty on error.</returns>
    public async Task<List<OrynivoTrackLiteInfo>> GetTrackFoldersAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<List<OrynivoTrackLiteInfo>>(server, "/api/folders/tracks", cancellationToken)
                   ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Uploads client-selected image bytes and assigns them to an artist on the remote server.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="artistId">Database ID of the artist.</param>
    /// <param name="imageData">Raw image bytes to upload.</param>
    /// <param name="mimeType">Optional image MIME type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the server accepted the artist image.</returns>
    public async Task<bool> UploadArtistImageAsync(
        OrynivoServerSettings server,
        long artistId,
        byte[] imageData,
        string? mimeType,
        CancellationToken cancellationToken = default)
        => await UploadImageAsync(server, $"/api/artwork/artist/{artistId}", imageData, mimeType, cancellationToken);

    // ------------------------------------------------------------------
    // Server library configuration
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the library root directories configured on the remote server.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of server-side library root directories, or an empty list on error.</returns>
    public async Task<List<string>> GetLibraryPathsAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await GetJsonAsync<OrynivoLibraryPaths>(
                server,
                "/api/settings/library-paths",
                cancellationToken);
            return result?.Paths.ToList() ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Replaces the library root directories configured on the remote server.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="paths">Server-side library root directories.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the server accepted and persisted the paths.</returns>
    public async Task<bool> SetLibraryPathsAsync(
        OrynivoServerSettings server,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Put,
                BuildUrl(server, "/api/settings/library-paths"))
            {
                Content = JsonContent.Create(new OrynivoLibraryPaths(paths), options: JsonOptions)
            };
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Lists child directories visible on the remote server.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="path">Server-side directory path to browse, or <see langword="null"/> for filesystem roots.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Directory listing, or <see langword="null"/> on error.</returns>
    public async Task<OrynivoDirectoryListing?> GetDirectoriesAsync(
        OrynivoServerSettings server,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(path)
                ? "/api/files/directories"
                : $"/api/files/directories?path={Uri.EscapeDataString(path)}";
            return await GetJsonAsync<OrynivoDirectoryListing>(server, url, cancellationToken);
        }
        catch { return null; }
    }

    /// <summary>
    /// Starts a full scan on the remote server.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the server accepted the request.</returns>
    public async Task<bool> TriggerScanAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(server, "/api/scan"));
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns the current scan status from the remote server.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Scan status, or <see langword="null"/> when the request fails.</returns>
    public async Task<OrynivoScanStatus?> GetScanStatusAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<OrynivoScanStatus>(server, "/api/scan", cancellationToken);
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------
    // URL helpers (no HTTP calls)
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the authenticated stream URL for a track.
    /// The URL is safe to pass directly to FFmpeg.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="trackId">Database ID of the track to stream.</param>
    /// <returns>Authenticated HTTP URL for the audio stream.</returns>
    public static string GetStreamUrl(OrynivoServerSettings server, long trackId)
        => $"{server.BaseUrl.TrimEnd('/')}/api/stream/{trackId}?key={Uri.EscapeDataString(server.ApiKey)}";

    /// <summary>
    /// Returns the URL for an album's artwork thumbnail.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="albumId">Database ID of the album.</param>
    /// <param name="size">Thumbnail size: 96 or 320.</param>
    /// <returns>Authenticated HTTP URL for the artwork image.</returns>
    public static string GetAlbumArtworkUrl(OrynivoServerSettings server, long albumId, int size)
        => $"{server.BaseUrl.TrimEnd('/')}/api/artwork/album/{albumId}?size={size}&key={Uri.EscapeDataString(server.ApiKey)}";

    /// <summary>
    /// Returns the URL for an artist image.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="artistId">Database ID of the artist.</param>
    /// <returns>Authenticated HTTP URL for the artist image.</returns>
    public static string GetArtistArtworkUrl(OrynivoServerSettings server, long artistId)
        => $"{server.BaseUrl.TrimEnd('/')}/api/artwork/artist/{artistId}?key={Uri.EscapeDataString(server.ApiKey)}";

    /// <summary>
    /// Returns the URL for a track's artwork thumbnail by track ID.
    /// </summary>
    /// <param name="server">Server connection settings.</param>
    /// <param name="trackId">Database ID of the track.</param>
    /// <param name="size">Thumbnail size (<c>96</c> or <c>320</c>); omit for the original.</param>
    /// <returns>The authenticated track-artwork URL.</returns>
    public static string GetTrackArtworkUrl(OrynivoServerSettings server, long trackId, int? size = null)
    {
        var sizeParam = size is int s ? $"size={s}&" : string.Empty;
        return $"{server.BaseUrl.TrimEnd('/')}/api/artwork/track/{trackId}?{sizeParam}key={Uri.EscapeDataString(server.ApiKey)}";
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private async Task<T?> GetJsonAsync<T>(
        OrynivoServerSettings server,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(server, path));
        request.Headers.Add("X-Api-Key", server.ApiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task<bool> UploadImageAsync(
        OrynivoServerSettings server,
        string path,
        byte[] imageData,
        string? mimeType,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = new ByteArrayContent(imageData);
            if (!string.IsNullOrWhiteSpace(mimeType))
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);

            using var request = new HttpRequestMessage(HttpMethod.Put, BuildUrl(server, path))
            {
                Content = content
            };
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<bool> SendNoContentAsync(
        OrynivoServerSettings server,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, BuildUrl(server, path));
            request.Headers.Add("X-Api-Key", server.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static string BuildUrl(OrynivoServerSettings server, string path)
        => server.BaseUrl.TrimEnd('/') + path;

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();
}
