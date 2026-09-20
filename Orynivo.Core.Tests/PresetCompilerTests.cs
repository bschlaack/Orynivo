using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the preset expression language: precedence, functions, conditionals, user
/// variables, and the errors a preset author has to be able to act on.
/// </summary>
public sealed class PresetCompilerTests
{
    /// <summary>An empty or missing source compiles to a program that does nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("// only a comment")]
    public void Compile_TreatsEmptySourceAsAnEmptyProgram(string? source)
    {
        var program = PresetCompiler.Compile(source);

        Assert.True(program.IsEmpty);
        Assert.Empty(program.Variables);
        program.Execute(new float[4]);
    }

    /// <summary>An assignment writes the variable's slot.</summary>
    [Fact]
    public void Execute_AssignsVariables()
    {
        var program = PresetCompiler.Compile("q1 = 0.25;");

        var slots = new float[program.Variables.Count];
        program.Execute(slots);

        Assert.Single(program.Variables);
        Assert.Equal(0.25f, slots[program.IndexOf("q1")]);
    }

    /// <summary>Arithmetic respects the usual precedence.</summary>
    [Fact]
    public void Execute_RespectsPrecedence()
    {
        var program = PresetCompiler.Compile("q1 = 2 + 3 * 4; q2 = (2 + 3) * 4; q3 = 10 - 2 - 3;");

        var slots = new float[program.Variables.Count];
        program.Execute(slots);

        Assert.Equal(14f, slots[program.IndexOf("q1")]);
        Assert.Equal(20f, slots[program.IndexOf("q2")]);
        Assert.Equal(5f, slots[program.IndexOf("q3")]);
    }

    /// <summary>Unary operators and the remainder work as in C.</summary>
    [Fact]
    public void Execute_HandlesUnaryOperatorsAndRemainder()
    {
        var program = PresetCompiler.Compile("q1 = -3 + 1; q2 = !0; q3 = !2; q4 = 7 % 3; q5 = -7 % 3;");

        var slots = new float[program.Variables.Count];
        program.Execute(slots);

        Assert.Equal(-2f, slots[program.IndexOf("q1")]);
        Assert.Equal(1f, slots[program.IndexOf("q2")]);
        Assert.Equal(0f, slots[program.IndexOf("q3")]);
        Assert.Equal(1f, slots[program.IndexOf("q4")]);
        Assert.Equal(-1f, slots[program.IndexOf("q5")]);
    }

    /// <summary>Comparisons and logical operators yield one or zero.</summary>
    [Fact]
    public void Execute_EvaluatesComparisonsAndLogic()
    {
        var program = PresetCompiler.Compile(
            "q1 = 2 > 1; q2 = 1 > 2; q3 = 1 == 1; q4 = 1 != 1; q5 = 1 < 2 && 2 < 3; q6 = 0 || 0; q7 = 2 >= 2;");

        var slots = new float[program.Variables.Count];
        program.Execute(slots);

        Assert.Equal(1f, slots[program.IndexOf("q1")]);
        Assert.Equal(0f, slots[program.IndexOf("q2")]);
        Assert.Equal(1f, slots[program.IndexOf("q3")]);
        Assert.Equal(0f, slots[program.IndexOf("q4")]);
        Assert.Equal(1f, slots[program.IndexOf("q5")]);
        Assert.Equal(0f, slots[program.IndexOf("q6")]);
        Assert.Equal(1f, slots[program.IndexOf("q7")]);
    }

    /// <summary>The ternary operator and the if() helper select a branch.</summary>
    [Fact]
    public void Execute_SelectsBranches()
    {
        var program = PresetCompiler.Compile("q1 = bass > 0.5 ? 1 : 0; q2 = if(mid, 2, 3);");

        var slots = new float[program.Variables.Count];
        slots[program.IndexOf("bass")] = 0.75f;
        slots[program.IndexOf("mid")] = 0f;
        program.Execute(slots);

        Assert.Equal(1f, slots[program.IndexOf("q1")]);
        Assert.Equal(3f, slots[program.IndexOf("q2")]);
    }

