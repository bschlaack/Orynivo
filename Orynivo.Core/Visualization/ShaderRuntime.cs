namespace Orynivo.Visualization;

/// <summary>
/// The operations a shader is built from, shared by the interpreter and the compiled shader path so
/// both produce identical results. Every operation lives here exactly once, takes its arguments
/// already evaluated, and is allocation-free: the compiled path calls these helpers per pixel, so
/// an allocation here would become garbage in the middle of a frame.
/// </summary>
internal static class ShaderRuntime
{
    /// <summary>
    /// The operations a call can be, as numbers. The compiler knows the function at compile time,
    /// so the compiled path dispatches on an integer instead of comparing names for every pixel.
    /// </summary>
    public enum Opcode
    {
        /// <summary>Not a known operation.</summary>
        Unknown = 0,

        /// <summary>A scalar or vector cast.</summary>
        Cast,

        /// <summary>A two, three, or four component constructor.</summary>
        Construct2,
        Construct3,
        Construct4,

        /// <summary>Component-wise intrinsics.</summary>
        Abs,
        Ceil,
        Cos,
        Exp,
        Floor,
        Frac,
        Log,
        Saturate,
        Sign,
        Sin,
        Sqrt,
        Tan,
        Pow,
        Min,
        Max,
        Step,
        Mul,

        /// <summary>Whole-value intrinsics.</summary>
        Length,
        Normalize,
        Dot,
        Lerp,
        Clamp,
        Smoothstep,

        /// <summary>Texture reads.</summary>
        Sample,
        SampleBlur1,
        SampleBlur2,
        SampleBlur3,
        SamplePixel
    }

