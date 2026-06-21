namespace Orynivo.Audio;

/// <summary>
/// Abstraction for an audio player supporting playback, pause, seeking, and volume control.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>Total length of the current audio file.</summary>
    TimeSpan Duration { get; }

    /// <summary>Current playback position based on actually rendered samples.</summary>
    TimeSpan Position { get; }

    /// <summary><see langword="true"/> when playback is paused.</summary>
    bool IsPaused { get; }

    /// <summary><see langword="true"/> when the playback position can be changed.</summary>
    bool CanSeek { get; }

    /// <summary>Playback volume in the range 0.0 (muted) to 1.0 (full volume).</summary>
    float Volume { get; set; }

    /// <summary>
    /// Additional linear PCM gain factor. Native bit-perfect output implementations ignore this value.
    /// </summary>
    float ReplayGainFactor { get; set; }

    /// <summary>Pauses playback.</summary>
    void Pause();

    /// <summary>Resumes a paused playback.</summary>
    void Resume();

    /// <summary>Asynchronously seeks to the specified position in the audio file.</summary>
    /// <param name="position">Target position; clamped to [0, <see cref="Duration"/>].</param>
    Task SeekAsync(TimeSpan position);

    /// <summary>Waits until playback has fully completed.</summary>
    Task WaitForCompletionAsync();
}

/// <summary>
/// Extends an audio player with notifications when continuous playback reaches
/// the next queued track without reopening the output device.
/// </summary>
public interface IGaplessAudioPlayer : IAudioPlayer
{
    /// <summary>Raised when the next queued track becomes audible.</summary>
    event EventHandler<GaplessTrackChangedEventArgs>? TrackChanged;

    /// <summary>Path or URL of the track that is currently audible.</summary>
    string CurrentFilePath { get; }

    /// <summary>Technical information for the track that is currently audible.</summary>
    AudioFileInfo CurrentInfo { get; }
}

/// <summary>
/// Describes one PCM track in a continuous playback session.
/// </summary>
/// <param name="FilePath">Local file path or supported stream URL.</param>
/// <param name="ReplayGainFactor">Linear ReplayGain factor for this track.</param>
public sealed record GaplessPlaybackItem(string FilePath, float ReplayGainFactor);

/// <summary>
/// Provides information about a newly audible track in a gapless session.
/// </summary>
/// <param name="FilePath">Path or URL of the new track.</param>
/// <param name="Info">Technical information for the new track.</param>
public sealed class GaplessTrackChangedEventArgs(string filePath, AudioFileInfo info) : EventArgs
{
    /// <summary>Gets the path or URL of the new track.</summary>
    public string FilePath { get; } = filePath;

    /// <summary>Gets the technical information for the new track.</summary>
    public AudioFileInfo Info { get; } = info;
}
