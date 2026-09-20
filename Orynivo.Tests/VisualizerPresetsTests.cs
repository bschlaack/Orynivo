using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the built-in visualizer presets. They are parsed at start-up, so a typo in a
/// shipped preset must fail here instead of showing an empty window.
/// </summary>
public sealed class VisualizerPresetsTests
{
    /// <summary>The built-in presets are present and named.</summary>
    [Fact]
    public void BuiltIn_ContainsNamedPresets()
    {
        Assert.NotEmpty(VisualizerPresets.BuiltIn);
        Assert.All(VisualizerPresets.BuiltIn, preset => Assert.False(string.IsNullOrWhiteSpace(preset.Name)));
    }

    /// <summary>Every shipped preset draws a waveform and keeps a usable decay.</summary>
    [Fact]
    public void BuiltIn_PresetsAreUsable()
    {
        Assert.All(VisualizerPresets.BuiltIn, preset =>
        {
            Assert.InRange(preset.Decay, 0f, 1f);
            Assert.InRange(preset.Zoom, 0.05f, 10f);
            Assert.InRange(preset.BlurLevel, 0, 4);
            Assert.InRange(preset.WaveAlpha, 0f, 1f);
        });
    }

    /// <summary>At least one preset warps the picture, so the engine is actually exercised.</summary>
    [Fact]
    public void BuiltIn_ContainsAWarpingPreset()
    {
        Assert.Contains(VisualizerPresets.BuiltIn, preset => !preset.PerPixel.IsEmpty);
    }

    /// <summary>Indexing wraps around in both directions.</summary>
    [Fact]
    public void At_WrapsAround()
    {
        var count = VisualizerPresets.BuiltIn.Count;

        Assert.Equal(VisualizerPresets.BuiltIn[0].Name, VisualizerPresets.At(count).Name);
        Assert.Equal(VisualizerPresets.BuiltIn[count - 1].Name, VisualizerPresets.At(-1).Name);
        Assert.Equal(VisualizerPresets.BuiltIn[1].Name, VisualizerPresets.At(1).Name);
    }

    /// <summary>A preset renders a frame without touching the audio path.</summary>
    [Fact]
    public void BuiltIn_PresetsRenderAFrame()
    {
        foreach (var preset in VisualizerPresets.BuiltIn)
        {
            var renderer = new PresetRenderer(preset, 32, 18);
            renderer.RenderFrame(new StubAudio(), 1d / 60d);
            renderer.RenderFrame(new StubAudio(), 1d / 60d);

            Assert.Equal(2, renderer.FrameCount);
        }
    }

    /// <summary>Audio source with a fixed spectrum and waveform.</summary>
    private sealed class StubAudio : IVisualizerAudioSource
    {
        public ReadOnlySpan<float> Bands => BandsValue;

        public ReadOnlySpan<float> Waveform => WaveformValue;

        public float Bass => 0.6f;

        public float Mid => 0.4f;

        public float Treble => 0.2f;

        public float Volume => 0.5f;

        private float[] BandsValue { get; } = BuildBands();

        private float[] WaveformValue { get; } = BuildWaveform();

        private static float[] BuildBands()
        {
            var bands = new float[64];
            for (var index = 0; index < bands.Length; index++)
                bands[index] = 1f - (index / 64f);
            return bands;
        }

        private static float[] BuildWaveform()
        {
            var waveform = new float[256];
            for (var index = 0; index < waveform.Length; index++)
                waveform[index] = MathF.Sin(index * 0.3f) * 0.4f;
            return waveform;
        }
    }
}
