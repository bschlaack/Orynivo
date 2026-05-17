namespace Player.Audio;

public interface IAudioPlayer : IDisposable
{
    TimeSpan Duration { get; }
    TimeSpan Position { get; }
    bool IsPaused { get; }
    bool CanSeek { get; }
    float Volume { get; set; }
    void Pause();
    void Resume();
    Task SeekAsync(TimeSpan position);
    Task WaitForCompletionAsync();
}
