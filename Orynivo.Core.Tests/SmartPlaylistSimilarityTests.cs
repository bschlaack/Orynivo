using System.Text.Json;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the similarity criterion of smart playlists, including backward
/// compatibility with criteria that predate the similarity fields.
/// </summary>
public sealed class SmartPlaylistSimilarityTests
{
    /// <summary>Criteria without a reference keep the configured ordering.</summary>
    [Fact]
    public void Resolve_WithoutReference_IgnoresSimilarityFeatures()
    {
        var criteria = new SmartPlaylistCriteria { SortOrder = SmartPlaylistSortOrder.Title };
        var candidates = new List<SmartPlaylistTrackInfo> { Track(1, "b"), Track(2, "a") };

        var result = criteria.Resolve(candidates, [Vector(1, "local"), Vector(2, "local")]);

        Assert.Equal([2L, 1L], result.Select(track => track.Id));
    }

    /// <summary>A configured reference orders neighbours by descending score and excludes the seed.</summary>
    [Fact]
    public void Resolve_WithReference_OrdersByDescendingScore()
    {
        var criteria = Reference(seedTrackId: 1);
        var candidates = new List<SmartPlaylistTrackInfo> { Track(1, "seed"), Track(2, "close"), Track(3, "far") };
        var vectors = new List<SimilarityFeatureVector>
        {
            Vector(1, "local", genres: ["rock"], tempo: 0.5),
            Vector(2, "local", genres: ["rock"], tempo: 0.5),
            Vector(3, "local", genres: ["ambient"], tempo: 0.05)
        };

        var result = criteria.Resolve(candidates, vectors);

        Assert.Equal([2L, 3L], result.Select(track => track.Id));
    }

    /// <summary>The minimum score removes weak neighbours.</summary>
    [Fact]
    public void Resolve_WithMinimumScore_FiltersWeakNeighbours()
    {
        var criteria = new SmartPlaylistCriteria
        {
            SimilaritySourceKey = "local",
            SimilarityTrackId = 1,
            SimilarityMinimumScore = 0.9
        };
        var candidates = new List<SmartPlaylistTrackInfo> { Track(1, "seed"), Track(2, "close"), Track(3, "far") };
        var vectors = new List<SimilarityFeatureVector>
        {
            Vector(1, "local", genres: ["rock"], tempo: 0.5),
            Vector(2, "local", genres: ["rock"], tempo: 0.5),
            Vector(3, "local", genres: ["ambient"], tempo: 0.05)
        };

        var result = criteria.Resolve(candidates, vectors);

        Assert.Equal([2L], result.Select(track => track.Id));
    }

    /// <summary>An unresolvable reference returns no tracks instead of an unrelated list.</summary>
    [Fact]
    public void Resolve_WithMissingSeed_ReturnsEmpty()
    {
        var criteria = Reference(seedTrackId: 99);
        var candidates = new List<SmartPlaylistTrackInfo> { Track(1, "seed"), Track(2, "other") };

        var result = criteria.Resolve(candidates, [Vector(1, "local"), Vector(2, "local")]);

        Assert.Empty(result);
    }

    /// <summary>The remaining criteria still filter similarity neighbours.</summary>
    [Fact]
    public void Resolve_WithReference_StillAppliesOtherCriteria()
    {
        var criteria = new SmartPlaylistCriteria
        {
            SimilaritySourceKey = "local",
            SimilarityTrackId = 1,
            FavoritesOnly = true
        };
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Track(1, "seed", favorite: true),
            Track(2, "close", favorite: false),
            Track(3, "far", favorite: true)
        };
        var vectors = new List<SimilarityFeatureVector>
        {
            Vector(1, "local", genres: ["rock"], tempo: 0.5),
            Vector(2, "local", genres: ["rock"], tempo: 0.5),
            Vector(3, "local", genres: ["ambient"], tempo: 0.05)
        };

        var result = criteria.Resolve(candidates, vectors);

