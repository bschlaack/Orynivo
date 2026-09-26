namespace Orynivo.Visualization;

/// <summary>
/// Computes how long the visualizer's render loop should wait before the next frame. Keeping the
/// arithmetic out of the window makes it testable: the loop must neither busy-spin when a frame
/// is cheap nor drift when a frame takes longer than the interval, and a shutdown must be noticed
/// within a bounded time even when the loop is sleeping.
/// </summary>
internal static class FramePacing
{
    /// <summary>Gets the longest single wait, so a stopped loop still notices a shutdown.</summary>
    public static readonly TimeSpan MaximumWait = TimeSpan.FromMilliseconds(250);

    /// <summary>Computes the wait before the next frame.</summary>
    /// <param name="intervalSeconds">Target interval between two frames.</param>
    /// <param name="secondsSinceLastFrame">Seconds since the previous frame started.</param>
    /// <returns>
    /// The wait, never negative and never longer than <see cref="MaximumWait"/>.
    /// </returns>
    public static TimeSpan NextWait(double intervalSeconds, double secondsSinceLastFrame)
    {
        var remaining = intervalSeconds - secondsSinceLastFrame;
        if (double.IsNaN(remaining) || remaining <= 0d)
            return TimeSpan.Zero;

        return remaining >= MaximumWait.TotalSeconds ? MaximumWait : TimeSpan.FromSeconds(remaining);
    }
}
