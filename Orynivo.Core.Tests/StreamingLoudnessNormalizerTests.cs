using Orynivo.Audio;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the streaming loudness normalizer: bypass, convergence toward the
/// target, bounded gain, silence safety, and reset.
/// </summary>
public sealed class StreamingLoudnessNormalizerTests
{
    private const int SampleRate = 44100;
    private const int SteadyStateSamples = SampleRate * 20;

    private static readonly double TargetLinear = Math.Pow(10, StreamingLoudnessNormalizer.DefaultTargetDbfs / 20);

    /// <summary>The disabled normalizer passes samples through unchanged.</summary>
    [Fact]
    public void Process_DisabledIsPassthrough()
    {
        var normalizer = new StreamingLoudnessNormalizer(SampleRate, enabled: false);

        var (left, right) = normalizer.Process(0.3f, -0.2f);

        Assert.Equal(0.3f, left);
        Assert.Equal(-0.2f, right);
    }

    /// <summary>A quiet stream is boosted toward the target.</summary>
    [Fact]
    public void Process_BoostsQuietStreamTowardTarget()
    {
        var normalizer = new StreamingLoudnessNormalizer(SampleRate, enabled: true);

        var output = RunToSteadyState(normalizer, 0.05f);

        Assert.InRange(output, TargetLinear * 0.95, TargetLinear * 1.05);
    }

    /// <summary>A loud stream is attenuated toward the target.</summary>
    [Fact]
    public void Process_AttenuatesLoudStreamTowardTarget()
    {
        var normalizer = new StreamingLoudnessNormalizer(SampleRate, enabled: true);

        var output = RunToSteadyState(normalizer, 0.5f);

        Assert.InRange(output, TargetLinear * 0.95, TargetLinear * 1.05);
    }

    /// <summary>Gain never exceeds the configured maximum adjustment.</summary>
    [Fact]
    public void Process_ClampsGainToMaximum()
    {
        var normalizer = new StreamingLoudnessNormalizer(SampleRate, enabled: true);

        var output = RunToSteadyState(normalizer, 0.001f);
        var maximumLinear = Math.Pow(10, StreamingLoudnessNormalizer.DefaultMaximumGainDb / 20);

        Assert.InRange(output, 0.001f * maximumLinear * 0.95, 0.001f * maximumLinear * 1.05);
    }

    /// <summary>Silence never produces non-finite output or unbounded gain.</summary>
    [Fact]
    public void Process_SilenceStaysFinite()
    {
        var normalizer = new StreamingLoudnessNormalizer(SampleRate, enabled: true);

        for (var index = 0; index < 10000; index++)
        {
            var (left, right) = normalizer.Process(0f, 0f);
            Assert.Equal(0f, left);
            Assert.Equal(0f, right);
        }
    }

    /// <summary>Resetting returns the gain to unity after a seek.</summary>
    [Fact]
    public void Reset_ReturnsToUnityGain()
    {
        var normalizer = new StreamingLoudnessNormalizer(SampleRate, enabled: true);
        for (var index = 0; index < 50000; index++)
            normalizer.Process(0.5f, 0.5f);

        normalizer.Reset();
        var (left, right) = normalizer.Process(0.2f, 0.2f);

        // The gain restarts at unity; the first sample only nudges it slightly.
        Assert.InRange(left, 0.19f, 0.21f);
        Assert.InRange(right, 0.19f, 0.21f);
    }

    private static float RunToSteadyState(StreamingLoudnessNormalizer normalizer, float amplitude)
    {
        float output = 0;
        for (var index = 0; index < SteadyStateSamples; index++)
            (output, _) = normalizer.Process(amplitude, amplitude);
        return output;
    }
}
