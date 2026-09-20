using Orynivo.Controls;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the renderer-independent year-in-review content model shared by the
/// on-screen card and the PDF export.
/// </summary>
public sealed class YearInReviewLayoutTests
{
    /// <summary>The title names the year.</summary>
    [Fact]
    public void BuildTitle_NamesTheYear()
    {
        Assert.Equal("2026 in review", YearInReviewLayout.BuildTitle(CreateSummary()));
    }

    /// <summary>The headline reports listened hours and active days.</summary>
    [Fact]
    public void BuildHeadline_ReportsHoursAndActiveDays()
    {
        var headline = YearInReviewLayout.BuildHeadline(CreateSummary());

        Assert.Equal(2, headline.Count);
        Assert.Contains("2.5", headline[0]);
        Assert.Contains("12", headline[1]);
    }

    /// <summary>The monthly bars scale to the peak month and stay empty for silent months.</summary>
    [Fact]
    public void BuildMonthlyBars_ScalesToThePeakMonth()
    {
        var summary = CreateSummary() with
        {
            MonthlySeconds = [0, 1800, 0, 0, 900, 0, 0, 0, 0, 0, 0, 0]
        };

        var bars = YearInReviewLayout.BuildMonthlyBars(summary);

        Assert.Equal(12, bars.Count);
        Assert.Equal(1d, bars[1].Ratio);
        Assert.Equal(0.5d, bars[4].Ratio);
        Assert.Equal(0d, bars[0].Ratio);
        Assert.All(bars, bar => Assert.False(string.IsNullOrWhiteSpace(bar.Label)));
    }

    /// <summary>Sections keep the leading order and scale their bars to the top entry.</summary>
    [Fact]
    public void BuildSections_KeepsLeadingOrderAndScalesBars()
    {
        var sections = YearInReviewLayout.BuildSections(CreateSummary());

        Assert.Equal(3, sections.Count);
        Assert.Equal("Top genres", sections[0].Title);
        Assert.Equal("Rock", sections[0].Rows[0].Label);
        Assert.Equal(1d, sections[0].Rows[0].BarRatio);
        Assert.Equal(0.5d, sections[0].Rows[1].BarRatio);
        Assert.Equal("Top albums", sections[1].Title);
        Assert.Equal("Album — Artist", sections[1].Rows[0].Label);
        Assert.Equal("Top artists", sections[2].Title);
    }

    /// <summary>Empty sections are omitted instead of rendering a bare heading.</summary>
    [Fact]
    public void BuildSections_SkipsEmptySections()
    {
        var summary = CreateSummary() with { TopAlbums = [], TopArtists = [] };

        var sections = YearInReviewLayout.BuildSections(summary);

        Assert.Single(sections);
        Assert.Equal("Top genres", sections[0].Title);
    }

    /// <summary>Album labels omit a missing artist.</summary>
    [Fact]
    public void FormatAlbum_OmitsMissingArtist()
    {
        Assert.Equal("Album", YearInReviewLayout.FormatAlbum(new TopAlbumStat("Album", string.Empty, 60, null, null, null, null, null)));
        Assert.Equal("Album — Artist", YearInReviewLayout.FormatAlbum(new TopAlbumStat("Album", "Artist", 60, null, null, null, null, null)));
    }

    private static YearInReviewSummary CreateSummary() => new(
        Year: 2026,
        TotalListeningSeconds: 9000,
        ActiveDays: 12,
        MonthlySeconds: [0, 3600, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        TopGenres: [("Rock", 3600), ("Jazz", 1800)],
        TopAlbums: [new TopAlbumStat("Album", "Artist", 3600, null, null, null, null, null)],
        TopArtists: [new TopArtistStat("Artist", 3600, null, null, null)]);
}
