using System.IO;
using System.Net.Http;
using Orynivo.Library;
using Orynivo.Streaming;

namespace Orynivo;

/// <summary>Currently playing track passed to a now-playing metadata provider.</summary>
/// <param name="TrackId">Provider-local track identifier, or <see langword="null"/> when unknown.</param>
/// <param name="PlaybackPath">Local file path or remote stream URL identifying the track.</param>
/// <param name="Title">Track title, or <see langword="null"/>.</param>
/// <param name="Artist">Track artist, or <see langword="null"/>.</param>
/// <param name="Album">Album title, or <see langword="null"/>.</param>
/// <param name="Duration">Track duration in seconds, or <see langword="null"/>.</param>
internal sealed record NowPlayingTrackContext(
    long? TrackId,
    string PlaybackPath,
    string? Title,
    string? Artist,
    string? Album,
    double? Duration);

/// <summary>Artist whose profile a now-playing metadata provider should resolve.</summary>
/// <param name="ArtistId">Provider-local artist identifier, or <see langword="null"/> when unknown.</param>
/// <param name="ArtistName">Display artist name.</param>
internal sealed record NowPlayingArtistContext(long? ArtistId, string ArtistName);

/// <summary>Plain and synchronised lyrics returned by a now-playing metadata provider.</summary>
/// <param name="Plain">Unsynchronised plain-text lyrics, or <see langword="null"/>.</param>
/// <param name="Synced">LRC-formatted synchronised lyrics, or <see langword="null"/>.</param>
/// <param name="FetchedAt">Unix-seconds timestamp of the last lyrics lookup, or <see langword="null"/>.</param>
internal sealed record NowPlayingLyrics(string? Plain, string? Synced, long? FetchedAt)
{
    /// <summary>Gets a value indicating whether any plain or synchronised lyrics are present.</summary>
    public bool HasLyrics =>
        !string.IsNullOrWhiteSpace(Plain) || !string.IsNullOrWhiteSpace(Synced);
}

/// <summary>Displayable artwork file paths for the currently playing track.</summary>
/// <param name="ThumbnailPath">Local file path of a small thumbnail, or <see langword="null"/>.</param>
/// <param name="LargePath">Local file path of a larger image for backgrounds, or <see langword="null"/>.</param>
internal sealed record NowPlayingArtwork(string? ThumbnailPath, string? LargePath);

/// <summary>Resolved artist profile returned by a now-playing metadata provider.</summary>
/// <param name="Biography">Biography text, or <see langword="null"/>.</param>
/// <param name="SourceUrl">Canonical biography source URL, or <see langword="null"/>.</param>
/// <param name="Language">Profile language code, or <see langword="null"/>.</param>
/// <param name="ImagePath">Local file path of a displayable artist image, or <see langword="null"/>.</param>
/// <param name="FetchedAt">Unix-seconds timestamp of the last profile lookup, or <see langword="null"/>.</param>
/// <param name="ImageIsManual">Whether the stored image was selected manually.</param>
internal sealed record NowPlayingArtistProfile(
    string? Biography,
    string? SourceUrl,
    string? Language,
    string? ImagePath,
    long? FetchedAt,
    bool ImageIsManual);

/// <summary>
/// Retrieves and caches lyrics and artist profiles for the currently playing track.
/// Implemented once for the local library and once for a remote Orynivo Server so the
/// transport lyrics and artist-info views work the same way regardless of the source.
/// </summary>
internal interface INowPlayingMetadataProvider
{
    /// <summary>Returns already-cached lyrics for the track without contacting LRCLIB.</summary>
    /// <param name="track">Currently playing track.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cached lyrics, or <see langword="null"/> when none are stored.</returns>
    Task<NowPlayingLyrics?> GetCachedLyricsAsync(
        NowPlayingTrackContext track,
        CancellationToken cancellationToken = default);

