using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Orynivo.Controls;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the pure Dashboard listening-trend geometry: axis rounding, point
/// formatting, and smoothed curve construction with clamped control points.
/// </summary>
public sealed class ListeningTrendGeometryTests
{
    /// <summary>The rounded axis maximum is strictly above the peak.</summary>
    /// <param name="peak">Measured peak value.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(23)]
    [InlineData(147)]
    [InlineData(1234)]
    public void NiceAxisMaximum_IsStrictlyAbovePeak(double peak)
        => Assert.True(ListeningTrendGeometry.NiceAxisMaximum(peak) > peak);

    /// <summary>Known peaks round up to the expected nice axis ceilings.</summary>
    /// <param name="peak">Measured peak value.</param>
    /// <param name="expected">Expected axis ceiling.</param>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(7, 8)]
    [InlineData(10, 15)]
    [InlineData(100, 150)]
    public void NiceAxisMaximum_ReturnsExpectedRoundedCeiling(double peak, double expected)
        => Assert.Equal(expected, ListeningTrendGeometry.NiceAxisMaximum(peak), 6);

    /// <summary>Points are formatted with an invariant decimal separator.</summary>
    [Fact]
    public void FormatPoint_UsesInvariantCulture()
    {
        Assert.Equal("1.5,2.25", ListeningTrendGeometry.FormatPoint(new Point(1.5, 2.25)));
        Assert.Equal("10,20", ListeningTrendGeometry.FormatPoint(new Point(10, 20)));
    }

    /// <summary>A single point yields only a move command.</summary>
    [Fact]
    public void BuildSmoothCurve_SinglePointHasOnlyMoveCommand()
        => Assert.Equal("M 0,0 ", ListeningTrendGeometry.BuildSmoothCurve([new Point(0, 0)]));

    /// <summary>Two points yield exactly one cubic segment.</summary>
    [Fact]
    public void BuildSmoothCurve_TwoPointsAddOneCubicSegment()
    {
        var path = ListeningTrendGeometry.BuildSmoothCurve([new Point(0, 0), new Point(10, 10)]);

        Assert.StartsWith("M 0,0 ", path);
        Assert.Single(Regex.Matches(path, "C "));
    }

    /// <summary>Smoothed control points never invent values outside the measured range.</summary>
    [Fact]
    public void BuildSmoothCurve_ClampsControlPointsToMeasuredRange()
    {
        var points = new List<Point> { new(0, 0), new(10, 5), new(20, 0), new(30, 3) };

        var path = ListeningTrendGeometry.BuildSmoothCurve(points);
        var yValues = PathYValues(path);

        Assert.NotEmpty(yValues);
        Assert.All(yValues, y => Assert.InRange(y, 0, 5));
        Assert.Contains(yValues, y => Math.Abs(y - 5) < 0.0001);
    }

    private static IReadOnlyList<double> PathYValues(string path) =>
        Regex.Matches(path, @"-?\d+(?:\.\d+)?,(-?\d+(?:\.\d+)?)")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
}
