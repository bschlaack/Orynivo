using Orynivo.Library;
using Orynivo.Streaming;

namespace Orynivo;

/// <summary>Identifies where a library catalog row comes from.</summary>
internal enum LibraryCatalogSource
{
    /// <summary>The row belongs to the local SQLite/file-system library.</summary>
    Local,

    /// <summary>The row belongs to a remote Orynivo Server library.</summary>
    OrynivoServer
}

/// <summary>Artist metadata returned by a library catalog provider.</summary>
/// <param name="Source">Catalog source that produced the artist.</param>
/// <param name="Id">Provider-local artist identifier.</param>
/// <param name="Name">Display artist name.</param>
/// <param name="IsFavorite">Whether the artist is a favorite in the active client context.</param>
/// <param name="ArtworkPath">Local image path or authenticated remote image URL.</param>
/// <param name="ThumbnailPath">Local thumbnail path or authenticated remote thumbnail URL.</param>
/// <param name="Biography">Cached biography text.</param>
/// <param name="SourceUrl">Biography source URL.</param>
/// <param name="ProfileLanguage">Profile language code.</param>
/// <param name="ProfileFetchedAt">Profile fetch timestamp.</param>
/// <param name="ImageIsManual">Whether the image was manually selected.</param>
internal sealed record LibraryCatalogArtist(
    LibraryCatalogSource Source,
    long Id,
    string Name,
    bool IsFavorite,
    string? ArtworkPath = null,
    string? ThumbnailPath = null,
    string? Biography = null,
    string? SourceUrl = null,
    string? ProfileLanguage = null,
    long? ProfileFetchedAt = null,
    bool ImageIsManual = false);

/// <summary>Album metadata returned by a library catalog provider.</summary>
/// <param name="Source">Catalog source that produced the album.</param>
/// <param name="Id">Provider-local album identifier.</param>
/// <param name="Title">Album title.</param>
/// <param name="DisplayArtist">Album display artist.</param>
/// <param name="Year">Album year.</param>
/// <param name="ArtworkPath">Local artwork path or authenticated remote artwork URL.</param>
/// <param name="ThumbnailPath">Local thumbnail path or authenticated remote thumbnail URL.</param>
/// <param name="IsFavorite">Whether the album is a favorite in the active client context.</param>
/// <param name="ArtistId">Provider-local album-artist identifier, or <see langword="null"/>.</param>
internal sealed record LibraryCatalogAlbum(
    LibraryCatalogSource Source,
    long Id,
    string Title,
    string? DisplayArtist,
    int? Year,
    string? ArtworkPath,
    string? ThumbnailPath,
    bool IsFavorite,
    long? ArtistId = null);

/// <summary>Track metadata returned by a library catalog provider.</summary>
/// <param name="Source">Catalog source that produced the track.</param>
/// <param name="Id">Provider-local track identifier.</param>
/// <param name="PlaybackPath">Local file path or authenticated remote stream URL used for playback.</param>
/// <param name="SourcePath">Physical source path used for grouping and display, or <see langword="null"/>.</param>
/// <param name="FileName">File name.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Track artist.</param>
/// <param name="Album">Album title.</param>
/// <param name="AlbumArtist">Album artist.</param>
/// <param name="Genre">Genre text.</param>
/// <param name="Format">Container or codec label.</param>
/// <param name="Bitrate">Encoded bitrate in kbps.</param>
/// <param name="Duration">Duration in seconds.</param>
/// <param name="SortTitle">Sort title.</param>
/// <param name="IsFavorite">Whether the track is a favorite in the active client context.</param>
/// <param name="Year">Track year.</param>
/// <param name="TrackNumber">Track number.</param>
/// <param name="TrackTotal">Track total.</param>
/// <param name="DiscNumber">Disc number.</param>
/// <param name="DiscTotal">Disc total.</param>
/// <param name="SampleRate">Sample rate in Hz.</param>
/// <param name="BitDepth">Bit depth.</param>
/// <param name="Channels">Channel count.</param>
/// <param name="Composer">Composer text.</param>
/// <param name="Bpm">Beats per minute.</param>
/// <param name="FileSize">File size in bytes.</param>
/// <param name="AddedAt">Library-added timestamp in Unix seconds.</param>
/// <param name="ReplayGainTrack">Track ReplayGain value.</param>
/// <param name="ReplayGainAlbum">Album ReplayGain value.</param>
/// <param name="KnownDuration">Authoritative playback duration.</param>
/// <param name="ArtistId">Provider-local primary-artist identifier, or <see langword="null"/>.</param>
/// <param name="AlbumId">Provider-local album identifier, or <see langword="null"/>.</param>
internal sealed record LibraryCatalogTrack(
    LibraryCatalogSource Source,
    long Id,
    string PlaybackPath,
    string? SourcePath,
    string FileName,
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    string? Genre,
    string? Format,
    int? Bitrate,
    double? Duration,
    string? SortTitle,
    bool IsFavorite,
    int? Year,
    int? TrackNumber,
    int? TrackTotal,
    int? DiscNumber,
    int? DiscTotal,
    int? SampleRate,
    int? BitDepth,
    int? Channels,
    string? Composer,
    int? Bpm,
    long? FileSize,
    long? AddedAt,
    string? ReplayGainTrack,
    string? ReplayGainAlbum,
    TimeSpan? KnownDuration = null,
    long? ArtistId = null,
    long? AlbumId = null);

