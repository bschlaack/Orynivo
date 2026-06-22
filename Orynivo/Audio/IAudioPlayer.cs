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
/// <param name="FilePath">Stable track path or supported stream URL.</param>
/// <param name="ReplayGainFactor">Linear ReplayGain factor for this track.</param>
/// <param name="SourcePath">Physical media path. Defaults to <paramref name="FilePath"/>.</param>
/// <param name="SegmentStart">Optional start offset within the physical source.</param>
/// <param name="SegmentEnd">Optional exclusive end offset within the physical source.</param>
/// <param name="SourcePaths">Ordered physical media parts forming one logical track.</param>
/// <param name="KnownDuration">Optional authoritative duration for the logical track.</param>
public sealed record GaplessPlaybackItem(
    string FilePath,
    float ReplayGainFactor,
    string? SourcePath = null,
    TimeSpan? SegmentStart = null,
    TimeSpan? SegmentEnd = null,
    IReadOnlyList<string>? SourcePaths = null,
    TimeSpan? KnownDuration = null)
{
    /// <summary>Physical path passed to FFmpeg.</summary>
    public string PlaybackPath => SourcePath ?? FilePath;

    /// <summary>Ordered physical paths passed to FFmpeg.</summary>
    public IReadOnlyList<string> PlaybackPaths =>
        SourcePaths is { Count: > 0 } ? SourcePaths : [PlaybackPath];

    /// <summary>Length of the virtual segment when bounded.</summary>
    public TimeSpan? SegmentDuration =>
        SegmentStart is { } start && SegmentEnd is { } end && end > start
            ? end - start
            : null;
}

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
