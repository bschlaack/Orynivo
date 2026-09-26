using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the compound assignments of the preset expression language. Presets write
/// <c>n += 1</c>, <c>zoom -= 0.03</c>, and <c>gmegabuf(n) += x</c> constantly, and a lexer that
/// splits them into an operator and an "=" makes the whole block fail, which silently drops that
/// preset's motion.
/// </summary>
public sealed class PresetCompoundAssignmentTests
{
    /// <summary>Every arithmetic compound assignment updates its variable.</summary>
    [Fact]
    public void Parse_AppliesEachCompoundAssignment()
    {
        var preset = VisualizerPreset.Parse(
            "per_frame_1=q1 = 10; q2 = 10; q3 = 10; q4 = 10; q5 = 10;\n" +
            "per_frame_2=q1 += 5; q2 -= 4; q3 *= 3; q4 /= 4; q5 %= 3;");

        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(15f, Run(preset, "q1"));
        Assert.Equal(6f, Run(preset, "q2"));
        Assert.Equal(30f, Run(preset, "q3"));
        Assert.Equal(2.5f, Run(preset, "q4"));
        Assert.Equal(1f, Run(preset, "q5"));
    }

    /// <summary>A compound assignment may read its own target, as in "n += 1" inside a loop.</summary>
    [Fact]
    public void Parse_RepeatsACompoundAssignment()
    {
        var preset = VisualizerPreset.Parse("per_frame_1=n = 0;\nper_frame_2=loop(6, n += 2);");

        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(12f, Run(preset, "n"));
    }

    /// <summary>The shared memory buffer accepts a compound assignment too.</summary>
    [Fact]
    public void Parse_AppliesACompoundAssignmentToTheMegaBuffer()
    {
        var preset = VisualizerPreset.Parse(
            "per_frame_1=gmegabuf(3) = 1;\nper_frame_2=loop(4, gmegabuf(3) += 2);\nper_frame_3=q1 = megabuf(3);");

        Assert.Empty(preset.FailedBlocks);
        Assert.Equal(9f, Run(preset, "q1"));
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
