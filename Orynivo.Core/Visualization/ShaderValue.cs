namespace Orynivo.Visualization;

/// <summary>
/// A value flowing through a shader: a scalar or a vector of up to four components. HLSL shaders
/// work on <c>float</c>, <c>float2</c>, <c>float3</c>, and <c>float4</c>, so one type carries all
/// of them together with the component count; reading a component outside the count yields zero,
/// which is what the arithmetic relies on.
/// </summary>
public readonly record struct ShaderValue
{
    /// <summary>Creates a value.</summary>
    /// <param name="x">First component.</param>
    /// <param name="y">Second component.</param>
    /// <param name="z">Third component.</param>
    /// <param name="w">Fourth component.</param>
    /// <param name="count">Number of meaningful components, one to four.</param>
    /// <param name="isMatrix">
    /// Whether the value is a two-by-two matrix stored row-major in the four components instead of a
    /// vector. Milkdrop shaders construct a matrix with <c>float2x2(...)</c> and consume it with
    /// <c>mul</c>; keeping it in the same four floats avoids growing the value the per-pixel shader
    /// path copies around.
    /// </param>
    public ShaderValue(float x, float y, float z, float w, int count, bool isMatrix = false)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
        Count = Math.Clamp(count, 1, 4);
        IsMatrix = isMatrix;
    }

    /// <summary>Gets the first component.</summary>
    public float X { get; }

    /// <summary>Gets the second component.</summary>
    public float Y { get; }

    /// <summary>Gets the third component.</summary>
    public float Z { get; }

    /// <summary>Gets the fourth component.</summary>
    public float W { get; }

    /// <summary>Gets how many components are meaningful.</summary>
    public int Count { get; }

    /// <summary>Gets a value indicating whether the value is a two-by-two matrix, not a vector.</summary>
    public bool IsMatrix { get; }

    /// <summary>Gets the value as a truth value, using the first component.</summary>
    public bool IsTrue => X != 0f;

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

    /// <summary>Creates a two-by-two matrix stored row-major.</summary>
    /// <param name="m00">Row 0, column 0.</param>
    /// <param name="m01">Row 0, column 1.</param>
    /// <param name="m10">Row 1, column 0.</param>
    /// <param name="m11">Row 1, column 1.</param>
    /// <returns>The matrix value.</returns>
    public static ShaderValue Matrix2x2(float m00, float m01, float m10, float m11) =>
        new(m00, m01, m10, m11, 4, true);

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
