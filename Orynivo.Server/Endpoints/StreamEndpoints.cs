using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Orynivo.Audio;
using Orynivo.Library;

namespace Orynivo.Server.Endpoints;

/// <summary>
/// Maps audio streaming and artwork endpoints under <c>/api/</c>.
/// </summary>
public static class StreamEndpoints
{
    /// <summary>
    /// Registers stream and artwork routes on <paramref name="app"/>.
    /// </summary>
    /// <param name="app">The endpoint route builder to register routes on.</param>
    public static void MapStreamEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // --- Audio streaming -----------------------------------------------

        /// <summary>
        /// Streams the audio file for a track by database ID.
        /// Regular files support HTTP byte-range seeking.
        /// CUE virtual tracks are transcoded to FLAC on-the-fly via FFmpeg.
        /// </summary>
        api.MapGet("/stream/{trackId:long}", async (
            long trackId,
            HttpContext ctx,
            ILoggerFactory loggerFactory,
            double? ss) =>
        {
            var logger = loggerFactory.CreateLogger("Orynivo.Server.Stream");
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetTrackById(trackId);
            if (track is null) return Results.NotFound();

            return await BuildStreamResult(track.Path, track.SourcePath,
                track.SegmentStart, track.SegmentEnd, ctx, logger, ss);
        });

        /// <summary>
        /// Streams an audio file directly by absolute file path (encoded in the query string).
        /// Intended for queue items that may not yet be indexed.
        /// </summary>
        api.MapGet("/stream/path", async (
            string p,
            HttpContext ctx,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Orynivo.Server.Stream");
            if (!File.Exists(p)) return Results.NotFound();
            return await BuildStreamResult(p, null, null, null, ctx, logger);
        });

        // --- Artwork -------------------------------------------------------

