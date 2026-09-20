namespace Orynivo.Visualization;

/// <summary>
/// Supplies the textures a shader samples through <c>tex2D</c>. The renderer implements this in
/// the binding phase; the interpreter only knows the contract, so a shader can be evaluated
/// without any render state.
/// </summary>
public interface IShaderSampler
{
    /// <summary>Samples one of the shader's samplers.</summary>
    /// <param name="sampler">Sampler name, for example <c>sampler_main</c>.</param>
    /// <param name="u">Horizontal coordinate.</param>
    /// <param name="v">Vertical coordinate.</param>
    /// <returns>The sampled colour.</returns>
    ShaderValue Sample(string sampler, float u, float v);
}

/// <summary>
/// Evaluates a parsed <c>ps_2_0</c> shader. It walks the <see cref="ShaderNode"/> tree the parser
/// produced, keeps its variables in a plain dictionary the caller can seed and read back, and
/// supports the scalar and <c>float2</c>/<c>float3</c>/<c>float4</c> arithmetic, swizzles,
/// constructors, assignments, the ternary operator, <c>if</c>/<c>else</c>, <c>for</c>, and the
/// usual intrinsics. Sampling goes through <see cref="IShaderSampler"/>, so the interpreter itself
/// stays free of render state and can be tested on its own.
/// </summary>
public sealed class ShaderInterpreter
{
    /// <summary>Upper bound on loop iterations, so a runaway shader cannot hang a frame.</summary>
    public const int MaxLoopIterations = 4096;

    /// <summary>Maximum call depth, so recursion cannot exhaust the stack.</summary>
    private const int MaxDepth = 32;

    private readonly ShaderNode _program;
    private readonly IShaderSampler? _sampler;
    private readonly Dictionary<string, ShaderValue> _variables = new(StringComparer.Ordinal);
    private int _iterations;

    /// <summary>Creates an interpreter for a parsed shader.</summary>
    /// <param name="program">Root node returned by <see cref="ShaderParser.Parse"/>.</param>
    /// <param name="sampler">Sampler used by <c>tex2D</c>, or <see langword="null"/>.</param>
    public ShaderInterpreter(ShaderNode program, IShaderSampler? sampler = null)
    {
        _program = program ?? throw new ArgumentNullException(nameof(program));
        _sampler = sampler;
    }

    /// <summary>Gets the shader variables, which the caller seeds before and reads after a run.</summary>
    public IReadOnlyDictionary<string, ShaderValue> Variables => _variables;

    /// <summary>Sets a variable before the shader runs.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="value">Value to assign.</param>
    public void SetVariable(string name, ShaderValue value) => _variables[name] = value;

    /// <summary>Sets a scalar variable before the shader runs.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="value">Scalar value.</param>
    public void SetVariable(string name, float value) => _variables[name] = ShaderValue.Scalar(value);

    /// <summary>Runs the shader.</summary>
    /// <returns>The value the shader returned, or zero when it returned nothing.</returns>
    /// <exception cref="PresetExpressionException">The shader is invalid or ran away.</exception>
    public ShaderValue Run()
    {
        _iterations = 0;
        var result = ExecuteBlock(_program.Items, 0);
        return result ?? ShaderValue.Scalar(0f);
    }

    /// <summary>Executes a statement list.</summary>
    /// <param name="statements">Statements to run.</param>
    /// <param name="depth">Current call depth.</param>
    /// <returns>The returned value, or <see langword="null"/> when the block fell through.</returns>
    private ShaderValue? ExecuteBlock(IReadOnlyList<ShaderNode> statements, int depth)
    {
        if (depth > MaxDepth)
            throw new PresetExpressionException("The shader nested too deeply.", 0);

        foreach (var statement in statements)
        {
            var result = ExecuteStatement(statement, depth);
            if (result is not null)
                return result;
        }

        return null;
    }

