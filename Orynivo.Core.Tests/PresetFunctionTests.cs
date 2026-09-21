using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the Milkdrop functions that real presets rely on and that used to be reported as
/// unknown, which silently dropped the block that carries a preset's motion. The comparisons
/// yield one or zero because presets use them as numbers.
/// </summary>
public sealed class PresetFunctionTests
{
    /// <summary>above, below, and equal produce one or zero.</summary>
    [Fact]
    public void Comparisons_YieldOneOrZero()
    {
        Assert.Equal(1f, Run("q1 = above(2, 1);", "q1"));
        Assert.Equal(0f, Run("q1 = above(1, 2);", "q1"));
        Assert.Equal(1f, Run("q1 = below(1, 2);", "q1"));
        Assert.Equal(0f, Run("q1 = below(2, 1);", "q1"));
        Assert.Equal(1f, Run("q1 = equal(2, 2);", "q1"));
        Assert.Equal(0f, Run("q1 = equal(2, 3);", "q1"));
    }

    /// <summary>sqr multiplies its argument by itself.</summary>
    [Fact]
    public void Sqr_SquaresItsArgument()
    {
        Assert.Equal(9f, Run("q1 = sqr(3);", "q1"));
        Assert.Equal(6.25f, Run("q1 = sqr(-2.5);", "q1"));
    }

    /// <summary>sigmoid accepts the second argument some presets pass and ignores it.</summary>
    [Fact]
    public void Sigmoid_AcceptsAnOptionalSecondArgument()
    {
        Assert.Equal(0.5f, Run("q1 = sigmoid(0);", "q1"));
        Assert.Equal(0.5f, Run("q1 = sigmoid(0, 1);", "q1"));
        Assert.True(Run("q1 = sigmoid(10);", "q1") > 0.9f);
    }

    /// <summary>The bitwise functions work on the integer view of their arguments.</summary>
    [Fact]
    public void BitwiseFunctions_OperateOnIntegers()
    {
        Assert.Equal(4f, Run("q1 = band(6, 12);", "q1"));
        Assert.Equal(14f, Run("q1 = bor(6, 12);", "q1"));
        Assert.Equal(-1f, Run("q1 = bnot(0);", "q1"));
    }

    /// <summary>Function and constant names are case-insensitive, as in Milkdrop.</summary>
    [Fact]
    public void Names_AreCaseInsensitive()
    {
        Assert.Equal(0f, Run("q1 = Sin(0);", "q1"));
        Assert.Equal(1f, Run("q1 = Abs(-1);", "q1"));
        Assert.True(Run("q1 = PI;", "q1") > 3.14f);
    }

    /// <summary>Runs one statement and returns the variable it writes.</summary>
    /// <param name="statement">Preset statement.</param>
    /// <param name="variable">Variable to read back.</param>
    /// <returns>The resulting value.</returns>
    private static float Run(string statement, string variable)
    {
        var preset = VisualizerPreset.Parse("per_frame_1=" + statement);
        Assert.Empty(preset.FailedBlocks);
        var renderer = new PresetRenderer(preset, 32, 18);
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
