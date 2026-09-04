using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orynivo.Mcp;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Orynivo.Remote;

/// <summary>Hosts the opt-in, token-protected mobile web remote for the desktop player.</summary>
public sealed class MobileRemoteServerService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions RemoteJsonOptions = new(JsonSerializerDefaults.Web);
    private WebApplication? _app;

    /// <summary>Gets whether the mobile remote is currently listening.</summary>
    public bool IsRunning => _app is not null;

    /// <summary>Starts the LAN-bound remote after stopping a previous instance.</summary>
    /// <param name="port">TCP port to listen on.</param>
    /// <param name="accessToken">Dedicated bearer token required by every remote API request.</param>
    /// <param name="bridge">Thread-safe player control bridge.</param>
    /// <param name="cancellationToken">Startup cancellation token.</param>
    /// <returns>A task that completes when the endpoint is ready.</returns>
    public async Task StartAsync(
        int port,
        string accessToken,
        McpPlayerBridge bridge,
        CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Mobile remote access requires a dedicated token.");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseSetting("urls", $"http://0.0.0.0:{port}");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var expectedToken = Encoding.UTF8.GetBytes(accessToken);

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; connect-src 'self'; img-src 'self' data:; " +
                "style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; frame-ancestors 'none'";
            if (!context.Request.Path.StartsWithSegments("/remote/api"))
            {
                await next(context).ConfigureAwait(false);
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

            await next(context).ConfigureAwait(false);
        });

        app.MapGet("/remote", () => Results.Content(MobileRemotePage.Html, "text/html; charset=utf-8"));
        app.MapGet("/", () => Results.Redirect("/remote"));
        app.MapGet("/remote/api/state", async (CancellationToken ct) =>
            Results.Json(await CreateSnapshotAsync(bridge, ct).ConfigureAwait(false)));
        app.MapGet("/remote/api/artwork", async (HttpContext context, CancellationToken ct) =>
        {
            if (bridge.GetCurrentArtworkFunc is null)
                return Results.NotFound();
            var artwork = await bridge.OnUiAsync(bridge.GetCurrentArtworkFunc, ct).ConfigureAwait(false);
            if (artwork is null)
                return Results.NotFound();
            var etag = $"\"{artwork.CacheKey}\"";
            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = "private, max-age=86400";
            if (string.Equals(context.Request.Headers.IfNoneMatch, etag, StringComparison.Ordinal))
                return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.File(artwork.Data, artwork.MimeType);
        });
        app.MapGet("/remote/api/search", async (string? q, int? limit, CancellationToken ct) =>
        {
            if (bridge.SearchMobileTracksFunc is null || string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Results.Json(Array.Empty<MobileRemoteTrack>());
            var results = await bridge.SearchMobileTracksFunc(q, Math.Clamp(limit ?? 25, 1, 50), ct)
                .ConfigureAwait(false);
            return Results.Json(results);
        });
        app.MapGet("/remote/api/library/artists", async (string? q, int? limit, CancellationToken ct) =>
        {
            if (bridge.BrowseMobileArtistsFunc is null)
                return Results.Json(Array.Empty<MobileRemoteArtist>());
            var results = await bridge.BrowseMobileArtistsFunc(q, Math.Clamp(limit ?? 100, 1, 250), ct)
                .ConfigureAwait(false);
            return Results.Json(results);
        });
        app.MapGet("/remote/api/library/albums", async (string? artistId, CancellationToken ct) =>
        {
            if (bridge.BrowseMobileAlbumsFunc is null || string.IsNullOrWhiteSpace(artistId))
                return Results.BadRequest(new { error = "invalid_artist" });
            return Results.Json(await bridge.BrowseMobileAlbumsFunc(artistId, ct).ConfigureAwait(false));
        });
        app.MapGet("/remote/api/library/tracks", async (string? albumId, CancellationToken ct) =>
        {
            if (bridge.BrowseMobileAlbumTracksFunc is null || string.IsNullOrWhiteSpace(albumId))
                return Results.BadRequest(new { error = "invalid_album" });
            return Results.Json(await bridge.BrowseMobileAlbumTracksFunc(albumId, ct).ConfigureAwait(false));
        });
        app.MapGet("/remote/api/events", async (HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.ContentType = "text/event-stream";
            string? previous = null;
            while (!ct.IsCancellationRequested)
            {
                var snapshot = await CreateSnapshotAsync(bridge, ct).ConfigureAwait(false);
                var json = JsonSerializer.Serialize(snapshot, RemoteJsonOptions);
                if (!string.Equals(previous, json, StringComparison.Ordinal))
                {
                    await context.Response.WriteAsync($"event: state\ndata: {json}\n\n", ct).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
                    previous = json;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(750), ct).ConfigureAwait(false);
            }
        });
        app.MapPost("/remote/api/command", async (MobileRemoteCommand request, CancellationToken ct) =>
        {
            var accepted = await ExecuteCommandAsync(bridge, request, ct).ConfigureAwait(false);
            return accepted ? Results.NoContent() : Results.BadRequest(new { error = "unsupported_command" });
        });
        app.MapPost("/remote/api/tracks/queue", async (MobileRemoteTrackAction request, CancellationToken ct) =>
        {
            if (bridge.QueueMobileTrackFunc is null || string.IsNullOrWhiteSpace(request.Id))
                return Results.BadRequest(new { error = "invalid_track" });
            var accepted = await bridge.OnUiAsync(
                () => bridge.QueueMobileTrackFunc(request.Id, request.Action), ct).ConfigureAwait(false);
            return accepted ? Results.NoContent() : Results.BadRequest(new { error = "invalid_track_or_action" });
        });
        app.MapPost("/remote/api/queue", async (MobileRemoteQueueAction request, CancellationToken ct) =>
        {
            if (bridge.EditMobileQueueFunc is null)
                return Results.BadRequest(new { error = "queue_edit_unavailable" });
            var accepted = await bridge.OnUiAsync(
                () => bridge.EditMobileQueueFunc(request.Action, request.Index), ct).ConfigureAwait(false);
            return accepted ? Results.NoContent() : Results.BadRequest(new { error = "invalid_queue_action" });
        });
        app.MapGet("/remote/api/outputs", async (CancellationToken ct) =>
        {
            var values = await bridge.OnUiAsync(
                () => bridge.GetOutputProfilesFunc?.Invoke() ?? [], ct).ConfigureAwait(false);
            return Results.Json(values.Select(value =>
            {
                const string selectedSuffix = " (selected)";
                var selected = value.EndsWith(selectedSuffix, StringComparison.Ordinal);
                return new MobileRemoteOutput(
                    selected ? value[..^selectedSuffix.Length] : value,
                    selected);
            }));
        });
        app.MapPost("/remote/api/outputs/select", async (MobileRemoteOutputSelection request, CancellationToken ct) =>
        {
            if (bridge.SelectOutputProfileFunc is null || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "invalid_output" });
            var selected = await bridge.OnUiAsync(
                () => bridge.SelectOutputProfileFunc(request.Name), ct).ConfigureAwait(false);
            return selected ? Results.NoContent() : Results.NotFound(new { error = "output_not_found" });
        });

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        _app = app;
    }

    /// <summary>Stops and disposes the current remote host.</summary>
    /// <param name="cancellationToken">Shutdown cancellation token.</param>
    /// <returns>A task that completes after resources are released.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app is null)
            return;
        var app = _app;
        _app = null;
        try
        {
            await app.StopAsync(cancellationToken).ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // A remote shutdown failure must not prevent Orynivo from closing.
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private static async Task<MobileRemoteSnapshot> CreateSnapshotAsync(McpPlayerBridge bridge, CancellationToken ct)
    {
        var state = await bridge.OnUiAsync(
            () => bridge.GetStateFunc?.Invoke() ?? new PlayerState("stopped", null, null, null, null, 0, 0, 0, -1, 0),
            ct).ConfigureAwait(false);
        var queue = await bridge.OnUiAsync(
            () => (bridge.GetQueueFunc?.Invoke() ?? []).Select(entry =>
                new MobileRemoteQueueEntry(entry.Index, entry.IsCurrent, entry.FileName)).ToList(),
            ct).ConfigureAwait(false);
        return new MobileRemoteSnapshot(
            state.Status,
            state.Title,
            state.Artist,
            state.Album,
            state.PositionSeconds,
            state.DurationSeconds,
            state.Volume,
            state.QueueIndex,
            await bridge.OnUiAsync(() => bridge.GetCurrentFavoriteFunc?.Invoke(), ct).ConfigureAwait(false),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{state.QueueIndex}\n{state.Title}\n{state.Artist}\n{state.Album}"))),
            queue);
    }

    private static async Task<bool> ExecuteCommandAsync(
        McpPlayerBridge bridge,
        MobileRemoteCommand request,
        CancellationToken ct)
    {
        switch (request.Command.Trim().ToLowerInvariant())
        {
            case "pause-resume" when bridge.TogglePauseFunc is not null:
                await bridge.OnUiAsync(bridge.TogglePauseFunc, ct).ConfigureAwait(false);
                return true;
            case "next" when bridge.SkipNextFunc is not null:
                await bridge.OnUiAsync(bridge.SkipNextFunc, ct).ConfigureAwait(false);
                return true;
            case "previous" when bridge.SkipPreviousFunc is not null:
                await bridge.OnUiAsync(bridge.SkipPreviousFunc, ct).ConfigureAwait(false);
                return true;
            case "stop" when bridge.StopFunc is not null:
                await bridge.OnUiAsync(bridge.StopFunc, ct).ConfigureAwait(false);
                return true;
            case "seek" when bridge.SeekFunc is not null && request.Value is { } seconds:
                await bridge.OnUiAsync(() => bridge.SeekFunc(seconds), ct).ConfigureAwait(false);
                return true;
            case "volume" when bridge.SetVolumeFunc is not null && request.Value is { } volume:
                await bridge.OnUiAsync(() => bridge.SetVolumeFunc(Math.Clamp(volume, 0, 1)), ct).ConfigureAwait(false);
                return true;
            case "queue-index" when bridge.PlayQueueIndexFunc is not null && request.Value is { } index:
                return await bridge.OnUiAsync(
                    () => bridge.PlayQueueIndexFunc((int)Math.Round(index)),
                    ct).ConfigureAwait(false);
            case "favorite" when bridge.SetCurrentFavoriteFunc is not null && request.Value is { } favorite:
                return await bridge.OnUiAsync(
                    () => bridge.SetCurrentFavoriteFunc(favorite >= 0.5d), ct).ConfigureAwait(false);
            default:
                return false;
        }
    }
}