    /// <summary>Downloads lyrics from LRCLIB and stores them in the provider's cache.</summary>
    /// <param name="track">Currently playing track.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The downloaded lyrics, or <see langword="null"/> when LRCLIB has none.</returns>
    Task<NowPlayingLyrics?> DownloadLyricsAsync(
        NowPlayingTrackContext track,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves displayable cover artwork for the currently playing track.</summary>
    /// <param name="track">Currently playing track.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artwork file paths, or <see langword="null"/> when no artwork is available.</returns>
    Task<NowPlayingArtwork?> GetArtworkAsync(
        NowPlayingTrackContext track,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves an artist profile, downloading and caching it when stale or forced.</summary>
    /// <param name="artist">Artist to resolve.</param>
    /// <param name="language">Preferred profile language code.</param>
    /// <param name="forceRefresh">Whether to always download a fresh profile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved profile, or <see langword="null"/> when the artist is unknown.</returns>
    Task<NowPlayingArtistProfile?> GetArtistProfileAsync(
        NowPlayingArtistContext artist,
        string language,
        bool forceRefresh,
        CancellationToken cancellationToken = default);
}

/// <summary>Now-playing metadata provider backed by the local SQLite library.</summary>
internal sealed class LocalNowPlayingMetadataProvider : INowPlayingMetadataProvider
{
    /// <inheritdoc/>
    public Task<NowPlayingLyrics?> GetCachedLyricsAsync(
        NowPlayingTrackContext track,
        CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        var record = db.GetByPath(track.PlaybackPath);
        if (record is null)
            return Task.FromResult<NowPlayingLyrics?>(null);
        return Task.FromResult<NowPlayingLyrics?>(new NowPlayingLyrics(
            record.DownloadedLyrics ?? record.Lyrics,
            record.SyncedLyrics,
            record.LyricsFetchedAt));
    }

    /// <inheritdoc/>
    public async Task<NowPlayingLyrics?> DownloadLyricsAsync(
        NowPlayingTrackContext track,
        CancellationToken cancellationToken = default)
    {
        TrackRecord? record;
        using (var db = AudioDatabase.OpenDefault())
            record = db.GetByPath(track.PlaybackPath);
        if (record is null)
            return null;

        var result = await LyricsService.DownloadAsync(record, cancellationToken);
        using (var db = AudioDatabase.OpenDefault())
            db.UpdateDownloadedLyrics(track.PlaybackPath, result?.PlainLyrics, result?.SyncedLyrics, "LRCLIB");
        return result is null
            ? null
            : new NowPlayingLyrics(result.PlainLyrics, result.SyncedLyrics, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <inheritdoc/>
    public Task<NowPlayingArtwork?> GetArtworkAsync(
        NowPlayingTrackContext track,
        CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        var paths = db.GetArtworkPathsByTrackPath(track.PlaybackPath);
        if (paths is null)
            return Task.FromResult<NowPlayingArtwork?>(null);
        return Task.FromResult<NowPlayingArtwork?>(new NowPlayingArtwork(
            paths.Thumb96Path,
            paths.Thumb320Path ?? paths.OriginalPath ?? paths.Thumb96Path));
    }

    /// <inheritdoc/>
    public async Task<NowPlayingArtistProfile?> GetArtistProfileAsync(
        NowPlayingArtistContext artist,
        string language,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        if (artist.ArtistId is not long artistId)
            return null;

        ArtistInfo? info;
        using (var db = AudioDatabase.OpenDefault())
            info = db.GetArtistById(artistId);
        if (info is null)
            return null;

        if (NowPlayingMetadataHelpers.NeedsProfileDownload(
                forceRefresh, info.Biography, info.ProfileLanguage, info.ProfileFetchedAt, language))
        {
            var profile = await ArtistProfileService.DownloadAsync(
                artistId, info.Artist, language, downloadImage: !info.ImageIsManual, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using var db = AudioDatabase.OpenDefault();
            db.UpdateArtistProfile(artistId, profile?.Biography, profile?.ImagePath, profile?.SourceUrl, language);
            info = db.GetArtistById(artistId);
            if (info is null)
                return null;
        }

        return new NowPlayingArtistProfile(
            info.Biography, info.SourceUrl, info.ProfileLanguage, info.ImagePath,
            info.ProfileFetchedAt, info.ImageIsManual);
    }
}

/// <summary>Now-playing metadata provider backed by a remote Orynivo Server.</summary>
internal sealed class OrynivoServerNowPlayingMetadataProvider : INowPlayingMetadataProvider
{
    private static readonly HttpClient ImageClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly OrynivoServerSettings _server;
    private readonly OrynivoServerClient _client;

    /// <summary>Initializes a new instance of the <see cref="OrynivoServerNowPlayingMetadataProvider"/> class.</summary>
    /// <param name="server">Remote server connection settings.</param>
    /// <param name="client">HTTP client wrapper for the remote server.</param>
    public OrynivoServerNowPlayingMetadataProvider(OrynivoServerSettings server, OrynivoServerClient client)
    {
        _server = server;
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<NowPlayingLyrics?> GetCachedLyricsAsync(
        NowPlayingTrackContext track,
        CancellationToken cancellationToken = default)
    {
        if (track.TrackId is not long trackId)
            return null;
        var lyrics = await _client.GetTrackLyricsAsync(_server, trackId, cancellationToken);
        return lyrics is null
            ? null
            : new NowPlayingLyrics(lyrics.PlainLyrics, lyrics.SyncedLyrics, lyrics.FetchedAt);
    }

    /// <inheritdoc/>
    public async Task<NowPlayingLyrics?> DownloadLyricsAsync(
        NowPlayingTrackContext track,
        CancellationToken cancellationToken = default)
    {
        var result = await LyricsService.DownloadAsync(
            track.Title, track.Artist, track.Album, track.Duration, cancellationToken);
        if (track.TrackId is long trackId)
            await _client.UploadTrackLyricsAsync(
                _server, trackId, result?.PlainLyrics, result?.SyncedLyrics, cancellationToken);
        return result is null
            ? null
            : new NowPlayingLyrics(result.PlainLyrics, result.SyncedLyrics, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <inheritdoc/>
    public async Task<NowPlayingArtwork?> GetArtworkAsync(
        NowPlayingTrackContext track,
        CancellationToken cancellationToken = default)
    {
        if (track.TrackId is not long trackId)
            return null;
        try
        {
            var url = OrynivoServerClient.GetTrackArtworkUrl(_server, trackId, 320);
            var bytes = await ImageClient.GetByteArrayAsync(url, cancellationToken);
            if (bytes.Length == 0)
                return null;
            var path = AppPaths.GetDataPath("remote-artworks", $"track-art-{_server.Id}-{trackId}.img");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return new NowPlayingArtwork(path, path);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<NowPlayingArtistProfile?> GetArtistProfileAsync(
        NowPlayingArtistContext artist,
        string language,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        if (artist.ArtistId is not long artistId)
            return null;

        var serverArtist = await _client.GetArtistAsync(_server, artistId, cancellationToken);
        var imageIsManual = serverArtist?.ImageIsManual ?? false;

        if (NowPlayingMetadataHelpers.NeedsProfileDownload(
                forceRefresh,
                serverArtist?.Biography,
                serverArtist?.ProfileLanguage,
                serverArtist?.ProfileFetchedAt,
                language))
        {
            var profile = await ArtistProfileService.DownloadAsync(
                artistId, artist.ArtistName, language, downloadImage: !imageIsManual, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            byte[]? imageData = null;
            string? imageMime = null;
            if (!string.IsNullOrWhiteSpace(profile?.ImagePath) && File.Exists(profile.ImagePath))
            {
                imageData = await File.ReadAllBytesAsync(profile.ImagePath, cancellationToken);
                imageMime = NowPlayingMetadataHelpers.GuessImageMimeType(profile.ImagePath);
            }

            await _client.UpdateArtistProfileAsync(
                _server, artistId, profile?.Biography, profile?.SourceUrl,
                profile?.Language ?? language, imageData, imageMime, cancellationToken);

            return new NowPlayingArtistProfile(
                profile?.Biography, profile?.SourceUrl, profile?.Language ?? language,
                profile?.ImagePath, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), imageIsManual);
        }

        var imagePath = serverArtist?.HasImage == true
            ? await DownloadServerArtistImageAsync(artistId, cancellationToken)
            : null;
        return new NowPlayingArtistProfile(
            serverArtist?.Biography, serverArtist?.SourceUrl, serverArtist?.ProfileLanguage,
            imagePath, serverArtist?.ProfileFetchedAt, imageIsManual);
    }

    /// <summary>Downloads the server-cached artist image to the local remote-artwork cache for display.</summary>
    /// <param name="artistId">Database ID of the artist on the remote server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The local cache file path, or <see langword="null"/> when the download fails.</returns>
    private async Task<string?> DownloadServerArtistImageAsync(long artistId, CancellationToken cancellationToken)
    {
        try
        {
            var url = OrynivoServerClient.GetArtistArtworkUrl(_server, artistId);
            var bytes = await ImageClient.GetByteArrayAsync(url, cancellationToken);
            if (bytes.Length == 0)
                return null;
            var path = AppPaths.GetDataPath("remote-artworks", $"artist-info-{_server.Id}-{artistId}.img");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return path;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Shared helpers for now-playing metadata providers.</summary>
internal static class NowPlayingMetadataHelpers
{
    /// <summary>Determines whether an artist profile must be (re-)downloaded.</summary>
    /// <param name="forceRefresh">Whether a refresh was explicitly requested.</param>
    /// <param name="biography">Currently cached biography, or <see langword="null"/>.</param>
    /// <param name="profileLanguage">Language of the cached profile, or <see langword="null"/>.</param>
    /// <param name="fetchedAt">Unix-seconds timestamp of the cached profile, or <see langword="null"/>.</param>
    /// <param name="language">Requested profile language.</param>
    /// <returns><see langword="true"/> when a download is required.</returns>
    public static bool NeedsProfileDownload(
        bool forceRefresh,
        string? biography,
        string? profileLanguage,
        long? fetchedAt,
        string language)
    {
        if (forceRefresh || string.IsNullOrWhiteSpace(biography))
            return true;
        if (!string.Equals(profileLanguage, language, StringComparison.OrdinalIgnoreCase))
            return true;
        if (fetchedAt is not long timestamp)
            return true;
        return DateTimeOffset.FromUnixTimeSeconds(timestamp) < DateTimeOffset.UtcNow.AddDays(-90);
    }

    /// <summary>Guesses an image MIME type from a file extension.</summary>
    /// <param name="path">Image file path.</param>
    /// <returns>A MIME type string.</returns>
    public static string GuessImageMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };
}
