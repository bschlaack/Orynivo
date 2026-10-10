namespace Orynivo;

/// <summary>Loads independent sources with bounded concurrency and stable, credential-free outcomes.</summary>
internal static class LibrarySourceBatchLoader
{
    /// <summary>Loads sources in parallel while preserving input order and isolating failures.</summary>
    /// <typeparam name="TSource">Source context used only by the supplied loader.</typeparam>
    /// <typeparam name="T">Returned row type.</typeparam>
    /// <param name="sources">Captured source list.</param>
    /// <param name="sourceKey">Opaque outcome identity selector.</param>
    /// <param name="load">Asynchronous source loader.</param>
    /// <param name="cancellationToken">Owning navigation cancellation.</param>
    /// <param name="maximumConcurrency">Maximum simultaneous source loads.</param>
    /// <param name="timeout">Per-source budget, starting after admission.</param>
    /// <returns>Ordered available rows and explicit outcomes, including cancelled queued sources.</returns>
    internal static async Task<LibraryLoadResult<T>> LoadAsync<TSource, T>(
        IReadOnlyList<TSource> sources,
        Func<TSource, string> sourceKey,
        Func<TSource, CancellationToken, Task<IReadOnlyList<T>>> load,
        CancellationToken cancellationToken,
        int maximumConcurrency = 3,
        TimeSpan? timeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        using var slots = new SemaphoreSlim(maximumConcurrency);
        var tasks = sources.Select(async source =>
        {
            var key = sourceKey(source);
            try
            {
                await slots.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new LibraryLoadResult<T>([], [new(key, LibrarySourceLoadStatus.Cancelled)]);
            }
            try
            {
                return await LibrarySourceLoader.LoadAsync(key, token => load(source, token),
                    cancellationToken, timeout);
            }
            finally
            {
                slots.Release();
            }
        }).ToArray();
        return LibraryLoadResult<T>.Combine(await Task.WhenAll(tasks));
    }
}
