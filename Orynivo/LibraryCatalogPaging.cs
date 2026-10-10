namespace Orynivo;

/// <summary>Resolves complete paged catalogs without turning a failed page into a final empty page.</summary>
internal static class LibraryCatalogPaging
{
    /// <summary>Loads pages until a successful short page confirms the end of the catalog.</summary>
    /// <typeparam name="T">Catalog row type.</typeparam>
    /// <param name="loadPage">Strict page loader that propagates request failures.</param>
    /// <param name="pageSize">Positive page size supported by the server.</param>
    /// <param name="cancellationToken">Owning source cancellation.</param>
    /// <returns>The complete ordered catalog; failures never return the partial prefix.</returns>
    internal static async Task<List<T>> LoadAllAsync<T>(
        Func<int, CancellationToken, Task<IReadOnlyList<T>>> loadPage,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var rows = new List<T>();
        for (var page = 0; ; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = await loadPage(page, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            rows.AddRange(batch);
            if (batch.Count < pageSize)
                return rows;
        }
    }
}
