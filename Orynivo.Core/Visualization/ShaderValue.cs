namespace Orynivo.Visualization;

/// <summary>
/// A value flowing through a shader: a scalar, a vector of up to four components, or a handle to a
/// square matrix up to four by four. HLSL shaders work on <c>float</c>, <c>float2</c>, <c>float3</c>,
/// <c>float4</c>, and the <c>floatNxN</c> matrices, so one type carries all of them together with the
/// component count; reading a component outside the count yields zero, which is what the arithmetic
/// relies on.
/// <para>
/// A matrix needs nine or sixteen components, which would make every value four times larger and
/// slow the per-pixel shader path for the 99 percent of presets that never build one. It is therefore
/// kept in the runtime's per-pixel pool and the value carries only its index and dimension; see
/// <see cref="ShaderRuntime.StoreMatrix"/>.
/// </para>
/// </summary>
public readonly struct ShaderValue
{
    /// <summary>Creates a scalar or vector value.</summary>
    /// <param name="x">First component.</param>
    /// <param name="y">Second component.</param>
    /// <param name="z">Third component.</param>
    /// <param name="w">Fourth component.</param>
    /// <param name="count">Number of meaningful components, one to four.</param>
    /// <param name="isMatrix">Whether the value is a matrix handle instead of a vector.</param>
    public ShaderValue(float x, float y, float z, float w, int count, bool isMatrix = false)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
        Count = Math.Clamp(count, 1, 4);
        IsMatrix = isMatrix;
    }

    /// <summary>Gets the first component, or a matrix handle's pool index.</summary>
    public float X { get; }

    /// <summary>Gets the second component, or a matrix handle's dimension.</summary>
    public float Y { get; }

    /// <summary>Gets the third component.</summary>
    public float Z { get; }

    /// <summary>Gets the fourth component.</summary>
    public float W { get; }

    /// <summary>Gets how many components are meaningful.</summary>
    public int Count { get; }

    /// <summary>Gets a value indicating whether the value is a matrix handle, not a vector.</summary>
    public bool IsMatrix { get; }

    /// <summary>Gets the value as a truth value, using the first component.</summary>
    public bool IsTrue => X != 0f;

    /// <summary>Gets the matrix pool index, or zero when the value is not a matrix.</summary>
    internal int MatrixIndex => IsMatrix ? (int)X : 0;

    /// <summary>Gets the matrix dimension, or zero when the value is not a matrix.</summary>
    internal int MatrixDimension => IsMatrix ? (int)Y : 0;

    /// <summary>Creates a scalar value.</summary>
    /// <param name="value">Scalar value.</param>
    /// <returns>The scalar.</returns>
    public static ShaderValue Scalar(float value) => new(value, value, value, value, 1);

    /// <summary>Creates a vector value, repeating the last component for the missing ones.</summary>
    /// <param name="x">First component.</param>
    /// <param name="y">Second component.</param>
    /// <param name="z">Third component.</param>
    /// <param name="w">Fourth component.</param>
    /// <param name="count">Number of components.</param>
    /// <returns>The vector.</returns>
    public static ShaderValue Vector(float x, float y, float z, float w, int count) => new(x, y, z, w, count);

    /// <summary>Creates a matrix handle from its pool index and dimension.</summary>
    /// <param name="index">Pool index.</param>
    /// <param name="dimension">Matrix dimension.</param>
    /// <returns>The matrix value.</returns>
    internal static ShaderValue MatrixHandle(int index, int dimension) =>
        new(index, dimension, 0f, 0f, 4, true);

    /// <summary>Reads one component, or zero when the value is shorter.</summary>
    /// <param name="index">Component index from zero to three.</param>
    /// <returns>The component value.</returns>
    public float Get(int index) => index switch
    {
        0 => X,
        1 => Y,
        2 => Z,
        3 => W,
        _ => 0f
    };

    /// <summary>Returns a copy with one component replaced.</summary>
    /// <param name="index">Component index from zero to three.</param>
    /// <param name="value">New component value.</param>
    /// <returns>The updated value.</returns>
    public ShaderValue With(int index, float value)
    {
        var components = new[] { X, Y, Z, W };
        if (index < 0 || index > 3)
            return this;

        components[index] = value;
        return new ShaderValue(components[0], components[1], components[2], components[3], Math.Max(Count, index + 1));
    }
}
