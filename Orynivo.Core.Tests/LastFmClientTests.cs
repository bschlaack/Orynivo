using System.Net;
using System.Text;
using Orynivo.Scrobbling;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the Last.fm love and unlove requests without contacting the service.
/// </summary>
public sealed class LastFmClientTests
{
    /// <summary>Loving a track signs and sends a <c>track.love</c> request.</summary>
    [Fact]
    public async Task SetTrackLovedAsync_LoveSendsSignedRequest()
    {
        var handler = new CapturingHandler("""{"track":{}}""");
        using var http = new HttpClient(handler);
        var client = new LastFmClient(http, "api-key", "api-secret");

        var accepted = await client.SetTrackLovedAsync(
            new LastFmTrack("Artist", "Title", "Album", 200),
            loved: true,
            "session-key");

        Assert.True(accepted);
        var body = handler.LastRequestBody!;
        Assert.Equal("track.love", body["method"]);
        Assert.Equal("Artist", body["artist"]);
        Assert.Equal("Title", body["track"]);
        Assert.Equal("session-key", body["sk"]);
        Assert.Equal("api-key", body["api_key"]);
        Assert.False(string.IsNullOrWhiteSpace(body["api_sig"]));
    }

    /// <summary>Unloving a track uses <c>track.unlove</c>.</summary>
    [Fact]
    public async Task SetTrackLovedAsync_UnloveUsesUnloveMethod()
    {
        var handler = new CapturingHandler("""{"track":{}}""");
        using var http = new HttpClient(handler);
        var client = new LastFmClient(http, "api-key", "api-secret");

        await client.SetTrackLovedAsync(new LastFmTrack("Artist", "Title", null, null), loved: false, "session-key");

        Assert.Equal("track.unlove", handler.LastRequestBody!["method"]);
    }

    /// <summary>A Last.fm error response reports failure.</summary>
    [Fact]
    public async Task SetTrackLovedAsync_ErrorResponseReturnsFalse()
    {
        var handler = new CapturingHandler("""{"error":6,"message":"Invalid parameters"}""");
        using var http = new HttpClient(handler);
        var client = new LastFmClient(http, "api-key", "api-secret");

        var accepted = await client.SetTrackLovedAsync(
            new LastFmTrack("Artist", "Title", null, null),
            loved: true,
            "session-key");

        Assert.False(accepted);
    }

    /// <summary>A track without an artist or title is rejected without a request.</summary>
    [Theory]
    [InlineData("", "Title")]
    [InlineData("Artist", "")]
    [InlineData("   ", "Title")]
    public async Task SetTrackLovedAsync_WithoutArtistOrTitleSendsNothing(string artist, string title)
    {
        var handler = new CapturingHandler("""{"track":{}}""");
        using var http = new HttpClient(handler);
        var client = new LastFmClient(http, "api-key", "api-secret");

        var accepted = await client.SetTrackLovedAsync(
            new LastFmTrack(artist, title, null, null),
            loved: true,
            "session-key");

        Assert.False(accepted);
        Assert.Null(handler.LastRequestBody);
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        /// <summary>Gets the decoded form fields of the last request, or <see langword="null"/>.</summary>
        internal Dictionary<string, string>? LastRequestBody { get; private set; }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            LastRequestBody = body
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(
                    pair => Uri.UnescapeDataString(pair[0]),
                    pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty,
                    StringComparer.Ordinal);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
