namespace Orynivo.Audio;

/// <summary>
/// Compile-time WASAPI placeholder for non-Windows desktop builds.
/// </summary>
public sealed class WasapiAudioPlayer : IGaplessAudioPlayer, IEqualizerAudioPlayer
{
    private WasapiAudioPlayer() { }

    /// <inheritdoc/>
    public event EventHandler<GaplessTrackChangedEventArgs>? TrackChanged;
    /// <inheritdoc/>
    public string CurrentFilePath => string.Empty;
    /// <inheritdoc/>
    public AudioFileInfo CurrentInfo => throw new PlatformNotSupportedException();
    /// <inheritdoc/>
    public TimeSpan Duration => TimeSpan.Zero;
    /// <inheritdoc/>
    public TimeSpan Position => TimeSpan.Zero;
    /// <inheritdoc/>
    public bool IsPaused => false;
    /// <inheritdoc/>
    public bool CanSeek => false;
    /// <inheritdoc/>
    public float Volume { get; set; }
    /// <inheritdoc/>
    public float ReplayGainFactor { get; set; } = 1.0f;

    /// <summary>Rejects WASAPI playback on non-Windows systems.</summary>
    /// <param name="items">Playback items.</param>
    /// <param name="deviceId">Windows endpoint identifier.</param>
    /// <param name="equalizerEnabled">Whether equalization was requested.</param>
    /// <param name="equalizerProfile">Requested equalizer profile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>This method does not complete successfully.</returns>
    public static Task<(WasapiAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        IReadOnlyList<GaplessPlaybackItem> items,
        string deviceId,
        bool equalizerEnabled = false,
        EqualizerProfile? equalizerProfile = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException<(WasapiAudioPlayer, AudioFileInfo)>(
            new PlatformNotSupportedException());

    /// <summary>Rejects WASAPI playback on non-Windows systems.</summary>
    /// <param name="filePath">Playback path.</param>
    /// <param name="deviceId">Windows endpoint identifier.</param>
    /// <param name="equalizerEnabled">Whether equalization was requested.</param>
    /// <param name="equalizerProfile">Requested equalizer profile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>This method does not complete successfully.</returns>
    public static Task<(WasapiAudioPlayer AudioPlayer, AudioFileInfo Info)> CreateAsync(
        string filePath,
        string deviceId,
        bool equalizerEnabled = false,
        EqualizerProfile? equalizerProfile = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException<(WasapiAudioPlayer, AudioFileInfo)>(
            new PlatformNotSupportedException());

    /// <inheritdoc/>
    public void UpdateEqualizer(bool enabled, EqualizerProfile? profile) { }
    /// <inheritdoc/>
    public void Pause() { }
    /// <inheritdoc/>
    public void Resume() { }
    /// <inheritdoc/>
    public Task SeekAsync(TimeSpan position) => Task.CompletedTask;
    /// <inheritdoc/>
    public Task WaitForCompletionAsync() => Task.CompletedTask;
    /// <inheritdoc/>
    public void Dispose() { }
}
