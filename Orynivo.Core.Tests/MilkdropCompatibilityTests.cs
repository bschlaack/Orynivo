using Orynivo.Audio;
using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the Milkdrop compatibility work of phase 38a: the initialisation blocks, the
/// standard variable set, the full motion parameters, the post-processing stages, and the
/// waveform and shape programs. Every assertion is a deterministic property of the parsed
/// preset or of a rendered frame, so a regression is caught here instead of by eye.
/// </summary>
public sealed class MilkdropCompatibilityTests
{
    /// <summary>Every initialisation block of a Milkdrop preset is parsed.</summary>
    [Fact]
    public void Parse_ReadsEveryInitBlock()
    {
        var preset = VisualizerPreset.Parse(
            """
            [preset00]
            per_frame_init_1=q1 = 7;
            per_pixel_init_1=q2 = 8;
            wave_0_init_1=q3 = 9;
            shape_0_init_1=q4 = 10;
            """);

        Assert.False(preset.PerFrameInit.IsEmpty);
        Assert.False(preset.PerPixelInit.IsEmpty);
        Assert.False(preset.Waves[0].Init.IsEmpty);
        Assert.False(preset.Shapes[0].Init.IsEmpty);
    }

    /// <summary>Section headers are skipped and the four waveforms always exist.</summary>
    [Fact]
    public void Parse_AcceptsMilkdropSectionHeaders()
    {
        var preset = VisualizerPreset.Parse("[preset00]\nname=Example\nper_frame_1=q1 = 1;\n");

        Assert.Equal("Example", preset.Name);
        Assert.False(preset.PerFrame.IsEmpty);
        Assert.Equal(4, preset.Waves.Count);
    }

    /// <summary>The standard variable set is registered even when a preset mentions none.</summary>
    [Fact]
    public void Parse_RegistersTheStandardVariables()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=q1 = 1;");

