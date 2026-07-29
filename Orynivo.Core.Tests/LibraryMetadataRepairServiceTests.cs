using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies physical-folder problem detection and persistent library-only corrections.</summary>
public sealed class LibraryMetadataRepairServiceTests
{
    /// <summary>Detects a folder split by inconsistent album titles and missing track numbers.</summary>
    [Fact]
    public void Analyze_DetectsFragmentedPhysicalAlbum()
    {
        var folder = Path.Combine(Path.GetTempPath(), "orynivo-repair-analysis");
        var tracks = new[]
        {
            new MetadataRepairTrack(1, Path.Combine(folder, "one.flac"), Path.Combine(folder, "one.flac"),
                "One", "Artist", "Album", "Artist", 180, 1, 1),
            new MetadataRepairTrack(2, Path.Combine(folder, "two.flac"), Path.Combine(folder, "two.flac"),
                "Two", "Artist", "Album typo", "Artist", 200, null, 1)
        };

        var candidate = Assert.Single(LibraryMetadataRepairService.Analyze(tracks));

        Assert.Equal(2, candidate.AlbumCount);
        Assert.Equal(1, candidate.MissingTrackNumberCount);
        Assert.True(candidate.HasProblems);
    }

    /// <summary>Ensures confirmed metadata remains active when the original bad tags are upserted again.</summary>
    [Fact]
    public void ApplyTrackMetadataOverrides_SurvivesLaterUpsert()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-repair-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        var trackPath = Path.Combine(root, "track.flac");
        using var database = new AudioDatabase(databasePath);
        database.Upsert(CreateTrack(trackPath, "Wrong title", "Wrong artist", "Wrong album"));
        database.ApplyTrackMetadataOverrides(
        [
            new TrackMetadataOverride(
                trackPath,
                "Correct title",
                "Correct artist",
                "Correct artist",
                "Correct album",
                1,
                1,
                1,
                1,
                "recording-id",
                "release-id",
                "artist-id")
        ]);

        database.Upsert(CreateTrack(trackPath, "Wrong title", "Wrong artist", "Wrong album"));
        var corrected = database.GetByPath(trackPath);

        Assert.NotNull(corrected);
        Assert.Equal("Correct title", corrected.Title);
        Assert.Equal("Correct artist", corrected.Artist);
        Assert.Equal("Correct album", corrected.Album);
        Assert.Equal("release-id", corrected.MusicBrainzReleaseId);
    }

    /// <summary>Retains a plausible text match when MusicBrainz supplies no track durations.</summary>
    [Fact]
    public void CalculateMatchConfidence_UsesTitlesWithoutDurations()
    {
        var local = new[]
        {
            new MetadataRepairTrack(1, "one.flac", "one.flac", "First Song", "Artist",
                "Album", "Artist", null, 1, 1),
            new MetadataRepairTrack(2, "two.flac", "two.flac", "Second Song", "Artist",
                "Album", "Artist", null, 2, 1)
        };
        var remote = new[]
        {
            new MetadataMatchTrack(1, "First Song", "Artist", null, null),
            new MetadataMatchTrack(2, "Second Song", "Artist", null, null)
        };

        var confidence = LibraryMetadataRepairService.CalculateMatchConfidence(
            local,
            remote,
            0,
            0);

        Assert.True(confidence >= 0.75);
    }

    /// <summary>Ranks close duration and title evidence above an unrelated candidate.</summary>
    [Fact]
    public void CalculateMatchConfidence_PrefersMatchingEvidence()
    {
        var local = new[]
        {
            new MetadataRepairTrack(1, "one.flac", "one.flac", "First Song", "Artist",
                "Album", "Artist", 180, 1, 1)
        };
        var matching = new[]
        {
            new MetadataMatchTrack(1, "First Song", "Artist", null, 181000)
        };
        var unrelated = new[]
        {
            new MetadataMatchTrack(1, "Completely Different", "Artist", null, 240000)
        };

        var matchingConfidence = LibraryMetadataRepairService.CalculateMatchConfidence(
            local,
            matching,
            1,
            1);
        var unrelatedConfidence = LibraryMetadataRepairService.CalculateMatchConfidence(
            local,
            unrelated,
            60,
            1);

        Assert.True(matchingConfidence > unrelatedConfidence);
    }

    private static TrackRecord CreateTrack(string path, string title, string artist, string album) =>
        new()
        {
            Path = path,
            SourcePath = path,
            FileName = Path.GetFileName(path),
            ModifiedAt = 1,
            AddedAt = 1,
            Duration = 180,
            Title = title,
            Artist = artist,
            AlbumArtist = artist,
            Album = album,
            TrackNumber = 1
        };
}
