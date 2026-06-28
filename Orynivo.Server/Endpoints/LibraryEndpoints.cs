using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orynivo.Library;

namespace Orynivo.Server.Endpoints;

/// <summary>
/// Maps all library browsing endpoints under <c>/api/</c>.
/// </summary>
public static class LibraryEndpoints
{
    /// <summary>
    /// Registers artist, album, track, playlist, and search routes on <paramref name="app"/>.
    /// </summary>
    /// <param name="app">The endpoint route builder to register routes on.</param>
    public static void MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // --- Artists -------------------------------------------------------

        api.MapGet("/artists", () =>
        {
            using var db = AudioDatabase.OpenDefault();
            return Results.Ok(db.GetArtistsWithProfiles().Select(ArtistDto).ToList());
        });

        api.MapGet("/artists/{artistId:long}", (long artistId) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var artist = db.GetArtistById(artistId);
            return artist is null ? Results.NotFound() : Results.Ok(ArtistDto(artist));
        });

        api.MapPost("/artists/{artistId:long}/profile", async (
            long artistId,
            ArtistProfileUpdateRequest request,
            HttpContext context) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var artist = db.GetArtistById(artistId);
            if (artist is null) return Results.NotFound();

            string? imagePath = null;
            if (request.ImageData is { Length: > 0 })
            {
                imagePath = await ArtistImageSearchService.SaveImageAsync(
                    artistId,
                    request.ImageData,
                    request.ImageMimeType,
                    context.RequestAborted);
            }

            db.UpdateArtistProfile(
                artistId,
                request.Biography,
                imagePath,
                request.SourceUrl,
                request.Language ?? "en");

            artist = db.GetArtistById(artistId);
            return artist is null ? Results.NotFound() : Results.Ok(ArtistDto(artist));
        });

        api.MapGet("/artists/{artistId:long}/albums", (long artistId) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var artist = db.GetArtistById(artistId);
            if (artist is null) return Results.NotFound();
            var albums = db.GetAlbumsByArtist(artistId, includeArtwork: true);
            return Results.Ok(albums.Select(AlbumDto).ToList());
        });

        // --- Albums --------------------------------------------------------

        api.MapGet("/albums", () =>
        {
            using var db = AudioDatabase.OpenDefault();
            return Results.Ok(db.GetAlbumsLite(includeArtwork: true).Select(AlbumDto).ToList());
        });

        api.MapGet("/albums/{albumId:long}/tracks", (long albumId) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var tracks = db.GetTrackListByAlbum(albumId);
            return tracks.Count == 0
                ? Results.NotFound()
                : Results.Ok(tracks.Select(TrackDto).ToList());
        });

        // --- Tracks --------------------------------------------------------

        api.MapGet("/tracks", (int page = 0, int pageSize = 500) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var tracks = db.GetTrackList()
                .Skip(page * pageSize)
                .Take(pageSize)
                .Select(TrackDto)
                .ToList();
            return Results.Ok(tracks);
        });

        api.MapGet("/tracks/{trackId:long}", (long trackId) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetTrackById(trackId);
            return track is null ? Results.NotFound() : Results.Ok(TrackRecordDto(track));
        });

        // --- Folders -------------------------------------------------------

        api.MapGet("/folders/tracks", () =>
        {
            using var db = AudioDatabase.OpenDefault();
            var tracks = db.GetTracksLite().Select(t => new
            {
                Id = db.GetTrackIdByPath(t.Path) ?? 0,
                t.Path,
                t.SourcePath,
                t.FileName,
                t.Title,
                t.DiscNumber,
                t.TrackNumber
            }).ToList();
            return Results.Ok(tracks);
        });

        // --- Playlists -----------------------------------------------------

        api.MapGet("/playlists", () =>
        {
            using var db = AudioDatabase.OpenDefault();
            var playlists = db.GetAllPlaylists().Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.TrackCount,
                p.IsSmartPlaylist,
                p.CreatedAt,
                p.ModifiedAt
            }).ToList();
            return Results.Ok(playlists);
        });

        api.MapGet("/playlists/{playlistId:long}/tracks", (long playlistId) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var playlist = db.GetPlaylistById(playlistId);
            if (playlist is null) return Results.NotFound();

            if (playlist.IsSmartPlaylist && playlist.FilterCriteria is not null)
            {
                SmartPlaylistCriteria criteria;
                try { criteria = JsonSerializer.Deserialize<SmartPlaylistCriteria>(playlist.FilterCriteria)!; }
                catch { return Results.BadRequest(new { error = "Invalid smart playlist criteria." }); }

                var candidates = db.GetSmartPlaylistTracks();
                var resolved = criteria.Resolve(candidates);
                var ids = resolved.Select(t => t.Id).ToList();
                var tracks = db.GetTrackListByIds(ids);
                return Results.Ok(tracks.Select(TrackDto).ToList());
            }

            var entries = db.GetPlaylistTracks(playlistId);
            var playlistTracks = entries.Select(e => new
            {
                e.Id,
                e.PlaylistId,
                e.Path,
                e.TrackId,
                e.Position,
                e.AddedAt
            }).ToList();
            return Results.Ok(playlistTracks);
        });

        // --- Search --------------------------------------------------------

        api.MapGet("/search", (string q, int limit = 50) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { error = "Query parameter 'q' is required." });

            var ids = TrackSearchIndex.Search(q, limit);
            if (ids.Count == 0) return Results.Ok(new { tracks = Array.Empty<object>() });

            using var db = AudioDatabase.OpenDefault();
            return Results.Ok(new { tracks = db.GetTrackListByIds(ids).Select(TrackDto).ToList() });
        });

        api.MapGet("/search/full", (string q, int limit = 30) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { error = "Query parameter 'q' is required." });

            var results = TrackSearchIndex.SearchByCategory(q, limit);
            var trackIds = results.Tracks.Ids;
            var albumIds = results.Albums.Ids;
            var artistIds = results.Artists.Ids;

            if (trackIds.Count == 0 && albumIds.Count == 0 && artistIds.Count == 0)
                return Results.Ok(new
                {
                    tracks  = Array.Empty<object>(),
                    albums  = Array.Empty<object>(),
                    artists = Array.Empty<object>()
                });

            using var db = AudioDatabase.OpenDefault();

            var tracks  = trackIds.Count  > 0 ? db.GetTrackListByIds(trackIds).Select(TrackDto).ToList()  : [];
            var albums  = albumIds.Count  > 0 ? db.GetAlbumsLite(includeArtwork: true).Where(a => albumIds.Contains(a.Id)).Select(AlbumDto).ToList() : [];
            var artists = artistIds.Count > 0
                ? db.GetArtistsLite().Where(a => artistIds.Contains(a.Id)).Select(a => new { a.Id, Name = a.Artist, a.IsFavorite }).ToList()
                : [];

            return Results.Ok(new { tracks, albums, artists });
        });
    }

    private static object TrackDto(TrackListInfo t) => new
    {
        t.Id,
        t.Path,
        SourcePath = t.Path,
        FileName = Path.GetFileName(t.Path),
        t.Title,
        t.SortTitle,
        t.Artist,
        t.AlbumArtist,
        t.Album,
        t.Genre,
        t.Year,
        t.TrackNumber,
        t.TrackTotal,
        t.DiscNumber,
        t.DiscTotal,
        t.Duration,
        t.Bitrate,
        t.SampleRate,
        t.BitDepth,
        t.Channels,
        t.Composer,
        t.Bpm,
        t.FileSize,
        t.AddedAt,
        t.ReplayGainTrack,
        t.ReplayGainAlbum,
        t.Format,
        t.IsFavorite,
        IsCueTrack = t.Path.StartsWith("cue://", StringComparison.OrdinalIgnoreCase)
    };

    private static object TrackRecordDto(TrackRecord t) => new
    {
        t.Id,
        t.Path,
        t.SourcePath,
        t.CuePath,
        t.SegmentStart,
        t.SegmentEnd,
        t.FileName,
        t.Title,
        t.Artist,
        t.Album,
        t.AlbumArtist,
        t.Year,
        t.TrackNumber,
        t.TrackTotal,
        t.DiscNumber,
        t.DiscTotal,
        t.Genre,
        t.Duration,
        t.Bitrate,
        t.SampleRate,
        t.BitDepth,
        t.Channels,
        t.Format,
        t.FileSize,
        IsCueTrack = t.Path.StartsWith("cue://", StringComparison.OrdinalIgnoreCase)
    };

    private static object AlbumDto(AlbumInfo a) => new
    {
        a.Id,
        a.Album,
        a.DisplayArtist,
        a.Year,
        a.ArtworkPath,
        a.ThumbnailPath,
        a.IsFavorite
    };

    private static object ArtistDto(ArtistInfo a) => new
    {
        a.Id,
        Name = a.Artist,
        a.IsFavorite,
        a.Biography,
        a.SourceUrl,
        a.ProfileLanguage,
        a.ProfileFetchedAt,
        HasBiography = !string.IsNullOrEmpty(a.Biography),
        HasImage = !string.IsNullOrEmpty(a.ImagePath),
        a.ImageIsManual
    };
}

/// <summary>Request body for storing a client-refreshed artist profile on the server.</summary>
/// <param name="Biography">Downloaded artist biography, or <see langword="null"/> when no biography was found.</param>
/// <param name="SourceUrl">Canonical source URL, or <see langword="null"/>.</param>
/// <param name="Language">Preferred profile language.</param>
/// <param name="ImageData">Optional client-downloaded artist image bytes.</param>
/// <param name="ImageMimeType">MIME type for <paramref name="ImageData"/>.</param>
public sealed record ArtistProfileUpdateRequest(
    string? Biography,
    string? SourceUrl,
    string? Language,
    byte[]? ImageData,
    string? ImageMimeType);
