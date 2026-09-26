using System.Text;

namespace Orynivo.Visualization;

/// <summary>
/// Milkdrop's twenty-four <c>rot_*</c> shader matrices. The reference's <c>include.fx</c> declares
/// them as <c>float4x3</c> (four rows, three columns) and fills them from
/// <c>CState::RandomizePresetVars</c> and the per-frame matrix block in <c>milkdropfs.cpp</c>:
/// twenty-four random rotation/translation matrices, four static (<c>rot_s</c>), four slowly
/// moving (<c>rot_d</c>), four faster (<c>rot_f</c>), four very fast (<c>rot_vf</c>), four ultra
/// fast (<c>rot_uf</c>), and four that are re-randomised every frame (<c>rot_rand</c>).
/// <para>
/// A <c>float4x3</c> is a non-square matrix, which neither the interpreter's square-matrix pool nor
/// SkSL's square matrix types model. The engine therefore never materialises the type: it rewrites
/// the two constructs real presets use, the component read <c>rot_d1[1].y</c> and the product
/// <c>mul(vector, rot_d1)</c>, onto three <c>float4</c> column uniforms per matrix, which every
/// backend already understands.
/// </para>
/// <para>
/// Every preset is HLSL, so the matrix follows the reference's Direct3D row-vector convention and
/// the values are the exact ones the row-vector <c>mul</c> expects. Winamp seeds its random values
/// differently for every run, so only the shape and the time behaviour can match, not the numbers;
/// the seed is derived from the preset name so a preset looks the same across runs. The state is per
/// renderer, so two renderers in one process never share a randomised set.
/// </para>
/// </summary>
internal sealed class ShaderRotationMatrices
{
    /// <summary>
    /// The matrix names in reference order. The first twenty are the static, slow, fast, very fast,
    /// and ultra fast groups; the last four are re-randomised every frame.
    /// </summary>
    internal static readonly string[] Names =
    [
        "rot_s1", "rot_s2", "rot_s3", "rot_s4",
        "rot_d1", "rot_d2", "rot_d3", "rot_d4",
        "rot_f1", "rot_f2", "rot_f3", "rot_f4",
        "rot_vf1", "rot_vf2", "rot_vf3", "rot_vf4",
        "rot_uf1", "rot_uf2", "rot_uf3", "rot_uf4",
        "rot_rand1", "rot_rand2", "rot_rand3", "rot_rand4"
    ];

    private const int MatrixCount = 24;
    private const int RandomisedMatrices = 20;

    private readonly float[] _baseAngles = new float[RandomisedMatrices * 3];
    private readonly float[] _speeds = new float[RandomisedMatrices * 3];
    private readonly float[] _translations = new float[RandomisedMatrices * 3];

    /// <summary>The three column vectors of every matrix, flattened as <c>[matrix][column][row]</c>.</summary>
    private readonly float[] _columns = new float[MatrixCount * 12];

    private readonly float[] _left = new float[16];
    private readonly float[] _right = new float[16];
    private readonly float[] _temporary = new float[16];
    private readonly float[] _rotationA = new float[16];
    private readonly float[] _rotationB = new float[16];
    private readonly float[] _rotationC = new float[16];
    private readonly float[] _translation = new float[16];

    private uint _presetState;
    private uint _frameState;

    /// <summary>
    /// Creates the matrices for one preset and randomises them, exactly as the reference does once
    /// per preset load. The seed makes the result reproducible across runs.
    /// </summary>
    /// <param name="seed">Seed derived from the preset name.</param>
    public ShaderRotationMatrices(int seed)
    {
        _presetState = ((uint)seed) | 1u;
        _frameState = (((uint)seed) * 2654435761u) | 1u;
        RandomisePresetMatrices();
    }

