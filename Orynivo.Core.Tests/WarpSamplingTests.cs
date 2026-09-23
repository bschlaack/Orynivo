using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the warp's sampling arithmetic, which is the reference the GPU warp's fragment shader is
/// translated from: a translation that disagrees here renders a different picture on the two paths.
/// The arithmetic is the reference implementation's warp vertex shader, so the expectations below are
/// its behavior, not the engine's previous behavior.
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
        WarpSampling.SamplePosition(x, y, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, true, out var sx, out var sy);

        Assert.Equal(x, sx, 5);
        Assert.Equal(y, sy, 5);
    }

    /// <summary>Zoom divides the distance from the centre, so a larger zoom reads further out.</summary>
    [Fact]
    public void SamplePosition_ZoomDividesAroundTheCentre()
    {
        WarpSampling.SamplePosition(0.5f, 0.25f, 2f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, true, out var sx, out var sy);

        Assert.Equal(0.25f, sx, 5);
        Assert.Equal(0.125f, sy, 5);
    }

    /// <summary>Translation subtracts after the transform, the other way from the old engine.</summary>
    [Fact]
    public void SamplePosition_OffsetIsSubtractedLast()
    {
        WarpSampling.SamplePosition(0.5f, 0.5f, 1f, 1f, 0f, 0f, 0f, 0.1f, -0.2f, 1f, 1f, 1f, 1f, true, out var sx, out var sy);

        Assert.Equal(0.3f, sx, 5);
        Assert.Equal(0.9f, sy, 5);
    }

    /// <summary>A half turn mirrors the sample around the centre.</summary>
    [Fact]
    public void SamplePosition_HalfTurnMirrorsAroundTheCentre()
    {
        WarpSampling.SamplePosition(
            0.5f, 0f, 1f, 1f, MathF.PI, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, true, out var sx, out var sy);

        Assert.Equal(-2.5f, sx, 4);
        Assert.Equal(-2f, sy, 4);
    }

    /// <summary>Stretch divides each axis.</summary>
    [Fact]
    public void SamplePosition_StretchDividesEachAxis()
    {
        WarpSampling.SamplePosition(0.5f, 0.5f, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 2f, 0.5f, 1f, 1f, true, out var sx, out var sy);

        Assert.Equal(-0.25f, sx, 5);
        Assert.Equal(2f, sy, 5);
    }

    /// <summary>The radial term applies only when the exponent differs from one and it is wanted.</summary>
    [Fact]
    public void SamplePosition_RadialTermNeedsBothConditions()
    {
        // An exponent of two at radius one makes the radial zoom pow(zoom, pow(2, 1)) = zoom squared.
        WarpSampling.SamplePosition(1f, 0f, 2f, 2f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, true, out var withRadius, out _);
        WarpSampling.SamplePosition(1f, 0f, 2f, 2f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, false, out var withoutRadius, out _);

        Assert.Equal(0.5f, withoutRadius, 5);
        Assert.Equal(0.25f, withRadius, 5);

        // An exponent of one is a plain zoom even when the radius is wanted.
        WarpSampling.SamplePosition(1f, 0f, 2f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, true, out var plain, out _);
        Assert.Equal(0.5f, plain, 5);
    }

    /// <summary>The zoom clamp keeps a degenerate preset from collapsing the picture.</summary>
    [Fact]
    public void MinimumZoomIsTheReferenceClamp()
    {
        Assert.Equal(0.01f, WarpSampling.MinimumZoom);
    }
}
