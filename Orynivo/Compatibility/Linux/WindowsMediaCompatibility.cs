namespace Windows.Media;

/// <summary>
/// Mirrors the playback states consumed by the shared desktop UI when Windows
/// System Media Transport Controls are unavailable.
/// </summary>
internal enum MediaPlaybackStatus
{
    /// <summary>Playback is stopped.</summary>
    Stopped,

    /// <summary>Playback is currently active.</summary>
    Playing,

    /// <summary>Playback is paused.</summary>
    Paused
}
