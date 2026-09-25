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

    /// <summary>
    /// Milkdrop's band variables are relative to their long-term average, so a loud burst after a
    /// quiet passage exceeds one and a condition such as <c>above(bass, 1.2)</c> can fire.
    /// </summary>
    [Fact]
    public void Analyze_RelativeBandsExceedOneOnABurst()
    {
        var analyzer = new AudioSpectrumAnalyzer(44_100);
        var block = new float[2048 * 2];

        for (var frame = 0; frame < 150; frame++)
        {
            Fill(block, 0.02f);
            analyzer.Analyze(block, 1d / 60d);
        }

        Fill(block, 0.9f);
        analyzer.Analyze(block, 1d / 60d);

        Assert.True(analyzer.BassRelative > 1.2f, $"bass={analyzer.BassRelative:F3}");
        Assert.True(analyzer.BassAttRelative > 1f, $"bass_att={analyzer.BassAttRelative:F3}");
    }

    /// <summary>Fills an interleaved block with a low-frequency tone at the given amplitude.</summary>
    /// <param name="block">Interleaved stereo block.</param>
    /// <param name="amplitude">Peak amplitude.</param>
    private static void Fill(float[] block, float amplitude)
    {
        for (var index = 0; index < block.Length; index += 2)
        {
            var value = MathF.Sin(index * 0.05f) * amplitude;
            block[index] = value;
            block[index + 1] = value;
        }
    }

    /// <summary>The frame size and aspect ratio are published to the preset.</summary>
    [Fact]
    public void RenderFrame_PublishesTheFrameGeometry()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Create("test", null, null), 80, 40);

        renderer.RenderFrame(FakeAudio.Silent, 1d / 60d);

        Assert.Equal(80f, renderer.ReadVariable("pixelsx"));
        Assert.Equal(40f, renderer.ReadVariable("pixelsy"));
        // Milkdrop binds aspectx/aspecty to the *inverse* factors (milkdropfs.cpp:
        // var_pf_aspectx = m_fInvAspectX), so a landscape 80x40 frame reports (1, 2), not (1, 0.5).
        Assert.Equal(1f, renderer.ReadVariable("aspectx"));
        Assert.Equal(2f, renderer.ReadVariable("aspecty"));
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
        // The overlay only seeds the feedback on the first frame, so the measured feedback carries
        // the trace without the freshly drawn overlay masking the blur.
        const string body =
            "decay = 1;\nwave_a = 0;\nwavecode_0_enabled=1\nwavecode_0_scaling=100\n" +
            "wave_0_per_point_1=x = 0.5;\nwave_0_per_frame_1=a = max(0, 1 - frame);\n";
        var sharp = RenderFeedbackSource(body);
        var blurred = RenderFeedbackSource(body + "per_frame_1=blur1 = 0; blur2 = 3;\n");

        Assert.True(Variance(blurred) < Variance(sharp));
    }

    /// <summary>Renders a preset and returns the feedback frame the next warp would sample.</summary>
    /// <param name="text">Preset text.</param>
    /// <returns>The feedback pixels.</returns>
    private static float[] RenderFeedbackSource(string text)
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse(text), 40, 40);
        for (var frame = 0; frame < 4; frame++)
            renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        return renderer.MeshSource.Pixels.ToArray();
    }

    /// <summary>Milkdrop gamma above one is a brightness gain on the display image.</summary>
    [Fact]
    public void RenderFrame_GammaBrightensTheDisplay()
    {
        var plain = RenderFeedback(string.Empty);
        var brighter = RenderFeedback("fGammaAdj = 2.5;");

        Assert.True(Mean(brighter) > Mean(plain));
    }

    /// <summary>The decay is applied by the warp, so a lower decay dims the frame it carries.</summary>
    [Fact]
    public void RenderFrame_DecayDimsTheWarpedFrame()
    {
        var full = RenderFeedback(string.Empty);
        var faded = RenderFeedback("decay = 0.5;");

        Assert.True(Mean(faded) < Mean(full));
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
        var moved = RenderOverlay(
            "wavecode_0_enabled=1\nwavecode_0_scaling=100\nwave_0_per_point_1=x = 0.2;");

        Assert.True(MeanDifference(plain, moved) > 0.001f);
    }

    /// <summary>The per-frame waveform block can change the waveform colour.</summary>
    [Fact]
    public void RenderFrame_UsesTheWavePerFrameBlock()
    {
        const string wave = "wave_a=0\nwavecode_0_enabled=1\nwave_0_per_point_1=x=sample;y=0.5;\n";
        var white = RenderOverlay(wave);
        var red = RenderOverlay(wave + "wave_0_per_frame_1=g=0;b=0;");

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

    /// <summary>Dots and a connected line draw different pixels.</summary>
    [Fact]
    public void RenderFrame_WaveDotsDifferFromALine()
    {
        var line = RenderOverlay(string.Empty);
        var dots = RenderOverlay("wave_dots=1");

        Assert.True(MeanDifference(line, dots) > 0.001f);
    }

    /// <summary>A thick wave covers more rows than a thin one.</summary>
    [Fact]
    public void RenderFrame_ThickWaveDrawsMore()
    {
        var thin = RenderOverlay(string.Empty);
        var thick = RenderOverlay("wave_thick=1");

        Assert.True(Mean(thick) > Mean(thin));
    }

    /// <summary>The double line draws both channels, so it covers more than the single line.</summary>
    [Fact]
    public void RenderFrame_DoubleLineCoversMoreThanTheLine()
    {
        var single = RenderOverlay("wave_mode=6");
        var doubled = RenderOverlay("wave_mode=7");

        Assert.True(Mean(doubled) > Mean(single), $"single={Mean(single):F4} doubled={Mean(doubled):F4}");
    }

    /// <summary>A declared second waveform slot is drawn as well.</summary>
    [Fact]
    public void RenderFrame_DrawsDeclaredAdditionalWaves()
    {
        var single = RenderOverlay(string.Empty);
        var twoWaves = RenderOverlay(
            "wavecode_1_enabled=1\nwavecode_1_scaling=100\nwave_1_per_point_1=x = 0.8;");

        Assert.True(MeanDifference(single, twoWaves) > 0.001f);
    }

    /// <summary>A custom waveform is only drawn when its wavecode_N_enabled key is set.</summary>
    [Fact]
    public void RenderFrame_CustomWaveNeedsTheEnabledFlag()
    {
        var plain = RenderOverlay(string.Empty);
        var without = RenderOverlay("wave_0_per_point_1=x = 0.2;");

        Assert.Equal(Mean(plain), Mean(without), 5);
    }

    /// <summary>A waveform that asks for the spectrum draws nothing when there is no spectrum data.</summary>
    [Fact]
    public void RenderFrame_SpectrumWaveNeedsSpectrumData()
    {
        var plain = RenderOverlay(string.Empty);
        var spectrum = RenderOverlay(
            "wavecode_0_enabled=1\nwavecode_0_bSpectrum=1\nwavecode_0_scaling=100\nwave_0_per_point_1=x = 0.2;");

        Assert.Equal(Mean(plain), Mean(spectrum), 5);
    }

    /// <summary>A textured shape samples the frame instead of using the gradient colours.</summary>
    [Fact]
    public void RenderFrame_TexturedShapeSamplesTheFrame()
    {
        const string shape =
            "shapecode_0_enabled=1\nshapecode_0_x=0.5\nshapecode_0_y=0.5\nshapecode_0_rad=0.4\n" +
            "shapecode_0_a=1\nshapecode_0_a2=1\nshapecode_0_border_a=0\n";
        var gradient = RenderOverlay("wave_a=0\n" + shape);
        var textured = RenderOverlay("wave_a=0\n" + shape + "shapecode_0_textured=1\n");

        // The frame the overlay composites onto is black here, so the textured shape is black too.
        Assert.True(MeanDifference(gradient, textured) > 0.001f);
        Assert.Equal(0f, Mean(textured), 5);
    }

    /// <summary>A textured shape samples the previous feedback, not the current warp output.</summary>
    [Fact]
    public void RenderFrame_TexturedShapeSamplesPreviousFeedback()
    {
        var preset = VisualizerPreset.Parse(
            """
            [preset00]
            fDecay=1
            fWaveAlpha=0
            shapecode_0_enabled=1
            shapecode_0_sides=4
            shapecode_0_x=0.5
            shapecode_0_y=0.5
            shapecode_0_rad=1
            shapecode_0_textured=1
            shapecode_0_a=1
            shapecode_0_a2=1
            shapecode_0_r=1
            shapecode_0_g=1
            shapecode_0_b=1
            shapecode_0_r2=1
            shapecode_0_g2=1
            shapecode_0_b2=1
            warp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return float4(0, 0, 1, 1); }
            """);
        using var renderer = new PresetRenderer(preset, 64, 64);
        var previous = renderer.MeshSource.Pixels;
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
        {
            var offset = ((y * 64) + x) * 4;
            previous[offset] = 0.5f;
            previous[offset + 3] = 1f;
        }

        renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        var centre = ((32 * 64) + 32) * 4;
        Assert.True(renderer.MeshSource.Pixels[centre] > 0.3f);
        Assert.True(renderer.MeshSource.Pixels[centre + 2] < 0.1f);
    }

    /// <summary>
    /// The legacy final composite multiplies the frame by its animated hue shade, whose per-channel
    /// value stays between a half and one; a comp shader replaces that path entirely.
    /// </summary>
    [Fact]
    public void RenderFrame_LegacyCompositeAppliesTheHueShade()
    {
        var preset = VisualizerPreset.Parse(
            "decay=1\nwave_a=0\nfShader=1\nwarp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return float4(1, 1, 1, 1); }");
        var renderer = new PresetRenderer(preset, 32, 32);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        var pixels = renderer.Output.Pixels;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            Assert.InRange(pixels[index], 0.49f, 1.01f);
            Assert.InRange(pixels[index + 1], 0.49f, 1.01f);
            Assert.InRange(pixels[index + 2], 0.49f, 1.01f);
        }

        Assert.True(Mean(pixels) < 1f);
    }

    /// <summary>A zero shader amount leaves the legacy composite white as in MilkDrop.</summary>
    [Fact]
    public void RenderFrame_ZeroShaderAmountDoesNotTint()
    {
        var preset = VisualizerPreset.Parse(
            "decay=1\nwave_a=0\nfShader=0\nwarp_1=float4 main(float2 uv : TEXCOORD0) : COLOR { return float4(1, 1, 1, 1); }");
        var renderer = new PresetRenderer(preset, 32, 32);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);
        Assert.True(renderer.Output.GetPixel(16, 16, 0) > 0.99f);
        Assert.True(renderer.Output.GetPixel(16, 16, 1) > 0.99f);
        Assert.True(renderer.Output.GetPixel(16, 16, 2) > 0.99f);
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
