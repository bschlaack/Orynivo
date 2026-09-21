using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the HLSL interpreter: arithmetic and precedence, variables and assignments,
/// swizzles, constructors, the intrinsics, control flow, sampling through a bound sampler, and
/// the guards that keep a broken shader from hanging a frame.
/// </summary>
public sealed class ShaderInterpreterTests
{
    /// <summary>Arithmetic follows the C precedence.</summary>
    [Fact]
    public void Run_EvaluatesArithmeticWithPrecedence()
    {
        Assert.Equal(7f, Run("return 1 + 2 * 3;").X);
        Assert.Equal(9f, Run("return (1 + 2) * 3;").X);
        Assert.Equal(1f, Run("return 7 % 3;").X);
    }

    /// <summary>Variables keep their value across statements.</summary>
    [Fact]
    public void Run_ReadsAndWritesVariables()
    {
        Assert.Equal(10f, Run("x = 5; return x * 2;").X);
        Assert.Equal(12f, Run("x = 3; x *= 4; return x;").X);
        Assert.Equal(4f, Run("x = 3; x++; return x;").X);
    }

    /// <summary>Swizzles select and reorder components.</summary>
    [Fact]
    public void Run_HandlesSwizzles()
    {
        Assert.Equal(3f, Run("float3 c = float3(1, 2, 3); return c.z;").X);
        Assert.Equal(3f, Run("float3 c = float3(1, 2, 3); return c.b;").X);
        var reversed = Run("float3 c = float3(1, 2, 3); return c.zyx;");
        Assert.Equal(3, reversed.Count);
        Assert.Equal(3f, reversed.X);
        Assert.Equal(1f, reversed.Z);
    }

    /// <summary>Assigning to a swizzle writes back into the source vector.</summary>
    [Fact]
    public void Run_AssignsSwizzleComponents()
    {
        Assert.Equal(9f, Run("float3 c = float3(1, 2, 3); c.y = 9; return c.y;").X);
        Assert.Equal(3f, Run("float3 c = float3(1, 2, 3); c.y = 9; return c.z;").X);
    }

    /// <summary>Vector constructors concatenate and broadcast their arguments.</summary>
    [Fact]
    public void Run_BuildsVectorsFromConstructors()
    {
        var combined = Run("return float4(float2(1, 2), 3, 4);");
        Assert.Equal(4, combined.Count);
        Assert.Equal(1f, combined.X);
        Assert.Equal(2f, combined.Y);
        Assert.Equal(3f, combined.Z);
        Assert.Equal(4f, combined.W);

        Assert.Equal(2f, Run("float3 v = float3(2); return v.z;").X);
    }

    /// <summary>The common intrinsics produce the expected values.</summary>
    [Fact]
    public void Run_AppliesIntrinsics()
    {
        Assert.Equal(1f, Run("return saturate(4);").X);
        Assert.Equal(0f, Run("return saturate(-4);").X);
        Assert.Equal(5f, Run("return lerp(0, 10, 0.5);").X);
        Assert.Equal(32f, Run("return dot(float2(2, 4), float2(4, 6));").X);
        Assert.Equal(5f, Run("return length(float2(3, 4));").X);
        Assert.Equal(0.25f, Run("return frac(2.25);").X);
        Assert.Equal(5f, Run("return max(2, 7) - min(2, 7);").X);
        Assert.Equal(8f, Run("return pow(2, 3);").X);
    }

    /// <summary>Both branches of an if statement are reachable.</summary>
    [Fact]
    public void Run_HandlesIfElse()
    {
        const string shader = """
            x = 0;
            if (bass > 0.5) { x = 1; } else { x = 2; }
            return x;
            """;
        var interpreter = Create(shader, bass: 0.8f);
        Assert.Equal(1f, interpreter.Run().X);

        var quiet = Create(shader, bass: 0.1f);
        Assert.Equal(2f, quiet.Run().X);
    }

    /// <summary>A for loop accumulates and terminates.</summary>
    [Fact]
    public void Run_HandlesForLoops()
    {
        Assert.Equal(6f, Run("sum = 0; for (int i = 0; i < 4; i++) { sum += i; } return sum;").X);
    }

