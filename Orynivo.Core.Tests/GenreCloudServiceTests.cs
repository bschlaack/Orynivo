using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies hierarchical genre aggregation and compact candidate selection.</summary>
public sealed class GenreCloudServiceTests
{
    /// <summary>Verifies that descendant genres contribute to their root count.</summary>
    [Fact]
    public void BuildSnapshot_AggregatesDescendantsIntoRootGenres()
    {
        var tracks = new[]
        {
            Facet(1, "Progressive Rock"),
            Facet(2, "Indie Rock"),
            Facet(3, "Techno")
        };

        var snapshot = GenreCloudService.BuildSnapshot(tracks);

        Assert.Equal(2, snapshot.Nodes.Single(node => node.Key == "rock").TrackCount);
        Assert.Equal(1, snapshot.Nodes.Single(node => node.Key == "electronic").TrackCount);
    }

    /// <summary>Verifies that selecting a root exposes only its populated direct children.</summary>
    [Fact]
    public void BuildSnapshot_DrillsIntoDirectChildren()
    {
        var snapshot = GenreCloudService.BuildSnapshot(
            new[] { Facet(1, "Prog Rock"), Facet(2, "Shoegaze"), Facet(3, "Rock") },
            "rock");

        Assert.Contains(snapshot.Nodes, node => node.Key == "progressive-rock" && node.TrackCount == 1);
        Assert.Contains(snapshot.Nodes, node => node.Key == "alternative-rock" && node.TrackCount == 1);
        Assert.Equal(["rock"], snapshot.BreadcrumbKeys);
        Assert.Equal(3, snapshot.Candidates.Count);
    }

    /// <summary>Verifies alias normalization and the requested candidate bound.</summary>
    [Fact]
    public void BuildSnapshot_NormalizesAliasesAndBoundsCandidates()
    {
        var tracks = Enumerable.Range(1, 20).Select(id => Facet(id, "D'n'B")).ToList();

        var snapshot = GenreCloudService.BuildSnapshot(tracks, "electronic", 5);

        Assert.Contains(snapshot.Nodes, node => node.Key == "drum-and-bass" && node.TrackCount == 20);
        Assert.Equal(5, snapshot.Candidates.Count);
    }

    private static TrackFacetInfo Facet(long id, string genre) => new(id, false, genre, "flac", 1000);
}
