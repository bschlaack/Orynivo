using System.Linq.Expressions;

namespace Orynivo.Visualization;

/// <summary>
/// A shader compiled to a delegate that runs over a slot array. The interpreter remains the
/// reference implementation and the fallback; this type exists because walking the tree per pixel
/// costs roughly 660 ns, which keeps every shader outside the frame budget. A compiled shader
/// reads and writes its variables through slots instead of a name dictionary and calls the same
/// <see cref="ShaderRuntime"/> operations the interpreter uses, so both produce identical values.
/// </summary>
public sealed class ShaderProgram
{
    private readonly Func<ShaderValue[], IShaderSampler?, ShaderValue> _execute;
    private readonly Dictionary<string, int> _slots;

    /// <summary>Creates a compiled shader.</summary>
    /// <param name="execute">Compiled body.</param>
    /// <param name="slots">Slot layout of the compiled variables.</param>
    internal ShaderProgram(
        Func<ShaderValue[], IShaderSampler?, ShaderValue> execute,
        Dictionary<string, int> slots)
    {
        _execute = execute;
        _slots = slots;
    }

    /// <summary>Gets the number of slots the shader uses.</summary>
    public int SlotCount => _slots.Count;

    /// <summary>Returns the slot of a variable.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The slot index, or <c>-1</c> when the shader never uses the variable.</returns>
    public int IndexOf(string name) => _slots.TryGetValue(name, out var slot) ? slot : -1;

    /// <summary>Runs the shader.</summary>
    /// <param name="slots">Slot storage of at least <see cref="SlotCount"/> entries.</param>
    /// <param name="sampler">Bound sampler, or <see langword="null"/>.</param>
    /// <returns>The value the shader returned.</returns>
    public ShaderValue Execute(ShaderValue[] slots, IShaderSampler? sampler) => _execute(slots, sampler);
}

/// <summary>
/// Compiles the straight-line shaders that per-pixel shader bodies almost always are: local
/// declarations followed by a single return. Anything else — branches, loops, early returns, or an
/// operator the compiler does not model — is reported as unsupported and stays on the interpreter,
/// because a wrong picture is worse than a slow one.
/// </summary>
internal static class ShaderCompiler
{
    private static readonly Func<float, float> Negate = value => -value;

    /// <summary>The constructor of <see cref="ShaderValue"/> used to build results inline.</summary>
    private static readonly System.Reflection.ConstructorInfo ShaderValueConstructor =
        typeof(ShaderValue).GetConstructor([typeof(float), typeof(float), typeof(float), typeof(float), typeof(int)])!;

    /// <summary>Compiles a shader body, or reports that it is not supported.</summary>
    /// <param name="program">Parsed shader body.</param>
    /// <returns>The compiled shader, or <see langword="null"/> when it must stay interpreted.</returns>
    public static ShaderProgram? Compile(ShaderNode program)
    {
        var statements = new List<ShaderNode>();
        foreach (var statement in program.Items)
        {
            if (statement.Kind == ShaderNodeKind.Block)
                statements.AddRange(statement.Items);
            else
                statements.Add(statement);
        }

        var setup = new List<ShaderNode>();
        ShaderNode? returned = null;
        foreach (var statement in statements)
        {
            switch (statement.Kind)
            {
                case ShaderNodeKind.Declaration:
                case ShaderNodeKind.ExpressionStatement:
                    if (returned is not null)
                        return null;
                    setup.Add(statement);
                    break;
                case ShaderNodeKind.Return:
                    if (returned is not null || statement.Left is null)
                        return null;
                    returned = statement;
                    break;
                default:
                    return null;
            }
        }

        if (returned is null)
            return null;

        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        var slotsParameter = Expression.Parameter(typeof(ShaderValue[]), "slots");
        var samplerParameter = Expression.Parameter(typeof(IShaderSampler), "sampler");
        var result = Expression.Variable(typeof(ShaderValue), "result");
        var body = new List<Expression>();
        try
        {
            foreach (var statement in setup)
                body.Add(BuildStatement(slots, slotsParameter, samplerParameter, statement));
            body.Add(Expression.Assign(result, BuildExpression(slots, slotsParameter, samplerParameter, returned.Left!)));
        }
        catch (PresetExpressionException)
        {
            return null;
        }

        body.Add(result);
        var lambda = Expression.Lambda<Func<ShaderValue[], IShaderSampler?, ShaderValue>>(
            Expression.Block([result], body),
            slotsParameter,
            samplerParameter);
        return new ShaderProgram(lambda.Compile(), slots);
    }

