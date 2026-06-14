using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Orynivo.Library;

public sealed record ArtistInfo(
    long Id,
    string Artist,
    bool IsFavorite,
    string? Biography,
    string? ImagePath,
    string? SourceUrl,
    string? ProfileLanguage,
    long? ProfileFetchedAt);

/// <summary>Distinct-Album-Eintrag für die Albumansichten.</summary>
public sealed record AlbumInfo(
    long Id,
    string Album,
    string? DisplayArtist,
    int? Year,
    string? ArtworkPath,
    string? ThumbnailPath,
    bool IsFavorite);

/// <summary>Schlanker Track-Datensatz für die Haupt-Trackliste.</summary>
public sealed record TrackListInfo(
    string Path,
    string FileName,
    string? Title,
    string? Artist,
    string? Album,
    string? Genre,
    string? Format,
    int? Bitrate,
    double? Duration,
    string? SortTitle,
    long Id,
    bool IsFavorite);

public sealed record TrackFacetInfo(
    long Id,
    bool IsFavorite,
    string? Genre,
    string? Format,
    int? Bitrate);

public sealed record ArtworkPaths(string? OriginalPath, string? Thumb96Path, string? Thumb320Path);

/// <summary>Schlanker Track-Datensatz für listenbasierte Ansichten (kein Cover, keine Texte).</summary>
public sealed record TrackLite(
    string  Path,
    string  FileName,
    string? Title,
    int?    DiscNumber,
    int?    TrackNumber)
{
    public string DisplayName => Title ?? FileName;
}

public sealed record RecentAlbumInfo(long Id, string Title, string Artist, string? ThumbPath);

public sealed record CalendarDayData(int Day, double TotalSeconds, IReadOnlyList<string> TopGenres);
public sealed record ArtistNormalizationResult(int MergedArtists, int UpdatedTracks);

/// <summary>
/// Verwaltet die SQLite-Audiodatenbank. Eine Instanz pro Anwendungslaufzeit.
/// Die DB-Datei liegt unter %LOCALAPPDATA%\Orynivo\library.db.
/// </summary>
public sealed class AudioDatabase : IDisposable
{
    private readonly SqliteConnection _conn;
    private Dictionary<string, (long Id, string Name)>? _artistsByComparisonKey;

