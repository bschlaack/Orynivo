using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies that the preset per-frame random vector <c>rand_frame</c> is reproducible when
/// <see cref="PresetRenderer.RandomSeed"/> is set, while the default stays random so a preset
/// still looks different on every run as in MilkDrop.
/// </summary>
public sealed class RandomSeedTests
{
    /// <summary>A warp shader that paints the frame with the per-frame random vector.</summary>
    private const string RandomPreset = """
        name=Seed
        decay=1
        wave_a=0
        warp_1=`shader_body
        warp_2=`{
        warp_3=`  ret = float3(rand_frame.x, rand_frame.y, rand_frame.z);
        warp_4=`}
        """;

    /// <summary>Two renderers with the same seed produce the identical first frame.</summary>
    [Fact]
    public void SameSeed_ReproducesTheFrame()
    {
        var first = Render(seed: 12345);
        var second = Render(seed: 12345);

        Assert.Equal(first, second);
    }

    /// <summary>A different seed produces a different frame.</summary>
    [Fact]
    public void DifferentSeed_ChangesTheFrame()
    {
        var first = Render(seed: 12345);
        var other = Render(seed: 54321);

        Assert.NotEqual(first, other);
    }

    /// <summary>Renders one frame with the seed and returns its pixels.</summary>
    /// <param name="seed">Seed assigned to the renderer.</param>
    /// <returns>The rendered frame's float pixels.</returns>
    private static float[] Render(int seed)
    {
        using var renderer = new PresetRenderer(VisualizerPreset.Parse(RandomPreset), 32, 32)
        {
            RandomSeed = seed,
            ShaderTimeBudgetMilliseconds = 10_000d,
            ShaderPassBudgetMilliseconds = 10_000d
        };
        renderer.RenderFrame(new SilentAudio(), 1d / 60d);
        return renderer.Output.Pixels.ToArray();
    }

    /// <summary>An audio source with no content, so only the warp shader paints the frame.</summary>
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
