using Xunit;

namespace Orynivo.Tests;

/// <summary>Verifies source failure isolation, cancellation, complete paging, and cache recovery.</summary>
public sealed class LibraryLoadingTests
{
    /// <summary>A successfully empty catalog is complete and can be cached.</summary>
    [Fact]
    public async Task EmptySuccess_IsCompleteAndCacheable()
    {
        var result = await LoadSuccess("local", []);
        var cache = new LibraryViewCache<int>(3);

        Assert.Equal(LibraryLoadStatus.Complete, result.Status);
        Assert.True(cache.TryStore("Tracks", cache.Generation, result));
        Assert.True(cache.TryGet("Tracks", cache.Generation, out var cached));
        Assert.Empty(cached!.Rows);
    }

    /// <summary>A failed source preserves the other sources but prevents success-cache writes.</summary>
    [Fact]
    public async Task Failure_PreservesAvailableRowsAndIsNotCached()
    {
        var local = await LoadSuccess("local", [1, 2]);
        var failed = await LibrarySourceLoader.LoadAsync<int>("server:a",
            _ => throw new IOException("Synthetic private detail"), CancellationToken.None);
        var result = LibraryLoadResult<int>.Combine([local, failed]);
        var cache = new LibraryViewCache<int>(3);

        Assert.Equal(LibraryLoadStatus.Partial, result.Status);
        Assert.Equal(new[] { 1, 2 }, result.Rows);
        Assert.Equal(LibrarySourceLoadStatus.Failed, result.Sources[1].Status);
        Assert.False(cache.TryStore("Tracks", cache.Generation, result));
        Assert.False(cache.TryGet("Tracks", cache.Generation, out _));
    }

    /// <summary>A failing database is distinct from a valid empty library and does not hide remote rows.</summary>
    [Fact]
    public async Task LocalFailure_IsNotEmptySuccess()
    {
        var failed = await LibrarySourceLoader.LoadAsync<int>("local",
            _ => throw new IOException(), CancellationToken.None);
        var remote = await LoadSuccess("server:a", [7]);

        Assert.Equal(LibraryLoadStatus.Failed, failed.Status);
        var result = LibraryLoadResult<int>.Combine([failed, remote]);
        Assert.Equal(LibraryLoadStatus.Partial, result.Status);
        Assert.Equal(new[] { 7 }, result.Rows);
    }

    /// <summary>A timeout belongs to the source and does not cancel a successful sibling.</summary>
    [Fact]
    public async Task Timeout_IsDistinctFromNavigationCancellation()
    {
        var timedOut = await LibrarySourceLoader.LoadAsync<int>("server:a", async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        }, CancellationToken.None, TimeSpan.FromMilliseconds(10));
        var result = LibraryLoadResult<int>.Combine([await LoadSuccess("local", [1]), timedOut]);

