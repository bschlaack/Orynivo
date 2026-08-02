using Microsoft.Data.Sqlite;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies portable library backup operations against explicit server data roots.</summary>
public sealed class LibraryBackupServiceTests
{
    /// <summary>Exports and restores the database and manifest paths between independent data roots.</summary>
    [Fact]
    public async Task ExplicitDataRoot_RoundTripsLibraryAndConfiguredPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-server-backup-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, "source");
        var targetRoot = Path.Combine(root, "target");
        var archivePath = Path.Combine(root, "backup.zip");
        Directory.CreateDirectory(sourceRoot);

        try
        {
            var trackPath = Path.Combine(root, "music", "track.flac");
            using (var database = new AudioDatabase(Path.Combine(sourceRoot, "library.db")))
            {
                database.Upsert(new TrackRecord
                {
                    Path = trackPath,
                    SourcePath = trackPath,
                    FileName = "track.flac",
                    ModifiedAt = 1,
                    AddedAt = 1,
                    Title = "Backup track",
                    Artist = "Backup artist",
                    AlbumArtist = "Backup artist",
                    Album = "Backup album"
                });
            }

            var configuredPaths = new[] { Path.Combine(root, "music") };
            await LibraryBackupService.ExportAsync(
                archivePath,
                configuredPaths,
                sourceRoot);
            var restoredPaths = await LibraryBackupService.ImportAsync(
                archivePath,
                targetRoot,
                rebuildSearchIndex: false);

            Assert.Equal(configuredPaths, restoredPaths);
            using var restored = new AudioDatabase(Path.Combine(targetRoot, "library.db"));
            var track = Assert.Single(restored.GetAll());
            Assert.Equal("Backup track", track.Title);
            Assert.Equal("Backup album", track.Album);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
