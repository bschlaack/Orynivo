using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the statement forms real presets use inside a shader body, which are the ones that used
/// to be reported as "Expected ';' but found '{'".
/// </summary>
public sealed class ShaderStatementFormTests
{
    /// <summary>An if whose block opens on the same line parses.</summary>
    [Fact]
    public void Parse_AcceptsAnIfWithAnInlineBlock()
    {
        var node = ShaderParser.Parse("""
            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                float2 zz = -uv * texsize.xy * q26;
                if (q25 == 1) {zz *= (abs(uv.y)/abs(uv.x));}
                else if (q25 == 2)  {zz *= (abs(uv.y)-abs(uv.x));}
                return float4(zz, 0, 1);
            }
            """);

        // The parser must accept the form; the compiler deliberately leaves control flow to the
        // interpreter, so it is allowed to report the body as uncompiled.
        Assert.NotNull(node);
        Assert.Contains(node.Items, item => item.Kind == ShaderNodeKind.Block);
    }

    /// <summary>An else glued to its block, with a trailing semicolon, parses.</summary>
    [Fact]
    public void Parse_AcceptsAnElseGluedToItsBlock()
    {
        var node = ShaderParser.Parse("""
            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                float3 ret = tex2D(sampler_main, uv).rgb;
                float3 nex = tex2D(sampler_main, float2(uv.x + 0.001, uv.y)).rgb;
                if (abs(nex.x - ret.x) > 0.1)
                {ret = GetBlur2(uv).rgb;}
                else{ret = GetBlur3(uv).rgb;};
                return float4(ret, 1);
            }
            """);

        Assert.NotNull(node);
    }

    /// <summary>An if whose condition spans a comparison and whose block follows on the next line parses.</summary>
    [Fact]
    public void Parse_AcceptsABlockOnTheNextLine()
    {
        var node = ShaderParser.Parse("""
            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                float3 ret = tex2D(sampler_main, uv).rgb;
                float3 rsamp = tex2D(sampler_main, uv + 0.1).rgb;
                if (length(ret.xy - uv) > length(rsamp.xy - uv)) {
                ret.xy = rsamp.xy;ret.z = rsamp.z;}
                return float4(ret, 1);
            }
            """);

        Assert.NotNull(node);
    }

    /// <summary>A while loop parses and the interpreter runs it.</summary>
    [Fact]
    public void Parse_AcceptsAWhileLoop()
    {
        var program = ShaderParser.Parse("""
            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                int n = 0;
                float acc = 0;
                while (n < 4) {
                    acc = acc + 2;
                    n++;
                }
                return float4(acc, 0, 0, 1);
            }
            """);

        var interpreter = new ShaderInterpreter(program);
        var result = interpreter.Run();

        Assert.Equal(8f, result.X);
    }

    /// <summary>The comma operator inside parentheses yields its right operand.</summary>
    [Fact]
    public void Parse_AcceptsACommaInsideParentheses()
    {
        var program = ShaderParser.Parse("""
            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                float3 k = float3(2, 3, 4);
                float2 a = k.xy * (3, 3);
                return float4(a, 0, 1);
            }
            """);

        var result = new ShaderInterpreter(program).Run();

        Assert.Equal(6f, result.X);
        Assert.Equal(9f, result.Y);
    }

    /// <summary>An initializer list and a state block on a declaration parse.</summary>
    [Fact]
    public void Parse_AcceptsBracedDeclarationInitializers()
    {
        var node = ShaderParser.Parse("""
            sampler sampler_grad = sampler_state {
                AddressU = WRAP;
                AddressV = WRAP;
            };

            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                float2x2 rot = { 1, 0,
                                 0, 1 };
                return float4(uv, 0, 1);
            }
            """);

        Assert.NotNull(node);
    }

    /// <summary>The integer vector types presets declare, and element access on a vector, parse.</summary>
    [Fact]
    public void Parse_AcceptsIntegerVectorTypesAndElementAccess()
    {
        var node = ShaderParser.Parse("""
            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                int2 k1 = (texsize.xy * uv) % 2;
                float3 retish = 1 - tex2D(sampler_main, uv).rgb;
                float first = retish[0];
                return float4(k1.x + first, 0, 0, 1);
            }
            """);

        Assert.NotNull(node);
    }
}