    /// <summary>Builds one setup statement.</summary>
    /// <param name="slots">Slot layout being filled.</param>
    /// <param name="slotsParameter">Slot array parameter.</param>
    /// <param name="samplerParameter">Sampler parameter.</param>
    /// <param name="statement">Statement to build.</param>
    /// <returns>The statement expression.</returns>
    private static Expression BuildStatement(
        Dictionary<string, int> slots,
        ParameterExpression slotsParameter,
        ParameterExpression samplerParameter,
        ShaderNode statement)
    {
        if (statement.Kind == ShaderNodeKind.ExpressionStatement)
            return BuildExpression(slots, slotsParameter, samplerParameter, statement.Left!);

        var name = statement.Items.Count > 0 ? statement.Items[0].Text : string.Empty;
        var slot = Slot(slots, name);
        var value = statement.Left is null
            ? Expression.Constant(ShaderRuntime.DefaultFor(statement.Text))
            : BuildExpression(slots, slotsParameter, samplerParameter, statement.Left);
        return Expression.Assign(Expression.ArrayAccess(slotsParameter, Expression.Constant(slot)), value);
    }

    /// <summary>Builds one expression.</summary>
    /// <param name="slots">Slot layout being filled.</param>
    /// <param name="slotsParameter">Slot array parameter.</param>
    /// <param name="samplerParameter">Sampler parameter.</param>
    /// <param name="node">Expression to build.</param>
    /// <returns>The expression.</returns>
    private static Expression BuildExpression(
        Dictionary<string, int> slots,
        ParameterExpression slotsParameter,
        ParameterExpression samplerParameter,
        ShaderNode node)
    {
        switch (node.Kind)
        {
            case ShaderNodeKind.Literal:
                return Expression.Constant(ShaderValue.Scalar(node.Number));
            case ShaderNodeKind.Identifier:
                return Expression.ArrayAccess(
                    slotsParameter,
                    Expression.Constant(Slot(slots, node.Text)));
            case ShaderNodeKind.Member:
                return BuildSwizzle(slots, slotsParameter, samplerParameter, node);
            case ShaderNodeKind.Call:
                return BuildCall(slots, slotsParameter, samplerParameter, node);
            case ShaderNodeKind.Unary:
                return BuildUnary(slots, slotsParameter, samplerParameter, node);
            case ShaderNodeKind.Binary:
                return BuildBinary(slots, slotsParameter, samplerParameter, node);
            case ShaderNodeKind.Ternary:
                return Expression.Condition(
                    Expression.Property(
                        BuildExpression(slots, slotsParameter, samplerParameter, node.Left!),
                        nameof(ShaderValue.IsTrue)),
                    BuildExpression(slots, slotsParameter, samplerParameter, node.Right!),
                    BuildExpression(slots, slotsParameter, samplerParameter, node.Third!));
            default:
                throw new PresetExpressionException("Unsupported shader expression.", node.Position);
        }
    }

