using Microsoft.Data.Sqlite;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies that catalog queries hide album records without indexed tracks.</summary>
public sealed class AudioDatabaseAlbumVisibilityTests
{
    /// <summary>Excludes orphaned albums and their otherwise empty artists from every catalog surface.</summary>
    [Fact]
    public void AlbumCatalogQueries_ExcludeAlbumsWithoutTracks()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-album-visibility-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");

        try
        {
            using (var database = new AudioDatabase(databasePath))
            {
                database.Upsert(CreateTrack(Path.Combine(root, "visible.flac"), "Visible album", "Visible artist"));
            }

            long orphanAlbumId;
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO artists(name) VALUES ('Orphan artist');
                    INSERT INTO albums(title, source_directory, artist_id)
                    VALUES ('Orphan album', 'orphan-directory', last_insert_rowid());
                    SELECT last_insert_rowid();
                    """;
                orphanAlbumId = (long)(command.ExecuteScalar() ?? 0L);
            }

            using (var reopened = new AudioDatabase(databasePath))
            {
                var album = Assert.Single(reopened.GetAlbumsLite(includeArtwork: false));
                Assert.Equal("Visible album", album.Album);
                Assert.Single(reopened.GetAlbumsLite(includeArtwork: true));
                var artist = Assert.Single(reopened.GetArtistsLite());
                Assert.Equal("Visible artist", artist.Artist);
                Assert.Single(reopened.GetAlbumsByArtist(artist.Id));
                Assert.Null(reopened.GetAlbumById(orphanAlbumId));
                Assert.Equal(1, reopened.GetDashboardLibrarySummary().AlbumCount);
                Assert.DoesNotContain(reopened.GetRecentAlbums(20), recent => recent.Title == "Orphan album");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static TrackRecord CreateTrack(string path, string album, string artist) =>
        new()
        {
            Path = path,
            SourcePath = path,
            FileName = Path.GetFileName(path),
            ModifiedAt = 1,
            AddedAt = 1,
            Duration = 180,
            Title = "Visible track",
            Artist = artist,
            AlbumArtist = artist,
            Album = album,
            TrackNumber = 1
        };
}
