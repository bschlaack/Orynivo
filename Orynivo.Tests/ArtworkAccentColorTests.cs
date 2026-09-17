using Avalonia.Media;
using Orynivo.Controls;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the pure colour maths behind the transport accent derived from
/// album artwork.
/// </summary>
public sealed class ArtworkAccentColorTests
{
    /// <summary>Primary hues are computed correctly from 8-bit RGB triples.</summary>
    /// <param name="r">Red component.</param>
    /// <param name="g">Green component.</param>
    /// <param name="b">Blue component.</param>
    /// <param name="expected">Expected hue in degrees.</param>
    [Theory]
    [InlineData(255, 0, 0, 0)]
    [InlineData(0, 255, 0, 120)]
    [InlineData(0, 0, 255, 240)]
    [InlineData(255, 255, 0, 60)]
    [InlineData(0, 255, 255, 180)]
    [InlineData(255, 0, 255, 300)]
    public void RgbToHue_ReturnsPrimaryHue(double r, double g, double b, double expected)
        => Assert.Equal(expected, ArtworkAccentColor.RgbToHue(r, g, b), 3);

    /// <summary>Achromatic grey has no hue.</summary>
    [Fact]
    public void RgbToHue_GrayReturnsZero()
        => Assert.Equal(0, ArtworkAccentColor.RgbToHue(128, 128, 128));

    /// <summary>HSV triples convert to the expected opaque RGB colours.</summary>
    /// <param name="h">Hue in degrees.</param>
    /// <param name="s">Saturation.</param>
    /// <param name="v">Value.</param>
    /// <param name="r">Expected red component.</param>
    /// <param name="g">Expected green component.</param>
    /// <param name="b">Expected blue component.</param>
    [Theory]
    [InlineData(0, 1, 1, 255, 0, 0)]
    [InlineData(120, 1, 1, 0, 255, 0)]
    [InlineData(240, 1, 1, 0, 0, 255)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(0, 0, 1, 255, 255, 255)]
    public void HsvToColor_ReturnsExpectedRgb(double h, double s, double v, byte r, byte g, byte b)
        => Assert.Equal(Color.FromRgb(r, g, b), ArtworkAccentColor.HsvToColor(h, s, v));

    /// <summary>The normalised accent keeps saturation and value inside the readable range.</summary>
    [Fact]
    public void AdjustAccent_ClampsIntoReadableRange()
    {
        var color = ArtworkAccentColor.AdjustAccent(200, 20, 20);

        Assert.True(color.R > color.G);
        Assert.InRange(color.R, 0.6 * 255 - 1, 0.86 * 255 + 1);
    }

    /// <summary>The foreground chosen for a background always has strong contrast.</summary>
    /// <param name="r">Background red component.</param>
    /// <param name="g">Background green component.</param>
    /// <param name="b">Background blue component.</param>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    [InlineData(32, 64, 128)]
    public void GetReadableTextColor_PicksContrastingForeground(byte r, byte g, byte b)
    {
        var background = Color.FromRgb(r, g, b);
        var text = ArtworkAccentColor.GetReadableTextColor(background);

        var backgroundLuma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var textLuma = 0.2126 * text.R + 0.7152 * text.G + 0.0722 * text.B;
        Assert.True(Math.Abs(backgroundLuma - textLuma) > 100);
    }
}
