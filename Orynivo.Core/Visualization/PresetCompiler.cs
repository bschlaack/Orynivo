using System.Linq.Expressions;
using System.Reflection;

namespace Orynivo.Visualization;

/// <summary>
/// Compiles a preset expression block into a <see cref="PresetProgram"/>. It implements the
/// Milkdrop/AVS expression subset that preset authors actually use: assignments,
/// arithmetic, comparisons, logical operators, the ternary operator, the usual math
/// functions, and the <c>if(condition, then, else)</c> helper. Loops, arrays, and shaders
/// are deliberately out of scope, and unknown functions are reported as errors.
/// </summary>
public static class PresetCompiler
{
    private static readonly MethodInfo FmodMethod =
        typeof(PresetCompiler).GetMethod(nameof(Fmod), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo SignMethod =
        typeof(PresetCompiler).GetMethod(nameof(Sign), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo RandomMethod =
        typeof(PresetCompiler).GetMethod(nameof(NextRandom), BindingFlags.NonPublic | BindingFlags.Static)!;

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
                : new PresetProgram(layout, null, [], []);

        var state = new CompileState(layout);
        var body = new List<Expression>();
        var lexer = new PresetLexer(source);
        var current = lexer.Next();

        while (current.Kind != PresetTokenKind.End)
        {
            if (current.Kind == PresetTokenKind.Semicolon)
            {
                current = lexer.Next();
                continue;
            }

            body.Add(ParseStatement(state, lexer, ref current));
            if (current.Kind is PresetTokenKind.End or PresetTokenKind.Semicolon)
            {
                current = current.Kind == PresetTokenKind.Semicolon ? lexer.Next() : current;
                continue;
            }

            throw new PresetExpressionException($"Unexpected '{current.Text}'", current.Position);
        }

        if (body.Count == 0)
            return PresetProgram.Empty;

        var block = Expression.Block(body);
        var lambda = Expression.Lambda<Action<float[]>>(block, state.Slots);
        return new PresetProgram(state.Layout, lambda.Compile(), state.ReferencedNames, state.WrittenNames);
    }

    /// <summary>Parses one statement, which is either an assignment or a bare expression.</summary>
    private static Expression ParseStatement(CompileState state, PresetLexer lexer, ref PresetToken current)
    {
        if (current.Kind == PresetTokenKind.Identifier)
        {
            var name = current.Text;
            var position = current.Position;
            var next = lexer.Next();
            if (next.Kind == PresetTokenKind.OpenParenthesis &&
                (string.Equals(name, "megabuf", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "gmegabuf", StringComparison.OrdinalIgnoreCase)))
            {
                // Presets write a buffer entry by assigning to the call: gmegabuf(i) = value;
                current = lexer.Next();
                var index = ParseExpression(state, lexer, ref current);
                if (current.Kind == PresetTokenKind.Comma)
                {
                    // gmegabuf(index, value) writes the entry directly.
                    current = lexer.Next();
                    var written = ParseExpression(state, lexer, ref current);
                    if (current.Kind != PresetTokenKind.CloseParenthesis)
                        throw new PresetExpressionException("Expected ')' after the buffer value", position);

                    current = lexer.Next();
                    return Expression.Call(
                        typeof(PresetCompiler),
                        nameof(WriteMegaBufferValue),
                        null,
                        index,
                        written);
                }

                if (current.Kind != PresetTokenKind.CloseParenthesis)
                    throw new PresetExpressionException("Expected ')' after the buffer index", position);

                current = lexer.Next();
                if (current.Kind != PresetTokenKind.Assign)
                    return Expression.Call(typeof(PresetCompiler), nameof(ReadMegaBuffer), null, index);

                current = lexer.Next();
                var stored = ParseExpression(state, lexer, ref current);
                return Expression.Call(
                    typeof(PresetCompiler),
                    nameof(WriteMegaBufferValue),
                    null,
                    index,
                    stored);
            }

            if (next.Kind == PresetTokenKind.OpenParenthesis &&
                string.Equals(name, "loop", StringComparison.OrdinalIgnoreCase))
            {
                // Milkdrop's loop(count, statements) repeats a statement list. It is a statement,
                // not an expression, so it is handled before the expression parser sees the call.
                current = lexer.Next();
                return ParseLoop(state, lexer, ref current, position);
            }

            if (next.Kind == PresetTokenKind.Assign)
            {
                current = lexer.Next();
                var value = ParseExpression(state, lexer, ref current);
                return Expression.Assign(state.WriteSlot(name), value);
            }

            // Not an assignment: rewind by re-parsing the identifier as a primary expression.
            current = next;
            return ParseExpression(state, lexer, ref current, name, position);
        }

        return ParseExpression(state, lexer, ref current);
    }

    private static Expression ParseExpression(CompileState state, PresetLexer lexer, ref PresetToken current) =>
        ParseExpression(state, lexer, ref current, null, 0);

    private static Expression ParseExpression(
        CompileState state,
        PresetLexer lexer,
        ref PresetToken current,
        string? leadingIdentifier,
        int leadingPosition)
    {
        var condition = ParseBinary(state, lexer, ref current, leadingIdentifier, leadingPosition, 0);
        if (current.Kind != PresetTokenKind.Question)
            return condition;

        current = lexer.Next();
        var whenTrue = ParseExpression(state, lexer, ref current);
        if (current.Kind != PresetTokenKind.Colon)
            throw new PresetExpressionException("Expected ':' in a ternary expression", current.Position);

        current = lexer.Next();
        var whenFalse = ParseExpression(state, lexer, ref current);
        return Expression.Condition(IsTrue(condition), whenTrue, whenFalse);
    }

    /// <summary>Precedence climbing over the binary operators.</summary>
    private static Expression ParseBinary(
        CompileState state,
        PresetLexer lexer,
        ref PresetToken current,
        string? leadingIdentifier,
        int leadingPosition,
        int minimumPrecedence)
    {
        var left = ParseUnary(state, lexer, ref current, leadingIdentifier, leadingPosition);
        while (true)
        {
            var precedence = PrecedenceOf(current.Kind);
            if (precedence < 0 || precedence < minimumPrecedence)
                return left;

            var operation = current;
            current = lexer.Next();
            var right = ParseBinary(state, lexer, ref current, null, 0, precedence + 1);
            left = Apply(operation, left, right);
        }
    }

    private static Expression ParseUnary(
        CompileState state,
        PresetLexer lexer,
        ref PresetToken current,
        string? leadingIdentifier,
        int leadingPosition)
    {
        if (leadingIdentifier is not null)
            return ParsePrimaryFromIdentifier(state, lexer, ref current, leadingIdentifier, leadingPosition);

        switch (current.Kind)
        {
            case PresetTokenKind.Minus:
                {
                    current = lexer.Next();
                    return Expression.Negate(ParseUnary(state, lexer, ref current, null, 0));
                }
            case PresetTokenKind.Plus:
                {
                    current = lexer.Next();
                    return ParseUnary(state, lexer, ref current, null, 0);
                }
            case PresetTokenKind.Not:
                {
                    // "!x" is true when x is zero, matching the preset convention.
                    current = lexer.Next();
                    var operand = ParseUnary(state, lexer, ref current, null, 0);
                    return ToNumber(Expression.Equal(operand, Expression.Constant(0f)));
                }
            default:
                return ParsePrimary(state, lexer, ref current);
        }
    }

    private static Expression ParsePrimary(CompileState state, PresetLexer lexer, ref PresetToken current)
    {
        switch (current.Kind)
        {
            case PresetTokenKind.Number:
                {
                    var value = Expression.Constant(current.Value);
                    current = lexer.Next();
                    return value;
                }
            case PresetTokenKind.Identifier:
                {
                    var name = current.Text;
                    var position = current.Position;
                    current = lexer.Next();
                    return ParsePrimaryFromIdentifier(state, lexer, ref current, name, position);
                }
            case PresetTokenKind.OpenParenthesis:
                {
                    current = lexer.Next();
                    var inner = ParseExpression(state, lexer, ref current);
                    if (current.Kind != PresetTokenKind.CloseParenthesis)
                        throw new PresetExpressionException("Expected ')'", current.Position);
                    current = lexer.Next();
                    return inner;
                }
            default:
                throw new PresetExpressionException($"Unexpected '{current.Text}'", current.Position);
        }
    }

    private static Expression ParsePrimaryFromIdentifier(
        CompileState state,
        PresetLexer lexer,
        ref PresetToken current,
        string name,
        int position)
    {
        if (current.Kind != PresetTokenKind.OpenParenthesis)
        {
            if (string.Equals(name, "pi", StringComparison.OrdinalIgnoreCase))
                return Expression.Constant(MathF.PI);
            return state.Slot(name);
        }

        current = lexer.Next();
        var arguments = new List<Expression>();
        if (current.Kind != PresetTokenKind.CloseParenthesis)
        {
            while (true)
            {
                arguments.Add(ParseExpression(state, lexer, ref current));
                if (current.Kind != PresetTokenKind.Comma)
                    break;
                current = lexer.Next();
            }
        }

        if (current.Kind != PresetTokenKind.CloseParenthesis)
            throw new PresetExpressionException("Expected ')'", current.Position);
        current = lexer.Next();
        return BuildCall(name, arguments, position);
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
                Expression.Constant(1f),
                Expression.Add(
                    Expression.Constant(1f),
                    MathCall(nameof(MathF.Exp), Expression.Negate(arguments[0]))));
        }

