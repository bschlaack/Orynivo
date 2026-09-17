namespace Orynivo.Audio;

/// <summary>Exposes live headphone-crossfeed updates for PCM audio players.</summary>
internal interface ICrossfeedAudioPlayer
{
    /// <summary>Applies a crossfeed state and strength to the running session.</summary>
    /// <param name="enabled">Whether crossfeed is active.</param>
    /// <param name="strength">Selected blend strength.</param>
    void UpdateCrossfeed(bool enabled, CrossfeedStrength strength);
}
