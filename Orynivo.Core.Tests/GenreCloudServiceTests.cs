using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies hierarchical genre aggregation and compact candidate selection.</summary>
public sealed class GenreCloudServiceTests
{
    /// <summary>Verifies recursive branch expansion and preservation of dynamic genre leaves.</summary>
    [Fact]
    public void ResolveLeafGenreKeys_ExpandsBranchesAndPreservesDynamicKeys()
    {
        var leaves = GenreCloudService.ResolveLeafGenreKeys(["edm", "unmapped:zeuhl"]);

        Assert.Contains("unmapped:zeuhl", leaves);
        Assert.DoesNotContain("edm", leaves);
        Assert.All(leaves.Where(key => key != "unmapped:zeuhl"), key =>
            Assert.False(GenreCloudService.BuildSnapshot([], key).Nodes.Any()));
    }
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

    /// <summary>Verifies that cloud nodes count distinct albums independently of their track totals.</summary>
    [Fact]
    public void BuildSnapshot_CountsDistinctAlbumsPerGenre()
    {
        var tracks = new[]
        {
            Facet(1, "Progressive Rock", 10),
            Facet(2, "Progressive Rock", 10),
            Facet(3, "Indie Rock", 11)
        };

        var rock = GenreCloudService.BuildSnapshot(tracks).Nodes.Single(node => node.Key == "rock");

        Assert.Equal(3, rock.TrackCount);
        Assert.Equal(2, rock.AlbumCount);
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

        var snapshot = GenreCloudService.BuildSnapshot(tracks, "edm", 5);

        Assert.Contains(snapshot.Nodes, node => node.Key == "drum-and-bass" && node.TrackCount == 20);
        Assert.Equal(5, snapshot.Candidates.Count);
    }

    /// <summary>Verifies that broad dance tags become a top-level category instead of Other.</summary>
    [Fact]
    public void BuildSnapshot_ClassifiesDanceAsTopLevelGenre()
    {
        var root = GenreCloudService.BuildSnapshot([Facet(1, "Dance")]);

        Assert.Equal(1, root.Nodes.Single(node => node.Key == "dance").TrackCount);
        Assert.DoesNotContain(root.Nodes, node => node.Key == "more-genres");
    }

    /// <summary>Verifies controlled recognition of descriptive compound genre tags.</summary>
    [Fact]
    public void BuildSnapshot_RecognizesCompoundSubgenres()
    {
        var metal = GenreCloudService.BuildSnapshot([Facet(1, "Melodic Death Metal")], "metal");
        var house = GenreCloudService.BuildSnapshot([Facet(2, "Organic Deep House")], "edm");

        Assert.Equal(1, metal.Nodes.Single(node => node.Key == "death-metal").TrackCount);
        Assert.Equal(1, house.Nodes.Single(node => node.Key == "house").TrackCount);
    }

    /// <summary>Verifies that a populated leaf retains its selection and candidates without inventing children.</summary>
    [Fact]
    public void BuildSnapshot_PreservesLeafSelectionWithoutChildNodes()
    {
        var snapshot = GenreCloudService.BuildSnapshot([Facet(1, "Deep House")], "deep-house");

        Assert.Equal("deep-house", snapshot.ParentKey);
        Assert.Equal(["dance", "edm", "house", "deep-house"], snapshot.BreadcrumbKeys);
        Assert.Empty(snapshot.Nodes);
        Assert.Single(snapshot.Candidates);
    }

    /// <summary>Verifies that a multi-parent genre contributes to every applicable top-level path.</summary>
    [Fact]
    public void BuildSnapshot_TraversesMultipleParentsWithoutDuplicateCounts()
    {
        var snapshot = GenreCloudService.BuildSnapshot([Facet(1, "Dance-Pop")]);

        Assert.Equal(1, snapshot.Nodes.Single(node => node.Key == "dance").TrackCount);
        Assert.Equal(1, snapshot.Nodes.Single(node => node.Key == "pop").TrackCount);
    }

    /// <summary>Verifies that unknown tags remain discoverable by their real name.</summary>
    [Fact]
    public void BuildSnapshot_PreservesUnknownGenresUnderMoreGenres()
    {
        var root = GenreCloudService.BuildSnapshot([Facet(1, "Zeuhl")]);
        var more = GenreCloudService.BuildSnapshot([Facet(1, "Zeuhl")], "more-genres");

        Assert.Equal(1, root.Nodes.Single(node => node.Key == "more-genres").TrackCount);
        Assert.Contains(more.Nodes, node => node.DisplayName == "Zeuhl" && node.TrackCount == 1);
    }

    private static TrackFacetInfo Facet(long id, string genre, long? albumId = null) =>
        new(id, false, genre, "flac", 1000, AlbumId: albumId);
}
