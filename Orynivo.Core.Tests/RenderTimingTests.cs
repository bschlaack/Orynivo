using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the per-stage render measurement of phase 39a. Every assertion is a ratio or a
/// bound rather than an absolute duration, so the tests describe where the cost goes without
/// depending on how fast the machine running them happens to be.
/// </summary>
public sealed class RenderTimingTests
{
    private const int Width = 200;
    private const int Height = 120;

    /// <summary>Every stage is reported, and the stages cannot exceed the whole frame.</summary>
    [Fact]
    public void RenderFrame_ReportsEveryStage()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse("fDecay=0.95\nper_pixel_1=x = x * 0.999;"), Width, Height);

        renderer.RenderFrame(new TimingAudio(), 1d / 60d);

        var timings = renderer.Timings;
        Assert.True(timings.Total > 0d);
        Assert.True(timings.Warp >= 0d);
        Assert.True(timings.Blur >= 0d);
        Assert.True(timings.PostProcess >= 0d);
        Assert.True(timings.Overlay >= 0d);
        Assert.True(timings.Composite >= 0d);
        Assert.True(timings.Shader >= 0d);
        // The stages are consecutive slices of the frame, so they can never add up to more.
        Assert.True(timings.Measured <= timings.Total + 0.5d, $"{timings.Measured} > {timings.Total}");
    }

    /// <summary>More blur passes cost more blur time.</summary>
    [Fact]
    public void RenderFrame_BlurTimeGrowsWithTheBlurCount()
    {
        var plain = new PresetRenderer(VisualizerPreset.Parse("fDecay=0.95"), Width, Height);
        var blurred = new PresetRenderer(VisualizerPreset.Parse("fDecay=0.95\nblur2=3"), Width, Height);

        plain.RenderFrame(new TimingAudio(), 1d / 60d);
        blurred.RenderFrame(new TimingAudio(), 1d / 60d);

        Assert.True(blurred.Timings.Blur > plain.Timings.Blur);
    }

    /// <summary>A warp shader runs inside the warp stage and dominates its cost.</summary>
    [Fact]
    public void RenderFrame_WarpShaderCostsMoreThanThePlainSample()
    {
        var plain = new PresetRenderer(VisualizerPreset.Parse("fDecay=0.95"), Width, Height);
        var shaded = new PresetRenderer(
            VisualizerPreset.Parse("""
                fDecay=0.95
                warp_1=float4 main(float2 uv : TEXCOORD0) : COLOR
                {
                    float3 col = tex2D(sampler_main, uv).rgb;
                    return float4(col * saturate(0.9 + bass), 1.0);
                }
                """),
            Width,
            Height)
        {
            ShaderTimeBudgetMilliseconds = 10_000d
        };

        plain.RenderFrame(new TimingAudio(), 1d / 60d);
        shaded.RenderFrame(new TimingAudio(), 1d / 60d);

        Assert.True(shaded.Timings.Warp > plain.Timings.Warp);
        Assert.False(shaded.ShadersSkipped);
    }

    /// <summary>The comp shader stage is measured on its own.</summary>
    [Fact]
    public void RenderFrame_ReportsTheCompShaderStage()
    {
        var plain = new PresetRenderer(VisualizerPreset.Parse("fDecay=0.95"), Width, Height);
        var shaded = new PresetRenderer(
            VisualizerPreset.Parse("""
                fDecay=0.95
                comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return tex2D(sampler_main, uv); }
                """),
            Width,
            Height)
        {
            ShaderTimeBudgetMilliseconds = 10_000d
        };

        plain.RenderFrame(new TimingAudio(), 1d / 60d);
        shaded.RenderFrame(new TimingAudio(), 1d / 60d);

        Assert.Equal(0d, plain.Timings.Shader);
        Assert.True(shaded.Timings.Shader > 0d);
        Assert.True(shaded.LastShaderMilliseconds > 0d);
    }

    /// <summary>The average covers every frame until the window is restarted.</summary>
    [Fact]
    public void AverageTimings_CoversEveryFrameUntilReset()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse("fDecay=0.95"), Width, Height);
        for (var frame = 0; frame < 4; frame++)
            renderer.RenderFrame(new TimingAudio(), 1d / 60d);

        var average = renderer.AverageTimings;
        Assert.True(average.Total > 0d);
        Assert.True(average.Warp > 0d);

        renderer.ResetTimings();
        Assert.Equal(renderer.Timings.Total, renderer.AverageTimings.Total);

        // After the reset the next frame is the only one in the new window.
        renderer.RenderFrame(new TimingAudio(), 1d / 60d);
        Assert.Equal(renderer.Timings.Total, renderer.AverageTimings.Total);
    }

    /// <summary>The reduce-motion path reports its overlay work and no warp.</summary>
    [Fact]
    public void RenderOverlayOnly_ReportsTheOverlayStage()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse("fDecay=0.95"), Width, Height);

        renderer.RenderOverlayOnly(new TimingAudio());

        Assert.Equal(0d, renderer.Timings.Warp);
        Assert.True(renderer.Timings.Overlay > 0d);
        Assert.True(renderer.Timings.Total > 0d);
    }

    /// <summary>Resetting the renderer also clears the timing window.</summary>
    [Fact]
    public void Reset_ClearsTheTimingWindow()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse("fDecay=0.95"), Width, Height);
        renderer.RenderFrame(new TimingAudio(), 1d / 60d);

        renderer.Reset();
        renderer.RenderFrame(new TimingAudio(), 1d / 60d);

        Assert.Equal(renderer.Timings.Total, renderer.AverageTimings.Total);
    }

    /// <summary>
    /// A rendered frame does not allocate per-frame arrays or objects, so it cannot add
    /// garbage-collection pauses to the render thread. The threshold is deliberately generous:
    /// tiered compilation and test-framework bookkeeping can attribute a few kilobytes to the
    /// thread, while a single per-frame buffer for this size would already be hundreds of
    /// kilobytes, so the test still catches a real regression.
    /// </summary>
    [Fact]
    public void RenderFrame_StaysAllocationFreeOnTheHotPath()
    {
        var renderer = new PresetRenderer(
            VisualizerPreset.Parse("fDecay=0.95\nper_pixel_1=x = x + 0.001;"),
            Width,
            Height);
        var audio = new TimingAudio();
        // Warm up first so the measurement only covers steady-state frames.
        for (var frame = 0; frame < 3; frame++)
            renderer.RenderFrame(audio, 1d / 60d);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 5; frame++)
            renderer.RenderFrame(audio, 1d / 60d);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 32 * 1024, $"five frames allocated {allocated} bytes");
    }

    /// <summary>An audio source with content on every band and a waveform.</summary>
    private sealed class TimingAudio : IVisualizerAudioSource
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
