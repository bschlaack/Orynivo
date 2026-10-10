using System.Net;
using System.Text;
using System.Text.Json;
using Orynivo.Streaming;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies that strict catalog reads distinguish HTTP and payload failures from empty libraries.</summary>
public sealed class OrynivoCatalogFailureTests
{
    /// <summary>Strict catalog endpoints preserve failures while legacy callers retain their empty fallback.</summary>
    /// <param name="endpoint">Catalog operation under test.</param>
    [Theory]
    [InlineData("artists")]
    [InlineData("albums")]
    [InlineData("tracks")]
    [InlineData("facets")]
    [InlineData("by-ids")]
    public async Task RejectedRequest_StrictThrowsAndLegacyReturnsEmpty(string endpoint)
    {
        using var http = new HttpClient(new ResponseHandler(HttpStatusCode.ServiceUnavailable, "[]"));
        using var client = new OrynivoServerClient(http);

        await Assert.ThrowsAsync<HttpRequestException>(() => Read(client, endpoint, true));
        Assert.Equal(0, await Read(client, endpoint, false));
    }

    /// <summary>A successful empty response is valid in strict mode.</summary>
    /// <param name="endpoint">Catalog operation under test.</param>
    [Theory]
    [InlineData("artists")]
    [InlineData("albums")]
    [InlineData("tracks")]
    [InlineData("facets")]
    [InlineData("by-ids")]
    public async Task EmptyCatalog_IsSuccessful(string endpoint)
    {
        using var http = new HttpClient(new ResponseHandler(HttpStatusCode.OK, "[]"));
        using var client = new OrynivoServerClient(http);
        Assert.Equal(0, await Read(client, endpoint, true));
    }

    /// <summary>Null, malformed, and wrong-shaped payloads must never masquerade as empty catalogs.</summary>
    /// <param name="payload">Invalid response payload.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("invalid")]
    public async Task InvalidPayload_StrictThrows(string payload)
    {
        using var http = new HttpClient(new ResponseHandler(HttpStatusCode.OK, payload));
        using var client = new OrynivoServerClient(http);
        foreach (var endpoint in new[] { "artists", "albums", "tracks", "facets", "by-ids" })
            await Assert.ThrowsAsync<JsonException>(() => Read(client, endpoint, true));
    }

    /// <summary>Strict requests propagate navigation cancellation to the owning loader.</summary>
    [Fact]
    public async Task CancelledRequest_StrictPropagatesCancellation()
    {
        using var http = new HttpClient(new ResponseHandler(HttpStatusCode.OK, "[]"));
        using var client = new OrynivoServerClient(http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var endpoint in new[] { "artists", "albums", "tracks", "facets", "by-ids" })
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(client, endpoint, true, cancellation.Token));
    }

    /// <summary>Invokes a catalog operation against the offline HTTP fixture.</summary>
    /// <param name="client">Fixture client.</param>
    /// <param name="endpoint">Catalog operation name.</param>
    /// <param name="strict">Whether request failures must propagate.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The number of returned rows.</returns>
    private static async Task<int> Read(OrynivoServerClient client, string endpoint, bool strict,
        CancellationToken cancellationToken = default)
    {
        var server = new OrynivoServerSettings { Id = "fixture", Name = "Fixture", BaseUrl = "http://localhost:5280" };
        return endpoint switch
        {
            "artists" => (await client.GetArtistsAsync(server, cancellationToken, strict)).Count,
            "albums" => (await client.GetAlbumsAsync(server, cancellationToken, strict)).Count,
            "tracks" => (await client.GetTracksAsync(server, cancellationToken: cancellationToken, requireComplete: strict)).Count,
            "facets" => (await client.GetTrackFacetsAsync(server, cancellationToken, strict)).Count,
            "by-ids" => (await client.GetTracksByIdsAsync(server, [1], cancellationToken, strict)).Count,
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
        };
    }

    /// <summary>Returns synthetic catalog responses without accessing a network or user library.</summary>
    /// <param name="status">HTTP response status.</param>
    /// <param name="payload">Synthetic JSON response body.</param>
    private sealed class ResponseHandler(HttpStatusCode status, string payload) : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
