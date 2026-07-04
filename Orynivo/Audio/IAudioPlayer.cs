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
/// <param name="KnownInfo">
/// Optional pre-known stream characteristics that let the player skip the FFmpeg
/// probe. Supplied for remote tracks whose format is already reported by the
/// server, avoiding one HTTP round-trip on playback start.
/// </param>
public sealed record GaplessPlaybackItem(
    string FilePath,
    float ReplayGainFactor,
    string? SourcePath = null,
    TimeSpan? SegmentStart = null,
    TimeSpan? SegmentEnd = null,
    IReadOnlyList<string>? SourcePaths = null,
    TimeSpan? KnownDuration = null,
    KnownAudioInfo? KnownInfo = null)
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

    /// <summary>
    /// Builds an <see cref="AudioFileInfo"/> from the pre-known stream
    /// characteristics so the player can avoid probing. The duration is left at
    /// <see cref="TimeSpan.Zero"/> because callers override it from
    /// <see cref="SegmentDuration"/> or <see cref="KnownDuration"/>.
    /// </summary>
    /// <returns>
    /// The technical information, or <see langword="null"/> when no usable
    /// pre-known sample rate is available and probing is required.
    /// </returns>
    public AudioFileInfo? TryCreateKnownAudioInfo()
    {
        if (KnownInfo is not { SourceSampleRate: > 0 } known)
            return null;

        var outputSampleRate = known.IsDsd
            ? 176_400
            : known.SourceSampleRate is >= 8_000 and <= 768_000
                ? known.SourceSampleRate
                : 192_000;
        return new AudioFileInfo(
            string.IsNullOrWhiteSpace(known.CodecName) ? "unknown" : known.CodecName,
            known.SourceSampleRate,
            known.Channels > 0 ? known.Channels : 2,
            outputSampleRate,
            known.IsDsd,
            known.ContainerName ?? string.Empty,
            TimeSpan.Zero);
    }
}

/// <summary>
/// Pre-known technical characteristics of a stream, used to skip the FFmpeg
/// probe when a remote server already reports them.
/// </summary>
/// <param name="SourceSampleRate">Source sample rate in hertz.</param>
/// <param name="Channels">Channel count of the source stream.</param>
/// <param name="CodecName">Codec or format identifier for display.</param>
/// <param name="IsDsd">Whether the source is a DSD stream.</param>
/// <param name="ContainerName">Container/format name for display.</param>
public sealed record KnownAudioInfo(
    int SourceSampleRate,
    int Channels,
    string CodecName,
    bool IsDsd,
    string? ContainerName);

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
