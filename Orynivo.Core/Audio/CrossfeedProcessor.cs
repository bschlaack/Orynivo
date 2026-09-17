namespace Orynivo.Audio;

/// <summary>
/// Applies a lightweight headphone crossfeed that blends a low-passed portion of
/// each channel into the opposite channel, reducing the exaggerated stereo
/// separation of headphone listening while keeping correlated (mono) content
/// centered.
/// </summary>
/// <remarks>
/// This is a simple one-pole model rather than a full BS2B network; it never
/// boosts the total level because the direct path is attenuated by half the
/// crossfeed gain. The processor only affects PCM output and is bypassed while
/// disabled, so native DSD remains bit-perfect.
/// </remarks>
public sealed class CrossfeedProcessor
{
    private readonly int _sampleRate;

    private bool _enabled;
    private double _coefficient;
    private double _gain;
    private double _directGain;
    private double _lowPassLeft;
    private double _lowPassRight;

    /// <summary>Initializes a stereo crossfeed processor for a PCM sample rate.</summary>
    /// <param name="sampleRate">Output sample rate in hertz.</param>
    /// <param name="enabled">Whether crossfeed is active.</param>
    /// <param name="strength">Selected blend strength.</param>
    internal CrossfeedProcessor(int sampleRate, bool enabled, CrossfeedStrength strength)
    {
        _sampleRate = sampleRate;
        Configure(enabled, strength);
    }

    /// <summary>Applies a new enabled state and strength without clearing filter history.</summary>
    /// <param name="enabled">Whether crossfeed is active.</param>
    /// <param name="strength">Selected blend strength.</param>
    internal void Update(bool enabled, CrossfeedStrength strength) => Configure(enabled, strength);

    /// <summary>Clears filter history after a discontinuous seek.</summary>
    internal void Reset()
    {
        _lowPassLeft = 0;
        _lowPassRight = 0;
    }

    /// <summary>Processes one stereo frame.</summary>
    /// <param name="left">Left input sample.</param>
    /// <param name="right">Right input sample.</param>
    /// <returns>The processed stereo frame; the input unchanged while disabled.</returns>
    internal (float Left, float Right) Process(float left, float right)
    {
        if (!_enabled)
            return (left, right);

        _lowPassLeft += _coefficient * (left - _lowPassLeft);
        _lowPassRight += _coefficient * (right - _lowPassRight);

        var outputLeft = (left * _directGain) + (_gain * _lowPassRight);
        var outputRight = (right * _directGain) + (_gain * _lowPassLeft);
        return ((float)outputLeft, (float)outputRight);
    }

    private void Configure(bool enabled, CrossfeedStrength strength)
    {
        _enabled = enabled;

        var (cutoffHz, gain) = strength switch
        {
            CrossfeedStrength.Light => (700.0, 0.20),
            CrossfeedStrength.Strong => (650.0, 0.50),
            _ => (700.0, 0.35)
        };

        _coefficient = 1 - Math.Exp(-2 * Math.PI * cutoffHz / _sampleRate);
        _gain = gain;
        _directGain = 1 - (gain / 2);
    }
}
