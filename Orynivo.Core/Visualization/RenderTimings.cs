namespace Orynivo.Visualization;

/// <summary>
/// The cost of one rendered frame, split by stage and measured in milliseconds. The stages are
/// consecutive, so their sum is at most <see cref="Total"/>; the remainder is the frame
/// bookkeeping around them. A warp shader runs inside the per-pixel loop, so its cost is part of
/// <see cref="Warp"/>: timing it per pixel would cost more than the measurement is worth.
/// </summary>
/// <param name="Warp">Feedback warp, including a warp shader when the preset has one.</param>
/// <param name="Blur">Box-blur passes.</param>
/// <param name="PostProcess">Feedback fade, video echo, centre darkening, borders, and gamma.</param>
/// <param name="Overlay">Waveforms, spectrum, motion vectors, and shapes.</param>
/// <param name="Composite">Combining the faded feedback with the freshly drawn overlay.</param>
/// <param name="Shader">Comp shaders, which run over the composited frame.</param>
/// <param name="Total">The complete frame, including the stages above.</param>
public readonly record struct RenderTimings(
    double Warp,
    double Blur,
    double PostProcess,
    double Overlay,
    double Composite,
    double Shader,
    double Total)
{
    /// <summary>Gets the sum of the individually measured stages.</summary>
    public double Measured => Warp + Blur + PostProcess + Overlay + Composite + Shader;
}
