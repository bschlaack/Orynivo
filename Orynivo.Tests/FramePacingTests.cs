using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the visualizer's frame pacing, which keeps the render loop from busy-spinning while
/// still noticing a shutdown within a bounded time.
/// </summary>
public sealed class FramePacingTests
{
    /// <summary>A frame that used the whole interval waits for nothing.</summary>
    [Fact]
    public void NextWait_ReturnsZeroWhenTheIntervalIsUsedUp()
    {
        Assert.Equal(TimeSpan.Zero, FramePacing.NextWait(1d / 60d, 1d / 60d));
    }

    /// <summary>A frame that took longer than the interval does not wait at all.</summary>
    [Fact]
    public void NextWait_ReturnsZeroWhenTheFrameTookTooLong()
    {
        Assert.Equal(TimeSpan.Zero, FramePacing.NextWait(1d / 60d, 0.5d));
    }

    /// <summary>An idle loop waits for the remaining time of the interval.</summary>
    [Fact]
    public void NextWait_ReturnsTheRemainingTime()
    {
        var wait = FramePacing.NextWait(0.05d, 0.01d);

        Assert.Equal(0.04d, wait.TotalSeconds, 6);
    }

    /// <summary>The wait is capped so a shutdown is noticed while the loop sleeps.</summary>
    [Fact]
    public void NextWait_CapsTheWait()
    {
        var wait = FramePacing.NextWait(30d, 0d);

        Assert.Equal(FramePacing.MaximumWait, wait);
    }

    /// <summary>A zero interval never produces a wait or a negative duration.</summary>
    [Fact]
    public void NextWait_HandlesAZeroInterval()
    {
        Assert.Equal(TimeSpan.Zero, FramePacing.NextWait(0d, 0d));
        Assert.True(FramePacing.NextWait(double.NaN, 0d) >= TimeSpan.Zero);
    }
}
