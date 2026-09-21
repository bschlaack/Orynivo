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
