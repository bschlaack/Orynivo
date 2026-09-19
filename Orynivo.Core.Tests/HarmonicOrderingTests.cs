using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the deterministic greedy Camelot-wheel ordering used for harmonic
/// mixing batches.
/// </summary>
public sealed class HarmonicOrderingTests
{
    /// <summary>A batch is walked through its nearest wheel neighbours.</summary>
    [Fact]
    public void Order_WalksNearestWheelNeighbours()
    {
        var items = new[] { new Entry(1, "8A"), new Entry(2, "2B"), new Entry(3, "9A"), new Entry(4, "8B") };

        var ordered = HarmonicOrdering.Order(items, item => Parse(item.Key));

        Assert.Equal([1, 3, 4, 2], ordered.Select(item => item.Id));
    }

    /// <summary>Items without a key keep their relative order after the chain.</summary>
    [Fact]
    public void Order_AppendsKeylessItemsInOriginalOrder()
    {
        var items = new[] { new Entry(1, "8A"), new Entry(2, null), new Entry(3, "9A"), new Entry(4, null) };

        var ordered = HarmonicOrdering.Order(items, item => Parse(item.Key));

        Assert.Equal([1, 3, 2, 4], ordered.Select(item => item.Id));
    }

    /// <summary>The input is returned unchanged when fewer than two items carry a key.</summary>
    [Fact]
    public void Order_WithoutEnoughKeys_KeepsInputOrder()
    {
        var single = new[] { new Entry(1, "8A"), new Entry(2, null), new Entry(3, null) };
        var none = new[] { new Entry(1, null), new Entry(2, null) };

        Assert.Equal([1, 2, 3], HarmonicOrdering.Order(single, item => Parse(item.Key)).Select(item => item.Id));
        Assert.Equal([1, 2], HarmonicOrdering.Order(none, item => Parse(item.Key)).Select(item => item.Id));
    }

    /// <summary>A relative major/minor pair is treated as the closest neighbour.</summary>
    [Fact]
    public void Order_PrefersRelativeKeyOverDistantKey()
    {
        var items = new[] { new Entry(1, "8A"), new Entry(2, "4B"), new Entry(3, "8B") };

        var ordered = HarmonicOrdering.Order(items, item => Parse(item.Key));

        Assert.Equal([1, 3, 2], ordered.Select(item => item.Id));
    }

    /// <summary>Repeated ordering of the same input is stable.</summary>
    [Fact]
    public void Order_IsDeterministic()
    {
        var items = new[] { new Entry(1, "8A"), new Entry(2, "2B"), new Entry(3, "9A"), new Entry(4, "8B"), new Entry(5, "12A") };

        var first = HarmonicOrdering.Order(items, item => Parse(item.Key)).Select(item => item.Id).ToList();
        var second = HarmonicOrdering.Order(items, item => Parse(item.Key)).Select(item => item.Id).ToList();

        Assert.Equal(first, second);
    }

    /// <summary>Empty and single-item inputs are returned unchanged.</summary>
    [Fact]
    public void Order_WithTrivialInput_ReturnsInput()
    {
        Assert.Empty(HarmonicOrdering.Order(Array.Empty<Entry>(), item => Parse(item.Key)));
        Assert.Equal([1], HarmonicOrdering.Order([new Entry(1, "8A")], item => Parse(item.Key)).Select(item => item.Id));
    }

    private static CamelotKey? Parse(string? value) =>
        CamelotKey.TryParse(value, out var key) ? key : null;

    private sealed record Entry(int Id, string? Key);
}
