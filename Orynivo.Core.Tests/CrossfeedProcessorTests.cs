using Orynivo.Audio;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the headphone crossfeed processor: bypass when disabled, centered
/// mono content, strength monotonicity, silence, and bounded output.
/// </summary>
public sealed class CrossfeedProcessorTests
{
    private const int SampleRate = 44100;

    /// <summary>The disabled processor passes samples through unchanged.</summary>
    [Fact]
    public void Process_DisabledIsPassthrough()
    {
        var processor = new CrossfeedProcessor(SampleRate, enabled: false, CrossfeedStrength.Medium);

        var (left, right) = processor.Process(0.5f, -0.25f);

        Assert.Equal(0.5f, left);
        Assert.Equal(-0.25f, right);
    }

    /// <summary>Correlated (mono) content remains perfectly centered.</summary>
    [Fact]
    public void Process_MonoContentStaysCentered()
    {
        var processor = new CrossfeedProcessor(SampleRate, enabled: true, CrossfeedStrength.Strong);

        for (var index = 0; index < 2000; index++)
        {
            var (left, right) = processor.Process(0.4f, 0.4f);
            Assert.Equal(left, right);
        }
    }

    /// <summary>A stronger preset leaks more of the opposite channel.</summary>
    [Fact]
    public void Process_StrongerStrengthLeaksMore()
    {
        var light = Leakage(CrossfeedStrength.Light);
        var medium = Leakage(CrossfeedStrength.Medium);
        var strong = Leakage(CrossfeedStrength.Strong);

        Assert.True(light > 0);
        Assert.True(light < medium);
        Assert.True(medium < strong);
    }

    /// <summary>Silence stays silent.</summary>
    [Fact]
    public void Process_SilenceStaysSilent()
    {
        var processor = new CrossfeedProcessor(SampleRate, enabled: true, CrossfeedStrength.Strong);

        for (var index = 0; index < 100; index++)
        {
            var (left, right) = processor.Process(0f, 0f);
            Assert.Equal(0f, left);
            Assert.Equal(0f, right);
        }
    }

    /// <summary>Full-scale anti-correlated input never produces non-finite output.</summary>
    [Fact]
    public void Process_OutputStaysFinite()
    {
        var processor = new CrossfeedProcessor(SampleRate, enabled: true, CrossfeedStrength.Strong);

        for (var index = 0; index < 5000; index++)
        {
            var (left, right) = processor.Process(1f, -1f);
            Assert.True(float.IsFinite(left));
            Assert.True(float.IsFinite(right));
        }
    }

    /// <summary>Resetting clears the filter history after a seek.</summary>
    [Fact]
    public void Reset_ClearsFilterHistory()
    {
        var processor = new CrossfeedProcessor(SampleRate, enabled: true, CrossfeedStrength.Medium);
        for (var index = 0; index < 500; index++)
            processor.Process(1f, 0f);

        processor.Reset();
        var (left, right) = processor.Process(0f, 0f);

        Assert.Equal(0f, left);
        Assert.Equal(0f, right);
    }

    private static float Leakage(CrossfeedStrength strength)
    {
        var processor = new CrossfeedProcessor(SampleRate, enabled: true, strength);
        float left = 0;
        for (var index = 0; index < 4000; index++)
            (left, _) = processor.Process(0f, 1f);
        return left;
    }
}