        Assert.Equal(LibrarySourceLoadStatus.TimedOut, timedOut.Sources[0].Status);
        Assert.Equal(LibraryLoadStatus.Partial, result.Status);
        Assert.Equal(new[] { 1 }, result.Rows);
    }

    /// <summary>An HTTP-client timeout remains a source timeout even before the outer budget expires.</summary>
    [Fact]
    public async Task TransportCancellation_IsSourceTimeout()
    {
        var result = await LibrarySourceLoader.LoadAsync<int>("server:a",
            _ => throw new TaskCanceledException(), CancellationToken.None);

        Assert.Equal(LibrarySourceLoadStatus.TimedOut, result.Sources[0].Status);
    }

    /// <summary>Navigation cancellation discards late rows even from a provider that ignores its token.</summary>
    [Fact]
    public async Task CancelledNavigation_DiscardsLateSuccess()
    {
        using var navigation = new CancellationTokenSource();
        var response = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = LibrarySourceLoader.LoadAsync<int>("server:a", _ => response.Task, navigation.Token);
        navigation.Cancel();
        response.SetResult([7]);
        var result = LibraryLoadResult<int>.Combine([await LoadSuccess("local", [1]), await pending]);

        Assert.Equal(LibraryLoadStatus.Cancelled, result.Status);
        Assert.Equal(LibrarySourceLoadStatus.Cancelled, result.Sources[1].Status);
        Assert.Equal(new[] { 1 }, result.Rows);
        Assert.False(new LibraryViewCache<int>(3).TryStore("Tracks", 0, result));
    }

    /// <summary>A load finishing after invalidation cannot repopulate the new generation.</summary>
    [Fact]
    public async Task InvalidatedLoad_CannotRepopulateCache()
    {
        var cache = new LibraryViewCache<int>(3);
        var generation = cache.Generation;
        var response = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = LibrarySourceLoader.LoadAsync<int>("local", _ => response.Task, CancellationToken.None);
        cache.Invalidate();
        response.SetResult([1]);

        Assert.False(cache.TryStore("Tracks", generation, await pending));
        Assert.False(cache.TryGet("Tracks", cache.Generation, out _));
    }

    /// <summary>A new successful request can recover a previously unavailable source without cache poisoning.</summary>
    [Fact]
    public async Task RecoveredSource_ReplacesPartialLoadWithCompleteSnapshot()
    {
        var available = false;
        Task<IReadOnlyList<int>> LoadRemote(CancellationToken _) => available
            ? Task.FromResult<IReadOnlyList<int>>([2])
            : throw new IOException();
        var cache = new LibraryViewCache<int>(3);
        var local = await LoadSuccess("local", [1]);
        var partial = LibraryLoadResult<int>.Combine([local,
            await LibrarySourceLoader.LoadAsync("server:a", LoadRemote, CancellationToken.None)]);
        Assert.False(cache.TryStore("Tracks", cache.Generation, partial));

        available = true;
        var complete = LibraryLoadResult<int>.Combine([local,
            await LibrarySourceLoader.LoadAsync("server:a", LoadRemote, CancellationToken.None)]);
        Assert.True(cache.TryStore("Tracks", cache.Generation, complete));
        Assert.True(cache.TryGet("Tracks", cache.Generation, out var cached));
        Assert.Equal(new[] { 1, 2 }, cached!.Rows);
        Assert.Equal(LibraryLoadStatus.Complete, cached.Status);
    }

    /// <summary>A later failed page must not publish or cache an apparently complete prefix.</summary>
    [Fact]
    public async Task FailedLaterPage_DiscardsPrefixAndRecoversOnRetry()
    {
        var available = false;
        Task<IReadOnlyList<int>> LoadPage(int page, CancellationToken _) => page switch
        {
            0 => Task.FromResult<IReadOnlyList<int>>([1, 2]),
            1 when available => Task.FromResult<IReadOnlyList<int>>([3]),
            _ => throw new IOException()
        };
        var result = await LibrarySourceLoader.LoadAsync<int>("server:a",
            async token => await LibraryCatalogPaging.LoadAllAsync(LoadPage, 2, token), CancellationToken.None);
        Assert.Equal(LibraryLoadStatus.Failed, result.Status);
        Assert.Empty(result.Rows);

        available = true;
        var recovered = await LibrarySourceLoader.LoadAsync<int>("server:a",
            async token => await LibraryCatalogPaging.LoadAllAsync(LoadPage, 2, token), CancellationToken.None);
        Assert.Equal(LibraryLoadStatus.Complete, recovered.Status);
        Assert.Equal(new[] { 1, 2, 3 }, recovered.Rows);
    }

    /// <summary>Cache access refreshes eviction order and invalidation removes every entry.</summary>
    [Fact]
    public async Task Cache_IsBoundedAndInvalidatesEveryView()
    {
        var cache = new LibraryViewCache<int>(2);
        var result = await LoadSuccess("local", [1]);
        cache.TryStore("Artists", cache.Generation, result);
        cache.TryStore("Albums", cache.Generation, result);
        Assert.True(cache.TryGet("Artists", cache.Generation, out _));
        cache.TryStore("Tracks", cache.Generation, result);
        Assert.False(cache.TryGet("Albums", cache.Generation, out _));
        Assert.True(cache.TryGet("Artists", cache.Generation, out _));

        cache.Invalidate();
        Assert.False(cache.TryGet("Artists", cache.Generation, out _));
        Assert.False(cache.TryGet("Tracks", cache.Generation, out _));
    }

    /// <summary>Loads a successful synthetic source through the production outcome classifier.</summary>
    /// <param name="source">Opaque fixture source identity.</param>
    /// <param name="rows">Synthetic catalog rows.</param>
    /// <returns>The successful classified result.</returns>
    private static Task<LibraryLoadResult<int>> LoadSuccess(string source, IReadOnlyList<int> rows) =>
        LibrarySourceLoader.LoadAsync(source, _ => Task.FromResult(rows), CancellationToken.None);
}
