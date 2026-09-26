using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies how the numbered expression parts of a preset are joined, which real presets depend on
/// in both directions: some split one expression across parts, others rely on a separator between
/// statements. It also covers the shared Milkdrop memory buffer.
/// </summary>
public sealed class PresetBlockJoiningTests
{
    /// <summary>A part that ends with an operator is continued by the next part.</summary>
    [Fact]
    public void Parse_JoinsPartsThatSplitAnExpression()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=q1 = 1 +\nper_frame_2=2;");

        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(3f, Run(preset, "q1"));
    }

    /// <summary>Parts that carry no separator get one between their statements.</summary>
    [Fact]
    public void Parse_InsertsASeparatorBetweenStatements()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=q1 = 1\nper_frame_2=q2 = 2");

        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(1f, Run(preset, "q1"));
        Assert.Equal(2f, Run(preset, "q2"));
    }

    /// <summary>A part that brings its own separator is not given a second one.</summary>
    [Fact]
    public void Parse_KeepsAnExistingSeparator()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=q1 = 1;\nper_frame_2=q2 = 2;");

        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(2f, Run(preset, "q2"));
    }

    /// <summary>The shared memory buffer round-trips a value.</summary>
    [Fact]
    public void Parse_ReadsAndWritesTheMegaBuffer()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=gmegabuf(5, 42);\nper_frame_2=q1 = megabuf(5);");

        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(42f, Run(preset, "q1"));
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
