using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the Skia comp pass against the interpreter. The pass carries the frame through eight-bit
/// textures, so the two pictures differ by at most a level or two; the test keeps them from drifting
/// further and covers the sampler bindings a comp shader reads.
/// </summary>
public sealed class SkiaCompPassTests
{
    /// <summary>A comp shader with no sampler renders the same gradient on both paths.</summary>
    [Fact]
    public void RenderFrame_SkiaCompPassMatchesTheInterpreter()
    {
        const string Preset =
            "fDecay=1\nwave_a=0\n" +
            "comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { ret = float3(uv.x, uv.y, 0.5); }";

        var cpu = Render(Preset, skia: false);
        var gpu = Render(Preset, skia: true);

        // A non-zero difference proves the Skia pass actually ran; the bound keeps it to the
        // eight-bit quantisation of the Skia surface instead of a wrong picture.
        var difference = MeanAbsoluteDifference(cpu, gpu);
        Assert.InRange(difference, 0.0002f, 0.002f);
        Assert.Contains(gpu, value => value > 0.1f);
    }

    /// <summary>The Skia pass resolves the noise sampler the way the interpreter does.</summary>
    [Fact]
    public void RenderFrame_SkiaCompPassResolvesTheNoiseSampler()
    {
        const string Preset =
            "fDecay=1\nwave_a=0\n" +
            "comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return tex2D(sampler_noise_lq, uv * 3); }";

        var cpu = Render(Preset, skia: false);
        var gpu = Render(Preset, skia: true);

        // The noise texture itself is quantised to eight bits on the Skia path, so the two pictures
        // may differ by a couple of levels while still describing the same texture.
        Assert.True(MeanAbsoluteDifference(cpu, gpu) < 0.004f);
        Assert.Contains(gpu, value => value > 0.05f);
    }

    /// <summary>Renders a preset with the Skia comp pass enabled or disabled.</summary>
    /// <param name="preset">Preset source.</param>
    /// <param name="skia">Whether the Skia comp pass is enabled.</param>
    /// <returns>The rendered pixels.</returns>
    private static float[] Render(string preset, bool skia)
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse(preset), 32, 18)
        {
            ShaderTimeBudgetMilliseconds = 100_000d,
            UseSkiaCompPass = skia
        };
        try
        {
            renderer.RenderFrame(new Silent(), 1d / 60d);
            return renderer.Output.Pixels.ToArray();
        }
        finally
        {
            renderer.Dispose();
        }
    }

    /// <summary>Computes the mean absolute difference of two frames.</summary>
    /// <param name="left">First frame.</param>
    /// <param name="right">Second frame.</param>
    /// <returns>The mean difference per channel.</returns>
    private static float MeanAbsoluteDifference(float[] left, float[] right)
    {
        var total = 0f;
        var count = 0;
        for (var index = 0; index < Math.Min(left.Length, right.Length); index += 4)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                total += Math.Abs(left[index + channel] - right[index + channel]);
                count++;
            }
        }

        return total / Math.Max(1, count);
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
