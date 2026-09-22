using Orynivo.Visualization;
using SkiaSharp;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies that the SkSL the transpiler emits is accepted by Skia. This is the gate for the GPU
/// path: the tree the parser already produces is the input, so a shader that compiles here can run
/// as a Skia runtime effect, and one that does not stays on the CPU interpreter.
/// </summary>
public sealed class ShaderTranspilerTests
{
    /// <summary>A straight-line shader with a sampler becomes compilable SkSL.</summary>
    [Fact]
    public void Transpile_CompilesASampledShader()
    {
        AssertCompiles("""
            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                float3 c = tex2D(sampler_main, uv).rgb;
                c = c * 0.5 + float3(bass, mid, treb) * 0.25;
                return float4(c, 1);
            }
            """);
    }

    /// <summary>A shader that writes ret and loops compiles too, which the CPU JIT cannot do.</summary>
    [Fact]
    public void Transpile_CompilesALoopingShader()
    {
        AssertCompiles("""
            float3 sum = 0;
            int n = 0;
            while (n < 4) {
                sum += tex2D(sampler_main, uv + float2(0.01, 0.0) * n).rgb;
                n++;
            }
            ret = sum * 0.25;
            """);
    }

    /// <summary>The frame helpers map onto Skia's shader sampling.</summary>
    [Fact]
    public void Transpile_MapsTheFrameHelpers()
    {
        AssertCompiles("""
            float3 a = GetPixel(uv);
            float3 b = GetBlur1(uv);
            ret = lerp(a, b, saturate(bass * 2));
            """);
    }

    /// <summary>The volume sample maps onto the generated atlas sampler.</summary>
    [Fact]
    public void Transpile_CompilesATex3DVolumeSample()
    {
        AssertCompiles("""
            float3 n = tex3D(sampler_noisevol_hq, float3(uv * 3, time)).rgb;
            ret = n * 0.5;
            """);
    }

    /// <summary>A variable the shader never declares compiles as a zero constant.</summary>
    [Fact]
    public void Transpile_CompilesAnUnknownIdentifier()
    {
        AssertCompiles("""
            float3 n = float3(roam_cos.y, roam_sin.x, 0.0);
            ret = n * 0.5;
            """);
    }

    /// <summary>A uniform the shader writes becomes a writable local.</summary>
    [Fact]
    public void Transpile_CompilesAWrittenUniform()
    {
        AssertCompiles("""
            q25 = q24 + 0.01;
            ret = float3(q25);
            """);
    }

    /// <summary>A for loop becomes a bounded counter that SkSL accepts.</summary>
    [Fact]
    public void Transpile_CompilesAForLoop()
    {
        AssertCompiles("""
            float3 sum = 0;
            for (int n = 0; n < 4; n++) {
                sum += tex2D(sampler_main, uv + float2(0.01, 0.0) * n).rgb;
            }
            ret = sum * 0.25;
            """);
    }

    /// <summary>A coordinate wider than float2 is narrowed for the sampler.</summary>
    [Fact]
    public void Transpile_CompilesAVectorCoordinate()
    {
        AssertCompiles("""
            ret = tex2D(sampler_main, float3(uv, 0.5)).rgb;
            """);
    }

    /// <summary>A vector element read uses an integer index.</summary>
    [Fact]
    public void Transpile_CompilesAVectorIndex()
    {
        AssertCompiles("""
            float3 v = float3(0.1, 0.2, 0.3);
            ret = float3(v[0], v[1], v[2]);
            """);
    }

    /// <summary>A sampler the prelude does not list is declared from the shader's own call.</summary>
    [Fact]
    public void Transpile_DeclaresASamplerTheShaderNames()
    {
        var node = ShaderParser.Parse("""
            ret = tex2D(sampler_fw_main, uv).rgb;
            """);

        var sksl = ShaderTranspiler.Transpile(node, out var samplers);
        Assert.Contains("sampler_fw_main", samplers, StringComparer.Ordinal);
        Assert.Contains("uniform shader sampler_fw_main;", sksl, StringComparison.Ordinal);
    }

    /// <summary>An unsupported construct is reported instead of being emitted wrongly.</summary>
    [Fact]
    public void Transpile_ReportsWhatItCannotTranslate()
    {
        var node = ShaderParser.Parse("""
            float3 c = noSuchFunction(uv);
            ret = c;
            """);

        var exception = Assert.Throws<PresetExpressionException>(() => ShaderTranspiler.Transpile(node));
        Assert.Contains("noSuchFunction", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A float2x2 matrix becomes a SkSL mat2, and mul becomes a matrix product.</summary>
    [Fact]
    public void Transpile_CompilesAMatrixShader()
    {
        var node = ShaderParser.Parse("""
            float2x2 rot = float2x2(bass, mid, -mid, bass);
            ret = float3(mul(rot, uv - 0.5) + 0.5, 1);
            """);

        var sksl = ShaderTranspiler.Transpile(node, out _);
        Assert.Contains("mat2", sksl, StringComparison.Ordinal);

        using var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);
        Assert.True(effect is not null, $"SkSL was rejected: {errors}\n{sksl}");
    }

    /// <summary>A float3x3 matrix becomes a SkSL mat3 and compiles.</summary>
    [Fact]
    public void Transpile_CompilesAThreeByThreeMatrix()
    {
        var node = ShaderParser.Parse("""
            static const float3x3 rot = float3x3(q20, q21, q22, q23, q24, q25, q26, q27, q28);
            ret = mul(float3(uv, 0.5), rot);
            """);

        var sksl = ShaderTranspiler.Transpile(node, out _);
        Assert.Contains("mat3(", sksl, StringComparison.Ordinal);

        using var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);
        Assert.True(effect is not null, $"SkSL was rejected: {errors}\n{sksl}");
    }

    /// <summary>A float2x2 built from a float4 uniform spreads its components for SkSL's mat2.</summary>
    [Fact]
    public void Transpile_CompilesAMatrixFromAVector()
    {
        var node = ShaderParser.Parse("""
            float2x2 rot = float2x2(_qb);
            ret = float3(mul(uv, rot) + 0.5, 1);
            """);

        var sksl = ShaderTranspiler.Transpile(node, out _);
        Assert.Contains("mat2(", sksl, StringComparison.Ordinal);

        using var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);
        Assert.True(effect is not null, $"SkSL was rejected: {errors}\n{sksl}");
    }

    /// <summary>Transpiles a shader and asserts that Skia accepts the result.</summary>
    /// <param name="source">HLSL source.</param>
    private static void AssertCompiles(string source)
    {
        var node = ShaderParser.Parse(source);
        var sksl = ShaderTranspiler.Transpile(node);

        using var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);
        Assert.True(effect is not null, $"SkSL was rejected: {errors}\n{sksl}");
    }
}