        /// <summary>
        /// Serves album artwork by album database ID.
        /// Use <c>?size=96</c> or <c>?size=320</c> for thumbnails; omit for the original.
        /// </summary>
        api.MapGet("/artwork/album/{albumId:long}", (long albumId, int? size) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var album = db.GetAlbumById(albumId);
            if (album is null) return Results.NotFound();

            var artworkPaths = db.EnsureArtworkFilesForAlbum(albumId);
            var path = SelectExistingArtworkPath(artworkPaths, size);
            if (path is null
                && LibraryScanner.RepairMissingAlbumArtwork(albumId))
            {
                artworkPaths = db.EnsureArtworkFilesForAlbum(albumId);
                path = SelectExistingArtworkPath(artworkPaths, size);
            }

            if (path is null) return Results.NotFound();
            return Results.File(path, GuessImageMimeType(path), enableRangeProcessing: false);
        });

        /// <summary>
        /// Stores uploaded album artwork bytes and attaches them to the album.
        /// </summary>
        api.MapPut("/artwork/album/{albumId:long}", async (long albumId, HttpRequest request) =>
        {
            var data = await ReadImageUploadAsync(request);
            if (data.Length == 0) return Results.BadRequest(new { error = "Artwork image data is required." });

            using var db = AudioDatabase.OpenDefault();
            return db.AttachArtworkToAlbum(albumId, data, request.ContentType)
                ? Results.NoContent()
                : Results.NotFound();
        });

        /// <summary>
        /// Serves the manually cached artist image by artist database ID.
        /// </summary>
        api.MapGet("/artwork/artist/{artistId:long}", (long artistId) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var artist = db.GetArtistById(artistId);
            if (artist is null) return Results.NotFound();
            if (string.IsNullOrEmpty(artist.ImagePath) || !File.Exists(artist.ImagePath))
                return Results.NotFound();

            return Results.File(artist.ImagePath, GuessImageMimeType(artist.ImagePath), enableRangeProcessing: false);
        });

        /// <summary>
        /// Stores uploaded artist image bytes in the server-side image cache and marks the image as manual.
        /// </summary>
        api.MapPut("/artwork/artist/{artistId:long}", async (long artistId, HttpRequest request) =>
        {
            var data = await ReadImageUploadAsync(request);
            if (data.Length == 0) return Results.BadRequest(new { error = "Artist image data is required." });

            using var db = AudioDatabase.OpenDefault();
            if (db.GetArtistById(artistId) is null)
                return Results.NotFound();

            var path = await ArtistImageSearchService.SaveImageAsync(artistId, data, request.ContentType, request.HttpContext.RequestAborted);
            return db.UpdateArtistImage(artistId, path)
                ? Results.NoContent()
                : Results.NotFound();
        });

        /// <summary>
        /// Serves artwork for a track looked up by file path (encoded in <c>?p=</c>).
        /// </summary>
        api.MapGet("/artwork/track", (string p, int? size) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var artworkPaths = db.GetArtworkPathsByTrackPath(p);
            if (artworkPaths is null) return Results.NotFound();

            var path = SelectExistingArtworkPath(artworkPaths, size);

            if (path is null) return Results.NotFound();
            return Results.File(path, GuessImageMimeType(path), enableRangeProcessing: false);
        });

        /// <summary>
        /// Serves artwork for a track looked up by its database ID.
        /// Use <c>?size=96</c> or <c>?size=320</c> for thumbnails; omit for the original.
        /// </summary>
        api.MapGet("/artwork/track/{trackId:long}", (long trackId, int? size) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetTrackById(trackId);
            if (track is null) return Results.NotFound();

            var artworkPaths = db.GetArtworkPathsByTrackPath(track.Path);
            if (artworkPaths is null) return Results.NotFound();

            var path = SelectExistingArtworkPath(artworkPaths, size);
            if (path is null) return Results.NotFound();
            return Results.File(path, GuessImageMimeType(path), enableRangeProcessing: false);
        });
    }

    private static async Task<IResult> BuildStreamResult(
        string trackPath,
        string? sourcePath,
        double? segmentStart,
        double? segmentEnd,
        HttpContext ctx,
        ILogger logger,
        double? seekSeconds = null)
    {
        // CUE virtual track: transcode the segment to FLAC via FFmpeg
        if (trackPath.StartsWith("cue://", StringComparison.OrdinalIgnoreCase)
            && sourcePath is not null)
        {
            return await TranscodeCueSegmentAsync(
                sourcePath, (segmentStart ?? 0) + (seekSeconds ?? 0), segmentEnd, ctx, logger);
        }

        if (!File.Exists(trackPath)) return Results.NotFound();

        // Server-side seek: a remote client navigating within the track requests the
        // stream with ?ss=<seconds>. Seeking the local file and transcoding from that
        // offset is far faster than the client binary-searching a seektable-less file
        // over HTTP. The client then decodes the offset stream from position 0.
        if (seekSeconds is > 0)
            return await TranscodeFromOffsetAsync(trackPath, seekSeconds.Value, ctx, logger);

        // Regular file: serve with byte-range support
        var mime = GuessMimeType(trackPath);
        return Results.File(trackPath, mime, enableRangeProcessing: true);
    }

    /// <summary>
    /// Pipes the audio file re-encoded to FLAC from <paramref name="startSeconds"/> using a fast
    /// local input seek. Used for remote client seeking within a track.
    /// </summary>
    /// <param name="sourcePath">Local audio file path.</param>
    /// <param name="startSeconds">Seek offset in seconds.</param>
    /// <param name="ctx">HTTP context whose response body receives the stream.</param>
    /// <param name="logger">Logger for FFmpeg diagnostics.</param>
    /// <returns>An empty result after the FFmpeg output has been piped to the response.</returns>
    private static Task<IResult> TranscodeFromOffsetAsync(
        string sourcePath,
        double startSeconds,
        HttpContext ctx,
        ILogger logger)
        => TranscodeCueSegmentAsync(sourcePath, startSeconds, null, ctx, logger);

    private static string? SelectExistingArtworkPath(ArtworkPaths? artworkPaths, int? size)
    {
        if (artworkPaths is null)
            return null;

        string?[] candidates = size switch
        {
            96  => [artworkPaths.Thumb96Path, artworkPaths.OriginalPath, artworkPaths.Thumb320Path],
            320 => [artworkPaths.Thumb320Path, artworkPaths.OriginalPath, artworkPaths.Thumb96Path],
            _   => [artworkPaths.OriginalPath, artworkPaths.Thumb320Path, artworkPaths.Thumb96Path]
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrEmpty(path) && File.Exists(path));
    }

    /// <summary>
    /// Pipes FFmpeg output for a CUE segment directly into the HTTP response body.
    /// Output format is FLAC (lossless, streamable).
    /// </summary>
    private static async Task<IResult> TranscodeCueSegmentAsync(
        string sourcePath,
        double startSeconds,
        double? endSeconds,
        HttpContext ctx,
        ILogger logger)
    {
        if (!File.Exists(sourcePath)) return Results.NotFound();

        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "audio/flac";

        var args = BuildFfmpegArgs(sourcePath, startSeconds, endSeconds);
        logger.LogDebug("FFmpeg transcode: {Args}", args);

        using var ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = "ffmpeg",
                Arguments              = args,
                WorkingDirectory       = FfmpegLocator.GetSafeWorkingDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            }
        };

        try
        {
            ffmpeg.Start();
            await ffmpeg.StandardOutput.BaseStream.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
            await ffmpeg.WaitForExitAsync(ctx.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected (e.g. seeked again); stop transcoding promptly.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FFmpeg transcode failed for {Source}", sourcePath);
        }
        finally
        {
            try
            {
                if (!ffmpeg.HasExited)
                    ffmpeg.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process already gone.
            }
        }

        return Results.Empty;
    }

    private static async Task<byte[]> ReadImageUploadAsync(HttpRequest request)
    {
        const int maxImageBytes = 20 * 1024 * 1024;
        await using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, request.HttpContext.RequestAborted);
        return buffer.Length is 0 or > maxImageBytes
            ? []
            : buffer.ToArray();
    }

    private static string BuildFfmpegArgs(string source, double start, double? end)
    {
        var startArg = start > 0
            ? $"-ss {start.ToString("F6", CultureInfo.InvariantCulture)} "
            : string.Empty;

        var durationArg = string.Empty;
        if (end.HasValue && end.Value > start)
        {
            var duration = end.Value - start;
            durationArg = $"-t {duration.ToString("F6", CultureInfo.InvariantCulture)} ";
        }

        return $"{startArg}-i \"{source}\" {durationArg}-c:a flac -f flac pipe:1";
    }

    private static string GuessMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".flac" => "audio/flac",
            ".mp3"  => "audio/mpeg",
            ".aac"  => "audio/aac",
            ".ogg"  => "audio/ogg",
            ".opus" => "audio/ogg",
            ".wav"  => "audio/wav",
            ".m4a"  => "audio/mp4",
            ".aiff" or ".aif" => "audio/aiff",
            ".dsf"  => "audio/x-dsf",
            ".dff"  => "audio/x-dff",
            ".wv"   => "audio/x-wavpack",
            _       => "application/octet-stream"
        };

    private static string GuessImageMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
}
