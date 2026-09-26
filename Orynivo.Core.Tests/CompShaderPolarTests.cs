using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the comp shader's <c>rad</c>/<c>ang</c> use MilkDrop's <c>UvToMathSpace</c> convention
/// (<c>milkdropfs.cpp</c>): the position is scaled by the aspect, <c>rad</c> is one at the screen
/// corners, and <c>ang</c> runs zero through two pi. The CPU interpreter and the GPU emitter have to
/// agree on this, because a comp shader such as Mashup (13) derives its whole picture from them.
/// </summary>
public sealed class CompShaderPolarTests
{
    private const int Width = 32;
    private const int Height = 18;

    /// <summary>The comp shader reads the reference polar pair at a known pixel.</summary>
    [Fact]
    public void CompRadAng_MatchUvToMathSpace()
    {
        var renderer = new PresetRenderer(
            VisualizerPreset.Parse(
                "comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return float4(rad, ang / 6.2831853, 0.0, 1.0); }"),
            Width,
            Height)
        {
            ShaderTimeBudgetMilliseconds = 10_000d,
            ShaderPassBudgetMilliseconds = 10_000d
        };

        renderer.RenderFrame(new SilentAudio(), 1d / 60d);

        // A pixel off both axes exercises the aspect scaling, the corner normalisation, and the
        // angle wrap; the center pixel would hide all three.
        const int pixelX = 24;
        const int pixelY = 6;
        var u = (pixelX + 0.5f) / Width;
        var v = (pixelY + 0.5f) / Height;
        WarpSampling.GetAspect(Width, Height, out var aspectX, out var aspectY);
        var px = ((u * 2f) - 1f) * aspectX;
        var py = ((v * 2f) - 1f) * aspectY;
        var expectedRad = MathF.Sqrt((px * px) + (py * py)) /
                          MathF.Sqrt((aspectX * aspectX) + (aspectY * aspectY));
        var expectedAng = MathF.Atan2(py, px);
        if (expectedAng < 0f)
            expectedAng += 2f * MathF.PI;

        Assert.Equal(expectedRad, renderer.Output.GetPixel(pixelX, pixelY, 0), 3);
        Assert.Equal(expectedAng / (2f * MathF.PI), renderer.Output.GetPixel(pixelX, pixelY, 1), 3);
    }

    /// <summary>An audio source with no content, so only the comp shader paints the frame.</summary>
    private sealed class SilentAudio : IVisualizerAudioSource
    {
        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => [];

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => [];

        /// <inheritdoc/>
        public float Bass => 0f;

        /// <inheritdoc/>
        public float Mid => 0f;

        /// <inheritdoc/>
        public float Treble => 0f;

        /// <inheritdoc/>
        public float Volume => 0f;
    }
}
