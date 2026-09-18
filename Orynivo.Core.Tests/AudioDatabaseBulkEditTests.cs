using Microsoft.Data.Sqlite;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies transactional bulk favorite and personal-rating updates for several
/// tracks at once.
/// </summary>
public sealed class AudioDatabaseBulkEditTests
{
    /// <summary>Applies the favorite state to every supplied track.</summary>
    [Fact]
    public void SetTrackFavorites_UpdatesEveryTrack()
    {
        RunWithDatabase((database, ids) =>
        {
            var processed = database.SetTrackFavorites(ids, true);

            Assert.Equal(ids.Count, processed);
            Assert.All(database.GetTrackList(), track => Assert.True(track.IsFavorite));

            database.SetTrackFavorites(ids, false);
            Assert.All(database.GetTrackList(), track => Assert.False(track.IsFavorite));
        });
    }

    /// <summary>Applies the personal rating to every supplied track.</summary>
    [Fact]
    public void SetTrackUserRatings_UpdatesEveryTrack()
    {
        RunWithDatabase((database, ids) =>
        {
            var processed = database.SetTrackUserRatings(ids, 4);

            Assert.Equal(ids.Count, processed);
            Assert.All(ids, id => Assert.Equal(4, database.GetTrackRating(id)!.UserRating));

            database.SetTrackUserRatings(ids, 0);
            Assert.All(ids, id => Assert.Equal(0, database.GetTrackRating(id)!.UserRating));
        });
    }

    /// <summary>Duplicate identifiers are processed once.</summary>
    [Fact]
    public void SetTrackFavorites_DeduplicatesIdentifiers()
    {
        RunWithDatabase((database, ids) =>
        {
            var processed = database.SetTrackFavorites([ids[0], ids[0], ids[0]], true);

            Assert.Equal(1, processed);
        });
    }

    /// <summary>An empty selection changes nothing.</summary>
    [Fact]
    public void SetTrackUserRatings_EmptySelectionIsNoOp()
    {
        RunWithDatabase((database, _) =>
        {
            Assert.Equal(0, database.SetTrackUserRatings([], 3));
        });
    }

    /// <summary>An out-of-range rating is rejected.</summary>
    [Fact]
    public void SetTrackUserRatings_RejectsOutOfRangeRating()
    {
        RunWithDatabase((database, ids) =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => database.SetTrackUserRatings(ids, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => database.SetTrackUserRatings(ids, -1));
        });
    }

    private static void RunWithDatabase(Action<AudioDatabase, List<long>> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-bulk-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var database = new AudioDatabase(Path.Combine(root, "library.db"));
            foreach (var name in new[] { "a.flac", "b.flac", "c.flac" })
                database.Upsert(CreateTrack(Path.Combine(root, name)));

            var ids = database.GetTrackList().Select(track => track.Id).ToList();
            action(database, ids);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static TrackRecord CreateTrack(string path) => new()
    {
        Path = path,
        SourcePath = path,
        FileName = Path.GetFileName(path),
        ModifiedAt = 1,
        AddedAt = 1,
        Duration = 180,
        Title = Path.GetFileNameWithoutExtension(path),
        Artist = "Artist",
        AlbumArtist = "Artist",
        Album = "Album",
        TrackNumber = 1
    };
}
