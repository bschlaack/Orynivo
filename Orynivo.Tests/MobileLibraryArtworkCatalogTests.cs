using Orynivo.Remote;
using Xunit;

namespace Orynivo.Tests;

/// <summary>Checks opaque artwork registration, bounded retention and source isolation.</summary>
public sealed class MobileLibraryArtworkCatalogTests
{
    /// <summary>Only registered random keys resolve; repeated sources share keys and old entries expire.</summary>
    [Fact]
    public void KeysAreOpaqueBoundedAndNeverAcceptBrowserLocations()
    {
        var catalog = new MobileLibraryArtworkCatalog();
        Assert.Null(catalog.Register(null));
        var first = catalog.Register("synthetic-private-image")!;
        Assert.True(Guid.TryParseExact(first, "N", out _));
        Assert.Equal(first, catalog.Register("synthetic-private-image"));
        Assert.Equal("synthetic-private-image", catalog.Resolve(first));
        Assert.Null(catalog.Resolve("synthetic-private-image"));
        Assert.Null(catalog.Resolve(Guid.NewGuid().ToString("N")));
        for (var i = 0; i < 2048; i++) catalog.Register("fixture-" + i);
        Assert.Null(catalog.Resolve(first));
        var last = catalog.Register("fixture-2047")!;
        Assert.Equal("fixture-2047", catalog.Resolve(last));
    }
}
