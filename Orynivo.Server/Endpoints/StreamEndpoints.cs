using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
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
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Orynivo.Server.Stream");
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetTrackById(trackId);
            if (track is null) return Results.NotFound();

            return await BuildStreamResult(track.Path, track.SourcePath,
                track.SegmentStart, track.SegmentEnd, ctx, logger);
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

            var path = size switch
            {
                96  => album.ThumbnailPath,
                320 => album.ThumbnailPath,
                _   => album.ArtworkPath
            };

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Results.NotFound();
            return Results.File(path, "image/jpeg", enableRangeProcessing: false);
        });

        /// <summary>
        /// Serves artwork for a track looked up by file path (encoded in <c>?p=</c>).
        /// </summary>
        api.MapGet("/artwork/track", (string p, int? size) =>
        {
            using var db = AudioDatabase.OpenDefault();
            var artworkPaths = db.GetArtworkPathsByTrackPath(p);
            if (artworkPaths is null) return Results.NotFound();

            var path = size switch
            {
                96  => artworkPaths.Thumb96Path,
                320 => artworkPaths.Thumb320Path,
                _   => artworkPaths.OriginalPath
            };

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Results.NotFound();
            return Results.File(path, "image/jpeg", enableRangeProcessing: false);
        });
    }

    private static async Task<IResult> BuildStreamResult(
        string trackPath,
        string? sourcePath,
        double? segmentStart,
        double? segmentEnd,
        HttpContext ctx,
        ILogger logger)
    {
        // CUE virtual track: transcode the segment to FLAC via FFmpeg
        if (trackPath.StartsWith("cue://", StringComparison.OrdinalIgnoreCase)
            && sourcePath is not null)
        {
            return await TranscodeCueSegmentAsync(
                sourcePath, segmentStart ?? 0, segmentEnd, ctx, logger);
        }

        // Regular file: serve with byte-range support
        if (!File.Exists(trackPath)) return Results.NotFound();
        var mime = GuessMimeType(trackPath);
        return Results.File(trackPath, mime, enableRangeProcessing: true);
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
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            }
        };

        try
        {
            ffmpeg.Start();
            await ffmpeg.StandardOutput.BaseStream.CopyToAsync(ctx.Response.Body);
            await ffmpeg.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FFmpeg transcode failed for {Source}", sourcePath);
        }

        return Results.Empty;
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
}
