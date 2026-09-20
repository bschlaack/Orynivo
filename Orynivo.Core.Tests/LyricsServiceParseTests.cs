using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the LRC parser, including the enhanced-LRC word timestamps used by the
/// karaoke view.
/// </summary>
public sealed class LyricsServiceParseTests
{
    /// <summary>Plain synchronized lines keep their text and report no words.</summary>
    [Fact]
    public void ParseLrc_WithoutWordMarkers_ReturnsPlainLines()
    {
        var lines = LyricsService.ParseLrc("[00:05.00]Hello world\n[00:10.00]Second line");

        Assert.Equal(2, lines.Count);
        Assert.Equal("Hello world", lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(5), lines[0].Time);
        Assert.Empty(lines[0].Words);
    }

    /// <summary>Enhanced-LRC word markers are extracted and removed from the text.</summary>
    [Fact]
    public void ParseLrc_WithWordMarkers_ExtractsWordsAndStripsMarkers()
    {
        var lines = LyricsService.ParseLrc("[00:12.00]<00:12.00>Hello <00:12.50>big <00:13.10>world");

        var line = Assert.Single(lines);
        Assert.Equal("Hello big world", line.Text);
        Assert.Equal(TimeSpan.FromSeconds(12), line.Time);
        Assert.Equal(3, line.Words.Count);
        Assert.Equal("Hello", line.Words[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(12), line.Words[0].Time);
        Assert.Equal("big", line.Words[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(12.5), line.Words[1].Time);
        Assert.Equal("world", line.Words[2].Text);
        Assert.Equal(TimeSpan.FromSeconds(13.1), line.Words[2].Time);
    }

    /// <summary>A line without word markers keeps its words empty even next to enhanced lines.</summary>
    [Fact]
    public void ParseLrc_MixesPlainAndEnhancedLines()
    {
        var lines = LyricsService.ParseLrc(
            "[00:01.00]<00:01.00>First\n[00:05.00]Second\n[00:09.00]<00:09.00>Third <00:09.40>line");

        Assert.Equal(3, lines.Count);
        Assert.NotEmpty(lines[0].Words);
        Assert.Empty(lines[1].Words);
        Assert.Equal("Second", lines[1].Text);
        Assert.Equal(2, lines[2].Words.Count);
    }

    /// <summary>Lines are returned in chronological order regardless of file order.</summary>
    [Fact]
    public void ParseLrc_OrdersLinesByTime()
    {
        var lines = LyricsService.ParseLrc("[00:20.00]Later\n[00:02.00]Earlier");

        Assert.Equal("Earlier", lines[0].Text);
        Assert.Equal("Later", lines[1].Text);
    }

    /// <summary>A line made only of markers reports no words.</summary>
    [Fact]
    public void ParseLrc_WithOnlyMarkers_ReportsNoWords()
    {
        var lines = LyricsService.ParseLrc("[00:12.00]<00:12.00> <00:12.50>");

        var line = Assert.Single(lines);
        Assert.Equal(string.Empty, line.Text);
        Assert.Empty(line.Words);
    }

    /// <summary>Empty or whitespace input returns no lines.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no timestamps here")]
    public void ParseLrc_WithoutTimestamps_ReturnsEmpty(string? lyrics)
    {
        Assert.Empty(LyricsService.ParseLrc(lyrics));
    }

    /// <summary>The word-level selector picks the last word at or before the position.</summary>
    [Fact]
    public void WordSelection_UsesTheSharedSelector()
    {
        var line = Assert.Single(LyricsService.ParseLrc("[00:12.00]<00:12.00>Hello <00:12.50>big <00:13.10>world"));

        Assert.Equal(0, LyricLineSelector.FindActiveIndex(line.Words, word => word.Time, TimeSpan.FromSeconds(12.2)));
        Assert.Equal(1, LyricLineSelector.FindActiveIndex(line.Words, word => word.Time, TimeSpan.FromSeconds(12.6)));
        Assert.Equal(2, LyricLineSelector.FindActiveIndex(line.Words, word => word.Time, TimeSpan.FromSeconds(30)));
        Assert.Equal(-1, LyricLineSelector.FindActiveIndex(line.Words, word => word.Time, TimeSpan.FromSeconds(11)));
    }
}
