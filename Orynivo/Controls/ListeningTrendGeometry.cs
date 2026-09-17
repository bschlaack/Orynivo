using System.Globalization;
using System.Text;
using Avalonia;

namespace Orynivo.Controls;

/// <summary>
/// Pure geometry helpers for the Dashboard listening-trend chart: smooth curve
/// construction, point formatting, and axis rounding.
/// </summary>
internal static class ListeningTrendGeometry
{
    /// <summary>
    /// Builds a Catmull-Rom-derived cubic Bézier path through the supplied points.
    /// Each segment's two control-point Y values are clamped to the segment's real
    /// endpoint range so smoothing can never invent a peak or trough outside the
    /// measured values.
    /// </summary>
    /// <param name="points">Ordered trend points in chart coordinates.</param>
    /// <returns>An SVG-style path string starting with a move command.</returns>
    internal static string BuildSmoothCurve(IReadOnlyList<Point> points)
    {
        var path = new StringBuilder($"M {FormatPoint(points[0])} ");
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = i > 0 ? points[i - 1] : points[i];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i + 2 < points.Count ? points[i + 2] : p2;
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
            var minimumY = Math.Min(p1.Y, p2.Y);
            var maximumY = Math.Max(p1.Y, p2.Y);
            c1 = new Point(c1.X, Math.Clamp(c1.Y, minimumY, maximumY));
            c2 = new Point(c2.X, Math.Clamp(c2.Y, minimumY, maximumY));
            path.Append($"C {FormatPoint(c1)} {FormatPoint(c2)} {FormatPoint(p2)} ");
        }
        return path.ToString();
    }

    /// <summary>Formats a point as an invariant-culture <c>x,y</c> pair.</summary>
    /// <param name="point">The point to format.</param>
    /// <returns>The formatted coordinate pair.</returns>
    internal static string FormatPoint(Point point) =>
        $"{point.X.ToString("0.###", CultureInfo.InvariantCulture)},{point.Y.ToString("0.###", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Returns a rounded axis maximum that is strictly above the supplied peak so the
    /// smoothed curve retains headroom.
    /// </summary>
    /// <param name="peakMinutes">The largest measured value in minutes.</param>
    /// <returns>A rounded axis ceiling strictly greater than <paramref name="peakMinutes"/>.</returns>
    internal static double NiceAxisMaximum(double peakMinutes)
    {
        var roughStep = Math.Max(1, peakMinutes / 4);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
        var normalized = roughStep / magnitude;
        var step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;
        var maximum = Math.Ceiling(peakMinutes / step) * step;
        if (maximum <= peakMinutes + 0.0001)
            maximum += step;
        return maximum;
    }
}
