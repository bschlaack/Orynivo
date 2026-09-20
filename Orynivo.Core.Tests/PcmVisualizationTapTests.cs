using Orynivo.Audio;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the lock-free hand-off between the audio pump and the visualizer, including
/// that the audio side never waits and the consumer always sees the newest samples.
/// </summary>
public sealed class PcmVisualizationTapTests
{
    /// <summary>Pushed samples are handed back unchanged.</summary>
    [Fact]
    public void ReadNewest_ReturnsPushedSamples()
    {
        var tap = new PcmVisualizationTap(1024);
        var pushed = new[] { 0.1f, -0.1f, 0.2f, -0.2f, 0.3f, -0.3f };

        tap.Push(pushed);
        var destination = new float[pushed.Length];
        var frames = tap.ReadNewest(destination);

        Assert.Equal(3, frames);
        Assert.Equal(pushed, destination);
    }

    /// <summary>An empty tap returns nothing.</summary>
    [Fact]
    public void ReadNewest_ReturnsZeroWhenEmpty()
    {
        var tap = new PcmVisualizationTap(1024);

        Assert.Equal(0, tap.ReadNewest(new float[64]));
        Assert.Equal(0, tap.BufferedFrames);
    }

    /// <summary>A producer that runs ahead drops the oldest samples instead of blocking.</summary>
    [Fact]
    public void Push_DropsOldestSamplesOnOverflow()
    {
        var tap = new PcmVisualizationTap(256);
        for (var block = 0; block < 10; block++)
        {
            var samples = new float[256 * 2];
            Array.Fill(samples, block + 1);
            tap.Push(samples);
        }

        Assert.True(tap.BufferedFrames <= 256);

        var destination = new float[256 * 2];
        var frames = tap.ReadNewest(destination);

        Assert.Equal(256, frames);
        Assert.Equal(10f, destination[^1]);
    }

    /// <summary>A block larger than the ring buffer keeps its newest part.</summary>
    [Fact]
    public void Push_KeepsTheNewestPartOfAnOversizedBlock()
    {
        var tap = new PcmVisualizationTap(256);
        var samples = new float[1024 * 2];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = index;

        tap.Push(samples);
        var destination = new float[256 * 2];
        var frames = tap.ReadNewest(destination);

        Assert.Equal(256, frames);
        Assert.Equal(samples[^2], destination[^2]);
        Assert.Equal(samples[^1], destination[^1]);
    }

    /// <summary>Clearing discards the buffered samples.</summary>
    [Fact]
    public void Clear_DiscardsBufferedSamples()
    {
        var tap = new PcmVisualizationTap(1024);
        tap.Push(new float[256]);

        tap.Clear();

        Assert.Equal(0, tap.BufferedFrames);
    }

    /// <summary>A too small capacity is rejected.</summary>
    [Fact]
    public void Constructor_RejectsATinyCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcmVisualizationTap(16));
    }
}
