namespace Orynivo.Visualization;

/// <summary>
/// One node of the parsed Milkdrop expression language. <see cref="PresetCompiler"/> builds this
/// tree once and then compiles it for both execution paths: the LINQ back end produces the
/// interpreter delegate, and <see cref="PresetExpressionTranspiler"/> emits the SkSL the GPU runs.
/// Keeping the syntax separate from either back end is what stops the two from drifting apart.
/// </summary>
/// <param name="Position">Source position, used for error messages.</param>
internal abstract record PresetSyntaxNode(int Position);

/// <summary>A numeric literal.</summary>
/// <param name="Value">The literal value.</param>
/// <param name="Position">Source position.</param>
internal sealed record PresetLiteralNode(double Value, int Position) : PresetSyntaxNode(Position);

/// <summary>A variable reference, which the layout maps to a slot.</summary>
/// <param name="Name">Variable name.</param>
/// <param name="Position">Source position.</param>
internal sealed record PresetVariableNode(string Name, int Position) : PresetSyntaxNode(Position);

/// <summary>A prefix unary operator.</summary>
/// <param name="Operator">Operator token kind.</param>
/// <param name="Operand">Operand expression.</param>
/// <param name="Position">Source position.</param>
internal sealed record PresetUnaryNode(
    PresetTokenKind Operator,
    PresetSyntaxNode Operand,
    int Position) : PresetSyntaxNode(Position);

/// <summary>A binary operator.</summary>
/// <param name="Operator">Operator token kind.</param>
/// <param name="Left">Left operand.</param>
/// <param name="Right">Right operand.</param>
/// <param name="Position">Source position.</param>
internal sealed record PresetBinaryNode(
    PresetTokenKind Operator,
    PresetSyntaxNode Left,
    PresetSyntaxNode Right,
    int Position) : PresetSyntaxNode(Position);

/// <summary>The <c>condition ? whenTrue : whenFalse</c> operator.</summary>
/// <param name="Condition">Condition expression.</param>
/// <param name="WhenTrue">Value when the condition is non-zero.</param>
/// <param name="WhenFalse">Value when the condition is zero.</param>
/// <param name="Position">Source position.</param>
internal sealed record PresetConditionalNode(
    PresetSyntaxNode Condition,
    PresetSyntaxNode WhenTrue,
    PresetSyntaxNode WhenFalse,
    int Position) : PresetSyntaxNode(Position);

/// <summary>A call to one of the Milkdrop functions.</summary>
/// <param name="Name">Function name as written.</param>
/// <param name="Arguments">Evaluated arguments.</param>
/// <param name="Position">Source position.</param>
internal sealed record PresetCallNode(
    string Name,
    IReadOnlyList<PresetSyntaxNode> Arguments,
    int Position) : PresetSyntaxNode(Position);

/// <summary>An assignment to a variable, plain or compound.</summary>
/// <param name="Name">Target variable name.</param>
/// <param name="Value">Assigned value.</param>
/// <param name="Compound">Base operator of a compound assignment, or <see langword="null"/>.</param>
/// <param name="Position">Source position.</param>
internal sealed record PresetAssignmentNode(
    string Name,
    PresetSyntaxNode Value,
    PresetTokenKind? Compound,
    int Position) : PresetSyntaxNode(Position);

/// <summary>An access to Milkdrop's shared memory buffer.</summary>
/// <param name="Index">Buffer index.</param>
/// <param name="Value">Written value, or <see langword="null"/> for a read.</param>
/// <param name="Compound">Base operator of a compound write, or <see langword="null"/>.</param>
/// <param name="Position">Source position.</param>
internal sealed record PresetMegaBufferNode(
    PresetSyntaxNode Index,
    PresetSyntaxNode? Value,
    PresetTokenKind? Compound,
    int Position) : PresetSyntaxNode(Position);

/// <summary>Milkdrop's <c>loop(count, statements)</c> construct.</summary>
/// <param name="Count">Iteration count.</param>
/// <param name="Body">Statements repeated by the loop.</param>
/// <param name="Position">Source position.</param>
internal sealed record PresetLoopNode(
    PresetSyntaxNode Count,
    IReadOnlyList<PresetSyntaxNode> Body,
    int Position) : PresetSyntaxNode(Position);

/// <summary>Milkdrop's <c>while(condition, statements)</c> construct.</summary>
/// <param name="Condition">Loop condition.</param>
/// <param name="Body">Statements repeated while the condition holds.</param>
/// <param name="Position">Source position.</param>
internal sealed record PresetWhileNode(
    PresetSyntaxNode Condition,
    IReadOnlyList<PresetSyntaxNode> Body,
    int Position) : PresetSyntaxNode(Position);

/// <summary>A sequence of statements, which is the root of one compiled block.</summary>
/// <param name="Statements">Statements in source order.</param>
internal sealed record PresetBlockNode(IReadOnlyList<PresetSyntaxNode> Statements) : PresetSyntaxNode(0);

/// <summary>
/// A semicolon-separated sequence of statements used where one expression value is
/// expected, as in <c>if(a, x = 1; y = 2, b)</c>. The value is the last statement.
/// </summary>
/// <param name="Statements">Statements in source order.</param>
/// <param name="Position">Source position.</param>
internal sealed record PresetSequenceNode(
    IReadOnlyList<PresetSyntaxNode> Statements,
    int Position) : PresetSyntaxNode(Position);
