using Microsoft.AspNetCore.Http;
using Orynivo.Server;
using Orynivo.Server.Middleware;
using Xunit;

namespace Orynivo.Server.Tests;

/// <summary>
/// Verifies that the API key middleware protects every endpoint except
/// <c>/api/health</c> and accepts the key only through the documented header or
/// query parameter.
/// </summary>
public sealed class ApiKeyMiddlewareTests
{
    private static async Task<(HttpContext Context, bool NextCalled, string Body)> InvokeAsync(
        string configuredKey,
        string path,
        string? headerKey = null,
        string? queryKey = null)
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            new ServerSettings { ApiKey = configuredKey });

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var body = new MemoryStream();
        context.Response.Body = body;
        if (headerKey is not null)
            context.Request.Headers["X-Api-Key"] = headerKey;
        if (queryKey is not null)
            context.Request.QueryString = new QueryString($"?key={queryKey}");

        await middleware.InvokeAsync(context);

        body.Position = 0;
        var text = await new StreamReader(body).ReadToEndAsync();
        return (context, nextCalled, text);
    }

    /// <summary>The health endpoint is reachable without a key.</summary>
    [Fact]
    public async Task InvokeAsync_AllowsHealthWithoutKey()
    {
        var (context, nextCalled, _) = await InvokeAsync("secret", "/api/health");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    /// <summary>A protected endpoint without a key is rejected with a JSON 401.</summary>
    [Fact]
    public async Task InvokeAsync_RejectsMissingKey()
    {
        var (context, nextCalled, body) = await InvokeAsync("secret", "/api/info");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains("Unauthorized", body);
    }

    /// <summary>A wrong header key is rejected.</summary>
    [Fact]
    public async Task InvokeAsync_RejectsWrongHeaderKey()
    {
        var (context, nextCalled, _) = await InvokeAsync("secret", "/api/info", headerKey: "wrong");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    /// <summary>The key is accepted through the <c>X-Api-Key</c> header.</summary>
    [Fact]
    public async Task InvokeAsync_AcceptsHeaderKey()
    {
        var (_, nextCalled, _) = await InvokeAsync("secret", "/api/info", headerKey: "secret");

        Assert.True(nextCalled);
    }

    /// <summary>The key is accepted through the <c>key</c> query parameter.</summary>
    [Fact]
    public async Task InvokeAsync_AcceptsQueryKey()
    {
        var (_, nextCalled, _) = await InvokeAsync("secret", "/api/stream/1", queryKey: "secret");

        Assert.True(nextCalled);
    }

    /// <summary>An unconfigured server key rejects every request.</summary>
    [Fact]
    public async Task InvokeAsync_RejectsWhenServerKeyMissing()
    {
        var (context, nextCalled, _) = await InvokeAsync(string.Empty, "/api/info", headerKey: "anything");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    /// <summary>The key comparison is case-sensitive.</summary>
    [Fact]
    public async Task InvokeAsync_IsCaseSensitive()
    {
        var (context, nextCalled, _) = await InvokeAsync("Secret", "/api/info", headerKey: "secret");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }
}