    /// <summary>The ternary operator selects one operand.</summary>
    [Fact]
    public void Run_HandlesTernaryExpressions()
    {
        Assert.Equal(1f, Run("return 1 < 2 ? 1 : 0;").X);
        Assert.Equal(0f, Run("return 1 > 2 ? 1 : 0;").X);
    }

    /// <summary>Sampling goes through the bound sampler.</summary>
    [Fact]
    public void Run_SamplesThroughTheSampler()
    {
        var sampler = new FixedSampler();
        var interpreter = new ShaderInterpreter(
            ShaderParser.Parse("return tex2D(sampler_main, uv).rgb;"),
            sampler);
        interpreter.SetVariable("uv", ShaderValue.Vector(0.25f, 0.75f, 0f, 0f, 2));

        var result = interpreter.Run();

        Assert.Equal(3, result.Count);
        Assert.Equal(0.25f, sampler.LastU);
        Assert.Equal(0.75f, sampler.LastV);
        Assert.Equal("sampler_main", sampler.LastSampler);
        Assert.Equal(0.5f, result.X);
    }

    /// <summary>Division by zero yields zero instead of an infinity.</summary>
    [Fact]
    public void Run_TreatsDivisionByZeroAsZero()
    {
        Assert.Equal(0f, Run("return 1 / 0;").X);
    }

    /// <summary>A shader without a return statement yields zero.</summary>
    [Fact]
    public void Run_ReturnsZeroWithoutAReturn()
    {
        Assert.Equal(0f, Run("x = 1;").X);
    }

    /// <summary>An unknown function reports its name and position.</summary>
    [Fact]
    public void Run_ReportsUnknownFunctions()
    {
        var error = Assert.Throws<PresetExpressionException>(() => Run("return mystery(1);"));

        Assert.Contains("mystery", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A loop without a bound stops at the iteration budget.</summary>
    [Fact]
    public void Run_ReportsRunawayLoops()
    {
        var error = Assert.Throws<PresetExpressionException>(
            () => Run("x = 0; for (i = 0; i < 1000000; i++) { x += 1; } return x;"));

        Assert.Contains("looped", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Runs a shader body and returns its value.</summary>
    /// <param name="source">Shader source.</param>
    /// <returns>The returned value.</returns>
    private static ShaderValue Run(string source) => Create(source).Run();

    /// <summary>Creates an interpreter for a shader body.</summary>
    /// <param name="source">Shader source.</param>
    /// <param name="bass">Value for the bass variable.</param>
    /// <returns>The interpreter.</returns>
    private static ShaderInterpreter Create(string source, float bass = 0f)
    {
        var interpreter = new ShaderInterpreter(ShaderParser.Parse(source));
        interpreter.SetVariable("bass", bass);
        return interpreter;
    }

    /// <summary>A sampler that records its arguments and returns a fixed colour.</summary>
    private sealed class FixedSampler : IShaderSampler
    {
        /// <summary>Gets the sampler name of the last call.</summary>
        public string LastSampler { get; private set; } = string.Empty;

        /// <summary>Gets the horizontal coordinate of the last call.</summary>
        public float LastU { get; private set; }

        /// <summary>Gets the vertical coordinate of the last call.</summary>
        public float LastV { get; private set; }

        /// <inheritdoc/>
        public ShaderValue Sample(string sampler, float u, float v)
        {
            LastSampler = sampler;
            LastU = u;
            LastV = v;
            return ShaderValue.Vector(0.5f, 0.25f, 0.75f, 1f, 4);
        }

        /// <inheritdoc/>
        public ShaderValue SampleBlur(int level, float u, float v) =>
            ShaderValue.Vector(0.1f * level, 0f, 0f, 1f, 4);

        /// <inheritdoc/>
        public ShaderValue SampleVolume(string sampler, float x, float y, float z)
        {
            LastSampler = sampler;
            LastU = x;
            LastV = y;
            return ShaderValue.Vector(x, y, z, 1f, 4);
        }

        /// <inheritdoc/>
        public ShaderValue SamplePixel(int x, int y) =>
            ShaderValue.Vector(x, y, 0f, 1f, 4);
    }
}
