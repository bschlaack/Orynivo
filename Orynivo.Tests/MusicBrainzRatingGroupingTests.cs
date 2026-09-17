using Orynivo;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the MusicBrainz rating-refresh row partitioning: valid recording
/// MBIDs are grouped case-insensitively while invalid or absent MBIDs stay
/// ungrouped.
/// </summary>
public sealed class MusicBrainzRatingGroupingTests
{
    private const string MbidOne = "11111111-1111-1111-1111-111111111111";
    private const string MbidTwo = "22222222-2222-2222-2222-222222222222";

    private sealed record Row(string Id, string? Mbid);

    private static (IReadOnlyList<IReadOnlyList<Row>> Groups, IReadOnlyList<Row> Ungrouped) Partition(
        params Row[] rows)
        => MusicBrainzRatingGrouping.Partition(rows, row => row.Mbid);

    /// <summary>Rows sharing an MBID are grouped case-insensitively.</summary>
    [Fact]
    public void Partition_GroupsByMbidCaseInsensitively()
    {
        var (groups, ungrouped) = Partition(
            new Row("a", MbidOne),
            new Row("b", MbidOne.ToUpperInvariant()),
            new Row("c", MbidTwo));

        Assert.Empty(ungrouped);
        Assert.Equal(2, groups.Count);
        Assert.Equal(["a", "b"], groups[0].Select(row => row.Id));
        Assert.Equal(["c"], groups[1].Select(row => row.Id));
    }

    /// <summary>Rows without a valid recording MBID are returned ungrouped.</summary>
    [Fact]
    public void Partition_ReturnsInvalidMbidsAsUngrouped()
    {
        var (groups, ungrouped) = Partition(
            new Row("a", null),
            new Row("b", "not-a-guid"),
            new Row("c", string.Empty),
            new Row("d", MbidOne));

        Assert.Single(groups);
        Assert.Equal(["d"], groups[0].Select(row => row.Id));
        Assert.Equal(["a", "b", "c"], ungrouped.Select(row => row.Id));
    }

    /// <summary>Groups keep the first-occurrence order of their MBID.</summary>
    [Fact]
    public void Partition_PreservesFirstOccurrenceOrder()
    {
        var (groups, _) = Partition(
            new Row("a", MbidTwo),
            new Row("b", MbidOne),
            new Row("c", MbidTwo));

        Assert.Equal(2, groups.Count);
        Assert.Equal(["a", "c"], groups[0].Select(row => row.Id));
        Assert.Equal(["b"], groups[1].Select(row => row.Id));
    }

    /// <summary>Empty input yields no groups and no ungrouped rows.</summary>
    [Fact]
    public void Partition_EmptyInput()
    {
        var (groups, ungrouped) = Partition();

        Assert.Empty(groups);
        Assert.Empty(ungrouped);
    }
}
