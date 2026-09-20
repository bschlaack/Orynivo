using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the generated texture bank the Milkdrop shaders sample: the sizes match the
/// original textures, generation is deterministic, the values stay in range, and the three
/// wrap modes behave as documented.
/// </summary>
public sealed class VisualizerTextureBankTests
{
    /// <summary>The textures use the sizes of the original Milkdrop ones.</summary>
    [Fact]
    public void GetSize_MatchesTheMilkdropSizes()
    {
        Assert.Equal(32, VisualizerTextureBank.GetSize(VisualizerTexture.NoiseLow));
        Assert.Equal(256, VisualizerTextureBank.GetSize(VisualizerTexture.NoiseMedium));
        Assert.Equal(512, VisualizerTextureBank.GetSize(VisualizerTexture.NoiseHigh));
        Assert.Equal(32, VisualizerTextureBank.GetSize(VisualizerTexture.Random00));
        Assert.Equal(32, VisualizerTextureBank.GetSize(VisualizerTexture.Random15));
    }

    /// <summary>Two banks generate exactly the same textures.</summary>
    [Fact]
    public void GetPixels_IsDeterministic()
    {
        var first = new VisualizerTextureBank().GetPixels(VisualizerTexture.NoiseLow).ToArray();
        var second = new VisualizerTextureBank().GetPixels(VisualizerTexture.NoiseLow).ToArray();

        Assert.Equal(first, second);
    }

    /// <summary>Every channel stays in the zero-to-one range and the alpha is opaque.</summary>
    [Fact]
    public void GetPixels_ReturnsRgbaInRange()
    {
        var pixels = new VisualizerTextureBank().GetPixels(VisualizerTexture.Random00);

        for (var index = 0; index < pixels.Length; index += 4)
        {
            Assert.InRange(pixels[index], 0f, 1f);
            Assert.InRange(pixels[index + 1], 0f, 1f);
            Assert.InRange(pixels[index + 2], 0f, 1f);
            Assert.Equal(1f, pixels[index + 3]);
        }
    }

    /// <summary>The sixteen random textures are distinct.</summary>
    [Fact]
    public void GetPixels_RandomTexturesDiffer()
    {
        var bank = new VisualizerTextureBank();
        var first = bank.GetPixels(VisualizerTexture.Random00).ToArray();
        var last = bank.GetPixels(VisualizerTexture.Random15).ToArray();

        Assert.NotEqual(first, last);
    }

    /// <summary>The noise textures are smoothed while the random ones stay hard.</summary>
    [Fact]
    public void GetPixels_NoiseIsSmootherThanRandomNoise()
    {
        var bank = new VisualizerTextureBank();
        var noise = bank.GetPixels(VisualizerTexture.NoiseLow);
        var random = bank.GetPixels(VisualizerTexture.Random00);

        Assert.True(Variation(noise) < Variation(random));
    }

    /// <summary>Repeat wraps a coordinate back into the texture.</summary>
    [Fact]
    public void Sample_RepeatsOutsideTheRange()
    {
        var bank = new VisualizerTextureBank();
        var direct = bank.Sample(VisualizerTexture.Random00, 0.25f, 0.25f, VisualizerTextureWrap.Repeat).ToArray();
        var wrapped = bank.Sample(VisualizerTexture.Random00, 1.25f, 0.25f, VisualizerTextureWrap.Repeat).ToArray();

        Assert.Equal(direct, wrapped);
    }

    /// <summary>Clamp holds the edge pixel.</summary>
    [Fact]
    public void Sample_ClampsToTheEdge()
    {
        var bank = new VisualizerTextureBank();
        var edge = bank.Sample(VisualizerTexture.Random00, 1f, 0.5f, VisualizerTextureWrap.Clamp).ToArray();
        var beyond = bank.Sample(VisualizerTexture.Random00, 3f, 0.5f, VisualizerTextureWrap.Clamp).ToArray();

        Assert.Equal(edge, beyond);
    }

    /// <summary>Mirror reflects the coordinate at the edge.</summary>
    [Fact]
    public void Sample_MirrorsOutsideTheRange()
    {
        var bank = new VisualizerTextureBank();
        var inside = bank.Sample(VisualizerTexture.Random00, 0.75f, 0.5f, VisualizerTextureWrap.Mirror).ToArray();
        var mirrored = bank.Sample(VisualizerTexture.Random00, 1.25f, 0.5f, VisualizerTextureWrap.Mirror).ToArray();

        Assert.Equal(inside, mirrored);
    }

    /// <summary>Sampling a texel centre returns that texel unchanged.</summary>
    [Fact]
    public void Sample_ReturnsTheTexelAtItsCentre()
    {
        var bank = new VisualizerTextureBank();
        var pixels = bank.GetPixels(VisualizerTexture.Random00);
        const int size = 32;
        const int x = 5;
        const int y = 7;
        var expected = pixels[(((y * size) + x) * 4) + 1];

        var sampled = bank.Sample(
            VisualizerTexture.Random00,
            (x + 0.5f) / size,
            (y + 0.5f) / size,
            VisualizerTextureWrap.Repeat);

        Assert.Equal(expected, sampled[1], 4);
    }

    /// <summary>Clearing the bank drops the cached textures.</summary>
    [Fact]
    public void Clear_DropsTheGeneratedTextures()
    {
        var bank = new VisualizerTextureBank();
        var before = bank.GetPixels(VisualizerTexture.NoiseLow).ToArray();
        bank.Clear();

        Assert.Equal(before, bank.GetPixels(VisualizerTexture.NoiseLow).ToArray());
    }

    private static float Variation(ReadOnlySpan<float> pixels)
    {
        var total = 0f;
        var count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var value = pixels[index];
            total += value;
            count++;
        }

        var mean = total / Math.Max(1, count);
        var variance = 0f;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var delta = pixels[index] - mean;
            variance += delta * delta;
        }

        return variance / Math.Max(1, count);
    }
}
