namespace Orynivo.Visualization;

/// <summary>
/// The expression programs of one of the four Milkdrop waveforms. Each waveform has its own
/// initialisation, per-frame, and per-point block; the drawing itself lives in the renderer.
/// </summary>
/// <param name="Init">One-time initialisation block.</param>
/// <param name="PerFrame">Block that runs once per frame before the waveform is drawn.</param>
/// <param name="PerPoint">Block that may move every point by writing <c>x</c> and <c>y</c>.</param>
public sealed record VisualizerWave(
    PresetProgram Init,
    PresetProgram PerFrame,
    PresetProgram PerPoint);
