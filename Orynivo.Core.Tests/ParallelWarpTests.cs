using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the parallel warp: a pass may only run on several threads when everything it writes
/// is re-seeded for every pixel, and both paths have to render byte-identical frames. The frames
/// are compared pixel by pixel, which is the only assertion that matters for a rendering change.
/// </summary>
public sealed class ParallelWarpTests
{
    private const int Width = 200;
    private const int Height = 256;

    /// <summary>A parallel-safe preset renders exactly the same frame on both paths.</summary>
    [Theory]
    [InlineData("fDecay=0.95")]
    [InlineData("fDecay=0.95\nper_pixel_1=x = x + cos(ang) * 0.02 * rad;\nper_pixel_2=y = y + sin(ang) * 0.02 * rad;")]
    [InlineData("fDecay=0.9\nzoom=1.02\nrot=0.03\ncx=0.05\nper_pixel_1=rad = rad * 1.01;")]
    [InlineData("fDecay=0.95\nshape_0_sides=5\nshape_0_rad=0.2\nwave_0_per_point_1=y = sample * 0.5;")]
    // The full-frame passes (blur, decay, echo, darken, gamma, borders, composite) are row-independent.
    [InlineData("fDecay=0.9\nblur2=3")]
    [InlineData("fDecay=0.9\necho_alpha=0.5\necho_zoom=1.2\nob_a=0.5\nib_a=0.5\ndarken_center=0.3\nfGammaAdj=1.2")]
    public void RenderFrame_ParallelAndSequentialProduceIdenticalFrames(string presetText)
    {
        var parallel = Render(presetText, parallelism: true);
        var sequential = Render(presetText, parallelism: false);

        Assert.Equal(sequential, parallel);
    }

    /// <summary>
    /// A preset that writes a value another pixel could read stays sequential, and still renders
    /// identically, because both runs then take the same path.
    /// </summary>
    [Fact]
    public void RenderFrame_UnsafePresetStaysSequentialAndIdentical()
    {
        const string presetText = "fDecay=0.95\nper_pixel_1=q1 = q1 + 1;\nper_pixel_2=x = x + q1 * 0.0001;";
        var renderer = new PresetRenderer(VisualizerPreset.Parse(presetText), Width, Height);

        Assert.False(renderer.WarpParallelismAvailable);
        Assert.Equal(Render(presetText, parallelism: false), Render(presetText, parallelism: true));
    }

    /// <summary>Only writes to the values the warp re-seeds per pixel allow parallelism.</summary>
    [Fact]
    public void WarpParallelism_RequiresSeededWritesOnly()
    {
        Assert.True(Create("per_pixel_1=x = x + 0.01;").WarpParallelismAvailable);
        Assert.True(Create("per_pixel_1=rad = rad * 1.1; y = y * 0.9;").WarpParallelismAvailable);
        Assert.True(Create("per_pixel_1=ang = ang + 0.1;").WarpParallelismAvailable);
        Assert.False(Create("per_pixel_1=q1 = q1 + 1;").WarpParallelismAvailable);
        Assert.False(Create("per_pixel_1=bass = 1;").WarpParallelismAvailable);
    }

    /// <summary>A preset with a warp shader stays sequential, because the interpreter keeps state.</summary>
    [Fact]
    public void WarpParallelism_IsDisabledByWarpShaders()
    {
        var preset = VisualizerPreset.Parse("""
            fDecay=0.95
            warp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return tex2D(sampler_main, uv); }
            """);

        Assert.False(new PresetRenderer(preset, Width, Height).WarpParallelismAvailable);
    }

    /// <summary>The parallel path is the default and the switch only changes the split.</summary>
    [Fact]
    public void ParallelismEnabled_DefaultsToTrue()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Create("plain", null, null), Width, Height);

        Assert.True(renderer.ParallelismEnabled);
    }

    /// <summary>Renders a few frames and returns the resulting pixels.</summary>
    /// <param name="presetText">Preset to render.</param>
    /// <param name="parallelism">Whether the parallel path is allowed.</param>
    /// <returns>The rendered pixels.</returns>
    private static float[] Render(string presetText, bool parallelism)
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse(presetText), Width, Height)
        {
            ParallelismEnabled = parallelism
        };
        var audio = new ParallelAudio();
        for (var frame = 0; frame < 3; frame++)
            renderer.RenderFrame(audio, 1d / 60d);

        return renderer.Output.Pixels.ToArray();
    }

    /// <summary>Creates a renderer for a preset body.</summary>
    /// <param name="presetText">Preset body.</param>
    /// <returns>The renderer.</returns>
    private static PresetRenderer Create(string presetText) =>
        new(VisualizerPreset.Parse("fDecay=0.95\n" + presetText), Width, Height);

    /// <summary>An audio source with content on every band and a waveform.</summary>
    private sealed class ParallelAudio : IVisualizerAudioSource
    {
        private readonly float[] _bands = [0.7f, 0.55f, 0.4f, 0.25f, 0.15f, 0.1f];
        private readonly float[] _waveform = Enumerable.Range(0, 256)
            .Select(index => MathF.Sin(index * 0.13f) * 0.6f)
            .ToArray();

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _waveform;

        /// <inheritdoc/>
        public float Bass => 0.7f;

        /// <inheritdoc/>
        public float Mid => 0.55f;

        /// <inheritdoc/>
        public float Treble => 0.4f;

        /// <inheritdoc/>
        public float Volume => 0.6f;
    }
}

