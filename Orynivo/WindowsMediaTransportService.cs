using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Orynivo;

/// <summary>
/// Hosts Orynivo's Windows System Media Transport Controls session and keeps
/// global media buttons, playback state, timeline, metadata, and artwork in sync.
/// </summary>
internal sealed class WindowsMediaTransportService : IDisposable
{
    private readonly MediaPlayer _hostPlayer;
    private readonly SystemMediaTransportControls _controls;
    private DateTimeOffset _lastTimelineUpdate = DateTimeOffset.MinValue;
    private long _metadataVersion;
    private bool _disposed;

    private WindowsMediaTransportService()
    {
        _hostPlayer = new MediaPlayer();
        _hostPlayer.CommandManager.IsEnabled = false;
        _controls = _hostPlayer.SystemMediaTransportControls;
        _controls.IsEnabled = false;
        _controls.IsPlayEnabled = true;
        _controls.IsPauseEnabled = true;
        _controls.IsStopEnabled = true;
        _controls.ButtonPressed += Controls_OnButtonPressed;
        _controls.PlaybackPositionChangeRequested += Controls_OnPlaybackPositionChangeRequested;
    }

    /// <summary>Raised when Windows requests playback to start or resume.</summary>
    internal event Action? PlayRequested;

    /// <summary>Raised when Windows requests playback to pause.</summary>
    internal event Action? PauseRequested;

    /// <summary>Raised when Windows requests the previous queue item.</summary>
    internal event Action? PreviousRequested;

    /// <summary>Raised when Windows requests the next queue item.</summary>
    internal event Action? NextRequested;

    /// <summary>Raised when Windows requests playback to stop.</summary>
    internal event Action? StopRequested;

    /// <summary>Raised when Windows requests a new playback position.</summary>
    internal event Action<TimeSpan>? PositionChangeRequested;

    /// <summary>
    /// Creates the Windows media integration when the required operating-system
    /// APIs are available; otherwise returns <see langword="null"/>.
    /// </summary>
    internal static WindowsMediaTransportService? TryCreate()
    {
        try
        {
            return new WindowsMediaTransportService();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Updates the system media overlay and lock-screen metadata for the audible item.
    /// </summary>
    /// <param name="metadata">Metadata and optional local or remote artwork.</param>
    internal async Task UpdateMetadataAsync(WindowsMediaMetadata metadata)
    {
        if (_disposed)
            return;

        var version = Interlocked.Increment(ref _metadataVersion);
        RandomAccessStreamReference? thumbnail = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(metadata.ArtworkPath) &&
                File.Exists(metadata.ArtworkPath))
            {
                var file = await StorageFile.GetFileFromPathAsync(metadata.ArtworkPath);
                thumbnail = RandomAccessStreamReference.CreateFromFile(file);
            }
            else if (metadata.ArtworkUri is not null)
            {
                thumbnail = RandomAccessStreamReference.CreateFromUri(metadata.ArtworkUri);
            }
        }
        catch
        {
            thumbnail = null;
        }

        if (_disposed || version != Volatile.Read(ref _metadataVersion))
            return;

        try
        {
            var updater = _controls.DisplayUpdater;
            updater.ClearAll();
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = metadata.Title;
            updater.MusicProperties.Artist = metadata.Artist;
            updater.MusicProperties.AlbumTitle = metadata.Album;
            updater.Thumbnail = thumbnail;
            updater.Update();
            _controls.IsEnabled = true;
        }
        catch
        {
            // SMTC metadata failures must never affect audio playback.
        }
    }

    /// <summary>Updates which queue navigation commands Windows may expose.</summary>
    /// <param name="canGoPrevious">Whether a previous queue item is available.</param>
    /// <param name="canGoNext">Whether a next queue item is available.</param>
    internal void SetNavigationCapabilities(bool canGoPrevious, bool canGoNext)
    {
        if (_disposed)
            return;
        try
        {
            _controls.IsPreviousEnabled = canGoPrevious;
            _controls.IsNextEnabled = canGoNext;
        }
        catch
        {
            // SMTC availability must never affect queue navigation.
        }
    }

    /// <summary>Updates the playback status displayed by Windows.</summary>
    /// <param name="status">Current playback status.</param>
    internal void SetPlaybackStatus(MediaPlaybackStatus status)
    {
        if (_disposed)
            return;
        try
        {
            _controls.IsEnabled = true;
            _controls.PlaybackStatus = status;
        }
        catch
        {
            // SMTC availability must never affect playback.
        }
    }

    /// <summary>
    /// Updates the system timeline, throttled to the cadence recommended for SMTC.
    /// </summary>
    /// <param name="position">Current audible playback position.</param>
    /// <param name="duration">Current item duration.</param>
    /// <param name="force">Whether to bypass the normal five-second throttle.</param>
    internal void UpdateTimeline(TimeSpan position, TimeSpan duration, bool force = false)
    {
        if (_disposed || duration <= TimeSpan.Zero)
            return;

        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastTimelineUpdate < TimeSpan.FromSeconds(5))
            return;
        _lastTimelineUpdate = now;

        var boundedPosition = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > duration
                ? duration
                : position;
        try
        {
            _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                Position = boundedPosition,
                MaxSeekTime = duration,
                EndTime = duration
            });
        }
        catch
        {
            // Timeline updates are optional system UI state.
        }
    }

    /// <summary>Clears metadata and removes Orynivo's inactive system media session.</summary>
    internal void Clear()
    {
        if (_disposed)
            return;
        Interlocked.Increment(ref _metadataVersion);
        try
        {
            _controls.PlaybackStatus = MediaPlaybackStatus.Stopped;
            _controls.DisplayUpdater.ClearAll();
            _controls.DisplayUpdater.Update();
            _controls.IsEnabled = false;
        }
        catch
        {
            // Clearing optional Windows UI state must remain best effort.
        }
        _lastTimelineUpdate = DateTimeOffset.MinValue;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Interlocked.Increment(ref _metadataVersion);
        try
        {
            _controls.ButtonPressed -= Controls_OnButtonPressed;
            _controls.PlaybackPositionChangeRequested -= Controls_OnPlaybackPositionChangeRequested;
            _controls.IsEnabled = false;
            _hostPlayer.Dispose();
        }
        catch
        {
            // Disposal remains best effort during application shutdown.
        }
    }

    private void Controls_OnButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                PlayRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Pause:
                PauseRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Previous:
                PreviousRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Next:
                NextRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Stop:
                StopRequested?.Invoke();
                break;
        }
    }

    private void Controls_OnPlaybackPositionChangeRequested(
        SystemMediaTransportControls sender,
        PlaybackPositionChangeRequestedEventArgs args) =>
        PositionChangeRequested?.Invoke(args.RequestedPlaybackPosition);
}

/// <summary>
/// Describes the metadata shown by Windows for Orynivo's current media item.
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
