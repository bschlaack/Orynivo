namespace Orynivo.Visualization;

/// <summary>
/// One vertex of a GPU shape fill. The position is in the engine's minus-one-to-one space, the colour
/// is the raw (not premultiplied) channel value, and the texture coordinate is the shape's fan
/// coordinate, which only a textured fill reads.
/// </summary>
/// <param name="X">Horizontal position, minus one to one.</param>
/// <param name="Y">Vertical position, minus one to one.</param>
/// <param name="Red">Red, zero to one.</param>
/// <param name="Green">Green, zero to one.</param>
/// <param name="Blue">Blue, zero to one.</param>
/// <param name="Alpha">Alpha, zero to one.</param>
/// <param name="U">Horizontal texture coordinate.</param>
/// <param name="V">Vertical texture coordinate.</param>
public readonly record struct ShapeFillVertex(
    float X,
    float Y,
    float Red,
    float Green,
    float Blue,
    float Alpha,
    float U,
    float V);

/// <summary>
/// One shape fill a GPU overlay can draw: a triangle fan whose first vertex is the fan centre. It is
/// the geometry <c>PresetRenderer.FillShapeFan</c> rasterizes, published so the same fill can run on
/// the GPU. The centre vertex carries the shape's second colour and the rim vertices its first, which
/// is exactly the interpolation the CPU applies.
/// </summary>
/// <param name="Vertices">Fan vertices, centre first.</param>
/// <param name="Textured">Whether the fill samples the frame instead of interpolating the colours.</param>
/// <param name="Additive">Whether the fill adds to the frame instead of blending over it.</param>
public sealed record ShapeFill(
    IReadOnlyList<ShapeFillVertex> Vertices,
    bool Textured,
    bool Additive);
