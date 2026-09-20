using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the HLSL parser: declarations, swizzles, calls, control flow, precedence, function
/// signatures, bare statement bodies, sampler declarations without a type, and the error
/// positions it reports.
/// </summary>
public sealed class ShaderParserTests
{
    /// <summary>A realistic Milkdrop warp shader parses into a body.</summary>
    [Fact]
    public void Parse_ReadsAMilkdropWarpShader()
    {
        var program = ShaderParser.Parse("""
            sampler_main : register(s0);
            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                float2 offset = 0.01 * float2(sin(time), cos(time));
                float3 col = tex2D(sampler_main, uv + offset).rgb;
                return float4(col * saturate(1.0 + bass), 1.0);
            }
            """);

        Assert.Equal(ShaderNodeKind.Program, program.Kind);
        var body = Assert.Single(program.Items);
        Assert.Equal(ShaderNodeKind.Block, body.Kind);
        Assert.Equal(3, body.Items.Count);
    }

    /// <summary>Declarations keep their type, name, and initializer.</summary>
    [Fact]
    public void Parse_ReadsDeclarations()
    {
        var program = ShaderParser.Parse("float2 offset = float2(1, 2);");
        var declaration = Assert.Single(program.Items);

        Assert.Equal(ShaderNodeKind.Declaration, declaration.Kind);
        Assert.Equal("float2", declaration.Text);
        Assert.Equal("offset", Assert.Single(declaration.Items).Text);
        Assert.Equal(ShaderNodeKind.Call, declaration.Left!.Kind);
        Assert.Equal(2, declaration.Left.Items.Count);
    }

    /// <summary>A member access is a swizzle on its target.</summary>
    [Fact]
    public void Parse_ReadsSwizzles()
    {
        var program = ShaderParser.Parse("return col.rgb;");
        var returned = Assert.Single(program.Items);

        Assert.Equal(ShaderNodeKind.Return, returned.Kind);
        Assert.Equal(ShaderNodeKind.Member, returned.Left!.Kind);
        Assert.Equal("rgb", returned.Left.Text);
        Assert.Equal("col", returned.Left.Left!.Text);
    }

    /// <summary>Calls keep their name and argument list.</summary>
    [Fact]
    public void Parse_ReadsCallsWithArguments()
    {
        var program = ShaderParser.Parse("float3 c = tex2D(sampler_main, uv);");
        var call = program.Items[0].Left!;

        Assert.Equal(ShaderNodeKind.Call, call.Kind);
        Assert.Equal("tex2D", call.Text);
        Assert.Equal(2, call.Items.Count);
    }

    /// <summary>An if statement keeps its condition and both branches.</summary>
    [Fact]
    public void Parse_ReadsIfElse()
    {
        var program = ShaderParser.Parse("if (bass > 0.5) { x = 1; } else { x = 2; }");
        var conditional = Assert.Single(program.Items);

        Assert.Equal(ShaderNodeKind.If, conditional.Kind);
        Assert.Equal(">", conditional.Left!.Text);
        Assert.Equal(ShaderNodeKind.Block, conditional.Right!.Kind);
        Assert.Equal(ShaderNodeKind.Block, conditional.Third!.Kind);
    }

    /// <summary>A for loop keeps its initializer, condition, increment, and body.</summary>
    [Fact]
    public void Parse_ReadsForLoops()
    {
        var program = ShaderParser.Parse("for (int i = 0; i < 4; i++) { x = x + i; }");
        var loop = Assert.Single(program.Items);

        Assert.Equal(ShaderNodeKind.For, loop.Kind);
        Assert.Equal(ShaderNodeKind.Declaration, loop.Left!.Kind);
        Assert.Equal("<", loop.Right!.Text);
        Assert.Equal("++", loop.Third!.Text);
        Assert.Equal(ShaderNodeKind.Block, Assert.Single(loop.Items).Kind);
    }

    /// <summary>Multiplication binds tighter than addition.</summary>
    [Fact]
    public void Parse_AppliesOperatorPrecedence()
    {
        var program = ShaderParser.Parse("return a + b * c;");
        var expression = program.Items[0].Left!;

        Assert.Equal("+", expression.Text);
        Assert.Equal("a", expression.Left!.Text);
        Assert.Equal("*", expression.Right!.Text);
    }

    /// <summary>The ternary operator keeps all three operands.</summary>
    [Fact]
    public void Parse_ReadsTernaryExpressions()
    {
        var program = ShaderParser.Parse("return a ? b : c;");
        var expression = program.Items[0].Left!;

        Assert.Equal(ShaderNodeKind.Ternary, expression.Kind);
        Assert.Equal("a", expression.Left!.Text);
        Assert.Equal("b", expression.Right!.Text);
        Assert.Equal("c", expression.Third!.Text);
    }

    /// <summary>A bare statement body parses without a function wrapper.</summary>
    [Fact]
    public void Parse_AcceptsBareStatementBodies()
    {
        var program = ShaderParser.Parse("float2 uv = x + y;\nreturn uv;");

        Assert.Equal(2, program.Items.Count);
        Assert.Equal(ShaderNodeKind.Declaration, program.Items[0].Kind);
        Assert.Equal(ShaderNodeKind.Return, program.Items[1].Kind);
    }

    /// <summary>A missing semicolon reports its position.</summary>
    [Fact]
    public void Parse_ReportsMissingSemicolons()
    {
        var error = Assert.Throws<PresetExpressionException>(() => ShaderParser.Parse("float a = 1"));

        Assert.True(error.Position >= 10);
    }
}
