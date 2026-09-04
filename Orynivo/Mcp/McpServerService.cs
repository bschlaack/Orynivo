using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Orynivo.Mcp;

/// <summary>
/// Hosts an HTTP/SSE MCP server using ASP.NET Core.
/// The server is started via <see cref="StartAsync"/> and stopped via <see cref="StopAsync"/>.
/// It exposes all <see cref="McpTools"/> to MCP-compatible language-model clients.
/// </summary>
public sealed class McpServerService : IAsyncDisposable
{
    private WebApplication? _app;

    /// <summary>Gets the port this server is currently listening on, or -1 when stopped.</summary>
    public int Port { get; private set; } = -1;

    /// <summary>Gets a value indicating whether the server is currently running.</summary>
    public bool IsRunning => _app is not null;

    /// <summary>
    /// Starts the MCP server on loopback or, when explicitly enabled, all network interfaces.
    /// A previous running instance is stopped before the new one starts.
    /// </summary>
    /// <param name="port">TCP port to listen on.</param>
    /// <param name="bridge">Bridge providing player state and control callbacks to MCP tools.</param>
    /// <param name="allowNetworkAccess">Whether to bind to all network interfaces.</param>
    /// <param name="accessToken">Bearer token required for network access.</param>
    /// <param name="ct">Cancellation token forwarded to <see cref="WebApplication.StartAsync"/>.</param>
    /// <returns>A task that completes once the server has started and is ready to accept connections.</returns>
    public async Task StartAsync(
        int port,
        McpPlayerBridge bridge,
        bool allowNetworkAccess = false,
        string? accessToken = null,
        CancellationToken ct = default)
    {
        await StopAsync(ct);

        if (allowNetworkAccess && string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("MCP network access requires an access token.");

        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseSetting("urls", $"http://{(allowNetworkAccess ? "0.0.0.0" : "localhost")}:{port}");

        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(bridge);
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
                {
                    Name    = "Orynivo",
                    Version = "1.0"
                };
            })
            .WithHttpTransport()
            .WithTools<McpTools>();

        var app = builder.Build();
        if (allowNetworkAccess)
        {
            var expectedToken = Encoding.UTF8.GetBytes(accessToken!);
            app.Use(async (context, next) =>
            {
                if (!context.Request.Path.StartsWithSegments("/mcp"))
                {
                    await next(context);
                    return;
                }

                var authorization = context.Request.Headers.Authorization.ToString();
                const string prefix = "Bearer ";
                var supplied = authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? authorization[prefix.Length..].Trim()
                    : string.Empty;
                var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
                if (suppliedBytes.Length != expectedToken.Length ||
                    !CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedToken))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    return;
                }

                await next(context);
            });
        }
        app.MapMcp("/mcp");

        await app.StartAsync(ct);
        _app = app;
        Port = port;
    }

    /// <summary>
    /// Stops the running MCP server and releases all resources.
    /// Has no effect if the server is not running.
    /// </summary>
    /// <param name="ct">Cancellation token forwarded to <see cref="WebApplication.StopAsync"/>.</param>
    /// <returns>A task that completes once the server has stopped.</returns>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_app is null)
            return;
        var app = _app;
        _app = null;
        Port = -1;
        try
        {
            await app.StopAsync(ct);
            await app.DisposeAsync();
        }
        catch
        {
            // stop/dispose failures must not propagate to the settings apply flow
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await StopAsync();
}
