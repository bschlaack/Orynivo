using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the synchronized lyric line selection used by the lyrics view and
/// the fullscreen karaoke view.
/// </summary>
public sealed class LyricLineSelectorTests
{
    private static readonly TimeSpan?[] Timed =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(20)];

    /// <summary>No lines or no timestamps never select a line.</summary>
    [Fact]
    public void FindActiveIndex_WithoutTimedLines_ReturnsNone()
    {
        Assert.Equal(-1, LyricLineSelector.FindActiveIndex(Array.Empty<TimeSpan?>(), time => time, TimeSpan.Zero));
        Assert.Equal(-1, LyricLineSelector.FindActiveIndex(
            new TimeSpan?[] { null, null },
            time => time,
            TimeSpan.FromMinutes(1)));
    }

    /// <summary>Positions before the first timestamp select nothing.</summary>
    [Fact]
    public void FindActiveIndex_BeforeFirstLine_ReturnsNone()
    {
        Assert.Equal(-1, LyricLineSelector.FindActiveIndex(Timed, time => time, TimeSpan.FromSeconds(4.999)));
        Assert.Equal(-1, LyricLineSelector.FindActiveIndex(Timed, time => time, TimeSpan.Zero));
    }

    /// <summary>An exact timestamp selects its own line.</summary>
    [Fact]
    public void FindActiveIndex_OnExactTimestamp_SelectsThatLine()
    {
        Assert.Equal(0, LyricLineSelector.FindActiveIndex(Timed, time => time, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, LyricLineSelector.FindActiveIndex(Timed, time => time, TimeSpan.FromSeconds(12)));
        Assert.Equal(2, LyricLineSelector.FindActiveIndex(Timed, time => time, TimeSpan.FromSeconds(20)));
    }

    /// <summary>A position between lines keeps the previous line highlighted.</summary>
    [Fact]
    public void FindActiveIndex_BetweenLines_KeepsPreviousLine()
    {
        Assert.Equal(0, LyricLineSelector.FindActiveIndex(Timed, time => time, TimeSpan.FromSeconds(11.9)));
        Assert.Equal(1, LyricLineSelector.FindActiveIndex(Timed, time => time, TimeSpan.FromSeconds(19.9)));
    }

    /// <summary>A position after the last line keeps the last line highlighted.</summary>
    [Fact]
    public void FindActiveIndex_AfterLastLine_KeepsLastLine()
    {
        Assert.Equal(2, LyricLineSelector.FindActiveIndex(Timed, time => time, TimeSpan.FromMinutes(10)));
    }

    /// <summary>The selector works on any line type through the timestamp accessor.</summary>
    [Fact]
    public void FindActiveIndex_UsesTimestampAccessor()
    {
        var lines = new[]
        {
            new Line("first", TimeSpan.FromSeconds(1)),
            new Line("second", TimeSpan.FromSeconds(8))
        };

        Assert.Equal(1, LyricLineSelector.FindActiveIndex(lines, line => line.Time, TimeSpan.FromSeconds(8)));
        Assert.Equal(0, LyricLineSelector.FindActiveIndex(lines, line => line.Time, TimeSpan.FromSeconds(7)));
    }

    private sealed record Line(string Text, TimeSpan? Time);
}
