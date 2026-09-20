using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies library-only bulk genre editing: the stored override is reapplied by
/// later scans and the source media files are never modified.
/// </summary>
public sealed class AudioDatabaseGenreOverrideTests
{
    /// <summary>The genre is updated and a later scan cannot restore the embedded value.</summary>
    [Fact]
    public void SetTrackGenres_SurvivesALaterScan()
    {
        using var test = CoreTestDatabase.Create("orynivo-genre-override");

        using (var database = test.Open())
        {
            database.Upsert(CreateTrack(test, "one.flac", "Rock"));
            var id = database.GetTrackList().Single().Id;

            var changed = database.SetTrackGenres([id], "Progressive Rock");

            Assert.Equal(1, changed);
            Assert.Equal("Progressive Rock", database.GetTrackList().Single().Genre);
        }

        // The next scan re-reads the embedded genre and must not win.
        using (var rescanned = test.Open())
        {
            rescanned.Upsert(CreateTrack(test, "one.flac", "Rock"));

            Assert.Equal("Progressive Rock", rescanned.GetTrackList().Single().Genre);
        }
    }

    /// <summary>Clearing the genre removes the override so the next scan restores the embedded value.</summary>
    [Fact]
    public void SetTrackGenres_ClearedGenreIsRestoredByTheNextScan()
    {
        using var test = CoreTestDatabase.Create("orynivo-genre-override");
        using var database = test.Open();
        database.Upsert(CreateTrack(test, "one.flac", "Rock"));
        var id = database.GetTrackList().Single().Id;
        database.SetTrackGenres([id], "Progressive Rock");

        database.SetTrackGenres([id], null);
        Assert.Null(database.GetTrackList().Single().Genre);

        database.Upsert(CreateTrack(test, "one.flac", "Rock"));
        Assert.Equal("Rock", database.GetTrackList().Single().Genre);
    }

    /// <summary>Every selected track is updated in one call.</summary>
    [Fact]
    public void SetTrackGenres_UpdatesEveryTrack()
    {
        using var test = CoreTestDatabase.Create("orynivo-genre-override");
        using var database = test.Open();
        database.Upsert(CreateTrack(test, "one.flac", "Rock"));
        database.Upsert(CreateTrack(test, "two.flac", "Rock"));
        var ids = database.GetTrackList().Select(track => track.Id).ToList();

        var changed = database.SetTrackGenres(ids, "Jazz");

        Assert.Equal(ids.Count, changed);
        Assert.All(database.GetTrackList(), track => Assert.Equal("Jazz", track.Genre));
    }

    /// <summary>Duplicate identifiers are processed once.</summary>
    [Fact]
    public void SetTrackGenres_DeduplicatesIdentifiers()
    {
        using var test = CoreTestDatabase.Create("orynivo-genre-override");
        using var database = test.Open();
        database.Upsert(CreateTrack(test, "one.flac", "Rock"));
        var id = database.GetTrackList().Single().Id;

        Assert.Equal(1, database.SetTrackGenres([id, id, id], "Jazz"));
    }

    /// <summary>An empty selection changes nothing.</summary>
    [Fact]
    public void SetTrackGenres_EmptySelectionIsNoOp()
    {
        using var test = CoreTestDatabase.Create("orynivo-genre-override");
        using var database = test.Open();
        database.Upsert(CreateTrack(test, "one.flac", "Rock"));

        Assert.Equal(0, database.SetTrackGenres([], "Jazz"));
        Assert.Equal("Rock", database.GetTrackList().Single().Genre);
    }

    private static TrackRecord CreateTrack(CoreTestDatabase test, string fileName, string genre) => new()
    {
        Path = test.PathOf("Music", "Album", fileName),
        SourcePath = test.PathOf("Music", "Album", fileName),
        FileName = fileName,
        ModifiedAt = 1,
        AddedAt = 1,
        Title = Path.GetFileNameWithoutExtension(fileName),
        Artist = "Artist",
        AlbumArtist = "Artist",
        Album = "Album",
        Genre = genre
    };
}