        foreach (var name in new[]
                 {
                     "time", "fps", "frame", "monitor", "bass_att", "mid_att", "treb_att",
                     "aspectx", "aspecty", "pixelsx", "pixelsy", "zoomexp", "rot", "cx", "cy",
                     "dx", "dy", "sx", "sy", "blur1", "blur2", "blur3", "darken_center",
                     "fGammaAdj", "wave_mode", "wave_mystery", "ob_a", "ib_a", "echo_alpha",
                     "mv_x", "q32", "b8"
                 })
        {
            Assert.True(preset.Layout.IndexOf(name) >= 0, $"missing variable {name}");
        }
    }

    /// <summary>The one-time blocks run exactly once, no matter how many frames render.</summary>
    [Fact]
    public void RenderFrame_RunsTheInitBlocksOnce()
    {
        var preset = VisualizerPreset.Parse("per_frame_init_1=q1 = q1 + 1;\nper_pixel_init_1=q2 = q2 + 1;");
        var renderer = new PresetRenderer(preset, 16, 9);

        for (var frame = 0; frame < 3; frame++)
            renderer.RenderFrame(FakeAudio.Silent, 1d / 60d);

        Assert.Equal(1f, renderer.ReadVariable("q1"));
        Assert.Equal(1f, renderer.ReadVariable("q2"));
    }

    /// <summary>The smoothed bands trail the raw bands.</summary>
    [Fact]
    public void RenderFrame_SmoothsTheAttenuatedBands()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Create("test", null, null), 16, 9);

        renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        Assert.True(renderer.ReadVariable("bass_att") < renderer.ReadVariable("bass"));
        Assert.True(renderer.ReadVariable("bass_att") > 0f);
    }

    /// <summary>The frame size and aspect ratio are published to the preset.</summary>
    [Fact]
    public void RenderFrame_PublishesTheFrameGeometry()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Create("test", null, null), 80, 40);

        renderer.RenderFrame(FakeAudio.Silent, 1d / 60d);

        Assert.Equal(80f, renderer.ReadVariable("pixelsx"));
        Assert.Equal(40f, renderer.ReadVariable("pixelsy"));
        Assert.Equal(2f, renderer.ReadVariable("aspectx"));
    }

    /// <summary>Rotation, zoom, and stretch each change the sampled feedback.</summary>
    [Theory]
    [InlineData("rot = 0.6;")]
    [InlineData("zoom = 1.35;")]
    [InlineData("sx = 0.6;")]
    [InlineData("cx = 0.4; zoom = 1.2;")]
    [InlineData("dx = 0.3;")]
    public void RenderFrame_AppliesTheMotionParameters(string perFrame)
    {
        var still = RenderFeedback(string.Empty);
        var moved = RenderFeedback(perFrame);

        Assert.True(MeanDifference(still, moved) > 0.001f, $"{perFrame} did not change the frame");
    }

    /// <summary>The blur count from the standard keys smooths the feedback further.</summary>
    [Fact]
    public void RenderFrame_BlurKeysSmoothTheFeedback()
    {
        var sharp = RenderFeedback(string.Empty);
        var blurred = RenderFeedback("blur1 = 0; blur2 = 3;");

        Assert.True(Variance(blurred) < Variance(sharp));
    }

    /// <summary>A gamma above one darkens the feedback image.</summary>
    [Fact]
    public void RenderFrame_GammaDarkensTheFeedback()
    {
        var plain = RenderFeedback(string.Empty);
        var darker = RenderFeedback("fGammaAdj = 2.5;");

        Assert.True(Mean(darker) < Mean(plain));
    }

    /// <summary>Darken centre pulls the middle of the frame down more than its corner.</summary>
    [Fact]
    public void RenderFrame_DarkenCenterTargetsTheMiddle()
    {
        var preset = VisualizerPreset.Parse(
            "decay = 1;\nper_frame_1=darken_center = 1;\nper_frame_2=wave_a = 0;");
        var renderer = new PresetRenderer(preset, 40, 40);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        var pixels = renderer.Output.Pixels;
        var middle = Luminance(pixels, (20 * 40) + 20);
        var corner = Luminance(pixels, (2 * 40) + 2);

        Assert.True(middle < corner);
    }

    /// <summary>The waveform honours the first waveform's per-point program.</summary>
    [Fact]
    public void RenderFrame_UsesTheWavePrograms()
    {
        var plain = RenderOverlay(string.Empty);
        var moved = RenderOverlay("wave_0_per_point_1=y = y * 3;");

        Assert.True(MeanDifference(plain, moved) > 0.001f);
    }

    /// <summary>The per-frame waveform block can change the waveform colour.</summary>
    [Fact]
    public void RenderFrame_UsesTheWavePerFrameBlock()
    {
        var white = RenderOverlay(string.Empty);
        var red = RenderOverlay("wave_0_per_frame_1=wave_g = 0; wave_b = 0;");

        Assert.True(MeanDifference(white, red) > 0.001f);
    }

    /// <summary>Renders a few frames with feedback so the motion parameters have something to move.</summary>
    /// <param name="perFrame">Per-frame expression to add.</param>
    /// <returns>The rendered pixels.</returns>
    private static float[] RenderFeedback(string perFrame)
    {
        // The wave is what the feedback loop carries, so these tests need it visible; hiding it used
        // to leave the spectrum as the only content, which made them measure the overlay instead.
        var text = "decay = 1;\nper_frame_1=wave_a = 1;\n" +
                   (perFrame.Length == 0 ? string.Empty : "per_frame_2=" + perFrame + "\n");
        var renderer = new PresetRenderer(VisualizerPreset.Parse(text), 40, 40);
        for (var frame = 0; frame < 4; frame++)
            renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        return renderer.Output.Pixels.ToArray();
    }

    /// <summary>Renders the overlay only, without the feedback warp.</summary>
    /// <param name="extra">Additional preset text.</param>
    /// <returns>The rendered pixels.</returns>
    private static float[] RenderOverlay(string extra)
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse("decay = 0;\n" + extra), 40, 40);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);
        return renderer.Output.Pixels.ToArray();
    }

    private static float Mean(ReadOnlySpan<float> pixels)
    {
        var total = 0f;
        for (var index = 0; index < pixels.Length; index += 4)
            total += Luminance(pixels, index);

        return total / Math.Max(1, pixels.Length / 4);
    }

    private static float Variance(ReadOnlySpan<float> pixels)
    {
        var mean = Mean(pixels);
        var total = 0f;
        var count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var delta = Luminance(pixels, index) - mean;
            total += delta * delta;
            count++;
        }

        return total / Math.Max(1, count);
    }

    private static float MeanDifference(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var total = 0f;
        for (var index = 0; index < left.Length; index += 4)
            total += Math.Abs(Luminance(left, index) - Luminance(right, index));

        return total / Math.Max(1, left.Length / 4);
    }

    /// <summary>The circular wave modes draw a different picture than the line modes.</summary>
    [Fact]
    public void RenderFrame_CircularWaveModeChangesThePicture()
    {
        var line = RenderOverlay("wave_mode=3");
        var circular = RenderOverlay("wave_mode=0");

        Assert.True(MeanDifference(line, circular) > 0.001f);
    }

    /// <summary>Dots draw fewer pixels than a connected line.</summary>
    [Fact]
    public void RenderFrame_WaveDotsDrawLessThanALine()
    {
        var line = RenderOverlay(string.Empty);
        var dots = RenderOverlay("wave_dots=1");

        Assert.True(Mean(dots) < Mean(line));
    }

    /// <summary>A thick wave covers more rows than a thin one.</summary>
    [Fact]
    public void RenderFrame_ThickWaveDrawsMore()
    {
        var thin = RenderOverlay(string.Empty);
        var thick = RenderOverlay("wave_thick=1");

        Assert.True(Mean(thick) > Mean(thin));
    }

    /// <summary>The doubled modes mirror the wave around its centre.</summary>
    [Fact]
    public void RenderFrame_DoubledWaveModeMirrors()
    {
        var single = RenderOverlay("wave_mode=3");
        var doubled = RenderOverlay("wave_mode=2");

        Assert.True(Mean(doubled) > Mean(single));
    }

    /// <summary>A declared second waveform slot is drawn as well.</summary>
    [Fact]
    public void RenderFrame_DrawsDeclaredAdditionalWaves()
    {
        var single = RenderOverlay(string.Empty);
        var twoWaves = RenderOverlay("wave_1_per_point_1=y = y + 0.4;");

        Assert.True(MeanDifference(single, twoWaves) > 0.001f);
    }

    /// <summary>The outer border paints the frame edges.</summary>
    [Fact]
    public void RenderFrame_OuterBorderPaintsTheEdge()
    {
        var plain = RenderFeedback(string.Empty);
        var bordered = RenderFeedback("ob_a=1\nob_r=1\nob_g=0\nob_b=0");

        Assert.True(MeanDifference(plain, bordered) > 0.001f);
    }

    /// <summary>The inner border is inset, so it leaves the very corner alone.</summary>
    [Fact]
    public void RenderFrame_InnerBorderIsInset()
    {
        var preset = VisualizerPreset.Parse(
            "decay = 1;\nper_frame_1=wave_a = 0; ib_a = 1; ib_g = 1; ib_r = 0; ib_b = 0;");
        var renderer = new PresetRenderer(preset, 40, 40);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        var pixels = renderer.Output.Pixels;
        var corner = Luminance(pixels, (0 * 40) + 0);
        var inset = Luminance(pixels, (2 * 40) + 2);
        Assert.True(corner < inset);
    }

    /// <summary>The video echo blends a scaled copy of the frame back over itself.</summary>
    [Fact]
    public void RenderFrame_VideoEchoBlendsAScaledCopy()
    {
        var plain = RenderFeedback(string.Empty);
        var echoed = RenderFeedback("echo_alpha=1\necho_zoom=2");

        Assert.True(MeanDifference(plain, echoed) > 0.001f);
    }

    /// <summary>Motion vectors are only drawn when their length is set.</summary>
    [Fact]
    public void RenderFrame_MotionVectorsDrawWhenEnabled()
    {
        // These are per-frame expressions, so they write the engine variable directly; the preset
        // key bMotionVectors is mapped onto it when a preset declares it as a key.
        var plain = RenderFeedback("mv_enabled=0; mv_l=1;");
        var vectors = RenderFeedback("mv_enabled=1; mv_l=1;");
        var lengthOnly = RenderFeedback("mv_l=1;");

        Assert.True(MeanDifference(plain, vectors) > 0.0005f);

        // A length without the enable flag must stay invisible. Sharing one variable for the flag
        // and the length drew a grid of stray lines on real presets that only set a length.
        Assert.True(MeanDifference(plain, lengthOnly) <= 0.0005f);
    }

    /// <summary>A deterministic audio source with content on every band.</summary>
    private sealed class FakeAudio : IVisualizerAudioSource
    {
        /// <summary>A source that reports silence.</summary>
        public static readonly FakeAudio Silent = new(0f);

        private readonly float[] _bands;
        private readonly float[] _waveform;

        /// <summary>Creates a source whose levels are all the given value.</summary>
        /// <param name="level">Band level in the range zero to one.</param>
        public FakeAudio(float level = 0.6f)
        {
            _bands = [level, level * 0.8f, level * 0.6f, level * 0.4f];
            _waveform = new float[64];
            for (var index = 0; index < _waveform.Length; index++)
                _waveform[index] = level * MathF.Sin(index * 0.3f);
        }

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _waveform;

        /// <inheritdoc/>
        public float Bass => _bands[0];

        /// <inheritdoc/>
        public float Mid => _bands[1];

        /// <inheritdoc/>
        public float Treble => _bands[2];

        /// <inheritdoc/>
        public float Volume => _bands[0];
    }

    private static float Luminance(ReadOnlySpan<float> pixels, int index) =>
        (pixels[index] * 0.3f) + (pixels[index + 1] * 0.6f) + (pixels[index + 2] * 0.1f);
}
