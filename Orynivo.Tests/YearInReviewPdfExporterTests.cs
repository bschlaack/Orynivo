using System.Text;
using Orynivo;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies that the year-in-review PDF export really writes a PDF document, so
/// the SkiaSharp PDF backend is exercised and not only the pure layout.
/// </summary>
public sealed class YearInReviewPdfExporterTests
{
    /// <summary>A summary with data produces a non-empty PDF file.</summary>
    [Fact]
    public void TryWrite_WritesAPdfDocument()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"orynivo-year-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "review.pdf");
        try
        {
            var written = YearInReviewPdfExporter.TryWrite(CreateSummary(), path);

            Assert.True(written, "The exporter reported failure.");
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 1000, $"The PDF is suspiciously small ({bytes.Length} bytes).");
            Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An empty summary still produces a valid document.</summary>
    [Fact]
    public void TryWrite_WithoutListeningDataStillWritesAPdf()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"orynivo-year-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "empty.pdf");
        try
        {
            var summary = new YearInReviewSummary(2026, 0, 0, new double[12], [], [], []);

            Assert.True(YearInReviewPdfExporter.TryWrite(summary, path));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
