using Orynivo.Scrobbling;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the offline Last.fm scrobble queue: persistence, clearing, and the
/// bounded retention of the newest entries.
/// </summary>
public sealed class PendingScrobbleStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "orynivo-scrobble-tests",
        Guid.NewGuid().ToString("N"),
        "pending.json");

    /// <summary>Removes the temporary queue file.</summary>
    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true); } catch { }
    }

    /// <summary>An enqueued scrobble survives a reload.</summary>
    [Fact]
    public void Enqueue_ThenLoad_RoundTrips()
    {
        var store = new PendingScrobbleStore(_path);
        store.Enqueue(new PendingScrobble("Artist", "Title", "Album", 200, 1700000000));

        var entry = Assert.Single(store.Load());
        Assert.Equal("Artist", entry.Artist);
        Assert.Equal("Title", entry.Title);
        Assert.Equal("Album", entry.Album);
        Assert.Equal(200, entry.Duration);
        Assert.Equal(1700000000, entry.PlayedAtUnix);
    }

    /// <summary>A missing file yields an empty queue.</summary>
    [Fact]
    public void Load_MissingFileIsEmpty()
        => Assert.Empty(new PendingScrobbleStore(_path).Load());

    /// <summary>Clearing removes every queued entry.</summary>
    [Fact]
    public void Clear_RemovesEntries()
    {
        var store = new PendingScrobbleStore(_path);
        store.Enqueue(new PendingScrobble("A", "B", null, null, 1));

        store.Clear();

        Assert.Empty(store.Load());
    }

    /// <summary>An unreadable file yields an empty queue instead of throwing.</summary>
    [Fact]
    public void Load_CorruptFileIsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not json");

        Assert.Empty(new PendingScrobbleStore(_path).Load());
    }

    /// <summary>Trimming keeps the newest entries when the bound is exceeded.</summary>
    [Fact]
    public void Trim_RetainsNewestEntries()
    {
        var entries = Enumerable.Range(1, PendingScrobbleStore.MaximumEntries + 10)
            .Select(index => new PendingScrobble("a", index.ToString(), null, null, index))
            .ToList();

        var trimmed = PendingScrobbleStore.Trim(entries);

        Assert.Equal(PendingScrobbleStore.MaximumEntries, trimmed.Count);
        Assert.Equal("11", trimmed[0].Title);
        Assert.Equal((PendingScrobbleStore.MaximumEntries + 10).ToString(), trimmed[^1].Title);
    }

    /// <summary>Trimming leaves short lists unchanged.</summary>
    [Fact]
    public void Trim_LeavesShortListsUnchanged()
    {
        var entries = new List<PendingScrobble> { new("a", "b", null, null, 1) };

        Assert.Same(entries, PendingScrobbleStore.Trim(entries));
    }
}