    /// <summary>Builds a call, which is an intrinsic, a constructor, or a texture sample.</summary>
    /// <param name="slots">Slot layout being filled.</param>
    /// <param name="slotsParameter">Slot array parameter.</param>
    /// <param name="samplerParameter">Sampler parameter.</param>
    /// <param name="node">Call to build.</param>
    /// <returns>The call expression.</returns>
    private static Expression BuildCall(
        Dictionary<string, int> slots,
        ParameterExpression slotsParameter,
        ParameterExpression samplerParameter,
        ShaderNode node)
    {
        var count = node.Items.Count;
        if (count > 4)
            throw new PresetExpressionException("Unsupported shader call.", node.Position);

        var arguments = new Expression[4];
        for (var index = 0; index < 4; index++)
        {
            arguments[index] = index < count
                ? BuildExpression(slots, slotsParameter, samplerParameter, node.Items[index])
                : Expression.Constant(default(ShaderValue));
        }

        var samplerName = count > 0 && node.Items[0].Kind == ShaderNodeKind.Identifier
            ? node.Items[0].Text
            : string.Empty;
        return Expression.Call(
            typeof(ShaderRuntime),
            nameof(ShaderRuntime.Call),
            null,
            Expression.Constant(node.Text),
            Expression.Constant(samplerName),
            Expression.Constant(node.Position),
            Expression.Convert(samplerParameter, typeof(IShaderSampler)),
            arguments[0],
            arguments[1],
            arguments[2],
            arguments[3],
            Expression.Constant(count));
    }

    /// <summary>Builds a prefix unary operator.</summary>
    /// <param name="slots">Slot layout being filled.</param>
    /// <param name="slotsParameter">Slot array parameter.</param>
    /// <param name="samplerParameter">Sampler parameter.</param>
    /// <param name="node">Unary node.</param>
    /// <returns>The unary expression.</returns>
    private static Expression BuildUnary(
        Dictionary<string, int> slots,
        ParameterExpression slotsParameter,
        ParameterExpression samplerParameter,
        ShaderNode node)
    {
        var operand = BuildExpression(slots, slotsParameter, samplerParameter, node.Left!);
        return node.Text switch
        {
            "+" => operand,
            "-" => Expression.Call(
                typeof(ShaderRuntime),
                nameof(ShaderRuntime.Map),
                null,
                operand,
                Expression.Constant(Negate)),
            "!" => Expression.Call(typeof(ShaderRuntime), nameof(ShaderRuntime.Not), null, operand),
            _ => throw new PresetExpressionException("Unsupported shader operator.", node.Position)
        };
    }

    /// <summary>Builds a binary operator, rejecting the assignment forms the compiler does not model.</summary>
    /// <param name="slots">Slot layout being filled.</param>
    /// <param name="slotsParameter">Slot array parameter.</param>
    /// <param name="samplerParameter">Sampler parameter.</param>
    /// <param name="node">Binary node.</param>
    /// <returns>The binary expression.</returns>
    private static Expression BuildBinary(
        Dictionary<string, int> slots,
        ParameterExpression slotsParameter,
        ParameterExpression samplerParameter,
        ShaderNode node)
    {
        var left = BuildExpression(slots, slotsParameter, samplerParameter, node.Left!);
        var right = BuildExpression(slots, slotsParameter, samplerParameter, node.Right!);
        switch (node.Text)
        {
            case "+":
                return BinaryCall(nameof(ShaderRuntime.Add), left, right);
            case "-":
                return BinaryCall(nameof(ShaderRuntime.Subtract), left, right);
            case "*":
                return BinaryCall(nameof(ShaderRuntime.Multiply), left, right);
            case "/":
                return BinaryCall(nameof(ShaderRuntime.Divide), left, right);
            case "%":
                return BinaryCall(nameof(ShaderRuntime.Modulo), left, right);
            case "==":
                return Compare(left, right, 0);
            case "!=":
                return Compare(left, right, 1);
            case "<":
                return Compare(left, right, 2);
            case ">":
                return Compare(left, right, 3);
            case "<=":
                return Compare(left, right, 4);
            case ">=":
                return Compare(left, right, 5);
            case "=":
                // Assigning to a local is common in shader bodies, so it is compiled; assigning to
                // anything else (a swizzle target) stays on the interpreter.
                if (node.Left!.Kind == ShaderNodeKind.Identifier)
                {
                    return Expression.Assign(
                        Expression.ArrayAccess(
                            slotsParameter,
                            Expression.Constant(Slot(slots, node.Left.Text))),
                        right);
                }

                throw new PresetExpressionException("Unsupported shader assignment.", node.Position);
            case "&&":
                return Expression.Condition(
                    Expression.Property(left, nameof(ShaderValue.IsTrue)),
                    Expression.Condition(
                        Expression.Property(right, nameof(ShaderValue.IsTrue)),
                        Expression.Constant(ShaderValue.Scalar(1f)),
                        Expression.Constant(ShaderValue.Scalar(0f))),
                    Expression.Constant(ShaderValue.Scalar(0f)));
            case "||":
                return Expression.Condition(
                    Expression.Property(left, nameof(ShaderValue.IsTrue)),
                    Expression.Constant(ShaderValue.Scalar(1f)),
                    Expression.Condition(
                        Expression.Property(right, nameof(ShaderValue.IsTrue)),
                        Expression.Constant(ShaderValue.Scalar(1f)),
                        Expression.Constant(ShaderValue.Scalar(0f))));
            default:
                throw new PresetExpressionException("Unsupported shader operator.", node.Position);
        }
    }

