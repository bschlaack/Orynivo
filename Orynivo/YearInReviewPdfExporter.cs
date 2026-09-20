using Orynivo.Controls;
using Orynivo.Library;
using SkiaSharp;

namespace Orynivo;

/// <summary>
/// Renders a year-in-review summary as a bounded single-page A4 PDF through
/// SkiaSharp. It reads only the supplied summary and writes only the requested
/// file; no network access and no additional data collection.
/// </summary>
internal static class YearInReviewPdfExporter
{
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Margin = 48f;

    /// <summary>Writes the summary as a PDF file.</summary>
    /// <param name="summary">Year summary to export.</param>
    /// <param name="filePath">Target PDF path.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    internal static bool TryWrite(YearInReviewSummary summary, string filePath)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            using var document = SKDocument.CreatePdf(filePath);
            if (document is null)
                return false;
            using var canvas = document.BeginPage(PageWidth, PageHeight);
            Draw(canvas, summary);
            document.EndPage();
            document.Close();
            return true;
        }
        catch
        {
            // A failed export must never affect playback or the open dialog.
            return false;
        }
    }

    private static void Draw(SKCanvas canvas, YearInReviewSummary summary)
    {
        canvas.Clear(SKColors.White);
        using var titlePaint = CreatePaint(0x18, 0x19, 0x2E);
        using var bodyPaint = CreatePaint(0x33, 0x33, 0x44);
        using var mutedPaint = CreatePaint(0x6A, 0x6A, 0x7A);
        using var sectionPaint = CreatePaint(0x18, 0x19, 0x2E);
        using var titleFont = CreateFont(26);
        using var bodyFont = CreateFont(12);
        using var mutedFont = CreateFont(10);
        using var sectionFont = CreateFont(14, bold: true);
        using var barPaint = new SKPaint { Color = new SKColor(0x20, 0xD9, 0xE8), IsAntialias = true };
        using var trackPaint = new SKPaint { Color = new SKColor(0xEE, 0xEE, 0xF2), IsAntialias = true };

        var bottom = PageHeight - Margin;
        var y = Margin;
        canvas.DrawText(YearInReviewLayout.BuildTitle(summary), Margin, y, titleFont, titlePaint);
        y += 30;

        foreach (var line in YearInReviewLayout.BuildHeadline(summary))
        {
            canvas.DrawText(line, Margin, y, bodyFont, bodyPaint);
            y += 18;
        }

        y += 14;
        DrawMonthlyBars(canvas, summary, y, trackPaint, barPaint, mutedPaint, mutedFont);
        y += 134;

        var available = PageWidth - (Margin * 2);
        foreach (var section in YearInReviewLayout.BuildSections(summary))
        {
            if (y > bottom - 40)
                break;
            canvas.DrawText(section.Title, Margin, y, sectionFont, sectionPaint);
            y += 20;
            foreach (var row in section.Rows)
            {
                if (y > bottom)
                    break;
                var valueWidth = bodyFont.MeasureText(row.Value);
                canvas.DrawText(Truncate(row.Label, 56), Margin, y, bodyFont, bodyPaint);
                canvas.DrawText(row.Value, PageWidth - Margin - valueWidth, y, bodyFont, bodyPaint);

                var barWidth = available * 0.55f;
                canvas.DrawRect(new SKRect(Margin, y + 4, Margin + barWidth, y + 7), trackPaint);
                if (row.BarRatio > 0)
                {
                    canvas.DrawRect(
                        new SKRect(Margin, y + 4, Margin + (barWidth * (float)row.BarRatio), y + 7),
                        barPaint);
                }
                y += 22;
            }
            y += 10;
        }
    }

    private static void DrawMonthlyBars(
        SKCanvas canvas,
        YearInReviewSummary summary,
        float top,
        SKPaint trackPaint,
        SKPaint barPaint,
        SKPaint labelPaint,
        SKFont labelFont)
    {
        var bars = YearInReviewLayout.BuildMonthlyBars(summary);
        var available = PageWidth - (Margin * 2);
        var slot = available / bars.Count;
        var barWidth = slot * 0.62f;
        const float chartHeight = 90f;
        var baseline = top + chartHeight;

        for (var index = 0; index < bars.Count; index++)
        {
            var x = Margin + (index * slot) + ((slot - barWidth) / 2f);
            canvas.DrawRect(new SKRect(x, top, x + barWidth, baseline), trackPaint);
            var height = chartHeight * (float)bars[index].Ratio;
            if (height > 0)
                canvas.DrawRect(new SKRect(x, baseline - height, x + barWidth, baseline), barPaint);
            canvas.DrawText(bars[index].Label, x, baseline + 13, labelFont, labelPaint);
        }
    }

    /// <summary>Creates a solid, antialiased paint for the given colour.</summary>
    /// <param name="red">Red channel.</param>
    /// <param name="green">Green channel.</param>
    /// <param name="blue">Blue channel.</param>
    /// <returns>The configured paint.</returns>
    private static SKPaint CreatePaint(byte red, byte green, byte blue) => new()
    {
        Color = new SKColor(red, green, blue),
        IsAntialias = true
    };

    /// <summary>
    /// Creates the text font. SkiaSharp 3 moved the text properties from
    /// <see cref="SKPaint"/> to <see cref="SKFont"/>.
    /// </summary>
    /// <param name="size">Font size in points.</param>
    /// <param name="bold">Whether the text is emboldened.</param>
    /// <returns>The configured font.</returns>
    private static SKFont CreateFont(float size, bool bold = false) =>
        new(SKTypeface.Default, size) { Embolden = bold };

    private static string Truncate(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "…";
}
