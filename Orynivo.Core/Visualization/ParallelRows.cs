namespace Orynivo.Visualization;

/// <summary>
/// Splits a full-frame pass into row ranges and runs them in parallel. Every pass that uses it
/// must be row-independent: a pixel may only read the source buffer and write its own pixel, so
/// the result never depends on the split. Small frames stay on the calling thread, because the
/// coordination would cost more than the work.
/// </summary>
internal static class ParallelRows
{
    /// <summary>Rows below this count are processed on the calling thread.</summary>
    private const int MinimumRowsForParallelism = 32;

    /// <summary>
    /// Gets the number of workers a full-frame pass may use. It is capped so a machine with many
    /// cores does not turn one frame into a scheduling storm, and never exceeds the processor
    /// count.
    /// </summary>
    public static int WorkerCount { get; } = Math.Clamp(Environment.ProcessorCount, 1, 8);

    /// <summary>Runs a row-range body over the rows of a frame.</summary>
    /// <param name="height">Number of rows to cover.</param>
    /// <param name="body">
    /// Body receiving the worker index, the inclusive start row, and the exclusive end row. The
    /// index is stable per worker and can key per-worker scratch state.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The height is negative.</exception>
    public static void For(int height, Action<int, int, int> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (height == 0)
            return;

        var workers = Math.Min(WorkerCount, Math.Max(1, height / MinimumRowsForParallelism));
        if (workers <= 1)
        {
            body(0, 0, height);
            return;
        }

        Parallel.For(
            0,
            workers,
            new ParallelOptions { MaxDegreeOfParallelism = workers },
            worker =>
            {
                // Whole-row ranges, so the split never lands inside a row.
                var from = (int)(((long)height * worker) / workers);
                var to = (int)(((long)height * (worker + 1)) / workers);
                body(worker, from, to);
            });
    }
}
