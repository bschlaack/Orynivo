using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Orynivo.Mcp;
using System.Security.Cryptography;
using System.Text;

namespace Orynivo.Remote;

public sealed partial class MobileRemoteServerService
{
    private McpPlayerBridge? _browserBridge;
    private BrowserAudioCookie? _browserCookie;
    private readonly object _browserCookieSync = new();
    private long _browserSelectionVersion;
    private const string BrowserCookieName = "OrynivoBrowserAudio";

    /// <summary>Retains one session-only media cookie outside API payloads and URLs.</summary>
    private sealed record BrowserAudioCookie(string ClientId, byte[] Token);

    private bool AuthorizeBrowserStream(HttpContext context)
    {
        var cookie = Volatile.Read(ref _browserCookie);
        var supplied = Encoding.UTF8.GetBytes(context.Request.Cookies[BrowserCookieName] ?? "");
        return context.Request.Method == "GET" && cookie is not null && cookie.Token.Length == supplied.Length &&
            CryptographicOperations.FixedTimeEquals(cookie.Token, supplied);
    }

    private void MapBrowserAudioEndpoints(WebApplication app, McpPlayerBridge bridge)
    {
        _browserBridge = bridge;
        app.MapPost("/remote/api/browser-audio/select", async (BrowserAudioSelection request, HttpContext context, CancellationToken ct) =>
        {
            if (!Guid.TryParseExact(request.ClientId, "N", out _) || bridge.SelectBrowserOutputFunc is not { } select) return Results.BadRequest();
            if (request.FormatId is not null && Audio.BrowserAudioEncoding.Find(request.FormatId) is null) return Results.BadRequest();
            long version;
            lock (_browserCookieSync)
            {
                version = Interlocked.Increment(ref _browserSelectionVersion);
                Volatile.Write(ref _browserCookie, null);
            }
            if (!await bridge.OnUiAsync(() =>
                {
                    if (request.FormatId is not null) bridge.SetBrowserMusicFormatFunc?.Invoke(request.FormatId);
                    return select(request.ClientId);
                }, ct).ConfigureAwait(false)) return Results.BadRequest();
            if (version != Volatile.Read(ref _browserSelectionVersion)) return Results.Conflict();
            if (await bridge.OnUiAsync(() => bridge.GetBrowserAudioStateFunc?.Invoke()?.ClientId, ct).ConfigureAwait(false) != request.ClientId)
                return Results.Conflict();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            lock (_browserCookieSync)
            {
                if (version != Volatile.Read(ref _browserSelectionVersion)) return Results.Conflict();
                Volatile.Write(ref _browserCookie, new BrowserAudioCookie(request.ClientId, Encoding.UTF8.GetBytes(token)));
            }
            context.Response.Cookies.Append(BrowserCookieName, token, new CookieOptions
            { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, Path = "/remote/api/browser-audio" });
            context.Response.Headers.CacheControl = "no-store";
            return Results.NoContent();
        });
        app.MapPost("/remote/api/browser-audio/release", async (BrowserAudioSelection request, HttpContext context, CancellationToken ct) =>
        {
            if (bridge.ReleaseBrowserOutputFunc is not { } release || !Guid.TryParseExact(request.ClientId, "N", out _)) return Results.BadRequest();
            lock (_browserCookieSync)
            {
                if (_browserCookie?.ClientId == request.ClientId)
                {
                    Interlocked.Increment(ref _browserSelectionVersion);
                    Volatile.Write(ref _browserCookie, null);
                }
            }
            // Do not send a delayed deletion header that could erase a newer tab's
            // cookie. Revocation is server-side; the next claim replaces the cookie.
            await bridge.OnUiAsync(() => release(request.ClientId), ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        app.MapPost("/remote/api/browser-audio/report", async (BrowserAudioReport request, CancellationToken ct) =>
        {
            if (!double.IsFinite(request.PositionSeconds) || request.PositionSeconds < 0 || request.StreamId?.Length > 64 ||
                bridge.ReportBrowserAudioFunc is not { } report) return Results.BadRequest();
            return await bridge.OnUiAsync(() => report(request), ct).ConfigureAwait(false) ? Results.NoContent() : Results.Conflict();
        });
        app.MapGet("/remote/api/browser-audio/stream/{id}", async (string id, HttpContext context, CancellationToken ct) =>
        {
            var cookie = Volatile.Read(ref _browserCookie);
            var responseWriter = await bridge.OnUiAsync(() => cookie is not null &&
                bridge.GetBrowserAudioStateFunc?.Invoke()?.ClientId == cookie.ClientId
                    ? bridge.OpenBrowserAudioResponseFunc?.Invoke(id) : null, ct).ConfigureAwait(false);
            if (responseWriter is not null)
            {
                try { await responseWriter(context, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (IOException) when (ct.IsCancellationRequested) { }
                return;
            }
            var write = await bridge.OnUiAsync(() => cookie is not null &&
                bridge.GetBrowserAudioStateFunc?.Invoke()?.ClientId == cookie.ClientId
                    ? bridge.OpenBrowserAudioStreamFunc?.Invoke(id) : null, ct).ConfigureAwait(false);
            if (write is null) { context.Response.StatusCode = 404; return; }
            context.Response.ContentType = "audio/mpeg";
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            try { await write(context.Response.Body, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (IOException) when (ct.IsCancellationRequested) { }
        });
    }

    private async Task StopBrowserOutputAsync()
    {
        lock (_browserCookieSync)
        {
            Interlocked.Increment(ref _browserSelectionVersion);
            Volatile.Write(ref _browserCookie, null);
        }
        var bridge = _browserBridge;
        _browserBridge = null;
        if (bridge?.ReleaseBrowserOutputFunc is not { } release) return;
        var client = await bridge.OnUiAsync(() => bridge.GetBrowserAudioStateFunc?.Invoke()?.ClientId).ConfigureAwait(false);
        if (client is not null) await bridge.OnUiAsync(() => release(client)).ConfigureAwait(false);
    }
}
