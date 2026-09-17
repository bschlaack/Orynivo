using Orynivo;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the pure genre-cloud recommendation scoring: affinity aggregation,
/// favorite weighting, and the deterministic tie-break variation.
/// </summary>
public sealed class GenreRecommendationScoreTests
{
    /// <summary>The tie-break variation is deterministic and within [0, 1).</summary>
    [Fact]
    public void StableVariation_IsDeterministicAndInRange()
    {
        var first = GenreRecommendationScore.StableVariation("server-1", 42);
        var second = GenreRecommendationScore.StableVariation("server-1", 42);

        Assert.Equal(first, second);
        Assert.InRange(first, 0d, 0.9999999d);
    }

    /// <summary>Different tracks produce different tie-break variations.</summary>
    [Fact]
    public void StableVariation_DiffersByTrack()
        => Assert.NotEqual(
            GenreRecommendationScore.StableVariation("server-1", 1),
            GenreRecommendationScore.StableVariation("server-1", 2));

    /// <summary>Only the candidate's own and related genres contribute affinity.</summary>
    [Fact]
    public void SumRelatedAffinity_SumsSelfAndRelatedOnly()
    {
        var weights = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["a"] = 10,
            ["b"] = 20
        };

        Assert.Equal(10d, GenreRecommendationScore.SumRelatedAffinity("a", weights));
    }

    /// <summary>Empty listening weights yield zero affinity.</summary>
    [Fact]
    public void SumRelatedAffinity_EmptyWeightsIsZero()
        => Assert.Equal(0d, GenreRecommendationScore.SumRelatedAffinity("a", new Dictionary<string, double>()));

    /// <summary>A higher listening affinity ranks a candidate higher.</summary>
    [Fact]
    public void Compute_HigherAffinityScoresHigher()
    {
        var withoutHistory = GenreRecommendationScore.Compute(
            "server-1", 1, "a", isFavorite: false, new Dictionary<string, double>());
        var withHistory = GenreRecommendationScore.Compute(
            "server-1", 1, "a", isFavorite: false, new Dictionary<string, double> { ["a"] = 1000 });

        Assert.True(withHistory > withoutHistory);
    }

    /// <summary>A favorite candidate receives a fixed score bonus.</summary>
    [Fact]
    public void Compute_FavoriteAddsFixedBonus()
    {
        var favorite = GenreRecommendationScore.Compute(
            "server-1", 1, "a", isFavorite: true, new Dictionary<string, double>());
        var regular = GenreRecommendationScore.Compute(
            "server-1", 1, "a", isFavorite: false, new Dictionary<string, double>());

        Assert.Equal(25d, favorite - regular, 6);
    }
}
