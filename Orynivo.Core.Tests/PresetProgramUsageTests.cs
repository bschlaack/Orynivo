using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the variable-usage information the compiler reports for a compiled program. The
/// renderer uses it to skip work a preset never looks at (the polar pair, the motion grid, and
/// the seeded sampling position), so the set has to be conservative: it may report a variable the
/// program does not really use, but it must never miss one.
/// </summary>
public sealed class PresetProgramUsageTests
{
    /// <summary>Read variables are reported.</summary>
    [Fact]
    public void Uses_ReportsReadVariables()
    {
        var program = PresetCompiler.Compile("rad = rad * (1 + bass);");

        Assert.True(program.Uses("rad"));
        Assert.True(program.Uses("bass"));
        Assert.False(program.Uses("ang"));
        Assert.False(program.Uses("q7"));
    }

    /// <summary>Written variables count as used, because the renderer has to read them back.</summary>
    [Fact]
    public void Uses_ReportsWrittenVariables()
    {
        var program = PresetCompiler.Compile("x = 0.5;");

        Assert.True(program.Uses("x"));
        Assert.False(program.Uses("y"));
    }

    /// <summary>An empty program uses nothing.</summary>
    [Fact]
    public void Uses_IsFalseForAnEmptyProgram()
    {
        Assert.Empty(PresetProgram.Empty.ReferencedVariables);
        Assert.False(PresetProgram.Empty.Uses("x"));
    }

    /// <summary>The usage set travels with the parsed preset's per-pixel program.</summary>
    [Fact]
    public void Parse_KeepsTheUsageOfThePerPixelProgram()
    {
        var preset = VisualizerPreset.Parse("per_pixel_1=x = x + cos(ang) * 0.01;");

        Assert.True(preset.PerPixel.Uses("x"));
        Assert.True(preset.PerPixel.Uses("ang"));
        Assert.False(preset.PerPixel.Uses("rad"));
    }
}
