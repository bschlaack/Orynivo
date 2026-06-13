using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Player.Library;

public sealed record LibraryExportProgress(int Percentage, string? CurrentFile);
public sealed record LibraryImportProgress(int Percentage, string? CurrentFile);

public static class LibraryBackupService
{
    private const int CurrentFormatVersion = 1;

    private static string PlayerDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Player");

    public static Task ExportAsync(
        string destinationPath,
        IReadOnlyList<string> libraryPaths,
        IProgress<LibraryExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => Export(destinationPath, libraryPaths, PlayerDataRoot, progress, cancellationToken),
            cancellationToken);

    public static Task<IReadOnlyList<string>> ImportAsync(
        string archivePath,
        IProgress<LibraryImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => Import(
                archivePath,
                PlayerDataRoot,
                rebuildSearchIndex: true,
                progress,
                cancellationToken),
            cancellationToken);

    private static void Export(
        string destinationPath,
        IReadOnlyList<string> libraryPaths,
        string playerDataRoot,
        IProgress<LibraryExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stagingRoot = CreateTemporaryDirectory();
        var temporaryArchivePath = Path.ChangeExtension(destinationPath, ".tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 2, "manifest.json");
            var manifest = new LibraryBackupManifest(
                CurrentFormatVersion,
                DateTimeOffset.UtcNow,
                libraryPaths.ToList());
            File.WriteAllText(
                Path.Combine(stagingRoot, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            var sourceDatabase = Path.Combine(playerDataRoot, "library.db");
            if (File.Exists(sourceDatabase))
            {
                Report(progress, 8, "library.db");
                using (var source = new SqliteConnection($"Data Source={sourceDatabase}"))
                using (var destination = new SqliteConnection(
                    $"Data Source={Path.Combine(stagingRoot, "library.db")}"))
                {
                    source.Open();
                    destination.Open();
                    source.BackupDatabase(destination);
                }
                SqliteConnection.ClearAllPools();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var artworkFiles = EnumerateFiles(Path.Combine(playerDataRoot, "artworks"));
            var copiedArtworkFiles = 0;
            CopyDirectory(
                Path.Combine(playerDataRoot, "artworks"),
                Path.Combine(stagingRoot, "artworks"),
                cancellationToken,
                file =>
                {
                    copiedArtworkFiles++;
                    var percentage = artworkFiles.Count == 0
                        ? 40
                        : 15 + (int)(25d * copiedArtworkFiles / artworkFiles.Count);
                    Report(progress, percentage, Path.GetFileName(file));
                });
            CopyDirectory(
                Path.Combine(playerDataRoot, "artist-images"),
                Path.Combine(stagingRoot, "artist-images"),
                cancellationToken,
                file => Report(progress, 40, Path.GetFileName(file)));

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);
            TryDeleteFile(temporaryArchivePath);

            CreateArchive(
                stagingRoot,
                temporaryArchivePath,
                progress,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(temporaryArchivePath, destinationPath, overwrite: true);
            Report(progress, 100, Path.GetFileName(destinationPath));
        }
        catch
        {
            TryDeleteFile(temporaryArchivePath);
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private static IReadOnlyList<string> Import(
        string archivePath,
        string playerDataRoot,
        bool rebuildSearchIndex,
        IProgress<LibraryImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stagingRoot = CreateTemporaryDirectory();
        var rollbackRoot = CreateTemporaryDirectory();
        var targetDatabase = Path.Combine(playerDataRoot, "library.db");
        var targetArtworks = Path.Combine(playerDataRoot, "artworks");
        var targetArtistImages = Path.Combine(playerDataRoot, "artist-images");
        var targetSearchIndex = Path.Combine(playerDataRoot, "search-index");
        var rollbackDatabase = Path.Combine(rollbackRoot, "library.db");
        var rollbackArtworks = Path.Combine(rollbackRoot, "artworks");
        var rollbackArtistImages = Path.Combine(rollbackRoot, "artist-images");
        var existingDatabaseMoved = false;
        var existingArtworksMoved = false;
        var existingArtistImagesMoved = false;
        var importedDatabaseInstalled = false;
        var importedArtworksInstalled = false;
        var importedArtistImagesInstalled = false;

        try
        {
            ExtractArchive(archivePath, stagingRoot, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            Report(progress, 38, "manifest.json");
            var manifestPath = Path.Combine(stagingRoot, "manifest.json");
            var importedDatabase = Path.Combine(stagingRoot, "library.db");
            if (!File.Exists(manifestPath) || !File.Exists(importedDatabase))
                throw new InvalidDataException("The archive is not a complete Player library backup.");

            var manifest = JsonSerializer.Deserialize<LibraryBackupManifest>(
                File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException("The backup manifest is invalid.");
            if (manifest.FormatVersion != CurrentFormatVersion)
                throw new InvalidDataException(
                    $"Unsupported library backup version: {manifest.FormatVersion}.");

            Report(progress, 42, "library.db");
            ValidateDatabase(importedDatabase);
            cancellationToken.ThrowIfCancellationRequested();

            Report(progress, 47, Path.GetFileName(targetDatabase));
            Directory.CreateDirectory(playerDataRoot);
            SqliteConnection.ClearAllPools();
            CloseDatabaseSidecarFiles(targetDatabase);
            if (File.Exists(targetDatabase))
            {
                File.Move(targetDatabase, rollbackDatabase);
                existingDatabaseMoved = true;
            }
            if (Directory.Exists(targetArtworks))
            {
                Directory.Move(targetArtworks, rollbackArtworks);
                existingArtworksMoved = true;
            }
            if (Directory.Exists(targetArtistImages))
            {
                Directory.Move(targetArtistImages, rollbackArtistImages);
                existingArtistImagesMoved = true;
            }

            File.Copy(importedDatabase, targetDatabase);
            importedDatabaseInstalled = true;
            var importedArtworks = Path.Combine(stagingRoot, "artworks");
            if (Directory.Exists(importedArtworks))
            {
                importedArtworksInstalled = true;
                var artworkFiles = EnumerateFiles(importedArtworks);
                var copiedArtworkFiles = 0;
                CopyDirectory(
                    importedArtworks,
                    targetArtworks,
                    cancellationToken,
                    file =>
                    {
                        copiedArtworkFiles++;
                        var percentage = artworkFiles.Count == 0
                            ? 68
                            : 50 + (int)(18d * copiedArtworkFiles / artworkFiles.Count);
                        Report(progress, percentage, Path.GetFileName(file));
                    });
            }
            var importedArtistImages = Path.Combine(stagingRoot, "artist-images");
            if (Directory.Exists(importedArtistImages))
            {
                importedArtistImagesInstalled = true;
                CopyDirectory(
                    importedArtistImages,
                    targetArtistImages,
                    cancellationToken,
                    file => Report(progress, 68, Path.GetFileName(file)));
            }
            Report(progress, 70, "library.db");
            RebaseArtworkPaths(
                targetDatabase,
                targetArtworks,
                (current, total, file) =>
                {
                    var percentage = total == 0
                        ? 78
                        : 70 + (int)(8d * current / total);
                    Report(progress, percentage, file);
                });
            RebaseArtistImagePaths(targetDatabase, targetArtistImages);

            if (rebuildSearchIndex)
            {
                Report(progress, 80, "search-index");
                if (Directory.Exists(targetSearchIndex))
                    Directory.Delete(targetSearchIndex, recursive: true);
                using var database = new AudioDatabase(targetDatabase);
                TrackSearchIndex.Rebuild(
                    database.GetAll(),
                    (current, total, file) =>
                    {
                        var percentage = total == 0
                            ? 98
                            : 80 + (int)(18d * current / total);
                        Report(progress, percentage, file);
                    });
            }

            Report(progress, 100, Path.GetFileName(archivePath));
            return manifest.LibraryPaths ?? [];
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            if (importedDatabaseInstalled)
                TryDeleteFile(targetDatabase);
            CloseDatabaseSidecarFiles(targetDatabase);
            if (importedArtworksInstalled)
                TryDeleteDirectory(targetArtworks);
            if (importedArtistImagesInstalled)
                TryDeleteDirectory(targetArtistImages);
            if (existingDatabaseMoved && File.Exists(rollbackDatabase))
                File.Move(rollbackDatabase, targetDatabase);
            if (existingArtworksMoved && Directory.Exists(rollbackArtworks))
                Directory.Move(rollbackArtworks, targetArtworks);
            if (existingArtistImagesMoved && Directory.Exists(rollbackArtistImages))
                Directory.Move(rollbackArtistImages, targetArtistImages);
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
            TryDeleteDirectory(rollbackRoot);
        }
    }

    private static void ValidateDatabase(string databasePath)
    {
        using var connection = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('tracks', 'albums', 'artists', 'playlists', 'play_history');
            """;
        if (Convert.ToInt32(command.ExecuteScalar()) != 5)
            throw new InvalidDataException("The archive contains an invalid library database.");
    }

    private static void RebaseArtworkPaths(
        string databasePath,
        string artworkRoot,
        Action<int, int, string?>? progress = null)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT id, content_hash, mime_type FROM artworks;";
        using var reader = select.ExecuteReader();
        var rows = new List<(long Id, string Hash, string? Mime)>();
        while (reader.Read())
            rows.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        reader.Close();

        using var transaction = connection.BeginTransaction();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var originalExtension = row.Mime?.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".jpg"
            };
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE artworks
                SET original_path = $original,
                    thumb_96_path = $thumb96,
                    thumb_320_path = $thumb320
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue(
                "$original",
                Path.Combine(artworkRoot, "original", row.Hash + originalExtension));
            update.Parameters.AddWithValue(
                "$thumb96",
                Path.Combine(artworkRoot, "thumb_96", row.Hash + ".jpg"));
            update.Parameters.AddWithValue(
                "$thumb320",
                Path.Combine(artworkRoot, "thumb_320", row.Hash + ".jpg"));
            update.Parameters.AddWithValue("$id", row.Id);
            update.ExecuteNonQuery();
            progress?.Invoke(index + 1, rows.Count, row.Hash + originalExtension);
        }
        transaction.Commit();
    }

    private static void RebaseArtistImagePaths(string databasePath, string artistImageRoot)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT id, image_path FROM artists WHERE image_path IS NOT NULL;";
        using var reader = select.ExecuteReader();
        var rows = new List<(long Id, string Path)>();
        while (reader.Read())
            rows.Add((reader.GetInt64(0), reader.GetString(1)));
        reader.Close();

        using var transaction = connection.BeginTransaction();
        foreach (var row in rows)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE artists SET image_path = $path WHERE id = $id;";
            update.Parameters.AddWithValue(
                "$path",
                Path.Combine(artistImageRoot, Path.GetFileName(row.Path)));
            update.Parameters.AddWithValue("$id", row.Id);
            update.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void ExtractArchive(
        string archivePath,
        string destinationDirectory,
        IProgress<LibraryImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        var fileEntries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToList();
        var totalBytes = fileEntries.Sum(entry => Math.Max(1, entry.Length));
        long processedBytes = 0;
        var buffer = new byte[128 * 1024];

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The archive contains an invalid file path.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using var input = entry.Open();
            using var output = new FileStream(
                targetPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                FileOptions.SequentialScan);
            int bytesRead;
            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, bytesRead);
                processedBytes += bytesRead;
                var percentage = 2 + (int)(34d * processedBytes / totalBytes);
                Report(progress, Math.Min(36, percentage), entry.Name);
            }
        }
    }

    private static void CopyDirectory(
        string source,
        string destination,
        CancellationToken cancellationToken,
        Action<string>? fileCopied = null)
    {
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(
                destination,
                Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            fileCopied?.Invoke(file);
        }
    }

    private static void CreateArchive(
        string sourceDirectory,
        string archivePath,
        IProgress<LibraryExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = EnumerateFiles(sourceDirectory);
        var totalBytes = files.Sum(file => Math.Max(1, new FileInfo(file).Length));
        long processedBytes = 0;
        var buffer = new byte[128 * 1024];

        using var archiveStream = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDirectory, file)
                .Replace(Path.DirectorySeparatorChar, '/');
            var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
            using var input = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                buffer.Length,
                FileOptions.SequentialScan);
            using var output = entry.Open();
            int bytesRead;
            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, bytesRead);
                processedBytes += bytesRead;
                var percentage = 40 + (int)(58d * processedBytes / totalBytes);
                Report(progress, Math.Min(98, percentage), Path.GetFileName(file));
            }
        }
    }

    private static List<string> EnumerateFiles(string root)
        => Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList()
            : [];

    private static void Report(
        IProgress<LibraryExportProgress>? progress,
        int percentage,
        string? currentFile)
        => progress?.Report(new LibraryExportProgress(
            Math.Clamp(percentage, 0, 100),
            currentFile));

    private static void Report(
        IProgress<LibraryImportProgress>? progress,
        int percentage,
        string? currentFile)
        => progress?.Report(new LibraryImportProgress(
            Math.Clamp(percentage, 0, 100),
            currentFile));

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Player", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CloseDatabaseSidecarFiles(string databasePath)
    {
        TryDeleteFile(databasePath + "-wal");
        TryDeleteFile(databasePath + "-shm");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private sealed record LibraryBackupManifest(
        int FormatVersion,
        DateTimeOffset ExportedAtUtc,
        List<string>? LibraryPaths);
}
