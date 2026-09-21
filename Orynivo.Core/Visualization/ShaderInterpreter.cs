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

    /// <summary>Samples one of the progressively blurred copies of the frame.</summary>
    /// <param name="level">Blur level from one to three.</param>
    /// <param name="u">Horizontal coordinate.</param>
    /// <param name="v">Vertical coordinate.</param>
    /// <returns>The sampled colour.</returns>
    ShaderValue SampleBlur(int level, float u, float v);

    /// <summary>Samples a cubic volume texture, which is what <c>tex3D</c> reads.</summary>
    /// <param name="sampler">Sampler name, for example <c>sampler_noisevol_hq</c>.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="z">Z coordinate.</param>
    /// <returns>The sampled colour.</returns>
    ShaderValue SampleVolume(string sampler, float x, float y, float z);

    /// <summary>Reads one pixel of the frame by integer coordinate.</summary>
    /// <param name="x">Column index.</param>
    /// <param name="y">Row index.</param>
    /// <returns>The pixel colour.</returns>
    ShaderValue SamplePixel(int x, int y);
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

    /// <summary>The helper functions the shader defines, by name, ready to be called.</summary>
    private readonly Dictionary<string, ShaderNode> _functions = new(StringComparer.Ordinal);
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
        ShaderValue? result = null;
        foreach (var statement in _program.Items)
        {
            // A definition is a helper unless it is the entry point, which Milkdrop names main; its
            // bare shader_body form has no definition at all. Order cannot decide it, because a
            // preset may declare its helpers before main.
            if (statement.Kind == ShaderNodeKind.Function &&
                !string.Equals(statement.Text, "main", StringComparison.Ordinal))
            {
                _functions[statement.Text] = statement;
                continue;
            }

            result = ExecuteStatement(statement, 0);
            if (result is not null)
                break;
        }

        ReturnedValue = result is not null;
        return result ?? ShaderValue.Scalar(0f);
    }

    /// <summary>
    /// Gets a value indicating whether the shader body returned a value. Milkdrop shaders often
    /// write their result into the <c>ret</c> variable instead, so a caller that needs the output
    /// colour has to fall back to that variable when nothing was returned.
    /// </summary>
    public bool ReturnedValue { get; private set; }

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
            case ShaderNodeKind.Function:
                // Until the entry point is marked, a definition still runs where it stands, which is
                // the behaviour this refactor deliberately preserves.
                return ExecuteBlock(statement.Items, depth);
            case ShaderNodeKind.Declaration:
                // Every declared name exists; only the first may carry the initializer. The value is
                // coerced to the declared type, because HLSL truncates a wider value and the SkSL
                // emitter does the same, so a "float z = float4(...)" has to agree on both paths.
                for (var index = 0; index < statement.Items.Count; index++)
                {
                    var declared = statement.Items[index].Text;
                    if (declared.Length == 0)
                        continue;

                    _variables[declared] = ShaderRuntime.Coerce(
                        index == 0 && statement.Left is not null
                            ? Evaluate(statement.Left, depth)
                            : ShaderRuntime.DefaultFor(statement.Text),
                        statement.Text);
                }

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
            case ShaderNodeKind.While:
                return ExecuteWhile(statement, depth);
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
            var result = ExecuteLoopBody(loop, depth);
            if (result is not null)
                return result;

            if (loop.Third is not null)
                Evaluate(loop.Third, depth);
        }

        return null;
    }

    /// <summary>Executes a <c>while</c> loop with a bounded iteration count.</summary>
    /// <param name="loop">Loop node.</param>
    /// <param name="depth">Current call depth.</param>
    /// <returns>The returned value, or <see langword="null"/> when the loop finished.</returns>
    private ShaderValue? ExecuteWhile(ShaderNode loop, int depth)
    {
        while (loop.Left is not null && Evaluate(loop.Left, depth).IsTrue)
        {
            var result = ExecuteLoopBody(loop, depth);
            if (result is not null)
                return result;
        }

        return null;
    }

    /// <summary>Runs one iteration of a loop body against the shared iteration budget.</summary>
    /// <param name="loop">Loop node holding the body.</param>
    /// <param name="depth">Current call depth.</param>
    /// <returns>The returned value, or <see langword="null"/> when the iteration finished.</returns>
    private ShaderValue? ExecuteLoopBody(ShaderNode loop, int depth)
    {
        if (++_iterations > MaxLoopIterations)
            throw new PresetExpressionException("The shader looped too often.", loop.Position);

        foreach (var body in loop.Items)
        {
            var result = ExecuteStatement(body, depth);
            if (result is not null)
                return result;
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
                return ShaderRuntime.Swizzle(Evaluate(expression.Left!, depth), expression.Text, expression.Position);
            case ShaderNodeKind.Call:
                return EvaluateCall(expression, depth);
            case ShaderNodeKind.Unary:
                return EvaluateUnary(expression, depth);
            case ShaderNodeKind.Binary:
                return EvaluateBinary(expression, depth);
            case ShaderNodeKind.Index:
                return EvaluateIndex(expression, depth);
            case ShaderNodeKind.Ternary:
                return Evaluate(expression.Left!, depth).IsTrue
                    ? Evaluate(expression.Right!, depth)
                    : Evaluate(expression.Third!, depth);
            default:
                return ShaderValue.Scalar(0f);
        }
    }

    /// <summary>
    /// Evaluates an element access with <c>[...]</c>. Milkdrop presets use it for the components of
    /// a vector and for the rows of a matrix; only the vector form is modelled, so an out-of-range
    /// index yields zero instead of failing the shader.
    /// </summary>
    /// <param name="expression">Index node.</param>
    /// <param name="depth">Current call depth.</param>
    /// <returns>The selected component.</returns>
    private ShaderValue EvaluateIndex(ShaderNode expression, int depth)
    {
        var target = Evaluate(expression.Left!, depth);
        var index = (int)Evaluate(expression.Right!, depth).X;
        if (index < 0 || index >= target.Count)
            return ShaderValue.Scalar(0f);

        return ShaderValue.Scalar(target.Get(index));
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
                return ShaderRuntime.Map(operand, value => -value);
            case "+":
                return operand;
            case "!":
                return ShaderValue.Scalar(operand.IsTrue ? 0f : 1f);
            case "~":
                return ShaderRuntime.Map(operand, value => (float)~(int)value);
            case "++":
            case "--":
                var updated = ShaderRuntime.Map(operand, value => expression.Text == "++" ? value + 1f : value - 1f);
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
        if (name == ",")
        {
            Evaluate(expression.Left!, depth);
            return Evaluate(expression.Right!, depth);
        }

        if (name is "=" or "+=" or "-=" or "*=" or "/=")
        {
            var current = name == "=" ? ShaderValue.Scalar(0f) : Evaluate(expression.Left!, depth);
            var operand = Evaluate(expression.Right!, depth);
            var assigned = name switch
            {
                "=" => operand,
                "+=" => ShaderRuntime.ComponentWise(current, operand, (a, b) => a + b),
                "-=" => ShaderRuntime.ComponentWise(current, operand, (a, b) => a - b),
                "*=" => ShaderRuntime.ComponentWise(current, operand, (a, b) => a * b),
                _ => ShaderRuntime.ComponentWise(current, operand, ShaderRuntime.SafeDivide)
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
            "+" => ShaderRuntime.ComponentWise(first, second, (a, b) => a + b),
            "-" => ShaderRuntime.ComponentWise(first, second, (a, b) => a - b),
            "*" => ShaderRuntime.ComponentWise(first, second, (a, b) => a * b),
            "/" => ShaderRuntime.ComponentWise(first, second, ShaderRuntime.SafeDivide),
            "%" => ShaderRuntime.ComponentWise(first, second, (a, b) => b == 0f ? 0f : a % b),
            "==" => ShaderRuntime.ComponentWise(first, second, (a, b) => a == b ? 1f : 0f),
            "!=" => ShaderRuntime.ComponentWise(first, second, (a, b) => a != b ? 1f : 0f),
            "<" => ShaderRuntime.ComponentWise(first, second, (a, b) => a < b ? 1f : 0f),
            ">" => ShaderRuntime.ComponentWise(first, second, (a, b) => a > b ? 1f : 0f),
            "<=" => ShaderRuntime.ComponentWise(first, second, (a, b) => a <= b ? 1f : 0f),
            ">=" => ShaderRuntime.ComponentWise(first, second, (a, b) => a >= b ? 1f : 0f),
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
        Span<int> components = stackalloc int[4];
        var count = ShaderRuntime.ComponentIndices(target.Text, target.Position, components);
        for (var index = 0; index < count && index < value.Count; index++)
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
        var count = call.Items.Count;

        // A call to a function the shader defines itself is bound to its parameters and run with a
        // fresh scope; the caller's values are put back afterwards so a local never leaks out.
        if (_functions.TryGetValue(name, out var function))
        {
            if (depth >= MaxDepth)
                throw new PresetExpressionException("The shader recursed too deeply.", call.Position);

            Span<ShaderValue?> previous = function.ParameterList.Count <= 8
                ? stackalloc ShaderValue?[8]
                : new ShaderValue?[function.ParameterList.Count];
            for (var index = 0; index < function.ParameterList.Count; index++)
            {
                var parameter = function.ParameterList[index].Text;
                previous[index] = _variables.TryGetValue(parameter, out var existing) ? existing : null;
                _variables[parameter] = index < count
                    ? Evaluate(call.Items[index], depth)
                    : ShaderValue.Scalar(0f);
            }

            var returned = ExecuteBlock(function.Items, depth + 1);
            for (var index = 0; index < function.ParameterList.Count; index++)
            {
                var parameter = function.ParameterList[index].Text;
                if (previous[index] is { } value)
                    _variables[parameter] = value;
                else
                    _variables.Remove(parameter);
            }

            return returned ?? ShaderValue.Scalar(0f);
        }

        Span<ShaderValue> arguments = stackalloc ShaderValue[4];
        for (var index = 0; index < count && index < 4; index++)
            arguments[index] = Evaluate(call.Items[index], depth);

        var samplerName = call.Items.Count > 0 && call.Items[0].Kind == ShaderNodeKind.Identifier
            ? call.Items[0].Text
            : string.Empty;
        return ShaderRuntime.Call(
            name,
            samplerName,
            call.Position,
            _sampler,
            arguments[0],
            arguments[1],
            arguments[2],
            arguments[3],
            count);
    }

}
