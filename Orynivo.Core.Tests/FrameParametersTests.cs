using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the frame parameters and the overlay-only frame a GPU pipeline reads, so a GPU path
/// cannot disagree with the CPU path about what a preset asked for.
/// </summary>
public sealed class FrameParametersTests
{
    /// <summary>The pass parameters come from the preset's keys, with the same clamps as the passes.</summary>
    [Fact]
    public void ReadFrameParameters_ReflectsTheKeys()
    {
        var preset = VisualizerPreset.Parse(
            "decay=0.9\nblur_level=2\ndarken_center=0.25\nfGammaAdj=1.5\nfShader=0.3\n" +
            "echo_zoom=1.2\necho_alpha=0.4\necho_orient=2\n" +
            "ob_r=0.1\nob_g=0.2\nob_b=0.3\nob_a=0.5\nib_a=0.25");
        var renderer = new PresetRenderer(preset, 40, 40);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        var parameters = renderer.ReadFrameParameters();

        Assert.Equal(0.9f, parameters.Decay, 5);
        Assert.Equal(2, parameters.BlurPasses);
        Assert.Equal(0.25f, parameters.DarkenCenter, 5);
        Assert.Equal(1.5f, parameters.Gamma, 5);
        Assert.Equal(0.3f, parameters.ShaderAmount, 5);
        Assert.Equal(1.2f, parameters.EchoZoom, 5);
        Assert.Equal(0.4f, parameters.EchoAlpha, 5);
        Assert.Equal(2, parameters.EchoOrientation);
        Assert.Equal(0.1f, parameters.OuterBorder.Red, 5);
        Assert.Equal(0.5f, parameters.OuterBorder.Alpha, 5);
        Assert.Equal(0.02f, parameters.OuterBorder.Thickness, 5);
        Assert.Equal(0.06f, parameters.InnerBorder.Inset, 5);
        Assert.Equal(0.25f, parameters.InnerBorder.Alpha, 5);
    }

    /// <summary>Unusable values fall back to the documented defaults and clamps.</summary>
    [Fact]
    public void ReadFrameParameters_ClampsOutOfRangeValues()
    {
        var preset = VisualizerPreset.Parse("decay=9\nfGammaAdj=0.001\necho_orient=9\necho_alpha=-1");
        var renderer = new PresetRenderer(preset, 40, 40);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        var parameters = renderer.ReadFrameParameters();

        Assert.Equal(1f, parameters.Decay, 5);
        Assert.Equal(0.1f, parameters.Gamma, 5);
        Assert.Equal(3, parameters.EchoOrientation);
        Assert.Equal(0f, parameters.EchoAlpha, 5);
    }

    /// <summary>The overlay-only frame holds the overlay and nothing else.</summary>
    [Fact]
    public void RenderOverlayFrame_HoldsOnlyTheOverlay()
    {
        var silent = new PresetRenderer(VisualizerPreset.Parse("wave_alpha=0"), 40, 40);
        silent.RenderOverlayFrame(new FakeAudio());
        var silentPixels = silent.OverlayFrame.Pixels;
        for (var index = 0; index < silentPixels.Length; index++)
            Assert.Equal(0f, silentPixels[index]);

        var visible = new PresetRenderer(VisualizerPreset.Parse("wave_alpha=1"), 40, 40);
        visible.RenderOverlayFrame(new FakeAudio());
        var visiblePixels = visible.OverlayFrame.Pixels;
        var anyVisible = false;
        for (var index = 0; index < visiblePixels.Length; index++)
            anyVisible |= visiblePixels[index] > 0f;
        Assert.True(anyVisible, "the overlay frame should hold the waveform");
    }

    /// <summary>A source with fixed levels and a sine waveform.</summary>
    private sealed class FakeAudio : IVisualizerAudioSource
    {
        private readonly float[] _bands = [0.6f, 0.5f, 0.4f, 0.3f];
        private readonly float[] _waveform = new float[64];

        /// <summary>Creates the source.</summary>
        public FakeAudio()
        {
            for (var index = 0; index < _waveform.Length; index++)
                _waveform[index] = (float)Math.Sin(index * 0.2);
        }

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _waveform;

        /// <inheritdoc/>
        public float Volume => _bands[0];

        /// <inheritdoc/>
        public float Bass => _bands[0];

        /// <inheritdoc/>
        public float Mid => _bands[1];

        /// <inheritdoc/>
        public float Treble => _bands[2];
    }
}