    /// <summary>Executes one statement.</summary>
    /// <param name="statement">Statement to run.</param>
    /// <param name="depth">Current call depth.</param>
    /// <returns>The returned value, or <see langword="null"/> when execution continues.</returns>
    private ShaderValue? ExecuteStatement(ShaderNode statement, int depth)
    {
        switch (statement.Kind)
        {
            case ShaderNodeKind.Block:
                return ExecuteBlock(statement.Items, depth);
            case ShaderNodeKind.Declaration:
                var name = statement.Items.Count > 0 ? statement.Items[0].Text : string.Empty;
                if (name.Length > 0)
                    _variables[name] = statement.Left is null ? DefaultFor(statement.Text) : Evaluate(statement.Left, depth);
                return null;
            case ShaderNodeKind.ExpressionStatement:
                if (statement.Left is not null)
                    Evaluate(statement.Left, depth);
                return null;
            case ShaderNodeKind.If:
                var condition = statement.Left is null || Evaluate(statement.Left, depth).IsTrue;
                if (condition)
                    return statement.Right is null ? null : ExecuteStatement(statement.Right, depth);
                return statement.Third is null ? null : ExecuteStatement(statement.Third, depth);
            case ShaderNodeKind.For:
                return ExecuteFor(statement, depth);
            case ShaderNodeKind.Return:
                return statement.Left is null ? ShaderValue.Scalar(0f) : Evaluate(statement.Left, depth);
            default:
                return null;
        }
    }

    /// <summary>Executes a <c>for</c> loop with a bounded iteration count.</summary>
    /// <param name="loop">Loop node.</param>
    /// <param name="depth">Current call depth.</param>
    /// <returns>The returned value, or <see langword="null"/> when the loop finished.</returns>
    private ShaderValue? ExecuteFor(ShaderNode loop, int depth)
    {
        if (loop.Left is not null)
            ExecuteStatement(loop.Left, depth);

        while (loop.Right is null || Evaluate(loop.Right, depth).IsTrue)
        {
            if (++_iterations > MaxLoopIterations)
                throw new PresetExpressionException("The shader looped too often.", loop.Position);

            foreach (var body in loop.Items)
            {
                var result = ExecuteStatement(body, depth);
                if (result is not null)
                    return result;
            }

            if (loop.Third is not null)
                Evaluate(loop.Third, depth);
        }

        return null;
    }

    /// <summary>Evaluates an expression.</summary>
    /// <param name="expression">Expression node.</param>
    /// <param name="depth">Current call depth.</param>
    /// <returns>The resulting value.</returns>
    private ShaderValue Evaluate(ShaderNode expression, int depth)
    {
        switch (expression.Kind)
        {
            case ShaderNodeKind.Literal:
                return ShaderValue.Scalar(expression.Number);
            case ShaderNodeKind.Identifier:
                return _variables.TryGetValue(expression.Text, out var variable) ? variable : ShaderValue.Scalar(0f);
            case ShaderNodeKind.Member:
                return Swizzle(Evaluate(expression.Left!, depth), expression.Text, expression.Position);
            case ShaderNodeKind.Call:
                return EvaluateCall(expression, depth);
            case ShaderNodeKind.Unary:
                return EvaluateUnary(expression, depth);
            case ShaderNodeKind.Binary:
                return EvaluateBinary(expression, depth);
            case ShaderNodeKind.Ternary:
                return Evaluate(expression.Left!, depth).IsTrue
                    ? Evaluate(expression.Right!, depth)
                    : Evaluate(expression.Third!, depth);
            default:
                return ShaderValue.Scalar(0f);
        }
    }

    /// <summary>Evaluates a prefix or postfix unary operator.</summary>
    /// <param name="expression">Unary node.</param>
    /// <param name="depth">Current call depth.</param>
    /// <returns>The resulting value.</returns>
    private ShaderValue EvaluateUnary(ShaderNode expression, int depth)
    {
        var operand = Evaluate(expression.Left!, depth);
        switch (expression.Text)
        {
            case "-":
                return Map(operand, value => -value);
            case "+":
                return operand;
            case "!":
                return ShaderValue.Scalar(operand.IsTrue ? 0f : 1f);
            case "~":
                return Map(operand, value => (float)~(int)value);
            case "++":
            case "--":
                var updated = Map(operand, value => expression.Text == "++" ? value + 1f : value - 1f);
                if (expression.Left!.Kind == ShaderNodeKind.Identifier)
                    _variables[expression.Left.Text] = updated;
                return updated;
            default:
                return operand;
        }
    }

