using System.Text;

namespace Orynivo.Visualization;

/// <summary>
/// Translates the parsed preset expression language into SkSL. It consumes the same
/// <see cref="PresetSyntaxNode"/> tree <see cref="PresetCompiler"/> built for the interpreter, so
/// the GPU and the CPU cannot disagree about what a block means. The language is scalar, so the
/// emitter works in floats and only has to rename the engine-bound values and report the uniforms
/// the caller has to seed. <c>megabuf</c>/<c>gmegabuf</c> and <c>rand</c> are deliberately refused:
/// the first is global mutable state the GPU cannot share, and the second is not reproducible, so a
/// block that uses either stays on the interpreter rather than rendering a different picture.
/// </summary>
public static class PresetExpressionTranspiler
{
    /// <summary>Prefix of the local variables the engine seeds for every pixel.</summary>
    private const string LocalPrefix = "_orynivo_";

    /// <summary>Prefix of the uniforms the emitter reports to its caller.</summary>
    private const string VariablePrefix = "_orynivo_v_";

    /// <summary>The local that carries the horizontal sampling position.</summary>
    internal const string LocalX = LocalPrefix + "x";

    /// <summary>The local that carries the vertical sampling position.</summary>
    internal const string LocalY = LocalPrefix + "y";

    /// <summary>The local that carries the polar radius.</summary>
    internal const string LocalRadius = LocalPrefix + "rad";

    /// <summary>The local that carries the polar angle.</summary>
    internal const string LocalAngle = LocalPrefix + "ang";

    /// <summary>
    /// The C-like remainder helper. It reproduces <see cref="PresetCompiler"/>'s <c>fmod</c>, which
    /// truncates towards zero, while SkSL's own <c>mod</c> floors; a negative dividend would
    /// otherwise render a different picture on the two paths.
    /// </summary>
    public const string FmodHelper =
        "float orynivoFmod(float a, float b) { return b == 0.0 ? 0.0 : a - b * float(int(a / b)); }\n";

    /// <summary>The variables the engine binds to the enclosing warp main rather than to a uniform.</summary>
    private static readonly HashSet<string> EngineLocals = new(StringComparer.Ordinal)
    {
        "x", "y", "rad", "ang"
    };

    /// <summary>
    /// The bound of a translated <c>loop</c>. Skia unrolls a runtime effect's loops, so a larger
    /// count cannot be expressed; a constant count above the bound therefore stays on the
    /// interpreter instead of rendering short.
    /// </summary>
    private const int MaxGpuLoopIterations = 64;

    /// <summary>Returns the uniform name the emitter uses for one variable.</summary>
    /// <param name="variable">Preset variable name.</param>
    /// <returns>The emitted uniform name.</returns>
    public static string UniformName(string variable) => VariablePrefix + SkSL.SafeName(variable);

    /// <summary>Returns the block-local name the emitter uses for a written variable.</summary>
    /// <param name="variable">Preset variable name.</param>
    /// <returns>The emitted local name.</returns>
    private static string LocalName(string variable) => "_orynivo_l_" + SkSL.SafeName(variable);

    /// <summary>
    /// Reports whether a per-pixel block can run on the GPU without a carried value. The GPU
    /// evaluates every pixel independently, so a variable the block writes must be assigned before
    /// it is read on every path; a value written by one pixel and read by the next would otherwise
    /// make the picture depend on the execution order. The engine re-seeds <c>x</c>, <c>y</c>,
    /// <c>rad</c>, and <c>ang</c> for every pixel, so they always count as assigned.
    /// </summary>
    /// <param name="program">Per-pixel program to test.</param>
    /// <returns><see langword="true"/> when the block only reads a written variable after assigning it.</returns>
    public static bool CanRunInParallel(PresetProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.Syntax is null)
            return true;

        var written = new HashSet<string>(program.WrittenVariables, StringComparer.Ordinal);
        if (written.Count == 0)
            return true;

