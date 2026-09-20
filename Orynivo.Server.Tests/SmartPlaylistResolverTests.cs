using Microsoft.Data.Sqlite;
using Orynivo.Library;
using Orynivo.Server.Services;
using Xunit;

namespace Orynivo.Server.Tests;

/// <summary>
/// Verifies that the server resolves a similarity smart playlist to its nearest
/// neighbours instead of returning an empty list.
/// </summary>
public sealed class SmartPlaylistResolverTests
{
    /// <summary>A similarity reference resolves to the matching neighbours.</summary>
    [Fact]
    public void Resolve_WithSimilarityReference_ReturnsNeighbours()
    {
        RunWithDatabase(database =>
        {
            var candidates = database.GetSmartPlaylistTracks();
            var seed = candidates.Single(track => track.SortTitle == "Seed");
            var criteria = new SmartPlaylistCriteria
            {
                SimilaritySourceKey = "local",
                SimilarityTrackId = seed.Id
            };

            var resolved = SmartPlaylistResolver.Resolve(database, criteria, candidates);

            Assert.Contains(resolved, track => track.SortTitle == "Neighbour");
            Assert.DoesNotContain(resolved, track => track.SortTitle == "Seed");
        });
    }

    /// <summary>The plain resolver cannot honour a reference, which is why the server needs the helper.</summary>
    [Fact]
    public void PlainResolve_WithSimilarityReference_ReturnsEmpty()
    {
        RunWithDatabase(database =>
        {
            var candidates = database.GetSmartPlaylistTracks();
            var seed = candidates.Single(track => track.SortTitle == "Seed");
            var criteria = new SmartPlaylistCriteria
            {
                SimilaritySourceKey = "local",
                SimilarityTrackId = seed.Id
            };

            Assert.Empty(criteria.Resolve(candidates));
        });
    }

    /// <summary>Criteria without a reference keep their normal resolution.</summary>
    [Fact]
    public void Resolve_WithoutReference_ReturnsPlainResult()
    {
        RunWithDatabase(database =>
        {
            var candidates = database.GetSmartPlaylistTracks();
            var criteria = new SmartPlaylistCriteria { SearchText = "Neighbour" };

            var resolved = SmartPlaylistResolver.Resolve(database, criteria, candidates);

            Assert.Equal(["Neighbour"], resolved.Select(track => track.SortTitle));
        });
    }

    private static void RunWithDatabase(Action<AudioDatabase> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-server-resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        try
        {
            using (var database = new AudioDatabase(databasePath))
            {
                database.Upsert(CreateTrack(root, "seed.flac", "Seed", "Artist A", "Rock", 120));
                database.Upsert(CreateTrack(root, "neighbour.flac", "Neighbour", "Artist B", "Rock", 120));
                database.Upsert(CreateTrack(root, "distant.flac", "Distant", "Artist C", "Ambient", 60));
            }

            using var reopened = new AudioDatabase(databasePath);
            action(reopened);
        }
        finally
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={databasePath}");
                SqliteConnection.ClearPool(connection);
            }
            catch
            {
                // A pool that cannot be cleared must not hide the test result.
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private static TrackRecord CreateTrack(
        string root,
        string fileName,
        string title,
        string artist,
        string genre,
        int bpm)
    {
        var path = Path.Combine(root, "Music", "Album", fileName);
        return new TrackRecord
        {
            Path = path,
            SourcePath = path,
            FileName = fileName,
            ModifiedAt = 1,
            AddedAt = 1,
            Title = title,
            Artist = artist,
            AlbumArtist = artist,
            Album = "Album",
            Genre = genre,
            Bpm = bpm
        };
    }
}
