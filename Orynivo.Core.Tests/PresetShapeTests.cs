using Orynivo.Audio;
using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies custom shapes and the per-point waveform program: the last stage that turns a
/// preset from a warped picture into the Milkdrop-style layered look.
/// </summary>
public sealed class PresetShapeTests
{
    /// <summary>Shape keys are parsed into a shape with the expected defaults.</summary>
    [Fact]
    public void Parse_ReadsShapeKeys()
    {
        const string text = """
            name=Shapes
            shape_0_sides=6
            shape_0_x=-0.5
            shape_0_y=0.25
            shape_0_rad=0.3
            shape_0_r=1
            shape_0_g=0
            shape_0_b=0
            shape_0_a=0.8
            shape_0_additive=1
            shape_0_per_point_1=x = x * 0.5;
            """;

        var preset = VisualizerPreset.Parse(text);

        var shape = Assert.Single(preset.Shapes);
        Assert.Equal(6, shape.Sides);
        Assert.Equal(-0.5f, shape.X, 5);
        Assert.Equal(0.25f, shape.Y, 5);
        Assert.Equal(0.3f, shape.Radius, 5);
        Assert.Equal(1f, shape.Red, 5);
        Assert.Equal(0f, shape.Green, 5);
        Assert.Equal(0.8f, shape.Alpha, 5);
        Assert.True(shape.Additive);
        Assert.False(shape.PerPoint.IsEmpty);
    }

    /// <summary>Numbered shapes remain visible even when an earlier slot is absent.</summary>
    [Fact]
    public void Parse_ReadsNumberedShapes()
    {
        var preset = VisualizerPreset.Parse("shape_0_sides=3\nshape_1_sides=4\nshape_3_sides=5");

        Assert.Equal(3, preset.Shapes.Count);
    }

    /// <summary>A preset without shape keys has no shapes.</summary>
    [Fact]
    public void Parse_WithoutShapesIsEmpty()
    {
        Assert.Empty(VisualizerPreset.Parse("per_frame_1=q1 = 1;").Shapes);
    }

    /// <summary>A centred shape lights the middle of the frame.</summary>
    [Fact]
    public void RenderFrame_DrawsAShape()
    {
        var preset = VisualizerPreset.Parse("""
            name=Shape
            decay=0
            wave_alpha=0
            shape_0_sides=8
            shape_0_x=0
            shape_0_y=0
            shape_0_rad=0.5
            shape_0_r=1
            shape_0_g=1
            shape_0_b=1
            shape_0_a=1
            shape_0_border_a=0
            """);
        var renderer = new PresetRenderer(preset, 64, 36);

        renderer.RenderFrame(new StubAudio(), 1d / 60d);

        var centre = renderer.Output.GetPixel(32, 18, 0);
        Assert.True(centre > 0.5f);
        Assert.Equal(0f, renderer.Output.GetPixel(1, 1, 0), 5);
    }

    /// <summary>A shape's per-frame program moves it.</summary>
    [Fact]
    public void RenderFrame_HonoursShapePerFrameCode()
    {
        var preset = VisualizerPreset.Parse("""
            name=Moved
            decay=0
            wave_alpha=0
            shape_0_sides=8
            shape_0_x=0
            shape_0_y=0
            shape_0_rad=0.4
            shape_0_r=1
            shape_0_g=1
            shape_0_b=1
            shape_0_a=1
            shape_0_border_a=0
            shape_0_per_frame_1=x = 0.6;
            """);
        var renderer = new PresetRenderer(preset, 64, 36);

        renderer.RenderFrame(new StubAudio(), 1d / 60d);

        Assert.Equal(0f, renderer.Output.GetPixel(32, 18, 0), 5);
        Assert.True(renderer.Output.GetPixel(50, 18, 0) > 0.5f);
    }

