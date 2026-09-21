using System.Globalization;
using System.Text;

namespace Orynivo.Visualization;

/// <summary>
/// Translates the parsed HLSL subset of a Milkdrop shader into SkSL, the shading language of Skia's
/// runtime effects. It works on the tree <see cref="ShaderParser"/> already produces, so the GPU
/// path shares the front end with the CPU interpreter and only the back end differs; anything the
/// subset does not cover is reported instead of being guessed, and the caller keeps that shader on
/// the interpreter.
/// </summary>
public static class ShaderTranspiler
{
    /// <summary>
    /// Upper bound on the iterations a translated loop may run. Skia unrolls the loops of a
    /// runtime effect, so a large bound makes the program too large to compile; a shader with a
    /// longer loop therefore stays on the CPU interpreter.
    /// </summary>
    private const int MaxTranslatedIterations = 32;

    /// <summary>
    /// Every uniform the prelude declares, with its component count. The runner seeds all of them,
    /// because Skia requires each declared uniform to be set, and the shader vocabulary of Milkdrop
    /// includes variables the engine has to supply: the 32 <c>q</c> and 8 <c>t</c> slots, and the
    /// texture size of each sampler, which shaders read as <c>texsize_noise_lq.zw</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, int> UniformComponents { get; } = BuildUniformComponents();

    /// <summary>The uniforms the literal prelude already spells out.</summary>
    private static readonly HashSet<string> LiteralUniforms = new(StringComparer.Ordinal)
    {
        "texsize", "time", "frame", "fps", "bass", "mid", "treb", "vol",
        "bass_att", "mid_att", "treb_att", "aspect", "rand_frame", "rand_preset"
    };

    /// <summary>Builds the uniform table.</summary>
    /// <returns>The uniform names with their component counts.</returns>
    private static Dictionary<string, int> BuildUniformComponents()
    {
        var uniforms = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["texsize"] = 4,
            ["time"] = 1,
            ["frame"] = 1,
            ["fps"] = 1,
            ["bass"] = 1,
            ["mid"] = 1,
            ["treb"] = 1,
            ["vol"] = 1,
            ["bass_att"] = 1,
            ["mid_att"] = 1,
            ["treb_att"] = 1,
            ["aspect"] = 2,
            ["rand_frame"] = 4,
            ["rand_preset"] = 4
        };
        foreach (var name in new[]
        {
            "texsize_main", "texsize_fc_main", "texsize_pc_main",
            "texsize_noise_lq", "texsize_noise_mq", "texsize_noise_hq",
            "texsize_noisevol_lq", "texsize_noisevol_hq"
        })
        {
            uniforms[name] = 4;
        }

