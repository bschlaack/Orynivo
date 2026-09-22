using System.Globalization;

namespace Orynivo.Visualization;

/// <summary>
/// The SkSL knowledge shared by the two front ends of the GPU path. <see cref="ShaderTranspiler"/>
/// translates Milkdrop's HLSL subset, while <see cref="PresetExpressionTranspiler"/> translates the
/// scalar preset expression language; both need the same naming and conversion rules, so they live
/// here instead of in either emitter. SkSL is strictly typed while the engine stores values as
/// floats, so the conversion helpers below are what keep a generated program valid.
/// </summary>
internal static class SkSL
{
    /// <summary>Names SkSL reserves, which a preset variable has to avoid.</summary>
    public static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "output", "input", "main"
    };

    /// <summary>Built-in functions that keep their name in SkSL.</summary>
    public static readonly HashSet<string> DirectFunctions = new(StringComparer.Ordinal)
    {
        "abs", "acos", "asin", "atan", "ceil", "clamp", "cos", "cross", "degrees", "distance",
        "dot", "exp", "exp2", "floor", "length", "log", "log2", "max", "min", "mix", "normalize",
        "pow", "radians", "reflect", "refract", "sign", "sin", "smoothstep", "sqrt", "step", "tan"
    };

    /// <summary>Built-in functions whose SkSL spelling differs.</summary>
    public static readonly Dictionary<string, string> RenamedFunctions = new(StringComparer.Ordinal)
    {
        ["frac"] = "fract",
        ["fmod"] = "mod",
        ["rsqrt"] = "inversesqrt"
    };

    /// <summary>Renames a variable whose name SkSL reserves.</summary>
    /// <param name="name">Declared name.</param>
    /// <returns>The name to emit.</returns>
    public static string SafeName(string name) => ReservedNames.Contains(name) ? "_orynivo_" + name : name;

    /// <summary>The type name for a component count.</summary>
    /// <param name="count">Component count.</param>
    /// <returns>The SkSL type name.</returns>
    public static string ComponentType(int count) => count switch
    {
        <= 1 => "float",
        2 => "float2",
        3 => "float3",
        _ => "float4"
    };

    /// <summary>Counts the components of a mapped type name.</summary>
    /// <param name="type">Type name.</param>
    /// <returns>The component count.</returns>
    public static int ComponentCount(string type)
    {
        foreach (var character in type)
        {
            if (character is '2' or '3' or '4')
                return character - '0';
        }

        return 1;
    }

    /// <summary>Returns the wider of two types, or nothing when either is unknown.</summary>
    /// <param name="left">First type.</param>
    /// <param name="right">Second type.</param>
    /// <returns>The wider type name, or <see langword="null"/>.</returns>
    public static string? Wider(string? left, string? right)
    {
        if (left is null || right is null)
            return null;

        return ComponentCount(left) >= ComponentCount(right) ? left : right;
    }

    /// <summary>
    /// Converts an expression to a target type. SkSL has no implicit conversion between a scalar and
    /// a vector, so a value is widened with a constructor or narrowed with a swizzle; an unknown type
    /// is left alone rather than guessed at.
    /// </summary>
    /// <param name="text">Expression text.</param>
    /// <param name="from">Its inferred type, or nothing.</param>
    /// <param name="to">The type it has to become.</param>
    /// <returns>The converted text.</returns>
    public static string Convert(string text, string? from, string to)
    {
        if (from is null || from == to)
            return text;

        var source = ComponentCount(from);
        var target = ComponentCount(to);
        if (source == target)
            return text;

        if (target > source)
        {
            // A scalar broadcasts, because ShaderValue.Scalar stores the same value in all four
            // components, so the single-argument constructor is right for it. A vector does not: its
            // missing components read as zero, so it is padded with zeros. Treating both the same way
            // turned "colour * 0.5" into "colour * float3(0.5, 0, 0)", which the CPU/GPU comparison
            // test caught three times before the cause was found.
            if (source == 1)
                return $"{to}({text})";

            var fill = string.Join(", ", Enumerable.Repeat("0.0", target - source));
            return $"{to}({text}, {fill})";
        }

        return $"{text}.{"xyzw"[..target]}";
    }

    /// <summary>Maps a Milkdrop type name onto its SkSL spelling.</summary>
    /// <param name="type">Type name.</param>
    /// <returns>The SkSL type name.</returns>
    public static string MapType(string type)
    {
        if (type.StartsWith("half", StringComparison.Ordinal))
            return MapType("float" + type[4..]);
        if (type.StartsWith("double", StringComparison.Ordinal))
            return MapType("float" + type[6..]);

        // The engine stores every value as a float, so integer and boolean variables become floats.
        // SkSL is strictly typed, and "n < 4" with an int and a float is a compile error.
        foreach (var prefix in new[] { "int", "uint", "bool" })
        {
            if (type.StartsWith(prefix, StringComparison.Ordinal))
                return "float" + type[prefix.Length..];
        }

        // A Milkdrop matrix is a SkSL matrix. SkSL spells a two-by-two matrix mat2, not float2x2.
        if (IsMatrixType(type))
            return "mat" + MatrixDimension(type);

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

    /// <summary>Reports whether a declared type name is a matrix.</summary>
    /// <param name="type">Type name.</param>
    /// <returns><see langword="true"/> when the type is a matrix.</returns>
    public static bool IsMatrixType(string type)
    {
        // The SkSL spelling, which the emitter infers for a matrix constructor.
        if (type is "mat2" or "mat3" or "mat4")
            return true;

        foreach (var prefix in new[] { "float", "half", "double" })
        {
            if (!type.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var suffix = type[prefix.Length..];
            return suffix.Length == 3 && suffix[1] == 'x' &&
                   suffix[0] is '2' or '3' or '4' && suffix[2] == suffix[0];
        }

        return false;
    }

    /// <summary>Gets the dimension of a matrix type name.</summary>
    /// <param name="type">Matrix type name.</param>
    /// <returns>The dimension, or two when the name is not a square matrix.</returns>
    public static int MatrixDimension(string type)
    {
        foreach (var character in type)
        {
            if (character is '2' or '3' or '4')
                return character - '0';
        }

        return 2;
    }

    /// <summary>Reports whether a name is a vector or matrix constructor.</summary>
    /// <param name="name">Function name.</param>
    /// <returns><see langword="true"/> when the name is a constructor.</returns>
    public static bool IsVectorConstructor(string name) => name.Length >= 6 &&
        (name.StartsWith("float", StringComparison.Ordinal) ||
         name.StartsWith("half", StringComparison.Ordinal) ||
         name.StartsWith("double", StringComparison.Ordinal) ||
         name.StartsWith("int", StringComparison.Ordinal) ||
         name.StartsWith("uint", StringComparison.Ordinal) ||
         name.StartsWith("bool", StringComparison.Ordinal));

    /// <summary>Builds the indentation of one depth level.</summary>
    /// <param name="depth">Indentation depth.</param>
    /// <returns>The indentation text.</returns>
    public static string Indent(int depth) => new(' ', depth * 4);

    /// <summary>Formats a float the way the shader emitters write literals.</summary>
    /// <param name="value">Value to format.</param>
    /// <returns>The invariant literal text.</returns>
    public static string Literal(float value) => value.ToString("0.0########", CultureInfo.InvariantCulture);
}
