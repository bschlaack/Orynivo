using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Orynivo.Library;

/// <summary>Artist row joined from the <c>artists</c> table, including optional biography and image data.</summary>
public sealed record ArtistInfo(
    long Id,
    string Artist,
    bool IsFavorite,
    string? Biography,
    string? ImagePath,
    string? SourceUrl,
    string? ProfileLanguage,
    long? ProfileFetchedAt,
    bool ImageIsManual);

/// <summary>Distinct album entry used by the album-grid and album-list views.</summary>
public sealed record AlbumInfo(
    long Id,
    string Album,
    string? DisplayArtist,
    int? Year,
    string? ArtworkPath,
    string? ThumbnailPath,
    bool IsFavorite,
    long? ArtistId = null);

/// <summary>Lightweight track row for list and search tables; omits artwork and lyrics payloads.</summary>
/// <param name="Path">Absolute audio-file path.</param>
/// <param name="FileName">File name including extension.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Primary artist.</param>
/// <param name="Album">Album title.</param>
/// <param name="AlbumArtist">Album artist.</param>
/// <param name="Genre">Genre text.</param>
/// <param name="Format">Container format.</param>
/// <param name="Bitrate">Encoded bitrate in kbps.</param>
/// <param name="Duration">Duration in seconds.</param>
/// <param name="SortTitle">Title used for sorting.</param>
/// <param name="Id">Database track identifier.</param>
/// <param name="IsFavorite">Whether the track is marked as a favorite.</param>
/// <param name="Year">Release year.</param>
/// <param name="TrackNumber">Track number.</param>
/// <param name="TrackTotal">Total number of tracks.</param>
/// <param name="DiscNumber">Disc number.</param>
/// <param name="DiscTotal">Total number of discs.</param>
/// <param name="SampleRate">Source sample rate in Hz.</param>
/// <param name="BitDepth">Source bit depth.</param>
/// <param name="Channels">Source channel count.</param>
/// <param name="Composer">Composer text.</param>
/// <param name="Bpm">Beats per minute.</param>
/// <param name="FileSize">File size in bytes.</param>
/// <param name="AddedAt">Library-added timestamp in Unix seconds.</param>
/// <param name="ReplayGainTrack">Track ReplayGain value.</param>
/// <param name="ReplayGainAlbum">Album ReplayGain value.</param>
/// <param name="ArtistId">Database identifier of the primary artist, or <see langword="null"/>.</param>
/// <param name="AlbumId">Database identifier of the album, or <see langword="null"/>.</param>
public sealed record TrackListInfo(
    string Path,
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
    long Id,
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
    long AddedAt,
    string? ReplayGainTrack,
    string? ReplayGainAlbum,
    long? ArtistId = null,
    long? AlbumId = null);

/// <summary>Minimal track row for filter/facet building; carries only classification fields.</summary>
/// <param name="Id">Track database identifier.</param>
/// <param name="IsFavorite">Whether the track is marked as a favourite.</param>
/// <param name="Genre">Stored genre text.</param>
/// <param name="Format">Lowercase container format.</param>
/// <param name="Bitrate">Encoded bitrate in kbps.</param>
/// <param name="SourceKey">Stable source key, currently <c>local</c> for database rows.</param>
public sealed record TrackFacetInfo(
    long Id,
    bool IsFavorite,
    string? Genre,
    string? Format,
    int? Bitrate,
    string SourceKey = "local");

/// <summary>Compact metadata and playback-history values used to resolve smart playlists.</summary>
/// <param name="Id">Track database identifier.</param>
/// <param name="IsFavorite">Whether the track is marked as a favourite.</param>
/// <param name="Genre">Stored genre text.</param>
/// <param name="Format">Lowercase container format.</param>
/// <param name="Bitrate">Encoded bitrate in kbps.</param>
/// <param name="Year">Release year.</param>
/// <param name="Artist">Primary artist name.</param>
/// <param name="Album">Album title.</param>
/// <param name="Duration">Track duration in seconds.</param>
/// <param name="AddedAt">Library-add timestamp in Unix seconds.</param>
/// <param name="PlayCount">Number of recorded local-track playback sessions.</param>
/// <param name="LastPlayedAt">Most recent playback timestamp in Unix seconds.</param>
/// <param name="SortTitle">Resolved title used for alphabetical ordering.</param>
/// <param name="SourceKey">Stable source key used by source-aware smart playlists.</param>
public sealed record SmartPlaylistTrackInfo(
    long Id,
    bool IsFavorite,
    string? Genre,
    string? Format,
    int? Bitrate,
    int? Year,
    string? Artist,
    string? Album,
    double? Duration,
    long AddedAt,
    int PlayCount,
    long? LastPlayedAt,
    string SortTitle,
    string SourceKey = "local");

/// <summary>File-system paths for the three artwork variants stored per album (original, 96-px thumb, 320-px thumb).</summary>
public sealed record ArtworkPaths(string? OriginalPath, string? Thumb96Path, string? Thumb320Path);

/// <summary>Minimal track row for list-based views that do not need cover art or lyrics text.</summary>
public sealed record TrackLite(
    string  Path,
    string  SourcePath,
    string  FileName,
    string? Title,
    int?    DiscNumber,
    int?    TrackNumber)
{
    /// <summary>Returns <see cref="Title"/> when set, otherwise falls back to <see cref="FileName"/>.</summary>
    public string DisplayName => Title ?? FileName;
}

/// <summary>Album summary entry used by the dashboard's Recently Added widget.</summary>
/// <param name="Id">Database album identifier.</param>
/// <param name="Title">Album title.</param>
/// <param name="Artist">Display artist name.</param>
/// <param name="ThumbPath">Local 96-px thumbnail path, or <see langword="null"/> when no artwork exists.</param>
/// <param name="ArtistId">Database album-artist identifier, or <see langword="null"/>.</param>
/// <param name="AddedAt">Unix timestamp of the most recently added track in the album.</param>
/// <param name="IsFavorite">Whether the album is flagged as a favorite.</param>
/// <param name="ArtworkPath">Local 320-px thumbnail path for a crisp artwork card, or <see langword="null"/>.</param>
public sealed record RecentAlbumInfo(long Id, string Title, string Artist, string? ThumbPath, long? ArtistId = null, long AddedAt = 0, bool IsFavorite = false, string? ArtworkPath = null);

/// <summary>Compact library counters displayed in the dashboard hero.</summary>
public sealed record DashboardLibrarySummary(int AlbumCount, int TrackCount, int ArtistCount, int FavoriteCount);

/// <summary>Aggregated listening data for a single calendar day.</summary>
public sealed record CalendarDayData(int Day, double TotalSeconds, IReadOnlyList<string> TopGenres);

/// <summary>Single row from the listening-history log, enriched with track and artist display fields.</summary>
public sealed record DailyHistoryEntry(
    long Id,
    long? TrackId,
    string Path,
    DateTime StartedAt,
    double ListenedSeconds,
    double? DurationSeconds,
    string MediaType,
    string Title,
    string? Artist,
    string? Album,
    long? ArtistId,
    long? AlbumId,
    string? ExternalId);

/// <summary>Aggregated listening statistics for a single album across every playback source.</summary>
/// <param name="Title">Album display title.</param>
/// <param name="Artist">Album artist display name.</param>
/// <param name="Seconds">Total listened seconds in the requested period.</param>
/// <param name="LocalAlbumId">Local album identifier when a local library album matched, otherwise <see langword="null"/>.</param>
/// <param name="LocalArtistId">Local album-artist identifier when known, otherwise <see langword="null"/>.</param>
/// <param name="ThumbPath">Local artwork thumbnail path when available, otherwise <see langword="null"/>.</param>
/// <param name="ExternalId">Representative playback-history external identifier for remote/Plex resolution, or <see langword="null"/>.</param>
/// <param name="Path">Representative playback path for remote/Plex resolution, or <see langword="null"/>.</param>
public sealed record TopAlbumStat(
    string Title,
    string Artist,
    double Seconds,
    long? LocalAlbumId,
    long? LocalArtistId,
    string? ThumbPath,
    string? ExternalId,
    string? Path);

/// <summary>Aggregated listening statistics for a single artist across every playback source.</summary>
/// <param name="Name">Artist display name.</param>
/// <param name="Seconds">Total listened seconds in the requested period.</param>
/// <param name="LocalArtistId">Local artist identifier when a local library artist matched, otherwise <see langword="null"/>.</param>
/// <param name="ExternalId">Representative playback-history external identifier for remote/Plex resolution, or <see langword="null"/>.</param>
/// <param name="Path">Representative playback path for remote/Plex resolution, or <see langword="null"/>.</param>
public sealed record TopArtistStat(
    string Name,
    double Seconds,
    long? LocalArtistId,
    string? ExternalId,
    string? Path);

/// <summary>Result returned after an artist-name normalisation run.</summary>
public sealed record ArtistNormalizationResult(int MergedArtists, int UpdatedTracks);

/// <summary>Result returned after renaming or merging an artist.</summary>
public sealed record ArtistRenameResult(long ArtistId, string ArtistName, bool Merged);

/// <summary>Persisted playback queue state loaded from the library database.</summary>
/// <param name="Paths">Queue paths in playback order.</param>
/// <param name="CurrentIndex">Zero-based current queue index, or <c>-1</c> when no current item is stored.</param>
public sealed record PlaybackQueueSnapshot(IReadOnlyList<string> Paths, int CurrentIndex);

/// <summary>
/// Manages the SQLite audio library database. One instance per application lifetime.
/// The database file is stored at <c>%LOCALAPPDATA%\Orynivo\library.db</c>.
/// </summary>
public sealed class AudioDatabase : IDisposable
{
    private readonly SqliteConnection _conn;
    private Dictionary<string, (long Id, string Name)>? _artistsByComparisonKey;
    private Dictionary<long, string>? _artistNamesById;

