using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies the INI-style preset surface that third-party presets use.</summary>
public sealed class VisualizerPresetTests
{
    /// <summary>Name, parameters, and expression blocks are read.</summary>
    [Fact]
    public void Parse_ReadsTheSupportedKeys()
    {
        const string text = """
            [preset00]
            name=Plasma Test
            decay=0.9
            zoom=1.1
            warp=0.5
            blur_level=2
            wave_alpha=0.5
            wave_scale=0.4
            per_frame_init_1=q1 = 0.5;
            per_frame_1=q1 = q1 + bass;
            per_frame_2=decay = 0.8;
            per_pixel_1=x = x + q1 * 0.1;
            """;

        var preset = VisualizerPreset.Parse(text);

        Assert.Equal("Plasma Test", preset.Name);
        Assert.Equal(0.9f, preset.Decay, 5);
        Assert.Equal(1.1f, preset.Zoom, 5);
        Assert.Equal(0.5f, preset.Warp, 5);
        Assert.Equal(2, preset.BlurLevel);
        Assert.Equal(0.5f, preset.WaveAlpha, 5);
        Assert.Equal(0.4f, preset.WaveScale, 5);
        Assert.False(preset.PerFrameInit.IsEmpty);
        Assert.False(preset.PerFrame.IsEmpty);
        Assert.False(preset.PerPixel.IsEmpty);
    }

    /// <summary>MilkDrop wave scales above one retain their full value.</summary>
    [Fact]
    public void Parse_PreservesLargeMilkdropWaveScale()
    {
        var preset = VisualizerPreset.Parse("fWaveScale=28.599");

        Assert.Equal(28.599f, preset.WaveScale, 3);
    }

    /// <summary>Every expression block shares one layout, so q1 carries across stages.</summary>
    [Fact]
    public void Parse_SharesTheLayoutBetweenStages()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=q1 = 0.25;\nper_pixel_1=x = q1;");

        var slots = new float[preset.Layout.Count];
        preset.PerFrame.Execute(slots);
        preset.PerPixel.Execute(slots);

        Assert.Equal(preset.Layout.IndexOf("q1"), preset.PerFrame.IndexOf("q1"));
        Assert.Equal(0.25f, slots[preset.Layout.IndexOf("x")], 5);
    }

    /// <summary>The parameters are always available to the renderer, even when unused.</summary>
    [Fact]
    public void Parse_RegistersTheParametersInTheLayout()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=q1 = 1;");

        Assert.True(preset.Layout.IndexOf("decay") >= 0);
        Assert.True(preset.Layout.IndexOf("zoom") >= 0);
        Assert.True(preset.Layout.IndexOf("warp") >= 0);
    }

    /// <summary>Unknown keys are ignored so a foreign preset still loads.</summary>
    [Fact]
    public void Parse_IgnoresUnknownKeys()
    {
        var preset = VisualizerPreset.Parse("fVideoEchoZoom=1.5\nsome_future_key=abc\nper_frame_1=q1 = 1;");

        Assert.False(preset.PerFrame.IsEmpty);
        Assert.Equal("Preset", preset.Name);
    }

    /// <summary>An unusable value falls back to the documented default.</summary>
    [Fact]
    public void Parse_FallsBackForUnusableValues()
    {
        var preset = VisualizerPreset.Parse("decay=not-a-number\nzoom=-4\nblur_level=99");

        Assert.Equal(0.96f, preset.Decay, 5);
        Assert.Equal(0.05f, preset.Zoom, 5);
        Assert.Equal(4, preset.BlurLevel);
    }


    /// <summary>
    /// An invalid expression block is skipped and recorded instead of rejecting the preset, so one
    /// unsupported construct cannot replace a whole preset with the fallback.
    /// </summary>
    [Fact]
    public void Parse_SkipsAnInvalidExpressionBlock()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=zoom = 1.01;\nper_pixel_1=x = unknown(1);");

        Assert.Contains(
            preset.FailedBlocks,
            block => block.StartsWith("per_pixel", StringComparison.Ordinal));
        Assert.True(preset.PerPixel.IsEmpty);
        // The block that does compile still works.
        Assert.False(preset.PerFrame.IsEmpty);
    }

    /// <summary>A preset whose blocks all compile reports no failure.</summary>
    [Fact]
    public void Parse_ReportsNoFailedBlocksWhenEverythingCompiles()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=zoom = 1.01;\nper_pixel_1=x = x + 0.01;");

        Assert.Empty(preset.FailedBlocks);
    }

    /// <summary>An empty document still produces a usable preset.</summary>
    [Fact]
    public void Parse_AcceptsAnEmptyDocument()
    {
        var preset = VisualizerPreset.Parse(string.Empty, "Fallback");

        Assert.Equal("Fallback", preset.Name);
        Assert.True(preset.PerFrame.IsEmpty);
        Assert.True(preset.PerPixel.IsEmpty);
    }
}

