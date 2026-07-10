using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using Orynivo.Audio;
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
        ".m4a", ".aac", ".ogg", ".opus", ".wma", ".cue"
    };

    private static readonly EnumerationOptions RecursiveScanOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true
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
    /// Re-reads embedded artwork for one album that is missing artwork in the database.
    /// </summary>
    /// <param name="albumId">Identifier of the album to repair.</param>
    /// <returns><see langword="true"/> when artwork was found and attached to the album.</returns>
    public static bool RepairMissingAlbumArtwork(long albumId)
    {
        using var db = AudioDatabase.OpenDefault();
        var samplePath = db.GetAlbumMissingArtworkSamplePath(albumId);
        return !string.IsNullOrWhiteSpace(samplePath)
               && RepairAlbumArtwork(db, albumId, samplePath);
    }

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
        progress?.Report(new ScanProgress(0, 0, rootPath));
        var discoveredFiles = Directory
            .EnumerateFiles(rootPath, "*.*", RecursiveScanOptions)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .Select(Path.GetFullPath)
            .ToList();
        var cueFiles = discoveredFiles
            .Where(path => Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var cueDefinitions = cueFiles
            .SelectMany(path =>
            {
                try { return CueSheetParser.Parse(path); }
                catch { return []; }
            })
            .Where(definition => System.IO.File.Exists(definition.SourcePath))
            .ToList();
        var cueSources = cueDefinitions
            .Select(definition => definition.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = discoveredFiles
            .Where(path => !Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            .Where(path => !cueSources.Contains(path))
            .ToList();

        int total = files.Count + cueFiles.Count;
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

                var metadataChanged = !timestamps.TryGetValue(filePath, out long knownModified) ||
                                      knownModified != modifiedAt;
                if (!refreshReplayGainMetadata && !metadataChanged)
                    continue;

                bool isNew = !timestamps.ContainsKey(filePath);
                var record = BuildRecord(filePath, fi, modifiedAt, now, out _);
                if (metadataChanged)
                    EnsureReplayGain(record, ct);
                db.Upsert(record);
                changedTracks.Add(db.GetByPath(filePath) ?? record);

                if (isNew) added++; else updated++;
            }
            catch
            {
                failed++;
            }
        }

        foreach (var cueGroup in cueDefinitions.GroupBy(definition => definition.CuePath, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(files.Count + 1, total, cueGroup.Key));
            try
            {
                var calculateReplayGain = cueGroup.Any(definition =>
                    db.GetByPath(definition.VirtualPath) is not { ReplayGainTrack: { Length: > 0 } });
                var records = BuildCueRecords(cueGroup.Key, cueGroup.ToList(), now, ct, calculateReplayGain);
                foreach (var record in records)
                {
                    var isNew = db.GetByPath(record.Path) is null;
                    db.Upsert(record);
                    changedTracks.Add(db.GetByPath(record.Path) ?? record);
                    if (isNew) added++; else updated++;
                }
                var staleCuePaths = db.GetTrackPathsByCue(cueGroup.Key)
                    .Except(records.Select(record => record.Path), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                DeleteCueTracksAndWaveforms(db, cueGroup.Key, records.Select(record => record.Path).ToList());
                if (staleCuePaths.Count > 0)
                    TrackSearchIndex.RemovePaths(staleCuePaths);
            }
            catch
            {
                failed++;
            }
        }

        changedTracks.AddRange(EnsureAlbumReplayGain(db, changedTracks, ct));
        if (changedTracks.Count > 0)
            TrackSearchIndex.UpdateMany(changedTracks);
        var existing = new HashSet<string>(
            files.Concat(cueDefinitions.Select(definition => definition.VirtualPath)),
            StringComparer.OrdinalIgnoreCase);
        var missingPaths = db.GetTrackPathsUnderDirectory(rootPath)
            .Where(path => !existing.Contains(path))
            .ToList();
        foreach (var missingPath in missingPaths)
            DeleteTrackAndWaveform(db, missingPath);
        if (missingPaths.Count > 0)
            TrackSearchIndex.RemovePaths(missingPaths);
        TrackSearchIndex.RemoveMissingUnderRoot(rootPath, existing);
        if (refreshReplayGainMetadata)
            db.MarkReplayGainMetadataScanned(rootPath);

        return new ScanResult(total, added, updated, missingPaths.Count, failed);
    }

    /// <summary>Removes database and index entries that no longer belong to any configured library root.</summary>
    /// <param name="rootPaths">Currently configured library roots.</param>
    /// <returns>Number of removed track records.</returns>
    public static int RemoveTracksOutsideRoots(IReadOnlyCollection<string> rootPaths)
    {
        var roots = rootPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path =>
            {
                try { return Path.GetFullPath(path); }
                catch { return null; }
            })
            .Where(path => path is not null)
            .Cast<string>()
            .ToList();

        using var db = AudioDatabase.OpenDefault();
        var removedPaths = new List<string>();
        foreach (var track in db.GetTrackCleanupRecords())
        {
            var sourcePath = string.IsNullOrWhiteSpace(track.SourcePath) ? track.Path : track.SourcePath;
            if (roots.Count > 0 && roots.Any(root => IsUnderRoot(sourcePath, root)))
                continue;

            DeleteWaveformForTrack(track);
            if (db.Delete(track.Path))
                removedPaths.Add(track.Path);
        }

        if (removedPaths.Count > 0)
            TrackSearchIndex.RemovePaths(removedPaths);
        return removedPaths.Count;
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
            if (Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                if (!System.IO.File.Exists(path))
                {
                    var removedCuePaths = db.GetTrackPathsByCue(path);
                    DeleteCueTracksAndWaveforms(db, path);
                    removedPaths.AddRange(removedCuePaths);
                    continue;
                }

                var definitions = CueSheetParser.Parse(path)
                    .Where(definition => System.IO.File.Exists(definition.SourcePath))
                    .ToList();
                var records = BuildCueRecords(path, definitions, now, cancellationToken, calculateMissingReplayGain: true);
                foreach (var cueRecord in records)
                {
                    db.Upsert(cueRecord);
                    updatedTracks.Add(db.GetByPath(cueRecord.Path) ?? cueRecord);
                }
                var staleCuePaths = db.GetTrackPathsByCue(path)
                    .Except(records.Select(record => record.Path), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                DeleteCueTracksAndWaveforms(db, path, records.Select(record => record.Path).ToList());
                removedPaths.AddRange(staleCuePaths);
                continue;
            }

            var relatedCuePaths = db.GetCuePathsForSource(path);
            if (relatedCuePaths.Count > 0)
            {
                foreach (var cuePath in relatedCuePaths)
                {
                    var definitions = System.IO.File.Exists(cuePath)
                        ? CueSheetParser.Parse(cuePath)
                            .Where(definition => System.IO.File.Exists(definition.SourcePath))
                            .ToList()
                        : [];
                    var records = BuildCueRecords(cuePath, definitions, now, cancellationToken, calculateMissingReplayGain: true);
                    foreach (var cueRecord in records)
                    {
                        db.Upsert(cueRecord);
                        updatedTracks.Add(db.GetByPath(cueRecord.Path) ?? cueRecord);
                    }
                    var staleCuePaths = db.GetTrackPathsByCue(cuePath)
                        .Except(records.Select(record => record.Path), StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    DeleteCueTracksAndWaveforms(db, cuePath, records.Select(record => record.Path).ToList());
                    removedPaths.AddRange(staleCuePaths);
                }
                continue;
            }

            if (!System.IO.File.Exists(path))
            {
                if (DeleteTrackAndWaveform(db, path))
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
                            WaitBeforeRetry(attempt, cancellationToken);
                            continue;
                        }
                        break;
                    }
                    EnsureReplayGain(candidate, cancellationToken);
                    record = candidate;
                }
                catch (IOException) when (attempt < 3)
                {
                    WaitBeforeRetry(attempt, cancellationToken);
                }
                catch (UnauthorizedAccessException) when (attempt < 3)
                {
                    WaitBeforeRetry(attempt, cancellationToken);
                }
            }

            if (record is null)
                continue;

            db.Upsert(record);
            updatedTracks.Add(db.GetByPath(path) ?? record);
        }

        updatedTracks.AddRange(EnsureAlbumReplayGain(db, updatedTracks, cancellationToken));

        if (updatedTracks.Count > 0)
            TrackSearchIndex.UpdateMany(updatedTracks);
        if (removedPaths.Count > 0)
            TrackSearchIndex.RemovePaths(removedPaths);
        return updatedTracks.Count > 0 || removedPaths.Count > 0;
    }

    private static bool DeleteTrackAndWaveform(AudioDatabase db, string path)
    {
        var track = db.GetByPath(path);
        if (track is not null)
            DeleteWaveformForTrack(track);
        return db.Delete(path);
    }

    private static int DeleteCueTracksAndWaveforms(
        AudioDatabase db,
        string cuePath,
        IReadOnlyCollection<string>? exceptPaths = null)
    {
        var paths = db.GetTrackPathsByCue(cuePath);
        if (exceptPaths is { Count: > 0 })
            paths = paths.Except(exceptPaths, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var path in paths)
        {
            if (db.GetByPath(path) is { } track)
                DeleteWaveformForTrack(track);
        }

        return db.DeleteCueTracks(cuePath, exceptPaths);
    }

    private static void DeleteWaveformForTrack(TrackRecord track)
    {
        if (track.Duration is not > 0)
            return;

        WaveformCache.DeleteCached(
            track.Path,
            string.IsNullOrWhiteSpace(track.SourcePath) ? null : track.SourcePath,
            TimeSpan.FromSeconds(track.Duration.Value),
            900,
            track.FileSize,
            track.ModifiedAt,
            track.SegmentStart is double start ? TimeSpan.FromSeconds(start) : null,
            track.SegmentEnd is double end ? TimeSpan.FromSeconds(end) : null);
    }

    private static bool IsUnderRoot(string path, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, path);
        return relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static void WaitBeforeRetry(int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(250 * (attempt + 1));
        if (cancellationToken.WaitHandle.WaitOne(delay))
            cancellationToken.ThrowIfCancellationRequested();
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
            SourcePath = filePath,
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

    private static List<TrackRecord> BuildCueRecords(
        string cuePath,
        IReadOnlyList<CueTrackDefinition> definitions,
        long addedAt,
        CancellationToken cancellationToken,
        bool calculateMissingReplayGain)
    {
        var cueModified = new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(cuePath)).ToUnixTimeSeconds();
        var result = new List<TrackRecord>(definitions.Count);
        foreach (var definition in definitions)
        {
            var sourceFile = new FileInfo(definition.SourcePath);
            var sourceModified = new DateTimeOffset(sourceFile.LastWriteTimeUtc).ToUnixTimeSeconds();
            var record = BuildRecord(
                definition.SourcePath,
                sourceFile,
                Math.Max(cueModified, sourceModified),
                addedAt,
                out _);
            record.Path = definition.VirtualPath;
            record.SourcePath = definition.SourcePath;
            record.CuePath = definition.CuePath;
            record.SegmentStart = definition.StartSeconds;
            record.SegmentEnd = definition.EndSeconds ?? record.Duration;
            record.Duration = record.SegmentEnd is double end
                ? Math.Max(0, end - definition.StartSeconds)
                : null;
            record.FileName = $"{Path.GetFileNameWithoutExtension(definition.SourcePath)} #{definition.Number:D2}";
            record.Title = definition.Title ?? record.Title ?? record.FileName;
            record.Artist = definition.Artist ?? record.Artist;
            record.Album = definition.Album ?? record.Album;
            record.AlbumArtist = definition.AlbumArtist ?? record.AlbumArtist ?? record.Artist;
            record.Genre = definition.Genre ?? record.Genre;
            record.Year = definition.Year ?? record.Year;
            record.TrackNumber = definition.Number;
            record.TrackTotal = definitions.Count;
            if (calculateMissingReplayGain)
                EnsureReplayGain(record, cancellationToken);
            result.Add(record);
        }
        return result;
    }

    private static void EnsureReplayGain(TrackRecord record, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(record.ReplayGainTrack))
            return;

        var sourcePath = string.IsNullOrWhiteSpace(record.SourcePath) ? record.Path : record.SourcePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !System.IO.File.Exists(sourcePath))
            return;

        try
        {
            record.ReplayGainTrack = AnalyzeReplayGainTrack(
                sourcePath,
                record.SegmentStart,
                record.SegmentEnd,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // ReplayGain analysis is opportunistic; a failed analyzer must not fail the library scan.
        }
    }

    private static string? AnalyzeReplayGainTrack(
        string sourcePath,
        double? segmentStart,
        double? segmentEnd,
        CancellationToken cancellationToken,
        Action<IList<string>>? configureInputArguments = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = FfmpegLocator.GetSafeWorkingDirectory()
        };
        AddReplayGainArguments(startInfo, sourcePath, segmentStart, segmentEnd, configureInputArguments);

        using var process = Process.Start(startInfo);

        if (process is null)
            return null;

        try
        {
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
            _ = stdoutTask.GetAwaiter().GetResult();

            var stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                return null;

            return ParseReplayGainTrack(stderr);
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private static void AddReplayGainArguments(
        ProcessStartInfo startInfo,
        string sourcePath,
        double? segmentStart,
        double? segmentEnd,
        Action<IList<string>>? configureInputArguments)
    {
        if (segmentStart is > 0)
        {
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(segmentStart.Value.ToString("F6", CultureInfo.InvariantCulture));
        }

        configureInputArguments?.Invoke(startInfo.ArgumentList);
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);

        if (segmentStart is double start && segmentEnd is double end && end > start)
        {
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add((end - start).ToString("F6", CultureInfo.InvariantCulture));
        }

        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:a:0");
        startInfo.ArgumentList.Add("-af");
        startInfo.ArgumentList.Add("replaygain");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("null");
        startInfo.ArgumentList.Add("-");
    }

    private static string? ParseReplayGainTrack(string stderr)
    {
        foreach (var line in stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            const string marker = "track_gain =";
            var markerIndex = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                continue;

            var value = line[(markerIndex + marker.Length)..].Trim();
            if (value.EndsWith("dB", StringComparison.OrdinalIgnoreCase))
                value = value[..^2].Trim();

            if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var gain) &&
                double.IsFinite(gain))
            {
                return FormatReplayGain(gain);
            }
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static List<TrackRecord> EnsureAlbumReplayGain(
        AudioDatabase db,
        IReadOnlyCollection<TrackRecord> changedTracks,
        CancellationToken cancellationToken)
    {
        if (changedTracks.Count == 0)
            return [];

        var changedRows = db.GetTrackListByPaths(changedTracks.Select(track => track.Path));
        var albumIds = changedRows
            .Select(track => track.AlbumId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (albumIds.Count == 0)
            return [];

        var updated = new List<TrackRecord>();
        foreach (var albumId in albumIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var albumRows = db.GetTrackListByAlbum(albumId);
            if (albumRows.Count == 0 || albumRows.All(row => !string.IsNullOrWhiteSpace(row.ReplayGainAlbum)))
                continue;

            var existingAlbumGain = albumRows
                .Select(row => row.ReplayGainAlbum)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var albumGain = existingAlbumGain ?? AnalyzeReplayGainAlbum(
                albumRows
                    .Select(row => db.GetByPath(row.Path))
                    .Where(track => track is not null)
                    .Cast<TrackRecord>()
                    .ToList(),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(albumGain))
                continue;

            var missingIds = albumRows
                .Where(row => string.IsNullOrWhiteSpace(row.ReplayGainAlbum))
                .Select(row => row.Id)
                .ToList();
            if (db.UpdateReplayGainAlbumForTracks(missingIds, albumGain) == 0)
                continue;

            updated.AddRange(
                db.GetTrackListByIds(missingIds)
                    .Select(row => db.GetByPath(row.Path))
                    .Where(track => track is not null)
                    .Cast<TrackRecord>());
        }

        return updated;
    }

    private static string? AnalyzeReplayGainAlbum(
        IReadOnlyList<TrackRecord> tracks,
        CancellationToken cancellationToken)
    {
        var playableTracks = tracks
            .Where(track =>
            {
                var sourcePath = string.IsNullOrWhiteSpace(track.SourcePath) ? track.Path : track.SourcePath;
                return !string.IsNullOrWhiteSpace(sourcePath) && System.IO.File.Exists(sourcePath);
            })
            .OrderBy(track => track.DiscNumber ?? 0)
            .ThenBy(track => track.TrackNumber ?? 0)
            .ThenBy(track => track.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (playableTracks.Count == 0)
            return null;

        var listPath = Path.Combine(Path.GetTempPath(), $"orynivo-rg-{Guid.NewGuid():N}.ffconcat");
        try
        {
            WriteReplayGainConcatFile(listPath, playableTracks);
            return AnalyzeReplayGainTrack(listPath, null, null, cancellationToken, inputArguments =>
            {
                inputArguments.Add("-safe");
                inputArguments.Add("0");
                inputArguments.Add("-f");
                inputArguments.Add("concat");
            });
        }
        finally
        {
            try { System.IO.File.Delete(listPath); }
            catch { }
        }
    }

    private static void WriteReplayGainConcatFile(string listPath, IReadOnlyList<TrackRecord> tracks)
    {
        using var writer = new StreamWriter(listPath, false, new System.Text.UTF8Encoding(false));
        writer.WriteLine("ffconcat version 1.0");
        foreach (var track in tracks)
        {
            var sourcePath = string.IsNullOrWhiteSpace(track.SourcePath) ? track.Path : track.SourcePath;
            writer.WriteLine($"file '{EscapeFfconcatPath(sourcePath)}'");
            if (track.SegmentStart is double start && start > 0)
                writer.WriteLine($"inpoint {start.ToString("F6", CultureInfo.InvariantCulture)}");
            if (track.SegmentEnd is double end && end > 0)
                writer.WriteLine($"outpoint {end.ToString("F6", CultureInfo.InvariantCulture)}");
        }
    }

    private static string EscapeFfconcatPath(string path)
        => path.Replace("\\", "/", StringComparison.Ordinal)
            .Replace("'", "'\\''", StringComparison.Ordinal);

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

            if (RepairAlbumArtwork(db, albumId, path))
                repaired++;
        }

        return repaired;
    }

    private static bool RepairAlbumArtwork(AudioDatabase db, long albumId, string path)
    {
        try
        {
            using var tagFile = TagLib.File.Create(path);
            var pic = tagFile.Tag.Pictures?.FirstOrDefault(p => p.Type == PictureType.FrontCover)
                   ?? tagFile.Tag.Pictures?.FirstOrDefault();
            var data = pic?.Data?.Data;
            if (data is null || data.Length == 0)
                return false;

            return db.AttachArtworkToAlbum(albumId, data, NullIfEmpty(pic?.MimeType));
        }
        catch
        {
            // Skip corrupt or unreadable files so the remaining repair can continue.
            return false;
        }
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
