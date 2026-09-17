namespace Orynivo.Audio;

/// <summary>
/// Applies a slow, bounded automatic gain to live radio and podcast PCM so their
/// loudness does not jump relative to the ReplayGain-normalized music library.
/// </summary>
/// <remarks>
/// The normalizer tracks a running mean square of the mono sum and moves the gain
/// toward the configured target with a long time constant, so it never pumps on
/// short passages. It is bypassed while disabled and never runs on native DSD or
/// on library tracks, which already use ReplayGain.
/// </remarks>
public sealed class StreamingLoudnessNormalizer
{
    /// <summary>The default target level in dBFS RMS.</summary>
    public const double DefaultTargetDbfs = -18;

    /// <summary>The default maximum gain adjustment in decibels.</summary>
    public const double DefaultMaximumGainDb = 12;

    private const double SilenceFloorMeanSquare = 1e-8;
    private const double LevelWindowSeconds = 3.0;
    private const double GainWindowSeconds = 2.0;

    private readonly int _sampleRate;

    private bool _enabled;
    private double _targetLinear;
    private double _minimumGain;
    private double _maximumGain;
    private double _levelCoefficient;
    private double _gainCoefficient;
    private double _meanSquare;
    private double _gain = 1.0;

    /// <summary>Initializes a normalizer for a PCM sample rate.</summary>
    /// <param name="sampleRate">Output sample rate in hertz.</param>
    /// <param name="enabled">Whether normalization is active.</param>
    /// <param name="targetDbfs">Target level in dBFS RMS.</param>
    /// <param name="maximumGainDb">Maximum gain adjustment in decibels (symmetric).</param>
    internal StreamingLoudnessNormalizer(
        int sampleRate,
        bool enabled,
        double targetDbfs = DefaultTargetDbfs,
        double maximumGainDb = DefaultMaximumGainDb)
    {
        _sampleRate = sampleRate;
        Configure(enabled, targetDbfs, maximumGainDb);
    }

    /// <summary>Applies a new state and target without clearing the running estimate.</summary>
    /// <param name="enabled">Whether normalization is active.</param>
    /// <param name="targetDbfs">Target level in dBFS RMS.</param>
    /// <param name="maximumGainDb">Maximum gain adjustment in decibels (symmetric).</param>
    internal void Update(bool enabled, double targetDbfs, double maximumGainDb)
        => Configure(enabled, targetDbfs, maximumGainDb);

    /// <summary>Clears the running estimate and returns to unity gain after a seek.</summary>
    internal void Reset()
    {
        _meanSquare = 0;
        _gain = 1.0;
    }

    /// <summary>Processes one stereo frame.</summary>
    /// <param name="left">Left input sample.</param>
    /// <param name="right">Right input sample.</param>
    /// <returns>The processed stereo frame; the input unchanged while disabled.</returns>
    internal (float Left, float Right) Process(float left, float right)
    {
        if (!_enabled)
            return (left, right);

        var mono = (left + right) * 0.5;
        _meanSquare += _levelCoefficient * ((mono * mono) - _meanSquare);

        if (_meanSquare > SilenceFloorMeanSquare)
        {
            var currentRms = Math.Sqrt(_meanSquare);
            var desiredGain = Math.Clamp(_targetLinear / currentRms, _minimumGain, _maximumGain);
            _gain += _gainCoefficient * (desiredGain - _gain);
        }

        return ((float)(left * _gain), (float)(right * _gain));
    }

    private void Configure(bool enabled, double targetDbfs, double maximumGainDb)
    {
        _enabled = enabled;
        _targetLinear = Math.Pow(10, targetDbfs / 20);
        var maximumGain = Math.Pow(10, Math.Abs(maximumGainDb) / 20);
        _minimumGain = 1 / maximumGain;
        _maximumGain = maximumGain;
        _levelCoefficient = 1 - Math.Exp(-1.0 / (_sampleRate * LevelWindowSeconds));
        _gainCoefficient = 1 - Math.Exp(-1.0 / (_sampleRate * GainWindowSeconds));
    }
}
