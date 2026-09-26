using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the HLSL tokenizer that the shader runtime is built on: identifiers, keywords,
/// numbers with suffixes, operators, comments, swizzles, positions, and error reporting.
/// </summary>
public sealed class ShaderLexerTests
{
    /// <summary>Identifiers and keywords are told apart.</summary>
    [Fact]
    public void Tokenize_ReadsIdentifiersAndKeywords()
    {
        var tokens = ShaderLexer.Tokenize("if (uv) return value;");

        Assert.Equal(ShaderTokenKind.Keyword, tokens[0].Kind);
        Assert.Equal("if", tokens[0].Text);
        Assert.Equal(ShaderTokenKind.Punctuation, tokens[1].Kind);
        Assert.Equal("uv", tokens[2].Text);
        Assert.Equal(ShaderTokenKind.Keyword, tokens[4].Kind);
        Assert.Equal("return", tokens[4].Text);
    }

    /// <summary>A trailing f or h marks a float or half literal and is not part of the value.</summary>
    [Fact]
    public void Tokenize_ReadsNumbersWithSuffixes()
    {
        var tokens = ShaderLexer.Tokenize("1.5f 2 .25h 3.0");

        Assert.Equal(1.5f, tokens[0].Number);
        Assert.Equal(2f, tokens[1].Number);
        Assert.Equal(0.25f, tokens[2].Number);
        Assert.Equal(3f, tokens[3].Number);
    }

    /// <summary>Two-character operators are one token, not two.</summary>
    [Fact]
    public void Tokenize_ReadsMultiCharacterOperators()
    {
        var tokens = ShaderLexer.Tokenize("a <= b == c && d != e");

        Assert.Contains(tokens, token => token.Text == "<=");
        Assert.Contains(tokens, token => token.Text == "==");
        Assert.Contains(tokens, token => token.Text == "&&");
        Assert.Contains(tokens, token => token.Text == "!=");
        Assert.DoesNotContain(tokens, token => token.Text == "=" && token.Position == 2);
    }

    /// <summary>Line and block comments are skipped.</summary>
    [Fact]
    public void Tokenize_SkipsComments()
    {
        var tokens = ShaderLexer.Tokenize("a // trailing\nb /* block\ncomment */ c");

        Assert.Equal(["a", "b", "c"], tokens.Where(t => t.Kind == ShaderTokenKind.Identifier).Select(t => t.Text));
    }

    /// <summary>A swizzle is a dot followed by the component letters.</summary>
    [Fact]
    public void Tokenize_ReadsSwizzles()
    {
        var tokens = ShaderLexer.Tokenize("uv.xyzw");

        Assert.Equal(ShaderTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(".", tokens[1].Text);
        Assert.Equal("xyzw", tokens[2].Text);
    }

    /// <summary>Every token keeps its offset in the source.</summary>
    [Fact]
    public void Tokenize_TracksPositions()
    {
        var tokens = ShaderLexer.Tokenize("  float2 uv;");

        Assert.Equal(2, tokens[0].Position);
        Assert.Equal(9, tokens[1].Position);
        Assert.Equal(11, tokens[2].Position);
    }

    /// <summary>The stream always ends with an end token.</summary>
    [Fact]
    public void Tokenize_EndsWithEndToken()
    {
        var tokens = ShaderLexer.Tokenize("x");

        Assert.Equal(ShaderTokenKind.End, tokens[^1].Kind);
        Assert.Equal(1, tokens[^1].Position);
    }

    /// <summary>Empty source produces only the end token.</summary>
    [Fact]
    public void Tokenize_HandlesEmptySource()
    {
        Assert.Single(ShaderLexer.Tokenize(null));
        Assert.Single(ShaderLexer.Tokenize(string.Empty));
    }

    /// <summary>An unexpected character reports its position.</summary>
    [Fact]
    public void Tokenize_ReportsUnexpectedCharacters()
    {
        var error = Assert.Throws<PresetExpressionException>(() => ShaderLexer.Tokenize("float a = §;"));

        Assert.Equal(10, error.Position);
    }

    /// <summary>A realistic Milkdrop warp shader tokenizes completely.</summary>
    [Fact]
    public void Tokenize_HandlesAMilkdropWarpShader()
    {
        const string shader = """
            sampler_main : register(s0);
            float4 main(float2 uv : TEXCOORD0) : COLOR
            {
                float2 offset = 0.01 * float2(sin(time), cos(time));
                float3 col = tex2D(sampler_main, uv + offset).rgb;
                return float4(col * saturate(1.0 + bass), 1.0);
            }
            """;

        var tokens = ShaderLexer.Tokenize(shader);

        Assert.Equal(ShaderTokenKind.End, tokens[^1].Kind);
        Assert.Contains(tokens, token => token.Text == "tex2D");
        Assert.Contains(tokens, token => token.Text == "register");
        Assert.Contains(tokens, token => token.Kind == ShaderTokenKind.Number && token.Number == 0.01f);
    }
}