    /// <summary>Evaluates a binary operator, including the assignment operators.</summary>
    /// <param name="expression">Binary node.</param>
    /// <param name="depth">Current call depth.</param>
    /// <returns>The resulting value.</returns>
    private ShaderValue EvaluateBinary(ShaderNode expression, int depth)
    {
        var name = expression.Text;
        if (name is "=" or "+=" or "-=" or "*=" or "/=")
        {
            var current = name == "=" ? ShaderValue.Scalar(0f) : Evaluate(expression.Left!, depth);
            var operand = Evaluate(expression.Right!, depth);
            var assigned = name switch
            {
                "=" => operand,
                "+=" => ComponentWise(current, operand, (a, b) => a + b),
                "-=" => ComponentWise(current, operand, (a, b) => a - b),
                "*=" => ComponentWise(current, operand, (a, b) => a * b),
                _ => ComponentWise(current, operand, SafeDivide)
            };
            Assign(expression.Left!, assigned);
            return assigned;
        }

        if (name is "&&" or "||")
        {
            var left = Evaluate(expression.Left!, depth).IsTrue;
            if (name == "&&" && !left)
                return ShaderValue.Scalar(0f);
            if (name == "||" && left)
                return ShaderValue.Scalar(1f);

            return ShaderValue.Scalar(Evaluate(expression.Right!, depth).IsTrue ? 1f : 0f);
        }

        var first = Evaluate(expression.Left!, depth);
        var second = Evaluate(expression.Right!, depth);
        return name switch
        {
            "+" => ComponentWise(first, second, (a, b) => a + b),
            "-" => ComponentWise(first, second, (a, b) => a - b),
            "*" => ComponentWise(first, second, (a, b) => a * b),
            "/" => ComponentWise(first, second, SafeDivide),
            "%" => ComponentWise(first, second, (a, b) => b == 0f ? 0f : a % b),
            "==" => ComponentWise(first, second, (a, b) => a == b ? 1f : 0f),
            "!=" => ComponentWise(first, second, (a, b) => a != b ? 1f : 0f),
            "<" => ComponentWise(first, second, (a, b) => a < b ? 1f : 0f),
            ">" => ComponentWise(first, second, (a, b) => a > b ? 1f : 0f),
            "<=" => ComponentWise(first, second, (a, b) => a <= b ? 1f : 0f),
            ">=" => ComponentWise(first, second, (a, b) => a >= b ? 1f : 0f),
            _ => ShaderValue.Scalar(0f)
        };
    }

    /// <summary>Writes an assigned value back to a variable or a swizzle target.</summary>
    /// <param name="target">Assignment target.</param>
    /// <param name="value">Value to write.</param>
    private void Assign(ShaderNode target, ShaderValue value)
    {
        if (target.Kind == ShaderNodeKind.Identifier)
        {
            _variables[target.Text] = value;
            return;
        }

        if (target.Kind != ShaderNodeKind.Member || target.Left!.Kind != ShaderNodeKind.Identifier)
            return;

        var name = target.Left.Text;
        var current = _variables.TryGetValue(name, out var existing) ? existing : ShaderValue.Scalar(0f);
        var components = ComponentIndices(target.Text, target.Position);
        for (var index = 0; index < components.Count && index < value.Count; index++)
            current = current.With(components[index], value.Get(index));

        _variables[name] = current;
    }

