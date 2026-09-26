using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the SkSL emitter for the preset expression language: it reports the uniforms the caller
/// has to seed, maps the engine-bound values onto their locals, and refuses the constructs the GPU
/// cannot share with the interpreter.
/// </summary>
public sealed class PresetExpressionTranspilerTests
{
    /// <summary>The engine-bound values become locals while other variables become uniforms.</summary>
    [Fact]
    public void TryTranspile_MapsLocalsAndUniforms()
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        var program = PresetCompiler.Compile("x = x + sin(rad) * 0.1; y = q1 + 1;", layout);

        Assert.True(
            PresetExpressionTranspiler.TryTranspile(program, out var body, out var uniforms, out var error),
            error);

        Assert.Contains(PresetExpressionTranspiler.LocalX, body, StringComparison.Ordinal);
        Assert.Contains(PresetExpressionTranspiler.LocalRadius, body, StringComparison.Ordinal);
        Assert.Contains(PresetExpressionTranspiler.UniformName("q1"), body, StringComparison.Ordinal);
        Assert.Contains("q1", uniforms);
        // The engine re-seeds x for every pixel, so it is never declared as a uniform.
        Assert.DoesNotContain(PresetExpressionTranspiler.UniformName("x"), body, StringComparison.Ordinal);
        Assert.DoesNotContain("x", uniforms);
    }

    /// <summary>A loop becomes a bounded SkSL for loop.</summary>
    [Fact]
    public void TryTranspile_EmitsABoundedLoop()
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        var program = PresetCompiler.Compile("loop(3, x = x + 1);", layout);

        Assert.True(
            PresetExpressionTranspiler.TryTranspile(program, out var body, out _, out var error),
            error);

        Assert.Contains("for (int", body, StringComparison.Ordinal);
        Assert.Contains("break", body, StringComparison.Ordinal);
    }

    /// <summary>A shared-memory-buffer access stays on the interpreter.</summary>
    [Fact]
    public void TryTranspile_RefusesTheSharedBuffer()
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        var program = PresetCompiler.Compile("x = megabuf(1);", layout);

        Assert.False(PresetExpressionTranspiler.TryTranspile(program, out _, out _, out var error));
        Assert.NotNull(error);
    }

    /// <summary>The per-pixel random function stays on the interpreter.</summary>
    [Fact]
    public void TryTranspile_RefusesRandom()
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        var program = PresetCompiler.Compile("x = rand(1);", layout);

        Assert.False(PresetExpressionTranspiler.TryTranspile(program, out _, out _, out var error));
        Assert.NotNull(error);
    }

    /// <summary>An empty program emits nothing.</summary>
    [Fact]
    public void TryTranspile_EmptyProgramIsEmpty()
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        var program = PresetCompiler.Compile("", layout);

        Assert.True(PresetExpressionTranspiler.TryTranspile(program, out var body, out var uniforms, out var error), error);
        Assert.Equal(string.Empty, body);
        Assert.Empty(uniforms);
    }

    /// <summary>A temporary variable written before it is read stays on the GPU.</summary>
    [Fact]
    public void CanRunInParallel_AllowsATemporaryWrittenFirst()
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        var program = PresetCompiler.Compile("num = x; x = num * 0.5; y = num;", layout);

        Assert.True(PresetExpressionTranspiler.CanRunInParallel(program));
    }

    /// <summary>A variable read before it is written carries between pixels, so it stays on the CPU.</summary>
    [Fact]
    public void CanRunInParallel_RefusesAReadBeforeWrite()
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        var program = PresetCompiler.Compile("x = x + num; num = 1;", layout);

        Assert.False(PresetExpressionTranspiler.CanRunInParallel(program));
    }

    /// <summary>A value a loop writes is not definite afterwards, so a later read stays on the CPU.</summary>
    [Fact]
    public void CanRunInParallel_RefusesAReadAfterALoopWrite()
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        var program = PresetCompiler.Compile("loop(3, num = 1); x = x + num;", layout);

        Assert.False(PresetExpressionTranspiler.CanRunInParallel(program));
    }

    /// <summary>A written temporary becomes a block local rather than an immutable uniform.</summary>
    [Fact]
    public void TryTranspile_EmitsATemporaryAsALocal()
    {
        var layout = PresetVariableLayout.RegisterStandardVariables(new PresetVariableLayout());
        var program = PresetCompiler.Compile("num = x; x = num * 0.5;", layout);

        Assert.True(PresetExpressionTranspiler.TryTranspile(program, out var body, out var uniforms, out var error), error);
        Assert.Contains("_orynivo_l_num", body, StringComparison.Ordinal);
        Assert.Contains("num", uniforms);
    }
}
