using Orynivo.Controls;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the genre-cloud perceptual luminance fingerprint used to de-duplicate
/// background images.
/// </summary>
public sealed class GenreCloudImageFingerprintTests
{
    /// <summary>Uniform luminance sets every bit because each sample meets the average.</summary>
    [Fact]
    public void Compute_UniformLuminanceSetsAllBits()
    {
        var luminance = new float[GenreCloudImageFingerprint.SampleCount];
        Array.Fill(luminance, 128f);

        Assert.Equal(ulong.MaxValue, GenreCloudImageFingerprint.Compute(luminance));
    }

    /// <summary>Bright-then-dark samples set only the bits above the average.</summary>
    [Fact]
    public void Compute_SplitsAtAverage()
    {
        var luminance = new float[GenreCloudImageFingerprint.SampleCount];
        for (var index = 0; index < 32; index++)
            luminance[index] = 255f;

        Assert.Equal(0xFFFFFFFFUL, GenreCloudImageFingerprint.Compute(luminance));
    }

    /// <summary>The fingerprint is deterministic for the same samples.</summary>
    [Fact]
    public void Compute_IsDeterministic()
    {
        var luminance = Enumerable.Range(0, GenreCloudImageFingerprint.SampleCount)
            .Select(index => index * 4f)
            .ToArray();

        Assert.Equal(
            GenreCloudImageFingerprint.Compute(luminance),
            GenreCloudImageFingerprint.Compute(luminance));
    }

    /// <summary>A wrong sample count is rejected.</summary>
    [Fact]
    public void Compute_WrongSampleCountThrows()
        => Assert.Throws<ArgumentException>(() => GenreCloudImageFingerprint.Compute(new float[10]));
}