    /// <summary>
    /// Opens (or creates) the SQLite database at <paramref name="dbPath"/>,
    /// applies connection pragmas, and ensures the schema is up to date.
    /// </summary>
    /// <param name="dbPath">Absolute path to the <c>.db</c> file.</param>
    public AudioDatabase(string dbPath)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath)!);
        _conn = new SqliteConnection($"Data Source={dbPath}");
        using (Orynivo.StartupDiagnostics.Time("AudioDatabase: SQLite open"))
            _conn.Open();
        using (Orynivo.StartupDiagnostics.Time("AudioDatabase: ApplyPragmas"))
            ApplyPragmas();
        using (Orynivo.StartupDiagnostics.Time("AudioDatabase: EnsureSchema"))
            EnsureSchema();
    }

    // ------------------------------------------------------------------
    // Öffentliche Factory für den Standard-Speicherort
    // ------------------------------------------------------------------

    /// <summary>Opens the database at the default data path (<c>%LOCALAPPDATA%\Orynivo\library.db</c>).</summary>
    public static AudioDatabase OpenDefault()
    {
        var path = AppPaths.GetDataPath("library.db");
        return new AudioDatabase(path);
    }

    private void MigrateLegacyCachePaths()
    {
        const string migrationKey = "cache_paths_orynivo_v1";
        if (string.Equals(GetMeta(migrationKey), "done", StringComparison.Ordinal))
            return;

        if (!string.Equals(AppPaths.LegacyDataRoot, AppPaths.DataRoot, StringComparison.OrdinalIgnoreCase))
        {
            using var transaction = _conn.BeginTransaction();
            foreach (var (table, column) in new[]
            {
                ("artworks", "original_path"),
                ("artworks", "thumb_96_path"),
                ("artworks", "thumb_320_path"),
                ("artists", "image_path")
            })
            {
                using var command = _conn.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    UPDATE {table}
                    SET {column} = REPLACE({column}, $legacyRoot, $dataRoot)
                    WHERE {column} LIKE $legacyPrefix;
                    """;
                command.Parameters.AddWithValue("$legacyRoot", AppPaths.LegacyDataRoot);
                command.Parameters.AddWithValue("$dataRoot", AppPaths.DataRoot);
                command.Parameters.AddWithValue(
                    "$legacyPrefix",
                    AppPaths.LegacyDataRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "%");
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        SetMeta(migrationKey, "done");
    }

    // ------------------------------------------------------------------
    // Einfügen / Aktualisieren
    // ------------------------------------------------------------------

    /// <summary>
    /// Inserts or updates a track and its associated artist, album, and artwork records in a single transaction.
    /// Artist and album artist names are normalised before being written.
    /// </summary>
    /// <param name="track">The track record to upsert.</param>
    public void Upsert(TrackRecord track)
    {
        track.Title = TrimToNull(track.Title);
        track.SortTitle = TrimToNull(track.SortTitle);
        track.Artist = ArtistNameNormalizer.NormalizeDisplayName(track.Artist);
        track.AlbumArtist = ArtistNameNormalizer.NormalizeDisplayName(track.AlbumArtist ?? track.Artist);
        using var tx = _conn.BeginTransaction();
        try
        {
            var artistId  = EnsureArtist(track.Artist, tx);
            track.Artist = GetCanonicalArtistName(artistId, track.Artist);
            var albumArtistId = EnsureArtist(GetFirstArtist(track.AlbumArtist ?? track.Artist), tx);
            track.AlbumArtist = GetCanonicalArtistName(albumArtistId, track.AlbumArtist ?? track.Artist);
            var artworkId = EnsureArtwork(track.CoverData, track.CoverMimeType, tx);
            var albumId = EnsureAlbum(
                track.Album,
                track.AlbumArtist ?? track.Artist,
                track.Year,
                artworkId,
                GetPhysicalAlbumDirectory(track.SourcePath ?? track.Path),
                tx);

            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
            INSERT INTO tracks (
                path, source_path, cue_path, segment_start, segment_end,
                file_name, file_size, modified_at, added_at,
                format, duration, sample_rate, bit_depth, channels,
                bitrate, is_lossless, is_dsd, dsd_rate,
                title, sort_title, artist, sort_artist,
                album_artist, sort_album_artist, album, sort_album,
                genre, year, date, track_number, track_total,
                disc_number, disc_total, composer, conductor,
                lyricist, lyrics, comment, copyright, publisher,
                encoded_by, encoding_settings, bpm, compilation,
                isrc, language, mood,
                replay_gain_track, replay_gain_album,
                musicbrainz_track_id, musicbrainz_release_id,
                musicbrainz_artist_id, acoustid_fingerprint,
                has_cover, cover_mime_type, cover_data,
                artist_id, album_id
            ) VALUES (
                $path, $source_path, $cue_path, $segment_start, $segment_end,
                $file_name, $file_size, $modified_at, $added_at,
                $format, $duration, $sample_rate, $bit_depth, $channels,
                $bitrate, $is_lossless, $is_dsd, $dsd_rate,
                $title, $sort_title, $artist, $sort_artist,
                $album_artist, $sort_album_artist, $album, $sort_album,
                $genre, $year, $date, $track_number, $track_total,
                $disc_number, $disc_total, $composer, $conductor,
                $lyricist, $lyrics, $comment, $copyright, $publisher,
                $encoded_by, $encoding_settings, $bpm, $compilation,
                $isrc, $language, $mood,
                $replay_gain_track, $replay_gain_album,
                $musicbrainz_track_id, $musicbrainz_release_id,
                $musicbrainz_artist_id, $acoustid_fingerprint,
                $has_cover, $cover_mime_type, $cover_data,
                $artist_id, $album_id
            )
            ON CONFLICT(path) DO UPDATE SET
                source_path         = excluded.source_path,
                cue_path            = excluded.cue_path,
                segment_start       = excluded.segment_start,
                segment_end         = excluded.segment_end,
                file_name           = excluded.file_name,
                file_size           = excluded.file_size,
                modified_at         = excluded.modified_at,
                format              = excluded.format,
                duration            = excluded.duration,
                sample_rate         = excluded.sample_rate,
                bit_depth           = excluded.bit_depth,
                channels            = excluded.channels,
                bitrate             = excluded.bitrate,
                is_lossless         = excluded.is_lossless,
                is_dsd              = excluded.is_dsd,
                dsd_rate            = excluded.dsd_rate,
                title               = excluded.title,
                sort_title          = excluded.sort_title,
                artist              = excluded.artist,
                sort_artist         = excluded.sort_artist,
                album_artist        = excluded.album_artist,
                sort_album_artist   = excluded.sort_album_artist,
                album               = excluded.album,
                sort_album          = excluded.sort_album,
                genre               = excluded.genre,
                year                = excluded.year,
                date                = excluded.date,
                track_number        = excluded.track_number,
                track_total         = excluded.track_total,
                disc_number         = excluded.disc_number,
                disc_total          = excluded.disc_total,
                composer            = excluded.composer,
                conductor           = excluded.conductor,
                lyricist            = excluded.lyricist,
                lyrics              = excluded.lyrics,
                comment             = excluded.comment,
                copyright           = excluded.copyright,
                publisher           = excluded.publisher,
                encoded_by          = excluded.encoded_by,
                encoding_settings   = excluded.encoding_settings,
                bpm                 = excluded.bpm,
                compilation         = excluded.compilation,
                isrc                = excluded.isrc,
                language            = excluded.language,
                mood                = excluded.mood,
                replay_gain_track   = excluded.replay_gain_track,
                replay_gain_album   = excluded.replay_gain_album,
                musicbrainz_track_id    = excluded.musicbrainz_track_id,
                musicbrainz_release_id  = excluded.musicbrainz_release_id,
                musicbrainz_artist_id   = excluded.musicbrainz_artist_id,
                acoustid_fingerprint    = excluded.acoustid_fingerprint,
                has_cover           = excluded.has_cover,
                cover_mime_type     = excluded.cover_mime_type,
                cover_data          = excluded.cover_data,
                artist_id           = excluded.artist_id,
                album_id            = excluded.album_id;
            """;

            BindTrack(cmd, track);
            Add(cmd, "$artist_id", artistId);
            Add(cmd, "$album_id", albumId);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch
        {
            ClearArtistIdentityCache();
            throw;
        }
    }

    // ------------------------------------------------------------------
    // Lesen
    // ------------------------------------------------------------------

    public TrackRecord? GetByPath(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM tracks WHERE path = $path LIMIT 1;";
        cmd.Parameters.AddWithValue("$path", path);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapRow(reader) : null;
    }

    /// <summary>
    /// Returns the full <see cref="TrackRecord"/> for the given database identifier, or <see langword="null"/> when not found.
    /// </summary>
    /// <param name="id">Database track identifier.</param>
    /// <returns>The matching <see cref="TrackRecord"/>, or <see langword="null"/>.</returns>
    public TrackRecord? GetTrackById(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM tracks WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapRow(reader) : null;
    }

    /// <summary>
    /// Determines whether tracks below a library root need a one-time metadata refresh for ReplayGain tags.
    /// </summary>
    /// <param name="rootPath">Configured library root path.</param>
    /// <returns><see langword="true"/> when the root has not yet completed the ReplayGain metadata refresh.</returns>
    public bool NeedsReplayGainMetadataScan(string rootPath) =>
        !string.Equals(GetMeta(GetReplayGainScanKey(rootPath)), "done", StringComparison.Ordinal);

    /// <summary>
    /// Marks the one-time ReplayGain metadata refresh as complete for a library root.
    /// </summary>
    /// <param name="rootPath">Configured library root path.</param>
    public void MarkReplayGainMetadataScanned(string rootPath) =>
        SetMeta(GetReplayGainScanKey(rootPath), "done");

    private static string GetReplayGainScanKey(string rootPath)
    {
        var normalizedPath = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        return $"replay_gain_metadata_v1_{hash}";
    }

    public void UpdateDownloadedLyrics(
        string path,
        string? plainLyrics,
        string? syncedLyrics,
        string source)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE tracks
            SET downloaded_lyrics = $plain,
                synced_lyrics = $synced,
                lyrics_source = $source,
                lyrics_fetched_at = $fetched_at
            WHERE path = $path;
            """;
        Add(cmd, "$plain", (object?)plainLyrics ?? DBNull.Value);
        Add(cmd, "$synced", (object?)syncedLyrics ?? DBNull.Value);
        Add(cmd, "$source", source);
        Add(cmd, "$fetched_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Add(cmd, "$path", path);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Stores an album-level ReplayGain value for selected tracks.</summary>
    /// <param name="trackIds">Database track identifiers to update.</param>
    /// <param name="albumGain">Album-level ReplayGain value in dB text form.</param>
    /// <param name="onlyMissing">When true, existing album ReplayGain values are preserved.</param>
    /// <returns>The number of rows updated.</returns>
    public int UpdateReplayGainAlbumForTracks(
        IEnumerable<long> trackIds,
        string albumGain,
        bool onlyMissing = true)
    {
        var ids = trackIds.Distinct().ToList();
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(albumGain))
            return 0;

        var updated = 0;
        foreach (var batch in ids.Chunk(500))
        {
            using var cmd = _conn.CreateCommand();
            var parameters = batch.Select((id, index) =>
            {
                var name = $"$id{index}";
                Add(cmd, name, id);
                return name;
            }).ToList();
            cmd.CommandText = $"""
                UPDATE tracks
                SET replay_gain_album = $album_gain
                WHERE id IN ({string.Join(", ", parameters)})
                  AND ($only_missing = 0 OR replay_gain_album IS NULL OR TRIM(replay_gain_album) = '');
                """;
            Add(cmd, "$album_gain", albumGain);
            Add(cmd, "$only_missing", onlyMissing ? 1 : 0);
            updated += cmd.ExecuteNonQuery();
        }

        return updated;
    }

    /// <summary>Stores a track-level ReplayGain value for one track.</summary>
    /// <param name="trackId">Database track identifier.</param>
    /// <param name="trackGain">Track-level ReplayGain value in dB text form.</param>
    /// <param name="onlyMissing">When true, an existing track ReplayGain value is preserved.</param>
    /// <returns><see langword="true"/> when a row was updated.</returns>
    public bool UpdateReplayGainTrack(
        long trackId,
        string trackGain,
        bool onlyMissing = true)
    {
        if (string.IsNullOrWhiteSpace(trackGain))
            return false;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE tracks
            SET replay_gain_track = $track_gain
            WHERE id = $id
              AND ($only_missing = 0 OR replay_gain_track IS NULL OR TRIM(replay_gain_track) = '');
            """;
        Add(cmd, "$track_gain", trackGain);
        Add(cmd, "$id", trackId);
        Add(cmd, "$only_missing", onlyMissing ? 1 : 0);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>Stores downloaded plain and synchronised lyrics for the track with the given identifier.</summary>
    /// <param name="trackId">Database identifier of the track to update.</param>
    /// <param name="plainLyrics">Unsynchronised plain-text lyrics, or <see langword="null"/>.</param>
    /// <param name="syncedLyrics">LRC-formatted synchronised lyrics, or <see langword="null"/>.</param>
    /// <param name="source">Label identifying where the lyrics were obtained.</param>
    /// <returns><see langword="true"/> when a matching track row was updated.</returns>
    public bool UpdateDownloadedLyricsById(
        long trackId,
        string? plainLyrics,
        string? syncedLyrics,
        string source)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE tracks
            SET downloaded_lyrics = $plain,
                synced_lyrics = $synced,
                lyrics_source = $source,
                lyrics_fetched_at = $fetched_at
            WHERE id = $id;
            """;
        Add(cmd, "$plain", (object?)plainLyrics ?? DBNull.Value);
        Add(cmd, "$synced", (object?)syncedLyrics ?? DBNull.Value);
        Add(cmd, "$source", source);
        Add(cmd, "$fetched_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Add(cmd, "$id", trackId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public IEnumerable<TrackRecord> GetAll()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM tracks ORDER BY album_artist, album, disc_number, track_number;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            yield return MapRow(reader);
    }

    /// <summary>Loads tracks whose primary artist or album artist matches the specified artist.</summary>
    /// <param name="artistId">Artist identifier to match against track and album ownership.</param>
    /// <returns>Tracks affected by a rename or merge of the specified artist.</returns>
    public List<TrackRecord> GetTracksForArtistSearchIndex(long artistId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.*
            FROM tracks t
            LEFT JOIN albums al ON al.id = t.album_id
            WHERE t.artist_id = $artist_id
               OR al.artist_id = $artist_id
            ORDER BY t.album_artist, t.album, t.disc_number, t.track_number;
            """;
        Add(cmd, "$artist_id", artistId);
        using var reader = cmd.ExecuteReader();
        var result = new List<TrackRecord>();
        while (reader.Read())
            result.Add(MapRow(reader));
        return result;
    }

    /// <summary>Nur distinct artist – ohne Trackzeilen, BLOBs oder weitere Metadaten.</summary>
    public List<ArtistInfo> GetArtistsLite()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, is_favorite, NULL AS biography, image_path,
                   profile_source_url, profile_language, profile_fetched_at,
                   image_is_manual
            FROM artists
            ORDER BY CASE WHEN name = '' THEN 1 ELSE 0 END,
                     name COLLATE NOCASE;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<ArtistInfo>();
        while (reader.Read())
            result.Add(new ArtistInfo(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2) != 0,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.GetInt32(8) != 0));
        return result;
    }

    /// <summary>Loads compact artist rows including cached profile metadata but no track rows.</summary>
    /// <returns>Artists ordered by display name.</returns>
    public List<ArtistInfo> GetArtistsWithProfiles()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, is_favorite, biography, image_path,
                   profile_source_url, profile_language, profile_fetched_at,
                   image_is_manual
            FROM artists
            ORDER BY CASE WHEN name = '' THEN 1 ELSE 0 END,
                     name COLLATE NOCASE;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<ArtistInfo>();
        while (reader.Read())
            result.Add(new ArtistInfo(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2) != 0,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.GetInt32(8) != 0));
        return result;
    }

    public ArtistInfo? GetArtistById(long artistId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, is_favorite, biography, image_path,
                   profile_source_url, profile_language, profile_fetched_at,
                   image_is_manual
            FROM artists
            WHERE id = $id
            LIMIT 1;
            """;
        Add(cmd, "$id", artistId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapArtistInfo(reader) : null;
    }

    /// <summary>Loads artist rows for the specified artist identifiers.</summary>
    /// <param name="ids">Artist identifiers.</param>
    /// <returns>Matching artists ordered by display name.</returns>
    public List<ArtistInfo> GetArtistsByIds(IEnumerable<long> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            return [];

        using var cmd = _conn.CreateCommand();
        var parameters = idList.Select((id, i) => { var name = $"$id{i}"; Add(cmd, name, id); return name; }).ToList();
        cmd.CommandText = $"""
            SELECT id, name, is_favorite, biography, image_path,
                   profile_source_url, profile_language, profile_fetched_at,
                   image_is_manual
            FROM artists
            WHERE id IN ({string.Join(", ", parameters)})
            ORDER BY CASE WHEN name = '' THEN 1 ELSE 0 END,
                     name COLLATE NOCASE;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<ArtistInfo>();
        while (reader.Read())
            result.Add(MapArtistInfo(reader));
        return result;
    }

    public ArtistInfo? GetArtistByTrackPath(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ar.id, ar.name, ar.is_favorite, ar.biography, ar.image_path,
                   ar.profile_source_url, ar.profile_language, ar.profile_fetched_at,
                   ar.image_is_manual
            FROM tracks t
            JOIN artists ar ON ar.id = t.artist_id
            WHERE t.path = $path
            LIMIT 1;
            """;
        Add(cmd, "$path", path);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapArtistInfo(reader) : null;
    }

    public void UpdateArtistProfile(
        long artistId,
        string? biography,
        string? imagePath,
        string? sourceUrl,
        string language)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE artists
            SET biography = $biography,
                image_path = CASE
                    WHEN image_is_manual = 1 THEN image_path
                    ELSE COALESCE($image_path, image_path)
                END,
                profile_source_url = $source_url,
                profile_language = $language,
                profile_fetched_at = $fetched_at
            WHERE id = $id;
            """;
        Add(cmd, "$biography", (object?)biography ?? DBNull.Value);
        Add(cmd, "$image_path", (object?)imagePath ?? DBNull.Value);
        Add(cmd, "$source_url", (object?)sourceUrl ?? DBNull.Value);
        Add(cmd, "$language", language);
        Add(cmd, "$fetched_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Add(cmd, "$id", artistId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Stores a manually selected image path for an artist.</summary>
    /// <param name="artistId">Artist identifier.</param>
    /// <param name="imagePath">Absolute path to the cached artist image.</param>
    /// <returns><see langword="true"/> when an artist row was updated.</returns>
    public bool UpdateArtistImage(long artistId, string imagePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE artists
            SET image_path = $image_path,
                image_is_manual = 1
            WHERE id = $id;
            """;
        Add(cmd, "$image_path", imagePath);
        Add(cmd, "$id", artistId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public ArtistInfo? FindArtistByName(string artistName, long? excludeArtistId = null)
    {
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(artistName);
        if (comparisonKey.Length == 0)
            return null;

        return GetArtistsLite()
            .FirstOrDefault(artist =>
                artist.Id != excludeArtistId &&
                string.Equals(
                    ArtistNameNormalizer.CreateComparisonKey(artist.Artist),
                    comparisonKey,
                    StringComparison.Ordinal));
    }

    public ArtistRenameResult RenameArtist(long artistId, string artistName)
    {
        var normalizedName = ArtistNameNormalizer.NormalizeDisplayName(artistName);
        if (normalizedName.Length == 0)
            throw new ArgumentException("Artist name is required.", nameof(artistName));
        if (FindArtistByName(normalizedName, artistId) is not null)
            throw new InvalidOperationException("An artist with this name already exists.");

        using var tx = _conn.BeginTransaction();
        var previousName = GetArtistName(tx, artistId)
            ?? throw new InvalidOperationException("The artist no longer exists.");
        UpsertArtistAlias(tx, ArtistNameNormalizer.CreateComparisonKey(previousName), artistId);
        var renamedRows = ExecuteInTransaction(tx,
            "UPDATE artists SET name = $name WHERE id = $id;",
            ("$name", normalizedName), ("$id", artistId));
        if (renamedRows != 1)
            throw new InvalidOperationException("The artist could not be renamed.");
        UpdateDenormalizedArtistNames(tx, artistId, normalizedName);
        tx.Commit();
        ClearArtistIdentityCache();
        return new ArtistRenameResult(artistId, normalizedName, false);
    }

    public ArtistRenameResult MergeArtists(
        long currentArtistId,
        long matchingArtistId,
        long preferredArtistId,
        string artistName)
    {
        if (currentArtistId == matchingArtistId)
            throw new ArgumentException("Two different artists are required.");
        if (preferredArtistId != currentArtistId && preferredArtistId != matchingArtistId)
            throw new ArgumentException("The preferred artist must be one of the merged artists.");

        var normalizedName = ArtistNameNormalizer.NormalizeDisplayName(artistName);
        if (normalizedName.Length == 0)
            throw new ArgumentException("Artist name is required.", nameof(artistName));

        var survivorId = preferredArtistId;
        var duplicateId = preferredArtistId == currentArtistId ? matchingArtistId : currentArtistId;

        using var tx = _conn.BeginTransaction();
        var currentName = GetArtistName(tx, currentArtistId);
        var matchingName = GetArtistName(tx, matchingArtistId);
        ExecuteInTransaction(tx,
            "UPDATE artist_aliases SET artist_id = $survivor WHERE artist_id = $duplicate;",
            ("$survivor", survivorId), ("$duplicate", duplicateId));
        UpsertArtistAlias(tx, ArtistNameNormalizer.CreateComparisonKey(currentName), survivorId);
        UpsertArtistAlias(tx, ArtistNameNormalizer.CreateComparisonKey(matchingName), survivorId);
        MergeArtistAlbums(tx, survivorId, duplicateId);
        ExecuteInTransaction(tx,
            "UPDATE tracks SET artist_id = $survivor WHERE artist_id = $duplicate;",
            ("$survivor", survivorId), ("$duplicate", duplicateId));
        ExecuteInTransaction(tx, """
            UPDATE artists
            SET is_favorite = MAX(is_favorite, (SELECT is_favorite FROM artists WHERE id = $duplicate))
            WHERE id = $survivor;
            """,
            ("$survivor", survivorId), ("$duplicate", duplicateId));
        ExecuteInTransaction(tx,
            "DELETE FROM artists WHERE id = $duplicate;",
            ("$duplicate", duplicateId));
        ExecuteInTransaction(tx,
            "UPDATE artists SET name = $name WHERE id = $survivor;",
            ("$name", normalizedName), ("$survivor", survivorId));
        UpdateDenormalizedArtistNames(tx, survivorId, normalizedName);
        tx.Commit();
        ClearArtistIdentityCache();
        return new ArtistRenameResult(survivorId, normalizedName, true);
    }

    private string? GetArtistName(SqliteTransaction tx, long artistId)
    {
        using var command = _conn.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT name FROM artists WHERE id = $id LIMIT 1;";
        Add(command, "$id", artistId);
        return command.ExecuteScalar() as string;
    }

    private void UpsertArtistAlias(SqliteTransaction tx, string aliasKey, long artistId)
    {
        if (aliasKey.Length == 0)
            return;

        ExecuteInTransaction(tx, """
            INSERT INTO artist_aliases (alias_key, artist_id)
            VALUES ($alias_key, $artist_id)
            ON CONFLICT(alias_key) DO UPDATE SET artist_id = excluded.artist_id;
            """,
            ("$alias_key", aliasKey), ("$artist_id", artistId));
    }

    private void MergeArtistAlbums(SqliteTransaction tx, long survivorId, long duplicateId)
    {
        var duplicateAlbums = new List<(long Id, string Title, string SourceDirectory)>();
        using (var command = _conn.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                SELECT id, title, source_directory
                FROM albums
                WHERE artist_id = $artist_id;
                """;
            Add(command, "$artist_id", duplicateId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                duplicateAlbums.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2)));
            }
        }

        foreach (var duplicateAlbum in duplicateAlbums)
        {
            var survivorAlbumId = ExecuteScalarInTransaction(tx, """
                SELECT id
                FROM albums
                WHERE artist_id = $artist_id
                  AND title = $title COLLATE NOCASE
                  AND source_directory = $source_directory COLLATE NOCASE
                LIMIT 1;
                """,
                ("$artist_id", survivorId),
                ("$title", duplicateAlbum.Title),
                ("$source_directory", duplicateAlbum.SourceDirectory));

            if (survivorAlbumId is long existingAlbumId)
            {
                ExecuteInTransaction(tx,
                    "UPDATE tracks SET album_id = $survivor_album WHERE album_id = $duplicate_album;",
                    ("$survivor_album", existingAlbumId), ("$duplicate_album", duplicateAlbum.Id));
                ExecuteInTransaction(tx, """
                    UPDATE albums
                    SET is_favorite = MAX(is_favorite, (SELECT is_favorite FROM albums WHERE id = $duplicate_album)),
                        artwork_id = COALESCE(artwork_id, (SELECT artwork_id FROM albums WHERE id = $duplicate_album))
                    WHERE id = $survivor_album;
                    """,
                    ("$survivor_album", existingAlbumId), ("$duplicate_album", duplicateAlbum.Id));
                ExecuteInTransaction(tx,
                    "DELETE FROM albums WHERE id = $duplicate_album;",
                    ("$duplicate_album", duplicateAlbum.Id));
            }
            else
            {
                ExecuteInTransaction(tx,
                    "UPDATE albums SET artist_id = $survivor WHERE id = $album_id;",
                    ("$survivor", survivorId), ("$album_id", duplicateAlbum.Id));
            }
        }
    }

    private void UpdateDenormalizedArtistNames(
        SqliteTransaction tx,
        long artistId,
        string artistName)
    {
        ExecuteInTransaction(tx, """
            UPDATE tracks
            SET artist = $artist_name,
                sort_artist = NULL
            WHERE artist_id = $artist_id;

            UPDATE tracks
            SET album_artist = $artist_name,
                sort_album_artist = NULL
            WHERE album_id IN (
                SELECT id FROM albums WHERE artist_id = $artist_id
            );
            """,
            ("$artist_name", artistName), ("$artist_id", artistId));
    }

    /// <summary>Loads compact album rows identified by album title and album artist.</summary>
    /// <param name="includeArtwork">Whether cached artwork paths should be included.</param>
    /// <returns>Albums ordered by title and album artist.</returns>
    public List<AlbumInfo> GetAlbumsLite(bool includeArtwork = false)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = includeArtwork ? """
            SELECT
                al.id,
                al.title,
                ar.name,
                CASE WHEN al.year = 0 THEN NULL ELSE al.year END AS year,
                aw.thumb_320_path,
                aw.thumb_96_path,
                al.is_favorite,
                al.artist_id
            FROM albums al
            LEFT JOIN artists ar ON ar.id = al.artist_id
            LEFT JOIN artworks aw ON aw.id = al.artwork_id
            ORDER BY
                CASE WHEN al.title = '' THEN 1 ELSE 0 END,
                al.title COLLATE NOCASE,
                ar.name COLLATE NOCASE;
            """ : """
            SELECT
                al.id,
                al.title,
                ar.name,
                CASE WHEN al.year = 0 THEN NULL ELSE al.year END AS year,
                NULL AS thumb_320_path,
                NULL AS thumb_96_path,
                al.is_favorite,
                al.artist_id
            FROM albums al
            LEFT JOIN artists ar ON ar.id = al.artist_id
            ORDER BY
                CASE WHEN al.title = '' THEN 1 ELSE 0 END,
                al.title COLLATE NOCASE,
                ar.name COLLATE NOCASE;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<AlbumInfo>();
        while (reader.Read())
            result.Add(new AlbumInfo(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) || reader.GetInt32(3) == 0 ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6) != 0,
                reader.IsDBNull(7) ? null : reader.GetInt64(7)));
        return result;
    }

    /// <summary>Loads one album by its stable database identifier.</summary>
    /// <param name="albumId">Album identifier.</param>
    /// <returns>The matching album, or <see langword="null"/> when it does not exist.</returns>
    public AlbumInfo? GetAlbumById(long albumId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                al.id,
                al.title,
                ar.name,
                CASE WHEN al.year = 0 THEN NULL ELSE al.year END,
                aw.thumb_320_path,
                aw.thumb_96_path,
                al.is_favorite,
                al.artist_id
            FROM albums al
            LEFT JOIN artists ar ON ar.id = al.artist_id
            LEFT JOIN artworks aw ON aw.id = al.artwork_id
            WHERE al.id = $album_id
            LIMIT 1;
            """;
        Add(cmd, "$album_id", albumId);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new AlbumInfo(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6) != 0,
                reader.IsDBNull(7) ? null : reader.GetInt64(7))
            : null;
    }

    /// <summary>
    /// Rebuilds album assignments using album title and physical source directory
    /// as the identity while preserving matching favorites and artwork.
    /// </summary>
    public void RebuildAlbumsFromAlbumArtists()
        => RebuildAlbumsByPhysicalDirectory();

    private void RebuildAlbumsByPhysicalDirectory()
    {
        using var tx = _conn.BeginTransaction();
        using var select = _conn.CreateCommand();
        select.Transaction = tx;
        select.CommandText = """
            SELECT
                t.id,
                COALESCE(TRIM(t.album), ''),
                NULLIF(TRIM(t.album_artist), ''),
                NULLIF(TRIM(t.artist), ''),
                t.year,
                COALESCE(t.source_path, t.path),
                al.id,
                al.artwork_id,
                COALESCE(al.is_favorite, 0)
            FROM tracks t
            LEFT JOIN albums al ON al.id = t.album_id;
            """;
        using var reader = select.ExecuteReader();
        var rows = new List<(
            long Id,
            string Album,
            string? AlbumArtist,
            string? Artist,
            int? Year,
            string SourcePath,
            long? OldAlbumId,
            long? ArtworkId,
            bool IsFavorite)>();
        while (reader.Read())
            rows.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.GetInt32(8) != 0));
        reader.Close();

        ExecuteInTransaction(tx, "UPDATE tracks SET album_id = NULL;");
        ExecuteInTransaction(tx, "DROP TABLE albums;");
        CreateAlbumsTable(tx);
        ExecuteInTransaction(tx, """
            CREATE TEMP TABLE album_rebuild_map (
                album_title   TEXT NOT NULL,
                album_path    TEXT NOT NULL,
                album_id      INTEGER NOT NULL,
                PRIMARY KEY (album_title, album_path)
            );
            """);

        var albumGroups = rows
            .GroupBy(row => MakeAlbumDirectoryKey(
                row.Album,
                GetPhysicalAlbumDirectory(row.SourcePath)),
                StringComparer.OrdinalIgnoreCase);
        var splitOldAlbumIds = rows
            .Where(row => row.OldAlbumId.HasValue)
            .GroupBy(row => row.OldAlbumId!.Value)
            .Where(group => group
                .Select(row => MakeAlbumDirectoryKey(
                    row.Album,
                    GetPhysicalAlbumDirectory(row.SourcePath)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any())
            .Select(group => group.Key)
            .ToHashSet();
        foreach (var group in albumGroups)
        {
            var row = group.First();
            var albumDirectory = GetPhysicalAlbumDirectory(row.SourcePath);
            var albumArtists = group
                .Select(item => ArtistNameNormalizer.NormalizeDisplayName(item.AlbumArtist))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var primaryArtists = group
                .Select(item => ArtistNameNormalizer.NormalizeDisplayName(item.Artist))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var displayArtist = albumArtists.Count == 1
                ? albumArtists[0]
                : albumArtists.Count == 0 && primaryArtists.Count == 1
                    ? primaryArtists[0]
                    : null;
            var oldAlbumWasSplit = group.Any(item =>
                item.OldAlbumId is long oldAlbumId &&
                splitOldAlbumIds.Contains(oldAlbumId));
            var artworkId = group.Select(item => item.ArtworkId)
                .FirstOrDefault(id => id.HasValue);
            if (oldAlbumWasSplit)
            {
                var artwork = TryReadEmbeddedArtwork(row.SourcePath);
                artworkId = artwork.Data is null
                    ? artworkId
                    : EnsureArtwork(artwork.Data, artwork.MimeType, tx);
            }
            var albumId = EnsureAlbum(
                row.Album,
                displayArtist,
                group.Select(item => item.Year).FirstOrDefault(year => year.HasValue),
                artworkId,
                albumDirectory,
                tx)!.Value;
            if (group.Any(item => item.IsFavorite))
            {
                ExecuteInTransaction(
                    tx,
                    "UPDATE albums SET is_favorite = 1 WHERE id = $album_id;",
                    ("$album_id", albumId));
            }

            ExecuteInTransaction(tx, """
                INSERT INTO album_rebuild_map (album_title, album_path, album_id)
                VALUES ($album_title, $album_path, $album_id);
                """,
                ("$album_title", row.Album),
                ("$album_path", albumDirectory),
                ("$album_id", albumId));
        }

        foreach (var group in albumGroups)
        {
            var albumTitle = group.First().Album;
            var albumPath = GetPhysicalAlbumDirectory(group.First().SourcePath);
            var albumId = ExecuteScalarInTransaction(tx, """
                SELECT album_id
                FROM album_rebuild_map
                WHERE album_title = $album_title
                  AND album_path = $album_path
                LIMIT 1;
                """,
                ("$album_title", albumTitle),
                ("$album_path", albumPath));
            foreach (var batch in group.Select(item => item.Id).Chunk(400))
            {
                using var update = _conn.CreateCommand();
                update.Transaction = tx;
                var parameters = batch.Select((id, index) =>
                {
                    var name = $"$id{index}";
                    Add(update, name, id);
                    return name;
                }).ToList();
                update.CommandText =
                    $"UPDATE tracks SET album_id = $album_id WHERE id IN ({string.Join(", ", parameters)});";
                Add(update, "$album_id", albumId);
                update.ExecuteNonQuery();
            }
        }
        ExecuteInTransaction(tx, "DROP TABLE album_rebuild_map;");
        tx.Commit();
    }

    private static (byte[]? Data, string? MimeType) TryReadEmbeddedArtwork(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return (null, null);

        try
        {
            using var tagFile = TagLib.File.Create(path);
            var picture = tagFile.Tag.Pictures?.FirstOrDefault(
                              candidate => candidate.Type == TagLib.PictureType.FrontCover)
                          ?? tagFile.Tag.Pictures?.FirstOrDefault();
            var data = picture?.Data?.Data;
            return data is { Length: > 0 }
                ? (data, string.IsNullOrWhiteSpace(picture?.MimeType)
                    ? null
                    : picture.MimeType.Trim())
                : (null, null);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Finds one physical sample track path for every album without assigned artwork.</summary>
    /// <returns>Album identifiers with a readable sample path candidate.</returns>
    public List<(long AlbumId, string Path)> GetAlbumsMissingArtworkSamplePaths()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT al.id, MIN(COALESCE(NULLIF(t.source_path, ''), t.path)) AS sample_path
            FROM albums al
            JOIN tracks t ON t.album_id = al.id
            WHERE al.artwork_id IS NULL
            GROUP BY al.id;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<(long AlbumId, string Path)>();
        while (reader.Read())
            result.Add((reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    /// <summary>Finds one physical sample track path for an album when it has no assigned artwork.</summary>
    /// <param name="albumId">Identifier of the album to inspect.</param>
    /// <returns>A readable sample path candidate, or <see langword="null"/> when the album has artwork or no tracks.</returns>
    public string? GetAlbumMissingArtworkSamplePath(long albumId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT MIN(COALESCE(NULLIF(t.source_path, ''), t.path)) AS sample_path
            FROM albums al
            JOIN tracks t ON t.album_id = al.id
            WHERE al.id = $album_id
              AND al.artwork_id IS NULL
            GROUP BY al.id;
            """;
        Add(cmd, "$album_id", albumId);
        return cmd.ExecuteScalar() as string;
    }

    public List<(long AlbumId, string ReleaseId)> GetAlbumsMissingArtworkReleaseIds()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT al.id, MIN(t.musicbrainz_release_id) AS release_id
            FROM albums al
            JOIN tracks t ON t.album_id = al.id
            WHERE al.artwork_id IS NULL
              AND t.musicbrainz_release_id IS NOT NULL
              AND TRIM(t.musicbrainz_release_id) <> ''
            GROUP BY al.id;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<(long AlbumId, string ReleaseId)>();
        while (reader.Read())
            result.Add((reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    /// <summary>Stores artwork bytes in the artwork cache and attaches the resulting artwork to an album.</summary>
    /// <param name="albumId">Album identifier.</param>
    /// <param name="data">Raw image bytes.</param>
    /// <param name="mimeType">Optional image MIME type.</param>
    /// <returns><see langword="true"/> when an album row was updated.</returns>
    public bool AttachArtworkToAlbum(long albumId, byte[] data, string? mimeType)
    {
        using var tx = _conn.BeginTransaction();
        var artworkId = EnsureArtwork(data, mimeType, tx);
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE albums
            SET artwork_id = $artwork_id
            WHERE id = $album_id;
            """;
        Add(cmd, "$artwork_id", artworkId);
        Add(cmd, "$album_id", albumId);
        var changed = cmd.ExecuteNonQuery() > 0;
        tx.Commit();
        return changed;
    }

    public void ClearArtworkFromAlbum(long albumId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE albums SET artwork_id = NULL WHERE id = $album_id;";
        Add(cmd, "$album_id", albumId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Loads albums associated with an album artist or primary track artist.</summary>
    /// <param name="artistId">Artist identifier.</param>
    /// <param name="includeArtwork">Whether cached artwork paths should be included.</param>
    /// <returns>Matching albums ordered by title.</returns>
    public List<AlbumInfo> GetAlbumsByArtist(long artistId, bool includeArtwork = false)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = includeArtwork ? """
            SELECT
                al.id,
                al.title,
                ar.name,
                CASE WHEN al.year = 0 THEN NULL ELSE al.year END AS year,
                aw.thumb_320_path,
                aw.thumb_96_path,
                al.is_favorite
            FROM albums al
            LEFT JOIN artists ar ON ar.id = al.artist_id
            LEFT JOIN artworks aw ON aw.id = al.artwork_id
            WHERE al.artist_id = $artist_id
               OR al.id IN (
                    SELECT DISTINCT album_id
                    FROM tracks
                    WHERE artist_id = $artist_id
                      AND album_id IS NOT NULL
               )
            ORDER BY
                CASE WHEN al.title = '' THEN 1 ELSE 0 END,
                al.title COLLATE NOCASE;
            """ : """
            SELECT
                al.id,
                al.title,
                ar.name,
                CASE WHEN al.year = 0 THEN NULL ELSE al.year END AS year,
                NULL AS thumb_320_path,
                NULL AS thumb_96_path,
                al.is_favorite
            FROM albums al
            LEFT JOIN artists ar ON ar.id = al.artist_id
            WHERE al.artist_id = $artist_id
               OR al.id IN (
                    SELECT DISTINCT album_id
                    FROM tracks
                    WHERE artist_id = $artist_id
                      AND album_id IS NOT NULL
               )
            ORDER BY
                CASE WHEN al.title = '' THEN 1 ELSE 0 END,
                al.title COLLATE NOCASE;
            """;
        Add(cmd, "$artist_id", artistId);
        using var reader = cmd.ExecuteReader();
        var result = new List<AlbumInfo>();
        while (reader.Read())
            result.Add(new AlbumInfo(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6) != 0,
                artistId));
        return result;
    }

    public long? GetTrackIdByPath(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM tracks WHERE path = $path LIMIT 1;";
        cmd.Parameters.AddWithValue("$path", path);
        var value = cmd.ExecuteScalar();
        return value is null || value is DBNull ? null : Convert.ToInt64(value);
    }

    /// <summary>Records the start of a playback-history entry.</summary>
    /// <param name="path">Played local path, stream URL, or source identifier.</param>
    /// <param name="trackId">Local track identifier, or <see langword="null"/> for remote and non-library playback.</param>
    /// <param name="durationSeconds">Known total duration in seconds, or <see langword="null"/>.</param>
    /// <param name="mediaType">Media type stored with the history entry.</param>
    /// <param name="title">Display title captured at playback start.</param>
    /// <param name="subtitle">Display subtitle or artist captured at playback start.</param>
    /// <param name="album">Album or collection title captured at playback start.</param>
    /// <param name="externalId">Optional source-specific identifier for remote history actions.</param>
    /// <param name="genre">Genre captured at playback start for non-local tracks.</param>
    /// <returns>The new playback-history row identifier.</returns>
    public long RecordPlaybackStart(
        string path,
        long? trackId,
        double? durationSeconds,
        string mediaType = "track",
        string? title = null,
        string? subtitle = null,
        string? album = null,
        string? externalId = null,
        string? genre = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO play_history (
                track_id, path, started_at, duration_seconds,
                media_type, title, subtitle, album, external_id, genre)
            VALUES (
                $track_id, $path, $started_at, $duration_seconds,
                $media_type, $title, $subtitle, $album, $external_id, $genre)
            RETURNING id;
            """;
        Add(cmd, "$track_id", trackId);
        Add(cmd, "$path", path);
        Add(cmd, "$started_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Add(cmd, "$duration_seconds", durationSeconds);
        Add(cmd, "$media_type", mediaType);
        Add(cmd, "$title", title);
        Add(cmd, "$subtitle", subtitle);
        Add(cmd, "$album", string.IsNullOrWhiteSpace(album) ? null : album);
        Add(cmd, "$external_id", externalId);
        Add(cmd, "$genre", string.IsNullOrWhiteSpace(genre) ? null : genre);
        return (long)cmd.ExecuteScalar()!;
    }

    public void RecordPlaybackEnd(long historyId, double positionSeconds, bool completed)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE play_history
            SET ended_at = $ended_at,
                position_seconds = $position_seconds,
                completed = $completed
            WHERE id = $id;
            """;
        Add(cmd, "$ended_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Add(cmd, "$position_seconds", positionSeconds);
        Add(cmd, "$completed", completed ? 1 : 0);
        Add(cmd, "$id", historyId);
        cmd.ExecuteNonQuery();
    }

    public void Optimize()
    {
        Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        Execute("VACUUM;");
        Execute("ANALYZE;");
    }

    public ArtistNormalizationResult NormalizeArtists()
    {
        using var tx = _conn.BeginTransaction();
        var artists = new List<(long Id, string Name, int Usage, bool Favorite)>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT ar.id, ar.name,
                       (SELECT COUNT(*) FROM tracks t WHERE t.artist_id = ar.id) +
                       (SELECT COUNT(*) FROM albums al WHERE al.artist_id = ar.id) AS usage_count,
                       ar.is_favorite
                FROM artists ar;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                artists.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3) != 0));
        }

        var mergedArtists = 0;
        foreach (var group in artists.GroupBy(artist => ArtistNameNormalizer.CreateComparisonKey(artist.Name)))
        {
            var survivor = group
                .OrderByDescending(artist => artist.Usage)
                .ThenByDescending(artist => ArtistNameNormalizer.NormalizeDisplayName(artist.Name).Length)
                .ThenBy(artist => artist.Id)
                .First();
            var canonicalName = ArtistNameNormalizer.NormalizeDisplayName(survivor.Name);
            if (canonicalName.Length == 0 && group.Key.Length != 0)
                canonicalName = survivor.Name.Trim();

            foreach (var duplicate in group.Where(artist => artist.Id != survivor.Id))
            {
                ExecuteInTransaction(tx,
                    "UPDATE artist_aliases SET artist_id = $survivor WHERE artist_id = $duplicate;",
                    ("$survivor", survivor.Id), ("$duplicate", duplicate.Id));
                UpsertArtistAlias(
                    tx,
                    ArtistNameNormalizer.CreateComparisonKey(duplicate.Name),
                    survivor.Id);
                MergeArtistAlbums(tx, survivor.Id, duplicate.Id);
                ExecuteInTransaction(tx,
                    "UPDATE tracks SET artist_id = $survivor WHERE artist_id = $duplicate;",
                    ("$survivor", survivor.Id), ("$duplicate", duplicate.Id));
                ExecuteInTransaction(tx, """
                    UPDATE artists
                    SET is_favorite = MAX(is_favorite, (SELECT is_favorite FROM artists WHERE id = $duplicate)),
                        biography = COALESCE(biography, (SELECT biography FROM artists WHERE id = $duplicate)),
                        image_path = CASE
                            WHEN image_is_manual = 1 THEN image_path
                            WHEN (SELECT image_is_manual FROM artists WHERE id = $duplicate) = 1
                                THEN (SELECT image_path FROM artists WHERE id = $duplicate)
                            ELSE COALESCE(image_path, (SELECT image_path FROM artists WHERE id = $duplicate))
                        END,
                        image_is_manual = MAX(image_is_manual, (SELECT image_is_manual FROM artists WHERE id = $duplicate)),
                        profile_source_url = COALESCE(profile_source_url, (SELECT profile_source_url FROM artists WHERE id = $duplicate)),
                        profile_language = COALESCE(profile_language, (SELECT profile_language FROM artists WHERE id = $duplicate)),
                        profile_fetched_at = MAX(profile_fetched_at, (SELECT profile_fetched_at FROM artists WHERE id = $duplicate))
                    WHERE id = $survivor;
                    """,
                    ("$survivor", survivor.Id), ("$duplicate", duplicate.Id));
                ExecuteInTransaction(tx, "DELETE FROM artists WHERE id = $duplicate;", ("$duplicate", duplicate.Id));
                mergedArtists++;
            }

            ExecuteInTransaction(tx,
                "UPDATE artists SET name = $name WHERE id = $id;",
                ("$name", canonicalName), ("$id", survivor.Id));
            UpsertArtistAlias(tx, group.Key, survivor.Id);
        }

        ExecuteInTransaction(tx, """
            UPDATE tracks
            SET artist = COALESCE((SELECT name FROM artists WHERE id = tracks.artist_id), artist),
                sort_artist = NULL;

            UPDATE tracks
            SET album_artist = COALESCE((
                SELECT ar.name
                FROM albums al
                JOIN artists ar ON ar.id = al.artist_id
                WHERE al.id = tracks.album_id
            ), album_artist),
                sort_album_artist = NULL;
            """);

        var updatedTracks = Convert.ToInt32(ExecuteScalarInTransaction(
            tx,
            "SELECT COUNT(*) FROM tracks WHERE artist_id IS NOT NULL;"));
        tx.Commit();
        ClearArtistIdentityCache();
        return new ArtistNormalizationResult(mergedArtists, updatedTracks);
    }

    /// <summary>Nur die Spalten, die die Trackliste tatsächlich rendert – keine BLOBs, keine Texte.</summary>
    public List<TrackListInfo> GetTrackList()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                path, file_name, title, artist, album, album_artist, genre, format, bitrate,
                duration, sort_title, id, is_favorite, year, track_number, track_total,
                disc_number, disc_total, sample_rate, bit_depth, channels, composer, bpm,
                file_size, added_at, replay_gain_track, replay_gain_album, artist_id, album_id
            FROM tracks
            ORDER BY COALESCE(sort_title, title, file_name) COLLATE NOCASE;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<TrackListInfo>();
        while (reader.Read())
            result.Add(MapTrackListInfo(reader));
        return result;
    }

    public List<TrackListInfo> GetTrackListByAlbum(long albumId, long? artistId = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                path, file_name, title, artist, album, album_artist, genre, format, bitrate,
                duration, sort_title, id, is_favorite, year, track_number, track_total,
                disc_number, disc_total, sample_rate, bit_depth, channels, composer, bpm,
                file_size, added_at, replay_gain_track, replay_gain_album, artist_id, album_id
            FROM tracks
            WHERE album_id = $album_id
              AND ($artist_id IS NULL OR artist_id = $artist_id)
            ORDER BY
                COALESCE(disc_number, 0),
                COALESCE(track_number, 0),
                file_name COLLATE NOCASE;
            """;
        Add(cmd, "$album_id", albumId);
        Add(cmd, "$artist_id", artistId);
        using var reader = cmd.ExecuteReader();
        var result = new List<TrackListInfo>();
        while (reader.Read())
            result.Add(MapTrackListInfo(reader));
        return result;
    }

    /// <summary>
    /// Resolves the physical source directory for every track in an album selection.
    /// CUE tracks use their shared source file instead of the virtual <c>cue://</c> path.
    /// </summary>
    /// <param name="albumId">Database album identifier.</param>
    /// <param name="artistId">Optional primary-artist filter.</param>
    /// <returns>A mapping from track identifier to physical source directory.</returns>
    public Dictionary<long, string> GetAlbumTrackDirectories(long albumId, long? artistId = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, path, source_path
            FROM tracks
            WHERE album_id = $album_id
              AND ($artist_id IS NULL OR artist_id = $artist_id);
            """;
        Add(cmd, "$album_id", albumId);
        Add(cmd, "$artist_id", artistId);
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<long, string>();
        while (reader.Read())
        {
            var trackId = reader.GetInt64(0);
            var path = reader.IsDBNull(2) ? reader.GetString(1) : reader.GetString(2);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                result[trackId] = directory;
        }
        return result;
    }

    public List<TrackListInfo> GetTrackListByIds(IEnumerable<long> ids)
    {
        const int batchSize = 500;
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            return [];

        var result = new List<TrackListInfo>(idList.Count);
        foreach (var batch in idList.Chunk(batchSize))
        {
            using var cmd = _conn.CreateCommand();
            var parameters = batch.Select((id, i) =>
            {
                var name = $"$id{i}";
                Add(cmd, name, id);
                return name;
            }).ToList();
            cmd.CommandText = $"""
                SELECT
                    path, file_name, title, artist, album, album_artist, genre, format, bitrate,
                    duration, sort_title, id, is_favorite, year, track_number, track_total,
                    disc_number, disc_total, sample_rate, bit_depth, channels, composer, bpm,
                    file_size, added_at, replay_gain_track, replay_gain_album, artist_id, album_id
                FROM tracks
                WHERE id IN ({string.Join(", ", parameters)});
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(MapTrackListInfo(reader));
        }

        var order = idList.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return result.OrderBy(t => order[t.Id]).ToList();
    }

    /// <summary>Loads compact track metadata for the specified local file paths.</summary>
    /// <param name="paths">Absolute paths to resolve; duplicates are queried once.</param>
    /// <returns>Matching tracks in unspecified order.</returns>
    public List<TrackListInfo> GetTrackListByPaths(IEnumerable<string> paths)
    {
        const int batchSize = 400;
        var pathList = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (pathList.Count == 0)
            return [];

        var result = new List<TrackListInfo>(pathList.Count);
        foreach (var batch in pathList.Chunk(batchSize))
        {
            using var cmd = _conn.CreateCommand();
            var parameters = batch.Select((path, index) =>
            {
                var name = $"$path{index}";
                Add(cmd, name, path);
                return name;
            }).ToList();
            cmd.CommandText = $"""
                SELECT
                    path, file_name, title, artist, album, album_artist, genre, format, bitrate,
                    duration, sort_title, id, is_favorite, year, track_number, track_total,
                    disc_number, disc_total, sample_rate, bit_depth, channels, composer, bpm,
                    file_size, added_at, replay_gain_track, replay_gain_album, artist_id, album_id
                FROM tracks
                WHERE path IN ({string.Join(", ", parameters)});
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(MapTrackListInfo(reader));
        }

        return result;
    }

    /// <summary>Loads the lightweight classification fields used by the interactive track filters.</summary>
    /// <returns>All local tracks with favourite, genre, format, and bitrate values.</returns>
    public List<TrackFacetInfo> GetTrackFacets()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, is_favorite, genre, format, bitrate FROM tracks;";
        using var reader = cmd.ExecuteReader();
        var result = new List<TrackFacetInfo>();
        while (reader.Read())
            result.Add(new TrackFacetInfo(
                reader.GetInt64(0),
                reader.GetInt32(1) != 0,
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4)));
        return result;
    }

    /// <summary>
    /// Loads compact metadata and aggregated playback history used to resolve smart playlists.
    /// </summary>
    /// <returns>All local tracks with their smart-playlist and playback-history values.</returns>
    public List<SmartPlaylistTrackInfo> GetSmartPlaylistTracks()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                t.id,
                t.is_favorite,
                t.genre,
                t.format,
                t.bitrate,
                t.year,
                t.artist,
                t.album,
                t.duration,
                t.added_at,
                COALESCE(ph.play_count, 0) AS play_count,
                ph.last_played_at,
                COALESCE(t.sort_title, t.title, t.file_name) AS display_sort_title
            FROM tracks t
            LEFT JOIN (
                SELECT
                    track_id,
                    COUNT(*) AS play_count,
                    MAX(started_at) AS last_played_at
                FROM play_history
                WHERE media_type = 'track'
                  AND track_id IS NOT NULL
                GROUP BY track_id
            ) ph ON ph.track_id = t.id;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<SmartPlaylistTrackInfo>();
        while (reader.Read())
            result.Add(new SmartPlaylistTrackInfo(
                reader.GetInt64(0),
                reader.GetInt32(1) != 0,
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.GetInt64(9),
                reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetInt64(11),
                reader.GetString(12)));
        return result;
    }

    public List<TrackListInfo> GetTrackListFiltered(IEnumerable<long> ids)
        => GetTrackListByIds(ids)
            .OrderBy(
                track => track.SortTitle ?? track.Title ?? track.FileName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static TrackListInfo MapTrackListInfo(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetInt32(8),
        reader.IsDBNull(9) ? null : reader.GetDouble(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.GetInt64(11),
        reader.GetInt32(12) != 0,
        reader.IsDBNull(13) ? null : reader.GetInt32(13),
        reader.IsDBNull(14) ? null : reader.GetInt32(14),
        reader.IsDBNull(15) ? null : reader.GetInt32(15),
        reader.IsDBNull(16) ? null : reader.GetInt32(16),
        reader.IsDBNull(17) ? null : reader.GetInt32(17),
        reader.IsDBNull(18) ? null : reader.GetInt32(18),
        reader.IsDBNull(19) ? null : reader.GetInt32(19),
        reader.IsDBNull(20) ? null : reader.GetInt32(20),
        reader.IsDBNull(21) ? null : reader.GetString(21),
        reader.IsDBNull(22) ? null : reader.GetInt32(22),
        reader.IsDBNull(23) ? null : reader.GetInt64(23),
        reader.GetInt64(24),
        reader.IsDBNull(25) ? null : reader.GetString(25),
        reader.IsDBNull(26) ? null : reader.GetString(26),
        reader.IsDBNull(27) ? null : reader.GetInt64(27),
        reader.IsDBNull(28) ? null : reader.GetInt64(28));

    /// <summary>Loads distinct albums referenced by the specified track identifiers.</summary>
    /// <param name="ids">Track identifiers.</param>
    /// <returns>Matching albums ordered by title.</returns>
    public List<AlbumInfo> GetAlbumsByTrackIds(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return [];
        using var cmd = _conn.CreateCommand();
        var parameters = idList.Select((id, i) => { var name = $"$id{i}"; Add(cmd, name, id); return name; }).ToList();
        cmd.CommandText = $"""
            SELECT al.id, al.title, ar.name,
                   CASE WHEN al.year = 0 THEN NULL ELSE al.year END,
                   aw.thumb_320_path, aw.thumb_96_path, al.is_favorite
            FROM tracks t
            JOIN albums al ON al.id = t.album_id
            LEFT JOIN artists ar ON ar.id = al.artist_id
            LEFT JOIN artworks aw ON aw.id = al.artwork_id
            WHERE t.id IN ({string.Join(", ", parameters)})
            GROUP BY al.id
            ORDER BY al.title COLLATE NOCASE;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<AlbumInfo>();
        while (reader.Read())
            result.Add(new AlbumInfo(
                reader.GetInt64(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6) != 0));
        return result;
    }

    public List<ArtistInfo> GetArtistsByTrackIds(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return [];
        using var cmd = _conn.CreateCommand();
        var parameters = idList.Select((id, i) => { var name = $"$id{i}"; Add(cmd, name, id); return name; }).ToList();
        cmd.CommandText = $"""
            SELECT DISTINCT ar.id, ar.name, ar.is_favorite, NULL AS biography, ar.image_path,
                            ar.profile_source_url, ar.profile_language, ar.profile_fetched_at,
                            ar.image_is_manual
            FROM tracks t
            JOIN artists ar ON ar.id = t.artist_id
            WHERE t.id IN ({string.Join(", ", parameters)})
            ORDER BY ar.name COLLATE NOCASE;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<ArtistInfo>();
        while (reader.Read())
            result.Add(MapArtistInfo(reader));
        return result;
    }

    /// <summary>Maps each specified track identifier to its assigned album identifier.</summary>
    /// <param name="ids">Track identifiers.</param>
    /// <returns>A mapping for tracks that currently reference an album.</returns>
    public Dictionary<long, long> GetAlbumIdsByTrackIds(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return [];

        using var cmd = _conn.CreateCommand();
        var parameters = idList.Select((id, i) => { var name = $"$id{i}"; Add(cmd, name, id); return name; }).ToList();
        cmd.CommandText = $"""
            SELECT t.id, al.id
            FROM tracks t
            JOIN albums al ON al.id = t.album_id
            WHERE t.id IN ({string.Join(", ", parameters)});
            """;
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<long, long>();
        while (reader.Read())
            result[reader.GetInt64(0)] = reader.GetInt64(1);
        return result;
    }

    public Dictionary<long, long> GetArtistIdsByTrackIds(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return [];

        using var cmd = _conn.CreateCommand();
        var parameters = idList.Select((id, i) => { var name = $"$id{i}"; Add(cmd, name, id); return name; }).ToList();
        cmd.CommandText = $"""
            SELECT t.id, ar.id
            FROM tracks t
            JOIN artists ar ON ar.id = t.artist_id
            WHERE t.id IN ({string.Join(", ", parameters)});
            """;
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<long, long>();
        while (reader.Read())
            result[reader.GetInt64(0)] = reader.GetInt64(1);
        return result;
    }

    /// <summary>Maps each specified track identifier to its album artist identifier.</summary>
    /// <param name="ids">Track identifiers.</param>
    /// <returns>A mapping for tracks whose album currently references an artist.</returns>
    public Dictionary<long, long> GetAlbumArtistIdsByTrackIds(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return [];

        using var cmd = _conn.CreateCommand();
        var parameters = idList.Select((id, i) => { var name = $"$id{i}"; Add(cmd, name, id); return name; }).ToList();
        cmd.CommandText = $"""
            SELECT t.id, al.artist_id
            FROM tracks t
            JOIN albums al ON al.id = t.album_id
            WHERE t.id IN ({string.Join(", ", parameters)})
              AND al.artist_id IS NOT NULL;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<long, long>();
        while (reader.Read())
            result[reader.GetInt64(0)] = reader.GetInt64(1);
        return result;
    }

    public (long Id, bool IsFavorite)? GetTrackIdAndFavorite(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, is_favorite FROM tracks WHERE path = $path LIMIT 1;";
        Add(cmd, "$path", path);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return (reader.GetInt64(0), reader.GetInt32(1) != 0);
    }

    public (long? ArtistId, long? AlbumId) GetTrackNavigationIds(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT artist_id, album_id
            FROM tracks
            WHERE path = $path
            LIMIT 1;
            """;
        Add(cmd, "$path", path);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return (null, null);
        return (
            reader.IsDBNull(0) ? null : reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1));
    }

    public long? GetAlbumArtistId(long albumId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT artist_id FROM albums WHERE id = $id LIMIT 1;";
        Add(cmd, "$id", albumId);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    public void SetTrackFavorite(long id, bool value) => SetFavorite("tracks", id, value);
    public void SetArtistFavorite(long id, bool value) => SetFavorite("artists", id, value);
    public void SetAlbumFavorite(long id, bool value) => SetFavorite("albums", id, value);

    /// <summary>Gets cached artwork file paths for an album.</summary>
    /// <param name="albumId">Album identifier.</param>
    /// <returns>Cached artwork paths, or <see langword="null"/> when the album has no artwork row.</returns>
    public ArtworkPaths? GetArtworkPathsByAlbumId(long albumId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT aw.original_path, aw.thumb_96_path, aw.thumb_320_path
            FROM albums al
            LEFT JOIN artworks aw ON aw.id = al.artwork_id
            WHERE al.id = $album_id
            LIMIT 1;
            """;
        Add(cmd, "$album_id", albumId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() && !reader.IsDBNull(0)
            ? new ArtworkPaths(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    }

    /// <summary>Ensures cached artwork files exist for an album and returns their paths.</summary>
    /// <param name="albumId">Album identifier.</param>
    /// <returns>Cached artwork paths, or <see langword="null"/> when the album has no artwork payload.</returns>
    public ArtworkPaths? EnsureArtworkFilesForAlbum(long albumId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT aw.id, aw.content_hash, aw.mime_type, aw.data, aw.original_path, aw.thumb_96_path, aw.thumb_320_path
            FROM albums al
            LEFT JOIN artworks aw ON aw.id = al.artwork_id
            WHERE al.id = $album_id
            LIMIT 1;
            """;
        Add(cmd, "$album_id", albumId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(3))
            return null;

        var artworkId = reader.GetInt64(0);
        var hash = reader.GetString(1);
        var mimeType = reader.IsDBNull(2) ? null : reader.GetString(2);
        var data = (byte[])reader.GetValue(3);
        var original = reader.IsDBNull(4) ? null : reader.GetString(4);
        var thumb96 = reader.IsDBNull(5) ? null : reader.GetString(5);
        var thumb320 = reader.IsDBNull(6) ? null : reader.GetString(6);
        reader.Close();

        if (File.Exists(original) && File.Exists(thumb96) && File.Exists(thumb320))
            return new ArtworkPaths(original, thumb96, thumb320);

        var files = ArtworkCache.EnsureFiles(hash, data, mimeType);
        using var update = _conn.CreateCommand();
        update.CommandText = """
            UPDATE artworks
            SET original_path = $original,
                thumb_96_path = $thumb96,
                thumb_320_path = $thumb320
            WHERE id = $id;
            """;
        Add(update, "$original", files.OriginalPath);
        Add(update, "$thumb96", files.Thumb96Path);
        Add(update, "$thumb320", files.Thumb320Path);
        Add(update, "$id", artworkId);
        update.ExecuteNonQuery();

        return new ArtworkPaths(files.OriginalPath, files.Thumb96Path, files.Thumb320Path);
    }

    /// <summary>Gets cached artwork file paths for a track's album.</summary>
    /// <param name="path">Track path.</param>
    /// <returns>Cached artwork paths, or <see langword="null"/> when the track or album has no artwork row.</returns>
    public ArtworkPaths? GetArtworkPathsByTrackPath(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT aw.original_path, aw.thumb_96_path, aw.thumb_320_path
            FROM tracks t
            LEFT JOIN albums al ON al.id = t.album_id
            LEFT JOIN artworks aw ON aw.id = al.artwork_id
            WHERE t.path = $path
            LIMIT 1;
            """;
        Add(cmd, "$path", path);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new ArtworkPaths(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    }

    /// <summary>Nur path/file_name/title/disc_number/track_number – kein BLOB, keine Texte.</summary>
    public List<TrackLite> GetTracksLite()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT path, COALESCE(source_path, path), file_name, title, disc_number, track_number FROM tracks ORDER BY COALESCE(source_path, path), track_number;";
        using var reader = cmd.ExecuteReader();
        var result = new List<TrackLite>();
        while (reader.Read())
            result.Add(new TrackLite(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : (int?)reader.GetInt64(4),
                reader.IsDBNull(5) ? null : (int?)reader.GetInt64(5)));
        return result;
    }

    /// <summary>Tracks direkt in <paramref name="dirPath"/> (ohne Unterordner), sortiert nach Disc/Track/Dateiname.</summary>
    public List<TrackLite> GetTracksByDirectory(string dirPath)
    {
        var prefix = dirPath.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT path, COALESCE(source_path, path), file_name, title, disc_number, track_number " +
            "FROM tracks WHERE COALESCE(source_path, path) LIKE $prefix || '%' " +
            "ORDER BY disc_number, track_number, file_name;";
        cmd.Parameters.AddWithValue("$prefix", prefix);
        using var reader = cmd.ExecuteReader();
        var result = new List<TrackLite>();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            var sourcePath = reader.GetString(1);
            if (!string.Equals(System.IO.Path.GetDirectoryName(sourcePath), dirPath,
                               StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(new TrackLite(
                path,
                sourcePath,
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : (int?)reader.GetInt64(4),
                reader.IsDBNull(5) ? null : (int?)reader.GetInt64(5)));
        }
        return result;
    }

    /// <summary>Alle Track-Pfade rekursiv unterhalb von <paramref name="rootPath"/>, inkl. Unterordner.</summary>
    public List<string> GetTrackPathsUnderDirectory(string rootPath)
    {
        var prefix = rootPath.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT path FROM tracks WHERE COALESCE(source_path, path) LIKE $prefix || '%' ORDER BY path;";
        cmd.Parameters.AddWithValue("$prefix", prefix);
        using var reader = cmd.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    /// <summary>
    /// Gibt Pfad + modified_at aller bekannten Tracks zurück – effizient für Re-Scan-Erkennung.
    /// </summary>
    public Dictionary<string, long> GetPathTimestamps()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT path, modified_at FROM tracks WHERE cue_path IS NULL;";
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetInt64(1);
        return result;
    }

    /// <summary>Returns minimal track records required for library-root cleanup.</summary>
    /// <returns>Track records containing path, source path, duration, file size, modified time, and CUE segment fields.</returns>
    public List<TrackRecord> GetTrackCleanupRecords()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, path, COALESCE(source_path, path) AS source_path, duration,
                   file_size, modified_at, segment_start, segment_end
            FROM tracks;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<TrackRecord>();
        while (reader.Read())
        {
            result.Add(new TrackRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                Path = reader.GetString(reader.GetOrdinal("path")),
                SourcePath = reader.GetString(reader.GetOrdinal("source_path")),
                Duration = NullableDouble(reader, "duration"),
                FileSize = NullableLong(reader, "file_size"),
                ModifiedAt = reader.GetInt64(reader.GetOrdinal("modified_at")),
                SegmentStart = NullableDouble(reader, "segment_start"),
                SegmentEnd = NullableDouble(reader, "segment_end")
            });
        }

        return result;
    }

    public int CountByDirectory(string rootPath)
    {
        var prefix = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM tracks WHERE COALESCE(source_path, path) LIKE $prefix || '%';";
        cmd.Parameters.AddWithValue("$prefix", prefix);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Deletes a track by absolute path.</summary>
    /// <param name="path">Absolute audio-file path.</param>
    /// <returns><see langword="true"/> when a database row was removed.</returns>
    public bool Delete(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tracks WHERE path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        return cmd.ExecuteNonQuery() > 0;
    }

    public int DeleteCueTracks(string cuePath, IReadOnlyCollection<string>? exceptPaths = null)
    {
        using var cmd = _conn.CreateCommand();
        var exclusion = string.Empty;
        if (exceptPaths is { Count: > 0 })
        {
            var parameters = exceptPaths.Select((path, index) =>
            {
                var name = $"$keep{index}";
                Add(cmd, name, path);
                return name;
            }).ToList();
            exclusion = $" AND path NOT IN ({string.Join(", ", parameters)})";
        }
        cmd.CommandText = $"DELETE FROM tracks WHERE cue_path = $cue_path{exclusion};";
        Add(cmd, "$cue_path", cuePath);
        return cmd.ExecuteNonQuery();
    }

    public List<string> GetTrackPathsByCue(string cuePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT path FROM tracks WHERE cue_path = $cue_path;";
        Add(cmd, "$cue_path", cuePath);
        using var reader = cmd.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    public string? GetSourcePath(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(source_path, path) FROM tracks WHERE path = $path LIMIT 1;";
        Add(cmd, "$path", path);
        return cmd.ExecuteScalar() as string;
    }

    public List<string> GetCuePathsForSource(string sourcePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT cue_path
            FROM tracks
            WHERE source_path = $source_path AND cue_path IS NOT NULL;
            """;
        Add(cmd, "$source_path", sourcePath);
        using var reader = cmd.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    // ------------------------------------------------------------------
    // Schema
    // ------------------------------------------------------------------

    private void ApplyPragmas()
    {
        Execute("PRAGMA journal_mode = WAL;");
        Execute("PRAGMA synchronous  = NORMAL;");
        Execute("PRAGMA foreign_keys = ON;");
        Execute("PRAGMA temp_store   = MEMORY;");
    }

    private void EnsureSchema()
    {
        using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: tracks table/indexes"))
        {
            Execute("""
            CREATE TABLE IF NOT EXISTS tracks (
                -- Primärschlüssel
                id                      INTEGER PRIMARY KEY AUTOINCREMENT,

                -- Dateisystem
                path                    TEXT    NOT NULL UNIQUE,
                file_name               TEXT    NOT NULL,
                file_size               INTEGER,
                modified_at             INTEGER NOT NULL,
                added_at                INTEGER NOT NULL,

                -- Technische Audio-Metadaten
                format                  TEXT,
                duration                REAL,
                sample_rate             INTEGER,
                bit_depth               INTEGER,
                channels                INTEGER,
                bitrate                 INTEGER,
                is_lossless             INTEGER NOT NULL DEFAULT 0,
                is_dsd                  INTEGER NOT NULL DEFAULT 0,
                dsd_rate                INTEGER,

                -- ID3 / Allgemeine Tags
                title                   TEXT,
                sort_title              TEXT,
                artist                  TEXT,
                sort_artist             TEXT,
                album_artist            TEXT,
                sort_album_artist       TEXT,
                album                   TEXT,
                sort_album              TEXT,
                genre                   TEXT,
                year                    INTEGER,
                date                    TEXT,
                track_number            INTEGER,
                track_total             INTEGER,
                disc_number             INTEGER,
                disc_total              INTEGER,
                composer                TEXT,
                conductor               TEXT,
                lyricist                TEXT,
                lyrics                  TEXT,
                downloaded_lyrics       TEXT,
                synced_lyrics           TEXT,
                lyrics_source           TEXT,
                lyrics_fetched_at       INTEGER,
                comment                 TEXT,
                copyright               TEXT,
                publisher               TEXT,
                encoded_by              TEXT,
                encoding_settings       TEXT,
                bpm                     INTEGER,
                compilation             INTEGER NOT NULL DEFAULT 0,
                isrc                    TEXT,
                language                TEXT,
                mood                    TEXT,
                replay_gain_track       TEXT,
                replay_gain_album       TEXT,

                -- MusicBrainz / AcoustID
                musicbrainz_track_id    TEXT,
                musicbrainz_release_id  TEXT,
                musicbrainz_artist_id   TEXT,
                acoustid_fingerprint    TEXT,

                -- Cover Art
                has_cover               INTEGER NOT NULL DEFAULT 0,
                cover_mime_type         TEXT,
                cover_data              BLOB,
                artist_id               INTEGER REFERENCES artists(id),
                album_id                INTEGER REFERENCES albums(id),
                is_favorite             INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_tracks_artist         ON tracks (artist);
            CREATE INDEX IF NOT EXISTS idx_tracks_album_artist   ON tracks (album_artist);
            CREATE INDEX IF NOT EXISTS idx_tracks_album          ON tracks (album);
            CREATE INDEX IF NOT EXISTS idx_tracks_genre          ON tracks (genre);
            CREATE INDEX IF NOT EXISTS idx_tracks_year           ON tracks (year);
            CREATE INDEX IF NOT EXISTS idx_tracks_title          ON tracks (title);
            CREATE INDEX IF NOT EXISTS idx_tracks_format         ON tracks (format);
            CREATE INDEX IF NOT EXISTS idx_tracks_modified_at    ON tracks (modified_at);
            """);
        }

        using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: tracks compatibility columns"))
        {
            EnsureColumn("tracks", "artist_id", "INTEGER REFERENCES artists(id)");
            EnsureColumn("tracks", "album_id", "INTEGER REFERENCES albums(id)");
            EnsureColumn("tracks", "is_favorite", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn("tracks", "source_path", "TEXT");
            EnsureColumn("tracks", "cue_path", "TEXT");
            EnsureColumn("tracks", "segment_start", "REAL");
            EnsureColumn("tracks", "segment_end", "REAL");
            Execute("UPDATE tracks SET source_path = path WHERE source_path IS NULL OR source_path = '';");
            Execute("CREATE INDEX IF NOT EXISTS idx_tracks_source_path ON tracks (source_path);");
            Execute("CREATE INDEX IF NOT EXISTS idx_tracks_cue_path ON tracks (cue_path);");
            EnsureColumn("tracks", "downloaded_lyrics", "TEXT");
            EnsureColumn("tracks", "synced_lyrics", "TEXT");
            EnsureColumn("tracks", "lyrics_source", "TEXT");
            EnsureColumn("tracks", "lyrics_fetched_at", "INTEGER");
        }

        using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: core tables/indexes"))
        {
            Execute("""
            CREATE TABLE IF NOT EXISTS artists (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT NOT NULL UNIQUE COLLATE NOCASE,
                is_favorite INTEGER NOT NULL DEFAULT 0,
                biography TEXT,
                image_path TEXT,
                image_is_manual INTEGER NOT NULL DEFAULT 0,
                profile_source_url TEXT,
                profile_language TEXT,
                profile_fetched_at INTEGER
            );

            CREATE TABLE IF NOT EXISTS artist_aliases (
                alias_key TEXT PRIMARY KEY,
                artist_id INTEGER NOT NULL REFERENCES artists(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS artworks (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                content_hash TEXT NOT NULL UNIQUE,
                mime_type    TEXT,
                data         BLOB NOT NULL,
                original_path TEXT,
                thumb_96_path TEXT,
                thumb_320_path TEXT
            );

            CREATE TABLE IF NOT EXISTS albums (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                title       TEXT NOT NULL,
                source_directory TEXT NOT NULL DEFAULT '',
                artist_id   INTEGER REFERENCES artists(id),
                year        INTEGER,
                artwork_id  INTEGER REFERENCES artworks(id),
                is_favorite INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS favorites (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                target_type TEXT NOT NULL CHECK(target_type IN ('track','artist','album')),
                target_id   INTEGER NOT NULL,
                created_at  INTEGER NOT NULL,
                UNIQUE(target_type, target_id)
            );

            CREATE TABLE IF NOT EXISTS play_history (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                track_id         INTEGER REFERENCES tracks(id) ON DELETE SET NULL,
                path             TEXT NOT NULL,
                started_at       INTEGER NOT NULL,
                ended_at         INTEGER,
                duration_seconds REAL,
                position_seconds REAL,
                completed        INTEGER NOT NULL DEFAULT 0,
                media_type       TEXT NOT NULL DEFAULT 'track',
                title            TEXT,
                subtitle         TEXT,
                album            TEXT,
                external_id      TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_albums_artist        ON albums (artist_id);
            CREATE INDEX IF NOT EXISTS idx_artist_aliases_artist ON artist_aliases (artist_id);
            CREATE INDEX IF NOT EXISTS idx_tracks_artist_id     ON tracks (artist_id);
            CREATE INDEX IF NOT EXISTS idx_tracks_album_id      ON tracks (album_id);
            CREATE INDEX IF NOT EXISTS idx_play_history_track   ON play_history (track_id, started_at DESC);
            CREATE INDEX IF NOT EXISTS idx_play_history_started ON play_history (started_at DESC);

            CREATE TABLE IF NOT EXISTS app_meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);
        }

        using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: legacy cache path migration"))
            MigrateLegacyCachePaths();

        using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: non-track compatibility columns"))
        {
            EnsureColumn("artists", "is_favorite", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn("artists", "biography", "TEXT");
            EnsureColumn("artists", "image_path", "TEXT");
            EnsureColumn("artists", "image_is_manual", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn("artists", "profile_source_url", "TEXT");
            EnsureColumn("artists", "profile_language", "TEXT");
            EnsureColumn("artists", "profile_fetched_at", "INTEGER");
            EnsureColumn("albums", "is_favorite", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn("albums", "source_directory", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn("artworks", "original_path", "TEXT");
            EnsureColumn("artworks", "thumb_96_path", "TEXT");
            EnsureColumn("artworks", "thumb_320_path", "TEXT");
            EnsureColumn("play_history", "media_type", "TEXT NOT NULL DEFAULT 'track'");
            EnsureColumn("play_history", "title", "TEXT");
            EnsureColumn("play_history", "subtitle", "TEXT");
            EnsureColumn("play_history", "album", "TEXT");
            EnsureColumn("play_history", "external_id", "TEXT");
            // Genre captured at playback time so genre statistics can include tracks
            // without a local library row (remote Orynivo Server and Plex tracks).
            EnsureColumn("play_history", "genre", "TEXT");
        }

        if (!string.Equals(GetMeta("normalized_library_v1"), "done", StringComparison.Ordinal))
        {
            using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: MigrateNormalizedLibrary"))
                MigrateNormalizedLibrary();
            SetMeta("normalized_library_v1", "done");
        }
        if (!string.Equals(GetMeta("album_disc_directory_identity_v1"), "done", StringComparison.Ordinal))
        {
            using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: RebuildAlbumsByPhysicalDirectory"))
                RebuildAlbumsByPhysicalDirectory();
            SetMeta("album_artist_rebuild_v1", "done");
            SetMeta("album_title_uniqueness_v1", "done");
            SetMeta("album_title_artist_identity_v1", "done");
            SetMeta("album_title_directory_identity_v1", "done");
            SetMeta("album_disc_directory_identity_v1", "done");
        }
        using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: album identity indexes"))
        {
            Execute("""
            DROP INDEX IF EXISTS idx_albums_title_artist_identity;
            CREATE UNIQUE INDEX IF NOT EXISTS idx_albums_title_directory_identity
            ON albums (title COLLATE NOCASE, source_directory COLLATE NOCASE);
            """);
        }
        if (!string.Equals(GetMeta("artwork_files_v1"), "done", StringComparison.Ordinal))
        {
            using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: MigrateArtworkFiles"))
                MigrateArtworkFiles();
            SetMeta("artwork_files_v1", "done");
        }
        var artworkRoot = AppPaths.GetDataPath("artworks");
        if (!string.Equals(GetMeta("artwork_files_root_v1"), artworkRoot, StringComparison.Ordinal))
        {
            using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: RepairArtworkFilesForCurrentRoot"))
                RepairArtworkFilesForCurrentRoot();
            SetMeta("artwork_files_root_v1", artworkRoot);
        }
        if (!string.Equals(GetMeta("trim_track_titles_v1"), "done", StringComparison.Ordinal))
        {
            using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: TrimExistingTrackTitles"))
                TrimExistingTrackTitles();
            SetMeta("trim_track_titles_v1", "done");
        }

        using (Orynivo.StartupDiagnostics.Time("AudioDatabase.EnsureSchema: playlists/radio/podcast tables"))
        {
            Execute("""
            CREATE TABLE IF NOT EXISTS playlists (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                name             TEXT    NOT NULL,
                description      TEXT,
                created_at       INTEGER NOT NULL,
                modified_at      INTEGER NOT NULL,
                is_smart         INTEGER NOT NULL DEFAULT 0,
                filter_criteria  TEXT
            );

            CREATE TABLE IF NOT EXISTS playlist_tracks (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
                track_id    INTEGER REFERENCES tracks(id) ON DELETE SET NULL,
                path        TEXT    NOT NULL,
                position    INTEGER NOT NULL,
                added_at    INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_playlist_tracks_playlist ON playlist_tracks (playlist_id, position);
            CREATE INDEX IF NOT EXISTS idx_playlist_tracks_path     ON playlist_tracks (path);

            CREATE TABLE IF NOT EXISTS playback_queue (
                position   INTEGER PRIMARY KEY,
                path       TEXT    NOT NULL,
                is_current INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS radio_stations (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                station_uuid  TEXT NOT NULL UNIQUE,
                name          TEXT NOT NULL,
                stream_url    TEXT NOT NULL,
                homepage      TEXT,
                favicon       TEXT,
                country_code  TEXT,
                codec         TEXT,
                bitrate       INTEGER NOT NULL DEFAULT 0,
                tags          TEXT,
                created_at    INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_radio_stations_name
                ON radio_stations (name COLLATE NOCASE);

            CREATE TABLE IF NOT EXISTS podcasts (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                collection_id  INTEGER NOT NULL UNIQUE,
                name           TEXT NOT NULL,
                author         TEXT,
                feed_url       TEXT NOT NULL,
                artwork_url    TEXT,
                genre          TEXT,
                created_at     INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_podcasts_name
                ON podcasts (name COLLATE NOCASE);

            CREATE TABLE IF NOT EXISTS podcast_episode_progress (
                podcast_id       INTEGER NOT NULL REFERENCES podcasts(id) ON DELETE CASCADE,
                episode_key      TEXT NOT NULL,
                position_seconds REAL NOT NULL DEFAULT 0,
                duration_seconds REAL,
                is_completed     INTEGER NOT NULL DEFAULT 0,
                updated_at       INTEGER NOT NULL,
                PRIMARY KEY (podcast_id, episode_key)
            );
            """);
            EnsureColumn("playlists", "is_smart",        "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn("playlists", "filter_criteria",  "TEXT");
        }
    }

    // ------------------------------------------------------------------
    // Internetradio
    // ------------------------------------------------------------------

    public long SaveRadioStation(RadioBrowserStation station)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO radio_stations (
                station_uuid, name, stream_url, homepage, favicon,
                country_code, codec, bitrate, tags, created_at)
            VALUES (
                $uuid, $name, $url, $homepage, $favicon,
                $country, $codec, $bitrate, $tags, $created)
            ON CONFLICT(station_uuid) DO UPDATE SET
                name = excluded.name,
                stream_url = excluded.stream_url,
                homepage = excluded.homepage,
                favicon = excluded.favicon,
                country_code = excluded.country_code,
                codec = excluded.codec,
                bitrate = excluded.bitrate,
                tags = excluded.tags
            RETURNING id;
            """;
        Add(cmd, "$uuid", station.StationUuid);
        Add(cmd, "$name", station.Name);
        Add(cmd, "$url", station.StreamUrl);
        Add(cmd, "$homepage", station.Homepage);
        Add(cmd, "$favicon", station.Favicon);
        Add(cmd, "$country", station.CountryCode);
        Add(cmd, "$codec", station.Codec);
        Add(cmd, "$bitrate", station.Bitrate);
        Add(cmd, "$tags", station.Tags);
        Add(cmd, "$created", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return (long)cmd.ExecuteScalar()!;
    }

    public List<RadioStationRecord> GetRadioStations()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, station_uuid, name, stream_url, homepage, favicon,
                   country_code, codec, bitrate, tags
            FROM radio_stations
            ORDER BY name COLLATE NOCASE;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<RadioStationRecord>();
        while (reader.Read())
            result.Add(MapRadioStation(reader));
        return result;
    }

    public RadioStationRecord? GetRadioStation(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, station_uuid, name, stream_url, homepage, favicon,
                   country_code, codec, bitrate, tags
            FROM radio_stations
            WHERE id = $id
            LIMIT 1;
            """;
        Add(cmd, "$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapRadioStation(reader) : null;
    }

    public void DeleteRadioStation(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM radio_stations WHERE id = $id;";
        Add(cmd, "$id", id);
        cmd.ExecuteNonQuery();
    }

    private static RadioStationRecord MapRadioStation(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetString(9));

    // ------------------------------------------------------------------
    // Podcasts
    // ------------------------------------------------------------------

    public long SavePodcast(PodcastSearchResult podcast)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO podcasts (
                collection_id, name, author, feed_url, artwork_url, genre, created_at)
            VALUES (
                $collectionId, $name, $author, $feedUrl, $artworkUrl, $genre, $created)
            ON CONFLICT(collection_id) DO UPDATE SET
                name = excluded.name,
                author = excluded.author,
                feed_url = excluded.feed_url,
                artwork_url = excluded.artwork_url,
                genre = excluded.genre
            RETURNING id;
            """;
        Add(cmd, "$collectionId", podcast.CollectionId);
        Add(cmd, "$name", podcast.Name);
        Add(cmd, "$author", podcast.Author);
        Add(cmd, "$feedUrl", podcast.FeedUrl);
        Add(cmd, "$artworkUrl", podcast.ArtworkUrl);
        Add(cmd, "$genre", podcast.Genre);
        Add(cmd, "$created", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return (long)cmd.ExecuteScalar()!;
    }

    public List<PodcastRecord> GetPodcasts()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, collection_id, name, author, feed_url, artwork_url, genre
            FROM podcasts
            ORDER BY name COLLATE NOCASE;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<PodcastRecord>();
        while (reader.Read())
            result.Add(MapPodcast(reader));
        return result;
    }

    public PodcastRecord? GetPodcast(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, collection_id, name, author, feed_url, artwork_url, genre
            FROM podcasts
            WHERE id = $id
            LIMIT 1;
            """;
        Add(cmd, "$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapPodcast(reader) : null;
    }

    public void DeletePodcast(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM podcasts WHERE id = $id;";
        Add(cmd, "$id", id);
        cmd.ExecuteNonQuery();
    }

    private static PodcastRecord MapPodcast(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));

    public Dictionary<string, PodcastEpisodeProgress> GetPodcastEpisodeProgress(long podcastId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT episode_key, position_seconds, duration_seconds, is_completed
            FROM podcast_episode_progress
            WHERE podcast_id = $podcastId;
            """;
        Add(cmd, "$podcastId", podcastId);
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<string, PodcastEpisodeProgress>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var progress = new PodcastEpisodeProgress(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.GetInt32(3) != 0);
            result[progress.EpisodeKey] = progress;
        }
        return result;
    }

    public PodcastEpisodeProgress? GetPodcastEpisodeProgress(long podcastId, string episodeKey)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT episode_key, position_seconds, duration_seconds, is_completed
            FROM podcast_episode_progress
            WHERE podcast_id = $podcastId AND episode_key = $episodeKey
            LIMIT 1;
            """;
        Add(cmd, "$podcastId", podcastId);
        Add(cmd, "$episodeKey", episodeKey);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new PodcastEpisodeProgress(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.GetInt32(3) != 0)
            : null;
    }

    public void SavePodcastEpisodeProgress(
        long podcastId,
        string episodeKey,
        double positionSeconds,
        double? durationSeconds,
        bool isCompleted)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO podcast_episode_progress (
                podcast_id, episode_key, position_seconds, duration_seconds,
                is_completed, updated_at)
            VALUES (
                $podcastId, $episodeKey, $position, $duration,
                $completed, $updated)
            ON CONFLICT(podcast_id, episode_key) DO UPDATE SET
                position_seconds = excluded.position_seconds,
                duration_seconds = excluded.duration_seconds,
                is_completed = excluded.is_completed,
                updated_at = excluded.updated_at;
            """;
        Add(cmd, "$podcastId", podcastId);
        Add(cmd, "$episodeKey", episodeKey);
        Add(cmd, "$position", Math.Max(0, positionSeconds));
        Add(cmd, "$duration", durationSeconds);
        Add(cmd, "$completed", isCompleted ? 1 : 0);
        Add(cmd, "$updated", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------
    // Playlisten – CRUD
    // ------------------------------------------------------------------

    /// <summary>Loads the persisted editable playback queue.</summary>
    /// <returns>The saved queue paths and current queue index.</returns>
    public PlaybackQueueSnapshot GetPlaybackQueue()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT position, path, is_current
            FROM playback_queue
            ORDER BY position;
            """;
        using var reader = cmd.ExecuteReader();
        var paths = new List<string>();
        var currentIndex = -1;
        while (reader.Read())
        {
            paths.Add(reader.GetString(1));
            if (reader.GetInt32(2) != 0)
                currentIndex = paths.Count - 1;
        }

        return new PlaybackQueueSnapshot(paths, currentIndex);
    }

    /// <summary>Replaces the persisted editable playback queue transactionally.</summary>
    /// <param name="paths">Queue paths in playback order.</param>
    /// <param name="currentIndex">Zero-based current queue index, or <c>-1</c> when no item is current.</param>
    public void SavePlaybackQueue(IReadOnlyList<string> paths, int currentIndex)
    {
        using var transaction = _conn.BeginTransaction();
        using (var deleteCommand = _conn.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM playback_queue;";
            deleteCommand.ExecuteNonQuery();
        }

        using var insertCommand = _conn.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO playback_queue (position, path, is_current)
            VALUES ($position, $path, $current);
            """;
        var positionParameter = insertCommand.Parameters.Add("$position", SqliteType.Integer);
        var pathParameter = insertCommand.Parameters.Add("$path", SqliteType.Text);
        var currentParameter = insertCommand.Parameters.Add("$current", SqliteType.Integer);

        for (var index = 0; index < paths.Count; index++)
        {
            positionParameter.Value = index + 1;
            pathParameter.Value = paths[index];
            currentParameter.Value = index == currentIndex ? 1 : 0;
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Returns the paths of the previously playing queue, captured the last time the queue was
    /// replaced wholesale, so it can be restored on demand. Empty when no snapshot exists.
    /// </summary>
    /// <returns>The previous queue's ordered paths, or an empty list.</returns>
    public IReadOnlyList<string> GetPreviousPlaybackQueue()
    {
        var json = GetMeta("previous_playback_queue");
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Stores the paths of the queue that was just replaced, for later restoration.</summary>
    /// <param name="paths">The previous queue's ordered paths.</param>
    public void SavePreviousPlaybackQueue(IReadOnlyList<string> paths)
    {
        SetMeta("previous_playback_queue", System.Text.Json.JsonSerializer.Serialize(paths));
    }

    public long CreateSmartPlaylist(string name, string filterCriteria)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO playlists (name, is_smart, filter_criteria, created_at, modified_at)
            VALUES ($name, 1, $filter, $now, $now)
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$filter", filterCriteria);
        cmd.Parameters.AddWithValue("$now", now);
        return (long)cmd.ExecuteScalar()!;
    }

    public long CreatePlaylist(string name, string? description = null)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO playlists (name, description, created_at, modified_at)
            VALUES ($name, $desc, $now, $now)
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Creates a regular playlist and inserts all supplied paths transactionally.</summary>
    /// <param name="name">Playlist display name.</param>
    /// <param name="paths">Local paths or remote stream URLs in playlist order.</param>
    /// <returns>The new playlist identifier.</returns>
    public long CreatePlaylist(string name, IReadOnlyList<string> paths)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var transaction = _conn.BeginTransaction();
        using var playlistCommand = _conn.CreateCommand();
        playlistCommand.Transaction = transaction;
        playlistCommand.CommandText = """
            INSERT INTO playlists (name, created_at, modified_at)
            VALUES ($name, $now, $now)
            RETURNING id;
            """;
        playlistCommand.Parameters.AddWithValue("$name", name);
        playlistCommand.Parameters.AddWithValue("$now", now);
        var playlistId = (long)playlistCommand.ExecuteScalar()!;

        using var itemCommand = _conn.CreateCommand();
        itemCommand.Transaction = transaction;
        itemCommand.CommandText = """
            INSERT INTO playlist_tracks (
                playlist_id, track_id, path, position, added_at)
            VALUES (
                $playlistId,
                (SELECT id FROM tracks WHERE path = $path LIMIT 1),
                $path,
                $position,
                $now);
            """;
        var playlistParameter = itemCommand.Parameters.Add("$playlistId", Microsoft.Data.Sqlite.SqliteType.Integer);
        var pathParameter = itemCommand.Parameters.Add("$path", Microsoft.Data.Sqlite.SqliteType.Text);
        var positionParameter = itemCommand.Parameters.Add("$position", Microsoft.Data.Sqlite.SqliteType.Integer);
        var nowParameter = itemCommand.Parameters.Add("$now", Microsoft.Data.Sqlite.SqliteType.Integer);
        playlistParameter.Value = playlistId;
        nowParameter.Value = now;

        for (var index = 0; index < paths.Count; index++)
        {
            pathParameter.Value = paths[index];
            positionParameter.Value = index + 1;
            itemCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return playlistId;
    }

    public void UpdatePlaylist(long id, string name, string? description = null)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE playlists
            SET name = $name, description = $desc, modified_at = $now
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Updates the name and serialized criteria of an existing smart playlist.</summary>
    /// <param name="id">Smart-playlist database identifier.</param>
    /// <param name="name">Updated display name.</param>
    /// <param name="filterCriteria">Updated JSON-serialized smart-playlist criteria.</param>
    public void UpdateSmartPlaylist(long id, string name, string filterCriteria)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE playlists
            SET name = $name,
                filter_criteria = $filter,
                modified_at = $now
            WHERE id = $id
              AND is_smart = 1;
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$filter", filterCriteria);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeletePlaylist(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playlists WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        // ON DELETE CASCADE entfernt zugehörige playlist_tracks automatisch
    }

    public IEnumerable<PlaylistRecord> GetAllPlaylists()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.*, COUNT(pt.id) AS track_count
            FROM playlists p
            LEFT JOIN playlist_tracks pt ON pt.playlist_id = p.id
            GROUP BY p.id
            ORDER BY p.name COLLATE NOCASE;
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            yield return MapPlaylist(reader);
    }

    public PlaylistRecord? GetPlaylistById(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.*, COUNT(pt.id) AS track_count
            FROM playlists p
            LEFT JOIN playlist_tracks pt ON pt.playlist_id = p.id
            WHERE p.id = $id
            GROUP BY p.id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapPlaylist(reader) : null;
    }

    // ------------------------------------------------------------------
    // Playlist-Tracks – CRUD
    // ------------------------------------------------------------------

    public IEnumerable<PlaylistTrackRecord> GetPlaylistTracks(long playlistId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM playlist_tracks
            WHERE playlist_id = $pid
            ORDER BY position;
            """;
        cmd.Parameters.AddWithValue("$pid", playlistId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            yield return MapPlaylistTrack(reader);
    }

    public void AddTrackToPlaylist(long playlistId, string path, long? trackId = null)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO playlist_tracks (playlist_id, track_id, path, position, added_at)
            VALUES ($pid, $tid, $path,
                    COALESCE((SELECT MAX(position) FROM playlist_tracks WHERE playlist_id = $pid), 0) + 1,
                    $now);
            UPDATE playlists SET modified_at = $now WHERE id = $pid;
            """;
        cmd.Parameters.AddWithValue("$pid", playlistId);
        cmd.Parameters.AddWithValue("$tid", (object?)trackId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    public void RemoveTrackFromPlaylist(long playlistTrackId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE playlists SET modified_at = $now
            WHERE id = (SELECT playlist_id FROM playlist_tracks WHERE id = $id);
            DELETE FROM playlist_tracks WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", playlistTrackId);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Verschiebt einen Track innerhalb der Playlist auf eine neue 1-basierte Position
    /// und nummeriert alle anderen Einträge lückenlos neu.
    /// </summary>
    public void MovePlaylistTrack(long playlistId, long playlistTrackId, int newPosition)
    {
        using var tx = _conn.BeginTransaction();
        try
        {
            using var selectCmd = _conn.CreateCommand();
            selectCmd.Transaction = tx;
            selectCmd.CommandText =
                "SELECT id FROM playlist_tracks WHERE playlist_id = $pid ORDER BY position;";
            selectCmd.Parameters.AddWithValue("$pid", playlistId);

            var ids = new List<long>();
            using (var reader = selectCmd.ExecuteReader())
                while (reader.Read())
                    ids.Add(reader.GetInt64(0));

            ids.Remove(playlistTrackId);
            ids.Insert(Math.Clamp(newPosition - 1, 0, ids.Count), playlistTrackId);

            for (int i = 0; i < ids.Count; i++)
            {
                using var updateCmd = _conn.CreateCommand();
                updateCmd.Transaction = tx;
                updateCmd.CommandText =
                    "UPDATE playlist_tracks SET position = $pos WHERE id = $id;";
                updateCmd.Parameters.AddWithValue("$pos", i + 1);
                updateCmd.Parameters.AddWithValue("$id", ids[i]);
                updateCmd.ExecuteNonQuery();
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using var touchCmd = _conn.CreateCommand();
            touchCmd.Transaction = tx;
            touchCmd.CommandText =
                "UPDATE playlists SET modified_at = $now WHERE id = $pid;";
            touchCmd.Parameters.AddWithValue("$now", now);
            touchCmd.Parameters.AddWithValue("$pid", playlistId);
            touchCmd.ExecuteNonQuery();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ------------------------------------------------------------------
    // Hilfsmethoden
    // ------------------------------------------------------------------

    private void Execute(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private int ExecuteInTransaction(
        SqliteTransaction tx,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var parameter in parameters)
            Add(cmd, parameter.Name, parameter.Value);
        return cmd.ExecuteNonQuery();
    }

    private object? ExecuteScalarInTransaction(
        SqliteTransaction tx,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var parameter in parameters)
            Add(cmd, parameter.Name, parameter.Value);
        return cmd.ExecuteScalar();
    }

    private void CreateAlbumsTable(SqliteTransaction tx)
    {
        ExecuteInTransaction(tx, """
            CREATE TABLE albums (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                title            TEXT NOT NULL,
                source_directory TEXT NOT NULL DEFAULT '',
                artist_id        INTEGER REFERENCES artists(id),
                year             INTEGER,
                artwork_id       INTEGER REFERENCES artworks(id),
                is_favorite      INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX idx_albums_artist ON albums (artist_id);
            CREATE UNIQUE INDEX idx_albums_title_directory_identity
            ON albums (title COLLATE NOCASE, source_directory COLLATE NOCASE);
            """);
    }

    private static string NormalizeAlbumTitleKey(string? title) =>
        title?.Trim().ToUpperInvariant() ?? string.Empty;

    private string? GetMeta(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_meta WHERE key = $key LIMIT 1;";
        Add(cmd, "$key", key);
        return cmd.ExecuteScalar() as string;
    }

    private void SetMeta(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_meta (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        Add(cmd, "$key", key);
        Add(cmd, "$value", value);
        cmd.ExecuteNonQuery();
    }

    private void SetFavorite(string table, long id, bool value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"UPDATE {table} SET is_favorite = $value WHERE id = $id;";
        Add(cmd, "$value", value ? 1 : 0);
        Add(cmd, "$id", id);
        cmd.ExecuteNonQuery();
    }

    private void MigrateArtworkFiles()
    {
        using var select = _conn.CreateCommand();
        select.CommandText = """
            SELECT id, content_hash, mime_type, data
            FROM artworks
            WHERE data IS NOT NULL
              AND (original_path IS NULL OR thumb_96_path IS NULL OR thumb_320_path IS NULL);
            """;
        using var reader = select.ExecuteReader();
        var rows = new List<(long Id, string Hash, string? Mime, byte[] Data)>();
        while (reader.Read())
            rows.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), (byte[])reader.GetValue(3)));
        reader.Close();

        foreach (var row in rows)
        {
            var files = ArtworkCache.EnsureFiles(row.Hash, row.Data, row.Mime);
            using var update = _conn.CreateCommand();
            update.CommandText = """
                UPDATE artworks
                SET original_path = $original,
                    thumb_96_path = $thumb96,
                    thumb_320_path = $thumb320
                WHERE id = $id;
                """;
            Add(update, "$original", files.OriginalPath);
            Add(update, "$thumb96", files.Thumb96Path);
            Add(update, "$thumb320", files.Thumb320Path);
            Add(update, "$id", row.Id);
            update.ExecuteNonQuery();
        }
    }

    private void RepairArtworkFilesForCurrentRoot()
    {
        using var select = _conn.CreateCommand();
        select.CommandText = """
            SELECT id, content_hash, mime_type, data, original_path, thumb_96_path, thumb_320_path
            FROM artworks
            WHERE data IS NOT NULL;
            """;
        using var reader = select.ExecuteReader();
        var rows = new List<(long Id, string Hash, string? Mime, byte[] Data, string? Original, string? Thumb96, string? Thumb320)>();
        while (reader.Read())
        {
            rows.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                (byte[])reader.GetValue(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        reader.Close();

        foreach (var row in rows)
        {
            if (File.Exists(row.Original) && File.Exists(row.Thumb96) && File.Exists(row.Thumb320))
                continue;

            var files = ArtworkCache.EnsureFiles(row.Hash, row.Data, row.Mime);
            using var update = _conn.CreateCommand();
            update.CommandText = """
                UPDATE artworks
                SET original_path = $original,
                    thumb_96_path = $thumb96,
                    thumb_320_path = $thumb320
                WHERE id = $id;
                """;
            Add(update, "$original", files.OriginalPath);
            Add(update, "$thumb96", files.Thumb96Path);
            Add(update, "$thumb320", files.Thumb320Path);
            Add(update, "$id", row.Id);
            update.ExecuteNonQuery();
        }
    }

    private void EnsureColumn(string table, string column, string definition)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    private void TrimExistingTrackTitles()
    {
        var titles = new List<(long Id, string? Title, string? SortTitle)>();
        using (var select = _conn.CreateCommand())
        {
            select.CommandText = "SELECT id, title, sort_title FROM tracks;";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                titles.Add((
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        using var tx = _conn.BeginTransaction();
        foreach (var track in titles)
        {
            var title = TrimToNull(track.Title);
            var sortTitle = TrimToNull(track.SortTitle);
            if (string.Equals(title, track.Title, StringComparison.Ordinal) &&
                string.Equals(sortTitle, track.SortTitle, StringComparison.Ordinal))
            {
                continue;
            }

            using var update = _conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = """
                UPDATE tracks
                SET title = $title,
                    sort_title = $sort_title
                WHERE id = $id;
                """;
            Add(update, "$title", (object?)title ?? DBNull.Value);
            Add(update, "$sort_title", (object?)sortTitle ?? DBNull.Value);
            Add(update, "$id", track.Id);
            update.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void MigrateNormalizedLibrary()
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, artist, album_artist, album, year, cover_data, cover_mime_type,
                   COALESCE(source_path, path)
            FROM tracks
            WHERE artist_id IS NULL OR album_id IS NULL OR cover_data IS NOT NULL;
            """;
        using var reader = cmd.ExecuteReader();
        var rows = new List<(
            long Id,
            string? Artist,
            string? AlbumArtist,
            string? Album,
            int? Year,
            byte[]? Cover,
            string? Mime,
            string SourcePath)>();
        while (reader.Read())
            rows.Add((
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : (byte[])reader.GetValue(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7)));
        reader.Close();

        foreach (var row in rows)
        {
            var artistId  = EnsureArtist(row.Artist, tx);
            var artworkId = EnsureArtwork(row.Cover, row.Mime, tx);
            var albumId = EnsureAlbum(
                row.Album,
                row.AlbumArtist ?? row.Artist,
                row.Year,
                artworkId,
                GetPhysicalAlbumDirectory(row.SourcePath),
                tx);
            using var update = _conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = """
                UPDATE tracks
                SET artist_id = $artist_id,
                    album_id = $album_id,
                    cover_data = NULL
                WHERE id = $id;
                """;
            Add(update, "$artist_id", artistId);
            Add(update, "$album_id", albumId);
            Add(update, "$id", row.Id);
            update.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private long? EnsureArtist(string? name, SqliteTransaction tx)
    {
        var normalized = ArtistNameNormalizer.NormalizeDisplayName(name);
        var key = ArtistNameNormalizer.CreateComparisonKey(normalized);
        EnsureArtistComparisonCache(tx);
        if (_artistsByComparisonKey!.TryGetValue(key, out var existing))
            return existing.Id;

        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO artists (name) VALUES ($name)
            ON CONFLICT(name) DO NOTHING;
            SELECT id FROM artists WHERE name = $name COLLATE NOCASE LIMIT 1;
            """;
        Add(cmd, "$name", normalized);
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        _artistsByComparisonKey[key] = (id, normalized);
        _artistNamesById![id] = normalized;
        return id;
    }

    private string GetCanonicalArtistName(long? artistId, string? fallback)
    {
        if (artistId is long id &&
            _artistNamesById is not null &&
            _artistNamesById.TryGetValue(id, out var name))
        {
            return name;
        }

        return ArtistNameNormalizer.NormalizeDisplayName(fallback);
    }

    private void EnsureArtistComparisonCache(SqliteTransaction tx)
    {
        if (_artistsByComparisonKey is not null)
            return;

        _artistsByComparisonKey = new Dictionary<string, (long Id, string Name)>(StringComparer.Ordinal);
        _artistNamesById = new Dictionary<long, string>();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, name FROM artists ORDER BY id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var name = reader.GetString(1);
            _artistNamesById[id] = name;
            _artistsByComparisonKey.TryAdd(
                ArtistNameNormalizer.CreateComparisonKey(name),
                (id, name));
        }
        reader.Close();

        cmd.CommandText = """
            SELECT aa.alias_key, ar.id, ar.name
            FROM artist_aliases aa
            JOIN artists ar ON ar.id = aa.artist_id;
            """;
        using var aliasReader = cmd.ExecuteReader();
        while (aliasReader.Read())
        {
            var key = aliasReader.GetString(0);
            var id = aliasReader.GetInt64(1);
            var name = aliasReader.GetString(2);
            _artistsByComparisonKey[key] = (id, name);
        }
    }

    private void ClearArtistIdentityCache()
    {
        _artistsByComparisonKey = null;
        _artistNamesById = null;
    }

    private long? EnsureArtwork(byte[]? data, string? mimeType, SqliteTransaction tx)
    {
        if (data is null || data.Length == 0)
            return null;
        var hash = Convert.ToHexString(SHA256.HashData(data));
        var files = ArtworkCache.EnsureFiles(hash, data, mimeType);
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO artworks (content_hash, mime_type, data, original_path, thumb_96_path, thumb_320_path)
            VALUES ($hash, $mime, $data, $original, $thumb96, $thumb320)
            ON CONFLICT(content_hash) DO UPDATE SET
                original_path = COALESCE(artworks.original_path, excluded.original_path),
                thumb_96_path = COALESCE(artworks.thumb_96_path, excluded.thumb_96_path),
                thumb_320_path = COALESCE(artworks.thumb_320_path, excluded.thumb_320_path);
            SELECT id FROM artworks WHERE content_hash = $hash LIMIT 1;
            """;
        Add(cmd, "$hash", hash);
        Add(cmd, "$mime", mimeType);
        Add(cmd, "$data", data);
        Add(cmd, "$original", files.OriginalPath);
        Add(cmd, "$thumb96", files.Thumb96Path);
        Add(cmd, "$thumb320", files.Thumb320Path);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private long? EnsureAlbum(
        string? title,
        string? displayArtist,
        int? year,
        long? artworkId,
        string sourceDirectory,
        SqliteTransaction tx)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "" : title.Trim();
        var normalizedYear  = year ?? 0;
        var albumArtistId = EnsureArtist(GetFirstArtist(displayArtist), tx);
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE albums
            SET artwork_id = COALESCE(artwork_id, $artwork_id),
                artist_id = CASE
                    WHEN artist_id IS NULL OR artist_id = $artist_id THEN artist_id
                    ELSE NULL
                END
            WHERE id = (
                SELECT id
                FROM albums
                WHERE title = $title COLLATE NOCASE
                  AND source_directory = $source_directory COLLATE NOCASE
                ORDER BY id
                LIMIT 1
            );

            INSERT INTO albums (title, source_directory, artist_id, year, artwork_id)
            SELECT $title, $source_directory, $artist_id, $year, $artwork_id
            WHERE NOT EXISTS (
                SELECT 1
                FROM albums
                WHERE title = $title COLLATE NOCASE
                  AND source_directory = $source_directory COLLATE NOCASE
            );

            SELECT id FROM albums
            WHERE title = $title COLLATE NOCASE
              AND source_directory = $source_directory COLLATE NOCASE
            ORDER BY id
            LIMIT 1;
            """;
        Add(cmd, "$title", normalizedTitle);
        Add(cmd, "$source_directory", sourceDirectory);
        Add(cmd, "$artist_id", albumArtistId);
        Add(cmd, "$year", normalizedYear);
        Add(cmd, "$artwork_id", artworkId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string GetPhysicalAlbumDirectory(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return string.Empty;

        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (!DiscDirectoryNameRegex.IsMatch(leaf))
            return directory;

        return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory))
               ?? directory;
    }

    private static string MakeAlbumDirectoryKey(string? title, string? sourceDirectory) =>
        $"{NormalizeAlbumTitleKey(title)}\u001f{sourceDirectory?.Trim().ToUpperInvariant() ?? string.Empty}";

    private static readonly Regex DiscDirectoryNameRegex = new(
        @"^(?:cd|disc|disk|dvd)[\s._-]*0*[1-9]\d?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? GetFirstArtist(string? displayArtist)
    {
        if (string.IsNullOrWhiteSpace(displayArtist))
            return displayArtist;

        return displayArtist
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? displayArtist.Trim();
    }

    private static void BindTrack(SqliteCommand cmd, TrackRecord t)
    {
        Add(cmd, "$path",                   t.Path);
        Add(cmd, "$source_path",            string.IsNullOrWhiteSpace(t.SourcePath) ? t.Path : t.SourcePath);
        Add(cmd, "$cue_path",               (object?)t.CuePath ?? DBNull.Value);
        Add(cmd, "$segment_start",          (object?)t.SegmentStart ?? DBNull.Value);
        Add(cmd, "$segment_end",            (object?)t.SegmentEnd ?? DBNull.Value);
        Add(cmd, "$file_name",              t.FileName);
        Add(cmd, "$file_size",              (object?)t.FileSize ?? DBNull.Value);
        Add(cmd, "$modified_at",            t.ModifiedAt);
        Add(cmd, "$added_at",               t.AddedAt);
        Add(cmd, "$format",                 (object?)t.Format ?? DBNull.Value);
        Add(cmd, "$duration",               (object?)t.Duration ?? DBNull.Value);
        Add(cmd, "$sample_rate",            (object?)t.SampleRate ?? DBNull.Value);
        Add(cmd, "$bit_depth",              (object?)t.BitDepth ?? DBNull.Value);
        Add(cmd, "$channels",               (object?)t.Channels ?? DBNull.Value);
        Add(cmd, "$bitrate",                (object?)t.Bitrate ?? DBNull.Value);
        Add(cmd, "$is_lossless",            t.IsLossless ? 1 : 0);
        Add(cmd, "$is_dsd",                 t.IsDsd ? 1 : 0);
        Add(cmd, "$dsd_rate",               (object?)t.DsdRate ?? DBNull.Value);
        Add(cmd, "$title",                  (object?)t.Title ?? DBNull.Value);
        Add(cmd, "$sort_title",             (object?)t.SortTitle ?? DBNull.Value);
        Add(cmd, "$artist",                 (object?)t.Artist ?? DBNull.Value);
        Add(cmd, "$sort_artist",            (object?)t.SortArtist ?? DBNull.Value);
        Add(cmd, "$album_artist",           (object?)t.AlbumArtist ?? DBNull.Value);
        Add(cmd, "$sort_album_artist",      (object?)t.SortAlbumArtist ?? DBNull.Value);
        Add(cmd, "$album",                  (object?)t.Album ?? DBNull.Value);
        Add(cmd, "$sort_album",             (object?)t.SortAlbum ?? DBNull.Value);
        Add(cmd, "$genre",                  (object?)t.Genre ?? DBNull.Value);
        Add(cmd, "$year",                   (object?)t.Year ?? DBNull.Value);
        Add(cmd, "$date",                   (object?)t.Date ?? DBNull.Value);
        Add(cmd, "$track_number",           (object?)t.TrackNumber ?? DBNull.Value);
        Add(cmd, "$track_total",            (object?)t.TrackTotal ?? DBNull.Value);
        Add(cmd, "$disc_number",            (object?)t.DiscNumber ?? DBNull.Value);
        Add(cmd, "$disc_total",             (object?)t.DiscTotal ?? DBNull.Value);
        Add(cmd, "$composer",               (object?)t.Composer ?? DBNull.Value);
        Add(cmd, "$conductor",              (object?)t.Conductor ?? DBNull.Value);
        Add(cmd, "$lyricist",               (object?)t.Lyricist ?? DBNull.Value);
        Add(cmd, "$lyrics",                 (object?)t.Lyrics ?? DBNull.Value);
        Add(cmd, "$comment",                (object?)t.Comment ?? DBNull.Value);
        Add(cmd, "$copyright",              (object?)t.Copyright ?? DBNull.Value);
        Add(cmd, "$publisher",              (object?)t.Publisher ?? DBNull.Value);
        Add(cmd, "$encoded_by",             (object?)t.EncodedBy ?? DBNull.Value);
        Add(cmd, "$encoding_settings",      (object?)t.EncodingSettings ?? DBNull.Value);
        Add(cmd, "$bpm",                    (object?)t.Bpm ?? DBNull.Value);
        Add(cmd, "$compilation",            t.Compilation ? 1 : 0);
        Add(cmd, "$isrc",                   (object?)t.Isrc ?? DBNull.Value);
        Add(cmd, "$language",               (object?)t.Language ?? DBNull.Value);
        Add(cmd, "$mood",                   (object?)t.Mood ?? DBNull.Value);
        Add(cmd, "$replay_gain_track",      (object?)t.ReplayGainTrack ?? DBNull.Value);
        Add(cmd, "$replay_gain_album",      (object?)t.ReplayGainAlbum ?? DBNull.Value);
        Add(cmd, "$musicbrainz_track_id",   (object?)t.MusicBrainzTrackId ?? DBNull.Value);
        Add(cmd, "$musicbrainz_release_id", (object?)t.MusicBrainzReleaseId ?? DBNull.Value);
        Add(cmd, "$musicbrainz_artist_id",  (object?)t.MusicBrainzArtistId ?? DBNull.Value);
        Add(cmd, "$acoustid_fingerprint",   (object?)t.AcoustIdFingerprint ?? DBNull.Value);
        Add(cmd, "$has_cover",              t.HasCover ? 1 : 0);
        Add(cmd, "$cover_mime_type",        (object?)t.CoverMimeType ?? DBNull.Value);
        // Artwork wird dedupliziert in `artworks` gespeichert; in `tracks` bleibt kein Duplikat.
        Add(cmd, "$cover_data",             DBNull.Value);
    }

    private static TrackRecord MapRow(SqliteDataReader r)
    {
        return new TrackRecord
        {
            Id                    = r.GetInt64(r.GetOrdinal("id")),
            Path                  = r.GetString(r.GetOrdinal("path")),
            SourcePath            = NullableString(r, "source_path") ?? r.GetString(r.GetOrdinal("path")),
            CuePath               = NullableString(r, "cue_path"),
            SegmentStart          = NullableDouble(r, "segment_start"),
            SegmentEnd            = NullableDouble(r, "segment_end"),
            FileName              = r.GetString(r.GetOrdinal("file_name")),
            FileSize              = NullableLong(r, "file_size"),
            ModifiedAt            = r.GetInt64(r.GetOrdinal("modified_at")),
            AddedAt               = r.GetInt64(r.GetOrdinal("added_at")),
            Format                = NullableString(r, "format"),
            Duration              = NullableDouble(r, "duration"),
            SampleRate            = NullableInt(r, "sample_rate"),
            BitDepth              = NullableInt(r, "bit_depth"),
            Channels              = NullableInt(r, "channels"),
            Bitrate               = NullableInt(r, "bitrate"),
            IsLossless            = r.GetInt32(r.GetOrdinal("is_lossless")) != 0,
            IsDsd                 = r.GetInt32(r.GetOrdinal("is_dsd")) != 0,
            DsdRate               = NullableInt(r, "dsd_rate"),
            Title                 = NullableString(r, "title"),
            SortTitle             = NullableString(r, "sort_title"),
            Artist                = NullableString(r, "artist"),
            SortArtist            = NullableString(r, "sort_artist"),
            AlbumArtist           = NullableString(r, "album_artist"),
            SortAlbumArtist       = NullableString(r, "sort_album_artist"),
            Album                 = NullableString(r, "album"),
            SortAlbum             = NullableString(r, "sort_album"),
            Genre                 = NullableString(r, "genre"),
            Year                  = NullableInt(r, "year"),
            Date                  = NullableString(r, "date"),
            TrackNumber           = NullableInt(r, "track_number"),
            TrackTotal            = NullableInt(r, "track_total"),
            DiscNumber            = NullableInt(r, "disc_number"),
            DiscTotal             = NullableInt(r, "disc_total"),
            Composer              = NullableString(r, "composer"),
            Conductor             = NullableString(r, "conductor"),
            Lyricist              = NullableString(r, "lyricist"),
            Lyrics                = NullableString(r, "lyrics"),
            DownloadedLyrics      = NullableString(r, "downloaded_lyrics"),
            SyncedLyrics          = NullableString(r, "synced_lyrics"),
            LyricsSource          = NullableString(r, "lyrics_source"),
            LyricsFetchedAt       = NullableLong(r, "lyrics_fetched_at"),
            Comment               = NullableString(r, "comment"),
            Copyright             = NullableString(r, "copyright"),
            Publisher             = NullableString(r, "publisher"),
            EncodedBy             = NullableString(r, "encoded_by"),
            EncodingSettings      = NullableString(r, "encoding_settings"),
            Bpm                   = NullableInt(r, "bpm"),
            Compilation           = r.GetInt32(r.GetOrdinal("compilation")) != 0,
            Isrc                  = NullableString(r, "isrc"),
            Language              = NullableString(r, "language"),
            Mood                  = NullableString(r, "mood"),
            ReplayGainTrack       = NullableString(r, "replay_gain_track"),
            ReplayGainAlbum       = NullableString(r, "replay_gain_album"),
            MusicBrainzTrackId    = NullableString(r, "musicbrainz_track_id"),
            MusicBrainzReleaseId  = NullableString(r, "musicbrainz_release_id"),
            MusicBrainzArtistId   = NullableString(r, "musicbrainz_artist_id"),
            AcoustIdFingerprint   = NullableString(r, "acoustid_fingerprint"),
            HasCover              = r.GetInt32(r.GetOrdinal("has_cover")) != 0,
            CoverMimeType         = NullableString(r, "cover_mime_type"),
            CoverData             = r.IsDBNull(r.GetOrdinal("cover_data"))
                                        ? null
                                        : (byte[])r.GetValue(r.GetOrdinal("cover_data")),
        };
    }

    private static ArtistInfo MapArtistInfo(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetInt32(2) != 0,
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetInt64(7),
        reader.GetInt32(8) != 0);

    private static void Add(SqliteCommand cmd, string name, object? value)
        => cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string? NullableString(SqliteDataReader r, string col)
    {
        int ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }

    private static int? NullableInt(SqliteDataReader r, string col)
    {
        int ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetInt32(ord);
    }

    private static long? NullableLong(SqliteDataReader r, string col)
    {
        int ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetInt64(ord);
    }

    private static double? NullableDouble(SqliteDataReader r, string col)
    {
        int ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetDouble(ord);
    }

    private static PlaylistRecord MapPlaylist(SqliteDataReader r) => new()
    {
        Id              = r.GetInt64(r.GetOrdinal("id")),
        Name            = r.GetString(r.GetOrdinal("name")),
        Description     = NullableString(r, "description"),
        TrackCount      = r.GetInt32(r.GetOrdinal("track_count")),
        CreatedAt       = r.GetInt64(r.GetOrdinal("created_at")),
        ModifiedAt      = r.GetInt64(r.GetOrdinal("modified_at")),
        IsSmartPlaylist = r.GetInt32(r.GetOrdinal("is_smart")) != 0,
        FilterCriteria  = NullableString(r, "filter_criteria"),
    };

    private static PlaylistTrackRecord MapPlaylistTrack(SqliteDataReader r) => new()
    {
        Id         = r.GetInt64(r.GetOrdinal("id")),
        PlaylistId = r.GetInt64(r.GetOrdinal("playlist_id")),
        TrackId    = NullableLong(r, "track_id"),
        Path       = r.GetString(r.GetOrdinal("path")),
        Position   = r.GetInt32(r.GetOrdinal("position")),
        AddedAt    = r.GetInt64(r.GetOrdinal("added_at")),
    };

    // ------------------------------------------------------------------
    // Dashboard-Abfragen
    // ------------------------------------------------------------------

    /// <summary>Returns the four dashboard counters without materialising library rows.</summary>
    public DashboardLibrarySummary GetDashboardLibrarySummary()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT (SELECT COUNT(*) FROM albums),
                   (SELECT COUNT(*) FROM tracks),
                   (SELECT COUNT(*) FROM artists),
                   (SELECT COUNT(*) FROM tracks WHERE is_favorite != 0);
            """;
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new DashboardLibrarySummary(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3))
            : new DashboardLibrarySummary(0, 0, 0, 0);
    }

    /// <summary>Returns total listened seconds, optionally limited to history since a Unix timestamp.</summary>
    public double GetTotalListeningSeconds(long? sinceUnix = null, long? untilUnix = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(SUM(position_seconds), 0)
            FROM play_history
            WHERE position_seconds > 0
              AND ($since IS NULL OR started_at >= $since)
              AND ($until IS NULL OR started_at < $until);
            """;
        cmd.Parameters.AddWithValue("$since", (object?)sinceUnix ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$until", (object?)untilUnix ?? DBNull.Value);
        return Convert.ToDouble(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    /// <summary>Returns listened seconds split into equally sized chronological buckets.</summary>
    public IReadOnlyList<double> GetListeningTrend(long? sinceUnix = null, int bucketCount = 5)
    {
        bucketCount = Math.Clamp(bucketCount, 2, 24);
        var end = DateTimeOffset.Now.ToUnixTimeSeconds() + 1;
        long start;
        using (var startCmd = _conn.CreateCommand())
        {
            startCmd.CommandText = "SELECT COALESCE($since, MIN(started_at), $end) FROM play_history;";
            startCmd.Parameters.AddWithValue("$since", (object?)sinceUnix ?? DBNull.Value);
            startCmd.Parameters.AddWithValue("$end", end);
            start = Convert.ToInt64(startCmd.ExecuteScalar() ?? end, CultureInfo.InvariantCulture);
        }

        var width = Math.Max(1d, (end - start) / (double)bucketCount);
        var result = new double[bucketCount];
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT MIN($last, CAST((started_at - $start) / $width AS INTEGER)) AS bucket,
                   SUM(position_seconds)
            FROM play_history
            WHERE position_seconds > 0 AND started_at >= $start AND started_at < $end
            GROUP BY bucket;
            """;
        cmd.Parameters.AddWithValue("$last", bucketCount - 1);
        cmd.Parameters.AddWithValue("$start", start);
        cmd.Parameters.AddWithValue("$end", end);
        cmd.Parameters.AddWithValue("$width", width);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var index = reader.GetInt32(0);
            if (index >= 0 && index < result.Length)
                result[index] = reader.GetDouble(1);
        }
        return result;
    }

    public List<RecentAlbumInfo> GetRecentAlbums(int limit = 12)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT a.id,
                   COALESCE(a.title, '')  AS title,
                   COALESCE(ar.name, '')  AS artist,
                   art.thumb_96_path,
                   a.artist_id,
                   MAX(COALESCE(t.added_at, 0)) AS last_added,
                   COALESCE(a.is_favorite, 0) AS is_favorite,
                   COALESCE(art.thumb_320_path, art.original_path) AS artwork_path
            FROM albums a
            LEFT JOIN artists  ar  ON ar.id  = a.artist_id
            LEFT JOIN artworks art ON art.id  = a.artwork_id
            LEFT JOIN tracks   t   ON t.album_id = a.id
            GROUP BY a.id
            ORDER BY last_added DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        var result = new List<RecentAlbumInfo>();
        while (r.Read())
            result.Add(new RecentAlbumInfo(
                r.GetInt64(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetInt64(4),
                r.IsDBNull(5) ? 0 : r.GetInt64(5),
                !r.IsDBNull(6) && r.GetInt64(6) != 0,
                r.IsDBNull(7) ? null : r.GetString(7)));
        return result;
    }

    public List<CalendarDayData> GetCalendarData(int year, int month)
    {
        var ym = $"{year:D4}-{month:D2}";

        var dayTotals = new Dictionary<int, double>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT CAST(strftime('%d', started_at, 'unixepoch', 'localtime') AS INTEGER) AS day,
                       SUM(COALESCE(position_seconds, 0)) AS secs
                FROM play_history
                WHERE strftime('%Y-%m', started_at, 'unixepoch', 'localtime') = $ym
                  AND position_seconds > 0
                GROUP BY day;
                """;
            cmd.Parameters.AddWithValue("$ym", ym);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                dayTotals[r.GetInt32(0)] = r.GetDouble(1);
        }

        var dayGenres = new Dictionary<int, Dictionary<string, double>>();
        using (var cmd = _conn.CreateCommand())
        {
            // COALESCE so remote/Plex tracks (genre stored in play_history.genre)
            // contribute to the per-day genre breakdown like local tracks.
            cmd.CommandText = """
                SELECT CAST(strftime('%d', ph.started_at, 'unixepoch', 'localtime') AS INTEGER) AS day,
                       SUM(COALESCE(ph.position_seconds, 0)) AS secs,
                       COALESCE(t.genre, ph.genre) AS genre
                FROM play_history ph
                LEFT JOIN tracks t ON t.id = ph.track_id
                WHERE strftime('%Y-%m', ph.started_at, 'unixepoch', 'localtime') = $ym
                  AND ph.position_seconds > 0
                  AND COALESCE(t.genre, ph.genre) IS NOT NULL
                  AND COALESCE(t.genre, ph.genre) != ''
                GROUP BY day, COALESCE(t.genre, ph.genre);
                """;
            cmd.Parameters.AddWithValue("$ym", ym);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int day = r.GetInt32(0);
                double secs = r.GetDouble(1);
                if (!dayGenres.TryGetValue(day, out var gd))
                    dayGenres[day] = gd = new(StringComparer.OrdinalIgnoreCase);
                foreach (var g in r.GetString(2).Split(';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    gd[g] = gd.GetValueOrDefault(g) + secs;
            }
        }

        return dayTotals
            .Select(kvp =>
            {
                IReadOnlyList<string> top = dayGenres.TryGetValue(kvp.Key, out var gd)
                    ? gd.OrderByDescending(x => x.Value).Take(3).Select(x => x.Key).ToList()
                    : Array.Empty<string>();
                return new CalendarDayData(kvp.Key, kvp.Value, top);
            })
            .OrderBy(x => x.Day)
            .ToList();
    }

    public List<DailyHistoryEntry> GetHistoryForDay(DateTime date)
    {
        var localDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        var nextLocalDate = localDate.AddDays(1);
        var start = new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate))
            .ToUnixTimeSeconds();
        var end = new DateTimeOffset(nextLocalDate, TimeZoneInfo.Local.GetUtcOffset(nextLocalDate))
            .ToUnixTimeSeconds();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ph.id,
                   ph.track_id,
                   ph.path,
                   ph.started_at,
                   COALESCE(ph.position_seconds, 0),
                   ph.duration_seconds,
                   ph.media_type,
                   COALESCE(t.title, ph.title, t.file_name, ph.path),
                   COALESCE(ar.name, t.artist, ph.subtitle),
                   COALESCE(a.title, t.album, ph.album),
                   t.artist_id,
                   t.album_id,
                   ph.external_id
            FROM play_history ph
            LEFT JOIN tracks t ON t.id = ph.track_id
            LEFT JOIN artists ar ON ar.id = t.artist_id
            LEFT JOIN albums a ON a.id = t.album_id
            WHERE ph.started_at >= $start
              AND ph.started_at < $end
              AND COALESCE(ph.position_seconds, 0) > 0
            ORDER BY ph.started_at DESC, ph.id DESC;
            """;
        cmd.Parameters.AddWithValue("$start", start);
        cmd.Parameters.AddWithValue("$end", end);

        using var r = cmd.ExecuteReader();
        var result = new List<DailyHistoryEntry>();
        while (r.Read())
        {
            result.Add(new DailyHistoryEntry(
                r.GetInt64(0),
                r.IsDBNull(1) ? null : r.GetInt64(1),
                r.GetString(2),
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(3)).LocalDateTime,
                r.GetDouble(4),
                r.IsDBNull(5) ? null : r.GetDouble(5),
                r.GetString(6),
                r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetInt64(10),
                r.IsDBNull(11) ? null : r.GetInt64(11),
                r.IsDBNull(12) ? null : r.GetString(12)));
        }
        return result;
    }

    /// <summary>Returns the most recent play-history entries across all media types.</summary>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <returns>History entries ordered by start time descending.</returns>
    public List<DailyHistoryEntry> GetRecentHistory(int limit = 20)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ph.id,
                   ph.track_id,
                   ph.path,
                   ph.started_at,
                   COALESCE(ph.position_seconds, 0),
                   ph.duration_seconds,
                   ph.media_type,
                   COALESCE(t.title, ph.title, t.file_name, ph.path),
                   COALESCE(ar.name, t.artist, ph.subtitle),
                   COALESCE(a.title, t.album, ph.album),
                   t.artist_id,
                   t.album_id,
                   ph.external_id
            FROM play_history ph
            LEFT JOIN tracks t ON t.id = ph.track_id
            LEFT JOIN artists ar ON ar.id = t.artist_id
            LEFT JOIN albums a ON a.id = t.album_id
            WHERE COALESCE(ph.position_seconds, 0) > 0
            ORDER BY ph.started_at DESC, ph.id DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        var result = new List<DailyHistoryEntry>();
        while (r.Read())
        {
            result.Add(new DailyHistoryEntry(
                r.GetInt64(0),
                r.IsDBNull(1) ? null : r.GetInt64(1),
                r.GetString(2),
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(3)).LocalDateTime,
                r.GetDouble(4),
                r.IsDBNull(5) ? null : r.GetDouble(5),
                r.GetString(6),
                r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetInt64(10),
                r.IsDBNull(11) ? null : r.GetInt64(11),
                r.IsDBNull(12) ? null : r.GetString(12)));
        }
        return result;
    }

    /// <summary>Aggregates listening time per genre across all playback sources.</summary>
    /// <param name="limit">Maximum number of genres to return.</param>
    /// <param name="sinceUnix">Optional inclusive lower bound on the playback start (Unix seconds); <see langword="null"/> means all time.</param>
    /// <returns>Genres ordered by total play time descending.</returns>
    public List<(string Genre, double Seconds)> GetTopGenres(int limit = 10, long? sinceUnix = null)
    {
        var agg = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _conn.CreateCommand();
        // COALESCE so that remote/Plex tracks (no local row, genre captured at
        // playback time in play_history.genre) are included alongside local tracks.
        cmd.CommandText = $"""
            SELECT COALESCE(t.genre, ph.genre) AS genre,
                   SUM(COALESCE(ph.position_seconds, 0)) AS secs
            FROM play_history ph
            LEFT JOIN tracks t ON t.id = ph.track_id
            WHERE ph.position_seconds > 0
              AND COALESCE(t.genre, ph.genre) IS NOT NULL
              AND COALESCE(t.genre, ph.genre) != ''
              {(sinceUnix.HasValue ? "AND ph.started_at >= $since" : string.Empty)}
            GROUP BY COALESCE(t.genre, ph.genre)
            ORDER BY secs DESC;
            """;
        if (sinceUnix.HasValue)
            cmd.Parameters.AddWithValue("$since", sinceUnix.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            double secs = r.GetDouble(1);
            foreach (var g in r.GetString(0).Split(';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                agg[g] = agg.GetValueOrDefault(g) + secs;
        }
        return agg.OrderByDescending(x => x.Value).Take(limit)
                  .Select(x => (x.Key, x.Value)).ToList();
    }

    /// <summary>
    /// Aggregates listening time per album across local, remote Orynivo Server, and Plex
    /// playback, merging entries by album title plus artist so the same album from any
    /// source is counted once. Local matches carry their album ID and artwork thumbnail
    /// for in-library navigation; remote/Plex matches carry a representative external ID
    /// and stream path so they can be resolved when clicked.
    /// </summary>
    /// <param name="limit">Maximum number of albums to return.</param>
    /// <param name="sinceUnix">Optional inclusive lower bound on the playback start (Unix seconds); <see langword="null"/> means all time.</param>
    /// <returns>Albums ordered by total play time descending.</returns>
    public List<TopAlbumStat> GetTopAlbums(int limit = 10, long? sinceUnix = null)
    {
        var agg = new Dictionary<string, TopAlbumAccumulator>(StringComparer.Ordinal);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT COALESCE(ph.position_seconds, 0) AS secs,
                   COALESCE(a.title, t.album, ph.album) AS album_title,
                   COALESCE(ar.name, t.artist, ph.subtitle) AS artist_name,
                   t.album_id AS local_album_id,
                   a.artist_id AS local_artist_id,
                   ph.external_id AS ext,
                   ph.path AS path,
                   COALESCE(art.thumb_320_path, art.thumb_96_path, art.original_path) AS thumb
            FROM play_history ph
            LEFT JOIN tracks   t   ON t.id = ph.track_id
            LEFT JOIN albums   a   ON a.id = t.album_id
            LEFT JOIN artists  ar  ON ar.id = a.artist_id
            LEFT JOIN artworks art ON art.id = a.artwork_id
            WHERE ph.media_type = 'track'
              AND COALESCE(ph.position_seconds, 0) > 0
              AND TRIM(COALESCE(a.title, t.album, ph.album, '')) != ''
              {(sinceUnix.HasValue ? "AND ph.started_at >= $since" : string.Empty)};
            """;
        if (sinceUnix.HasValue)
            cmd.Parameters.AddWithValue("$since", sinceUnix.Value);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            double secs = r.GetDouble(0);
            string title = r.GetString(1);
            string artist = r.IsDBNull(2) ? string.Empty : r.GetString(2);
            long? albumId = r.IsDBNull(3) ? null : r.GetInt64(3);
            long? artistId = r.IsDBNull(4) ? null : r.GetInt64(4);
            string? ext = r.IsDBNull(5) ? null : r.GetString(5);
            string? path = r.IsDBNull(6) ? null : r.GetString(6);
            string? thumb = r.IsDBNull(7) ? null : r.GetString(7);

            var key = title.ToLowerInvariant() + "" + artist.ToLowerInvariant();
            if (!agg.TryGetValue(key, out var acc))
                agg[key] = acc = new TopAlbumAccumulator { Title = title, Artist = artist };
            acc.Seconds += secs;
            // Prefer a local match (album ID + thumbnail) as the representative identity;
            // otherwise keep the first remote/Plex external ID and path for resolution.
            if (albumId.HasValue && !acc.LocalAlbumId.HasValue)
            {
                acc.LocalAlbumId = albumId;
                acc.LocalArtistId = artistId;
                acc.ThumbPath = thumb;
            }
            acc.ExternalId ??= ext;
            acc.Path ??= path;
        }

        return agg.Values
            .OrderByDescending(a => a.Seconds)
            .Take(limit)
            .Select(a => new TopAlbumStat(
                a.Title, a.Artist, a.Seconds,
                a.LocalAlbumId, a.LocalArtistId, a.ThumbPath, a.ExternalId, a.Path))
            .ToList();
    }

    /// <summary>
    /// Aggregates listening time per artist across local, remote Orynivo Server, and Plex
    /// playback, merging entries by display name. Local matches carry their artist ID for
    /// in-library navigation; remote/Plex matches carry a representative external ID and
    /// stream path so they can be resolved when clicked.
    /// </summary>
    /// <param name="limit">Maximum number of artists to return.</param>
    /// <param name="sinceUnix">Optional inclusive lower bound on the playback start (Unix seconds); <see langword="null"/> means all time.</param>
    /// <returns>Artists ordered by total play time descending.</returns>
    public List<TopArtistStat> GetTopArtists(int limit = 10, long? sinceUnix = null)
    {
        var agg = new Dictionary<string, TopArtistAccumulator>(StringComparer.Ordinal);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT COALESCE(ph.position_seconds, 0) AS secs,
                   COALESCE(ar.name, t.artist, ph.subtitle) AS artist_name,
                   t.artist_id AS local_artist_id,
                   ph.external_id AS ext,
                   ph.path AS path
            FROM play_history ph
            LEFT JOIN tracks  t  ON t.id = ph.track_id
            LEFT JOIN artists ar ON ar.id = t.artist_id
            WHERE ph.media_type = 'track'
              AND COALESCE(ph.position_seconds, 0) > 0
              AND TRIM(COALESCE(ar.name, t.artist, ph.subtitle, '')) != ''
              {(sinceUnix.HasValue ? "AND ph.started_at >= $since" : string.Empty)};
            """;
        if (sinceUnix.HasValue)
            cmd.Parameters.AddWithValue("$since", sinceUnix.Value);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            double secs = r.GetDouble(0);
            string name = r.GetString(1);
            long? artistId = r.IsDBNull(2) ? null : r.GetInt64(2);
            string? ext = r.IsDBNull(3) ? null : r.GetString(3);
            string? path = r.IsDBNull(4) ? null : r.GetString(4);

            var key = name.ToLowerInvariant();
            if (!agg.TryGetValue(key, out var acc))
                agg[key] = acc = new TopArtistAccumulator { Name = name };
            acc.Seconds += secs;
            if (artistId.HasValue && !acc.LocalArtistId.HasValue)
                acc.LocalArtistId = artistId;
            acc.ExternalId ??= ext;
            acc.Path ??= path;
        }

        return agg.Values
            .OrderByDescending(a => a.Seconds)
            .Take(limit)
            .Select(a => new TopArtistStat(a.Name, a.Seconds, a.LocalArtistId, a.ExternalId, a.Path))
            .ToList();
    }

    /// <summary>Mutable accumulator used while aggregating <see cref="GetTopAlbums"/>.</summary>
    private sealed class TopAlbumAccumulator
    {
        /// <summary>Gets or sets the album display title.</summary>
        public string Title { get; set; } = string.Empty;
        /// <summary>Gets or sets the album artist display name.</summary>
        public string Artist { get; set; } = string.Empty;
        /// <summary>Gets or sets the accumulated listened seconds.</summary>
        public double Seconds { get; set; }
        /// <summary>Gets or sets the local album identifier when matched.</summary>
        public long? LocalAlbumId { get; set; }
        /// <summary>Gets or sets the local album-artist identifier when known.</summary>
        public long? LocalArtistId { get; set; }
        /// <summary>Gets or sets the local artwork thumbnail path when available.</summary>
        public string? ThumbPath { get; set; }
        /// <summary>Gets or sets the representative external identifier for remote/Plex resolution.</summary>
        public string? ExternalId { get; set; }
        /// <summary>Gets or sets the representative playback path for remote/Plex resolution.</summary>
        public string? Path { get; set; }
    }

    /// <summary>Mutable accumulator used while aggregating <see cref="GetTopArtists"/>.</summary>
    private sealed class TopArtistAccumulator
    {
        /// <summary>Gets or sets the artist display name.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Gets or sets the accumulated listened seconds.</summary>
        public double Seconds { get; set; }
        /// <summary>Gets or sets the local artist identifier when matched.</summary>
        public long? LocalArtistId { get; set; }
        /// <summary>Gets or sets the representative external identifier for remote/Plex resolution.</summary>
        public string? ExternalId { get; set; }
        /// <summary>Gets or sets the representative playback path for remote/Plex resolution.</summary>
        public string? Path { get; set; }
    }

    public void Dispose() => _conn.Dispose();
}
