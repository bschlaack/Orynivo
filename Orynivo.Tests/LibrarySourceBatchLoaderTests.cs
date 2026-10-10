using Xunit;

namespace Orynivo.Tests;

/// <summary>Checks parallel admission, deterministic ordering, failure isolation and cancellation.</summary>
public sealed class LibrarySourceBatchLoaderTests
{
    /// <summary>An excluded source set is an empty selection, not an outage or a source-backed catalog snapshot.</summary>
    [Fact]
    public async Task NoRequestedSources_IsCompleteButNotCatalogCacheable()
    {
        var result = await LibrarySourceBatchLoader.LoadAsync<int, int>([], _ => "unused",
            (_, _) => throw new InvalidOperationException("Excluded source queried"), CancellationToken.None);
        Assert.Equal(LibraryLoadStatus.Complete, result.Status);
        Assert.Empty(result.Rows);
        Assert.Empty(result.Sources);
        Assert.False(new LibraryViewCache<int>(3).TryStore("selection", 0, result));
    }

    /// <summary>A queued source gets its own budget after the preceding source times out.</summary>
    [Fact]
    public async Task QueueWait_DoesNotConsumeTheNextSourceBudget()
    {
        var result = await LibrarySourceBatchLoader.LoadAsync<int, int>([0, 1], i => $"source:{i}",
            async (i, token) =>
            {
                if (i == 0) await Task.Delay(Timeout.Infinite, token);
                token.ThrowIfCancellationRequested();
                return new[] { i };
            }, CancellationToken.None, maximumConcurrency: 1, timeout: TimeSpan.FromMilliseconds(10));
        Assert.Equal(LibrarySourceLoadStatus.TimedOut, result.Sources[0].Status);
        Assert.Equal(LibrarySourceLoadStatus.Success, result.Sources[1].Status);
        Assert.Equal(new[] { 1 }, result.Rows);
    }

    /// <summary>Three sources may start together, but failures and completion order never reorder usable rows.</summary>
    [Fact]
    public async Task ParallelLoads_AreBoundedOrderedAndFailureIsolated()
    {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var peak = 0;
        var started = 0;
        var task = LibrarySourceBatchLoader.LoadAsync<int, int>([0, 1, 2, 3, 4], i => $"source:{i}",
            async (i, token) =>
            {
                var count = Interlocked.Increment(ref active);
                Interlocked.Exchange(ref peak, Math.Max(peak, count));
                if (Interlocked.Increment(ref started) == 3) admitted.SetResult();
                try
                {
                    await release.Task.WaitAsync(token);
                    if (i == 2) throw new InvalidOperationException("Private failure must not escape");
                    return new[] { i };
                }
                finally { Interlocked.Decrement(ref active); }
            }, CancellationToken.None);
        await admitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, started);
        Assert.Equal(3, active);
        release.SetResult();
        var result = await task;
        Assert.Equal(3, peak);
        Assert.Equal(new[] { 0, 1, 3, 4 }, result.Rows);
        Assert.Equal(Enumerable.Range(0, 5).Select(i => $"source:{i}"), result.Sources.Select(s => s.SourceKey));
        Assert.Equal(LibrarySourceLoadStatus.Failed, result.Sources[2].Status);
        Assert.Equal(LibraryLoadStatus.Partial, result.Status);
    }

    /// <summary>Cancellation stops admitted work and prevents queued sources from starting.</summary>
    [Fact]
    public async Task Cancellation_DoesNotStartQueuedSources()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = LibrarySourceBatchLoader.LoadAsync<int, int>([0, 1, 2], i => $"source:{i}",
            async (_, token) =>
            {
                Interlocked.Increment(ref calls);
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return Array.Empty<int>();
            }, cancellation.Token, maximumConcurrency: 1);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        var result = await task;
        Assert.Equal(1, calls);
        Assert.Empty(result.Rows);
        Assert.All(result.Sources, s => Assert.Equal(LibrarySourceLoadStatus.Cancelled, s.Status));
    }

    /// <summary>A timed-out source remains distinct from a successful empty source and recovers on retry.</summary>
    [Fact]
    public async Task TimeoutThenEmptySuccess_Recovers()
    {
        var timedOut = await LibrarySourceBatchLoader.LoadAsync<int, int>([0], _ => "source",
            async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Array.Empty<int>(); },
            CancellationToken.None, timeout: TimeSpan.FromMilliseconds(10));
        Assert.Equal(LibrarySourceLoadStatus.TimedOut, timedOut.Sources.Single().Status);
        var recovered = await LibrarySourceBatchLoader.LoadAsync<int, int>([0], _ => "source",
            (_, _) => Task.FromResult<IReadOnlyList<int>>([]), CancellationToken.None);
        Assert.Equal(LibraryLoadStatus.Complete, recovered.Status);
        Assert.Empty(recovered.Rows);
    }
}
