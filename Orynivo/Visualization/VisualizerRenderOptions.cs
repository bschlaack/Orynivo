namespace Orynivo.Visualization;

/// <summary>
/// Persisted visualizer render settings. The window renders at this resolution and lets the
/// image control scale the frame up, so a smaller value keeps the CPU cost bounded.
/// </summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="FrameRate">Target frames per second.</param>
/// <param name="ReduceMotion">
/// When <see langword="true"/> the picture shows a static spectrum instead of animating.
/// </param>
/// <param name="PresetDirectory">
/// Folder to load user presets from, or <see langword="null"/> for the default folder.
/// </param>
public sealed record VisualizerRenderOptions(
    int Width,
    int Height,
    int FrameRate,
    bool ReduceMotion,
    string? PresetDirectory);

/// <summary>A selectable visualizer frame size.</summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
public sealed record VisualizerResolution(int Width, int Height);
