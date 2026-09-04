using System.Text.Json;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies deterministic current-version similarity features.</summary>
public sealed class SimilarityFeatureServiceTests
{
    /// <summary>Combines existing classification, rating, favourite, and history signals.</summary>
    [Fact]
    public void Create_NormalizesExistingLibrarySignals()
    {
        var profile = new SimilarityTrackProfile(
            42,
            "local",
            7,
            "A-Ha",
            "Pop; Synthpop",
            130,
            "Energetic; Happy",
            true,
            3,
            4,
            999,
            5,
            1_700_000_000);

        var vector = SimilarityFeatureService.Create(profile);

        Assert.Equal(SimilarityFeatureService.CurrentVersion, vector.Version);
        Assert.Equal("aha", vector.ArtistKey);
        Assert.NotEmpty(vector.GenreKeys);
        Assert.Equal(["energetic", "happy"], vector.MoodKeys);
        Assert.Equal(0.5, vector.Tempo);
        Assert.Equal(0.8, vector.PersonalAffinity);
        Assert.InRange(vector.CommunityAffinity, 0.79, 0.8);
        Assert.InRange(vector.Familiarity, 0.63, 0.64);
    }

    /// <summary>Prefers explicit and tempo-derived mood signals.</summary>
    [Fact]
    public void RankMood_PrefersTempoAndExplicitMoodWhileRetainingDiversity()
    {
        var candidates = new[]
        {
            Vector(1, "a", 0.2, ["calm"]),
            Vector(2, "b", 0.8, ["energetic"]),
            Vector(3, "c", 0.75, ["party"])
        };

        var calm = SimilarityFeatureService.RankMood(SimilarityMood.Calm, candidates, 3);
        var energetic = SimilarityFeatureService.RankMood(SimilarityMood.Energetic, candidates, 3);

        Assert.Equal(1, calm[0].Vector.TrackId);
        Assert.Contains(energetic[0].Vector.TrackId, new long[] { 2, 3 });
    }

    private static SimilarityFeatureVector Vector(long id, string artist, double tempo, IReadOnlyList<string> moods) =>
        new(SimilarityFeatureService.CurrentVersion, "local", id, id, artist, ["pop"], moods,
            tempo, 0, 0, 0, null);

    /// <summary>Keeps the schema version and source identity in serialized remote payloads.</summary>
    [Fact]
    public void Vector_RoundTripsWithVersionAndCredentialFreeSource()
    {
        var vector = SimilarityFeatureService.Create(new SimilarityTrackProfile(
            1, "orynivo:server-id", null, "Artist", null, null, null,
            false, 0, null, null, 0, null));

        var json = JsonSerializer.Serialize(vector);
        var restored = JsonSerializer.Deserialize<SimilarityFeatureVector>(json);

        Assert.NotNull(restored);
        Assert.Equal(SimilarityFeatureService.CurrentVersion, restored.Version);
        Assert.Equal("orynivo:server-id", restored.SourceKey);
        Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ranks close genre/tempo neighbours and enforces artist diversity.</summary>
    [Fact]
    public void RankSimilar_PrefersCloseFeaturesAndLimitsArtists()
    {
        var seed = Vector(1, "seed", ["pop"], 0.5);
        var candidates = new[]
        {
            Vector(2, "same", ["pop"], 0.51),
            Vector(3, "same", ["pop"], 0.52),
            Vector(4, "same", ["pop"], 0.53),
            Vector(5, "other", ["metal"], 0.95)
        };

        var matches = SimilarityFeatureService.RankSimilar(
            seed, candidates, maximumResults: 4, maximumPerArtist: 2);

        Assert.Equal([2L, 3L, 5L], matches.Select(match => match.Vector.TrackId));
        Assert.True(matches[0].Score > matches[^1].Score);
    }

    /// <summary>Uses cached acoustic proximity when metadata candidates are otherwise equivalent.</summary>
    [Fact]
    public void RankSimilar_PrefersCloserCachedAudioDescriptors()
    {
        var seed = Vector(1, "seed", ["pop"], 0.5) with { Energy = 0.2, Brightness = 0.3, Dynamics = 0.4 };
        var close = Vector(2, "close", ["pop"], 0.5) with { Energy = 0.21, Brightness = 0.31, Dynamics = 0.39 };
        var distant = Vector(3, "distant", ["pop"], 0.5) with { Energy = 0.9, Brightness = 0.9, Dynamics = 0.9 };

        var matches = SimilarityFeatureService.RankSimilar(seed, [distant, close]);

        Assert.Equal(2, matches[0].Vector.TrackId);
    }

    private static SimilarityFeatureVector Vector(
        long id,
        string artist,
        IReadOnlyList<string> genres,
        double tempo) => new(
            SimilarityFeatureService.CurrentVersion,
            "local",
            id,
            id,
            artist,
            genres,
            [],
            tempo,
            0,
            0,
            0,
            null);
}
