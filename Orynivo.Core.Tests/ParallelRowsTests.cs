using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the row splitting used by the parallel full-frame passes: the ranges must tile the
/// frame exactly once, small frames must stay on the calling thread, and the result must not
/// depend on the split.
/// </summary>
public sealed class ParallelRowsTests
{
    /// <summary>The ranges cover every row exactly once, in order.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(271)]
    [InlineData(1080)]
    public void For_CoversEveryRowExactlyOnce(int height)
    {
        var ranges = new List<(int From, int To)>();
        var lockObject = new object();

        ParallelRows.For(
            height,
            (_, from, to) =>
            {
                lock (lockObject)
                {
                    ranges.Add((from, to));
                }
            });

        var ordered = ranges.OrderBy(range => range.From).ToList();
        Assert.Equal(0, ordered[0].From);
        Assert.Equal(height, ordered[^1].To);
        for (var index = 1; index < ordered.Count; index++)
            Assert.Equal(ordered[index - 1].To, ordered[index].From);
        Assert.Equal(height, ordered.Sum(range => range.To - range.From));
    }

    /// <summary>A small frame is processed as one range on the calling thread.</summary>
    [Fact]
    public void For_KeepsSmallFramesSequential()
    {
        var ranges = new List<(int From, int To)>();

        ParallelRows.For(8, (_, from, to) => ranges.Add((from, to)));

        var range = Assert.Single(ranges);
        Assert.Equal(0, range.From);
        Assert.Equal(8, range.To);
    }

    /// <summary>A frame without rows runs nothing.</summary>
    [Fact]
    public void For_SkipsAnEmptyFrame()
    {
        var called = false;

        ParallelRows.For(0, (_, _, _) => called = true);

        Assert.False(called);
    }

    /// <summary>The parallel result is identical to a sequential loop.</summary>
    [Fact]
    public void For_ProducesTheSameResultAsASequentialLoop()
    {
        const int height = 300;
        const int width = 64;
        var expected = new int[height * width];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
                expected[(row * width) + column] = (row * 31) + column;
        }

        var actual = new int[height * width];
        ParallelRows.For(
            height,
            (_, from, to) =>
            {
                for (var row = from; row < to; row++)
                {
                    for (var column = 0; column < width; column++)
                        actual[(row * width) + column] = (row * 31) + column;
                }
            });

        Assert.Equal(expected, actual);
    }

    /// <summary>The worker count stays within a sane range.</summary>
    [Fact]
    public void WorkerCount_IsBounded()
    {
        Assert.InRange(ParallelRows.WorkerCount, 1, 8);
    }
}
