namespace Orynivo.Audio;

/// <summary>Exposes live streaming-loudness-normalization updates for PCM audio players.</summary>
internal interface ILoudnessNormalizerAudioPlayer
{
    /// <summary>Applies a loudness-normalization state and target to the running session.</summary>
    /// <param name="enabled">Whether normalization is active.</param>
    /// <param name="targetDbfs">Target level in dBFS RMS.</param>
    /// <param name="maximumGainDb">Maximum gain adjustment in decibels (symmetric).</param>
    void UpdateLoudnessNormalization(bool enabled, double targetDbfs, double maximumGainDb);
}