/// <summary>Safe, path-free player state transferred to a mobile remote.</summary>
/// <param name="Status">Playback status.</param>
/// <param name="Title">Current title.</param>
/// <param name="Artist">Current artist.</param>
/// <param name="Album">Current album.</param>
/// <param name="PositionSeconds">Playback position in seconds.</param>
/// <param name="DurationSeconds">Track duration in seconds.</param>
/// <param name="Volume">Normalized volume.</param>
/// <param name="QueueIndex">Current queue index.</param>
/// <param name="IsFavorite">Current track favorite state, or no value for unsupported media.</param>
/// <param name="ArtworkKey">Credential-free identity used to refresh current artwork.</param>
/// <param name="Queue">Path-free queue entries.</param>
public sealed record MobileRemoteSnapshot(
    string Status,
    string? Title,
    string? Artist,
    string? Album,
    double PositionSeconds,
    double DurationSeconds,
    double Volume,
    int QueueIndex,
    bool? IsFavorite,
    string ArtworkKey,
    IReadOnlyList<MobileRemoteQueueEntry> Queue);

/// <summary>One path-free queue entry returned to the web remote.</summary>
/// <param name="Index">Queue index.</param>
/// <param name="IsCurrent">Whether the item is current.</param>
/// <param name="Title">Display title.</param>
public sealed record MobileRemoteQueueEntry(int Index, bool IsCurrent, string Title);