    /// <summary>Math functions evaluate as expected.</summary>
    [Fact]
    public void Execute_AppliesMathFunctions()
    {
        var program = PresetCompiler.Compile(
            "q1 = sin(0); q2 = cos(0); q3 = sqrt(9); q4 = abs(-4); q5 = floor(1.7); q6 = ceil(1.2);"
            + " q7 = int(-1.7); q8 = min(2, 3); q9 = max(2, 3); q10 = pow(2, 3); q11 = sign(-2); q12 = log10(100);");

        var slots = new float[program.Variables.Count];
        program.Execute(slots);

        Assert.Equal(0f, slots[program.IndexOf("q1")], 5);
        Assert.Equal(1f, slots[program.IndexOf("q2")], 5);
        Assert.Equal(3f, slots[program.IndexOf("q3")], 5);
        Assert.Equal(4f, slots[program.IndexOf("q4")]);
        Assert.Equal(1f, slots[program.IndexOf("q5")]);
        Assert.Equal(2f, slots[program.IndexOf("q6")]);
        Assert.Equal(-1f, slots[program.IndexOf("q7")]);
        Assert.Equal(2f, slots[program.IndexOf("q8")]);
        Assert.Equal(3f, slots[program.IndexOf("q9")]);
        Assert.Equal(8f, slots[program.IndexOf("q10")]);
        Assert.Equal(-1f, slots[program.IndexOf("q11")]);
        Assert.Equal(2f, slots[program.IndexOf("q12")], 5);
    }

    /// <summary>Built-in values and earlier statements are visible to later ones.</summary>
    [Fact]
    public void Execute_ReadsInputsAndChainsStatements()
    {
        var program = PresetCompiler.Compile("q1 = bass * 2; q2 = q1 + time;");

        var slots = new float[program.Variables.Count];
        slots[program.IndexOf("bass")] = 0.25f;
        slots[program.IndexOf("time")] = 10f;
        program.Execute(slots);

        Assert.Equal(0.5f, slots[program.IndexOf("q1")]);
        Assert.Equal(10.5f, slots[program.IndexOf("q2")]);
    }

    /// <summary>The mathematical constant is available without being a slot.</summary>
    [Fact]
    public void Execute_ProvidesPi()
    {
        var program = PresetCompiler.Compile("q1 = pi;");

        var slots = new float[program.Variables.Count];
        program.Execute(slots);

        Assert.Equal(MathF.PI, slots[program.IndexOf("q1")], 5);
        Assert.Equal(-1, program.IndexOf("pi"));
    }

    /// <summary>rand() stays inside the documented range.</summary>
    [Fact]
    public void Execute_KeepsRandomValuesInRange()
    {
        var program = PresetCompiler.Compile("q1 = rand(2);");
        var slots = new float[program.Variables.Count];

        for (var attempt = 0; attempt < 50; attempt++)
        {
            program.Execute(slots);
            Assert.InRange(slots[program.IndexOf("q1")], 0f, 2f);
        }
    }

    /// <summary>Comments and repeated separators are ignored.</summary>
    [Fact]
    public void Compile_SkipsCommentsAndEmptyStatements()
    {
        var program = PresetCompiler.Compile("q1 = 1; // a comment\n;; q2 = 2;");

        var slots = new float[program.Variables.Count];
        program.Execute(slots);

        Assert.Equal(1f, slots[program.IndexOf("q1")]);
        Assert.Equal(2f, slots[program.IndexOf("q2")]);
    }

    /// <summary>A too short slot array is rejected instead of corrupting memory.</summary>
    [Fact]
    public void Execute_RejectsATooShortSlotArray()
    {
        var program = PresetCompiler.Compile("q1 = 1; q2 = 2;");

        Assert.Throws<ArgumentException>(() => program.Execute(new float[1]));
    }

    /// <summary>Author mistakes are reported with a position.</summary>
    [Theory]
    [InlineData("q1 = ;")]
    [InlineData("q1 = 1 +")]
    [InlineData("q1 = (1 + 2")]
    [InlineData("q1 = 1 ? 2")]
    [InlineData("q1 = unknown(1)")]
    [InlineData("q1 = sin(1, 2)")]
    [InlineData("q1 = 1 $ 2")]
    public void Compile_ReportsInvalidExpressions(string source)
    {
        var exception = Assert.Throws<PresetExpressionException>(() => PresetCompiler.Compile(source));

        Assert.True(exception.Position >= 0);
        Assert.Contains("position", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
