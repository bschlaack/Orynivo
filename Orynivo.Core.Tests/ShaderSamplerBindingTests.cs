using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies that a shader sampling one of Milkdrop's noise or random textures gets that texture
/// instead of the frame. Unknown sampler names used to fall back to the frame, so a preset that
/// sampled noise rendered as if it sampled the picture.
/// </summary>
public sealed class ShaderSamplerBindingTests
{
    /// <summary>Sampling the noise texture differs from sampling the frame.</summary>
    [Fact]
    public void RenderFrame_ResolvesTheNoiseSampler()
    {
        var frame = Render("sampler_main");
        var noise = Render("sampler_noise_lq");

        Assert.True(MeanDifference(frame, noise) > 0.0005f);
    }

    /// <summary>A random texture resolves as well, and differs from the noise texture.</summary>
    [Fact]
    public void RenderFrame_ResolvesARandomSampler()
    {
        var noise = Render("sampler_noise_lq");
        var random = Render("sampler_rand07");

        Assert.True(MeanDifference(noise, random) > 0.0005f);
    }

    /// <summary>Renders a preset whose comp shader samples the named sampler.</summary>
    /// <param name="sampler">Sampler name to sample.</param>
    /// <returns>The rendered pixels.</returns>
    private static float[] Render(string sampler)
    {
        var preset = VisualizerPreset.Parse(
            "fDecay=1\nwave_a=0\n"
            + "comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return tex2D(" + sampler + ", uv); }");
        var renderer = new PresetRenderer(preset, 32, 18)
        {
            ShaderTimeBudgetMilliseconds = 100_000d
        };
        renderer.RenderFrame(new Silent(), 1d / 60d);

        return renderer.Output.Pixels.ToArray();
    }

    /// <summary>Computes the mean absolute difference of two frames.</summary>
    /// <param name="left">First frame.</param>
    /// <param name="right">Second frame.</param>
    /// <returns>The mean difference per pixel.</returns>
    private static float MeanDifference(float[] left, float[] right)
    {
        var total = 0f;
        for (var index = 0; index < left.Length; index += 4)
        {
            total += Math.Abs(left[index] - right[index])
                     + Math.Abs(left[index + 1] - right[index + 1])
                     + Math.Abs(left[index + 2] - right[index + 2]);
        }

        return total / Math.Max(1, (left.Length / 4) * 3);
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
