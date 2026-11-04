using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Headless;
using Orynivo.Mcp;
using Orynivo.Remote;
using QRCoder;
using Orynivo;

// Exercise the production host and dispatcher with synthetic metadata, never the real library.
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(45));
var fixtureDirectory = Path.Combine(Path.GetTempPath(), "orynivo-remote-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtureDirectory);
File.WriteAllText(Path.Combine(fixtureDirectory, ".legacy-data-migrated"), "isolated fixture");
Environment.SetEnvironmentVariable("ORYNIVO_DATA_DIR", fixtureDirectory);
AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var dispatcher = Dispatcher.UIThread;
var run = Task.Run(async () =>
{
    await using var host = new MobileRemoteServerService();
    using var reserve = new TcpListener(IPAddress.Loopback, 0);
    reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
    var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    var lastAction = "";
    var commandEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var commandReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var bridge = new McpPlayerBridge
    {
        TogglePauseFunc = async () => { commandEntered.SetResult(); await commandReady.Task; },
        GetStateFunc = () => new PlayerState("playing", "Fixture track", "Fixture artist", "Fixture album",
            "private-path-must-not-escape", 1, 120, .25, 0, 1),
        GetQueueFunc = () => [new QueueEntry(0, true, "secret-path", "Fixture track")],
        BrowseMobilePlaylistsFunc = _ => Task.FromResult<IReadOnlyList<MobileRemotePlaylist>>([new(1, "Fixture playlist", 1, false)]),
        BrowseMobilePlaylistTracksFunc = (_, _) => Task.FromResult<IReadOnlyList<MobileRemoteTrack>>([new("local:1", "Fixture track", null, null, 1998, "Local")]),
        QueueMobilePlaylistFunc = (id, action) => { lastAction = action; return Task.FromResult(id == 1); },
        BrowseMobileRadioFunc = async (q, ct) =>
        {
            Check(dispatcher.CheckAccess(), "radio browsing dispatcher");
            await Task.Yield(); ct.ThrowIfCancellationRequested();
            if (q == "failure") throw new InvalidOperationException("private-feed-must-not-escape");
            return [new("station-id", string.IsNullOrEmpty(q) ? "Saved station" : "Search station", "MP3")];
        },
        BrowseMobilePodcastsFunc = (_, _) => Task.FromResult<IReadOnlyList<MobileRemoteMedia>>([new("podcast-id", "Pinned podcast", "Publisher")]),
        BrowseMobileEpisodesFunc = (id, _) => Task.FromResult<IReadOnlyList<MobileRemoteMedia>?>(id == "podcast-id"
            ? [new("episode-id", "Episode", "Pinned podcast", 120, DateTimeOffset.UnixEpoch)] : null)
    };
    try
    {
        await host.StartAsync(port, token, bridge, stop.Token);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
        using var html = await client.GetAsync("/remote");
        Check(html.StatusCode == HttpStatusCode.OK, "public shell");
        var document = await html.Content.ReadAsStringAsync();
        Check(document.Contains("playlistActions") && !document.Contains("/*REMOTE_SCRIPT*/") && !document.Contains("/*REMOTE_WORDS*/"), "embedded assets");
        Check(html.Headers.CacheControl?.NoStore == true, "HTML must not be cached");
        Check(html.Headers.GetValues("Content-Security-Policy").Single().Contains("blob:"), "authenticated artwork blobs");
        foreach (var route in new[] { "/remote/api/state", "/remote/api/playlists", "/remote/api/playlists/1/tracks", "/remote/api/events",
            "/remote/api/media/radio", "/remote/api/media/podcasts", "/remote/api/media/podcasts/podcast-id/episodes" })
        {
            using var denied = await client.GetAsync(route);
            Check(denied.StatusCode == HttpStatusCode.Unauthorized, "unauthenticated request");
        }
        foreach (var kind in new[] { "radio", "podcasts" })
        {
            using var deniedPlay = await client.PostAsJsonAsync($"/remote/api/media/{kind}/play", new { id = "known" });
            Check(deniedPlay.StatusCode == HttpStatusCode.Unauthorized, "media playback authentication");
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using (var savedRadio = await client.GetAsync("/remote/api/media/radio"))
            Check((await savedRadio.Content.ReadAsStringAsync()).Contains("Saved station") && savedRadio.Headers.CacheControl?.NoStore == true,
                "saved radio and private response caching");
        Check((await client.GetStringAsync("/remote/api/media/radio?q=test")).Contains("Search station"), "radio search");
        Check((await client.GetStringAsync("/remote/api/media/podcasts")).Contains("Pinned podcast"), "pinned podcasts");
        var episodes = await client.GetStringAsync("/remote/api/media/podcasts/podcast-id/episodes");
        Check(episodes.Contains("episode-id") && !episodes.Contains("audioUrl") && !episodes.Contains("feedUrl"), "safe episode DTO");
        using (var missingEpisode = await client.GetAsync("/remote/api/media/podcasts/missing/episodes"))
            Check(missingEpisode.StatusCode == HttpStatusCode.NotFound, "expired podcast identity");
        using (var failedMedia = await client.GetAsync("/remote/api/media/radio?q=failure"))
            Check(failedMedia.StatusCode == HttpStatusCode.BadGateway &&
                !(await failedMedia.Content.ReadAsStringAsync()).Contains("private-feed"), "sanitized media failure");
        using (var longQuery = await client.GetAsync("/remote/api/media/radio?q=" + new string('x', 251)))
            Check(longQuery.StatusCode == HttpStatusCode.BadRequest, "bounded media search");
        foreach (var kind in new[] { "radio", "podcasts" })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await bridge.OnUiAsync(() => bridge.PlayMobileMediaFunc = async (source, id) =>
            {
                Check(dispatcher.CheckAccess() && source == kind, "media playback dispatcher and kind");
                if (id != "known") return false;
                entered.SetResult(); await ready.Task; return true;
            });
            var play = client.PostAsJsonAsync($"/remote/api/media/{kind}/play", new { id = "known" });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(!play.IsCompleted, "media play awaits preparation");
            ready.SetResult();
            using var result = await play;
            Check(result.StatusCode == HttpStatusCode.NoContent, "media playback accepted");
            using var unknown = await client.PostAsJsonAsync($"/remote/api/media/{kind}/play", new { id = "missing" });
            Check(unknown.StatusCode == HttpStatusCode.BadRequest, "unknown media rejected");
        }
        using (var invalidKind = await client.PostAsJsonAsync("/remote/api/media/unsupported/play", new { id = "known" }))
            Check(invalidKind.StatusCode == HttpStatusCode.BadRequest, "invalid media kind");
        var command = client.PostAsJsonAsync("/remote/api/command", new { command = "pause-resume" });
        await commandEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(!command.IsCompleted, "command waits for asynchronous preparation");
        commandReady.SetResult();
        using var commandResult = await command;
        Check(commandResult.StatusCode == HttpStatusCode.NoContent, "prepared command acknowledgement");
        await bridge.OnUiAsync(() =>
        {
            bridge.TogglePauseFunc = async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("Synthetic command failure");
            };
        });
        using var failedCommand = await client.PostAsJsonAsync("/remote/api/command", new { command = "pause-resume" });
        Check(failedCommand.StatusCode == HttpStatusCode.InternalServerError, "async command failure is not acknowledged as success");
        var playbackEntered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var playbackReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var legacyLookups = 0;
        await bridge.OnUiAsync(() =>
        {
            bridge.ResolveRemoteTrackFunc = _ =>
            {
                legacyLookups++;
                throw new InvalidOperationException("Play must resolve inside its playback owner");
            };
            bridge.PlayFileFunc = async path => { playbackEntered.SetResult(path); await playbackReady.Task; };
        });
        const string opaqueReference = "orynivo://fixture/track/70001";
        var toolPlay = new McpTools(bridge).PlayAsync(opaqueReference);
        Check(await playbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)) == opaqueReference,
            "MCP playback receives opaque identity before resolution");
        Check(!toolPlay.IsCompleted && legacyLookups == 0, "MCP lookup bypassed owning playback preparation");
        playbackReady.SetResult();
        Check((await toolPlay).Contains(opaqueReference), "MCP confirmation retains opaque identity");
        var replacementEntered = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await bridge.OnUiAsync(() =>
        {
            bridge.ReplaceQueueFunc = async paths => { replacementEntered.SetResult(paths); return await replacementReady.Task; };
        });
        var queuePaths = new[] { opaqueReference, "synthetic-local.flac", "orynivo://missing/track/1" };
        var replacementTool = new McpTools(bridge).ReplaceQueueAsync(queuePaths);
        Check((await replacementEntered.Task.WaitAsync(TimeSpan.FromSeconds(10))).SequenceEqual(queuePaths),
            "MCP queue resolver receives original ordered references");
        Check(!replacementTool.IsCompleted && legacyLookups == 0, "MCP replacement resolves outside command owner");
        replacementReady.SetResult(2);
        Check((await replacementTool).Contains("2 track(s); 1 could not be resolved"), "queue counts use owning resolution result");
        foreach (var next in new[] { false, true })
        {
            var editEntered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var editReady = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            await bridge.OnUiAsync(() =>
            {
                if (next) bridge.PlayNextFunc = async path => { editEntered.SetResult(path); return await editReady.Task; };
                else bridge.AppendToQueueFunc = async path => { editEntered.SetResult(path); return await editReady.Task; };
            });
            var tools = new McpTools(bridge);
            var edit = next ? tools.QueuePlayNextAsync(opaqueReference) : tools.QueueAppendAsync(opaqueReference);
            Check(await editEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)) == opaqueReference && legacyLookups == 0 && !edit.IsCompleted,
                "MCP independent queue edit resolves original identity inside owner");
            editReady.SetResult("fixture.flac");
            Check((await edit).Contains(opaqueReference), "edit confirmation retains opaque identity");
            await bridge.OnUiAsync(() =>
            {
                if (next) bridge.PlayNextFunc = _ => Task.FromResult<string?>(null);
                else bridge.AppendToQueueFunc = _ => Task.FromResult<string?>(null);
            });
            var missing = next ? await tools.QueuePlayNextAsync(opaqueReference) : await tools.QueueAppendAsync(opaqueReference);
            Check(missing.Contains("Could not resolve"), "unresolved independent edit reported success");
        }
        var clearEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clearReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await bridge.OnUiAsync(() => bridge.ClearQueueFunc = async () => { clearEntered.SetResult(); await clearReady.Task; });
        var clearTool = new McpTools(bridge).ClearQueueAsync();
        await clearEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(!clearTool.IsCompleted, "MCP Clear acknowledged before gapless preparation");
        clearReady.SetResult();
        Check((await clearTool).Contains("cleared"), "MCP Clear confirmation missing");
        await bridge.OnUiAsync(() => bridge.ClearQueueFunc = () => Task.FromException(new OperationCanceledException()));
        try { await new McpTools(bridge).ClearQueueAsync(); throw new Exception("Cancelled Clear reported success"); }
        catch (OperationCanceledException) { }
        var snapshot = await client.GetStringAsync("/remote/api/state");
        Check(!snapshot.Contains("private-path") && !snapshot.Contains("secret-path") && !snapshot.Contains(token), "safe state DTO");
        Check(snapshot.Contains("\"title\":\"Fixture track\""), "camelCase state");
        var playlists = await client.GetStringAsync("/remote/api/playlists");
        Check(playlists.Contains("Fixture playlist"), "playlist enumeration");
        var tracks = await client.GetStringAsync("/remote/api/playlists/1/tracks");
        Check(tracks.Contains("local:1") && !tracks.Contains("path"), "safe playlist tracks");
        foreach (var action in new[] { "play", "append" })
        {
            using var result = await client.PostAsJsonAsync("/remote/api/playlists/1/queue", new { action });
            Check(result.StatusCode == HttpStatusCode.NoContent && lastAction == action, "playlist action");
        }
        using var invalid = await client.PostAsJsonAsync("/remote/api/playlists/1/queue", new { action = "delete" });
        Check(invalid.StatusCode == HttpStatusCode.BadRequest, "reject invalid action");
        using var oversized = await client.PostAsync("/remote/api/playlists/1/queue", new StringContent(new string('x', 70_000), Encoding.UTF8, "application/json"));
        Check(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge, "reject oversized request");
        var qrPayload = "http://192.0.2.1:49201/remote#token=synthetic-test-only";
        using var data = QRCodeGenerator.GenerateQrCode(qrPayload, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        Directory.CreateDirectory("out");
        await File.WriteAllBytesAsync("out/mobile-remote-test-qr.png", qr.GetGraphic(6));
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "settings.json"),
            JsonSerializer.Serialize(new { MobileRemoteAccessToken = token, MobileRemoteEnabled = true }));
        var store = new SettingsStore();
        var settings = store.Load();
        Check(settings.MobileRemoteAccessToken == token, "legacy token migration");
        Check(!(await File.ReadAllTextAsync(Path.Combine(fixtureDirectory, "settings.json"))).Contains(token), "plaintext removed");
        settings.MobileRemoteAccessToken = "synthetic-rotated-token";
        store.Save(settings);
        Check(store.Load().MobileRemoteAccessToken == "synthetic-rotated-token", "encrypted token round trip");
        Console.WriteLine("PASS: production host authentication, embedded assets, safe DTOs, media browsing/search/failure/bounds, radio/podcast playback readiness, playlists, command failure, body limit, PNG QR and encrypted token migration/rotation.");
    }
    finally { await host.StopAsync(); stop.Cancel(); }
});
try { dispatcher.MainLoop(stop.Token); } catch (OperationCanceledException) { }
await run.ConfigureAwait(false);

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("Failed: " + name);
}
