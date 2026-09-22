using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the warp's sampling arithmetic, which is the reference the GPU warp's fragment shader is
/// translated from: a translation that disagrees here renders a different picture on the two paths.
/// </summary>
public sealed class WarpSamplingTests
{
    /// <summary>An identity warp samples where it looks.</summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, -0.25f)]
    [InlineData(-1f, 1f)]
    public void SamplePosition_IdentityKeepsThePosition(float x, float y)
    {
        WarpSampling.SamplePosition(x, y, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, true, out var sx, out var sy);

        Assert.Equal(x, sx, 5);
        Assert.Equal(y, sy, 5);
    }

    /// <summary>Zoom scales around the centre.</summary>
    [Fact]
    public void SamplePosition_ZoomScalesAroundTheCentre()
    {
        WarpSampling.SamplePosition(0.5f, 0.25f, 2f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, true, out var sx, out var sy);

        Assert.Equal(1f, sx, 5);
        Assert.Equal(0.5f, sy, 5);
    }

    /// <summary>Translation adds after the transform.</summary>
    [Fact]
    public void SamplePosition_OffsetIsAddedLast()
    {
        WarpSampling.SamplePosition(0.5f, 0.5f, 2f, 1f, 0f, 0f, 0f, 0.1f, -0.2f, 1f, 1f, true, out var sx, out var sy);

        Assert.Equal(1.1f, sx, 5);
        Assert.Equal(0.8f, sy, 5);
    }

    /// <summary>A quarter turn maps the axes onto each other.</summary>
    [Fact]
    public void SamplePosition_QuarterTurnSwapsTheAxes()
    {
        WarpSampling.SamplePosition(
            0.5f, 0f, 1f, 1f, MathF.PI / 2f, 0f, 0f, 0f, 0f, 1f, 1f, true, out var sx, out var sy);

        Assert.Equal(0f, sx, 4);
        Assert.Equal(0.5f, sy, 4);
    }

    /// <summary>Stretch scales each axis before the rotation.</summary>
    [Fact]
    public void SamplePosition_StretchScalesEachAxis()
    {
        WarpSampling.SamplePosition(0.5f, 0.5f, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 2f, 0.5f, true, out var sx, out var sy);

        Assert.Equal(1f, sx, 5);
        Assert.Equal(0.25f, sy, 5);
    }

    /// <summary>The radial term applies only when the exponent differs from one and it is wanted.</summary>
    [Fact]
    public void SamplePosition_RadialTermNeedsBothConditions()
    {
        // An exponent of two at radius one raises the zoom to the fifth power.
        WarpSampling.SamplePosition(1f, 0f, 2f, 2f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, true, out var withRadius, out _);
        WarpSampling.SamplePosition(1f, 0f, 2f, 2f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, false, out var withoutRadius, out _);

        Assert.Equal(2f, withoutRadius, 5);
        Assert.Equal(32f, withRadius, 3);

        // An exponent of one is a plain zoom even when the radius is wanted.
        WarpSampling.SamplePosition(1f, 0f, 2f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, true, out var plain, out _);
        Assert.Equal(2f, plain, 5);
    }

    /// <summary>The zoom clamp keeps a degenerate preset from collapsing the picture.</summary>
    [Fact]
    public void MinimumZoomIsTheReferenceClamp()
    {
        Assert.Equal(0.01f, WarpSampling.MinimumZoom);
    }
}