        var assigned = new HashSet<string>(EngineLocals, StringComparer.Ordinal);
        return StatementsAssignBeforeRead(program.Syntax.Statements, assigned, written);
    }

    /// <summary>Checks a statement list for a read of a written value before its assignment.</summary>
    /// <param name="statements">Statements to walk.</param>
    /// <param name="assigned">Variables definitely assigned before this point.</param>
    /// <param name="written">Variables the block writes.</param>
    /// <returns><see langword="false"/> when a written variable is read before it is assigned.</returns>
    private static bool StatementsAssignBeforeRead(
        IReadOnlyList<PresetSyntaxNode> statements,
        HashSet<string> assigned,
        HashSet<string> written)
    {
        foreach (var statement in statements)
        {
            if (!StatementAssignBeforeRead(statement, assigned, written))
                return false;
        }

        return true;
    }

    /// <summary>Checks one statement for a read of a written value before its assignment.</summary>
    /// <param name="statement">Statement to check.</param>
    /// <param name="assigned">Variables definitely assigned before this point.</param>
    /// <param name="written">Variables the block writes.</param>
    /// <returns><see langword="false"/> when a written variable is read before it is assigned.</returns>
    private static bool StatementAssignBeforeRead(
        PresetSyntaxNode statement,
        HashSet<string> assigned,
        HashSet<string> written) => statement switch
    {
        PresetAssignmentNode assignment =>
            ExpressionAssignBeforeRead(assignment.Value, assigned, written) && MarkAssigned(assignment.Name, assigned),
        PresetLoopNode loop =>
            ExpressionAssignBeforeRead(loop.Count, assigned, written) &&
            // The loop may run zero times, so a value it writes is not definite afterwards; a read
            // inside the body is checked against a copy that does carry the body's own assignments.
            StatementsAssignBeforeRead(loop.Body, new HashSet<string>(assigned, StringComparer.Ordinal), written),
        PresetBlockNode block => StatementsAssignBeforeRead(block.Statements, assigned, written),
        PresetMegaBufferNode buffer =>
            ExpressionAssignBeforeRead(buffer.Index, assigned, written) &&
            (buffer.Value is null || ExpressionAssignBeforeRead(buffer.Value, assigned, written)),
        _ => ExpressionAssignBeforeRead(statement, assigned, written)
    };

    /// <summary>Marks a variable as definitely assigned and reports success.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="assigned">Assignment set to extend.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    private static bool MarkAssigned(string name, HashSet<string> assigned)
    {
        assigned.Add(name);
        return true;
    }

    /// <summary>Checks an expression for a read of a written value before its assignment.</summary>
    /// <param name="expression">Expression to check.</param>
    /// <param name="assigned">Variables definitely assigned before this point.</param>
    /// <param name="written">Variables the block writes.</param>
    /// <returns><see langword="false"/> when a written variable is read before it is assigned.</returns>
    private static bool ExpressionAssignBeforeRead(
        PresetSyntaxNode expression,
        HashSet<string> assigned,
        HashSet<string> written)
    {
        switch (expression)
        {
            case PresetVariableNode variable:
                return !written.Contains(variable.Name) || assigned.Contains(variable.Name);
            case PresetUnaryNode unary:
                return ExpressionAssignBeforeRead(unary.Operand, assigned, written);
            case PresetBinaryNode binary:
                return ExpressionAssignBeforeRead(binary.Left, assigned, written) &&
                       ExpressionAssignBeforeRead(binary.Right, assigned, written);
            case PresetConditionalNode conditional:
                return ExpressionAssignBeforeRead(conditional.Condition, assigned, written) &&
                       ExpressionAssignBeforeRead(conditional.WhenTrue, assigned, written) &&
                       ExpressionAssignBeforeRead(conditional.WhenFalse, assigned, written);
            case PresetCallNode call:
                foreach (var argument in call.Arguments)
                {
                    if (!ExpressionAssignBeforeRead(argument, assigned, written))
                        return false;
                }

                return true;
            default:
                return true;
        }
    }

    /// <summary>
    /// Emits the statements of one program as SkSL. The result reads and writes the engine locals
    /// <c>x</c>, <c>y</c>, <c>rad</c>, and <c>ang</c> and reads the uniforms named by
    /// <see cref="UniformName"/>.
    /// </summary>
    /// <param name="program">Compiled program to emit.</param>
    /// <param name="body">Emitted statements, or an empty string for an empty program.</param>
    /// <param name="uniforms">The preset variable names the caller has to bind as uniforms.</param>
    /// <param name="error">Failure reason, or <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the whole block translated.</returns>
    public static bool TryTranspile(
        PresetProgram program,
        out string body,
        out IReadOnlyList<string> uniforms,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(program);
        var state = new EmitState(program.WrittenVariables);
        try
        {
            body = program.Syntax is null ? string.Empty : EmitProgram(program.Syntax, state);
            uniforms = [.. state.Uniforms];
            error = null;
            return true;
        }
        catch (PresetExpressionException exception)
        {
            body = string.Empty;
            uniforms = [];
            error = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Emits a whole program. A variable the block writes but the engine does not re-seed per pixel
    /// becomes a local initialized from its uniform, so the generated SkSL compiles; the caller
    /// decides whether the block's carry semantics are acceptable for its execution model.
    /// </summary>
    /// <param name="block">Block to emit.</param>
    /// <param name="state">Emit state collecting the uniforms.</param>
    /// <returns>The emitted program.</returns>
    private static string EmitProgram(PresetBlockNode block, EmitState state)
    {
        var builder = new StringBuilder();
        foreach (var name in state.Written.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (EngineLocals.Contains(name))
                continue;

            var uniform = UniformName(name);
            state.Uniforms.Add(name);
            builder.Append(SkSL.Indent(1))
                .Append("float ").Append(LocalName(name))
                .Append(" = ").Append(uniform).Append(";\n");
        }

        builder.Append(EmitBlock(block, 1, state));
        return builder.ToString();
    }

    /// <summary>Emits a statement list.</summary>
    /// <param name="block">Block to emit.</param>
    /// <param name="depth">Indentation depth.</param>
    /// <param name="state">Emit state collecting the uniforms.</param>
    /// <returns>The emitted statements.</returns>
    private static string EmitBlock(PresetBlockNode block, int depth, EmitState state)
    {
        var builder = new StringBuilder();
        foreach (var statement in block.Statements)
            EmitStatement(builder, statement, depth, state);
        return builder.ToString();
    }

    /// <summary>Emits one statement.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="statement">Statement node.</param>
    /// <param name="depth">Indentation depth.</param>
    /// <param name="state">Emit state collecting the uniforms.</param>
    private static void EmitStatement(StringBuilder builder, PresetSyntaxNode statement, int depth, EmitState state)
    {
        switch (statement)
        {
            case PresetAssignmentNode assignment:
                EmitAssignment(builder, assignment, depth, state);
                return;
            case PresetLoopNode loop:
                EmitLoop(builder, loop, depth, state);
                return;
            case PresetMegaBufferNode:
                throw new PresetExpressionException(
                    "The shared memory buffer stays on the interpreter.",
                    statement.Position);
            case PresetBlockNode block:
                builder.Append(EmitBlock(block, depth, state));
                return;
            default:
                builder.Append(SkSL.Indent(depth))
                    .Append(EmitExpression(statement, state))
                    .Append(";\n");
                return;
        }
    }

    /// <summary>Emits an assignment, turning a compound operator into a plain read-modify-write.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="assignment">Assignment node.</param>
    /// <param name="depth">Indentation depth.</param>
    /// <param name="state">Emit state collecting the uniforms.</param>
    private static void EmitAssignment(
        StringBuilder builder,
        PresetAssignmentNode assignment,
        int depth,
        EmitState state)
    {
        var target = Variable(assignment.Name, state);
        var value = EmitExpression(assignment.Value, state);
        builder.Append(SkSL.Indent(depth)).Append(target);
        if (assignment.Compound is { } compound)
        {
            // A compound assignment becomes an explicit read-modify-write, because the expression
            // language has no vector swizzle targets and this keeps the emitted type a plain float.
            var operation = compound switch
            {
                PresetTokenKind.Plus => "+",
                PresetTokenKind.Minus => "-",
                PresetTokenKind.Star => "*",
                PresetTokenKind.Slash => "/",
                _ => null
            };
            builder.Append(" = ").Append(operation is null
                ? $"orynivoFmod({target}, {value})"
                : $"({target} {operation} {value})");
        }
        else
        {
            builder.Append(" = ").Append(value);
        }

        builder.Append(";\n");
    }

    /// <summary>Emits Milkdrop's <c>loop</c> as a bounded SkSL for loop.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="loop">Loop node.</param>
    /// <param name="depth">Indentation depth.</param>
    /// <param name="state">Emit state collecting the uniforms.</param>
    private static void EmitLoop(StringBuilder builder, PresetLoopNode loop, int depth, EmitState state)
    {
        // Skia unrolls a runtime effect's loops, so the iteration count has to be bounded; a
        // constant count above the bound stays on the interpreter rather than rendering short.
        if (loop.Count is PresetLiteralNode literal)
        {
            var count = (int)literal.Value;
            if (count > MaxGpuLoopIterations)
            {
                throw new PresetExpressionException(
                    $"A per-pixel loop of {count} iterations stays on the interpreter.",
                    loop.Position);
            }
        }

        var counter = $"_orynivoLoop{depth}";
        var countText = EmitExpression(loop.Count, state);
        var indent = SkSL.Indent(depth);
        builder.Append(indent).Append("for (int ").Append(counter).Append(" = 0; ")
            .Append(counter).Append(" < ").Append(MaxGpuLoopIterations)
            .Append("; ").Append(counter).Append("++) {\n");
        builder.Append(indent).Append("    if (float(").Append(counter).Append(") >= ").Append(countText)
            .Append(") { break; }\n");
        foreach (var statement in loop.Body)
            EmitStatement(builder, statement, depth + 1, state);
        builder.Append(indent).Append("}\n");
    }

    /// <summary>Emits one expression.</summary>
    /// <param name="expression">Expression node.</param>
    /// <param name="state">Emit state collecting the uniforms.</param>
    /// <returns>The expression text.</returns>
    private static string EmitExpression(PresetSyntaxNode expression, EmitState state) => expression switch
    {
        PresetLiteralNode literal => SkSL.Literal(literal.Value),
        PresetVariableNode variable => Variable(variable.Name, state),
        PresetUnaryNode unary => EmitUnary(unary, state),
        PresetBinaryNode binary => EmitBinary(binary, state),
        PresetConditionalNode conditional =>
            $"(({EmitExpression(conditional.Condition, state)}) != 0.0 ? {EmitExpression(conditional.WhenTrue, state)} : {EmitExpression(conditional.WhenFalse, state)})",
        PresetCallNode call => EmitCall(call, state),
        _ => throw new PresetExpressionException("The expression has no SkSL translation.", expression.Position)
    };

    /// <summary>Emits a variable reference, as an engine local, a block local, or a uniform.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="state">Emit state collecting the uniforms.</param>
    /// <returns>The emitted reference.</returns>
    private static string Variable(string name, EmitState state)
    {
        if (EngineLocals.Contains(name))
        {
            return name switch
            {
                "x" => LocalX,
                "y" => LocalY,
                "rad" => LocalRadius,
                _ => LocalAngle
            };
        }

        if (state.Written.Contains(name))
            return LocalName(name);

        state.Uniforms.Add(name);
        return UniformName(name);
    }

    /// <summary>Emits a prefix unary operator, keeping the preset convention that zero is false.</summary>
    /// <param name="unary">Unary node.</param>
    /// <param name="state">Emit state collecting the uniforms.</param>
    /// <returns>The expression text.</returns>
    private static string EmitUnary(PresetUnaryNode unary, EmitState state)
    {
        var operand = EmitExpression(unary.Operand, state);
        return unary.Operator switch
        {
            PresetTokenKind.Minus => $"(-{operand})",
            PresetTokenKind.Plus => operand,
            PresetTokenKind.Not => $"(({operand}) == 0.0 ? 1.0 : 0.0)",
            _ => throw new PresetExpressionException(
                $"The unary operator '{unary.Operator}' has no SkSL translation.",
                unary.Position)
        };
    }

    /// <summary>Emits a binary operator, mapping comparisons onto the one-or-zero convention.</summary>
    /// <param name="binary">Binary node.</param>
    /// <param name="state">Emit state collecting the uniforms.</param>
    /// <returns>The expression text.</returns>
    private static string EmitBinary(PresetBinaryNode binary, EmitState state)
    {
        var left = EmitExpression(binary.Left, state);
        var right = EmitExpression(binary.Right, state);
        return binary.Operator switch
        {
            PresetTokenKind.Plus => $"({left} + {right})",
            PresetTokenKind.Minus => $"({left} - {right})",
            PresetTokenKind.Star => $"({left} * {right})",
            PresetTokenKind.Slash => $"({left} / {right})",
            PresetTokenKind.Percent => $"orynivoFmod({left}, {right})",
            PresetTokenKind.And => $"((({left}) != 0.0 && ({right}) != 0.0) ? 1.0 : 0.0)",
            PresetTokenKind.Or => $"((({left}) != 0.0 || ({right}) != 0.0) ? 1.0 : 0.0)",
            PresetTokenKind.Equal => $"(({left}) == ({right}) ? 1.0 : 0.0)",
            PresetTokenKind.NotEqual => $"(({left}) != ({right}) ? 1.0 : 0.0)",
            PresetTokenKind.Less => $"(({left}) < ({right}) ? 1.0 : 0.0)",
            PresetTokenKind.LessOrEqual => $"(({left}) <= ({right}) ? 1.0 : 0.0)",
            PresetTokenKind.Greater => $"(({left}) > ({right}) ? 1.0 : 0.0)",
            PresetTokenKind.GreaterOrEqual => $"(({left}) >= ({right}) ? 1.0 : 0.0)",
            _ => throw new PresetExpressionException(
                $"The binary operator '{binary.Operator}' has no SkSL translation.",
                binary.Position)
        };
    }

    /// <summary>Emits a call to one of the Milkdrop functions.</summary>
    /// <param name="call">Call node.</param>
    /// <param name="state">Emit state collecting the uniforms.</param>
    /// <returns>The expression text.</returns>
    private static string EmitCall(PresetCallNode call, EmitState state)
    {
        var arguments = new List<string>(call.Arguments.Count);
        foreach (var argument in call.Arguments)
            arguments.Add(EmitExpression(argument, state));

        var name = call.Name.ToLowerInvariant();
        string Unary(Func<string, string> build)
        {
            Require(call, arguments, 1);
            return build(arguments[0]);
        }

        string Binary(Func<string, string, string> build)
        {
            Require(call, arguments, 2);
            return build(arguments[0], arguments[1]);
        }

        if (name == "if")
        {
            Require(call, arguments, 3);
            return $"(({arguments[0]}) != 0.0 ? {arguments[1]} : {arguments[2]})";
        }

        if (name == "sigmoid")
        {
            // Some presets pass a second argument; only the value matters, matching the interpreter.
            if (arguments.Count == 0)
                throw new PresetExpressionException("'sigmoid' expects 1 argument", call.Position);
            return $"(1.0 / (1.0 + exp(-({arguments[0]}))))";
        }

        return name switch
        {
            "sin" or "cos" or "tan" or "asin" or "acos" or "atan" or "sqrt" or "abs" or "exp"
                or "log" or "floor" or "ceil" or "sign" or "min" or "max" or "pow"
                => $"{name}({string.Join(", ", arguments)})",
            "atan2" => Binary((y, x) => $"atan({y}, {x})"),
            "log10" => Unary(value => $"(log({value}) * 0.4342944819032518)"),
            "int" => Unary(value => $"float(int({value}))"),
            "fmod" => Binary((left, right) => $"orynivoFmod({left}, {right})"),
            "sqr" => Unary(value => $"(({value}) * ({value}))"),
            "above" => Binary((left, right) => $"(({left}) > ({right}) ? 1.0 : 0.0)"),
            "below" => Binary((left, right) => $"(({left}) < ({right}) ? 1.0 : 0.0)"),
            "equal" => Binary((left, right) => $"(({left}) == ({right}) ? 1.0 : 0.0)"),
            "band" => Binary((left, right) => $"float(int({left}) & int({right}))"),
            "bor" => Binary((left, right) => $"float(int({left}) | int({right}))"),
            "bnot" => Unary(value => $"float(~int({value}))"),
            "rand" => throw new PresetExpressionException(
                "The per-pixel random function stays on the interpreter.",
                call.Position),
            "megabuf" or "gmegabuf" => throw new PresetExpressionException(
                "The shared memory buffer stays on the interpreter.",
                call.Position),
            _ => throw new PresetExpressionException(
                $"The function '{call.Name}' has no SkSL translation.",
                call.Position)
        };
    }

    /// <summary>Checks a call's arity.</summary>
    /// <param name="call">Call node, for the error message.</param>
    /// <param name="arguments">Emitted arguments.</param>
    /// <param name="arity">Required arity.</param>
    private static void Require(PresetCallNode call, List<string> arguments, int arity)
    {
        if (arguments.Count != arity)
            throw new PresetExpressionException(
                $"'{call.Name}' expects {arity} argument(s)",
                call.Position);
    }

    /// <summary>The state one emission carries.</summary>
    private sealed class EmitState
    {
        private readonly IReadOnlyCollection<string> _written;

        /// <summary>Creates the state for one program.</summary>
        /// <param name="written">Variables the program assigns to.</param>
        public EmitState(IReadOnlyCollection<string> written) => _written = written;

        /// <summary>Gets the variables the program assigns to.</summary>
        public IReadOnlyCollection<string> Written => _written;

        /// <summary>Gets the preset variable names that have to be bound as uniforms.</summary>
        public SortedSet<string> Uniforms { get; } = new(StringComparer.Ordinal);
    }
}
