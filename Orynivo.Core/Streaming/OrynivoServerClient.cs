using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

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
/// <param name="IsFavorite">Whether the album is marked as favorite on the server.</param>
public sealed record OrynivoAlbumInfo(
    long Id,
    string Album,
    string? DisplayArtist,
    int? Year,
    bool IsFavorite);

/// <summary>Track entry returned by the Orynivo Server API.</summary>
/// <param name="Id">Database ID of the track.</param>
/// <param name="Path">Playable library path.</param>
/// <param name="SourcePath">Physical source path used for file-name and folder display, or <see langword="null"/>.</param>
/// <param name="FileName">File name.</param>
/// <param name="Title">Track title, or <see langword="null"/>.</param>
/// <param name="Artist">Track artist, or <see langword="null"/>.</param>
/// <param name="AlbumArtist">Album artist, or <see langword="null"/>.</param>
/// <param name="Album">Album title, or <see langword="null"/>.</param>
/// <param name="Year">Track year, or <see langword="null"/>.</param>
/// <param name="TrackNumber">Track number, or <see langword="null"/>.</param>
/// <param name="DiscNumber">Disc number, or <see langword="null"/>.</param>
/// <param name="Duration">Duration in seconds, or <see langword="null"/>.</param>
/// <param name="Bitrate">Bitrate in kbps, or <see langword="null"/>.</param>
/// <param name="SampleRate">Sample rate in Hz, or <see langword="null"/>.</param>
/// <param name="BitDepth">Bit depth, or <see langword="null"/>.</param>
/// <param name="Channels">Channel count, or <see langword="null"/>.</param>
/// <param name="Format">Audio format label, or <see langword="null"/>.</param>
/// <param name="IsFavorite">Whether the track is marked as favorite on the server.</param>
/// <param name="IsCueTrack">Whether the track is a CUE virtual track.</param>
public sealed record OrynivoTrackInfo(
    long Id,
    string Path,
    string? SourcePath,
    string FileName,
    string? Title,
    string? Artist,
    string? AlbumArtist,
    string? Album,
    int? Year,
    int? TrackNumber,
    int? DiscNumber,
    double? Duration,
    int? Bitrate,
    int? SampleRate,
    int? BitDepth,
    int? Channels,
    string? Format,
    bool IsFavorite,
    bool IsCueTrack);

/// <summary>Lightweight remote track entry used for folder-tree construction.</summary>
/// <param name="Id">Database ID of the track.</param>
/// <param name="Path">Playable library path.</param>
/// <param name="SourcePath">Physical source path used for folder grouping.</param>
/// <param name="FileName">File name.</param>
/// <param name="Title">Optional track title.</param>
/// <param name="DiscNumber">Optional disc number.</param>
/// <param name="TrackNumber">Optional track number.</param>
public sealed record OrynivoTrackLiteInfo(
    long Id,
    string Path,
    string SourcePath,
    string FileName,
    string? Title,
    int? DiscNumber,
    int? TrackNumber);

/// <summary>Track search response returned by the Orynivo Server API.</summary>
/// <param name="Tracks">Matching track rows.</param>
public sealed record OrynivoTrackSearchResult(IReadOnlyList<OrynivoTrackInfo> Tracks);

/// <summary>Server info returned by <c>/api/info</c>.</summary>
public sealed record OrynivoServerInfo(
    string Name,
    string Version,
    int ApiVersion);

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
public sealed record OrynivoScanStatus(
    bool IsRunning,
    string? Path,
    int Current,
    int Total,
    string? CurrentFile,
    OrynivoScanResult? LastResult,
    string? Error);

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

    private static string BuildUrl(OrynivoServerSettings server, string path)
        => server.BaseUrl.TrimEnd('/') + path;

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();
}
