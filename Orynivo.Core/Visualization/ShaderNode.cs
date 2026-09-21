namespace Orynivo.Visualization;

/// <summary>What one node of a parsed HLSL shader represents.</summary>
public enum ShaderNodeKind
{
    /// <summary>The whole parsed unit.</summary>
    Program,

    /// <summary>A brace-delimited block of statements.</summary>
    Block,

    /// <summary>
    /// A function definition. It is kept apart from <see cref="Block"/> because the entry point is
    /// itself a named block, so the two are otherwise indistinguishable: a helper must be callable
    /// while the entry point is the code that runs.
    /// </summary>
    Function,

    /// <summary>A variable declaration.</summary>
    Declaration,

    /// <summary>An expression used as a statement.</summary>
    ExpressionStatement,

    /// <summary>An <c>if</c> with an optional <c>else</c> branch.</summary>
    If,

    /// <summary>A <c>for</c> loop.</summary>
    For,

    /// <summary>A <c>while</c> loop.</summary>
    While,

    /// <summary>A <c>return</c> statement.</summary>
    Return,

    /// <summary>A numeric literal.</summary>
    Literal,

    /// <summary>A variable or function name.</summary>
    Identifier,

    /// <summary>A member access, which is a swizzle on a vector.</summary>
    Member,

    /// <summary>A function or intrinsic call.</summary>
    Call,

    /// <summary>A prefix or postfix unary operator.</summary>
    Unary,

    /// <summary>A binary operator.</summary>
    Binary,

    /// <summary>An element or row access with <c>[...]</c>.</summary>
    Index,

    /// <summary>The ternary conditional operator.</summary>
    Ternary
}

/// <summary>
/// One node of a parsed HLSL shader. The parser produces a tagged union instead of a type per
/// construct, which keeps the tree small and lets the interpreter switch on
/// <see cref="Kind"/>; unused fields stay <see langword="null"/> or empty.
/// </summary>
/// <param name="Kind">What the node represents.</param>
/// <param name="Position">Zero-based offset of the construct in the source.</param>
/// <param name="Text">Operator, type, name, or member text, depending on the kind.</param>
/// <param name="Number">Numeric value for literal nodes.</param>
/// <param name="Left">Left operand, condition, initializer, or first child.</param>
/// <param name="Right">Right operand, then branch, condition, or increment.</param>
/// <param name="Third">Else branch or third operand of a ternary.</param>
/// <param name="Children">Statement or argument list.</param>
/// <param name="Parameters">
/// Parameter names of a function definition, or <see langword="null"/> for every other node. The
/// engine stores every value as a float, so only the names matter.
/// </param>
public sealed record ShaderNode(
    ShaderNodeKind Kind,
    int Position,
    string Text = "",
    float Number = 0f,
    ShaderNode? Left = null,
    ShaderNode? Right = null,
    ShaderNode? Third = null,
    IReadOnlyList<ShaderNode>? Children = null,
    IReadOnlyList<ShaderNode>? Parameters = null)
{
    /// <summary>Gets the child nodes, never <see langword="null"/>.</summary>
    public IReadOnlyList<ShaderNode> Items => Children ?? [];

    /// <summary>Gets the parameter names of a function definition, never <see langword="null"/>.</summary>
    public IReadOnlyList<ShaderNode> ParameterList => Parameters ?? [];
}
