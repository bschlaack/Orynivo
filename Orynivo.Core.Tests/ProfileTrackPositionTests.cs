using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies the profile-scoped cross-device playback position store.</summary>
public sealed class ProfileTrackPositionTests
{
    /// <summary>A stored position round-trips for the active profile.</summary>
    [Fact]
    public void SaveAndGet_RoundTripsPerProfile()
    {
        using var fixture = CoreTestDatabase.Create("orynivo-position");
        using var db = fixture.Open();
        var trackId = AddTrack(db, fixture, "song.flac");
        // ActiveProfileId is process-wide AsyncLocal state; pin it for the test and
        // restore whatever the surrounding context had.
        var previousProfile = AudioDatabase.ActiveProfileId;
        try
        {
            AudioDatabase.SetActiveProfile("standard");
            db.SaveProfileTrackPosition(trackId, 123.5);
            Assert.Equal(123.5, db.GetProfileTrackPosition(trackId));

            AudioDatabase.SetActiveProfile("guest");
            Assert.Null(db.GetProfileTrackPosition(trackId));
            db.SaveProfileTrackPosition(trackId, 42);
            Assert.Equal(42, db.GetProfileTrackPosition(trackId));

            AudioDatabase.SetActiveProfile("standard");
            Assert.Equal(123.5, db.GetProfileTrackPosition(trackId));
        }
        finally
        {
            AudioDatabase.SetActiveProfile(previousProfile);
        }
    }

    /// <summary>A non-positive or invalid position clears the stored entry.</summary>
    [Theory]
    [InlineData(0d)]
    [InlineData(-5d)]
    [InlineData(double.NaN)]
    public void Save_ClearsForNonPositivePositions(double position)
    {
        using var fixture = CoreTestDatabase.Create("orynivo-position");
        using var db = fixture.Open();
        var trackId = AddTrack(db, fixture, "song.flac");
        var previousProfile = AudioDatabase.ActiveProfileId;
        try
        {
            AudioDatabase.SetActiveProfile("standard");
            db.SaveProfileTrackPosition(trackId, 90);
            db.SaveProfileTrackPosition(trackId, position);

            Assert.Null(db.GetProfileTrackPosition(trackId));
        }
        finally
        {
            AudioDatabase.SetActiveProfile(previousProfile);
        }
    }

    /// <summary>An unknown track has no stored position.</summary>
    [Fact]
    public void Get_ReturnsNullForUnknownTrack()
    {
        using var fixture = CoreTestDatabase.Create("orynivo-position");
        using var db = fixture.Open();

        Assert.Null(db.GetProfileTrackPosition(4242));
    }

    private static long AddTrack(AudioDatabase db, CoreTestDatabase fixture, string fileName)
    {
        var path = fixture.PathOf(fileName);
        db.Upsert(new TrackRecord
        {
            Path = path,
            SourcePath = path,
            FileName = fileName,
            ModifiedAt = 1,
            AddedAt = 1,
            Title = "Title",
            Artist = "Artist",
            Album = "Album",
            AlbumArtist = "Artist"
        });
        return db.GetTrackIdByPath(path) ?? throw new InvalidOperationException("Track was not stored.");
    }
}