    /// <summary>Evaluates a call, which is either an intrinsic or a vector constructor.</summary>
    /// <param name="call">Call node.</param>
    /// <param name="depth">Current call depth.</param>
    /// <returns>The resulting value.</returns>
    private ShaderValue EvaluateCall(ShaderNode call, int depth)
    {
        var name = call.Text;
        var arguments = new ShaderValue[call.Items.Count];
        for (var index = 0; index < arguments.Length; index++)
            arguments[index] = Evaluate(call.Items[index], depth);

        if (name is "float" or "half")
            return ShaderValue.Scalar(arguments.Length > 0 ? arguments[0].X : 0f);
        if (name is "int" or "uint" or "bool")
            return ShaderValue.Scalar(arguments.Length > 0 ? (float)(int)arguments[0].X : 0f);
        if (TryConstruct(name, arguments, out var constructed))
            return constructed;

        return name switch
        {
            "abs" => Unary(arguments, MathF.Abs),
            "ceil" => Unary(arguments, MathF.Ceiling),
            "cos" => Unary(arguments, MathF.Cos),
            "exp" => Unary(arguments, MathF.Exp),
            "floor" => Unary(arguments, MathF.Floor),
            "frac" => Unary(arguments, value => value - MathF.Floor(value)),
            "log" => Unary(arguments, value => value <= 0f ? 0f : MathF.Log(value)),
            "saturate" => Unary(arguments, value => Math.Clamp(value, 0f, 1f)),
            "sign" => Unary(arguments, value => MathF.Sign(value)),
            "sin" => Unary(arguments, MathF.Sin),
            "sqrt" => Unary(arguments, value => value <= 0f ? 0f : MathF.Sqrt(value)),
            "tan" => Unary(arguments, MathF.Tan),
            "length" => ShaderValue.Scalar(Length(arguments.Length > 0 ? arguments[0] : ShaderValue.Scalar(0f))),
            "normalize" => Normalize(arguments.Length > 0 ? arguments[0] : ShaderValue.Scalar(0f)),
            "dot" => ShaderValue.Scalar(Dot(arguments[0], arguments[1])),
            "pow" => ComponentWise(arguments[0], arguments[1], (a, b) => MathF.Pow(a, b)),
            "min" => ComponentWise(arguments[0], arguments[1], MathF.Min),
            "max" => ComponentWise(arguments[0], arguments[1], MathF.Max),
            "step" => ComponentWise(arguments[1], arguments[0], (a, b) => a >= b ? 1f : 0f),
            "lerp" or "mix" => Lerp(arguments[0], arguments[1], arguments[2]),
            "clamp" => ComponentWise(
                ComponentWise(arguments[0], arguments[1], MathF.Max),
                arguments[2],
                MathF.Min),
            "mul" => ComponentWise(arguments[0], arguments[1], (a, b) => a * b),
            "smoothstep" => Smoothstep(arguments[0], arguments[1], arguments[2]),
            "tex2D" or "tex2Dlod" => Sample(call, arguments),
            _ => throw new PresetExpressionException($"Unknown shader function '{name}'.", call.Position)
        };
    }

    /// <summary>Samples a texture through the bound sampler.</summary>
    /// <param name="call">Call node, used for the error position.</param>
    /// <param name="arguments">Evaluated arguments; the first names the sampler.</param>
    /// <returns>The sampled colour.</returns>
    private ShaderValue Sample(ShaderNode call, ShaderValue[] arguments)
    {
        if (_sampler is null || arguments.Length < 2)
            throw new PresetExpressionException("The shader sampled a texture without a sampler.", call.Position);

        var name = call.Items[0].Kind == ShaderNodeKind.Identifier
            ? call.Items[0].Text
            : "sampler_main";
        var u = arguments[1].X;
        var v = arguments.Length >= 3 ? arguments[2].X : arguments[1].Y;
        return _sampler.Sample(name, u, v);
    }

    /// <summary>Builds a vector from a constructor call.</summary>
    /// <param name="name">Constructor name.</param>
    /// <param name="arguments">Evaluated arguments.</param>
    /// <param name="value">The constructed value.</param>
    /// <returns><see langword="true"/> when the name was a vector constructor.</returns>
    private static bool TryConstruct(string name, ShaderValue[] arguments, out ShaderValue value)
    {
        value = ShaderValue.Scalar(0f);
        var count = name switch
        {
            "float2" or "half2" => 2,
            "float3" or "half3" => 3,
            "float4" or "half4" => 4,
            _ => 0
        };
        if (count == 0)
            return false;

        if (arguments.Length == 0)
        {
            value = ShaderValue.Scalar(0f);
            return true;
        }

        if (arguments.Length == 1)
        {
            var single = arguments[0];
            var components = new float[4];
            for (var index = 0; index < 4; index++)
                components[index] = single.Get(Math.Min(index, single.Count - 1));
            value = new ShaderValue(components[0], components[1], components[2], components[3], count);
            return true;
        }

        var flattened = new List<float>(count);
        foreach (var argument in arguments)
        {
            for (var index = 0; index < argument.Count && flattened.Count < count; index++)
                flattened.Add(argument.Get(index));
        }

        while (flattened.Count < 4)
            flattened.Add(0f);
        value = new ShaderValue(flattened[0], flattened[1], flattened[2], flattened[3], count);
        return true;
    }

    /// <summary>Applies a swizzle to a value.</summary>
    /// <param name="value">Value to swizzle.</param>
    /// <param name="components">Swizzle letters.</param>
    /// <param name="position">Source position for the error message.</param>
    /// <returns>The swizzled value.</returns>
    private static ShaderValue Swizzle(ShaderValue value, string components, int position)
    {
        var indices = ComponentIndices(components, position);
        var result = new float[4];
        for (var index = 0; index < indices.Count; index++)
            result[index] = value.Get(indices[index]);

        return new ShaderValue(result[0], result[1], result[2], result[3], indices.Count);
    }