/// <summary>A bounded command submitted by the mobile web remote.</summary>
/// <param name="Command">Command identifier.</param>
/// <param name="Value">Optional numeric seek or volume value.</param>
public sealed record MobileRemoteCommand(string Command, double? Value);

/// <summary>A credential-free track summary returned by mobile library search.</summary>
/// <param name="Id">Opaque provider-local identity accepted by mobile queue actions.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist name.</param>
/// <param name="Album">Album title.</param>
/// <param name="Year">Release year.</param>
/// <param name="Source">Safe provider display name.</param>
public sealed record MobileRemoteTrack(
    string Id,
    string Title,
    string? Artist,
    string? Album,
    int? Year,
    string Source);

/// <summary>A provider-bound artist summary for mobile library browsing.</summary>
/// <param name="Id">Opaque identity accepted by the album browser.</param>
/// <param name="Name">Artist display name.</param>
/// <param name="Source">Safe provider display name.</param>
public sealed record MobileRemoteArtist(string Id, string Name, string Source);

/// <summary>A provider-bound album summary for mobile library browsing.</summary>
/// <param name="Id">Opaque identity accepted by the track browser.</param>
/// <param name="Title">Album display title.</param>
/// <param name="Artist">Album artist.</param>
/// <param name="Year">Release year.</param>
/// <param name="Source">Safe provider display name.</param>
public sealed record MobileRemoteAlbum(string Id, string Title, string? Artist, int? Year, string Source);

/// <summary>A playback or queue action targeting an opaque mobile search result.</summary>
/// <param name="Id">Opaque search-result identity.</param>
/// <param name="Action">One of <c>play</c>, <c>next</c>, or <c>append</c>.</param>
public sealed record MobileRemoteTrackAction(string Id, string Action);

/// <summary>A validated edit request for the playback queue.</summary>
/// <param name="Action">One of <c>remove</c>, <c>up</c>, <c>down</c>, or <c>clear</c>.</param>
/// <param name="Index">Optional zero-based target index.</param>
public sealed record MobileRemoteQueueAction(string Action, int? Index);

/// <summary>A selectable output profile exposed to the mobile remote.</summary>
/// <param name="Name">Profile display name.</param>
/// <param name="Selected">Whether this profile is currently selected.</param>
public sealed record MobileRemoteOutput(string Name, bool Selected);

/// <summary>An output-profile selection request.</summary>
/// <param name="Name">Exact configured profile name.</param>
public sealed record MobileRemoteOutputSelection(string Name);
