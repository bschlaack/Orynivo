using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies that a compiled shader produces exactly the same values as the interpreter, which is
/// the reference implementation. Any difference here would mean a preset renders differently
/// depending on which path it took, so the comparison is the acceptance criterion for phase 39e.
/// </summary>
public sealed class ShaderCompilerTests
{
    private const string StraightLineShader = """
        comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR
        {
            float2 offset = uv - 0.5;
            float r = length(offset);
            float3 col = tex2D(sampler_main, uv).rgb;
            col = lerp(col, float3(1, 0.5, 0.2), saturate(1 - r * 2));
            return float4(col * saturate(0.8 + bass), 1.0);
        }
        """;

    /// <summary>A straight-line shader compiles and matches the interpreter.</summary>
    [Fact]
    public void Compile_MatchesTheInterpreterForAStraightLineShader()
    {
        Compare(StraightLineShader);
    }

    /// <summary>Arithmetic, comparisons, swizzles, and the ternary operator all match.</summary>
    [Theory]
    [InlineData("return float4(1, 2, 3, 4);")]
    [InlineData("return float4(q1 * 2, q1 / 3, q1 % 4, -q1);")]
    [InlineData("return float4(bass > 0.5, bass < 0.5, bass == 0.5, bass != 0.5);")]
    [InlineData("return float4(uv.x, uv.y, uv.x + uv.y, uv.x * uv.y);")]
    [InlineData("return bass > 0.5 ? float4(1, 0, 0, 1) : float4(0, 0, 1, 1);")]
    [InlineData("return float4(min(bass, mid), max(bass, mid), pow(bass, 2), sqrt(abs(bass)));")]
    [InlineData("return float4(sin(bass), cos(mid), tan(treb), frac(vol));")]
    [InlineData("return float4(dot(float3(1, 2, 3), float3(4, 5, 6)), length(float2(3, 4)), 0, 0);")]
    [InlineData("return float4(uv.xyxy.rgb, 1);")]
    [InlineData("return float4(normalize(float2(1, 2)), smoothstep(0, 1, bass), clamp(bass, 0, 1));")]
    public void Compile_MatchesTheInterpreter(string body)
    {
        Compare("comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR\n{\n" + body + "\n}");
    }

    /// <summary>A shader with control flow stays on the interpreter instead of being compiled.</summary>
    [Fact]
    public void Compile_LeavesControlFlowToTheInterpreter()
    {
        var node = ShaderParser.Parse("""
            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                if (bass > 0.5) { return float4(1, 0, 0, 1); }
                return float4(0, 0, 1, 1);
            }
            """);

        Assert.Null(ShaderCompiler.Compile(node));
    }

    /// <summary>Assigning to a local is compiled; assigning to a swizzle target is not.</summary>
    [Fact]
    public void Compile_HandlesLocalAssignmentsOnly()
    {
        var local = ShaderParser.Parse(
            "float4 main(float2 uv : TEXCOORD0) : COLOR { float3 c = float3(1, 0, 0); c = c * 2; return float4(c, 1); }");
        Assert.NotNull(ShaderCompiler.Compile(local));

        var swizzle = ShaderParser.Parse(
            "float4 main(float2 uv : TEXCOORD0) : COLOR { float3 c = float3(1, 0, 0); c.xy = float2(1, 1); return float4(c, 1); }");
        Assert.Null(ShaderCompiler.Compile(swizzle));
    }

    /// <summary>Compiles a shader and compares its result with the interpreter.</summary>
    /// <param name="shaderText">Preset text holding a comp shader.</param>
    private static void Compare(string shaderText)
    {
        var preset = VisualizerPreset.Parse(shaderText);
        var shader = Assert.Single(preset.CompShaders);
        var compiled = ShaderCompiler.Compile(shader.Program);
        Assert.NotNull(compiled);

        var sampler = new FixedSampler();
        var slots = new ShaderValue[compiled!.SlotCount];
        Seed(compiled, slots, "uv", ShaderValue.Vector(0.25f, 0.75f, 0f, 0f, 2));
        Seed(compiled, slots, "bass", ShaderValue.Scalar(0.6f));
        Seed(compiled, slots, "mid", ShaderValue.Scalar(0.4f));
        Seed(compiled, slots, "treb", ShaderValue.Scalar(0.3f));
        Seed(compiled, slots, "vol", ShaderValue.Scalar(0.5f));
        Seed(compiled, slots, "q1", ShaderValue.Scalar(1.5f));
        var compiledValue = compiled.Execute(slots, sampler);

        var interpreter = new ShaderInterpreter(shader.Program, sampler);
        interpreter.SetVariable("uv", ShaderValue.Vector(0.25f, 0.75f, 0f, 0f, 2));
        interpreter.SetVariable("bass", 0.6f);
        interpreter.SetVariable("mid", 0.4f);
        interpreter.SetVariable("treb", 0.3f);
        interpreter.SetVariable("vol", 0.5f);
        interpreter.SetVariable("q1", 1.5f);
        var interpretedValue = interpreter.Run();

        Assert.Equal(interpretedValue.Count, compiledValue.Count);
        for (var index = 0; index < interpretedValue.Count; index++)
        {
            Assert.Equal(interpretedValue.Get(index), compiledValue.Get(index), 4);
        }
    }

    /// <summary>Writes one variable into a compiled shader's slots.</summary>
    /// <param name="compiled">Compiled shader.</param>
    /// <param name="slots">Slot storage.</param>
    /// <param name="name">Variable name.</param>
    /// <param name="value">Value to store.</param>
    private static void Seed(ShaderProgram compiled, ShaderValue[] slots, string name, ShaderValue value)
    {
        var slot = compiled.IndexOf(name);
        if (slot >= 0)
            slots[slot] = value;
    }

    /// <summary>A sampler that returns a fixed colour.</summary>
    private sealed class FixedSampler : IShaderSampler
    {
        /// <inheritdoc/>
        public ShaderValue Sample(string sampler, float u, float v) =>
            ShaderValue.Vector(0.5f, 0.25f, 0.75f, 1f, 4);

        /// <inheritdoc/>
        public ShaderValue SampleBlur(int level, float u, float v) =>
            ShaderValue.Vector(0.1f * level, 0f, 0f, 1f, 4);

        /// <inheritdoc/>
        public ShaderValue SamplePixel(int x, int y) =>
            ShaderValue.Vector(x, y, 0f, 1f, 4);
    }
}
