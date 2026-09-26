namespace Orynivo.Visualization;

/// <summary>
/// One vertex of a GPU custom-wave geometry. The position is in the engine's minus-one-to-one
/// top-down overlay space, the colour is the raw (not premultiplied) channel value.
/// </summary>
/// <param name="X">Horizontal position, minus one to one.</param>
/// <param name="Y">Vertical position, minus one to one.</param>
/// <param name="Red">Red, zero to one.</param>
/// <param name="Green">Green, zero to one.</param>
/// <param name="Blue">Blue, zero to one.</param>
/// <param name="Alpha">Alpha, zero to one.</param>
public readonly record struct WaveVertex(
    float X,
    float Y,
    float Red,
    float Green,
    float Blue,
    float Alpha);

/// <summary>
/// One custom waveform a GPU overlay can draw. The vertices form a triangle list expanded from the
/// smoothed polyline, so the CPU no longer rasterizes the thick lines and dots per pixel. That is the
/// cost that made a high-resolution visualizer with a 512-sample wave render only a few frames.
/// </summary>
/// <param name="Vertices">Triangle-list vertices.</param>
/// <param name="Additive">Whether the geometry adds to the frame instead of blending over it.</param>
public sealed record WaveGeometry(
    IReadOnlyList<WaveVertex> Vertices,
    bool Additive);
