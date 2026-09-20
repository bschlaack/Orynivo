using Orynivo;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies that the smart-playlist editor carries the reference similarity
/// criterion across a save instead of silently dropping it.
/// </summary>
public sealed class SmartPlaylistCriteriaEditingTests
{
    /// <summary>A loaded reference survives an ordinary save.</summary>
    [Fact]
    public void ResolveSimilarityReference_KeepsLoadedReference()
    {
        var initial = new SmartPlaylistCriteria
        {
            SimilaritySourceKey = "local",
            SimilarityTrackId = 42,
            SimilarityMinimumScore = 0.3
        };

        var resolved = SmartPlaylistCriteriaEditing.ResolveSimilarityReference(initial, false, 0.55);

        Assert.Equal("local", resolved.SourceKey);
        Assert.Equal(42, resolved.TrackId);
        Assert.Equal(0.55, resolved.MinimumScore);
    }

    /// <summary>A remote provider key survives an ordinary save unchanged.</summary>
    [Fact]
    public void ResolveSimilarityReference_KeepsRemoteProviderKey()
    {
        var initial = new SmartPlaylistCriteria
        {
            SimilaritySourceKey = "server:abc",
            SimilarityTrackId = 7
        };

        var resolved = SmartPlaylistCriteriaEditing.ResolveSimilarityReference(initial, false, null);

        Assert.Equal("server:abc", resolved.SourceKey);
        Assert.Equal(7, resolved.TrackId);
        Assert.Null(resolved.MinimumScore);
    }

    /// <summary>Clearing the reference removes all three fields.</summary>
    [Fact]
    public void ResolveSimilarityReference_ClearedReferenceIsDropped()
    {
        var initial = new SmartPlaylistCriteria
        {
            SimilaritySourceKey = "local",
            SimilarityTrackId = 42,
            SimilarityMinimumScore = 0.3
        };

        var resolved = SmartPlaylistCriteriaEditing.ResolveSimilarityReference(initial, true, 0.9);

        Assert.Null(resolved.SourceKey);
        Assert.Null(resolved.TrackId);
        Assert.Null(resolved.MinimumScore);
    }

    /// <summary>Criteria without a reference stay without one.</summary>
    [Fact]
    public void ResolveSimilarityReference_WithoutReferenceStaysEmpty()
    {
        var resolved = SmartPlaylistCriteriaEditing.ResolveSimilarityReference(
            new SmartPlaylistCriteria(),
            false,
            0.4);

        Assert.Null(resolved.SourceKey);
        Assert.Null(resolved.TrackId);
        Assert.Null(resolved.MinimumScore);
    }

    /// <summary>An incomplete reference is not persisted.</summary>
    [Fact]
    public void ResolveSimilarityReference_IncompleteReferenceIsDropped()
    {
        var initial = new SmartPlaylistCriteria { SimilaritySourceKey = "local" };

        var resolved = SmartPlaylistCriteriaEditing.ResolveSimilarityReference(initial, false, 0.4);

        Assert.Null(resolved.SourceKey);
        Assert.Null(resolved.TrackId);
    }

    /// <summary>A picked reference replaces the loaded one.</summary>
    [Fact]
    public void ResolveSimilarityReference_PickedReferenceReplacesLoadedOne()
    {
        var initial = new SmartPlaylistCriteria
        {
            SimilaritySourceKey = "local",
            SimilarityTrackId = 42
        };

        var resolved = SmartPlaylistCriteriaEditing.ResolveSimilarityReference(
            initial,
            false,
            0.3,
            ("server:abc", 7));

        Assert.Equal("server:abc", resolved.SourceKey);
        Assert.Equal(7, resolved.TrackId);
        Assert.Equal(0.3, resolved.MinimumScore);
    }

    /// <summary>A picked reference also applies when the loaded criteria had none.</summary>
    [Fact]
    public void ResolveSimilarityReference_PickedReferenceAppliesWithoutLoadedOne()
    {
        var resolved = SmartPlaylistCriteriaEditing.ResolveSimilarityReference(
            new SmartPlaylistCriteria(),
            false,
            null,
            ("local", 11));

        Assert.Equal("local", resolved.SourceKey);
        Assert.Equal(11, resolved.TrackId);
    }

    /// <summary>An explicit removal wins over a previously picked reference.</summary>
    [Fact]
    public void ResolveSimilarityReference_ClearedReferenceWinsOverPick()
    {
        var resolved = SmartPlaylistCriteriaEditing.ResolveSimilarityReference(
            new SmartPlaylistCriteria { SimilaritySourceKey = "local", SimilarityTrackId = 1 },
            true,
            0.5,
            ("local", 11));

        Assert.Null(resolved.SourceKey);
        Assert.Null(resolved.TrackId);
        Assert.Null(resolved.MinimumScore);
    }
}