    /// <summary>Maps the function names a shader may use to their opcodes.</summary>
    private static readonly Dictionary<string, Opcode> Opcodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["float"] = Opcode.Cast,
        ["float1"] = Opcode.Cast,
        ["half"] = Opcode.Cast,
        ["half1"] = Opcode.Cast,
        ["int"] = Opcode.Cast,
        ["uint"] = Opcode.Cast,
        ["bool"] = Opcode.Cast,
        ["float2"] = Opcode.Construct2,
        ["half2"] = Opcode.Construct2,
        ["float3"] = Opcode.Construct3,
        ["half3"] = Opcode.Construct3,
        ["float4"] = Opcode.Construct4,
        ["half4"] = Opcode.Construct4,
        ["abs"] = Opcode.Abs,
        ["ceil"] = Opcode.Ceil,
        ["cos"] = Opcode.Cos,
        ["exp"] = Opcode.Exp,
        ["floor"] = Opcode.Floor,
        ["frac"] = Opcode.Frac,
        ["log"] = Opcode.Log,
        ["saturate"] = Opcode.Saturate,
        ["sign"] = Opcode.Sign,
        ["sin"] = Opcode.Sin,
        ["sqrt"] = Opcode.Sqrt,
        ["tan"] = Opcode.Tan,
        ["pow"] = Opcode.Pow,
        ["min"] = Opcode.Min,
        ["max"] = Opcode.Max,
        ["step"] = Opcode.Step,
        ["mul"] = Opcode.Mul,
        ["length"] = Opcode.Length,
        ["normalize"] = Opcode.Normalize,
        ["dot"] = Opcode.Dot,
        ["lerp"] = Opcode.Lerp,
        ["mix"] = Opcode.Lerp,
        ["clamp"] = Opcode.Clamp,
        ["smoothstep"] = Opcode.Smoothstep,
        ["tex2D"] = Opcode.Sample,
        ["tex2Dlod"] = Opcode.Sample,
        ["GetBlur1"] = Opcode.SampleBlur1,
        ["GetBlur2"] = Opcode.SampleBlur2,
        ["GetBlur3"] = Opcode.SampleBlur3,
        ["GetPixel"] = Opcode.SamplePixel
    };

    /// <summary>Returns the opcode of a function name.</summary>
    /// <param name="name">Function name.</param>
    /// <returns>The opcode, or <see cref="Opcode.Unknown"/> when the name is not known.</returns>
    public static Opcode OpcodeOf(string name) =>
        Opcodes.TryGetValue(name, out var opcode) ? opcode : Opcode.Unknown;

    /// <summary>Applies a call by name, which is what the interpreter uses.</summary>
    /// <param name="name">Function name.</param>
    /// <param name="samplerName">Sampler a texture call reads, or an empty string.</param>
    /// <param name="position">Source position, for error messages.</param>
    /// <param name="sampler">Bound sampler, or <see langword="null"/>.</param>
    /// <param name="a">First evaluated argument.</param>
    /// <param name="b">Second evaluated argument.</param>
    /// <param name="c">Third evaluated argument.</param>
    /// <param name="d">Fourth evaluated argument.</param>
    /// <param name="count">Number of evaluated arguments.</param>
    /// <returns>The resulting value.</returns>
    public static ShaderValue Call(
        string name,
        string samplerName,
        int position,
        IShaderSampler? sampler,
        ShaderValue a,
        ShaderValue b,
        ShaderValue c,
        ShaderValue d,
        int count)
    {
        var opcode = OpcodeOf(name);
        if (opcode == Opcode.Unknown)
            throw new PresetExpressionException($"Unknown shader function '{name}'.", position);

        return Call(opcode, samplerName, position, sampler, a, b, c, d, count);
    }

    /// <summary>Applies a call by opcode, which is what a compiled shader uses.</summary>
    /// <param name="opcode">Operation to apply.</param>
    /// <param name="samplerName">Sampler a texture call reads, or an empty string.</param>
    /// <param name="position">Source position, for error messages.</param>
    /// <param name="sampler">Bound sampler, or <see langword="null"/>.</param>
    /// <param name="a">First evaluated argument.</param>
    /// <param name="b">Second evaluated argument.</param>
    /// <param name="c">Third evaluated argument.</param>
    /// <param name="d">Fourth evaluated argument.</param>
    /// <param name="count">Number of evaluated arguments.</param>
    /// <returns>The resulting value.</returns>
    public static ShaderValue Call(
        Opcode opcode,
        string samplerName,
        int position,
        IShaderSampler? sampler,
        ShaderValue a,
        ShaderValue b,
        ShaderValue c,
        ShaderValue d,
        int count)
    {
        switch (opcode)
        {
            case Opcode.Cast:
                return ShaderValue.Scalar(count > 0 ? a.X : 0f);
            case Opcode.Construct2:
                return Construct(2, a, b, c, d, count);
            case Opcode.Construct3:
                return Construct(3, a, b, c, d, count);
            case Opcode.Construct4:
                return Construct(4, a, b, c, d, count);
            case Opcode.Abs:
                return Unary(count, a, MathF.Abs);
            case Opcode.Ceil:
                return Unary(count, a, MathF.Ceiling);
            case Opcode.Cos:
                return Unary(count, a, MathF.Cos);
            case Opcode.Exp:
                return Unary(count, a, MathF.Exp);
            case Opcode.Floor:
                return Unary(count, a, MathF.Floor);
            case Opcode.Frac:
                return Unary(count, a, value => value - MathF.Floor(value));
            case Opcode.Log:
                return Unary(count, a, value => value <= 0f ? 0f : MathF.Log(value));
            case Opcode.Saturate:
                return Unary(count, a, value => Math.Clamp(value, 0f, 1f));
            case Opcode.Sign:
                return Unary(count, a, value => MathF.Sign(value));
            case Opcode.Sin:
                return Unary(count, a, MathF.Sin);
            case Opcode.Sqrt:
                return Unary(count, a, value => value <= 0f ? 0f : MathF.Sqrt(value));
            case Opcode.Tan:
                return Unary(count, a, MathF.Tan);
            case Opcode.Pow:
                return ComponentWise(a, b, (left, right) => MathF.Pow(left, right));
            case Opcode.Min:
                return ComponentWise(a, b, MathF.Min);
            case Opcode.Max:
                return ComponentWise(a, b, MathF.Max);
            case Opcode.Step:
                return ComponentWise(b, a, (edge, value) => value >= edge ? 1f : 0f);
            case Opcode.Mul:
                return ComponentWise(a, b, (left, right) => left * right);
            case Opcode.Length:
                return ShaderValue.Scalar(Length(count > 0 ? a : ShaderValue.Scalar(0f)));
            case Opcode.Normalize:
                return Normalize(count > 0 ? a : ShaderValue.Scalar(0f));
            case Opcode.Dot:
                return ShaderValue.Scalar(Dot(a, b));
            case Opcode.Lerp:
                return Lerp(a, b, c);
            case Opcode.Clamp:
                return ComponentWise(ComponentWise(a, b, MathF.Max), c, MathF.Min);
            case Opcode.Smoothstep:
                return Smoothstep(a, b, c);
            case Opcode.Sample:
                return Sample(samplerName, count, a, b, c, sampler, position);
            case Opcode.SampleBlur1:
                return SampleBlur(1, count, a, sampler, position);
            case Opcode.SampleBlur2:
                return SampleBlur(2, count, a, sampler, position);
            case Opcode.SampleBlur3:
                return SampleBlur(3, count, a, sampler, position);
            case Opcode.SamplePixel:
                return SamplePixel(count, a, b, sampler, position);
            default:
                throw new PresetExpressionException("Unknown shader operation.", position);
        }
    }

    /// <summary>Applies a swizzle to a value.</summary>
    /// <param name="value">Value to swizzle.</param>
    /// <param name="components">Swizzle letters.</param>
    /// <param name="position">Source position for the error message.</param>
    /// <returns>The swizzled value.</returns>
    public static ShaderValue Swizzle(ShaderValue value, string components, int position)
    {
        Span<int> indices = stackalloc int[4];
        var count = ComponentIndices(components, position, indices);
        Span<float> result = stackalloc float[4];
        for (var index = 0; index < count; index++)
            result[index] = value.Get(indices[index]);

        return new ShaderValue(result[0], result[1], result[2], result[3], count);
    }

    /// <summary>Adds two values component by component.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>The sum.</returns>
    public static ShaderValue Add(ShaderValue left, ShaderValue right) =>
        ComponentWise(left, right, (a, b) => a + b);

    /// <summary>Subtracts two values component by component.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>The difference.</returns>
    public static ShaderValue Subtract(ShaderValue left, ShaderValue right) =>
        ComponentWise(left, right, (a, b) => a - b);

    /// <summary>Multiplies two values component by component.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>The product.</returns>
    public static ShaderValue Multiply(ShaderValue left, ShaderValue right) =>
        ComponentWise(left, right, (a, b) => a * b);

    /// <summary>Divides two values component by component, treating a zero divisor as zero.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>The quotient.</returns>
    public static ShaderValue Divide(ShaderValue left, ShaderValue right) =>
        ComponentWise(left, right, SafeDivide);

    /// <summary>Takes the remainder of two values component by component.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>The remainder.</returns>
    public static ShaderValue Modulo(ShaderValue left, ShaderValue right) =>
        ComponentWise(left, right, (a, b) => b == 0f ? 0f : a % b);

    /// <summary>Compares two values component by component, yielding one or zero.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <param name="comparison">
    /// Zero equals, one not equal, two less, three greater, four less-or-equal, five greater-or-equal.
    /// </param>
    /// <returns>The comparison result per component.</returns>
    public static ShaderValue Compare(ShaderValue left, ShaderValue right, int comparison) =>
        ComponentWise(left, right, (a, b) => comparison switch
        {
            0 => a == b ? 1f : 0f,
            1 => a != b ? 1f : 0f,
            2 => a < b ? 1f : 0f,
            3 => a > b ? 1f : 0f,
            4 => a <= b ? 1f : 0f,
            _ => a >= b ? 1f : 0f
        });

    /// <summary>Negates a truth value, yielding one or zero.</summary>
    /// <param name="value">Value to negate.</param>
    /// <returns>Zero when the value is true, otherwise one.</returns>
    public static ShaderValue Not(ShaderValue value) => ShaderValue.Scalar(value.IsTrue ? 0f : 1f);

    /// <summary>Maps swizzle letters to component indices.</summary>
    /// <param name="components">Swizzle letters.</param>
    /// <param name="position">Source position for the error message.</param>
    /// <param name="destination">Destination for at most four indices.</param>
    /// <returns>How many indices were written.</returns>
    public static int ComponentIndices(string components, int position, Span<int> destination)
    {
        var count = 0;
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
            if (count < destination.Length)
                destination[count] = index;
            count++;
        }

        return Math.Min(count, destination.Length);
    }

    /// <summary>Applies one function to every component of a value.</summary>
    /// <param name="value">Value to map.</param>
    /// <param name="map">Function to apply.</param>
    /// <returns>The mapped value.</returns>
    public static ShaderValue Map(ShaderValue value, Func<float, float> map) =>
        new(map(value.X), map(value.Y), map(value.Z), map(value.W), value.Count);

    /// <summary>Combines two values component by component.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <param name="combine">Function to apply per component.</param>
    /// <returns>The combined value.</returns>
    public static ShaderValue ComponentWise(
        ShaderValue left,
        ShaderValue right,
        Func<float, float, float> combine)
    {
        var count = Math.Max(left.Count, right.Count);
        return new ShaderValue(
            combine(left.Get(0), right.Get(0)),
            combine(left.Get(1), right.Get(1)),
            combine(left.Get(2), right.Get(2)),
            combine(left.Get(3), right.Get(3)),
            count);
    }

    /// <summary>Divides, treating a zero divisor as zero instead of producing an infinity.</summary>
    /// <param name="numerator">Numerator.</param>
    /// <param name="denominator">Denominator.</param>
    /// <returns>The quotient, or zero.</returns>
    public static float SafeDivide(float numerator, float denominator) =>
        denominator == 0f ? 0f : numerator / denominator;

    /// <summary>Returns the default value of a declared type.</summary>
    /// <param name="type">Type name.</param>
    /// <returns>The default value.</returns>
    public static ShaderValue DefaultFor(string type) => type switch
    {
        "float2" or "half2" => ShaderValue.Vector(0f, 0f, 0f, 0f, 2),
        "float3" or "half3" => ShaderValue.Vector(0f, 0f, 0f, 0f, 3),
        "float4" or "half4" => ShaderValue.Vector(0f, 0f, 0f, 0f, 4),
        _ => ShaderValue.Scalar(0f)
    };

    /// <summary>Applies one function to the first argument.</summary>
    private static ShaderValue Unary(int count, ShaderValue value, Func<float, float> map) =>
        count > 0 ? Map(value, map) : ShaderValue.Scalar(0f);

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

    /// <summary>Samples a texture through the bound sampler.</summary>
    private static ShaderValue Sample(
        string samplerName,
        int count,
        ShaderValue a,
        ShaderValue b,
        ShaderValue c,
        IShaderSampler? sampler,
        int position)
    {
        if (sampler is null || count < 2)
            throw new PresetExpressionException("The shader sampled a texture without a sampler.", position);

        var u = b.X;
        var v = count >= 3 ? c.X : b.Y;
        return sampler.Sample(samplerName.Length == 0 ? "sampler_main" : samplerName, u, v);
    }

    /// <summary>Samples a blurred copy of the frame.</summary>
    private static ShaderValue SampleBlur(
        int level,
        int count,
        ShaderValue a,
        IShaderSampler? sampler,
        int position)
    {
        if (sampler is null || count < 1)
            throw new PresetExpressionException("The shader sampled a texture without a sampler.", position);

        return sampler.SampleBlur(level, a.X, a.Y);
    }

    /// <summary>Reads one frame pixel by integer coordinate.</summary>
    private static ShaderValue SamplePixel(
        int count,
        ShaderValue a,
        ShaderValue b,
        IShaderSampler? sampler,
        int position)
    {
        if (sampler is null || count < 1)
            throw new PresetExpressionException("The shader sampled a texture without a sampler.", position);

        // Milkdrop spells this both ways: GetPixel(x, y) and GetPixel(float2(x, y)). Requiring two
        // scalars made every preset that used the float2 form fail, and a failed comp shader is
        // silently disabled, which costs that preset its picture.
        var x = a.X;
        var y = count >= 2 ? b.X : a.Y;
        return sampler.SamplePixel((int)x, (int)y);
    }

    /// <summary>Builds a vector from a constructor call.</summary>
    private static ShaderValue Construct(
        int size,
        ShaderValue a,
        ShaderValue b,
        ShaderValue c,
        ShaderValue d,
        int count)
    {
        if (count == 0)
            return ShaderValue.Scalar(0f);

        Span<float> flattened = stackalloc float[4];
        if (count == 1)
        {
            // One argument broadcasts, repeating its last component.
            for (var index = 0; index < 4; index++)
                flattened[index] = a.Get(Math.Min(index, a.Count - 1));
        }
        else
        {
            var filled = 0;
            if (count > 0)
                Append(flattened, ref filled, size, a);
            if (count > 1)
                Append(flattened, ref filled, size, b);
            if (count > 2)
                Append(flattened, ref filled, size, c);
            if (count > 3)
                Append(flattened, ref filled, size, d);
        }

        return new ShaderValue(flattened[0], flattened[1], flattened[2], flattened[3], size);
    }

    /// <summary>Appends the components of one argument to a flattened vector.</summary>
    /// <param name="target">Destination components.</param>
    /// <param name="filled">Number of components written so far.</param>
    /// <param name="limit">Component count of the constructed vector.</param>
    /// <param name="value">Argument to append.</param>
    private static void Append(Span<float> target, ref int filled, int limit, ShaderValue value)
    {
        for (var index = 0; index < value.Count && filled < limit; index++)
            target[filled++] = value.Get(index);
    }
}