    /// <summary>Maps swizzle letters to component indices.</summary>
    /// <param name="components">Swizzle letters.</param>
    /// <param name="position">Source position for the error message.</param>
    /// <returns>The component indices in order.</returns>
    private static List<int> ComponentIndices(string components, int position)
    {
        var indices = new List<int>(components.Length);
        foreach (var letter in components)
        {
            var index = letter switch
            {
                'x' or 'r' => 0,
                'y' or 'g' => 1,
                'z' or 'b' => 2,
                'w' or 'a' => 3,
                _ => -1
            };
            if (index < 0)
                throw new PresetExpressionException($"Invalid swizzle '{components}'.", position);
            indices.Add(index);
        }

        return indices;
    }

    /// <summary>Applies one function to every component of a value.</summary>
    private static ShaderValue Map(ShaderValue value, Func<float, float> map) =>
        new(map(value.X), map(value.Y), map(value.Z), map(value.W), value.Count);

    /// <summary>Applies one function to the first argument, component by component.</summary>
    private static ShaderValue Unary(ShaderValue[] arguments, Func<float, float> map) =>
        arguments.Length > 0 ? Map(arguments[0], map) : ShaderValue.Scalar(0f);

    /// <summary>Combines two values component by component.</summary>
    private static ShaderValue ComponentWise(ShaderValue left, ShaderValue right, Func<float, float, float> combine)
    {
        var count = Math.Max(left.Count, right.Count);
        var components = new float[4];
        for (var index = 0; index < 4; index++)
            components[index] = combine(left.Get(index), right.Get(index));
        return new ShaderValue(components[0], components[1], components[2], components[3], count);
    }

    /// <summary>Interpolates between two values.</summary>
    private static ShaderValue Lerp(ShaderValue from, ShaderValue to, ShaderValue amount)
    {
        var difference = ComponentWise(from, to, (a, b) => b - a);
        var scaled = ComponentWise(difference, amount, (delta, t) => delta * t);
        return ComponentWise(from, scaled, (a, b) => a + b);
    }

    /// <summary>Evaluates the smooth Hermite interpolation between two edges.</summary>
    private static ShaderValue Smoothstep(ShaderValue edge0, ShaderValue edge1, ShaderValue value)
    {
        var ratio = ComponentWise(
            ComponentWise(value, edge0, (a, b) => a - b),
            ComponentWise(edge1, edge0, (a, b) => a - b),
            SafeDivide);
        return ComponentWise(ratio, ratio, (t, _) => t * t * (3f - (2f * t)));
    }

    /// <summary>Divides, treating a zero divisor as zero instead of producing an infinity.</summary>
    private static float SafeDivide(float numerator, float denominator) =>
        denominator == 0f ? 0f : numerator / denominator;

    /// <summary>Computes the length of the first components of a value.</summary>
    private static float Length(ShaderValue value)
    {
        var total = 0f;
        for (var index = 0; index < value.Count; index++)
            total += value.Get(index) * value.Get(index);
        return MathF.Sqrt(total);
    }

    /// <summary>Normalizes a value, leaving a zero vector alone.</summary>
    private static ShaderValue Normalize(ShaderValue value)
    {
        var length = Length(value);
        return length <= 0f ? value : Map(value, component => component / length);
    }

    /// <summary>Computes the dot product of two values.</summary>
    private static float Dot(ShaderValue left, ShaderValue right)
    {
        var total = 0f;
        for (var index = 0; index < Math.Max(left.Count, right.Count); index++)
            total += left.Get(index) * right.Get(index);
        return total;
    }

    /// <summary>Returns the default value of a declared type.</summary>
    /// <param name="type">Type name.</param>
    /// <returns>The default value.</returns>
    private static ShaderValue DefaultFor(string type) => type switch
    {
        "float2" or "half2" => ShaderValue.Vector(0f, 0f, 0f, 0f, 2),
        "float3" or "half3" => ShaderValue.Vector(0f, 0f, 0f, 0f, 3),
        "float4" or "half4" => ShaderValue.Vector(0f, 0f, 0f, 0f, 4),
        _ => ShaderValue.Scalar(0f)
    };
}