    /// <summary>
    /// Computes the matrices for one frame. It runs once per frame because the four <c>rot_rand</c>
    /// matrices are re-randomised every frame, so both execution paths must read one set of columns.
    /// </summary>
    /// <param name="time">Seconds since the preset started, matching the shaders' <c>time</c>.</param>
    public void Build(double time)
    {
        var seconds = (float)time;
        for (var index = 0; index < RandomisedMatrices; index++)
        {
            BuildMatrix(
                index,
                _baseAngles[index * 3] + (_speeds[index * 3] * seconds),
                _baseAngles[(index * 3) + 1] + (_speeds[(index * 3) + 1] * seconds),
                _baseAngles[(index * 3) + 2] + (_speeds[(index * 3) + 2] * seconds),
                _translations[index * 3],
                _translations[(index * 3) + 1],
                _translations[(index * 3) + 2]);
        }

        for (var index = RandomisedMatrices; index < MatrixCount; index++)
        {
            BuildMatrix(
                index,
                NextFrameRandom() * 6.28f,
                NextFrameRandom() * 6.28f,
                NextFrameRandom() * 6.28f,
                NextFrameRandom(),
                NextFrameRandom(),
                NextFrameRandom());
        }
    }

    /// <summary>Writes the current columns into the shader uniform table.</summary>
    /// <param name="destination">Uniform table.</param>
    public void Write(IDictionary<string, ShaderValue> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        for (var index = 0; index < MatrixCount; index++)
        {
            for (var column = 0; column < 3; column++)
                destination[Names[index] + "_c" + column] = Column(Names[index], column);
        }
    }

    /// <summary>Reads one column of one matrix as a four component value.</summary>
    /// <param name="name">Matrix name.</param>
    /// <param name="column">Column index from zero to two.</param>
    /// <returns>The column, or zero when the name or column is unknown.</returns>
    public ShaderValue Column(string name, int column)
    {
        var index = Array.IndexOf(Names, name);
        if (index < 0 || column is < 0 or > 2)
            return ShaderValue.Scalar(0f);

        var offset = (index * 12) + (column * 4);
        return ShaderValue.Vector(
            _columns[offset], _columns[offset + 1], _columns[offset + 2], _columns[offset + 3], 4);
    }

    /// <summary>
    /// Rewrites the two <c>rot_*</c> constructs presets use onto the column uniforms: the component
    /// read <c>rot_NAME[row].component</c> becomes a column component, and the product
    /// <c>mul(vector, rot_NAME)</c> becomes the helper call. Anything it does not recognise is left
    /// untouched, so the shader fails exactly where it did before rather than silently changing.
    /// </summary>
    /// <param name="source">Shader source.</param>
    /// <returns>The rewritten source.</returns>
    internal static string Rewrite(string source)
    {
        if (string.IsNullOrEmpty(source) || source.IndexOf("rot_", StringComparison.Ordinal) < 0)
            return source;

        var builder = new StringBuilder(source.Length + 64);
        var index = 0;
        while (index < source.Length)
        {
            var start = source.IndexOf("rot_", index, StringComparison.Ordinal);
            if (start < 0)
            {
                builder.Append(source, index, source.Length - index);
                break;
            }

            if (!TryReadName(source, start, out var name, out var afterName))
            {
                builder.Append(source, index, (start + 4) - index);
                index = start + 4;
                continue;
            }

            // The product replacement has to drop the "mul" name, which sits before the first
            // argument, so it takes the untouched source range and appends it itself.
            if (TryRewriteProduct(source, index, name, start, afterName, builder, out var productEnd))
            {
                index = productEnd;
                continue;
            }

            builder.Append(source, index, start - index);
            if (TryRewriteComponent(source, name, start, afterName, builder, out var componentEnd))
            {
                index = componentEnd;
                continue;
            }

            builder.Append(source, start, afterName - start);
            index = afterName;
        }

        return builder.ToString();
    }