        for (var index = 1; index <= 32; index++)
            uniforms["q" + index.ToString(CultureInfo.InvariantCulture)] = 1;
        for (var index = 1; index <= 8; index++)
            uniforms["t" + index.ToString(CultureInfo.InvariantCulture)] = 1;
        return uniforms;
    }

    /// <summary>Emits the uniform declarations the literal prelude does not carry.</summary>
    /// <returns>The generated declarations.</returns>
    private static string GeneratedUniforms()
    {
        var builder = new StringBuilder();
        foreach (var (name, count) in UniformComponents)
        {
            if (LiteralUniforms.Contains(name))
                continue;

            builder.Append("uniform ")
                .Append(count switch { 2 => "float2", 4 => "float4", _ => "float" })
                .Append(' ').Append(name).Append(";\n");
        }

        return builder.ToString();
    }

    /// <summary>The sampler a shader reads when it does not name one.</summary>
    private const string MainSampler = "sampler_main";

    /// <summary>Built-in functions that keep their name in SkSL.</summary>
    private static readonly HashSet<string> DirectFunctions = new(StringComparer.Ordinal)
    {
        "abs", "acos", "asin", "atan", "ceil", "clamp", "cos", "cross", "degrees", "distance",
        "dot", "exp", "exp2", "floor", "length", "log", "log2", "max", "min", "mix", "normalize",
        "pow", "radians", "reflect", "refract", "sign", "sin", "smoothstep", "sqrt", "step", "tan"
    };

    /// <summary>Built-in functions whose SkSL spelling differs.</summary>
    private static readonly Dictionary<string, string> RenamedFunctions = new(StringComparer.Ordinal)
    {
        ["frac"] = "fract",
        ["fmod"] = "mod",
        ["rsqrt"] = "inversesqrt"
    };

    /// <summary>The uniform block every translated shader starts with.</summary>
    private const string Prelude = """
        uniform float4 texsize;
        uniform float time;
        uniform float frame;
        uniform float fps;
        uniform float bass;
        uniform float mid;
        uniform float treb;
        uniform float vol;
        uniform float bass_att;
        uniform float mid_att;
        uniform float treb_att;
        uniform float2 aspect;
        uniform float4 rand_frame;
        uniform float4 rand_preset;
        uniform shader sampler_main;
        uniform shader sampler_blur1;
        uniform shader sampler_blur2;
        uniform shader sampler_blur3;
        uniform shader sampler_noise_lq;
        uniform shader sampler_noise_mq;
        uniform shader sampler_noise_hq;
        uniform shader sampler_fc_main;
        uniform shader sampler_pc_main;
        uniform shader sampler_noisevol_lq;
        uniform shader sampler_noisevol_hq;
        uniform shader sampler_pw_main;
        uniform shader sampler_pw_noise_lq;
        uniform shader sampler_worms;
        float4 toColour(float3 c) { return float4(c, 1.0); }
        float4 toColour(float4 c) { return c; }
        float4 toColour(float c) { return float4(c, c, c, 1.0); }
        """;

    /// <summary>
    /// Translates a parsed shader body into SkSL. Milkdrop shaders write their result into the
    /// <c>ret</c> variable, which becomes the returned colour, so a body that never returns still
    /// produces a picture.
    /// </summary>
    /// <param name="program">Root node returned by <see cref="ShaderParser.Parse"/>.</param>
    /// <returns>SkSL source for a <c>half4 main(float2 fragCoord)</c> runtime effect.</returns>
    /// <exception cref="PresetExpressionException">The body uses something SkSL cannot express here.</exception>
    public static string Transpile(ShaderNode program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var builder = new StringBuilder();
        builder.Append(Prelude).Append(GeneratedUniforms()).Append('\n');
        builder.Append("half4 main(float2 fragCoord) {\n");
        builder.Append("    float2 uv_orig = fragCoord / texsize.xy;\n");
        builder.Append("    float2 uv = uv_orig;\n");
        builder.Append("    float2 centred = (uv * 2.0) - 1.0;\n");
        builder.Append("    float rad = length(centred);\n");
        builder.Append("    float ang = atan(centred.y, centred.x);\n");
        builder.Append("    float3 ret = float3(0.0);\n");
        // The output variable is declared by the prelude, so it is the one entry the type table
        // starts with; every other variable is recorded as it is declared.
        _types = new Dictionary<string, string>(StringComparer.Ordinal) { ["ret"] = "float3" };
        // The prelude uniforms are part of the vocabulary a body may read, so their types are
        // known too: that is what narrows "rand_frame * 64.0" into a scalar context.
        foreach (var (uniform, count) in UniformComponents)
        {
            _types[uniform] = count switch { 1 => "float", 2 => "float2", 4 => "float4", _ => "float" };
        }
        foreach (var statement in program.Items)
            EmitStatement(builder, statement, 1);
        builder.Append("    return half4(toColour(ret));\n");
        builder.Append("}\n");
        return builder.ToString();
    }

    /// <summary>Emits one statement.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="statement">Statement node.</param>
    /// <param name="depth">Indentation depth.</param>
    private static void EmitStatement(StringBuilder builder, ShaderNode statement, int depth)
    {
        var indent = Indent(depth);
        switch (statement.Kind)
        {
            case ShaderNodeKind.Block:
                builder.Append(indent).Append("{\n");
                foreach (var child in statement.Items)
                    EmitStatement(builder, child, depth + 1);
                builder.Append(indent).Append("}\n");
                return;
            case ShaderNodeKind.Declaration:
                {
                    // A declaration may name several variables; the parser keeps the first name and
                    // the rest are declared alongside it, which SkSL needs spelled out.
                    var name = statement.Items.Count > 0 ? statement.Items[0].Text : null;
                    if (name is null)
                        return;

                    var declared = MapType(statement.Text);

                    // The samplers are already declared in the prelude, and SkSL requires a shader
                    // variable to be global, so a sampler declaration inside the body is dropped.
                    if (declared == "shader")
                        return;

                    if (_types is not null)
                        _types[name] = declared;

                    builder.Append(indent).Append(declared).Append(' ').Append(name);
                    if (statement.Left is not null)
                        builder.Append(" = ").Append(EmitInitializer(statement.Text, statement.Left));
                    builder.Append(";\n");
                    return;
                }
            case ShaderNodeKind.ExpressionStatement:
                if (statement.Left is not null)
                    builder.Append(indent).Append(EmitExpression(statement.Left)).Append(";\n");
                return;
            case ShaderNodeKind.If:
                builder.Append(indent).Append("if (").Append(EmitCondition(statement.Left!)).Append(") ");
                EmitBody(builder, statement.Right, depth);
                if (statement.Third is not null)
                {
                    builder.Append(indent).Append("else ");
                    EmitBody(builder, statement.Third, depth);
                }

                return;
            case ShaderNodeKind.For:
                builder.Append(indent).Append("for (");
                builder.Append(statement.Left is null ? string.Empty : EmitForPart(statement.Left));
                builder.Append("; ");
                builder.Append(statement.Right is null ? string.Empty : EmitCondition(statement.Right));
                builder.Append("; ");
                builder.Append(statement.Third is null ? string.Empty : EmitExpression(statement.Third));
                builder.Append(") ");
                EmitBody(builder, statement.Items.Count > 0 ? statement.Items[0] : null, depth);
                return;
            case ShaderNodeKind.While:
                // SkSL runtime effects reject while and require a counted for whose index is the
                // left-hand side of the condition, so the loop becomes a bounded counter whose body
                // breaks out as soon as the real condition fails.
                {
                    var counter = $"_orynivoLoop{depth}";
                    var condition = EmitCondition(statement.Left!);
                    builder.Append(indent)
                        .Append("for (int ").Append(counter).Append(" = 0; ")
                        .Append(counter).Append(" < ").Append(MaxTranslatedIterations)
                        .Append("; ").Append(counter).Append("++) {\n");
                    builder.Append(indent).Append("    if (!(").Append(condition).Append(")) { break; }\n");
                    if (statement.Items.Count > 0)
                        EmitStatement(builder, statement.Items[0], depth + 1);
                    builder.Append(indent).Append("}\n");
                    return;
                }
            case ShaderNodeKind.Return:
                // Every return becomes the shader's colour, so the ret convention holds either way.
                builder.Append(indent).Append("return half4(toColour(")
                    .Append(statement.Left is null ? "ret" : EmitExpression(statement.Left))
                    .Append("));\n");
                return;
            default:
                throw new PresetExpressionException(
                    $"The shader statement '{statement.Kind}' has no SkSL translation.",
                    statement.Position);
        }
    }

    /// <summary>Emits the body of a control statement, adding braces when it is a single statement.</summary>
    /// <param name="builder">Output.</param>
    /// <param name="body">Body node, or <see langword="null"/> for an empty body.</param>
    /// <param name="depth">Indentation depth.</param>
    private static void EmitBody(StringBuilder builder, ShaderNode? body, int depth)
    {
        if (body is null)
        {
            builder.Append("{}\n");
            return;
        }

        if (body.Kind == ShaderNodeKind.Block)
        {
            EmitStatement(builder, body, depth);
            return;
        }

        builder.Append("{\n");
        EmitStatement(builder, body, depth + 1);
        builder.Append(Indent(depth)).Append("}\n");
    }

    /// <summary>Emits a <c>for</c> initializer, which is a declaration or an expression.</summary>
    /// <param name="initializer">Initializer node.</param>
    /// <returns>Text without its trailing semicolon.</returns>
    private static string EmitForPart(ShaderNode initializer)
    {
        var builder = new StringBuilder();
        EmitStatement(builder, initializer, 0);
        return builder.ToString().TrimEnd('\n', ';');
    }

    /// <summary>Emits an expression as a SkSL condition, where a non-zero value is true.</summary>
    /// <param name="expression">Condition node.</param>
    /// <returns>The condition text.</returns>
    private static string EmitCondition(ShaderNode expression) => EmitExpression(expression) + " != 0.0";

    /// <summary>
    /// Emits a declaration initializer. SkSL has no implicit scalar-to-vector or float-to-int
    /// conversion, so <c>float3 sum = 0;</c> has to become <c>float3 sum = float3(0.0);</c> and
    /// <c>int n = 0;</c> needs an integer literal.
    /// </summary>
    /// <param name="type">Declared type name.</param>
    /// <param name="expression">Initializer expression.</param>
    /// <returns>The initializer text.</returns>
    private static string EmitInitializer(string type, ShaderNode expression)
    {
        var mapped = MapType(type);
        var text = EmitExpression(expression);
        return Convert(text, TypeOf(expression), mapped);
    }

    /// <summary>Reports whether a type name is a vector or matrix with more than one component.</summary>
    /// <param name="type">Mapped type name.</param>
    /// <returns><see langword="true"/> when the type is a vector or matrix.</returns>
    private static bool IsVectorType(string type)
    {
        foreach (var character in type)
        {
            if (character is '2' or '3' or '4')
                return true;
        }

        return false;
    }

    /// <summary>Emits one expression.</summary>
    /// <param name="expression">Expression node.</param>
    /// <returns>The expression text.</returns>
    private static string EmitExpression(ShaderNode expression) => expression.Kind switch
    {
        ShaderNodeKind.Literal => expression.Number.ToString("0.0########", CultureInfo.InvariantCulture),
        ShaderNodeKind.Identifier => expression.Text,
        ShaderNodeKind.Member => $"{EmitExpression(expression.Left!)}.{expression.Text}",
        ShaderNodeKind.Index => $"{EmitExpression(expression.Left!)}[{EmitExpression(expression.Right!)}]",
        ShaderNodeKind.Unary => EmitUnary(expression),
        ShaderNodeKind.Binary => EmitBinary(expression),
        ShaderNodeKind.Ternary =>
            $"({EmitCondition(expression.Left!)} ? {EmitExpression(expression.Right!)} : {EmitExpression(expression.Third!)})",
        ShaderNodeKind.Call => EmitCall(expression),
        _ => throw new PresetExpressionException(
            $"The shader expression '{expression.Kind}' has no SkSL translation.",
            expression.Position)
    };

    /// <summary>
    /// The declared type of every variable of the shader being translated. SkSL is strictly typed
    /// while the engine stores every value as a float, so a preset may write <c>float3 x = 0;</c> or
    /// <c>y = aspect;</c> and SkSL refuses both; knowing the declared types is what lets the emitter
    /// widen or narrow those. The state is per thread because one translation runs on one thread.
    /// </summary>
    [ThreadStatic]
    private static Dictionary<string, string>? _types;

    /// <summary>Infers the SkSL type of an expression, or nothing when it cannot be known.</summary>
    /// <param name="expression">Expression node.</param>
    /// <returns>The type name, or <see langword="null"/>.</returns>
    private static string? TypeOf(ShaderNode expression) => expression.Kind switch
    {
        ShaderNodeKind.Literal => "float",
        ShaderNodeKind.Identifier => _types is not null && _types.TryGetValue(expression.Text, out var declared)
            ? declared
            : null,
        ShaderNodeKind.Member => ComponentType(expression.Text.Length),
        ShaderNodeKind.Call => CallType(expression),
        ShaderNodeKind.Unary => TypeOf(expression.Left!),
        ShaderNodeKind.Index => "float",
        ShaderNodeKind.Ternary => Wider(TypeOf(expression.Right!), TypeOf(expression.Third!)),
        ShaderNodeKind.Binary => BinaryType(expression),
        _ => null
    };

    /// <summary>The type name for a component count.</summary>
    /// <param name="count">Component count.</param>
    /// <returns>The SkSL type name.</returns>
    private static string ComponentType(int count) => count switch
    {
        <= 1 => "float",
        2 => "float2",
        3 => "float3",
        _ => "float4"
    };

    /// <summary>Returns the wider of two types, or nothing when either is unknown.</summary>
    /// <param name="left">First type.</param>
    /// <param name="right">Second type.</param>
    /// <returns>The wider type name, or <see langword="null"/>.</returns>
    private static string? Wider(string? left, string? right)
    {
        if (left is null || right is null)
            return null;

        return ComponentCount(left) >= ComponentCount(right) ? left : right;
    }

    /// <summary>Counts the components of a mapped type name.</summary>
    /// <param name="type">Type name.</param>
    /// <returns>The component count.</returns>
    private static int ComponentCount(string type)
    {
        foreach (var character in type)
        {
            if (character is '2' or '3' or '4')
                return character - '0';
        }

        return 1;
    }

    /// <summary>Infers the type a call produces.</summary>
    /// <param name="call">Call node.</param>
    /// <returns>The type name, or <see langword="null"/>.</returns>
    private static string? CallType(ShaderNode call)
    {
        switch (call.Text.ToLowerInvariant())
        {
            case "tex2d":
            case "tex3d":
                return "float4";
            case "getpixel":
            case "getblur1":
            case "getblur2":
            case "getblur3":
                return "float3";
            case "length":
            case "dot":
            case "lum":
                return "float";
        }

        if (IsVectorConstructor(call.Text))
            return MapType(call.Text);

        // An intrinsic keeps the widest component count of its arguments, which is how pow(float3, …)
        // and max(float3, …) behave.
        string? widest = null;
        foreach (var argument in call.Items)
            widest = Wider(widest, TypeOf(argument));
        return widest;
    }

    /// <summary>Infers the type of a binary expression.</summary>
    /// <param name="expression">Binary node.</param>
    /// <returns>The type name, or <see langword="null"/>.</returns>
    private static string? BinaryType(ShaderNode expression) => expression.Text switch
    {
        "=" or "+=" or "-=" or "*=" or "/=" => TypeOf(expression.Left!),
        "<" or ">" or "<=" or ">=" or "==" or "!=" or "&&" or "||" => "float",
        _ => Wider(TypeOf(expression.Left!), TypeOf(expression.Right!))
    };

    /// <summary>
    /// Converts an expression to a target type. SkSL has no implicit conversion between a scalar and
    /// a vector, so a value is widened with a constructor or narrowed with a swizzle; an unknown type
    /// is left alone rather than guessed at.
    /// </summary>
    /// <param name="text">Expression text.</param>
    /// <param name="from">Its inferred type, or nothing.</param>
    /// <param name="to">The type it has to become.</param>
    /// <returns>The converted text.</returns>
    private static string Convert(string text, string? from, string to)
    {
        if (from is null || from == to)
            return text;

        var source = ComponentCount(from);
        var target = ComponentCount(to);
        if (source == target)
            return text;
        if (target > source)
        {
            // Padding a smaller vector is deliberately not done: for a division it would create
            // "0.0 / 0.0", which SkSL rejects at compile time. A widening that cannot be expressed
            // safely is left alone so the shader falls back to the interpreter.
            return $"{to}({text})";
        }

        return $"{text}.{"xyzw"[..target]}";
    }

    /// <summary>
    /// Emits an assignment, converting between a scalar and a vector where the engine would allow it
    /// and SkSL would not. It only acts when both types are known, so an unknown expression is never
    /// rewritten on a guess.
    /// </summary>
    /// <param name="left">Target text.</param>
    /// <param name="right">Value text.</param>
    /// <param name="expression">Assignment node.</param>
    /// <returns>The assignment text.</returns>
    private static string EmitAssignment(string left, string right, ShaderNode expression)
    {
        var targetType = expression.Left is { Kind: ShaderNodeKind.Identifier } target &&
            _types is not null &&
            _types.TryGetValue(target.Text, out var declared)
                ? declared
                : null;
        var valueType = expression.Right is null ? null : TypeOf(expression.Right);
        if (targetType is not null && expression.Right is not null)
            right = Convert(right, valueType, targetType);

        return $"{left} = {right}";
    }

    /// <summary>Emits a unary expression, keeping the preset convention that zero is false.</summary>
    /// <param name="expression">Unary node.</param>
    /// <returns>The expression text.</returns>
    private static string EmitUnary(ShaderNode expression)
    {
        var operand = EmitExpression(expression.Left!);
        return expression.Text switch
        {
            "-" => $"(-{operand})",
            "+" => operand,
            "++" => $"(++{operand})",
            "--" => $"(--{operand})",
            "!" => $"(({operand}) == 0.0 ? 1.0 : 0.0)",
            _ => throw new PresetExpressionException(
                $"The unary operator '{expression.Text}' has no SkSL translation.",
                expression.Position)
        };
    }

    /// <summary>Emits a binary expression, mapping comparisons onto the one-or-zero convention.</summary>
    /// <param name="expression">Binary node.</param>
    /// <returns>The expression text.</returns>
    private static string EmitBinary(ShaderNode expression)
    {
        var leftNode = expression.Left!;
        var rightNode = expression.Right!;
        var leftType = TypeOf(leftNode);
        var rightType = TypeOf(rightNode);
        var left = EmitExpression(leftNode);
        var right = EmitExpression(rightNode);

        // SkSL wants matching component counts, while a preset mixes them freely: "float3 * float2"
        // and "float4 + float3" both occur. Both operands are brought to the wider type, which is
        // what the engine's component-wise arithmetic already does. An assignment is left alone: its
        // target must never be converted, only the value it is given, which EmitAssignment does.
        var isAssignment = expression.Text is "=" or "+=" or "-=" or "*=" or "/=";
        if (!isAssignment &&
            leftType is not null && rightType is not null &&
            ComponentCount(leftType) != ComponentCount(rightType) &&
            Wider(leftType, rightType) is { } common)
        {
            left = Convert(left, leftType, common);
            right = Convert(right, rightType, common);
        }

        return expression.Text switch
        {
            "+" => $"({left} + {right})",
            "-" => $"({left} - {right})",
            "*" => $"({left} * {right})",
            "/" => $"({left} / {right})",
            "%" => $"mod({left}, {right})",
            "=" => EmitAssignment(left, right, expression),
            "+=" => $"{left} += {right}",
            "-=" => $"{left} -= {right}",
            "*=" => $"{left} *= {right}",
            "/=" => $"{left} /= {right}",
            "==" => $"(({left} == {right}) ? 1.0 : 0.0)",
            "!=" => $"(({left} != {right}) ? 1.0 : 0.0)",
            "<" => $"(({left} < {right}) ? 1.0 : 0.0)",
            ">" => $"(({left} > {right}) ? 1.0 : 0.0)",
            "<=" => $"(({left} <= {right}) ? 1.0 : 0.0)",
            ">=" => $"(({left} >= {right}) ? 1.0 : 0.0)",
            "&&" => $"((({left} != 0.0) && ({right} != 0.0)) ? 1.0 : 0.0)",
            "||" => $"((({left} != 0.0) || ({right} != 0.0)) ? 1.0 : 0.0)",
            "," => $"({left}, {right})",
            _ => throw new PresetExpressionException(
                $"The binary operator '{expression.Text}' has no SkSL translation.",
                expression.Position)
        };
    }

    /// <summary>Emits a call, translating the sampler accessors and the renamed intrinsics.</summary>
    /// <param name="call">Call node.</param>
    /// <returns>The expression text.</returns>
    private static string EmitCall(ShaderNode call)
    {
        var name = call.Text;
        var arguments = new List<string>(call.Items.Count);
        foreach (var argument in call.Items)
            arguments.Add(EmitExpression(argument));

        // An intrinsic takes matching component counts, while a preset may hand it a float4 where a
        // float3 is meant, as in "max(ret, tex2D(...) * 0.97)". Every argument is brought to the
        // smallest vector count among them; widening a scalar is harmless because it is uniform.
        if (DirectFunctions.Contains(name) || name is "saturate" or "lerp" or "atan2" or "mul" or "lum")
        {
            var smallest = int.MaxValue;
            foreach (var argument in call.Items)
            {
                if (TypeOf(argument) is { } argumentType && ComponentCount(argumentType) > 1)
                    smallest = Math.Min(smallest, ComponentCount(argumentType));
            }

            if (smallest is > 1 and < int.MaxValue)
            {
                for (var index = 0; index < arguments.Count; index++)
                    arguments[index] = Convert(arguments[index], TypeOf(call.Items[index]), ComponentType(smallest));
            }
        }


        // Milkdrop samples the frame and its blur levels through these helpers. A Skia runtime
        // effect samples a shader in pixel coordinates, so a normalised coordinate is scaled back.
        switch (name.ToLowerInvariant())
        {
            case "tex2d":
                if (arguments.Count < 2)
                    throw new PresetExpressionException("tex2D needs a sampler and a coordinate.", call.Position);

                // A Skia shader evaluates to half4, so the result is widened to the float4 the
                // presets expect from tex2D. The coordinate is normalised, so only the size half of
                // texsize applies to it.
                return $"float4({arguments[0]}.eval({arguments[1]} * texsize.xy))";
            case "tex3d":
                // Milkdrop samples a 3D noise volume, and Skia's runtime effects only sample 2D
                // shaders. A procedural replacement was tried and rejected: a sine-based hash is not
                // reproducible between SkSL and the interpreter, so the two paths disagreed by 98 of
                // 255 levels. The GPU needs a real volume texture, shared by both paths, first.
                throw new PresetExpressionException(
                    "tex3D needs a volume texture, which the GPU path does not have yet.",
                    call.Position);
            case "getpixel":
                return arguments.Count >= 2
                    ? $"float4({MainSampler}.eval(float2({arguments[0]}, {arguments[1]}))).rgb"
                    : $"float4({MainSampler}.eval({arguments[0]})).rgb";
            case "getblur1":
                return $"float4(sampler_blur1.eval({arguments[0]})).rgb";
            case "getblur2":
                return $"float4(sampler_blur2.eval({arguments[0]})).rgb";
            case "getblur3":
                return $"float4(sampler_blur3.eval({arguments[0]})).rgb";
            case "saturate":
                return $"clamp({arguments[0]}, 0.0, 1.0)";
            case "atan2":
                return $"atan({arguments[0]}, {arguments[1]})";
            case "lerp":
                return $"mix({arguments[0]}, {arguments[1]}, {arguments[2]})";
            case "mul":
                return arguments.Count >= 2 ? $"({arguments[0]} * {arguments[1]})" : arguments[0];
            case "lum":
                // Milkdrop's luminance helper; the weights are the conventional Rec. 601 ones.
                return $"dot({arguments[0]}, float3(0.299, 0.587, 0.114))";
        }

        if (RenamedFunctions.TryGetValue(name, out var renamed))
            return $"{renamed}({string.Join(", ", arguments)})";

        if (DirectFunctions.Contains(name))
            return $"{name}({string.Join(", ", arguments)})";

        // A vector constructor such as float2(1, 0) keeps its spelling, with the half and double
        // spellings folded onto float because SkSL does not need the precision split here.
        var mapped = MapType(name);
        if (mapped != name || IsVectorConstructor(name))
            return $"{mapped}({string.Join(", ", arguments)})";

        throw new PresetExpressionException(
            $"The shader function '{name}' has no SkSL translation.",
            call.Position);
    }

    /// <summary>Reports whether a name is a vector or matrix constructor.</summary>
    /// <param name="name">Function name.</param>
    /// <returns><see langword="true"/> when the name is a constructor.</returns>
    private static bool IsVectorConstructor(string name) => name.Length >= 6 &&
        (name.StartsWith("float", StringComparison.Ordinal) ||
         name.StartsWith("half", StringComparison.Ordinal) ||
         name.StartsWith("double", StringComparison.Ordinal) ||
         name.StartsWith("int", StringComparison.Ordinal) ||
         name.StartsWith("uint", StringComparison.Ordinal) ||
         name.StartsWith("bool", StringComparison.Ordinal));

    /// <summary>Maps a Milkdrop type name onto its SkSL spelling.</summary>
    /// <param name="type">Type name.</param>
    /// <returns>The SkSL type name.</returns>
    private static string MapType(string type)
    {
        if (type.StartsWith("half", StringComparison.Ordinal))
            return "float" + type[4..];
        if (type.StartsWith("double", StringComparison.Ordinal))
            return "float" + type[6..];

        // The engine stores every value as a float, so integer and boolean variables become floats.
        // SkSL is strictly typed, and "n < 4" with an int and a float is a compile error.
        foreach (var prefix in new[] { "int", "uint", "bool" })
        {
            if (type.StartsWith(prefix, StringComparison.Ordinal))
                return "float" + type[prefix.Length..];
        }

        return type switch
        {
            "half" or "half1" => "float",
            "double" or "double1" => "float",
            "float1" => "float",
            "int" or "int1" or "uint" or "uint1" or "bool" or "bool1" => "float",
            "sampler" or "sampler2D" or "sampler3D" or "texture" => "shader",
            _ => type
        };
    }

    /// <summary>Builds the indentation of one depth level.</summary>
    /// <param name="depth">Indentation depth.</param>
    /// <returns>The indentation text.</returns>
    private static string Indent(int depth) => new(' ', depth * 4);
}
