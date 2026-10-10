using Orynivo.Library;
using Xunit;

namespace Orynivo.Tests;

/// <summary>Protects search source gating, facet semantics and captured selections against mutation.</summary>
public sealed class TrackFacetFilterSnapshotTests
{
    /// <summary>Later UI changes cannot alter the captured filter, including case-insensitive genre/source matching.</summary>
    [Fact]
    public void Capture_RemainsIndependentAndMatchesEveryDimension()
    {
        var genres = new List<string> { "Jazz" };
        var sources = new List<string> { "server:test" };
        var filters = new TrackFacetFilterSnapshot(true, genres, ["FLAC"], [900], sources);
        genres.Clear();
        sources.Clear();
        Assert.True(filters.HasTrackFilters);
        Assert.False(filters.IncludesSource("local"));
        Assert.True(filters.IncludesSource("SERVER:TEST"));
        var matching = new TrackFacetInfo(1, true, "Rock; jazz", "flac", 900, "server:test");
        Assert.True(filters.Matches(matching));
        Assert.False(filters.Matches(matching with { IsFavorite = false }));
        Assert.False(filters.Matches(matching with { Genre = "Rock" }));
        Assert.False(filters.Matches(matching with { Format = null }));
        Assert.False(filters.Matches(matching with { Bitrate = null }));
        Assert.False(filters.Matches(matching with { SourceKey = "local" }));
    }

    /// <summary>No selected source means all sources, without a metadata-facet scan.</summary>
    [Fact]
    public void EmptySelections_IncludeEverySourceAndTrack()
    {
        var filters = new TrackFacetFilterSnapshot(false, [], [], [], []);
        Assert.False(filters.HasTrackFilters);
        Assert.True(filters.IncludesSource("local"));
        Assert.True(filters.IncludesSource("server:one"));
        Assert.True(filters.Matches(new TrackFacetInfo(1, false, null, null, null, "local")));
    }
}