    /// <summary>Rewrites <c>rot_NAME[row].component</c> to a column component.</summary>
    /// <param name="source">Shader source.</param>
    /// <param name="name">Matrix name.</param>
    /// <param name="start">Index of the name.</param>
    /// <param name="afterName">Index just past the name.</param>
    /// <param name="builder">Rewrite target.</param>
    /// <param name="end">Index just past the rewritten construct.</param>
    /// <returns><see langword="true"/> when the construct was recognised.</returns>
    private static bool TryRewriteComponent(
        string source, string name, int start, int afterName, StringBuilder builder, out int end)
    {
        end = start;
        if (afterName >= source.Length || source[afterName] != '[')
            return false;

        var rowStart = afterName + 1;
        var rowEnd = source.IndexOf(']', rowStart);
        if (rowEnd < 0 || rowEnd - rowStart > 2)
            return false;

        var rowText = source.AsSpan(rowStart, rowEnd - rowStart).Trim();
        if (rowText.Length != 1 || rowText[0] is < '0' or > '3')
            return false;

        var dot = rowEnd + 1;
        if (dot >= source.Length || source[dot] != '.' || dot + 1 >= source.Length)
            return false;

        var component = source[dot + 1];
        var columnIndex = component switch
        {
            'x' or 'r' => 0,
            'y' or 'g' => 1,
            'z' or 'b' => 2,
            'w' or 'a' => 3,
            _ => -1
        };

        if (columnIndex < 0)
            return false;

        builder.Append(name).Append("_c").Append(columnIndex).Append('[').Append(rowText[0]).Append(']');
        end = dot + 2;
        return true;
    }

    /// <summary>Rewrites <c>mul(vector, rot_NAME)</c>, optionally negated, to the column product.</summary>
    /// <param name="source">Shader source.</param>
    /// <param name="sourceIndex">Index just past the text already emitted.</param>
    /// <param name="name">Matrix name.</param>
    /// <param name="start">Index of the name.</param>
    /// <param name="afterName">Index just past the name.</param>
    /// <param name="builder">Rewrite target.</param>
    /// <param name="end">Index just past the rewritten construct.</param>
    /// <returns><see langword="true"/> when the construct was recognised.</returns>
    private static bool TryRewriteProduct(
        string source, int sourceIndex, string name, int start, int afterName, StringBuilder builder, out int end)
    {
        end = start;

        // The product needs the closed call to end right after the name: "mul(a, rot_d1)".
        var close = afterName;
        while (close < source.Length && char.IsWhiteSpace(source[close]))
            close++;

        if (close >= source.Length || source[close] != ')')
            return false;

        // Walk back from the name over an optional unary minus and whitespace to the comma that
        // separates the two arguments of "mul".
        var cursor = start - 1;
        while (cursor >= 0 && char.IsWhiteSpace(source[cursor]))
            cursor--;

        var negated = false;
        if (cursor >= 0 && source[cursor] == '-')
        {
            negated = true;
            cursor--;
            while (cursor >= 0 && char.IsWhiteSpace(source[cursor]))
                cursor--;
        }

        if (cursor < 0 || source[cursor] != ',')
            return false;

        var argumentEnd = cursor;

        // Match back over the first argument to the opening parenthesis of "mul(".
        var depth = 0;
        cursor--;
        while (cursor >= 0)
        {
            var character = source[cursor];
            if (character == ')')
                depth++;
            else if (character == '(')
            {
                if (depth == 0)
                    break;
                depth--;
            }

            cursor--;
        }

        if (cursor < 0 || !IsMulName(source, cursor, out var mulStart))
            return false;

        // Emit everything before the call, then the helper call with the first argument unchanged and
        // the matrix replaced by its three columns.
        builder.Append(source, sourceIndex, mulStart - sourceIndex);
        builder.Append("orynivo_mul4x3(");
        builder.Append(source, cursor + 1, argumentEnd - (cursor + 1));
        AppendColumns(builder, name, negated);
        builder.Append(')');
        end = close + 1;
        return true;
    }

