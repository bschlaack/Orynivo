using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the deterministic mood/activity preset ranking over cached acoustic
/// descriptors, tempo, explicit mood, and preference signals.
/// </summary>
public sealed class SimilarityPresetTests
{
    /// <summary>Workout favours the fast, loud and bright track.</summary>
    [Fact]
    public void RankPreset_Workout_PrefersFastAndLoud()
    {
        var ranked = SimilarityFeatureService.RankPreset(
            SimilarityPreset.Workout,
            [Quiet(), Loud()],
            maximumResults: 10);

        Assert.Equal([2L], ranked.Take(1).Select(match => match.Vector.TrackId));
    }

    /// <summary>Wind down favours the slow, soft track.</summary>
    [Fact]
    public void RankPreset_WindDown_PrefersSlowAndSoft()
    {
        var ranked = SimilarityFeatureService.RankPreset(
            SimilarityPreset.WindDown,
            [Quiet(), Loud()],
            maximumResults: 10);

        Assert.Equal([1L], ranked.Take(1).Select(match => match.Vector.TrackId));
    }

    /// <summary>Focus favours the steady, low-energy track.</summary>
    [Fact]
    public void RankPreset_Focus_PrefersLowEnergy()
    {
        var ranked = SimilarityFeatureService.RankPreset(
            SimilarityPreset.Focus,
            [Quiet(), Loud()],
            maximumResults: 10);

        Assert.Equal([1L], ranked.Take(1).Select(match => match.Vector.TrackId));
    }

    /// <summary>Explicit mood tags break a tie between otherwise equal tracks.</summary>
    [Fact]
    public void RankPreset_ExplicitMoodBreaksTie()
    {
        var tagged = Vector(1, "artist-a", tempo: 0.8, energy: 0.8, mood: "workout");
        var untagged = Vector(2, "artist-b", tempo: 0.8, energy: 0.8);

        var ranked = SimilarityFeatureService.RankPreset(
            SimilarityPreset.Workout,
            [untagged, tagged],
            maximumResults: 10);

        Assert.Equal([1L], ranked.Take(1).Select(match => match.Vector.TrackId));
    }

    /// <summary>Repeated ranking of the same input returns an identical order.</summary>
    [Fact]
    public void RankPreset_IsDeterministic()
    {
        SimilarityFeatureVector[] candidates = [Quiet(), Loud(), Vector(3, "artist-c", 0.5, 0.5)];

        var first = SimilarityFeatureService.RankPreset(SimilarityPreset.Focus, candidates, 10)
            .Select(match => match.Vector.TrackId).ToList();
        var second = SimilarityFeatureService.RankPreset(SimilarityPreset.Focus, candidates, 10)
            .Select(match => match.Vector.TrackId).ToList();

        Assert.Equal(first, second);
    }

    /// <summary>Artist diversity limits still apply to preset ranking.</summary>
    [Fact]
    public void RankPreset_RespectsArtistDiversity()
    {
        SimilarityFeatureVector[] candidates =
        [
            Vector(1, "same-artist", 0.8, 0.8),
            Vector(2, "same-artist", 0.8, 0.8),
            Vector(3, "other-artist", 0.4, 0.4)
        ];

        var ranked = SimilarityFeatureService.RankPreset(
            SimilarityPreset.Workout,
            candidates,
            maximumResults: 10,
            maximumPerArtist: 1,
            maximumPerAlbum: 10);

        Assert.Equal([1L, 3L], ranked.Select(match => match.Vector.TrackId));
    }

    /// <summary>Tracks without cached descriptors still rank through tempo and preferences.</summary>
    [Fact]
    public void RankPreset_WithoutDescriptors_StillRanks()
    {
        var noDescriptors = Vector(1, "artist-a", tempo: 0.8, energy: null);
        var slow = Vector(2, "artist-b", tempo: 0.2, energy: null);

        var ranked = SimilarityFeatureService.RankPreset(
            SimilarityPreset.Workout,
            [slow, noDescriptors],
            maximumResults: 10);

        Assert.Equal(2, ranked.Count);
        Assert.Equal([1L], ranked.Take(1).Select(match => match.Vector.TrackId));
    }

    /// <summary>An empty candidate set produces no matches.</summary>
    [Fact]
    public void RankPreset_WithoutCandidates_ReturnsEmpty()
    {
        Assert.Empty(SimilarityFeatureService.RankPreset(SimilarityPreset.Focus, [], 10));
    }

    private static SimilarityFeatureVector Quiet() =>
        Vector(1, "artist-a", tempo: 0.2, energy: 0.2, brightness: 0.25, dynamics: 0.3);

    private static SimilarityFeatureVector Loud() =>
        Vector(2, "artist-b", tempo: 0.85, energy: 0.85, brightness: 0.8, dynamics: 0.7);

    private static SimilarityFeatureVector Vector(
        long id,
        string artist,
        double? tempo,
        double? energy,
        double? brightness = null,
        double? dynamics = null,
        string? mood = null) =>
        new(
            SimilarityFeatureService.CurrentVersion,
            "local",
            id,
            id,
            artist,
            ["pop"],
            mood is null ? [] : [mood],
            tempo,
            0.5,
            0,
            0,
            null,
            energy,
            brightness,
            dynamics);
}
