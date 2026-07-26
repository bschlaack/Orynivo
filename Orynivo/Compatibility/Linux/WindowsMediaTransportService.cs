using Windows.Media;

namespace Orynivo;

/// <summary>
/// No-op system-media integration used by non-Windows desktop builds.
/// </summary>
internal sealed class WindowsMediaTransportService : IDisposable
{
    /// <summary>Would be raised for a platform play request.</summary>
    internal event Action? PlayRequested;
    /// <summary>Would be raised for a platform pause request.</summary>
    internal event Action? PauseRequested;
    /// <summary>Would be raised for a platform previous-item request.</summary>
    internal event Action? PreviousRequested;
    /// <summary>Would be raised for a platform next-item request.</summary>
    internal event Action? NextRequested;
    /// <summary>Would be raised for a platform stop request.</summary>
    internal event Action? StopRequested;
    /// <summary>Would be raised for a platform seek request.</summary>
    internal event Action<TimeSpan>? PositionChangeRequested;

    /// <summary>Returns no service because SMTC is a Windows-only facility.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    internal static WindowsMediaTransportService? TryCreate() => null;

    /// <summary>Ignores metadata on platforms without SMTC.</summary>
    /// <param name="metadata">Metadata that would be displayed by Windows.</param>
    /// <returns>A completed task.</returns>
    internal Task UpdateMetadataAsync(WindowsMediaMetadata metadata) => Task.CompletedTask;

    /// <summary>Ignores navigation capabilities on platforms without SMTC.</summary>
    /// <param name="canGoPrevious">Whether previous navigation is available.</param>
    /// <param name="canGoNext">Whether next navigation is available.</param>
    internal void SetNavigationCapabilities(bool canGoPrevious, bool canGoNext) { }

    /// <summary>Ignores playback status on platforms without SMTC.</summary>
    /// <param name="status">Current playback status.</param>
    internal void SetPlaybackStatus(MediaPlaybackStatus status) { }

    /// <summary>Ignores timeline state on platforms without SMTC.</summary>
    /// <param name="position">Current playback position.</param>
    /// <param name="duration">Current track duration.</param>
    /// <param name="force">Whether an update would be forced.</param>
    internal void UpdateTimeline(TimeSpan position, TimeSpan duration, bool force = false) { }

    /// <summary>Clears no-op media state.</summary>
    internal void Clear() { }

    /// <inheritdoc/>
    public void Dispose() { }
}

/// <summary>
/// Describes metadata passed through the platform-neutral desktop UI.
/// </summary>
/// <param name="Title">Track, episode, or stream title.</param>
/// <param name="Artist">Artist, podcast, or station name.</param>
/// <param name="Album">Album or collection title.</param>
/// <param name="ArtworkPath">Optional local artwork path.</param>
/// <param name="ArtworkUri">Optional remote artwork URI.</param>
internal sealed record WindowsMediaMetadata(
    string Title,
    string Artist,
    string Album,
    string? ArtworkPath = null,
    Uri? ArtworkUri = null);
