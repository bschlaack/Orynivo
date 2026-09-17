using Microsoft.AspNetCore.Http;
using Orynivo.Library;
using Orynivo.Server;
using Orynivo.Server.Middleware;
using Xunit;

namespace Orynivo.Server.Tests;

/// <summary>
/// Verifies that the profile-context middleware scopes the active profile for a
/// request, defaults to <c>standard</c>, and rejects unknown profiles.
/// </summary>
public sealed class ProfileContextMiddlewareTests
{
    private static ServerSettings CreateSettings() => new()
    {
        Profiles =
        [
            new ServerProfile { Id = "standard", Name = "Standard" },
            new ServerProfile { Id = "alice", Name = "Alice" }
        ]
    };

    /// <summary>A valid profile header scopes the active profile and is restored afterwards.</summary>
    [Fact]
    public async Task InvokeAsync_ScopesActiveProfileAndRestoresIt()
    {
        string? observed = null;
        var middleware = new ProfileContextMiddleware(
            _ => { observed = AudioDatabase.ActiveProfileId; return Task.CompletedTask; },
            CreateSettings());

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/tracks";
        context.Request.Headers["X-Orynivo-Profile"] = "alice";

        await middleware.InvokeAsync(context);

        Assert.Equal("alice", observed);
        Assert.Equal("standard", AudioDatabase.ActiveProfileId);
    }

    /// <summary>Without a profile selector the standard profile is used.</summary>
    [Fact]
    public async Task InvokeAsync_DefaultsToStandardProfile()
    {
        string? observed = null;
        var middleware = new ProfileContextMiddleware(
            _ => { observed = AudioDatabase.ActiveProfileId; return Task.CompletedTask; },
            CreateSettings());

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/tracks";

        await middleware.InvokeAsync(context);

        Assert.Equal("standard", observed);
    }

    /// <summary>The profile may also be selected through the query parameter.</summary>
    [Fact]
    public async Task InvokeAsync_AcceptsQueryProfile()
    {
        string? observed = null;
        var middleware = new ProfileContextMiddleware(
            _ => { observed = AudioDatabase.ActiveProfileId; return Task.CompletedTask; },
            CreateSettings());

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/tracks";
        context.Request.QueryString = new QueryString("?profile=alice");

        await middleware.InvokeAsync(context);

        Assert.Equal("alice", observed);
    }

    /// <summary>An unknown profile is rejected with a 403 and never reaches the endpoint.</summary>
    [Fact]
    public async Task InvokeAsync_RejectsUnknownProfile()
    {
        var nextCalled = false;
        var middleware = new ProfileContextMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            CreateSettings());

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/tracks";
        context.Request.Headers["X-Orynivo-Profile"] = "unknown";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }
}
