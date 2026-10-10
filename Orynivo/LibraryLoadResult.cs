namespace Orynivo;

/// <summary>Describes the terminal outcome of one catalog source without retaining errors or connection details.</summary>
internal enum LibrarySourceLoadStatus
{
    /// <summary>The source completed, possibly with no matching rows.</summary>
    Success,
    /// <summary>The source could not provide a valid catalog.</summary>
    Failed,
    /// <summary>The source exceeded its request budget.</summary>
    TimedOut,
    /// <summary>The owning navigation was cancelled.</summary>
    Cancelled
}

/// <summary>Classifies a combined catalog load independently of its row count.</summary>
internal enum LibraryLoadStatus
{
    /// <summary>Every requested source completed successfully.</summary>
    Complete,
    /// <summary>At least one source succeeded and another was unavailable.</summary>
    Partial,
    /// <summary>No requested source succeeded.</summary>
    Failed,
    /// <summary>Navigation cancellation prevents publishing the result.</summary>
    Cancelled
}

/// <summary>Records a source outcome using only an opaque provider identity.</summary>
/// <param name="SourceKey">Credential-free local or server identity.</param>
/// <param name="Status">Terminal source outcome.</param>
internal sealed record LibrarySourceLoadOutcome(string SourceKey, LibrarySourceLoadStatus Status);

/// <summary>Retains available rows and explicit outcomes for every requested source.</summary>
/// <typeparam name="T">In-memory catalog row type; rows are never diagnostic payloads.</typeparam>
/// <param name="Rows">Rows from successful sources.</param>
/// <param name="Sources">Credential-free source outcomes.</param>
internal sealed record LibraryLoadResult<T>(IReadOnlyList<T> Rows, IReadOnlyList<LibrarySourceLoadOutcome> Sources)
{
    /// <summary>Gets completeness independently of row count; a selection with no requested sources is complete.</summary>
    internal LibraryLoadStatus Status => Sources.Any(source => source.Status == LibrarySourceLoadStatus.Cancelled)
        ? LibraryLoadStatus.Cancelled
        : Sources.All(source => source.Status == LibrarySourceLoadStatus.Success)
            ? LibraryLoadStatus.Complete
            : Sources.Any(source => source.Status == LibrarySourceLoadStatus.Success)
                ? LibraryLoadStatus.Partial
                : LibraryLoadStatus.Failed;

    /// <summary>Combines independent source loads while preserving source order and successful rows.</summary>
    /// <param name="results">Terminal results of all requested sources.</param>
    /// <returns>One result containing every source outcome.</returns>
    internal static LibraryLoadResult<T> Combine(IEnumerable<LibraryLoadResult<T>> results)
    {
        var completed = results.ToArray();
        return new(completed.SelectMany(result => result.Rows).ToList(),
            completed.SelectMany(result => result.Sources).ToArray());
    }
}

/// <summary>Isolates source failures and cancellation without retaining exception text.</summary>
internal static class LibrarySourceLoader
{
    /// <summary>Loads one source within an optional independent budget.</summary>
    /// <typeparam name="T">Catalog row type.</typeparam>
    /// <param name="sourceKey">Opaque provider identity; never a URL or display name.</param>
    /// <param name="load">Source request that observes the supplied token.</param>
    /// <param name="cancellationToken">Owning navigation cancellation.</param>
    /// <param name="timeout">Optional source request budget.</param>
    /// <returns>Successful rows or an explicit sanitized source outcome.</returns>
    internal static async Task<LibraryLoadResult<T>> LoadAsync<T>(
        string sourceKey,
        Func<CancellationToken, Task<IReadOnlyList<T>>> load,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        using var budget = new CancellationTokenSource();
        if (timeout.HasValue)
            budget.CancelAfter(timeout.Value);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var rows = await load(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            return new(rows, [new(sourceKey, LibrarySourceLoadStatus.Success)]);
        }
        catch (OperationCanceledException)
        {
            return new([], [new(sourceKey, cancellationToken.IsCancellationRequested
                ? LibrarySourceLoadStatus.Cancelled
                : LibrarySourceLoadStatus.TimedOut)]);
        }
        catch (Exception)
        {
            return new([], [new(sourceKey, cancellationToken.IsCancellationRequested
                ? LibrarySourceLoadStatus.Cancelled
                : budget.IsCancellationRequested
                    ? LibrarySourceLoadStatus.TimedOut
                    : LibrarySourceLoadStatus.Failed)]);
        }
    }
}
