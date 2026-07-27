using System.Text.Json;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies deterministic and transport-safe Fanart.tv thumbnail selection.</summary>
public sealed class FanartTvArtistImageServiceTests
{
    /// <summary>Selects the most-liked HTTPS artist thumbnail.</summary>
    [Fact]
    public void SelectBestArtistThumbnailUrl_PrefersLikesAndRequiresHttps()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "artistthumb": [
                { "url": "http://example.test/insecure.jpg", "likes": "99", "width": "1000", "height": "1000" },
                { "url": "https://example.test/low.jpg", "likes": "3", "width": "1000", "height": "1000" },
                { "url": "https://example.test/best.jpg", "likes": "12", "width": "1000", "height": "1000" }
              ]
            }
            """);

        var result = FanartTvArtistImageService.SelectBestArtistThumbnailUrl(document.RootElement);

        Assert.Equal("https://example.test/best.jpg", result);
    }

    /// <summary>Returns no image when Fanart.tv has no artist thumbnails.</summary>
    [Fact]
    public void SelectBestArtistThumbnailUrl_ReturnsNullWithoutArtistThumbs()
    {
        using var document = JsonDocument.Parse("""{ "hdmusiclogo": [] }""");

        Assert.Null(FanartTvArtistImageService.SelectBestArtistThumbnailUrl(document.RootElement));
    }
}