/// <summary>Common catalog surface for local and remote music libraries.</summary>
internal interface ILibraryCatalogProvider
{
    /// <summary>Gets the stable provider identifier.</summary>
    string Id { get; }

    /// <summary>Gets the provider display name.</summary>
    string DisplayName { get; }

    /// <summary>Gets the catalog source represented by this provider.</summary>
    LibraryCatalogSource Source { get; }

    /// <summary>Loads artists from the provider.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artist rows.</returns>
    Task<IReadOnlyList<LibraryCatalogArtist>> GetArtistsAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads albums from the provider.</summary>
    /// <param name="includeArtwork">Whether artwork availability/path metadata should be loaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album rows.</returns>
    Task<IReadOnlyList<LibraryCatalogAlbum>> GetAlbumsAsync(bool includeArtwork, CancellationToken cancellationToken = default);

    /// <summary>Loads one album by provider-local identifier.</summary>
    /// <param name="albumId">Provider-local album identifier.</param>
    /// <param name="includeArtwork">Whether artwork availability/path metadata should be loaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching album, or <see langword="null"/> when it is unavailable.</returns>
    Task<LibraryCatalogAlbum?> GetAlbumAsync(long albumId, bool includeArtwork, CancellationToken cancellationToken = default);

    /// <summary>Loads albums for an artist from the provider.</summary>
    /// <param name="artistId">Provider-local artist identifier.</param>
    /// <param name="includeArtwork">Whether artwork availability/path metadata should be loaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album rows.</returns>
    Task<IReadOnlyList<LibraryCatalogAlbum>> GetAlbumsByArtistAsync(long artistId, bool includeArtwork, CancellationToken cancellationToken = default);

    /// <summary>Loads a page of tracks from the provider.</summary>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="pageSize">Maximum rows to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Track rows.</returns>
    Task<IReadOnlyList<LibraryCatalogTrack>> GetTracksAsync(int page = 0, int pageSize = int.MaxValue, CancellationToken cancellationToken = default);

    /// <summary>Loads tracks for an album from the provider.</summary>
    /// <param name="albumId">Provider-local album identifier.</param>
    /// <param name="artistId">Optional provider-local primary-artist filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Track rows.</returns>
    Task<IReadOnlyList<LibraryCatalogTrack>> GetTracksByAlbumAsync(long albumId, long? artistId = null, CancellationToken cancellationToken = default);

    /// <summary>Searches tracks in the provider.</summary>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum rows to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching track rows.</returns>
    Task<IReadOnlyList<LibraryCatalogTrack>> SearchTracksAsync(string query, int limit, CancellationToken cancellationToken = default);

