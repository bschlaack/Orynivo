using Orynivo.Audio;
using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the render pipeline end to end: the overlay it draws, the feedback warp driven
/// by the per-pixel block, and the fade the per-frame block can override. Every assertion is
/// a deterministic property of the frame, so a preset that stops working is caught here
/// instead of by eye.
/// </summary>
public sealed class PresetRendererTests
{
    /// <summary>A frame with audio produces a visible picture.</summary>
    [Fact]
    public void RenderFrame_DrawsTheOverlay()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Create("test", null, null), 64, 36);

        renderer.RenderFrame(new TestAudio(), 1d / 60d);

        Assert.Equal(1, renderer.FrameCount);
        Assert.True(Energy(renderer) > 0f);
    }

    /// <summary>Silence with no feedback and no overlay leaves an empty frame.</summary>
    [Fact]
    public void RenderFrame_LeavesAnEmptyFrameWithoutAudioOrFeedback()
    {
        var preset = VisualizerPreset.Create("silent", "decay = 0;", null, decay: 0f);
        var renderer = new PresetRenderer(preset, 32, 18);

        renderer.RenderFrame(TestAudio.Silent, 1d / 60d);
        renderer.RenderFrame(TestAudio.Silent, 1d / 60d);

        Assert.Equal(0f, Energy(renderer), 5);
    }

    /// <summary>The per-pixel block decides where the previous frame is sampled from.</summary>
    [Fact]
    public void RenderFrame_HonoursThePerPixelWarp()
    {
        // Bands light only the left half, so a horizontal mirror must light the right half.
        var audio = TestAudio.LeftOnly;
        var identity = new PresetRenderer(VisualizerPreset.Create("identity", null, null), 64, 36);
        var mirrored = new PresetRenderer(VisualizerPreset.Create("mirror", null, "x = -x;"), 64, 36);

        identity.RenderFrame(audio, 1d / 60d);
        mirrored.RenderFrame(audio, 1d / 60d);
        Assert.Equal(0f, RightHalfEnergy(identity), 5);
        Assert.Equal(0f, RightHalfEnergy(mirrored), 5);

        identity.RenderFrame(audio, 1d / 60d);
        mirrored.RenderFrame(audio, 1d / 60d);

        Assert.Equal(0f, RightHalfEnergy(identity), 5);
        Assert.True(RightHalfEnergy(mirrored) > 0f);
    }

    /// <summary>The per-frame block overrides the preset's decay.</summary>
    [Fact]
    public void RenderFrame_HonoursThePerFrameDecay()
    {
        var preset = VisualizerPreset.Create("nodecay", "decay = 0;", "x = -x;", decay: 1f);
        var renderer = new PresetRenderer(preset, 64, 36);

        renderer.RenderFrame(TestAudio.LeftOnly, 1d / 60d);
        renderer.RenderFrame(TestAudio.LeftOnly, 1d / 60d);

        // The warp would mirror the picture to the right, but a zero decay discards it.
        Assert.Equal(0f, RightHalfEnergy(renderer), 5);
    }

    /// <summary>Resetting clears the picture and restarts the preset.</summary>
    [Fact]
    public void Reset_ClearsThePicture()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Create("test", null, null), 32, 18);
        renderer.RenderFrame(new TestAudio(), 1d / 60d);
        Assert.True(Energy(renderer) > 0f);

        renderer.Reset();

        Assert.Equal(0, renderer.FrameCount);
        Assert.Equal(0f, Energy(renderer), 5);
    }

    /// <summary>A frame rate of zero seconds still renders.</summary>
    [Fact]
    public void RenderFrame_AcceptsAZeroDelta()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Create("test", null, null), 16, 9);

        renderer.RenderFrame(new TestAudio(), 0d);

        Assert.Equal(1, renderer.FrameCount);
    }

    private static float Energy(PresetRenderer renderer)
    {
        var total = 0f;
        var pixels = renderer.Output.Pixels;
        for (var index = 0; index < pixels.Length; index += 4)
            total += pixels[index] + pixels[index + 1] + pixels[index + 2];
        return total;
    }

    private static float RightHalfEnergy(PresetRenderer renderer)
    {
        var total = 0f;
        var buffer = renderer.Output;
        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = buffer.Width / 2; x < buffer.Width; x++)
                total += buffer.GetPixel(x, y, 0) + buffer.GetPixel(x, y, 1) + buffer.GetPixel(x, y, 2);
        }

        return total;
    }

    /// <summary>
    /// Small audio source with controllable bands and waveform. The mirror tests use audio
    /// without a waveform, because the waveform overlay spans the complete frame width and
    /// would otherwise light the half that is being asserted as empty.
    /// </summary>
    private sealed class TestAudio : IVisualizerAudioSource
    {
        public static TestAudio Silent { get; } = new() { WaveformValue = [] };

        public static TestAudio LeftOnly { get; } = new() { BandsValue = LeftOnlyBands(), WaveformValue = [] };

        public ReadOnlySpan<float> Bands => BandsValue;

        public ReadOnlySpan<float> Waveform => WaveformValue;

        public float Bass { get; init; } = 0.5f;

        public float Mid { get; init; } = 0.3f;

        public float Treble { get; init; } = 0.2f;

        public float Volume { get; init; } = 0.4f;

        private float[] BandsValue { get; init; } = new float[AudioSpectrumAnalyzer.BandCount];

        private float[] WaveformValue { get; init; } = BuildWaveform();

        private static float[] BuildWaveform()
        {
            var waveform = new float[AudioSpectrumAnalyzer.WaveformPoints];
            for (var index = 0; index < waveform.Length; index++)
                waveform[index] = MathF.Sin(index * 0.2f) * 0.5f;
            return waveform;
        }

        private static float[] LeftOnlyBands()
        {
            var bands = new float[AudioSpectrumAnalyzer.BandCount];
            for (var band = 0; band < bands.Length / 2; band++)
                bands[band] = 1f;
            return bands;
        }
    }
}
