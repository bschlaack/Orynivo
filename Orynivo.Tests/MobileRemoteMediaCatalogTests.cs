using Orynivo.Library;
using Orynivo.Remote;
using Xunit;

namespace Orynivo.Tests;

/// <summary>Checks bounded opaque media identities without opening a catalog or audio device.</summary>
public sealed class MobileRemoteMediaCatalogTests
{
    /// <summary>Preserves original episode ownership while refusing cross-kind and URL inputs.</summary>
    [Fact]
    public void IdentityRetainsSourceAndRejectsOtherKinds()
    {
        var catalog = new MobileRemoteMediaCatalog();
        var podcast = new PodcastRecord(17, 5, "Synthetic show", null, "https://example.invalid/feed", null, null);
        var episode = new PodcastEpisode("key", "Synthetic episode", "https://example.invalid/audio", null, null, null);
        var item = new MobileRemotePodcastEpisode(podcast, episode);
        var id = catalog.Register(item);
        Assert.True(Guid.TryParseExact(id, "N", out _));
        Assert.Same(item, catalog.Resolve<MobileRemotePodcastEpisode>(id));
        Assert.Equal(17, catalog.Resolve<MobileRemotePodcastEpisode>(id)!.Podcast.Id);
        Assert.Null(catalog.Resolve<PodcastRecord>(id));
        Assert.Null(catalog.Resolve<MobileRemotePodcastEpisode>(episode.AudioUrl));
        Assert.Equal(id, catalog.Register(item));
    }

    /// <summary>Evicts old identities without rebinding them to a later source.</summary>
    [Fact]
    public void CapacityEvictsOldIdentityWithoutReusingIt()
    {
        var catalog = new MobileRemoteMediaCatalog();
        var first = catalog.Register("first");
        for (var i = 0; i < 2048; i++) catalog.Register("source " + i);
        Assert.Null(catalog.Resolve<string>(first));
        var latest = catalog.Register("source 2047");
        Assert.Equal("source 2047", catalog.Resolve<string>(latest));
    }
}