    public AudioDatabase(string dbPath)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath)!);
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        ApplyPragmas();
        EnsureSchema();
    }

    // ------------------------------------------------------------------
    // Öffentliche Factory für den Standard-Speicherort
    // ------------------------------------------------------------------

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

    public void Upsert(TrackRecord track)
    {
        track.Artist = ArtistNameNormalizer.NormalizeDisplayName(track.Artist);
        track.AlbumArtist = ArtistNameNormalizer.NormalizeDisplayName(track.AlbumArtist ?? track.Artist);
        using var tx = _conn.BeginTransaction();
        try
        {
            var artistId  = EnsureArtist(track.Artist, tx);
            var artworkId = EnsureArtwork(track.CoverData, track.CoverMimeType, tx);
            var albumId   = EnsureAlbum(track.Album, track.AlbumArtist ?? track.Artist, track.Year, artistId, artworkId, tx);

            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
            INSERT INTO tracks (
                path, file_name, file_size, modified_at, added_at,
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
                $path, $file_name, $file_size, $modified_at, $added_at,
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
            _artistsByComparisonKey = null;
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

    public IEnumerable<TrackRecord> GetAll()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM tracks ORDER BY album_artist, album, disc_number, track_number;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            yield return MapRow(reader);
    }

    /// <summary>Nur distinct artist – ohne Trackzeilen, BLOBs oder weitere Metadaten.</summary>
    public List<ArtistInfo> GetArtistsLite()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, is_favorite, NULL AS biography, image_path,
                   profile_source_url, profile_language, profile_fetched_at
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
                reader.IsDBNull(7) ? null : reader.GetInt64(7)));
        return result;
    }

    public ArtistInfo? GetArtistById(long artistId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, is_favorite, biography, image_path,
                   profile_source_url, profile_language, profile_fetched_at
            FROM artists
            WHERE id = $id
            LIMIT 1;
            """;
        Add(cmd, "$id", artistId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapArtistInfo(reader) : null;
    }

    public ArtistInfo? GetArtistByTrackPath(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ar.id, ar.name, ar.is_favorite, ar.biography, ar.image_path,
                   ar.profile_source_url, ar.profile_language, ar.profile_fetched_at
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
                image_path = COALESCE($image_path, image_path),
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

    /// <summary>
    /// Nur die für Albumlisten benötigten Felder; pro Album wird genau ein repräsentatives Cover geladen.
    /// </summary>
    public List<AlbumInfo> GetAlbumsLite(bool includeArtwork = false)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = includeArtwork ? """
            SELECT
                MIN(al.id) AS id,
                al.title,
                ar.name,
                CASE WHEN al.year = 0 THEN NULL ELSE al.year END AS year,
                aw.thumb_320_path,
                aw.thumb_96_path,
                al.is_favorite
            FROM albums al
            LEFT JOIN artists ar ON ar.id = al.artist_id
            LEFT JOIN artworks aw ON aw.id = (
                SELECT al2.artwork_id
                FROM albums al2
                WHERE al2.title = al.title
                  AND al2.artwork_id IS NOT NULL
                ORDER BY al2.id
                LIMIT 1
            )
            GROUP BY al.title
            ORDER BY
                CASE WHEN al.title = '' THEN 1 ELSE 0 END,
                al.title COLLATE NOCASE,
                ar.name COLLATE NOCASE;
            """ : """
            SELECT
                MIN(al.id) AS id,
                al.title,
                ar.name,
                CASE WHEN al.year = 0 THEN NULL ELSE al.year END AS year,
                aw.thumb_320_path,
                aw.thumb_96_path,
                al.is_favorite
            FROM albums al
            LEFT JOIN artists ar ON ar.id = al.artist_id
            LEFT JOIN artworks aw ON aw.id = al.artwork_id
            GROUP BY al.title
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
                reader.GetInt32(6) != 0));
        return result;
    }

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
                al.is_favorite
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
                reader.GetInt32(6) != 0)
            : null;
    }

    public void RebuildAlbumsFromAlbumArtists()
    {
        using var tx = _conn.BeginTransaction();
        var existingArtworkByAlbumKey = LoadExistingAlbumArtworkMap(tx);
        ExecuteInTransaction(tx, "UPDATE tracks SET album_id = NULL;");
        ExecuteInTransaction(tx, "DELETE FROM albums;");

        using var select = _conn.CreateCommand();
        select.Transaction = tx;
        select.CommandText = """
            SELECT id, album_artist, album, year, artist_id
            FROM tracks;
            """;
        using var reader = select.ExecuteReader();
        var rows = new List<(long Id, string? AlbumArtist, string? Album, int? Year, long? ArtistId)>();
        while (reader.Read())
            rows.Add((
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4)));
        reader.Close();

        foreach (var row in rows)
        {
            var key = MakeAlbumKey(row.Album);
            existingArtworkByAlbumKey.TryGetValue(key, out var artworkId);
            var albumId = EnsureAlbum(row.Album, row.AlbumArtist, row.Year, row.ArtistId, artworkId, tx);
            using var update = _conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE tracks SET album_id = $album_id WHERE id = $id;";
            Add(update, "$album_id", albumId);
            Add(update, "$id", row.Id);
            update.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public List<(long AlbumId, string Path)> GetAlbumsMissingArtworkSamplePaths()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT al.id, MIN(t.path) AS sample_path
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

    public void AttachArtworkToAlbum(long albumId, byte[] data, string? mimeType)
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
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public void ClearArtworkFromAlbum(long albumId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE albums SET artwork_id = NULL WHERE id = $album_id;";
        Add(cmd, "$album_id", albumId);
        cmd.ExecuteNonQuery();
    }

    public List<AlbumInfo> GetAlbumsByArtist(long artistId, bool includeArtwork = false)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = includeArtwork ? """
            SELECT
                MIN(al.id) AS id,
                al.title,
                ar.name,
                CASE WHEN al.year = 0 THEN NULL ELSE al.year END AS year,
                aw.thumb_320_path,
                aw.thumb_96_path,
                al.is_favorite
            FROM albums al
            LEFT JOIN artists ar ON ar.id = al.artist_id
            LEFT JOIN artworks aw ON aw.id = (
                SELECT al2.artwork_id
                FROM albums al2
                WHERE al2.title = al.title
                  AND al2.artwork_id IS NOT NULL
                ORDER BY al2.id
                LIMIT 1
            )
            WHERE al.artist_id = $artist_id
               OR al.id IN (
                    SELECT DISTINCT album_id
                    FROM tracks
                    WHERE artist_id = $artist_id
                      AND album_id IS NOT NULL
               )
            GROUP BY al.title
            ORDER BY
                CASE WHEN al.title = '' THEN 1 ELSE 0 END,
                al.title COLLATE NOCASE;
            """ : """
            SELECT
                MIN(al.id) AS id,
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
            GROUP BY al.title
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
                reader.GetInt32(6) != 0));
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

    public long RecordPlaybackStart(string path, long? trackId, double? durationSeconds)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO play_history (track_id, path, started_at, duration_seconds)
            VALUES ($track_id, $path, $started_at, $duration_seconds)
            RETURNING id;
            """;
        Add(cmd, "$track_id", trackId);
        Add(cmd, "$path", path);
        Add(cmd, "$started_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Add(cmd, "$duration_seconds", durationSeconds);
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
                    "UPDATE tracks SET artist_id = $survivor WHERE artist_id = $duplicate;",
                    ("$survivor", survivor.Id), ("$duplicate", duplicate.Id));
                ExecuteInTransaction(tx,
                    "UPDATE albums SET artist_id = $survivor WHERE artist_id = $duplicate;",
                    ("$survivor", survivor.Id), ("$duplicate", duplicate.Id));
                ExecuteInTransaction(tx, """
                    UPDATE artists
                    SET is_favorite = MAX(is_favorite, (SELECT is_favorite FROM artists WHERE id = $duplicate)),
                        biography = COALESCE(biography, (SELECT biography FROM artists WHERE id = $duplicate)),
                        image_path = COALESCE(image_path, (SELECT image_path FROM artists WHERE id = $duplicate)),
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
        _artistsByComparisonKey = null;
        return new ArtistNormalizationResult(mergedArtists, updatedTracks);
    }

    /// <summary>Nur die Spalten, die die Trackliste tatsächlich rendert – keine BLOBs, keine Texte.</summary>
    public List<TrackListInfo> GetTrackList()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                path, file_name, title, artist, album, genre, format, bitrate, duration, sort_title, id, is_favorite
            FROM tracks
            ORDER BY COALESCE(sort_title, title, file_name) COLLATE NOCASE;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<TrackListInfo>();
        while (reader.Read())
            result.Add(new TrackListInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetInt64(10),
                reader.GetInt32(11) != 0));
        return result;
    }

    public List<TrackListInfo> GetTrackListByAlbum(long albumId, long? artistId = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                path, file_name, title, artist, album, genre, format, bitrate, duration, sort_title, id, is_favorite
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
            result.Add(new TrackListInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetInt64(10),
                reader.GetInt32(11) != 0));
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
                    path, file_name, title, artist, album, genre, format, bitrate, duration, sort_title, id, is_favorite
                FROM tracks
                WHERE id IN ({string.Join(", ", parameters)});
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new TrackListInfo(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetInt64(10),
                    reader.GetInt32(11) != 0));
        }

        var order = idList.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return result.OrderBy(t => order[t.Id]).ToList();
    }

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

    public List<TrackListInfo> GetTrackListFiltered(IEnumerable<long> ids)
        => GetTrackListByIds(ids);

    public List<AlbumInfo> GetAlbumsByTrackIds(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return [];
        using var cmd = _conn.CreateCommand();
        var parameters = idList.Select((id, i) => { var name = $"$id{i}"; Add(cmd, name, id); return name; }).ToList();
        cmd.CommandText = $"""
            SELECT MIN(al.id), al.title, ar.name,
                   CASE WHEN al.year = 0 THEN NULL ELSE al.year END,
                   aw.thumb_320_path, aw.thumb_96_path, al.is_favorite
            FROM tracks t
            JOIN albums al ON al.id = t.album_id
            LEFT JOIN artists ar ON ar.id = al.artist_id
            LEFT JOIN artworks aw ON aw.id = al.artwork_id
            WHERE t.id IN ({string.Join(", ", parameters)})
            GROUP BY al.title
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
                            ar.profile_source_url, ar.profile_language, ar.profile_fetched_at
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

    public Dictionary<long, long> GetAlbumIdsByTrackIds(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return [];

        using var cmd = _conn.CreateCommand();
        var parameters = idList.Select((id, i) => { var name = $"$id{i}"; Add(cmd, name, id); return name; }).ToList();
        cmd.CommandText = $"""
            SELECT t.id, (
                SELECT MIN(al2.id)
                FROM albums al2
                WHERE al2.title = al.title
            )
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
            "SELECT path, file_name, title, disc_number, track_number FROM tracks ORDER BY path;";
        using var reader = cmd.ExecuteReader();
        var result = new List<TrackLite>();
        while (reader.Read())
            result.Add(new TrackLite(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : (int?)reader.GetInt64(3),
                reader.IsDBNull(4) ? null : (int?)reader.GetInt64(4)));
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
            "SELECT path, file_name, title, disc_number, track_number " +
            "FROM tracks WHERE path LIKE $prefix || '%' " +
            "ORDER BY disc_number, track_number, file_name;";
        cmd.Parameters.AddWithValue("$prefix", prefix);
        using var reader = cmd.ExecuteReader();
        var result = new List<TrackLite>();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            if (!string.Equals(System.IO.Path.GetDirectoryName(path), dirPath,
                               StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(new TrackLite(
                path,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : (int?)reader.GetInt64(3),
                reader.IsDBNull(4) ? null : (int?)reader.GetInt64(4)));
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
        cmd.CommandText = "SELECT path FROM tracks WHERE path LIKE $prefix || '%' ORDER BY path;";
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
        cmd.CommandText = "SELECT path, modified_at FROM tracks;";
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetInt64(1);
        return result;
    }

    public int CountByDirectory(string rootPath)
    {
        var prefix = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM tracks WHERE path LIKE $prefix || '%';";
        cmd.Parameters.AddWithValue("$prefix", prefix);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Delete(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tracks WHERE path = $path;";
        cmd.Parameters.AddWithValue("$path", path);
        cmd.ExecuteNonQuery();
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

        EnsureColumn("tracks", "artist_id", "INTEGER REFERENCES artists(id)");
        EnsureColumn("tracks", "album_id", "INTEGER REFERENCES albums(id)");
        EnsureColumn("tracks", "is_favorite", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("tracks", "downloaded_lyrics", "TEXT");
        EnsureColumn("tracks", "synced_lyrics", "TEXT");
        EnsureColumn("tracks", "lyrics_source", "TEXT");
        EnsureColumn("tracks", "lyrics_fetched_at", "INTEGER");

        Execute("""
            CREATE TABLE IF NOT EXISTS artists (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT NOT NULL UNIQUE COLLATE NOCASE,
                is_favorite INTEGER NOT NULL DEFAULT 0,
                biography TEXT,
                image_path TEXT,
                profile_source_url TEXT,
                profile_language TEXT,
                profile_fetched_at INTEGER
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
                artist_id   INTEGER REFERENCES artists(id),
                year        INTEGER,
                artwork_id  INTEGER REFERENCES artworks(id),
                is_favorite INTEGER NOT NULL DEFAULT 0,
                UNIQUE(title, artist_id, year)
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
                completed        INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_albums_artist        ON albums (artist_id);
            CREATE INDEX IF NOT EXISTS idx_tracks_artist_id     ON tracks (artist_id);
            CREATE INDEX IF NOT EXISTS idx_tracks_album_id      ON tracks (album_id);
            CREATE INDEX IF NOT EXISTS idx_play_history_track   ON play_history (track_id, started_at DESC);
            CREATE INDEX IF NOT EXISTS idx_play_history_started ON play_history (started_at DESC);

            CREATE TABLE IF NOT EXISTS app_meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);
        MigrateLegacyCachePaths();
        EnsureColumn("artists", "is_favorite", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("artists", "biography", "TEXT");
        EnsureColumn("artists", "image_path", "TEXT");
        EnsureColumn("artists", "profile_source_url", "TEXT");
        EnsureColumn("artists", "profile_language", "TEXT");
        EnsureColumn("artists", "profile_fetched_at", "INTEGER");
        EnsureColumn("albums", "is_favorite", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("artworks", "original_path", "TEXT");
        EnsureColumn("artworks", "thumb_96_path", "TEXT");
        EnsureColumn("artworks", "thumb_320_path", "TEXT");

        if (!string.Equals(GetMeta("normalized_library_v1"), "done", StringComparison.Ordinal))
        {
            MigrateNormalizedLibrary();
            SetMeta("normalized_library_v1", "done");
        }
        if (!string.Equals(GetMeta("album_artist_rebuild_v1"), "done", StringComparison.Ordinal))
        {
            RebuildAlbumsFromAlbumArtists();
            SetMeta("album_artist_rebuild_v1", "done");
        }
        if (!string.Equals(GetMeta("album_title_uniqueness_v1"), "done", StringComparison.Ordinal))
        {
            RebuildAlbumsFromAlbumArtists();
            SetMeta("album_title_uniqueness_v1", "done");
        }
        if (!string.Equals(GetMeta("artwork_files_v1"), "done", StringComparison.Ordinal))
        {
            MigrateArtworkFiles();
            SetMeta("artwork_files_v1", "done");
        }

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
            """);
        EnsureColumn("playlists", "is_smart",        "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("playlists", "filter_criteria",  "TEXT");
    }

    // ------------------------------------------------------------------
    // Playlisten – CRUD
    // ------------------------------------------------------------------

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

    private void ExecuteInTransaction(
        SqliteTransaction tx,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var parameter in parameters)
            Add(cmd, parameter.Name, parameter.Value);
        cmd.ExecuteNonQuery();
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

    private Dictionary<string, long?> LoadExistingAlbumArtworkMap(SqliteTransaction tx)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT al.title, ar.name, al.year, al.artwork_id
            FROM albums al
            LEFT JOIN artists ar ON ar.id = al.artist_id
            WHERE al.artwork_id IS NOT NULL;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var key = MakeAlbumKey(reader.IsDBNull(0) ? null : reader.GetString(0));
            result[key] = reader.IsDBNull(3) ? null : reader.GetInt64(3);
        }
        return result;
    }

    private static string MakeAlbumKey(string? title) =>
        title?.Trim() ?? string.Empty;

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

    private void MigrateNormalizedLibrary()
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, artist, album_artist, album, year, cover_data, cover_mime_type
            FROM tracks
            WHERE artist_id IS NULL OR album_id IS NULL OR cover_data IS NOT NULL;
            """;
        using var reader = cmd.ExecuteReader();
        var rows = new List<(long Id, string? Artist, string? AlbumArtist, string? Album, int? Year, byte[]? Cover, string? Mime)>();
        while (reader.Read())
            rows.Add((
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : (byte[])reader.GetValue(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        reader.Close();

        foreach (var row in rows)
        {
            var artistId  = EnsureArtist(row.Artist, tx);
            var artworkId = EnsureArtwork(row.Cover, row.Mime, tx);
            var albumId   = EnsureAlbum(row.Album, row.AlbumArtist ?? row.Artist, row.Year, artistId, artworkId, tx);
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
        return id;
    }

    private void EnsureArtistComparisonCache(SqliteTransaction tx)
    {
        if (_artistsByComparisonKey is not null)
            return;

        _artistsByComparisonKey = new Dictionary<string, (long Id, string Name)>(StringComparer.Ordinal);
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, name FROM artists ORDER BY id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var name = reader.GetString(1);
            _artistsByComparisonKey.TryAdd(
                ArtistNameNormalizer.CreateComparisonKey(name),
                (id, name));
        }
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

    private long? EnsureAlbum(string? title, string? displayArtist, int? year, long? fallbackArtistId, long? artworkId, SqliteTransaction tx)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "" : title.Trim();
        var normalizedYear  = year ?? 0;
        var albumArtistId = EnsureArtist(GetFirstArtist(displayArtist), tx);
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE albums
            SET artwork_id = COALESCE(artwork_id, $artwork_id)
            WHERE id = (
                SELECT id
                FROM albums
                WHERE title = $title
                ORDER BY id
                LIMIT 1
            );

            INSERT INTO albums (title, artist_id, year, artwork_id)
            SELECT $title, $artist_id, $year, $artwork_id
            WHERE NOT EXISTS (
                SELECT 1
                FROM albums
                WHERE title = $title
            );

            SELECT id FROM albums
            WHERE title = $title
            ORDER BY id
            LIMIT 1;
            """;
        Add(cmd, "$title", normalizedTitle);
        Add(cmd, "$artist_id", albumArtistId);
        Add(cmd, "$year", normalizedYear);
        Add(cmd, "$artwork_id", artworkId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

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
        reader.IsDBNull(7) ? null : reader.GetInt64(7));

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

    public List<RecentAlbumInfo> GetRecentAlbums(int limit = 12)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT a.id,
                   COALESCE(a.title, '')  AS title,
                   COALESCE(ar.name, '')  AS artist,
                   art.thumb_96_path,
                   MAX(COALESCE(t.added_at, 0)) AS last_added
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
                r.IsDBNull(3) ? null : r.GetString(3)));
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
            cmd.CommandText = """
                SELECT CAST(strftime('%d', ph.started_at, 'unixepoch', 'localtime') AS INTEGER) AS day,
                       SUM(COALESCE(ph.position_seconds, 0)) AS secs,
                       t.genre
                FROM play_history ph
                JOIN tracks t ON t.id = ph.track_id
                WHERE strftime('%Y-%m', ph.started_at, 'unixepoch', 'localtime') = $ym
                  AND ph.position_seconds > 0
                  AND t.genre IS NOT NULL AND t.genre != ''
                GROUP BY day, t.genre;
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

    public List<(string Genre, double Seconds)> GetTopGenres(int limit = 10)
    {
        var agg = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.genre, SUM(COALESCE(ph.position_seconds, 0)) AS secs
            FROM play_history ph
            JOIN tracks t ON t.id = ph.track_id
            WHERE ph.position_seconds > 0
              AND t.genre IS NOT NULL AND t.genre != ''
            GROUP BY t.genre
            ORDER BY secs DESC;
            """;
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

    public void Dispose() => _conn.Dispose();
}
