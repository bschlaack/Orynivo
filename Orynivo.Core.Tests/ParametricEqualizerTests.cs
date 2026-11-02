using Orynivo.Audio;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Checks deterministic stereo PCM response and transitions without audio output.</summary>
public sealed class ParametricEqualizerTests
{
    /// <summary>Empty and disabled profiles preserve both stereo channels exactly.</summary>
    [Fact]
    public void Bypass_PreservesStereoSamples()
    {
        foreach (var equalizer in new[] { new ParametricEqualizer(48000, true, new EqualizerProfile()),
            new ParametricEqualizer(48000, false, new EqualizerProfile { PreampDb = 12 }) })
        {
            for (var i = 0; i < 1000; i++)
            {
                var input = (float)(Math.Sin(i) * 0.1);
                var result = equalizer.Process(input, -input);
                Assert.Equal(input, result.Left);
                Assert.Equal(-input, result.Right);
            }
        }
    }

    /// <summary>Steady-state center-frequency gain combines linearly with an external PCM boost.</summary>
    /// <param name="gainDb">The peak filter gain.</param>
    /// <param name="boostDb">External input gain representing the PCM boost path.</param>
    [Theory]
    [InlineData(6, 0)]
    [InlineData(-6, 0)]
    [InlineData(6, 6)]
    public void PeakCenter_MatchesExpectedGain(double gainDb, double boostDb)
    {
        var equalizer = new ParametricEqualizer(48000, true, new EqualizerProfile
        { Filters = [new EqualizerFilter { Type = EqualizerFilterType.Peak, Frequency = 1000, GainDb = gainDb, Q = 1 }] });
        double inputEnergy = 0, outputEnergy = 0;
        for (var i = 0; i < 48000; i++)
        {
            var input = 0.01 * Math.Sin(2 * Math.PI * 1000 * i / 48000);
            var result = equalizer.Process((float)(input * Math.Pow(10, boostDb / 20)), 0);
            Assert.Equal(0, result.Right);
            if (i < 4800) continue;
            inputEnergy += input * input;
            outputEnergy += result.Left * result.Left;
        }
        Assert.InRange(20 * Math.Log10(Math.Sqrt(outputEnergy / inputEnergy)), gainDb + boostDb - 0.01, gainDb + boostDb + 0.01);
    }

    /// <summary>Profile changes transition from the previous gain to the new gain in 50 ms.</summary>
    [Fact]
    public void Update_CrossfadesAndResetClearsHistory()
    {
        var equalizer = new ParametricEqualizer(48000, true, new EqualizerProfile());
        equalizer.Update(true, new EqualizerProfile { PreampDb = -6 });
        Assert.Equal(0.1f, equalizer.Process(0.1f, 0).Left);
        for (var i = 1; i < 2400; i++) equalizer.Process(0.1f, 0);
        Assert.Equal(0.1 * Math.Pow(10, -6.0 / 20), equalizer.Process(0.1f, 0).Left, 6);
        equalizer.Update(true, new EqualizerProfile { Filters = [new EqualizerFilter
            { Type = EqualizerFilterType.LowPass, Frequency = 1000, Q = 0.7 }] });
        for (var i = 0; i < 3000; i++) equalizer.Process(0.1f, 0);
        equalizer.Reset();
        Assert.Equal((0f, 0f), equalizer.Process(0, 0));
    }
}
