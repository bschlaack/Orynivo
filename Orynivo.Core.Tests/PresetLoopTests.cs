using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies Milkdrop's <c>loop(count, statements)</c> construct and writing to the shared memory
/// buffer by assigning to the call, which is how real presets build their lookup tables.
/// </summary>
public sealed class PresetLoopTests
{
    /// <summary>A loop repeats its body the requested number of times.</summary>
    [Fact]
    public void Parse_RunsALoopBodyRepeatedly()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=i = 0;\nper_frame_2=loop(5, i = i + 1);");

        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(5f, Run(preset, "i"));
    }

    /// <summary>A loop body may hold several semicolon-separated statements.</summary>
    [Fact]
    public void Parse_RunsSeveralStatementsPerIteration()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=i = 0; q1 = 0;\nper_frame_2=loop(4, i = i + 1; q1 = q1 + 2;);");

        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(4f, Run(preset, "i"));
        Assert.Equal(8f, Run(preset, "q1"));
    }

    /// <summary>A preset writes a buffer entry by assigning to the call.</summary>
    [Fact]
    public void Parse_WritesTheMegaBufferByAssignment()
    {
        var preset = VisualizerPreset.Parse(
            "per_frame_1=loop(3, gmegabuf(7) = gmegabuf(7) + 2);\nper_frame_2=q1 = megabuf(7);");

        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(6f, Run(preset, "q1"));
    }

    /// <summary>The iteration count is clamped, so a preset cannot stall a frame.</summary>
    [Fact]
    public void Parse_ClampsTheLoopCount()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=i = 0;\nper_frame_2=loop(1000000, i = i + 1);");

        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(100000f, Run(preset, "i"));
    }

    /// <summary>A per-pixel loop that overruns the warp budget is left out instead of hanging.</summary>
    [Fact]
    public void RenderFrame_SuspendsAPerPixelLoopThatOverrunsTheWarpBudget()
    {
        // A loop inside per_pixel runs once per screen pixel, which is what made a single frame
        // take seconds and froze the window; the stage budget drops the program instead.
        var preset = VisualizerPreset.Parse("decay=1\nper_pixel_1=loop(200000, q1 = q1 + 1);");
        var renderer = new PresetRenderer(preset, 64, 36) { WarpStageBudgetMilliseconds = 0.001d };

        renderer.RenderFrame(new Silent(), 1d / 60d);

        Assert.True(renderer.PerPixelSuspended);
    }

    /// <summary>Nested loops are bounded in total, not only per loop.</summary>
    [Fact]
    public void RenderFrame_BoundsNestedLoopsInTotal()
    {
        // 20000 x 20000 is four hundred million iterations. The per-loop clamp cannot see that, and
        // it used to freeze the window without an exception to catch.
        var preset = VisualizerPreset.Parse("per_frame_1=q1 = 0;\nper_frame_2=loop(20000, loop(20000, q1 = q1 + 1));");
        var renderer = new PresetRenderer(preset, 16, 9);

        renderer.RenderFrame(new Silent(), 1d / 60d);

        Assert.NotNull(renderer.PresetError);
        Assert.Contains("looped too often", renderer.PresetError);
    }
    /// <summary>Runs a parsed preset and returns one of its variables.</summary>
    /// <param name="preset">Preset to run.</param>
    /// <param name="variable">Variable to read back.</param>
    /// <returns>The resulting value.</returns>
    private static float Run(VisualizerPreset preset, string variable)
    {
        var renderer = new PresetRenderer(preset, 16, 9);
        renderer.RenderFrame(new Silent(), 1d / 60d);
        return renderer.ReadVariable(variable);
    }

    /// <summary>An audio source that reports silence.</summary>
    private sealed class Silent : IVisualizerAudioSource
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
