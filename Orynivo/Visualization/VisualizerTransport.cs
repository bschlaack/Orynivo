namespace Orynivo.Visualization;

/// <summary>
/// Callbacks that let the visualizer window drive the main window's playback and mirror its
/// now-playing text. They reuse the normal transport methods, so the overlay buttons behave
/// exactly like the ones in the transport bar.
/// </summary>
/// <param name="Previous">Starts the previous queue item.</param>
/// <param name="PlayPause">Toggles play and pause.</param>
/// <param name="Next">Starts the next queue item.</param>
/// <param name="IsPlaying">Reports whether audio is currently playing.</param>
/// <param name="NowPlaying">Returns the title and artist of the currently audible item.</param>
public sealed record VisualizerTransport(
    Action Previous,
    Action PlayPause,
    Action Next,
    Func<bool> IsPlaying,
    Func<(string? Title, string? Artist)> NowPlaying);
