namespace Orynivo.Audio;

/// <summary>
/// Selects which ReplayGain metadata is applied during PCM playback.
/// </summary>
public enum ReplayGainMode
{
    /// <summary>Do not apply ReplayGain adjustment.</summary>
    Off,

    /// <summary>Prefer track gain and fall back to album gain when necessary.</summary>
    Track,

    /// <summary>Prefer album gain and fall back to track gain when necessary.</summary>
    Album
}
