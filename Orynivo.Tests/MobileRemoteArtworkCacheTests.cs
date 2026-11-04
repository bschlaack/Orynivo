using Orynivo.Mcp;
using Orynivo.Remote;
using Xunit;

namespace Orynivo.Tests;

/// <summary>Checks delayed artwork without any application window, device, network or personal library.</summary>
public sealed class MobileRemoteArtworkCacheTests
{
    /// <summary>Coalesces loads and refuses an earlier source after a newer playback starts.</summary>
    /// <returns>Completion after the synthetic old image is released.</returns>
    [Fact]
    public async Task NewSourceRejectsOldCompletionAndKeepsCurrentCache()
    {
        var cache = new MobileRemoteArtworkCache();
        cache.Update("old private source", null);
        var oldKey = cache.Key;
        var gate = new TaskCompletionSource<RemoteArtwork?>();
        var calls = 0;
        Task<RemoteArtwork?> Load() { calls++; return gate.Task; }
        var first = cache.GetAsync(Load);
        var second = cache.GetAsync(Load);
        Assert.Equal(1, calls);
        cache.Update("new private source", null);
        Assert.NotEqual(oldKey, cache.Key);
        var current = new RemoteArtwork([1, 2], "image/jpeg", "current");
        Assert.Same(current, await cache.GetAsync(() => Task.FromResult<RemoteArtwork?>(current)));
        gate.SetResult(new RemoteArtwork([3], "image/jpeg", "old"));
        Assert.Null(await first);
        Assert.Null(await second);
        Assert.Same(current, await cache.GetAsync(() => throw new InvalidOperationException()));
    }

    /// <summary>A delayed desktop image renews a previously missing thumbnail while unchanged state remains quiet.</summary>
    /// <returns>Completion after immediate synthetic cache reads.</returns>
    [Fact]
    public async Task PublishedImageRenewsMissingArtwork()
    {
        var cache = new MobileRemoteArtworkCache();
        cache.Update("source", null);
        var oldKey = cache.Key;
        Assert.Null(await cache.GetAsync(() => Task.FromResult<RemoteArtwork?>(null)));
        cache.Update("source", null);
        Assert.Equal(oldKey, cache.Key);
        var image = new object();
        cache.Update("source", image);
        Assert.NotEqual(oldKey, cache.Key);
        var thumbnail = new RemoteArtwork([1], "image/jpeg", "ready");
        Assert.Same(thumbnail, await cache.GetAsync(() => Task.FromResult<RemoteArtwork?>(thumbnail)));
    }
}