    /// <summary>Appends the three column uniforms of a matrix, optionally negated.</summary>
    /// <param name="builder">Rewrite target.</param>
    /// <param name="name">Matrix name.</param>
    /// <param name="negated">Whether the source writes <c>-rot_NAME</c>.</param>
    private static void AppendColumns(StringBuilder builder, string name, bool negated)
    {
        for (var column = 0; column < 3; column++)
        {
            builder.Append(", ");
            if (negated)
                builder.Append('-');
            builder.Append(name).Append("_c").Append(column);
        }
    }

    /// <summary>Checks whether the text before an opening parenthesis spells the <c>mul</c> function.</summary>
    /// <param name="source">Shader source.</param>
    /// <param name="open">Index of the opening parenthesis.</param>
    /// <param name="nameStart">Index of the first character of the <c>mul</c> name when it matches.</param>
    /// <returns><see langword="true"/> when the call is <c>mul</c>.</returns>
    private static bool IsMulName(string source, int open, out int nameStart)
    {
        nameStart = 0;
        var before = open - 1;
        while (before >= 0 && char.IsWhiteSpace(source[before]))
            before--;

        if (before < 2 || !source.AsSpan(before - 2, 3).Equals("mul", StringComparison.Ordinal))
            return false;

        // Reject a longer identifier that merely ends in "mul", such as "amul".
        var prefix = before - 3;
        if (prefix >= 0 && (char.IsLetterOrDigit(source[prefix]) || source[prefix] == '_'))
            return false;

        nameStart = before - 2;
        return true;
    }

    /// <summary>Reads one of the twenty-four names at an index.</summary>
    /// <param name="source">Shader source.</param>
    /// <param name="start">Index of the <c>rot_</c> prefix.</param>
    /// <param name="name">Matched name.</param>
    /// <param name="afterName">Index just past the name.</param>
    /// <returns><see langword="true"/> when a name matched.</returns>
    private static bool TryReadName(string source, int start, out string name, out int afterName)
    {
        foreach (var candidate in Names)
        {
            if (start + candidate.Length <= source.Length
                && source.AsSpan(start, candidate.Length).Equals(candidate, StringComparison.Ordinal))
            {
                name = candidate;
                afterName = start + candidate.Length;
                return true;
            }
        }

        name = string.Empty;
        afterName = start;
        return false;
    }

    /// <summary>Fills the twenty rotating matrices for the current preset.</summary>
    private void RandomisePresetMatrices()
    {
        for (var index = 0; index < RandomisedMatrices; index++)
        {
            // The reference scales the speed by 0.9 * (k / 8)^3.2, so the first group is nearly
            // static and the last group is very fast.
            var multiplier = 0.9f * MathF.Pow(index / 8f, 3.2f);
            _translations[index * 3] = (NextRandom() * 2f) - 1f;
            _translations[(index * 3) + 1] = (NextRandom() * 2f) - 1f;
            _translations[(index * 3) + 2] = (NextRandom() * 2f) - 1f;
            _baseAngles[index * 3] = NextRandom() * 6.28f;
            _baseAngles[(index * 3) + 1] = NextRandom() * 6.28f;
            _baseAngles[(index * 3) + 2] = NextRandom() * 6.28f;
            _speeds[index * 3] = ((NextRandom() * 2f) - 1f) * multiplier;
            _speeds[(index * 3) + 1] = ((NextRandom() * 2f) - 1f) * multiplier;
            _speeds[(index * 3) + 2] = ((NextRandom() * 2f) - 1f) * multiplier;
        }
    }