    /// <summary>A shape's per-point program reshapes its vertices.</summary>
    [Fact]
    public void RenderFrame_HonoursShapePerPointCode()
    {
        var preset = VisualizerPreset.Parse("""
            name=Squeezed
            decay=0
            wave_alpha=0
            shape_0_sides=12
            shape_0_x=0
            shape_0_y=0
            shape_0_rad=0.8
            shape_0_r=1
            shape_0_g=1
            shape_0_b=1
            shape_0_a=1
            shape_0_border_a=0
            shape_0_per_point_1=x = x * 0.1;
            """);
        var renderer = new PresetRenderer(preset, 64, 36);

        renderer.RenderFrame(new StubAudio(), 1d / 60d);

        // The shape collapses horizontally, so the middle column stays lit while the
        // columns near the original radius do not.
        Assert.True(renderer.Output.GetPixel(32, 18, 0) > 0.5f);
        Assert.Equal(0f, renderer.Output.GetPixel(58, 18, 0), 5);
    }

    /// <summary>The per-point waveform program moves the whole line.</summary>
    [Fact]
    public void RenderFrame_HonoursWavePerPointCode()
    {
        var preset = VisualizerPreset.Parse("""
            name=Line
            decay=0
            wave_alpha=1
            wave_scale=0.5
            per_point_1=y = -1;
            """);
        var renderer = new PresetRenderer(preset, 64, 36);

        renderer.RenderFrame(new StubAudio(), 1d / 60d);

        // The per-point block sees the vertex position in minus-one-to-one space, so y = -1 puts the
        // whole line on the bottom row.
        var bottom = 0f;
        var middle = 0f;
        for (var column = 0; column < 64; column++)
        {
            bottom += renderer.Output.GetPixel(column, 35, 1);
            middle += renderer.Output.GetPixel(column, 18, 1);
        }

        Assert.True(bottom > 0f);
        Assert.Equal(0f, middle, 5);
    }

    /// <summary>
    /// A textured shape's rim texture coordinates follow the reference formula
    /// (<c>milkdropfs.cpp</c>, marked "DON'T TOUCH!"): <c>0.5 + 0.5 * sin(angle + tex_ang + pi/4)</c>.
    /// Orynivo negated the sine, which sampled the captured frame mirrored, so the sign is pinned here.
    /// </summary>
    [Fact]
    public void RenderFrame_TexturedShapeUsesTheReferenceTextureCoordinate()
    {
        var preset = VisualizerPreset.Parse("""
            name=Textured
            decay=0
            wave_alpha=0
            shapecode_0_enabled=1
            shapecode_0_textured=1
            shapecode_0_sides=4
            shapecode_0_rad=0.2
            shapecode_0_tex_zoom=1
            shapecode_0_tex_ang=0
            """);
        var renderer = new PresetRenderer(preset, 64, 36) { CollectShapeFills = true };
        renderer.RenderFrame(new StubAudio(), 1d / 60d);

        var fill = Assert.Single(renderer.ShapeFills);
        Assert.True(fill.Textured);
        Assert.Equal(0.5f, fill.Vertices[0].V, 4);
        // The fan closes by repeating its first rim vertex, so the rim is the count minus two.
        var rimCount = fill.Vertices.Count - 2;
        for (var k = 1; k <= rimCount; k++)
        {
            var angle = (((k - 1) / (float)rimCount) * 2f * MathF.PI) + (MathF.PI * 0.25f);
            Assert.Equal(0.5f + (0.5f * MathF.Sin(angle)), fill.Vertices[k].V, 4);
        }
    }

    /// <summary>Audio source with a fixed spectrum and waveform.</summary>
    private sealed class StubAudio : IVisualizerAudioSource
    {
        public ReadOnlySpan<float> Bands => [];

        public ReadOnlySpan<float> Waveform => WaveformValue;

        public float Bass => 0.5f;

        public float Mid => 0.3f;

        public float Treble => 0.2f;

        public float Volume => 0.4f;

        private float[] WaveformValue { get; } = BuildWaveform();

        private static float[] BuildWaveform()
        {
            var waveform = new float[AudioSpectrumAnalyzer.WaveformPoints];
            for (var index = 0; index < waveform.Length; index++)
                waveform[index] = MathF.Sin(index * 0.4f) * 0.3f;
            return waveform;
        }
    }
}
