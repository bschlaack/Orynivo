using System.Linq.Expressions;
using System.Reflection;

namespace Orynivo.Visualization;

/// <summary>
/// Compiles a preset expression block into a <see cref="PresetProgram"/>. It implements the
/// Milkdrop/AVS expression subset that preset authors actually use: assignments,
/// arithmetic, comparisons, logical operators, the ternary operator, the usual math
/// functions, and the <c>if(condition, then, else)</c> helper. Loops, arrays, and shaders
/// are deliberately out of scope, and unknown functions are reported as errors. The source is
/// parsed once into a <see cref="PresetSyntaxNode"/> tree, which both the LINQ back end here and
/// <see cref="PresetExpressionTranspiler"/> consume, so the interpreter and the GPU can never
/// disagree about what a block means.
/// </summary>
public static class PresetCompiler
{
    private static readonly MethodInfo FmodMethod =
        typeof(PresetCompiler).GetMethod(nameof(Fmod), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo SignMethod =
        typeof(PresetCompiler).GetMethod(nameof(Sign), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo RandomMethod =
        typeof(PresetCompiler).GetMethod(nameof(NextRandom), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo TotalIterationsField =
        typeof(PresetCompiler).GetField(nameof(_totalLoopIterations), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly ConstructorInfo LoopOverflowConstructor =
        typeof(PresetExpressionException).GetConstructor([typeof(string), typeof(int)])!;

    /// <summary>
    /// Upper bound on the loop iterations of one program execution, counted across every nested
    /// loop. The per-loop clamp cannot bound nesting: <c>loop(20000, loop(20000, ...))</c> is four
    /// hundred million iterations, which freezes the window without an exception to catch.
    /// </summary>
    internal const int MaxTotalLoopIterations = 2_000_000;

    private static int _totalLoopIterations;

    /// <summary>Restarts the loop budget for one program execution.</summary>
    internal static void ResetLoopBudget() => _totalLoopIterations = 0;

    /// <summary>Compiles one expression block.</summary>
    /// <param name="source">
    /// Statements separated by semicolons, or <see langword="null"/> or empty for a program
    /// that does nothing.
    /// </param>
    /// <param name="layout">
    /// Optional shared layout. Every program of one preset must compile against the same
    /// layout so user variables carry from the per-frame into the per-pixel stage.
    /// </param>
    /// <returns>The compiled program.</returns>
    /// <exception cref="PresetExpressionException">The source is not valid.</exception>
    public static PresetProgram Compile(string? source, PresetVariableLayout? layout = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            return layout is null
                ? PresetProgram.Empty
                : new PresetProgram(layout, null, [], [], null);

        var state = new CompileState(layout);
        var statements = new List<PresetSyntaxNode>();
        var lexer = new PresetLexer(source);
        var current = lexer.Next();

        while (current.Kind != PresetTokenKind.End)
        {
            if (current.Kind == PresetTokenKind.Semicolon)
            {
                current = lexer.Next();
                continue;
            }

            statements.Add(ParseStatement(lexer, ref current));
            if (current.Kind is PresetTokenKind.End or PresetTokenKind.Semicolon)
            {
                current = current.Kind == PresetTokenKind.Semicolon ? lexer.Next() : current;
                continue;
            }

            throw new PresetExpressionException($"Unexpected '{current.Text}'", current.Position);
        }

        if (statements.Count == 0)
            return PresetProgram.Empty;

        var syntax = new PresetBlockNode(statements);
        var body = new List<Expression>(statements.Count);
        foreach (var statement in statements)
            body.Add(CompileStatement(state, statement));

        var block = Expression.Block(body);
        var lambda = Expression.Lambda<Action<double[]>>(block, state.Slots);
        return new PresetProgram(state.Layout, lambda.Compile(), state.ReferencedNames, state.WrittenNames, syntax);
    }

    /// <summary>Parses one statement, which is either an assignment or a bare expression.</summary>
    /// <param name="lexer">Token source.</param>
    /// <param name="current">Current token, advanced past the statement.</param>
    /// <returns>The parsed statement node.</returns>
    private static PresetSyntaxNode ParseStatement(PresetLexer lexer, ref PresetToken current)
    {
        if (current.Kind == PresetTokenKind.Identifier &&
            (string.Equals(current.Text, "loop", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(current.Text, "while", StringComparison.OrdinalIgnoreCase)))
        {
            var isWhile = string.Equals(current.Text, "while", StringComparison.OrdinalIgnoreCase);
            var position = current.Position;
            var next = lexer.Next();
            if (next.Kind == PresetTokenKind.OpenParenthesis)
            {
                // Milkdrop's loop(count, statements) and while(condition, statements) repeat a
                // statement list. They are statements, not expressions, so they are handled before
                // the expression parser sees the call.
                current = lexer.Next();
                return isWhile
                    ? ParseWhile(lexer, ref current, position)
                    : ParseLoop(lexer, ref current, position);
            }

            current = next;
            return ParseExpression(lexer, ref current, isWhile ? "while" : "loop", position);
        }

        return ParseAssignable(lexer, ref current);
    }

    /// <summary>
    /// Parses an assignment, a shared-memory-buffer write, or a plain expression. Milkdrop's
    /// <c>if(condition, then, else)</c> takes assignments as arguments, so the argument parser uses
    /// this as well; a nested assignment yields the assigned value, like HLSL.
    /// </summary>
    /// <param name="lexer">Token source.</param>
    /// <param name="current">Current token, advanced past the expression.</param>
    /// <returns>The parsed node.</returns>
    private static PresetSyntaxNode ParseAssignable(PresetLexer lexer, ref PresetToken current)
    {
        if (current.Kind == PresetTokenKind.Identifier)
        {
            var name = current.Text;
            var position = current.Position;
            var next = lexer.Next();
            if (next.Kind is PresetTokenKind.Assign or PresetTokenKind.AssignCompound)
            {
                var compound = next;
                current = lexer.Next();
                var value = ParseExpression(lexer, ref current);
                return new PresetAssignmentNode(
                    name,
                    value,
                    compound.Kind == PresetTokenKind.AssignCompound ? CompoundOperator(compound) : null,
                    position);
            }

            // Not an assignment: rewind by re-parsing the identifier as a primary expression.
            current = next;
            return ParseExpression(lexer, ref current, name, position);
        }

        return ParseExpression(lexer, ref current);
    }

    private static PresetSyntaxNode ParseExpression(PresetLexer lexer, ref PresetToken current) =>
        ParseExpression(lexer, ref current, null, 0);

    private static PresetSyntaxNode ParseExpression(
        PresetLexer lexer,
        ref PresetToken current,
        string? leadingIdentifier,
        int leadingPosition)
    {
        var condition = ParseBinary(lexer, ref current, leadingIdentifier, leadingPosition, 0);
        if (current.Kind != PresetTokenKind.Question)
            return condition;

        current = lexer.Next();
        var whenTrue = ParseExpression(lexer, ref current);
        if (current.Kind != PresetTokenKind.Colon)
            throw new PresetExpressionException("Expected ':' in a ternary expression", current.Position);

        current = lexer.Next();
        var whenFalse = ParseExpression(lexer, ref current);
        return new PresetConditionalNode(condition, whenTrue, whenFalse, current.Position);
    }

    /// <summary>Precedence climbing over the binary operators.</summary>
    private static PresetSyntaxNode ParseBinary(
        PresetLexer lexer,
        ref PresetToken current,
        string? leadingIdentifier,
        int leadingPosition,
        int minimumPrecedence)
    {
        var left = ParseUnary(lexer, ref current, leadingIdentifier, leadingPosition);
        while (true)
        {
            var precedence = PrecedenceOf(current.Kind);
            if (precedence < 0 || precedence < minimumPrecedence)
                return left;

            var operation = current;
            current = lexer.Next();
            var right = ParseBinary(lexer, ref current, null, 0, precedence + 1);
            left = new PresetBinaryNode(operation.Kind, left, right, operation.Position);
        }
    }

    private static PresetSyntaxNode ParseUnary(
        PresetLexer lexer,
        ref PresetToken current,
        string? leadingIdentifier,
        int leadingPosition)
    {
        if (leadingIdentifier is not null)
            return ParsePrimaryFromIdentifier(lexer, ref current, leadingIdentifier, leadingPosition);

        switch (current.Kind)
        {
            case PresetTokenKind.Minus:
                {
                    var position = current.Position;
                    current = lexer.Next();
                    return new PresetUnaryNode(PresetTokenKind.Minus, ParseUnary(lexer, ref current, null, 0), position);
                }
            case PresetTokenKind.Plus:
                {
                    current = lexer.Next();
                    return ParseUnary(lexer, ref current, null, 0);
                }
            case PresetTokenKind.Not:
                {
                    // "!x" is true when x is zero, matching the preset convention.
                    var position = current.Position;
                    current = lexer.Next();
                    return new PresetUnaryNode(PresetTokenKind.Not, ParseUnary(lexer, ref current, null, 0), position);
                }
            default:
                return ParsePrimary(lexer, ref current);
        }
    }

    private static PresetSyntaxNode ParsePrimary(PresetLexer lexer, ref PresetToken current)
    {
        switch (current.Kind)
        {
            case PresetTokenKind.Number:
                {
                    var value = new PresetLiteralNode(current.Value, current.Position);
                    current = lexer.Next();
                    return value;
                }
            case PresetTokenKind.Identifier:
                {
                    var name = current.Text;
                    var position = current.Position;
                    current = lexer.Next();
                    return ParsePrimaryFromIdentifier(lexer, ref current, name, position);
                }
            case PresetTokenKind.OpenParenthesis:
                {
                    current = lexer.Next();
                    var inner = ParseExpression(lexer, ref current);
                    if (current.Kind != PresetTokenKind.CloseParenthesis)
                        throw new PresetExpressionException("Expected ')'", current.Position);
                    current = lexer.Next();
                    return inner;
                }
            default:
                throw new PresetExpressionException($"Unexpected '{current.Text}'", current.Position);
        }
    }

    private static PresetSyntaxNode ParsePrimaryFromIdentifier(
        PresetLexer lexer,
        ref PresetToken current,
        string name,
        int position)
    {
        if (current.Kind != PresetTokenKind.OpenParenthesis)
        {
            if (string.Equals(name, "pi", StringComparison.OrdinalIgnoreCase))
                return new PresetLiteralNode(Math.PI, position);
            return new PresetVariableNode(name, position);
        }

        if (string.Equals(name, "loop", StringComparison.OrdinalIgnoreCase))
        {
            // loop(count, statements) is a statement, but presets also nest it inside if() and
            // other constructs, so the call parser accepts it wherever a primary expression starts.
            current = lexer.Next();
            return ParseLoop(lexer, ref current, position);
        }

        if (string.Equals(name, "while", StringComparison.OrdinalIgnoreCase))
        {
            current = lexer.Next();
            return ParseWhile(lexer, ref current, position);
        }

        current = lexer.Next();
        var arguments = new List<PresetSyntaxNode>();
        if (current.Kind != PresetTokenKind.CloseParenthesis)
        {
            while (true)
            {
                // An argument may be an assignment, which Milkdrop's if() uses:
                // if(condition, x = 1, y = 2). The nested assignment yields its value.
                var argument = ParseAssignable(lexer, ref current);
                if (current.Kind == PresetTokenKind.Semicolon)
                {
                    // A semicolon inside the parentheses continues the argument as a statement
                    // sequence, as in if(a, x = 1; y = 2, b); the value is the last statement.
                    var statements = new List<PresetSyntaxNode> { argument };
                    while (current.Kind == PresetTokenKind.Semicolon)
                    {
                        current = lexer.Next();
                        // Presets repeat the separator, so an empty statement is not an argument.
                        while (current.Kind == PresetTokenKind.Semicolon)
                            current = lexer.Next();
                        if (current.Kind is PresetTokenKind.Comma or PresetTokenKind.CloseParenthesis)
                            break;
                        statements.Add(ParseAssignable(lexer, ref current));
                    }

                    argument = new PresetSequenceNode(statements, position);
                }

                arguments.Add(argument);
                if (current.Kind != PresetTokenKind.Comma)
                    break;
                current = lexer.Next();
            }
        }

        if (current.Kind != PresetTokenKind.CloseParenthesis)
            throw new PresetExpressionException("Expected ')'", current.Position);
        current = lexer.Next();

        // Presets write a buffer entry by assigning to the call: gmegabuf(i) = value;. The
        // gmegabuf(index, value) form stays a call, which the compiler turns into a write.
        if (arguments.Count == 1 &&
            (string.Equals(name, "megabuf", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(name, "gmegabuf", StringComparison.OrdinalIgnoreCase)) &&
            current.Kind is PresetTokenKind.Assign or PresetTokenKind.AssignCompound)
        {
            var compound = current;
            current = lexer.Next();
            var value = ParseExpression(lexer, ref current);
            return new PresetMegaBufferNode(
                arguments[0],
                value,
                compound.Kind == PresetTokenKind.AssignCompound ? CompoundOperator(compound) : null,
                position);
        }

        return new PresetCallNode(name, arguments, position);
    }

    /// <summary>
    /// Parses Milkdrop's <c>loop(count, statements)</c> construct.
    /// </summary>
    /// <param name="lexer">Token source.</param>
    /// <param name="current">Token after the opening parenthesis.</param>
    /// <param name="position">Source position of the <c>loop</c> name.</param>
    /// <returns>The parsed loop node.</returns>
    private static PresetSyntaxNode ParseLoop(
        PresetLexer lexer,
        ref PresetToken current,
        int position)
    {
        var count = ParseExpression(lexer, ref current);
        if (current.Kind != PresetTokenKind.Comma)
            throw new PresetExpressionException("Expected ',' after the loop count", position);

        current = lexer.Next();
        var body = new List<PresetSyntaxNode>();
        while (current.Kind != PresetTokenKind.End && current.Kind != PresetTokenKind.CloseParenthesis)
        {
            // The statements of a loop body are separated by semicolons that belong to them.
            while (current.Kind == PresetTokenKind.Semicolon)
                current = lexer.Next();
            if (current.Kind == PresetTokenKind.CloseParenthesis)
                break;
            body.Add(ParseStatement(lexer, ref current));
        }

        if (current.Kind != PresetTokenKind.CloseParenthesis)
            throw new PresetExpressionException("Expected ')' to close loop(...)", current.Position);

        current = lexer.Next();
        return new PresetLoopNode(count, body, position);
    }

    /// <summary>
    /// Parses Milkdrop's <c>while(condition)</c> and <c>while(condition, statements)</c> constructs,
    /// which presets use as a bounded data-driven loop next to <c>loop(count, statements)</c>.
    /// </summary>
    /// <param name="lexer">Token source.</param>
    /// <param name="current">Token after the opening parenthesis.</param>
    /// <param name="position">Source position of the <c>while</c> name.</param>
    /// <returns>The parsed while node.</returns>
    private static PresetSyntaxNode ParseWhile(
        PresetLexer lexer,
        ref PresetToken current,
        int position)
    {
        var condition = ParseExpression(lexer, ref current);
        var body = new List<PresetSyntaxNode>();
        if (current.Kind == PresetTokenKind.Comma)
        {
            current = lexer.Next();
            while (current.Kind != PresetTokenKind.End && current.Kind != PresetTokenKind.CloseParenthesis)
            {
                while (current.Kind == PresetTokenKind.Semicolon)
                    current = lexer.Next();
                if (current.Kind == PresetTokenKind.CloseParenthesis)
                    break;
                body.Add(ParseStatement(lexer, ref current));
            }
        }
        else if (current.Kind != PresetTokenKind.CloseParenthesis)
        {
            // A real collection writes the one-argument form, where the condition is re-evaluated
            // until it turns false and the repeated work sits inside it as `exec2(body, condition)`.
            throw new PresetExpressionException("Expected ',' or ')' after the while condition", position);
        }

        if (current.Kind != PresetTokenKind.CloseParenthesis)
            throw new PresetExpressionException("Expected ')' to close while(...)", current.Position);

        current = lexer.Next();
        return new PresetWhileNode(condition, body, position);
    }

    /// <summary>Compiles one parsed statement into the LINQ tree the interpreter runs.</summary>
    /// <param name="state">Compile state.</param>
    /// <param name="statement">Statement node.</param>
    /// <returns>The statement expression.</returns>
    private static Expression CompileStatement(CompileState state, PresetSyntaxNode statement) => statement switch
    {
        PresetAssignmentNode assignment => CompileAssignment(state, assignment),
        PresetMegaBufferNode buffer => CompileMegaBuffer(state, buffer),
        PresetLoopNode loop => CompileLoop(state, loop),
        PresetWhileNode whileLoop => CompileWhile(state, whileLoop),
        _ => CompileExpression(state, statement)
    };

    /// <summary>Compiles an assignment, plain or compound.</summary>
    /// <param name="state">Compile state.</param>
    /// <param name="assignment">Assignment node.</param>
    /// <returns>The assignment expression.</returns>
    private static Expression CompileAssignment(CompileState state, PresetAssignmentNode assignment)
    {
        var value = CompileExpression(state, assignment.Value);
        var slot = state.WriteSlot(assignment.Name);
        return assignment.Compound is { } compound
            ? Expression.Assign(slot, Apply(compound, state.Slot(assignment.Name), value))
            : Expression.Assign(slot, value);
    }

    /// <summary>Compiles a shared-memory-buffer access.</summary>
    /// <param name="state">Compile state.</param>
    /// <param name="buffer">Buffer node.</param>
    /// <returns>The buffer call expression.</returns>
    private static Expression CompileMegaBuffer(CompileState state, PresetMegaBufferNode buffer)
    {
        var index = CompileExpression(state, buffer.Index);
        if (buffer.Value is null)
            return Expression.Call(typeof(PresetCompiler), nameof(ReadMegaBuffer), null, index);

        var value = CompileExpression(state, buffer.Value);
        if (buffer.Compound is { } compound)
        {
            var target = Expression.Call(typeof(PresetCompiler), nameof(ReadMegaBuffer), null, index);
            return Expression.Call(
                typeof(PresetCompiler),
                nameof(WriteMegaBufferValue),
                null,
                index,
                Apply(compound, target, value));
        }

        return Expression.Call(typeof(PresetCompiler), nameof(WriteMegaBufferValue), null, index, value);
    }

    /// <summary>Compiles Milkdrop's bounded <c>loop</c> construct.</summary>
    /// <param name="state">Compile state.</param>
    /// <param name="loop">Loop node.</param>
    /// <returns>The loop expression.</returns>
    private static Expression CompileLoop(CompileState state, PresetLoopNode loop)
    {
        var count = CompileExpression(state, loop.Count);
        var body = new List<Expression>(loop.Body.Count);
        foreach (var statement in loop.Body)
            body.Add(CompileStatement(state, statement));

        var index = Expression.Variable(typeof(int), "loopIndex");
        var limit = Expression.Variable(typeof(int), "loopLimit");
        var done = Expression.Label("loopDone");
        // The count is clamped, so a preset that asks for a million iterations cannot stall a frame.
        var clamped = Expression.Condition(
            Expression.GreaterThan(Expression.Convert(count, typeof(int)), Expression.Constant(MaxLoopIterations)),
            Expression.Constant(MaxLoopIterations),
            Expression.Convert(count, typeof(int)));
        body.Add(Expression.IfThen(
            Expression.GreaterThan(
                Expression.PreIncrementAssign(Expression.Field(null, TotalIterationsField)),
                Expression.Constant(MaxTotalLoopIterations)),
            Expression.Throw(
                Expression.New(
                    LoopOverflowConstructor,
                    Expression.Constant("The preset looped too often."),
                    Expression.Constant(loop.Position)))));
        body.Add(Expression.PostIncrementAssign(index));
        return Expression.Block(
            typeof(double),
            [index, limit],
            Expression.Assign(limit, clamped),
            Expression.Assign(index, Expression.Constant(0)),
            Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, limit),
                    Expression.Block(body),
                    Expression.Break(done)),
                done),
            Expression.Constant(0.0));
    }

    /// <summary>Compiles Milkdrop's bounded <c>while</c> construct.</summary>
    /// <param name="state">Compile state.</param>
    /// <param name="loop">While node.</param>
    /// <returns>The loop expression.</returns>
    private static Expression CompileWhile(CompileState state, PresetWhileNode loop)
    {
        var condition = IsTrue(CompileExpression(state, loop.Condition));
        var body = new List<Expression>(loop.Body.Count + 1);
        foreach (var statement in loop.Body)
            body.Add(CompileStatement(state, statement));

        var done = Expression.Label("whileDone");
        // The total-iteration guard keeps a preset whose condition never turns false from stalling a
        // frame; it shares the counter with loop(), so the budget is per frame for both.
        body.Add(Expression.IfThen(
            Expression.GreaterThan(
                Expression.PreIncrementAssign(Expression.Field(null, TotalIterationsField)),
                Expression.Constant(MaxTotalLoopIterations)),
            Expression.Throw(
                Expression.New(
                    LoopOverflowConstructor,
                    Expression.Constant("The preset looped too often."),
                    Expression.Constant(loop.Position)))));
        return Expression.Block(
            typeof(double),
            Expression.Loop(
                Expression.IfThenElse(
                    condition,
                    Expression.Block(body),
                    Expression.Break(done)),
                done),
            Expression.Constant(0.0));
    }

    /// <summary>Compiles one parsed expression into the LINQ tree the interpreter runs.</summary>
    /// <param name="state">Compile state.</param>
    /// <param name="node">Expression node.</param>
    /// <returns>The value expression.</returns>
    private static Expression CompileExpression(CompileState state, PresetSyntaxNode node) => node switch
    {
        PresetLiteralNode literal => Expression.Constant(literal.Value),
        PresetVariableNode variable => state.Slot(variable.Name),
        PresetUnaryNode unary => CompileUnary(state, unary),
        PresetBinaryNode binary => Apply(
            binary.Operator,
            CompileExpression(state, binary.Left),
            CompileExpression(state, binary.Right)),
        PresetConditionalNode conditional => Expression.Condition(
            IsTrue(CompileExpression(state, conditional.Condition)),
            CompileExpression(state, conditional.WhenTrue),
            CompileExpression(state, conditional.WhenFalse)),
        PresetCallNode call => CompileCall(state, call),
        // An assignment or buffer write used as an argument, as in if(condition, x = 1, y = 2).
        PresetAssignmentNode assignment => CompileAssignment(state, assignment),
        PresetMegaBufferNode buffer => CompileMegaBuffer(state, buffer),
        PresetLoopNode loop => CompileLoop(state, loop),
        PresetWhileNode whileLoop => CompileWhile(state, whileLoop),
        PresetSequenceNode sequence => CompileSequence(state, sequence),
        _ => throw new PresetExpressionException("The expression has no interpreter translation.", node.Position)
    };

    /// <summary>Compiles a prefix unary operator.</summary>
    /// <param name="state">Compile state.</param>
    /// <param name="unary">Unary node.</param>
    /// <returns>The value expression.</returns>
    private static Expression CompileUnary(CompileState state, PresetUnaryNode unary) => unary.Operator switch
    {
        PresetTokenKind.Minus => Expression.Negate(CompileExpression(state, unary.Operand)),
        PresetTokenKind.Plus => CompileExpression(state, unary.Operand),
        PresetTokenKind.Not => ToNumber(Expression.Equal(CompileExpression(state, unary.Operand), Expression.Constant(0.0))),
        _ => throw new PresetExpressionException($"Unsupported unary operator '{unary.Operator}'", unary.Position)
    };

    /// <summary>
    /// Compiles a semicolon-separated statement sequence used as one expression value. Every
    /// statement is evaluated in order and the sequence yields the value of the last one.
    /// </summary>
    /// <param name="state">Compile state.</param>
    /// <param name="sequence">Sequence node.</param>
    /// <returns>The value expression.</returns>
    private static Expression CompileSequence(CompileState state, PresetSequenceNode sequence)
    {
        var items = new List<Expression>(sequence.Statements.Count);
        foreach (var statement in sequence.Statements)
            items.Add(CompileExpression(state, statement));
        return items.Count == 1 ? items[0] : Expression.Block(items);
    }

    /// <summary>Compiles a function call.</summary>
    /// <param name="state">Compile state.</param>
    /// <param name="call">Call node.</param>
    /// <returns>The call expression.</returns>
    private static Expression CompileCall(CompileState state, PresetCallNode call)
    {
        var arguments = new List<Expression>(call.Arguments.Count);
        foreach (var argument in call.Arguments)
            arguments.Add(CompileExpression(state, argument));
        return BuildCall(call.Name, arguments, call.Position);
    }

    /// <summary>Builds the call for a known function, rejecting unknown names and arity.</summary>
    private static Expression BuildCall(string name, List<Expression> arguments, int position)
    {
        Expression Unary(Func<Expression, Expression> build, int arity = 1)
        {
            Require(arguments, arity, name, position);
            return build(arguments[0]);
        }

        if (string.Equals(name, "if", StringComparison.OrdinalIgnoreCase))
        {
            Require(arguments, 3, name, position);
            return Expression.Condition(IsTrue(arguments[0]), arguments[1], arguments[2]);
        }

        // Milkdrop preset expressions are case-insensitive, so "Sin" and "sin" are the same call.
        name = name.ToLowerInvariant();
        if (name == "sigmoid")
        {
            // Some presets pass a second argument; only the value matters.
            if (arguments.Count == 0)
                throw new PresetExpressionException("'sigmoid' expects 1 argument", position);
            return Expression.Divide(
                Expression.Constant(1.0),
                Expression.Add(
                    Expression.Constant(1.0),
                    MathCall(nameof(Math.Exp), Expression.Negate(arguments[0]))));
        }

        // Milkdrop's exec2/exec3/exec4 evaluate their arguments in order and yield the last one,
        // which is how presets pack several assignments into one expression.
        if (name is "exec2" or "exec3" or "exec4")
        {
            if (arguments.Count == 0)
                throw new PresetExpressionException($"'{name}' expects arguments", position);
            return arguments.Count == 1 ? arguments[0] : Expression.Block(arguments);
        }

        return name switch
        {
            "sin" => Unary(value => MathCall(nameof(Math.Sin), value)),
            "cos" => Unary(value => MathCall(nameof(Math.Cos), value)),
            "tan" => Unary(value => MathCall(nameof(Math.Tan), value)),
            "asin" => Unary(value => MathCall(nameof(Math.Asin), value)),
            "acos" => Unary(value => MathCall(nameof(Math.Acos), value)),
            "atan" => Unary(value => MathCall(nameof(Math.Atan), value)),
            "sqrt" => Unary(value => MathCall(nameof(Math.Sqrt), value)),
            "abs" => Unary(value => MathCall(nameof(Math.Abs), value)),
            "exp" => Unary(value => MathCall(nameof(Math.Exp), value)),
            "log" => Unary(value => MathCall(nameof(Math.Log), value)),
            "log10" => Unary(value => MathCall(nameof(Math.Log10), value)),
            "floor" => Unary(value => MathCall(nameof(Math.Floor), value)),
            "ceil" => Unary(value => MathCall(nameof(Math.Ceiling), value)),
            "int" => Unary(value => MathCall(nameof(Math.Truncate), value)),
            "sign" => Unary(value => Expression.Call(SignMethod, value)),
            "rand" => Unary(value => Expression.Multiply(Expression.Call(RandomMethod), value), 1),
            "min" => Binary(arguments, name, position, nameof(Math.Min)),
            "max" => Binary(arguments, name, position, nameof(Math.Max)),
            "pow" => Binary(arguments, name, position, nameof(Math.Pow)),
            "atan2" => Binary(arguments, name, position, nameof(Math.Atan2)),
            "fmod" => Binary(arguments, name, position, null),
            "sqr" => Unary(value => Expression.Multiply(value, value)),
            // Milkdrop's shared memory buffers. They are global state, so the accesses are
            // serialised: the parallel warp would otherwise race on the table a preset builds.
            "megabuf" => Unary(value => Expression.Call(typeof(PresetCompiler), nameof(ReadMegaBuffer), null, value)),
            "gmegabuf" => WriteMegaBuffer(arguments, name, position),
            // Milkdrop's comparisons yield one or zero instead of a boolean.
            "above" => Comparison(arguments, name, position, ExpressionType.GreaterThan),
            "below" => Comparison(arguments, name, position, ExpressionType.LessThan),
            "equal" => Comparison(arguments, name, position, ExpressionType.Equal),
            "band" => Bitwise(arguments, name, position, ExpressionType.And),
            "bor" => Bitwise(arguments, name, position, ExpressionType.Or),
            "bnot" => Bitwise(arguments, name, position, ExpressionType.Not),
            _ => throw new PresetExpressionException($"Unknown function '{name}'", position)
        };
    }

    /// <summary>
    /// Builds a comparison that yields one or zero. Milkdrop's <c>above</c>, <c>below</c>, and
    /// <c>equal</c> are used as numbers in its expressions, so they must not produce a boolean.
    /// </summary>
    /// <param name="arguments">Evaluated arguments.</param>
    /// <param name="name">Function name, for the error message.</param>
    /// <param name="position">Source position, for the error message.</param>
    /// <param name="comparison">Comparison to apply.</param>
    /// <returns>The conditional expression.</returns>
    private static Expression Comparison(
        List<Expression> arguments,
        string name,
        int position,
        ExpressionType comparison)
    {
        Require(arguments, 2, name, position);
        return Expression.Condition(
            Expression.MakeBinary(comparison, arguments[0], arguments[1]),
            Expression.Constant(1.0),
            Expression.Constant(0.0));
    }

    /// <summary>Builds a bitwise operation on the integer view of its arguments.</summary>
    /// <param name="arguments">Evaluated arguments.</param>
    /// <param name="name">Function name, for the error message.</param>
    /// <param name="position">Source position, for the error message.</param>
    /// <param name="operation">Bitwise operation to apply.</param>
    /// <returns>The converted result.</returns>
    private static Expression Bitwise(
        List<Expression> arguments,
        string name,
        int position,
        ExpressionType operation)
    {
        var unary = operation == ExpressionType.Not;
        Require(arguments, unary ? 1 : 2, name, position);
        var value = Expression.Convert(arguments[0], typeof(int));
        Expression result = unary
            ? Expression.Not(value)
            : Expression.MakeBinary(operation, value, Expression.Convert(arguments[1], typeof(int)));
        return Expression.Convert(result, typeof(double));
    }

    private static Expression Binary(
        List<Expression> arguments,
        string name,
        int position,
        string? methodName)
    {
        Require(arguments, 2, name, position);
        if (methodName is null)
            return Expression.Call(FmodMethod, arguments[0], arguments[1]);

        var method = typeof(Math).GetMethod(methodName, [typeof(double), typeof(double)])
            ?? throw new PresetExpressionException($"Unknown function '{name}'", position);
        return Expression.Call(method, arguments[0], arguments[1]);
    }

    private static int Require(List<Expression> arguments, int arity, string name, int position)
    {
        if (arguments.Count != arity)
            throw new PresetExpressionException($"'{name}' expects {arity} argument(s)", position);
        return arity;
    }

    private static Expression MathCall(string name, Expression value)
    {
        var method = typeof(Math).GetMethod(name, [typeof(double)])
            ?? throw new PresetExpressionException($"Unknown function '{name}'", 0);
        return Expression.Call(method, value);
    }

    /// <summary>Combines two operands, converting to a boolean where the operator needs one.</summary>
    private static Expression Apply(PresetTokenKind operation, Expression left, Expression right) => operation switch
    {
        PresetTokenKind.Plus => Expression.Add(left, right),
        PresetTokenKind.Minus => Expression.Subtract(left, right),
        PresetTokenKind.Star => Expression.Multiply(left, right),
        PresetTokenKind.Slash => Expression.Divide(left, right),
        PresetTokenKind.Percent => Expression.Call(FmodMethod, left, right),
        PresetTokenKind.And => ToNumber(Expression.AndAlso(IsTrue(left), IsTrue(right))),
        PresetTokenKind.Or => ToNumber(Expression.OrElse(IsTrue(left), IsTrue(right))),
        PresetTokenKind.Equal => Comparison(Expression.Equal, left, right),
        PresetTokenKind.NotEqual => Comparison(Expression.NotEqual, left, right),
        PresetTokenKind.Less => Comparison(Expression.LessThan, left, right),
        PresetTokenKind.LessOrEqual => Comparison(Expression.LessThanOrEqual, left, right),
        PresetTokenKind.Greater => Comparison(Expression.GreaterThan, left, right),
        PresetTokenKind.GreaterOrEqual => Comparison(Expression.GreaterThanOrEqual, left, right),
        _ => throw new PresetExpressionException($"Unsupported operator '{operation}'", 0)
    };

    /// <summary>Maps a compound assignment onto the binary operator it applies.</summary>
    /// <param name="operation">Compound assignment token, for example <c>+=</c>.</param>
    /// <returns>The matching binary operator kind.</returns>
    private static PresetTokenKind CompoundOperator(PresetToken operation) => operation.Text[0] switch
    {
        '+' => PresetTokenKind.Plus,
        '-' => PresetTokenKind.Minus,
        '*' => PresetTokenKind.Star,
        '/' => PresetTokenKind.Slash,
        _ => PresetTokenKind.Percent
    };

    /// <summary>Builds a comparison that yields one or zero.</summary>
    private static Expression Comparison(
        Func<Expression, Expression, BinaryExpression> comparison,
        Expression left,
        Expression right) =>
        Expression.Condition(comparison(left, right), Expression.Constant(1.0), Expression.Constant(0.0));

    /// <summary>Converts a value to the boolean a condition needs, where non-zero is true.</summary>
    private static Expression IsTrue(Expression value) =>
        Expression.NotEqual(value, Expression.Constant(0.0));

    /// <summary>Converts a condition back into the one-or-zero a preset variable holds.</summary>
    private static Expression ToNumber(Expression condition) =>
        Expression.Condition(condition, Expression.Constant(1.0), Expression.Constant(0.0));

    private static int PrecedenceOf(PresetTokenKind kind) => kind switch
    {
        PresetTokenKind.Or => 1,
        PresetTokenKind.And => 2,
        PresetTokenKind.Equal or PresetTokenKind.NotEqual => 3,
        PresetTokenKind.Less or PresetTokenKind.LessOrEqual or PresetTokenKind.Greater or PresetTokenKind.GreaterOrEqual => 4,
        PresetTokenKind.Plus or PresetTokenKind.Minus => 5,
        PresetTokenKind.Star or PresetTokenKind.Slash or PresetTokenKind.Percent => 6,
        _ => -1
    };

    /// <summary>C-like remainder that truncates towards zero.</summary>
    /// <param name="left">Dividend.</param>
    /// <param name="right">Divisor.</param>
    /// <returns>The remainder, or zero when the divisor is zero.</returns>
    private static double Fmod(double left, double right) =>
        right == 0.0 ? 0.0 : left - (right * Math.Truncate(left / right));

    /// <summary>Returns -1 for negative values and 1 otherwise, matching the preset convention.</summary>
    /// <param name="value">Input value.</param>
    /// <returns>The sign as -1 or 1.</returns>
    private static double Sign(double value) => value < 0.0 ? -1.0 : 1.0;

    /// <summary>The shared Milkdrop memory buffer, one entry per slot a preset can address.</summary>
    private static readonly double[] MegaBuffer = new double[1 << 20];

    /// <summary>Guards the shared memory buffer, which presets can read and write.</summary>
    private static readonly object MegaGate = new();

    /// <summary>Reads one entry of the shared Milkdrop memory buffer.</summary>
    /// <param name="index">Entry index.</param>
    /// <returns>The stored value, or zero outside the buffer.</returns>
    private static double ReadMegaBuffer(double index)
    {
        var slot = (int)index;
        if (slot < 0 || slot >= MegaBuffer.Length)
            return 0.0;
        lock (MegaGate)
            return MegaBuffer[slot];
    }

    /// <summary>Writes one entry of the shared Milkdrop memory buffer.</summary>
    /// <param name="index">Entry index.</param>
    /// <param name="value">Value to store.</param>
    /// <returns>The stored value.</returns>
    private static double WriteMegaBufferValue(double index, double value)
    {
        var slot = (int)index;
        if (slot < 0 || slot >= MegaBuffer.Length)
            return value;
        lock (MegaGate)
            MegaBuffer[slot] = value;
        return value;
    }

    /// <summary>Builds a call that writes one entry of the shared memory buffer.</summary>
    /// <param name="arguments">Evaluated arguments.</param>
    /// <param name="name">Function name, for the error message.</param>
    /// <param name="position">Source position, for the error message.</param>
    /// <returns>The call expression.</returns>
    private static Expression WriteMegaBuffer(List<Expression> arguments, string name, int position)
    {
        // Presets write one entry, and a few pass extra arguments that are ignored.
        if (arguments.Count == 0)
            throw new PresetExpressionException("''gmegabuf'' expects 2 arguments", position);
        if (arguments.Count == 1)
            return Expression.Call(typeof(PresetCompiler), nameof(ReadMegaBuffer), null, arguments[0]);

        return Expression.Call(
            typeof(PresetCompiler),
            nameof(WriteMegaBufferValue),
            null,
            arguments[0],
            arguments[1]);
    }

    /// <summary>Upper bound on the iterations of one Milkdrop <c>loop</c> call.</summary>
    private const int MaxLoopIterations = 100000;

    /// <summary>Draws the next pseudo-random value in the range zero to one.</summary>
    /// <returns>A value in the range zero to one.</returns>
    private static double NextRandom() => Random.Shared.NextDouble();

    /// <summary>Collects the slot layout while compiling.</summary>
    private sealed class CompileState
    {
        /// <summary>Creates a compile state, optionally over a shared layout.</summary>
        /// <param name="layout">Shared layout, or <see langword="null"/> for a private one.</param>
        public CompileState(PresetVariableLayout? layout) => Layout = layout ?? new PresetVariableLayout();

        /// <summary>Gets the slot array parameter shared by every compiled statement.</summary>
        public ParameterExpression Slots { get; } = Expression.Parameter(typeof(double[]), "slots");

        /// <summary>Gets the slot layout this program compiles against.</summary>
        public PresetVariableLayout Layout { get; }

        /// <summary>
        /// Gets the variable names the parsed statements mention, whether they read or write them.
        /// This is the conservative set the renderer uses to skip values a preset never looks at.
        /// </summary>
        public IReadOnlyCollection<string> ReferencedNames => _referencedNames;

        /// <summary>
        /// Gets the names this program assigns to. The renderer uses them to decide whether a
        /// per-pixel pass may run in parallel, because a value written by one pixel and read by
        /// another would make the result depend on the split.
        /// </summary>
        public IReadOnlyCollection<string> WrittenNames => _writtenNames;

        /// <summary>Returns the slot expression for a variable, adding the slot on first use.</summary>
        /// <param name="name">Variable name.</param>
        /// <returns>An array access expression for the variable's slot.</returns>
        public Expression Slot(string name)
        {
            _referencedNames.Add(name);
            return Expression.ArrayAccess(Slots, Expression.Constant(Layout.GetOrAdd(name)));
        }

        /// <summary>Returns the slot expression for an assignment target.</summary>
        /// <param name="name">Variable name.</param>
        /// <returns>An array access expression for the variable's slot.</returns>
        public Expression WriteSlot(string name)
        {
            _writtenNames.Add(name);
            return Slot(name);
        }

        private readonly HashSet<string> _referencedNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _writtenNames = new(StringComparer.Ordinal);
    }
}