        Assert.Equal([3L], result.Select(track => track.Id));
    }

    /// <summary>The result limit caps the ordered neighbour list.</summary>
    [Fact]
    public void Resolve_WithReference_RespectsResultLimit()
    {
        var criteria = new SmartPlaylistCriteria
        {
            SimilaritySourceKey = "local",
            SimilarityTrackId = 1,
            ResultLimit = 1
        };
        var candidates = new List<SmartPlaylistTrackInfo> { Track(1, "seed"), Track(2, "close"), Track(3, "far") };
        var vectors = new List<SimilarityFeatureVector>
        {
            Vector(1, "local", genres: ["rock"], tempo: 0.5),
            Vector(2, "local", genres: ["rock"], tempo: 0.5),
            Vector(3, "local", genres: ["ambient"], tempo: 0.05)
        };

        var result = criteria.Resolve(candidates, vectors);

        Assert.Equal([2L], result.Select(track => track.Id));
    }

    /// <summary>Missing similarity data returns no tracks instead of the complete library.</summary>
    [Fact]
    public void Resolve_WithReferenceAndNullFeatures_ReturnsEmpty()
    {
        var criteria = Reference(seedTrackId: 1);
        var candidates = new List<SmartPlaylistTrackInfo> { Track(1, "seed"), Track(2, "other") };

        Assert.Empty(criteria.Resolve(candidates, null));
    }

    /// <summary>The overload without vectors never ignores a similarity reference.</summary>
    [Fact]
    public void Resolve_PlainOverloadWithReference_ReturnsEmpty()
    {
        var criteria = Reference(seedTrackId: 1);
        var candidates = new List<SmartPlaylistTrackInfo> { Track(1, "seed"), Track(2, "other") };

        Assert.Empty(criteria.Resolve(candidates));
    }

    /// <summary>The similarity fields survive a JSON round trip.</summary>
    [Fact]
    public void Serialization_RoundTripsSimilarityFields()
    {
        var criteria = new SmartPlaylistCriteria
        {
            SimilaritySourceKey = "orynivo:7",
            SimilarityTrackId = 42,
            SimilarityMinimumScore = 0.4
        };

        var json = JsonSerializer.Serialize(criteria);
        var restored = JsonSerializer.Deserialize<SmartPlaylistCriteria>(json);

        Assert.NotNull(restored);
        Assert.Equal("orynivo:7", restored.SimilaritySourceKey);
        Assert.Equal(42, restored.SimilarityTrackId);
        Assert.Equal(0.4, restored.SimilarityMinimumScore);
    }

    /// <summary>Criteria persisted before the similarity fields still deserialize.</summary>
    [Fact]
    public void Serialization_LegacyCriteriaWithoutSimilarityFieldsRemainValid()
    {
        const string legacy = """{"FavoritesOnly":true,"SortOrder":2,"ResultLimit":10}""";

        var restored = JsonSerializer.Deserialize<SmartPlaylistCriteria>(legacy);

        Assert.NotNull(restored);
        Assert.True(restored.FavoritesOnly);
        Assert.Equal(SmartPlaylistSortOrder.LastPlayedNewest, restored.SortOrder);
        Assert.Null(restored.SimilaritySourceKey);
        Assert.Null(restored.SimilarityTrackId);
    }

    private static SmartPlaylistCriteria Reference(long seedTrackId) => new()
    {
        SimilaritySourceKey = "local",
        SimilarityTrackId = seedTrackId
    };

    private static SmartPlaylistTrackInfo Track(long id, string title, bool favorite = false) =>
        new(id, favorite, "rock", "flac", 1000, 2000, "Artist", "Album", 200, 1, 0, null, title);

    private static SimilarityFeatureVector Vector(
        long id,
        string sourceKey,
        IReadOnlyList<string>? genres = null,
        double? tempo = null) =>
        new(
            SimilarityFeatureService.CurrentVersion,
            sourceKey,
            id,
            id,
            "artist",
            genres ?? [],
            [],
            tempo,
            0.5,
            0,
            0,
            null);
}
