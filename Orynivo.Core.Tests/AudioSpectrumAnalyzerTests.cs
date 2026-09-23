using Orynivo.Audio;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies the spectrum the visualization presets react to.</summary>
public sealed class AudioSpectrumAnalyzerTests
{
    private const int SampleRate = 48_000;

    /// <summary>A bass tone raises the bass energy above the treble energy.</summary>
    [Fact]
    public void Analyze_SeparatesBassFromTreble()
    {
        var analyzer = new AudioSpectrumAnalyzer(SampleRate);

        for (var block = 0; block < 8; block++)
            analyzer.Analyze(Tone(80f, 0.5f));

        Assert.True(analyzer.Bass > analyzer.Treble);
        Assert.True(analyzer.Bass > 0.1f);
    }

    /// <summary>A treble tone raises the treble energy above the bass energy.</summary>
    [Fact]
    public void Analyze_SeparatesTrebleFromBass()
    {
        var analyzer = new AudioSpectrumAnalyzer(SampleRate);

        for (var block = 0; block < 8; block++)
            analyzer.Analyze(Tone(9_000f, 0.5f));

        Assert.True(analyzer.Treble > analyzer.Bass);
    }

    /// <summary>
    /// The reference damps the FFT input with a one-sample pre-emphasis, which cancels a signal that
    /// alternates every sample (the Nyquist rate) exactly.
    /// </summary>
    [Fact]
    public void Analyze_DampsTheNyquistBandWithPreEmphasis()
    {
        var analyzer = new AudioSpectrumAnalyzer(SampleRate);
        var alternating = new float[2048 * 2];
        for (var index = 0; index < alternating.Length; index++)
            alternating[index] = (index & 1) == 0 ? 0.5f : -0.5f;

        for (var block = 0; block < 8; block++)
            analyzer.Analyze(alternating);

        Assert.All(analyzer.Bands.ToArray(), value => Assert.Equal(0f, value, 5));
    }

    /// <summary>Bands and the summary stay inside the documented range.</summary>
    [Fact]
    public void Analyze_KeepsValuesInRange()
    {
        var analyzer = new AudioSpectrumAnalyzer(SampleRate);

        for (var block = 0; block < 4; block++)
            analyzer.Analyze(Tone(1_000f, 1f));

        Assert.All(analyzer.Bands.ToArray(), value => Assert.InRange(value, 0f, 1f));
        Assert.InRange(analyzer.Volume, 0f, 1f);
        Assert.InRange(analyzer.Bass, 0f, 1f);
        Assert.InRange(analyzer.Mid, 0f, 1f);
        Assert.InRange(analyzer.Treble, 0f, 1f);
    }

    /// <summary>Silence decays towards zero instead of snapping, then a reset clears it.</summary>
    [Fact]
    public void Analyze_DecaysOnSilenceAndResets()
    {
        var analyzer = new AudioSpectrumAnalyzer(SampleRate);
        for (var block = 0; block < 8; block++)
            analyzer.Analyze(Tone(1_000f, 0.8f));
        var loud = analyzer.Volume;

        for (var block = 0; block < 20; block++)
            analyzer.Analyze(new float[2048]);

        Assert.True(analyzer.Volume < loud);
        Assert.True(analyzer.Volume > 0f);

        analyzer.Reset();
        Assert.Equal(0f, analyzer.Volume);
        Assert.Equal(0, analyzer.FrameCount);
    }

    /// <summary>Every analyzed block advances the frame counter, even a short one.</summary>
    [Fact]
    public void Analyze_CountsFramesAndAcceptsShortBlocks()
    {
        var analyzer = new AudioSpectrumAnalyzer(SampleRate);

        analyzer.Analyze(new float[2]);
        analyzer.Analyze(new float[128]);

        Assert.Equal(2, analyzer.FrameCount);
    }

    /// <summary>The waveform overlay follows the analyzed samples.</summary>
    [Fact]
    public void Analyze_ExposesAWaveform()
    {
        var analyzer = new AudioSpectrumAnalyzer(SampleRate);

        analyzer.Analyze(Tone(440f, 0.5f));

        var waveform = analyzer.Waveform;
        Assert.Equal(AudioSpectrumAnalyzer.WaveformPoints, waveform.Length);
        Assert.All(waveform.ToArray(), value => Assert.InRange(value, -1f, 1f));
        Assert.Contains(waveform.ToArray(), value => MathF.Abs(value) > 0.05f);

        analyzer.Reset();
        Assert.All(analyzer.Waveform.ToArray(), value => Assert.Equal(0f, value));
    }

    /// <summary>An unusable configuration is rejected.</summary>
    [Fact]
    public void Constructor_RejectsUnusableArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpectrumAnalyzer(100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSpectrumAnalyzer(SampleRate, 1000));
    }

    private static float[] Tone(float frequency, float amplitude)
    {
        const int frames = 2048;
        var samples = new float[frames * 2];
        for (var index = 0; index < frames; index++)
        {
            var value = MathF.Sin(2f * MathF.PI * frequency * index / SampleRate) * amplitude;
            samples[index * 2] = value;
            samples[(index * 2) + 1] = value;
        }

        return samples;
    }
}
