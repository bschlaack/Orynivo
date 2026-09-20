using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies the framebuffer operations the feedback warp depends on.</summary>
public sealed class PixelBufferTests
{
    /// <summary>A new buffer is empty.</summary>
    [Fact]
    public void Constructor_StartsEmpty()
    {
        var buffer = new PixelBuffer(4, 3);

        Assert.Equal(4, buffer.Width);
        Assert.Equal(3, buffer.Height);
        Assert.All(buffer.Pixels.ToArray(), value => Assert.Equal(0f, value));
    }

    /// <summary>Adding colour saturates at one.</summary>
    [Fact]
    public void AddPixel_SaturatesAtOne()
    {
        var buffer = new PixelBuffer(2, 2);

        buffer.AddPixel(1, 1, 0.8f, 0.8f, 0.8f);
        buffer.AddPixel(1, 1, 0.8f, 0.8f, 0.8f);

        Assert.Equal(1f, buffer.GetPixel(1, 1, 0));
        Assert.Equal(0f, buffer.GetPixel(0, 0, 0));
    }

    /// <summary>Out-of-range writes are ignored and reads clamp.</summary>
    [Fact]
    public void PixelAccess_ClampsReadsAndIgnoresStrayWrites()
    {
        var buffer = new PixelBuffer(2, 2);
        buffer.AddPixel(0, 0, 1f, 0f, 0f);

        buffer.AddPixel(-5, 9, 1f, 1f, 1f);

        Assert.Equal(1f, buffer.GetPixel(-3, -3, 0));
        Assert.All(buffer.Pixels.ToArray(), value => Assert.InRange(value, 0f, 1f));
    }

    /// <summary>Bilinear sampling returns the corners exactly and blends between them.</summary>
    [Fact]
    public void SampleBilinear_InterpolatesBetweenPixels()
    {
        var buffer = new PixelBuffer(2, 1);
        buffer.AddPixel(0, 0, 0f, 0f, 0f);
        buffer.AddPixel(1, 0, 1f, 1f, 1f);
        var sample = new float[4];

        buffer.SampleBilinear(0f, 0f, sample);
        Assert.Equal(0f, sample[0], 5);

        buffer.SampleBilinear(1f, 0f, sample);
        Assert.Equal(1f, sample[0], 5);

        buffer.SampleBilinear(0.5f, 0f, sample);
        Assert.Equal(0.5f, sample[0], 5);
    }

    /// <summary>Sampling outside the frame yields transparent black, so no edge smear appears.</summary>
    [Fact]
    public void SampleBilinear_ReturnsTransparentOutsideTheFrame()
    {
        var buffer = new PixelBuffer(2, 2);
        buffer.AddPixel(1, 1, 0.5f, 0.5f, 0.5f);
        var sample = new float[4];

        buffer.SampleBilinear(9f, 9f, sample);
        buffer.SampleBilinear(-0.5f, 0.5f, sample);

        Assert.Equal(0f, sample[0], 5);
    }

    /// <summary>Scaling fades the image.</summary>
    [Fact]
    public void Scale_FadesEveryChannel()
    {
        var buffer = new PixelBuffer(1, 1);
        buffer.AddPixel(0, 0, 1f, 0.5f, 0.25f);

        buffer.Scale(0.5f);

        Assert.Equal(0.5f, buffer.GetPixel(0, 0, 0), 5);
        Assert.Equal(0.25f, buffer.GetPixel(0, 0, 1), 5);
        Assert.Equal(0.125f, buffer.GetPixel(0, 0, 2), 5);
    }

    /// <summary>A blur pass spreads a single lit pixel onto its neighbours.</summary>
    [Fact]
    public void Blur_SpreadsASinglePixel()
    {
        var buffer = new PixelBuffer(3, 3);
        buffer.AddPixel(1, 1, 0.9f, 0.9f, 0.9f);

        buffer.Blur();

        Assert.True(buffer.GetPixel(1, 1, 0) > 0f);
        Assert.True(buffer.GetPixel(0, 1, 0) > 0f);
        Assert.Equal(buffer.GetPixel(0, 1, 0), buffer.GetPixel(1, 1, 0), 5);
    }

    /// <summary>Copying replaces the destination content.</summary>
    [Fact]
    public void CopyFrom_ReplacesContent()
    {
        var source = new PixelBuffer(2, 2);
        source.AddPixel(0, 1, 0.4f, 0.3f, 0.2f);
        var destination = new PixelBuffer(2, 2);
        destination.AddPixel(1, 0, 1f, 1f, 1f);

        destination.CopyFrom(source);

        Assert.Equal(0.4f, destination.GetPixel(0, 1, 0), 5);
        Assert.Equal(0f, destination.GetPixel(1, 0, 0));
    }

    /// <summary>Copying a differently sized buffer is rejected.</summary>
    [Fact]
    public void CopyFrom_RejectsMismatchedSizes()
    {
        var source = new PixelBuffer(2, 2);
        var destination = new PixelBuffer(3, 3);

        Assert.Throws<ArgumentException>(() => destination.CopyFrom(source));
    }

    /// <summary>The BGRA export swaps the red and blue channels and forces opaque alpha.</summary>
    [Fact]
    public void WriteBgra_ConvertsChannels()
    {
        var buffer = new PixelBuffer(1, 1);
        buffer.AddPixel(0, 0, 1f, 0.5f, 0f);
        var bytes = new byte[4];

        buffer.WriteBgra(bytes);

        Assert.Equal(0, bytes[0]);
        Assert.Equal(128, bytes[1]);
        Assert.Equal(255, bytes[2]);
        Assert.Equal(255, bytes[3]);
    }

    /// <summary>A destination that is too small is rejected.</summary>
    [Fact]
    public void WriteBgra_RejectsASmallDestination()
    {
        var buffer = new PixelBuffer(4, 4);

        Assert.Throws<ArgumentException>(() => buffer.WriteBgra(new byte[4]));
    }

    /// <summary>An unusable size is rejected.</summary>
    [Fact]
    public void Constructor_RejectsUnusableSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(4, 0));
    }
}
