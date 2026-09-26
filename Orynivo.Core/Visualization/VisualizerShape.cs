namespace Orynivo.Visualization;

/// <summary>
/// One custom shape of a preset: a regular polygon whose default placement, colours, and
/// vertex count come from <c>shapecode_N_*</c> (or legacy <c>shape_N_*</c>) keys and whose per-frame and per-point
/// expression blocks may override them at runtime. The shape object itself stays immutable;
/// the renderer maintains an isolated equation context for each shape.
/// </summary>
public sealed class VisualizerShape
{
    /// <summary>Gets whether the shape is enabled.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Gets whether positions use Milkdrop's zero-to-one coordinate contract.</summary>
    public bool MilkdropCoordinates { get; init; }
    /// <summary>Gets the number of separately evaluated instances.</summary>
    public int Instances { get; init; } = 1;
    /// <summary>Gets the edge red component.</summary>
    public float Red2 { get; init; } = 1f;
    /// <summary>Gets the edge green component.</summary>
    public float Green2 { get; init; } = 1f;
    /// <summary>Gets the edge blue component.</summary>
    public float Blue2 { get; init; } = 1f;
    /// <summary>Gets the edge opacity.</summary>
    public float Alpha2 { get; init; }
    /// <summary>Gets whether the preset requests feedback texturing (currently unsupported by the overlay rasterizer).</summary>
    public bool Textured { get; init; }
    /// <summary>Gets the texture zoom.</summary>
    public float TextureZoom { get; init; } = 1f;
    /// <summary>Gets the texture rotation in radians.</summary>
    public float TextureAngle { get; init; }
    /// <summary>Gets whether the outline is two pixels thick.</summary>
    public bool ThickOutline { get; init; }
    /// <summary>Creates a shape from its parsed defaults.</summary>
    /// <param name="sides">Vertex count; values below three draw a filled circle.</param>
    /// <param name="x">Default horizontal position in the range -1 to 1.</param>
    /// <param name="y">Default vertical position in the range -1 to 1.</param>
    /// <param name="radius">Default radius in the range 0 to 1.</param>
    /// <param name="angle">Default rotation in radians.</param>
    /// <param name="red">Default fill red.</param>
    /// <param name="green">Default fill green.</param>
    /// <param name="blue">Default fill blue.</param>
    /// <param name="alpha">Default fill opacity.</param>
    /// <param name="borderRed">Default border red.</param>
    /// <param name="borderGreen">Default border green.</param>
    /// <param name="borderBlue">Default border blue.</param>
    /// <param name="borderAlpha">Default border opacity.</param>
    /// <param name="additive">Whether the shape is added instead of alpha-blended.</param>
    /// <param name="init">One-time initialisation program, or an empty program.</param>
    /// <param name="perFrame">Per-frame program, or an empty program.</param>
    /// <param name="perPoint">Per-vertex program, or an empty program.</param>
    public VisualizerShape(
        int sides,
        float x,
        float y,
        float radius,
        float angle,
        float red,
        float green,
        float blue,
        float alpha,
        float borderRed,
        float borderGreen,
        float borderBlue,
        float borderAlpha,
        bool additive,
        PresetProgram init,
        PresetProgram perFrame,
        PresetProgram perPoint)
    {
        Sides = sides;
        X = x;
        Y = y;
        Radius = radius;
        Angle = angle;
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
        BorderRed = borderRed;
        BorderGreen = borderGreen;
        BorderBlue = borderBlue;
        BorderAlpha = borderAlpha;
        Additive = additive;
        Init = init ?? PresetProgram.Empty;
        PerFrame = perFrame ?? PresetProgram.Empty;
        PerPoint = perPoint ?? PresetProgram.Empty;
    }

    /// <summary>Gets the vertex count; values below three draw a filled circle.</summary>
    public int Sides { get; }

    /// <summary>Gets the default horizontal position in the range -1 to 1.</summary>
    public float X { get; }

    /// <summary>Gets the default vertical position in the range -1 to 1.</summary>
    public float Y { get; }

    /// <summary>Gets the default radius in the range 0 to 1.</summary>
    public float Radius { get; }

    /// <summary>Gets the default rotation in radians.</summary>
    public float Angle { get; }

    /// <summary>Gets the default fill red.</summary>
    public float Red { get; }

    /// <summary>Gets the default fill green.</summary>
    public float Green { get; }

    /// <summary>Gets the default fill blue.</summary>
    public float Blue { get; }

    /// <summary>Gets the default fill opacity.</summary>
    public float Alpha { get; }

    /// <summary>Gets the default border red.</summary>
    public float BorderRed { get; }

    /// <summary>Gets the default border green.</summary>
    public float BorderGreen { get; }

    /// <summary>Gets the default border blue.</summary>
    public float BorderBlue { get; }

    /// <summary>Gets the default border opacity.</summary>
    public float BorderAlpha { get; }

    /// <summary>Gets a value indicating whether the shape is added instead of alpha-blended.</summary>
    public bool Additive { get; }

    /// <summary>Gets the one-time initialisation program of this shape.</summary>
    public PresetProgram Init { get; }

    /// <summary>Gets the per-frame program that may override the placement and colours.</summary>
    public PresetProgram PerFrame { get; }

    /// <summary>Gets the per-vertex program that may override each point and its colour.</summary>
    public PresetProgram PerPoint { get; }
}
