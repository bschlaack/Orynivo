using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies Milkdrop's per-frame random vector. Presets use it to vary a shader without changing
/// it per pixel, so an unbound variable would leave every such preset with a black picture.
/// </summary>
public sealed class ShaderRandomFrameTests
{
    private const string Shader =
        "fDecay=1\nwave_a=0\ncomp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return float4(rand_frame.rgb, 1.0); }";

    /// <summary>The random vector is bound, so the shader does not render black.</summary>
    [Fact]
    public void RenderFrame_BindsTheRandomFrameVector()
    {
        var renderer = Create();
        renderer.RenderFrame(new Silent(), 1d / 60d);

        Assert.True(Mean(renderer) > 0.0001f);
    }

    /// <summary>The vector changes between frames, which is what makes it useful.</summary>
    [Fact]
    public void RenderFrame_ChangesTheVectorPerFrame()
    {
        var renderer = Create();
        renderer.RenderFrame(new Silent(), 1d / 60d);
        var first = renderer.Output.Pixels.ToArray();
        renderer.RenderFrame(new Silent(), 1d / 60d);
        var second = renderer.Output.Pixels.ToArray();

        Assert.NotEqual(first, second);
    }

    /// <summary>Creates a renderer with the shader budget disabled so the shader always runs.</summary>
    /// <returns>The renderer.</returns>
    private static PresetRenderer Create() =>
        new(VisualizerPreset.Parse(Shader), 16, 9)
        {
            ShaderTimeBudgetMilliseconds = 100_000d
        };

    /// <summary>Computes the mean luminance of a frame.</summary>
    /// <param name="renderer">Renderer holding the frame.</param>
    /// <returns>The mean luminance.</returns>
    private static float Mean(PresetRenderer renderer)
    {
        var pixels = renderer.Output.Pixels;
        var total = 0f;
        for (var index = 0; index < pixels.Length; index += 4)
            total += (pixels[index] + pixels[index + 1] + pixels[index + 2]) / 3f;

        return total / Math.Max(1, pixels.Length / 4);
    }

    /// <summary>An audio source that reports silence.</summary>
    private sealed class Silent : IVisualizerAudioSource
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
