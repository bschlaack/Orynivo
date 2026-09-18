using Windows.Media;

namespace Orynivo;

/// <summary>
/// Non-Windows system-media integration. On Linux it forwards to the MPRIS
/// D-Bus media player so desktop media keys, panels, and applets can control
/// Orynivo; on macOS the service is inert.
/// </summary>
internal sealed class WindowsMediaTransportService : IDisposable
{
#if ORYNIVO_LINUX
    private readonly MprisMediaTransport _mpris;

    private WindowsMediaTransportService(MprisMediaTransport mpris)
    {
        _mpris = mpris;
        _mpris.PlayRequested += () => PlayRequested?.Invoke();
        _mpris.PauseRequested += () => PauseRequested?.Invoke();
        _mpris.PreviousRequested += () => PreviousRequested?.Invoke();
        _mpris.NextRequested += () => NextRequested?.Invoke();
        _mpris.StopRequested += () => StopRequested?.Invoke();
        _mpris.PositionChangeRequested += position => PositionChangeRequested?.Invoke(position);
        _mpris.VolumeChangeRequested += volume => VolumeChangeRequested?.Invoke(volume);
    }
#endif

    /// <summary>Raised when the desktop requests playback to start or resume.</summary>
    internal event Action? PlayRequested;

    /// <summary>Raised when the desktop requests playback to pause.</summary>
    internal event Action? PauseRequested;

    /// <summary>Raised when the desktop requests the previous queue item.</summary>
    internal event Action? PreviousRequested;

    /// <summary>Raised when the desktop requests the next queue item.</summary>
    internal event Action? NextRequested;

    /// <summary>Raised when the desktop requests playback to stop.</summary>
    internal event Action? StopRequested;

    /// <summary>Raised when the desktop requests a new playback position.</summary>
    internal event Action<TimeSpan>? PositionChangeRequested;

    /// <summary>Raised when the desktop requests a new output volume.</summary>
    internal event Action<double>? VolumeChangeRequested;

#if ORYNIVO_LINUX
    /// <summary>
    /// Creates the Linux MPRIS integration when a session bus is available;
    /// otherwise returns <see langword="null"/>.
    /// </summary>
    /// <returns>The connected transport, or <see langword="null"/> without a session bus.</returns>
    internal static WindowsMediaTransportService? TryCreate()
    {
        var mpris = MprisMediaTransport.TryCreate();
        return mpris is null ? null : new WindowsMediaTransportService(mpris);
    }

    /// <summary>Publishes the current item metadata to the desktop.</summary>
    /// <param name="metadata">Metadata and optional local or remote artwork.</param>
    /// <returns>A completed task.</returns>
    internal Task UpdateMetadataAsync(WindowsMediaMetadata metadata)
    {
        _mpris.UpdateMetadata(metadata);
        return Task.CompletedTask;
    }

    /// <summary>Updates which queue navigation commands the desktop may expose.</summary>
    /// <param name="canGoPrevious">Whether a previous queue item is available.</param>
    /// <param name="canGoNext">Whether a next queue item is available.</param>
    internal void SetNavigationCapabilities(bool canGoPrevious, bool canGoNext) =>
        _mpris.SetNavigationCapabilities(canGoPrevious, canGoNext);

    /// <summary>Updates the playback status displayed by the desktop.</summary>
    /// <param name="status">Current playback status.</param>
    internal void SetPlaybackStatus(MediaPlaybackStatus status) =>
        _mpris.SetPlaybackStatus(status);

    /// <summary>Publishes the current output volume to the desktop.</summary>
    /// <param name="volume">Linear volume from zero through one.</param>
    internal void SetVolume(double volume) => _mpris.SetVolume(volume);

    /// <summary>Updates the reported playback timeline.</summary>
    /// <param name="position">Current playback position.</param>
    /// <param name="duration">Current track duration.</param>
    /// <param name="force">Whether to bypass the normal throttle.</param>
    internal void UpdateTimeline(TimeSpan position, TimeSpan duration, bool force = false) =>
        _mpris.UpdateTimeline(position, duration, force);

    /// <summary>Clears the desktop's media state.</summary>
    internal void Clear() => _mpris.Clear();

    /// <inheritdoc/>
    public void Dispose() => _mpris.Dispose();
#else
    /// <summary>Returns no service because the desktop media integration is unavailable.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    internal static WindowsMediaTransportService? TryCreate() => null;

    /// <summary>Ignores metadata on platforms without a desktop media integration.</summary>
    /// <param name="metadata">Metadata that would be displayed by the desktop.</param>
    /// <returns>A completed task.</returns>
    internal Task UpdateMetadataAsync(WindowsMediaMetadata metadata) => Task.CompletedTask;

    /// <summary>Ignores navigation capabilities without a desktop media integration.</summary>
    /// <param name="canGoPrevious">Whether previous navigation is available.</param>
    /// <param name="canGoNext">Whether next navigation is available.</param>
    internal void SetNavigationCapabilities(bool canGoPrevious, bool canGoNext) { }

    /// <summary>Ignores playback status without a desktop media integration.</summary>
    /// <param name="status">Current playback status.</param>
    internal void SetPlaybackStatus(MediaPlaybackStatus status) { }

    /// <summary>Ignores output volume without a desktop media integration.</summary>
    /// <param name="volume">Linear volume from zero through one.</param>
    internal void SetVolume(double volume) { }

    /// <summary>Ignores timeline state without a desktop media integration.</summary>
    /// <param name="position">Current playback position.</param>
    /// <param name="duration">Current track duration.</param>
    /// <param name="force">Whether an update would be forced.</param>
    internal void UpdateTimeline(TimeSpan position, TimeSpan duration, bool force = false) { }

    /// <summary>Clears no-op media state.</summary>
    internal void Clear() { }

    /// <inheritdoc/>
    public void Dispose() { }
#endif
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
