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
