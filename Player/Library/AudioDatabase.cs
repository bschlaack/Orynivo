using System.IO;
using Microsoft.Data.Sqlite;

namespace Player.Library;

/// <summary>Distinct-Künstler-Eintrag.</summary>
public sealed record ArtistInfo(string Artist, int TrackCount);

/// <summary>Distinct-Album-Eintrag (ohne Cover-Art-BLOB).</summary>
public sealed record AlbumInfo(string Album, string? DisplayArtist, int? Year, int TrackCount);

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

/// <summary>
/// Verwaltet die SQLite-Audiodatenbank. Eine Instanz pro Anwendungslaufzeit.
/// Die DB-Datei liegt unter %LOCALAPPDATA%\Player\library.db (Windows) bzw.
/// ~/.local/share/Player/library.db (Linux/macOS).
/// </summary>
public sealed class AudioDatabase : IDisposable
{
    private readonly SqliteConnection _conn;

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
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        var path = System.IO.Path.Combine(appData, "Player", "library.db");
        return new AudioDatabase(path);
    }

    // ------------------------------------------------------------------
    // Einfügen / Aktualisieren
    // ------------------------------------------------------------------

    public void Upsert(TrackRecord track)
    {
        using var cmd = _conn.CreateCommand();
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
                has_cover, cover_mime_type, cover_data
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
                $has_cover, $cover_mime_type, $cover_data
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
                cover_data          = excluded.cover_data;
            """;

        BindTrack(cmd, track);
        cmd.ExecuteNonQuery();
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

    public IEnumerable<TrackRecord> GetAll()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM tracks ORDER BY album_artist, album, disc_number, track_number;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            yield return MapRow(reader);
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
                cover_data              BLOB
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

        Execute("""
            CREATE TABLE IF NOT EXISTS playlists (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT    NOT NULL,
                description TEXT,
                created_at  INTEGER NOT NULL,
                modified_at INTEGER NOT NULL
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
    }

    // ------------------------------------------------------------------
    // Playlisten – CRUD
    // ------------------------------------------------------------------

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
        Add(cmd, "$cover_data",             (object?)t.CoverData ?? DBNull.Value);
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
        Id          = r.GetInt64(r.GetOrdinal("id")),
        Name        = r.GetString(r.GetOrdinal("name")),
        Description = NullableString(r, "description"),
        TrackCount  = r.GetInt32(r.GetOrdinal("track_count")),
        CreatedAt   = r.GetInt64(r.GetOrdinal("created_at")),
        ModifiedAt  = r.GetInt64(r.GetOrdinal("modified_at")),
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

    public void Dispose() => _conn.Dispose();
}
