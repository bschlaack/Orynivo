using System.Net;
using System.Text;
using System.Text.Json;
using Orynivo.Streaming;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Checks strict full-search transport failures without accessing a live service.</summary>
public sealed class OrynivoSearchFailureTests
{
    /// <summary>Unavailable servers propagate strict failures while legacy callers retain empty results.</summary>
    [Fact]
    public async Task RejectedRequest_StrictThrowsAndLegacyIsEmpty()
    {
        using var http = new HttpClient(new ResponseHandler(HttpStatusCode.ServiceUnavailable, "failure"));
        using var client = new OrynivoServerClient(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchFullAsync(Server, "query", requireComplete: true));
        Assert.Empty((await client.SearchFullAsync(Server, "query")).Tracks);
    }

    /// <summary>Missing categories and invalid JSON cannot masquerade as a successful no-match search.</summary>
    /// <param name="payload">Synthetic invalid response.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("invalid")]
    [InlineData("{\"tracks\":[],\"albums\":null,\"artists\":[]}")]
    public async Task InvalidPayload_StrictThrows(string payload)
    {
        using var http = new HttpClient(new ResponseHandler(HttpStatusCode.OK, payload));
        using var client = new OrynivoServerClient(http);
        await Assert.ThrowsAsync<JsonException>(() => client.SearchFullAsync(Server, "query", requireComplete: true));
    }

    /// <summary>Empty categories are successful; navigation cancellation must still propagate.</summary>
    [Fact]
    public async Task EmptySuccessAndCancellation_AreDistinct()
    {
        using var http = new HttpClient(new ResponseHandler(HttpStatusCode.OK, "{\"tracks\":[],\"albums\":[],\"artists\":[]}"));
        using var client = new OrynivoServerClient(http);
        var result = await client.SearchFullAsync(Server, "query", requireComplete: true);
        Assert.Empty(result.Tracks);
        Assert.Empty(result.Albums);
        Assert.Empty(result.Artists);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchFullAsync(Server, "query",
            cancellationToken: cancellation.Token, requireComplete: true));
    }

    /// <summary>Gets synthetic connection settings for intercepted fixture requests.</summary>
    private static OrynivoServerSettings Server => new() { Id = "fixture", BaseUrl = "http://fixture.invalid" };

    /// <summary>Returns synthetic HTTP responses without a network connection.</summary>
    /// <param name="status">Synthetic response status.</param>
    /// <param name="payload">Synthetic JSON payload.</param>
    private sealed class ResponseHandler(HttpStatusCode status, string payload) : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status)
                { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
        }
    }
}
