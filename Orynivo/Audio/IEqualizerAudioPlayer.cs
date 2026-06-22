namespace Orynivo.Audio;

/// <summary>Exposes live equalizer updates for PCM audio players.</summary>
internal interface IEqualizerAudioPlayer
{
    /// <summary>Applies an equalizer profile with a short click-free transition.</summary>
    /// <param name="enabled">Whether the profile is active.</param>
    /// <param name="profile">Profile to apply.</param>
    void UpdateEqualizer(bool enabled, EqualizerProfile? profile);
}