    /// <summary>Builds a call to one of the component-wise runtime operations.</summary>
    /// <param name="method">Runtime method name.</param>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>The call expression.</returns>
    private static Expression BinaryCall(string method, Expression left, Expression right) =>
        Expression.Call(typeof(ShaderRuntime), method, null, left, right);

    /// <summary>
    /// Builds a swizzle whose components are selected at compile time. The source is bound to a
    /// local first, so an expression like a texture call is evaluated once instead of once per
    /// selected component.
    /// </summary>
    /// <param name="slots">Slot layout being filled.</param>
    /// <param name="slotsParameter">Slot array parameter.</param>
    /// <param name="samplerParameter">Sampler parameter.</param>
    /// <param name="node">Member access to build.</param>
    /// <returns>The swizzle expression.</returns>
    private static Expression BuildSwizzle(
        Dictionary<string, int> slots,
        ParameterExpression slotsParameter,
        ParameterExpression samplerParameter,
        ShaderNode node)
    {
        Span<int> indices = stackalloc int[4];
        var count = ShaderRuntime.ComponentIndices(node.Text, node.Position, indices);
        var source = Expression.Variable(typeof(ShaderValue), "swizzleSource");
        var get = typeof(ShaderValue).GetMethod(nameof(ShaderValue.Get))!;
        var components = new Expression[4];
        for (var index = 0; index < 4; index++)
        {
            components[index] = Expression.Call(
                source,
                get,
                Expression.Constant(index < count ? indices[index] : 0));
        }

        return Expression.Block(
            [source],
            Expression.Assign(
                source,
                BuildExpression(slots, slotsParameter, samplerParameter, node.Left!)),
            Expression.New(
                ShaderValueConstructor,
                components[0],
                components[1],
                components[2],
                components[3],
                Expression.Constant(count)));
    }

    /// <summary>Builds a component-wise comparison.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <param name="comparison">Comparison selector.</param>
    /// <returns>The call expression.</returns>
    private static Expression Compare(Expression left, Expression right, int comparison) =>
        Expression.Call(
            typeof(ShaderRuntime),
            nameof(ShaderRuntime.Compare),
            null,
            left,
            right,
            Expression.Constant(comparison));

    /// <summary>Returns the slot of a variable, adding one on first use.</summary>
    /// <param name="slots">Slot layout being filled.</param>
    /// <param name="name">Variable name.</param>
    /// <returns>The slot index.</returns>
    private static int Slot(Dictionary<string, int> slots, string name)
    {
        if (slots.TryGetValue(name, out var existing))
            return existing;

        var slot = slots.Count;
        slots[name] = slot;
        return slot;
    }
}
