using Orynivo.Streaming;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies that the authenticated stream URL carries the optional lossy
/// transcode parameters only when a streaming quality is configured.
/// </summary>
public sealed class OrynivoServerStreamUrlTests
{
    private static OrynivoServerSettings Server() => new()
    {
        Id = "server-1",
        Name = "Test",
        BaseUrl = "http://host:5280/",
        ApiKey = "secret key"
    };

    /// <summary>Without a configured quality the original stream is requested.</summary>
    [Fact]
    public void GetStreamUrl_WithoutQualityOmitsTranscodeParameters()
    {
        var url = OrynivoServerClient.GetStreamUrl(Server(), 42);

        Assert.Contains("/api/stream/42", url);
        Assert.DoesNotContain("format=", url);
        Assert.DoesNotContain("bitrate=", url);
    }

    /// <summary>A configured format and bitrate are appended.</summary>
    [Fact]
    public void GetStreamUrl_WithFormatAndBitrateIncludesBoth()
    {
        var server = Server();
        server.StreamingFormat = "opus";
        server.StreamingBitrateKbps = 128;

        var url = OrynivoServerClient.GetStreamUrl(server, 42);

        Assert.Contains("format=opus", url);
        Assert.Contains("bitrate=128", url);
    }

    /// <summary>A format without a bitrate uses the server's format default.</summary>
    [Fact]
    public void GetStreamUrl_WithFormatOnlyOmitsBitrate()
    {
        var server = Server();
        server.StreamingFormat = "aac";

        var url = OrynivoServerClient.GetStreamUrl(server, 42);

        Assert.Contains("format=aac", url);
        Assert.DoesNotContain("bitrate=", url);
    }

    /// <summary>The API key is still URL-escaped when quality parameters are present.</summary>
    [Fact]
    public void GetStreamUrl_EscapesApiKey()
    {
        var server = Server();
        server.StreamingFormat = "opus";

        var url = OrynivoServerClient.GetStreamUrl(server, 42);

        Assert.Contains("key=secret%20key", url);
    }
}
