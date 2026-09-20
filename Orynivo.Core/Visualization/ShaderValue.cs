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
    public ShaderValue(float x, float y, float z, float w, int count)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
        Count = Math.Clamp(count, 1, 4);
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
