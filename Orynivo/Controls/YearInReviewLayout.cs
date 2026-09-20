using System.Globalization;
using Orynivo.Library;

namespace Orynivo.Controls;

/// <summary>One labelled value row of the year-in-review export.</summary>
/// <param name="Label">Row label.</param>
/// <param name="Value">Formatted value shown after the label.</param>
/// <param name="BarRatio">Proportional bar fill from zero through one.</param>
public sealed record YearInReviewRow(string Label, string Value, double BarRatio);

/// <summary>One titled section of the year-in-review export.</summary>
/// <param name="Title">Section title.</param>
/// <param name="Rows">Ordered rows of the section.</param>
public sealed record YearInReviewSection(string Title, IReadOnlyList<YearInReviewRow> Rows);

/// <summary>
/// Renderer-independent content model of the year-in-review export. The on-screen
/// card and the PDF exporter both consume it, so the two cannot drift apart.
/// </summary>
public static class YearInReviewLayout
{
    /// <summary>Builds the document title.</summary>
    /// <param name="summary">Year summary to describe.</param>
    /// <returns>A title such as <c>2026 in review</c>.</returns>
    public static string BuildTitle(YearInReviewSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{summary.Year.ToString(CultureInfo.InvariantCulture)} in review");
    }

    /// <summary>Builds the headline statistic lines.</summary>
    /// <param name="summary">Year summary to describe.</param>
    /// <returns>One line per headline value.</returns>
    public static IReadOnlyList<string> BuildHeadline(YearInReviewSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return
        [
            string.Create(CultureInfo.InvariantCulture, $"{summary.TotalListeningSeconds / 3600d:0.0} hours listened"),
            string.Create(CultureInfo.InvariantCulture, $"{summary.ActiveDays} active days")
        ];
    }

    /// <summary>
    /// Builds the monthly bar ratios with localized abbreviated month labels. The
    /// peak month always fills the bar completely.
    /// </summary>
    /// <param name="summary">Year summary to describe.</param>
    /// <returns>Twelve label and ratio pairs.</returns>
    public static IReadOnlyList<(string Label, double Ratio)> BuildMonthlyBars(YearInReviewSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var peak = summary.MonthlySeconds.Count == 0 ? 0d : summary.MonthlySeconds.Max();
        var bars = new List<(string Label, double Ratio)>(12);
        for (var month = 0; month < 12; month++)
        {
            var seconds = month < summary.MonthlySeconds.Count ? summary.MonthlySeconds[month] : 0d;
            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month + 1);
            bars.Add((name.Length > 3 ? name[..3] : name, peak <= 0 ? 0d : Math.Clamp(seconds / peak, 0d, 1d)));
        }
        return bars;
    }

    /// <summary>Builds the ordered leading sections, skipping empty ones.</summary>
    /// <param name="summary">Year summary to describe.</param>
    /// <returns>The leading genre, album, and artist sections.</returns>
    public static IReadOnlyList<YearInReviewSection> BuildSections(YearInReviewSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var sections = new List<YearInReviewSection>(3);
        AddSection(
            sections,
            "Top genres",
            summary.TopGenres.Select(entry => (entry.Genre, entry.Seconds)));
        AddSection(
            sections,
            "Top albums",
            summary.TopAlbums.Select(entry => (Label: FormatAlbum(entry), entry.Seconds)));
        AddSection(
            sections,
            "Top artists",
            summary.TopArtists.Select(entry => (entry.Name, entry.Seconds)));
        return sections;
    }

    /// <summary>Formats an album row label as <c>Title — Artist</c>.</summary>
    /// <param name="album">Album statistic to format.</param>
    /// <returns>The combined label.</returns>
    public static string FormatAlbum(TopAlbumStat album)
    {
        ArgumentNullException.ThrowIfNull(album);
        return string.IsNullOrWhiteSpace(album.Artist) ? album.Title : $"{album.Title} — {album.Artist}";
    }

    private static void AddSection(
        List<YearInReviewSection> sections,
        string title,
        IEnumerable<(string Label, double Seconds)> entries)
    {
        var values = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Label))
            .ToList();
        if (values.Count == 0)
            return;

        var peak = values.Max(entry => entry.Seconds);
        var rows = values
            .Select(entry => new YearInReviewRow(
                entry.Label,
                string.Create(CultureInfo.InvariantCulture, $"{entry.Seconds / 3600d:0.0} h"),
                peak <= 0 ? 0d : Math.Clamp(entry.Seconds / peak, 0d, 1d)))
            .ToList();
        sections.Add(new YearInReviewSection(title, rows));
    }
}
