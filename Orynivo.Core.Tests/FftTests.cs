using Orynivo.Audio;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies the real-input FFT that feeds the audio visualizer.</summary>
public sealed class FftTests
{
    /// <summary>Only powers of two of at least two are usable.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(1024, true)]
    [InlineData(2048, true)]
    [InlineData(1500, false)]
    public void IsSupportedSize_RequiresAPowerOfTwo(int size, bool expected)
    {
        Assert.Equal(expected, Fft.IsSupportedSize(size));
    }

    /// <summary>A sine at a known frequency peaks in the matching bin.</summary>
    [Fact]
    public void ComputeMagnitudes_FindsASineFrequency()
    {
        const int sampleRate = 48_000;
        const int size = 2048;
        const float frequency = 1_000f;
        var samples = new float[size];
        for (var index = 0; index < size; index++)
            samples[index] = MathF.Sin(2f * MathF.PI * frequency * index / sampleRate);

        var magnitudes = new float[size / 2];
        Fft.ComputeMagnitudes(samples, magnitudes);

        var peak = 0;
        for (var bin = 1; bin < magnitudes.Length; bin++)
        {
            if (magnitudes[bin] > magnitudes[peak])
                peak = bin;
        }

        var expectedBin = frequency * size / sampleRate;
        Assert.InRange(peak, (int)expectedBin - 1, (int)expectedBin + 1);
    }

    /// <summary>Silence produces no energy.</summary>
    [Fact]
    public void ComputeMagnitudes_ReportsSilenceAsZero()
    {
        var magnitudes = new float[512];
        Fft.ComputeMagnitudes(new float[1024], magnitudes);

        Assert.All(magnitudes, value => Assert.Equal(0f, value, 6));
    }

    /// <summary>A constant signal concentrates in the first bin.</summary>
    [Fact]
    public void ComputeMagnitudes_ConcentratesDirectCurrent()
    {
        var samples = new float[1024];
        Array.Fill(samples, 0.5f);
        var magnitudes = new float[512];

        Fft.ComputeMagnitudes(samples, magnitudes);

        Assert.True(magnitudes[0] > 0.4f);
        Assert.All(magnitudes[1..], value => Assert.True(value < 0.01f));
    }

    /// <summary>An unsupported length or a mismatched destination is rejected.</summary>
    [Fact]
    public void ComputeMagnitudes_RejectsUnusableBuffers()
    {
        Assert.Throws<ArgumentException>(() => Fft.ComputeMagnitudes(new float[1000], new float[500]));
        Assert.Throws<ArgumentException>(() => Fft.ComputeMagnitudes(new float[1024], new float[100]));
    }
}
