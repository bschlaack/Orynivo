using System.Net;
using System.Text;
using Orynivo.Streaming;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the Orynivo Server client requests without contacting a server.
/// </summary>
public sealed class OrynivoServerClientTests
{
    /// <summary>The genre update is a PUT to the track genre endpoint.</summary>
    [Fact]
    public async Task UpdateTrackGenreAsync_SendsPutWithGenre()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var client = new OrynivoServerClient(http);

        var accepted = await client.UpdateTrackGenreAsync(CreateServer(), 7, "Jazz");

        Assert.True(accepted);
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal("/api/tracks/7/genre", handler.Path);
        Assert.Equal("Jazz", handler.Genre);
        Assert.Equal("secret", handler.ApiKey);
    }

    /// <summary>Clearing the genre sends a null value so the server drops the override.</summary>
    [Fact]
    public async Task UpdateTrackGenreAsync_NullClearsTheGenre()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var client = new OrynivoServerClient(http);

        var accepted = await client.UpdateTrackGenreAsync(CreateServer(), 7, null);

        Assert.True(accepted);
        Assert.Equal(string.Empty, handler.Genre);
    }

    /// <summary>A rejected request reports failure.</summary>
    [Fact]
    public async Task UpdateTrackGenreAsync_ErrorResponseReturnsFalse()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound);
        using var http = new HttpClient(handler);
        var client = new OrynivoServerClient(http);

        Assert.False(await client.UpdateTrackGenreAsync(CreateServer(), 7, "Jazz"));
    }

    private static OrynivoServerSettings CreateServer() => new()
    {
        Id = "server-1",
        Name = "Server",
        BaseUrl = "http://localhost:5280",
        ApiKey = "secret"
    };

    private sealed class CapturingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        /// <summary>Gets the HTTP method of the last request.</summary>
        internal HttpMethod? Method { get; private set; }

        /// <summary>Gets the absolute path of the last request.</summary>
        internal string? Path { get; private set; }

        /// <summary>Gets the raw body of the last request.</summary>
        internal string Body { get; private set; } = string.Empty;

        /// <summary>Gets the API key header of the last request.</summary>
        internal string? ApiKey { get; private set; }

        /// <summary>Gets the genre value parsed from the last request body.</summary>
        internal string? Genre =>
            System.Text.Json.JsonDocument.Parse(Body).RootElement.GetProperty("genre").GetString();

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            ApiKey = request.Headers.TryGetValues("X-Api-Key", out var values)
                ? values.FirstOrDefault()
                : null;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }
}
