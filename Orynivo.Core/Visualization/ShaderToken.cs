namespace Orynivo.Visualization;

/// <summary>The kinds of token an HLSL <c>ps_2_0</c> shader is made of.</summary>
public enum ShaderTokenKind
{
    /// <summary>End of the source.</summary>
    End,

    /// <summary>An identifier, including type and function names.</summary>
    Identifier,

    /// <summary>A numeric literal.</summary>
    Number,

    /// <summary>A punctuation or operator character or sequence.</summary>
    Punctuation,

    /// <summary>An <c>if</c>, <c>else</c>, <c>for</c>, <c>return</c>, or similar keyword.</summary>
    Keyword
}

/// <summary>One token of an HLSL shader.</summary>
/// <param name="Kind">Token kind.</param>
/// <param name="Text">Exact source text of the token.</param>
/// <param name="Position">Zero-based offset of the token in the source.</param>
/// <param name="Number">Numeric value for number tokens, otherwise zero.</param>
public sealed record ShaderToken(ShaderTokenKind Kind, string Text, int Position, float Number);