    /// <summary>Builds one matrix and stores its three columns.</summary>
    /// <param name="index">Matrix index.</param>
    /// <param name="rotationX">X rotation angle.</param>
    /// <param name="rotationY">Y rotation angle.</param>
    /// <param name="rotationZ">Z rotation angle.</param>
    /// <param name="translateX">X translation.</param>
    /// <param name="translateY">Y translation.</param>
    /// <param name="translateZ">Z translation.</param>
    private void BuildMatrix(
        int index,
        float rotationX,
        float rotationY,
        float rotationZ,
        float translateX,
        float translateY,
        float translateZ)
    {
        RotationX(rotationX, _rotationA);
        Translation(translateX, translateY, translateZ, _translation);
        RotationZ(rotationZ, _rotationB);
        RotationY(rotationY, _rotationC);

        // The reference composes row-vector Direct3D matrices: D3DXMatrixMultiply(&temp, &mx, &mxlate)
        // then temp * mz then temp * my, which is Rx * T * Rz * Ry.
        Multiply(_rotationA, _translation, _temporary);
        Multiply(_temporary, _rotationB, _left);
        Multiply(_left, _rotationC, _right);

        for (var column = 0; column < 3; column++)
        {
            var offset = (index * 12) + (column * 4);
            _columns[offset] = _right[column];
            _columns[offset + 1] = _right[4 + column];
            _columns[offset + 2] = _right[8 + column];
            _columns[offset + 3] = _right[12 + column];
        }
    }

    /// <summary>Builds a row-vector Direct3D X rotation.</summary>
    /// <param name="angle">Angle in radians.</param>
    /// <param name="matrix">Destination, row major.</param>
    private static void RotationX(float angle, float[] matrix)
    {
        Identity(matrix);
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        matrix[5] = cos;
        matrix[6] = sin;
        matrix[9] = -sin;
        matrix[10] = cos;
    }

    /// <summary>Builds a row-vector Direct3D Y rotation.</summary>
    /// <param name="angle">Angle in radians.</param>
    /// <param name="matrix">Destination, row major.</param>
    private static void RotationY(float angle, float[] matrix)
    {
        Identity(matrix);
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        matrix[0] = cos;
        matrix[2] = -sin;
        matrix[8] = sin;
        matrix[10] = cos;
    }

    /// <summary>Builds a row-vector Direct3D Z rotation.</summary>
    /// <param name="angle">Angle in radians.</param>
    /// <param name="matrix">Destination, row major.</param>
    private static void RotationZ(float angle, float[] matrix)
    {
        Identity(matrix);
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        matrix[0] = cos;
        matrix[1] = sin;
        matrix[4] = -sin;
        matrix[5] = cos;
    }

    /// <summary>Builds a row-vector Direct3D translation.</summary>
    /// <param name="x">X translation.</param>
    /// <param name="y">Y translation.</param>
    /// <param name="z">Z translation.</param>
    /// <param name="matrix">Destination, row major.</param>
    private static void Translation(float x, float y, float z, float[] matrix)
    {
        Identity(matrix);
        matrix[12] = x;
        matrix[13] = y;
        matrix[14] = z;
    }

    /// <summary>Resets a matrix to the identity.</summary>
    /// <param name="matrix">Matrix to reset.</param>
    private static void Identity(float[] matrix)
    {
        Array.Clear(matrix);
        matrix[0] = 1f;
        matrix[5] = 1f;
        matrix[10] = 1f;
        matrix[15] = 1f;
    }

    /// <summary>Multiplies two row-major four-by-four matrices.</summary>
    /// <param name="left">Left matrix.</param>
    /// <param name="right">Right matrix.</param>
    /// <param name="result">Destination matrix.</param>
    private static void Multiply(float[] left, float[] right, float[] result)
    {
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                var sum = 0f;
                for (var inner = 0; inner < 4; inner++)
                    sum += left[(row * 4) + inner] * right[(inner * 4) + column];
                result[(row * 4) + column] = sum;
            }
        }
    }

    /// <summary>Advances the per-preset random sequence.</summary>
    /// <returns>A value in zero..one.</returns>
    private float NextRandom()
    {
        _presetState = (_presetState * 1664525u) + 1013904223u;
        return (_presetState >> 8) * (1f / 16777216f);
    }

    /// <summary>Advances the per-frame random sequence used by <c>rot_rand</c>.</summary>
    /// <returns>A value in zero..one.</returns>
    private float NextFrameRandom()
    {
        _frameState = (_frameState * 1103515245u) + 12345u;
        return (_frameState >> 8) * (1f / 16777216f);
    }
}
