using System.IO;
using System.Net;
using System.Net.Http;
using TagLib;

namespace Orynivo.Library;

/// <summary>Incremental progress report emitted during a library scan or repair operation.</summary>
/// <param name="Current">Number of files processed so far.</param>
/// <param name="Total">Total number of files to process.</param>
/// <param name="CurrentFile">Path or label of the file currently being processed.</param>
public readonly record struct ScanProgress(int Current, int Total, string CurrentFile);

/// <summary>Summary of a completed library scan.</summary>
/// <param name="Total">Total files discovered.</param>
/// <param name="Added">New files added to the library.</param>
/// <param name="Updated">Existing files whose metadata was updated.</param>
/// <param name="Removed">Missing files removed from the library.</param>
/// <param name="Failed">Files that could not be processed.</param>
public readonly record struct ScanResult(int Total, int Added, int Updated, int Removed, int Failed);

/// <summary>
/// Scans library directories with TagLibSharp, upserts track metadata into the database,
/// and maintains the Lucene search index.
/// </summary>
public static class LibraryScanner
{
    private static readonly SemaphoreSlim ScanGate = new(1, 1);
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dsf", ".dff", ".flac", ".mp3", ".wav", ".aiff", ".aif",
        ".m4a", ".aac", ".ogg", ".opus", ".wma"
    };

    /// <summary>
    /// Asynchronously scans <paramref name="rootPath"/> for audio files, skipping unchanged files,
    /// and upserts changed or new tracks into the database.
    /// </summary>
    /// <param name="rootPath">Root directory to scan recursively.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<ScanResult> ScanAsync(
        string rootPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await ScanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => Scan(rootPath, progress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ScanGate.Release();
        }
    }

    /// <summary>
    /// Applies a debounced set of changed, created, renamed, or deleted paths to the library.
    /// </summary>
    /// <param name="paths">Absolute paths reported by file-system watchers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when database or search-index content changed.</returns>
    public static async Task<bool> ApplyFileChangesAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        var pathList = paths
            .Where(IsSupportedPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (pathList.Count == 0)
            return false;

        await ScanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => ApplyFileChanges(pathList, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ScanGate.Release();
        }
    }

    /// <summary>
    /// Re-reads embedded artwork from a sample file for each album that is missing artwork in the database.
    /// </summary>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of albums whose artwork was successfully repaired.</returns>
    public static Task<int> RepairMissingAlbumArtworkAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => RepairMissingAlbumArtwork(progress, cancellationToken), cancellationToken);

    /// <summary>
    /// Downloads front-cover images from the Cover Art Archive for albums that have a MusicBrainz release ID
    /// but no artwork in the database.
    /// </summary>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of album covers successfully downloaded.</returns>
    public static async Task<int> DownloadMissingAlbumArtworkAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var db = AudioDatabase.OpenDefault();
        var albums = db.GetAlbumsMissingArtworkReleaseIds();
        var downloaded = 0;

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0 (album-artwork-download)");

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
        var refreshReplayGainMetadata = db.NeedsReplayGainMetadataScan(rootPath);
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

                if (!refreshReplayGainMetadata &&
                    timestamps.TryGetValue(filePath, out long knownModified) &&
                    knownModified == modifiedAt)
                    continue;

                bool isNew = !timestamps.ContainsKey(filePath);
                var record = BuildRecord(filePath, fi, modifiedAt, now, out _);
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
        var existing = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var missingPaths = db.GetTrackPathsUnderDirectory(rootPath)
            .Where(path => !existing.Contains(path))
            .ToList();
        foreach (var missingPath in missingPaths)
            db.Delete(missingPath);
        if (missingPaths.Count > 0)
            TrackSearchIndex.RemovePaths(missingPaths);
        TrackSearchIndex.RemoveMissingUnderRoot(rootPath, files);
        if (refreshReplayGainMetadata)
            db.MarkReplayGainMetadataScanned(rootPath);

        return new ScanResult(total, added, updated, missingPaths.Count, failed);
    }

    private static bool ApplyFileChanges(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var updatedTracks = new List<TrackRecord>();
        var removedPaths = new List<string>();
        using var db = AudioDatabase.OpenDefault();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!System.IO.File.Exists(path))
            {
                if (db.Delete(path))
                    removedPaths.Add(path);
                continue;
            }

            TrackRecord? record = null;
            for (var attempt = 0; attempt < 4 && record is null; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var file = new FileInfo(path);
                    if (!file.Exists)
                        break;
                    var modifiedAt = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeSeconds();
                    var candidate = BuildRecord(path, file, modifiedAt, now, out var metadataRead);
                    if (!metadataRead)
                    {
                        if (attempt < 3)
                        {
                            Thread.Sleep(250 * (attempt + 1));
                            continue;
                        }
                        break;
                    }
                    record = candidate;
                }
                catch (IOException) when (attempt < 3)
                {
                    Thread.Sleep(250 * (attempt + 1));
                }
                catch (UnauthorizedAccessException) when (attempt < 3)
                {
                    Thread.Sleep(250 * (attempt + 1));
                }
            }

            if (record is null)
                continue;

            db.Upsert(record);
            updatedTracks.Add(db.GetByPath(path) ?? record);
        }

        if (updatedTracks.Count > 0)
            TrackSearchIndex.UpdateMany(updatedTracks);
        if (removedPaths.Count > 0)
            TrackSearchIndex.RemovePaths(removedPaths);
        return updatedTracks.Count > 0 || removedPaths.Count > 0;
    }

    private static bool IsSupportedPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        SupportedExtensions.Contains(Path.GetExtension(path));

    private static TrackRecord BuildRecord(
        string filePath,
        FileInfo fi,
        long modifiedAt,
        long addedAt,
        out bool metadataRead)
    {
        metadataRead = false;
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
            record.ReplayGainTrack      = FormatReplayGain(tag.ReplayGainTrackGain);
            record.ReplayGainAlbum      = FormatReplayGain(tag.ReplayGainAlbumGain);
            metadataRead = true;

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
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? JoinArray(string[]? arr)
        => arr is { Length: > 0 }
            ? NullIfEmpty(string.Join("; ", arr.Select(value => value.Trim()).Where(value => value.Length > 0)))
            : null;

    private static string? FormatReplayGain(double gain) =>
        double.IsNaN(gain) || double.IsInfinity(gain)
            ? null
            : gain.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