        return name switch
        {
            "sin" => Unary(value => MathCall(nameof(MathF.Sin), value)),
            "cos" => Unary(value => MathCall(nameof(MathF.Cos), value)),
            "tan" => Unary(value => MathCall(nameof(MathF.Tan), value)),
            "asin" => Unary(value => MathCall(nameof(MathF.Asin), value)),
            "acos" => Unary(value => MathCall(nameof(MathF.Acos), value)),
            "atan" => Unary(value => MathCall(nameof(MathF.Atan), value)),
            "sqrt" => Unary(value => MathCall(nameof(MathF.Sqrt), value)),
            "abs" => Unary(value => MathCall(nameof(MathF.Abs), value)),
            "exp" => Unary(value => MathCall(nameof(MathF.Exp), value)),
            "log" => Unary(value => MathCall(nameof(MathF.Log), value)),
            "log10" => Unary(value => MathCall(nameof(MathF.Log10), value)),
            "floor" => Unary(value => MathCall(nameof(MathF.Floor), value)),
            "ceil" => Unary(value => MathCall(nameof(MathF.Ceiling), value)),
            "int" => Unary(value => MathCall(nameof(MathF.Truncate), value)),
            "sign" => Unary(value => Expression.Call(SignMethod, value)),
            "rand" => Unary(value => Expression.Multiply(Expression.Call(RandomMethod), value), 1),
            "min" => Binary(arguments, name, position, nameof(MathF.Min)),
            "max" => Binary(arguments, name, position, nameof(MathF.Max)),
            "pow" => Binary(arguments, name, position, nameof(MathF.Pow)),
            "atan2" => Binary(arguments, name, position, nameof(MathF.Atan2)),
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
            Expression.Constant(1f),
            Expression.Constant(0f));
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
        return Expression.Convert(result, typeof(float));
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

