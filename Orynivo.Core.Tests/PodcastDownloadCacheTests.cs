using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the pure naming and retention decisions of the podcast download cache.
/// </summary>
public sealed class PodcastDownloadCacheTests
{
    /// <summary>The same episode always maps to the same safe file name.</summary>
    [Fact]
    public void BuildCacheFileName_IsStableAndSafe()
    {
        var first = PodcastDownloadCache.BuildCacheFileName(
            "1234",
            "episode-key",
            "https://example.org/audio/Episode%201.mp3?token=secret");
        var second = PodcastDownloadCache.BuildCacheFileName(
            "1234",
            "episode-key",
            "https://example.org/audio/Episode%201.mp3?token=other");

        Assert.Equal(first, second);
        Assert.Equal(".mp3", Path.GetExtension(first));
        Assert.StartsWith("episode-", first, StringComparison.Ordinal);
        Assert.DoesNotContain(' ', first);
        Assert.DoesNotContain('?', first);
    }

    /// <summary>Different episodes map to different files.</summary>
    [Fact]
    public void BuildCacheFileName_DiffersPerEpisode()
    {
        var first = PodcastDownloadCache.BuildCacheFileName("1234", "one", "https://example.org/a.mp3");
        var second = PodcastDownloadCache.BuildCacheFileName("1234", "two", "https://example.org/a.mp3");

        Assert.NotEqual(first, second);
    }

    /// <summary>An unusable URL falls back to the neutral extension.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://example.org/audio/track")]
    [InlineData("https://example.org/audio/track.m-p3")]
    public void BuildCacheFileName_WithoutUsableExtensionFallsBack(string? audioUrl)
    {
        var name = PodcastDownloadCache.BuildCacheFileName("1234", "key", audioUrl);

        Assert.EndsWith(PodcastDownloadCache.FallbackExtension, name, StringComparison.Ordinal);
    }

    /// <summary>A cache that fits the limit is left alone.</summary>
    [Fact]
    public void SelectForEviction_WithinLimit_KeepsEverything()
    {
        var entries = new[]
        {
            new PodcastDownloadEntry("a.audio", 100, 10),
            new PodcastDownloadEntry("b.audio", 100, 20)
        };

        Assert.Empty(PodcastDownloadCache.SelectForEviction(entries, 500));
    }

    /// <summary>Least recently used entries are evicted first until the cache fits.</summary>
    [Fact]
    public void SelectForEviction_RemovesLeastRecentlyUsedFirst()
    {
        var entries = new[]
        {
            new PodcastDownloadEntry("oldest.audio", 100, 1),
            new PodcastDownloadEntry("middle.audio", 100, 5),
            new PodcastDownloadEntry("newest.audio", 100, 9)
        };

        var evicted = PodcastDownloadCache.SelectForEviction(entries, 200);

        Assert.Equal(["oldest.audio"], evicted);
    }

    /// <summary>The most recently used entry survives an oversized cache.</summary>
    [Fact]
    public void SelectForEviction_KeepsTheNewestEntry()
    {
        var entries = new[]
        {
            new PodcastDownloadEntry("oldest.audio", 900, 1),
            new PodcastDownloadEntry("newest.audio", 900, 9)
        };

        var evicted = PodcastDownloadCache.SelectForEviction(entries, 100);

        Assert.Equal(["oldest.audio"], evicted);
    }

    /// <summary>Eviction is disabled for a non-positive limit or a single entry.</summary>
    [Fact]
    public void SelectForEviction_DisabledLimitKeepsEverything()
    {
        var entries = new[]
        {
            new PodcastDownloadEntry("a.audio", 900, 1),
            new PodcastDownloadEntry("b.audio", 900, 2)
        };

        Assert.Empty(PodcastDownloadCache.SelectForEviction(entries, 0));
        Assert.Empty(PodcastDownloadCache.SelectForEviction(entries, -1));
        Assert.Empty(PodcastDownloadCache.SelectForEviction([entries[0]], 1));
    }

    /// <summary>Identical timestamps fall back to a stable path order.</summary>
    [Fact]
    public void SelectForEviction_IdenticalTimestamps_AreDeterministic()
    {
        var entries = new[]
        {
            new PodcastDownloadEntry("b.audio", 100, 5),
            new PodcastDownloadEntry("a.audio", 100, 5)
        };

        var first = PodcastDownloadCache.SelectForEviction(entries, 100);
        var second = PodcastDownloadCache.SelectForEviction(entries, 100);

        Assert.Equal(["a.audio"], first);
        Assert.Equal(first, second);
    }
}
