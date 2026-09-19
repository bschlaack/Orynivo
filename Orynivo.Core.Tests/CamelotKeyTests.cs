using Orynivo.Audio;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the Camelot wheel mapping, parsing, adjacency, and the conservative
/// key estimate produced by the bounded chroma analysis.
/// </summary>
public sealed class CamelotKeyTests
{
    /// <summary>Every wheel position round-trips through its own label.</summary>
    [Fact]
    public void Label_RoundTripsForEveryWheelPosition()
    {
        for (var number = 1; number <= 12; number++)
        {
            foreach (var isMinor in new[] { false, true })
            {
                var key = new CamelotKey(number, isMinor);
                Assert.True(CamelotKey.TryParse(key.Label, out var parsed));
                Assert.Equal(key, parsed);
            }
        }
    }

    /// <summary>Camelot labels parse case-insensitively and reject invalid positions.</summary>
    [Theory]
    [InlineData("8A", 8, true)]
    [InlineData("8b", 8, false)]
    [InlineData("12B", 12, false)]
    [InlineData("1A", 1, true)]
    [InlineData(" 3a ", 3, true)]
    public void TryParse_AcceptsWheelLabels(string text, int number, bool isMinor)
    {
        Assert.True(CamelotKey.TryParse(text, out var key));
        Assert.Equal(new CamelotKey(number, isMinor), key);
    }

    /// <summary>Invalid labels are rejected.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("13A")]
    [InlineData("0B")]
    [InlineData("8C")]
    [InlineData("8")]
    [InlineData("Z")]
    [InlineData("X#")]
    public void TryParse_RejectsInvalidLabels(string text)
    {
        Assert.False(CamelotKey.TryParse(text, out _));
    }

    /// <summary>Conventional key names map onto the correct wheel positions.</summary>
    [Theory]
    [InlineData("A minor", 8, true)]
    [InlineData("Am", 8, true)]
    [InlineData("Amin", 8, true)]
    [InlineData("A moll", 8, true)]
    [InlineData("C major", 8, false)]
    [InlineData("C", 8, false)]
    [InlineData("Cmaj", 8, false)]
    [InlineData("C dur", 8, false)]
    [InlineData("F# minor", 11, true)]
    [InlineData("Db major", 3, false)]
    [InlineData("Bb minor", 3, true)]
    [InlineData("H", 1, false)]
    [InlineData("G#m", 1, true)]
    public void TryParse_MapsConventionalKeyNames(string text, int number, bool isMinor)
    {
        Assert.True(CamelotKey.TryParse(text, out var key));
        Assert.Equal(new CamelotKey(number, isMinor), key);
    }

    /// <summary>Pitch classes map to the documented wheel positions.</summary>
    [Theory]
    [InlineData(0, false, 8)]
    [InlineData(0, true, 5)]
    [InlineData(9, true, 8)]
    [InlineData(9, false, 11)]
    [InlineData(11, false, 1)]
    [InlineData(1, true, 12)]
    public void FromPitchClass_MapsWheelPositions(int pitchClass, bool isMinor, int expectedNumber)
    {
        var key = CamelotKey.FromPitchClass(pitchClass, isMinor);

        Assert.Equal(expectedNumber, key.Number);
        Assert.Equal(isMinor, key.IsMinor);
    }

    /// <summary>Wheel distance treats relative keys and neighbours as adjacent.</summary>
    [Fact]
    public void Distance_UsesWheelAdjacency()
    {
        var a = new CamelotKey(8, true);

        Assert.Equal(0, a.Distance(new CamelotKey(8, true)));
        Assert.Equal(1, a.Distance(new CamelotKey(8, false)));
        Assert.Equal(1, a.Distance(new CamelotKey(9, true)));
        Assert.Equal(1, a.Distance(new CamelotKey(7, true)));
        Assert.Equal(2, a.Distance(new CamelotKey(9, false)));
        Assert.True(a.IsCompatible(new CamelotKey(8, false)));
        Assert.True(a.IsCompatible(new CamelotKey(7, true)));
        Assert.False(a.IsCompatible(new CamelotKey(10, true)));
    }

    /// <summary>Wheel distance wraps around position twelve.</summary>
    [Fact]
    public void Distance_WrapsAroundTheWheel()
    {
        var first = new CamelotKey(1, true);

        Assert.Equal(1, first.Distance(new CamelotKey(12, true)));
        Assert.Equal(6, first.Distance(new CamelotKey(7, true)));
    }

    /// <summary>Wheel positions resolve back to their conventional key names.</summary>
    [Theory]
    [InlineData(8, true, "A minor")]
    [InlineData(8, false, "C major")]
    [InlineData(1, false, "B major")]
    [InlineData(1, true, "G# minor")]
    public void ToKeyName_ReturnsConventionalName(int number, bool isMinor, string expected)
    {
        Assert.Equal(expected, new CamelotKey(number, isMinor).ToKeyName());
    }

    /// <summary>Silence and very short input produce no key estimate.</summary>
    [Fact]
    public void EstimateKey_WithoutUsableAudio_ReturnsNull()
    {
        Assert.Null(AudioFeatureAnalysisService.EstimateKey([], 8000));
        Assert.Null(AudioFeatureAnalysisService.EstimateKey(new float[1024], 8000));
        Assert.Null(AudioFeatureAnalysisService.EstimateKey(new float[80000], 8000));
    }

    /// <summary>A C major scale is estimated as a compatible major key.</summary>
    [Fact]
    public void EstimateKey_RecognisesMajorScale()
    {
        var samples = Scale(60, [0, 2, 4, 5, 7, 9, 11]);

        var key = AudioFeatureAnalysisService.EstimateKey(samples, 8000);

        Assert.NotNull(key);
        Assert.False(key!.Value.IsMinor);
        Assert.True(key.Value.Distance(new CamelotKey(8, false)) <= 1);
    }

    /// <summary>An A natural minor scale is estimated as a compatible minor key.</summary>
    [Fact]
    public void EstimateKey_RecognisesMinorScale()
    {
        var samples = Scale(57, [0, 2, 3, 5, 7, 8, 10]);

        var key = AudioFeatureAnalysisService.EstimateKey(samples, 8000);

        Assert.NotNull(key);
        Assert.True(key!.Value.IsMinor);
        Assert.True(key.Value.Distance(new CamelotKey(8, true)) <= 1);
    }

    /// <summary>Descriptors expose the estimated key for caching.</summary>
    [Fact]
    public void AnalyzePcm_IncludesEstimatedKey()
    {
        var descriptor = AudioFeatureAnalysisService.AnalyzePcm(Scale(60, [0, 2, 4, 5, 7, 9, 11]), 8000);

        Assert.NotNull(descriptor);
        Assert.Equal(AudioFeatureAnalysisService.CurrentVersion, descriptor.Version);
        Assert.NotNull(descriptor.Key);
    }

    private static float[] Scale(int rootMidi, int[] intervals)
    {
        const int sampleRate = 8000;
        var samples = new List<float>();
        foreach (var interval in intervals)
            samples.AddRange(Tone(rootMidi + interval, 1.0, sampleRate));
        samples.AddRange(Tone(rootMidi, 2.0, sampleRate));
        return samples.ToArray();
    }

    private static IEnumerable<float> Tone(int midi, double seconds, int sampleRate)
    {
        var frequency = 440d * Math.Pow(2d, (midi - 69) / 12d);
        var count = (int)(sampleRate * seconds);
        for (var index = 0; index < count; index++)
            yield return (float)(0.4d * Math.Sin(2d * Math.PI * frequency * index / sampleRate));
    }
}
