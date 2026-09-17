using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Orynivo.Server;
using Orynivo.Server.Endpoints;
using Orynivo.Server.Middleware;
using Xunit;

namespace Orynivo.Server.Tests;

/// <summary>
/// Exercises the real health and server-info endpoints through the API key and
/// profile-context middleware pipeline using an in-memory test host.
/// </summary>
public sealed class CoreEndpointsTests : IAsyncLifetime
{
    private const string ApiKey = "test-api-key";

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    /// <summary>Builds the in-memory host with the production middleware chain.</summary>
    /// <returns>A task representing host startup.</returns>
    public async Task InitializeAsync()
    {
        var settings = new ServerSettings
        {
            ApiKey = ApiKey,
            ServerName = "Test Server",
            LibraryPaths = ["/music"]
        };

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(settings);

        _app = builder.Build();
        _app.UseMiddleware<ApiKeyMiddleware>();
        _app.UseMiddleware<ProfileContextMiddleware>();
        _app.MapCoreEndpoints(settings, "9.9.9", "portable", updateSupported: false);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    /// <summary>Stops and disposes the in-memory host.</summary>
    /// <returns>A task representing host shutdown.</returns>
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>The health probe is reachable without a key.</summary>
    [Fact]
    public async Task Health_ReturnsOkWithoutKey()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("ok", json.GetProperty("status").GetString());
        Assert.Equal("Test Server", json.GetProperty("server").GetString());
        Assert.Equal("9.9.9", json.GetProperty("version").GetString());
    }

    /// <summary>The health probe ignores an invalid key.</summary>
    [Fact]
    public async Task Health_IgnoresInvalidKey()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("X-Api-Key", "wrong");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Server info requires the API key.</summary>
    [Fact]
    public async Task Info_RequiresApiKey()
    {
        var response = await _client.GetAsync("/api/info");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Server info is returned for a valid header key.</summary>
    [Fact]
    public async Task Info_AcceptsHeaderKey()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/info");
        request.Headers.Add("X-Api-Key", ApiKey);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("Test Server", json.GetProperty("name").GetString());
        Assert.Equal("9.9.9", json.GetProperty("version").GetString());
        Assert.Equal(1, json.GetProperty("apiVersion").GetInt32());
        Assert.Equal("portable", json.GetProperty("installType").GetString());
        Assert.False(json.GetProperty("updateSupported").GetBoolean());
        Assert.Contains("/music", json.GetProperty("paths").EnumerateArray().Select(p => p.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("architecture").GetString()));
    }

    /// <summary>Server info accepts the key through the query parameter.</summary>
    [Fact]
    public async Task Info_AcceptsQueryKey()
    {
        var response = await _client.GetAsync($"/api/info?key={ApiKey}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>An unknown profile is rejected after successful authentication.</summary>
    [Fact]
    public async Task Info_RejectsUnknownProfile()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/info");
        request.Headers.Add("X-Api-Key", ApiKey);
        request.Headers.Add("X-Orynivo-Profile", "unknown");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>The standard profile is accepted.</summary>
    [Fact]
    public async Task Info_AcceptsStandardProfile()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/info");
        request.Headers.Add("X-Api-Key", ApiKey);
        request.Headers.Add("X-Orynivo-Profile", "standard");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
