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
/// <param name="AlwaysShowOverlay">
/// When <see langword="true"/> the title, hint, and playback buttons are always visible;
/// otherwise they appear only while the mouse moves over the visualizer.
/// </param>
/// <param name="PresetDirectory">
/// Folder to load user presets from, or <see langword="null"/> for the default folder.
/// </param>
/// <param name="AutoAdvanceEnabled">Whether the window advances to the next preset automatically.</param>
/// <param name="AutoAdvanceSeconds">Seconds to show one preset before advancing.</param>
public sealed record VisualizerRenderOptions(
    int Width,
    int Height,
    int FrameRate,
    bool ReduceMotion,
    bool AlwaysShowOverlay,
    string? PresetDirectory,
    bool AutoAdvanceEnabled = false,
    int AutoAdvanceSeconds = 15);

/// <summary>A selectable visualizer frame size.</summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
public sealed record VisualizerResolution(int Width, int Height);
