using System.IO;
using System.Net;
using System.Net.Http;
using TagLib;

namespace Player.Library;

public readonly record struct ScanProgress(int Current, int Total, string CurrentFile);
public readonly record struct ScanResult(int Total, int Added, int Updated, int Failed);

public static class LibraryScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dsf", ".dff", ".flac", ".mp3", ".wav", ".aiff", ".aif",
        ".m4a", ".aac", ".ogg", ".opus", ".wma"
    };

    public static Task<ScanResult> ScanAsync(
        string rootPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Scan(rootPath, progress, cancellationToken), cancellationToken);

    public static Task<int> RepairMissingAlbumArtworkAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => RepairMissingAlbumArtwork(progress, cancellationToken), cancellationToken);

    public static async Task<int> DownloadMissingAlbumArtworkAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        var albums = db.GetAlbumsMissingArtworkReleaseIds();
        var downloaded = 0;

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Player/1.0 (album-artwork-download)");

        for (var i = 0; i < albums.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (albumId, releaseId) = albums[i];
            progress?.Report(new ScanProgress(i + 1, albums.Count, releaseId));

            try
            {
                using var response = await client.GetAsync(
                    $"https://coverartarchive.org/release/{Uri.EscapeDataString(releaseId)}/front",
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    continue;

                response.EnsureSuccessStatusCode();
                var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (data.Length == 0)
                    continue;

                db.AttachArtworkToAlbum(albumId, data, response.Content.Headers.ContentType?.MediaType);
                downloaded++;
            }
            catch (HttpRequestException)
            {
                // Einzelne fehlende oder temporär nicht erreichbare Cover überspringen.
            }
        }

        return downloaded;
    }

    private static ScanResult Scan(
        string rootPath,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var files = Directory
            .EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        int total = files.Count;
        int added = 0;
        int updated = 0;
        int failed = 0;
        var changedTracks = new List<TrackRecord>();

        using var db = AudioDatabase.OpenDefault();
        var timestamps = db.GetPathTimestamps();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string filePath = files[i];
            progress?.Report(new ScanProgress(i + 1, total, filePath));

            try
            {
                var fi = new FileInfo(filePath);
                long modifiedAt = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds();

                if (timestamps.TryGetValue(filePath, out long knownModified) && knownModified == modifiedAt)
                    continue;

                bool isNew = !timestamps.ContainsKey(filePath);
                var record = BuildRecord(filePath, fi, modifiedAt, now);
                db.Upsert(record);
                changedTracks.Add(db.GetByPath(filePath) ?? record);

                if (isNew) added++; else updated++;
            }
            catch
            {
                failed++;
            }
        }

        if (changedTracks.Count > 0)
            TrackSearchIndex.UpdateMany(changedTracks);
        TrackSearchIndex.RemoveMissingUnderRoot(rootPath, files);

        return new ScanResult(total, added, updated, failed);
    }

    private static TrackRecord BuildRecord(string filePath, FileInfo fi, long modifiedAt, long addedAt)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        var record = new TrackRecord
        {
            Path       = filePath,
            FileName   = fi.Name,
            FileSize   = fi.Length,
            ModifiedAt = modifiedAt,
            AddedAt    = addedAt,
            Format     = ext.TrimStart('.'),
            IsLossless = ext is ".flac" or ".wav" or ".aiff" or ".aif" or ".dsf" or ".dff",
            IsDsd      = ext is ".dsf" or ".dff",
        };

        try
        {
            using var tagFile = TagLib.File.Create(filePath);

            var props = tagFile.Properties;
            if (props is not null)
            {
                record.Duration   = props.Duration.TotalSeconds > 0 ? props.Duration.TotalSeconds : null;
                record.SampleRate = props.AudioSampleRate > 0 ? props.AudioSampleRate : null;
                record.BitDepth   = props.BitsPerSample > 0 ? props.BitsPerSample : null;
                record.Channels   = props.AudioChannels > 0 ? props.AudioChannels : null;
                record.Bitrate    = props.AudioBitrate > 0 ? props.AudioBitrate : null;
            }

            if (record.IsDsd && record.SampleRate.HasValue)
            {
                record.DsdRate = record.SampleRate.Value switch
                {
                    >= 11289600 => 256,
                    >= 5644800  => 128,
                    >= 2822400  => 64,
                    _           => null
                };
            }

            var tag = tagFile.Tag;
            record.Title       = NullIfEmpty(tag.Title);
            record.Artist      = JoinArray(tag.Performers);
            record.AlbumArtist = JoinArray(tag.AlbumArtists);
            record.Album       = NullIfEmpty(tag.Album);
            record.Genre       = JoinArray(tag.Genres);
            record.Year        = tag.Year > 0 ? (int?)tag.Year : null;
            record.TrackNumber = tag.Track > 0 ? (int?)tag.Track : null;
            record.TrackTotal  = tag.TrackCount > 0 ? (int?)tag.TrackCount : null;
            record.DiscNumber  = tag.Disc > 0 ? (int?)tag.Disc : null;
            record.DiscTotal   = tag.DiscCount > 0 ? (int?)tag.DiscCount : null;
            record.Composer    = JoinArray(tag.Composers);
            record.Comment     = NullIfEmpty(tag.Comment);
            record.Copyright   = NullIfEmpty(tag.Copyright);
            record.Lyrics      = NullIfEmpty(tag.Lyrics);
            record.Bpm         = tag.BeatsPerMinute > 0 ? (int?)tag.BeatsPerMinute : null;

            record.MusicBrainzTrackId   = NullIfEmpty(tag.MusicBrainzTrackId);
            record.MusicBrainzReleaseId = NullIfEmpty(tag.MusicBrainzReleaseId);
            record.MusicBrainzArtistId  = NullIfEmpty(tag.MusicBrainzArtistId);

            var pic = tag.Pictures?.FirstOrDefault(p => p.Type == PictureType.FrontCover)
                   ?? tag.Pictures?.FirstOrDefault();
            if (pic is not null)
            {
                record.HasCover      = true;
                record.CoverMimeType = NullIfEmpty(pic.MimeType);
                record.CoverData     = pic.Data?.Data;
            }
        }
        catch
        {
            // Tag-Lesung fehlgeschlagen – Dateisystem-Metadaten bleiben erhalten
        }

        return record;
    }

    private static int RepairMissingAlbumArtwork(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        using var db = AudioDatabase.OpenDefault();
        var albums = db.GetAlbumsMissingArtworkSamplePaths();
        var repaired = 0;

        for (var i = 0; i < albums.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (albumId, path) = albums[i];
            progress?.Report(new ScanProgress(i + 1, albums.Count, path));

            try
            {
                using var tagFile = TagLib.File.Create(path);
                var pic = tagFile.Tag.Pictures?.FirstOrDefault(p => p.Type == PictureType.FrontCover)
                       ?? tagFile.Tag.Pictures?.FirstOrDefault();
                var data = pic?.Data?.Data;
                if (data is null || data.Length == 0)
                    continue;

                db.AttachArtworkToAlbum(albumId, data, NullIfEmpty(pic?.MimeType));
                repaired++;
            }
            catch
            {
                // Defekte oder nicht lesbare Dateien überspringen; Rest der Reparatur läuft weiter.
            }
        }

        return repaired;
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? JoinArray(string[]? arr)
        => arr is { Length: > 0 } ? NullIfEmpty(string.Join("; ", arr)) : null;
}