    /// <summary>Loads tracks for the specified provider-local identifiers.</summary>
    /// <param name="ids">Track identifiers to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching track rows.</returns>
    Task<IReadOnlyList<LibraryCatalogTrack>> GetTracksByIdsAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default);

    /// <summary>Stores album artwork in the provider.</summary>
    /// <param name="albumId">Provider-local album identifier.</param>
    /// <param name="imageData">Artwork image bytes.</param>
    /// <param name="mimeType">Image MIME type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when artwork was stored.</returns>
    Task<bool> SetAlbumArtworkAsync(long albumId, byte[] imageData, string? mimeType, CancellationToken cancellationToken = default);
}

/// <summary>Local file-system-backed catalog provider.</summary>
internal sealed class LocalLibraryCatalogProvider : ILibraryCatalogProvider
{
    /// <inheritdoc/>
    public string Id => "local";

    /// <inheritdoc/>
    public string DisplayName => "Local";

    /// <inheritdoc/>
    public LibraryCatalogSource Source => LibraryCatalogSource.Local;

    /// <inheritdoc/>
    public Task<IReadOnlyList<LibraryCatalogArtist>> GetArtistsAsync(CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        return Task.FromResult<IReadOnlyList<LibraryCatalogArtist>>(
            db.GetArtistsLite().Select(ToArtist).ToList());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<LibraryCatalogAlbum>> GetAlbumsAsync(bool includeArtwork, CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        return Task.FromResult<IReadOnlyList<LibraryCatalogAlbum>>(
            db.GetAlbumsLite(includeArtwork).Select(ToAlbum).ToList());
    }

    /// <inheritdoc/>
    public Task<LibraryCatalogAlbum?> GetAlbumAsync(long albumId, bool includeArtwork, CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        var album = db.GetAlbumById(albumId);
        return Task.FromResult(album is null ? null : ToAlbum(album));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<LibraryCatalogAlbum>> GetAlbumsByArtistAsync(long artistId, bool includeArtwork, CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        return Task.FromResult<IReadOnlyList<LibraryCatalogAlbum>>(
            db.GetAlbumsByArtist(artistId, includeArtwork).Select(ToAlbum).ToList());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<LibraryCatalogTrack>> GetTracksAsync(int page = 0, int pageSize = int.MaxValue, CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        var tracks = db.GetTrackList();
        if (pageSize != int.MaxValue)
            tracks = tracks.Skip(page * pageSize).Take(pageSize).ToList();
        return Task.FromResult<IReadOnlyList<LibraryCatalogTrack>>(tracks.Select(ToTrack).ToList());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<LibraryCatalogTrack>> GetTracksByAlbumAsync(long albumId, long? artistId = null, CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        return Task.FromResult<IReadOnlyList<LibraryCatalogTrack>>(
            db.GetTrackListByAlbum(albumId, artistId).Select(ToTrack).ToList());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<LibraryCatalogTrack>> SearchTracksAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        var ids = TrackSearchIndex.SearchByCategory(query).Tracks.Ids.Take(limit);
        return Task.FromResult<IReadOnlyList<LibraryCatalogTrack>>(
            db.GetTrackListByIds(ids).Select(ToTrack).ToList());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<LibraryCatalogTrack>> GetTracksByIdsAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        return Task.FromResult<IReadOnlyList<LibraryCatalogTrack>>(
            db.GetTrackListByIds(ids).Select(ToTrack).ToList());
    }

    /// <inheritdoc/>
    public Task<bool> SetAlbumArtworkAsync(long albumId, byte[] imageData, string? mimeType, CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        return Task.FromResult(db.AttachArtworkToAlbum(albumId, imageData, mimeType));
    }

    private static LibraryCatalogArtist ToArtist(ArtistInfo artist) => new(
        LibraryCatalogSource.Local,
        artist.Id,
        artist.Artist,
        artist.IsFavorite,
        artist.ImagePath,
        artist.ImagePath,
        artist.Biography,
        artist.SourceUrl,
        artist.ProfileLanguage,
        artist.ProfileFetchedAt,
        artist.ImageIsManual);

    private static LibraryCatalogAlbum ToAlbum(AlbumInfo album) => new(
        LibraryCatalogSource.Local,
        album.Id,
        album.Album,
        album.DisplayArtist,
        album.Year,
        album.ArtworkPath,
        album.ThumbnailPath,
        album.IsFavorite,
        album.ArtistId);

    private static LibraryCatalogTrack ToTrack(TrackListInfo track) => new(
        LibraryCatalogSource.Local,
        track.Id,
        track.Path,
        track.Path,
        track.FileName,
        track.Title,
        track.Artist,
        track.Album,
        track.AlbumArtist,
        track.Genre,
        track.Format,
        track.Bitrate,
        track.Duration,
        track.SortTitle,
        track.IsFavorite,
        track.Year,
        track.TrackNumber,
        track.TrackTotal,
        track.DiscNumber,
        track.DiscTotal,
        track.SampleRate,
        track.BitDepth,
        track.Channels,
        track.Composer,
        track.Bpm,
        track.FileSize,
        track.AddedAt,
        track.ReplayGainTrack,
        track.ReplayGainAlbum,
        track.Duration.HasValue ? TimeSpan.FromSeconds(track.Duration.Value) : null,
        track.ArtistId,
        track.AlbumId);
}

/// <summary>Remote Orynivo Server-backed catalog provider.</summary>
internal sealed class OrynivoServerLibraryCatalogProvider : ILibraryCatalogProvider
{
    private readonly OrynivoServerSettings _server;
    private readonly OrynivoServerClient _client;
    private readonly Func<string, long, bool> _isFavorite;

    /// <summary>Initializes a new instance of the <see cref="OrynivoServerLibraryCatalogProvider"/> class.</summary>
    /// <param name="server">Remote server connection settings.</param>
    /// <param name="client">HTTP client wrapper.</param>
    /// <param name="isFavorite">Client-side favorite lookup.</param>
    public OrynivoServerLibraryCatalogProvider(
        OrynivoServerSettings server,
        OrynivoServerClient client,
        Func<string, long, bool> isFavorite)
    {
        _server = server;
        _client = client;
        _isFavorite = isFavorite;
    }

    /// <inheritdoc/>
    public string Id => _server.Id;

    /// <inheritdoc/>
    public string DisplayName => _server.Name;

    /// <inheritdoc/>
    public LibraryCatalogSource Source => LibraryCatalogSource.OrynivoServer;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LibraryCatalogArtist>> GetArtistsAsync(CancellationToken cancellationToken = default)
    {
        var artists = await _client.GetArtistsAsync(_server, cancellationToken);
        return artists.Select(ToArtist).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LibraryCatalogAlbum>> GetAlbumsAsync(bool includeArtwork, CancellationToken cancellationToken = default)
    {
        var albums = await _client.GetAlbumsAsync(_server, cancellationToken);
        return albums.Select(ToAlbum).ToList();
    }

    /// <inheritdoc/>
    public async Task<LibraryCatalogAlbum?> GetAlbumAsync(long albumId, bool includeArtwork, CancellationToken cancellationToken = default)
    {
        var albums = await _client.GetAlbumsAsync(_server, cancellationToken);
        return albums.FirstOrDefault(album => album.Id == albumId) is { } album
            ? ToAlbum(album)
            : null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LibraryCatalogAlbum>> GetAlbumsByArtistAsync(long artistId, bool includeArtwork, CancellationToken cancellationToken = default)
    {
        var albums = await _client.GetAlbumsByArtistAsync(_server, artistId, cancellationToken);
        return albums.Select(ToAlbum).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LibraryCatalogTrack>> GetTracksAsync(int page = 0, int pageSize = int.MaxValue, CancellationToken cancellationToken = default)
    {
        var tracks = await _client.GetTracksAsync(_server, page, pageSize == int.MaxValue ? 500 : pageSize, cancellationToken);
        return tracks.Select(ToTrack).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LibraryCatalogTrack>> GetTracksByAlbumAsync(long albumId, long? artistId = null, CancellationToken cancellationToken = default)
    {
        var tracks = await _client.GetTracksByAlbumAsync(_server, albumId, cancellationToken);
        return tracks
            .Where(track => artistId is null || track.ArtistId == artistId)
            .Select(ToTrack)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LibraryCatalogTrack>> SearchTracksAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        var tracks = await _client.SearchTracksAsync(_server, query, limit, cancellationToken);
        return tracks.Select(ToTrack).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LibraryCatalogTrack>> GetTracksByIdsAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default)
    {
        var tracks = await _client.GetTracksByIdsAsync(_server, ids, cancellationToken);
        return tracks.Select(ToTrack).ToList();
    }

    /// <summary>
    /// Performs a categorised search (tracks, albums, artists) against the remote
    /// server, mirroring the local library search result sections.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum result count per category.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching tracks, albums, and artists mapped to catalog models.</returns>
    public async Task<(IReadOnlyList<LibraryCatalogTrack> Tracks,
                       IReadOnlyList<LibraryCatalogAlbum> Albums,
                       IReadOnlyList<LibraryCatalogArtist> Artists)> SearchFullAsync(
        string query, int limit, CancellationToken cancellationToken = default)
    {
        var result = await _client.SearchFullAsync(_server, query, limit, cancellationToken);
        return (
            result.Tracks.Select(ToTrack).ToList(),
            result.Albums.Select(ToAlbum).ToList(),
            result.Artists.Select(ToArtist).ToList());
    }

    /// <inheritdoc/>
    public async Task<bool> SetAlbumArtworkAsync(long albumId, byte[] imageData, string? mimeType, CancellationToken cancellationToken = default)
        => await _client.UploadAlbumArtworkAsync(_server, albumId, imageData, mimeType, cancellationToken);

    private LibraryCatalogArtist ToArtist(OrynivoArtistInfo artist)
    {
        var artUrl = artist.HasImage
            ? OrynivoServerClient.GetArtistArtworkUrl(_server, artist.Id)
            : null;
        return new LibraryCatalogArtist(
            LibraryCatalogSource.OrynivoServer,
            artist.Id,
            artist.Name,
            _isFavorite("Artist", artist.Id),
            artUrl,
            artUrl,
            artist.Biography,
            artist.SourceUrl,
            artist.ProfileLanguage,
            artist.ProfileFetchedAt,
            artist.ImageIsManual);
    }

    private LibraryCatalogAlbum ToAlbum(OrynivoAlbumInfo album) => new(
        LibraryCatalogSource.OrynivoServer,
        album.Id,
        album.Album,
        album.DisplayArtist,
        album.Year,
        string.IsNullOrWhiteSpace(album.ArtworkPath)
            ? null
            : OrynivoServerClient.GetAlbumArtworkUrl(_server, album.Id, 320),
        string.IsNullOrWhiteSpace(album.ThumbnailPath)
            ? null
            : OrynivoServerClient.GetAlbumArtworkUrl(_server, album.Id, 96),
        _isFavorite("Album", album.Id),
        album.ArtistId);

    private LibraryCatalogTrack ToTrack(OrynivoTrackInfo track)
    {
        var streamUrl = OrynivoServerClient.GetStreamUrl(_server, track.Id);
        return new LibraryCatalogTrack(
            LibraryCatalogSource.OrynivoServer,
            track.Id,
            streamUrl,
            track.SourcePath ?? track.Path,
            track.FileName,
            track.Title,
            track.Artist,
            track.Album,
            track.AlbumArtist,
            track.Genre,
            track.Format,
            track.Bitrate,
            track.Duration,
            track.SortTitle,
            _isFavorite("Track", track.Id),
            track.Year,
            track.TrackNumber,
            track.TrackTotal,
            track.DiscNumber,
            track.DiscTotal,
            track.SampleRate,
            track.BitDepth,
            track.Channels,
            track.Composer,
            track.Bpm,
            track.FileSize,
            track.AddedAt,
            track.ReplayGainTrack,
            track.ReplayGainAlbum,
            track.Duration.HasValue ? TimeSpan.FromSeconds(track.Duration.Value) : null,
            track.ArtistId,
            track.AlbumId);
    }
}