        var method = typeof(MathF).GetMethod(methodName, [typeof(float), typeof(float)])
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
        var method = typeof(MathF).GetMethod(name, [typeof(float)])
            ?? throw new PresetExpressionException($"Unknown function '{name}'", 0);
        return Expression.Call(method, value);
    }

    /// <summary>Combines two operands, converting to a boolean where the operator needs one.</summary>
    private static Expression Apply(PresetToken operation, Expression left, Expression right) => operation.Kind switch
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
        _ => throw new PresetExpressionException($"Unsupported operator '{operation.Text}'", operation.Position)
    };

    /// <summary>Builds a comparison that yields one or zero.</summary>
    private static Expression Comparison(
        Func<Expression, Expression, BinaryExpression> comparison,
        Expression left,
        Expression right) =>
        Expression.Condition(comparison(left, right), Expression.Constant(1f), Expression.Constant(0f));

    /// <summary>Converts a value to the boolean a condition needs, where non-zero is true.</summary>
    private static Expression IsTrue(Expression value) =>
        Expression.NotEqual(value, Expression.Constant(0f));

    /// <summary>Converts a condition back into the one-or-zero a preset variable holds.</summary>
    private static Expression ToNumber(Expression condition) =>
        Expression.Condition(condition, Expression.Constant(1f), Expression.Constant(0f));

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
    private static float Fmod(float left, float right) =>
        right == 0f ? 0f : left - (right * MathF.Truncate(left / right));

    /// <summary>Returns -1 for negative values and 1 otherwise, matching the preset convention.</summary>
    /// <param name="value">Input value.</param>
    /// <returns>The sign as -1 or 1.</returns>
    private static float Sign(float value) => value < 0f ? -1f : 1f;

    /// <summary>The shared Milkdrop memory buffer, one entry per slot a preset can address.</summary>
    private static readonly float[] MegaBuffer = new float[1 << 20];

    /// <summary>Guards the shared memory buffer, which presets can read and write.</summary>
    private static readonly object MegaGate = new();

    /// <summary>Reads one entry of the shared Milkdrop memory buffer.</summary>
    /// <param name="index">Entry index.</param>
    /// <returns>The stored value, or zero outside the buffer.</returns>
    private static float ReadMegaBuffer(float index)
    {
        var slot = (int)index;
        if (slot < 0 || slot >= MegaBuffer.Length)
            return 0f;
        lock (MegaGate)
            return MegaBuffer[slot];
    }

    /// <summary>Writes one entry of the shared Milkdrop memory buffer.</summary>
    /// <param name="index">Entry index.</param>
    /// <param name="value">Value to store.</param>
    /// <returns>The stored value.</returns>
    private static float WriteMegaBufferValue(float index, float value)
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

    /// <summary>Parses Milkdrop's <c>loop(count, statements)</c> construct.</summary>
    /// <param name="state">Compile state.</param>
    /// <param name="lexer">Token source.</param>
    /// <param name="current">Token after the opening parenthesis.</param>
    /// <param name="position">Source position of the <c>loop</c> name.</param>
    /// <returns>The expression that runs the loop.</returns>
    private static Expression ParseLoop(
        CompileState state,
        PresetLexer lexer,
        ref PresetToken current,
        int position)
    {
        var count = ParseExpression(state, lexer, ref current);
        if (current.Kind != PresetTokenKind.Comma)
            throw new PresetExpressionException("Expected ',' after the loop count", position);

        current = lexer.Next();
        var body = new List<Expression>();
        while (current.Kind != PresetTokenKind.End && current.Kind != PresetTokenKind.CloseParenthesis)
        {
            // The statements of a loop body are separated by semicolons that belong to them.
            while (current.Kind == PresetTokenKind.Semicolon)
                current = lexer.Next();
            if (current.Kind == PresetTokenKind.CloseParenthesis)
                break;
            body.Add(ParseStatement(state, lexer, ref current));
        }

        if (current.Kind != PresetTokenKind.CloseParenthesis)
            throw new PresetExpressionException("Expected ')' to close loop(...)", current.Position);

        current = lexer.Next();
        var index = Expression.Variable(typeof(int), "loopIndex");
        var limit = Expression.Variable(typeof(int), "loopLimit");
        var done = Expression.Label("loopDone");
        // The count is clamped, so a preset that asks for a million iterations cannot stall a frame.
        var clamped = Expression.Condition(
            Expression.GreaterThan(Expression.Convert(count, typeof(int)), Expression.Constant(MaxLoopIterations)),
            Expression.Constant(MaxLoopIterations),
            Expression.Convert(count, typeof(int)));
        body.Add(Expression.PostIncrementAssign(index));
        return Expression.Block(
            typeof(float),
            [index, limit],
            Expression.Assign(limit, clamped),
            Expression.Assign(index, Expression.Constant(0)),
            Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, limit),
                    Expression.Block(body),
                    Expression.Break(done)),
                done),
            Expression.Constant(0f));
    }

    /// <summary>Draws the next pseudo-random value in the range zero to one.</summary>
    /// <returns>A value in the range zero to one.</returns>
    private static float NextRandom() => Random.Shared.NextSingle();

    /// <summary>Collects the slot layout while parsing.</summary>
    private sealed class CompileState
    {
        /// <summary>Creates a compile state, optionally over a shared layout.</summary>
        /// <param name="layout">Shared layout, or <see langword="null"/> for a private one.</param>
        public CompileState(PresetVariableLayout? layout) => Layout = layout ?? new PresetVariableLayout();

        /// <summary>Gets the slot array parameter shared by every compiled statement.</summary>
        public ParameterExpression Slots { get; } = Expression.Parameter(typeof(float[]), "slots");

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
