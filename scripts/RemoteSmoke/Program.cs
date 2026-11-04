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
    IReadOnlyList<string>? lastIdentities = null;
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
        QueueMobileTracksFunc = ids => { lastIdentities = ids; return Task.FromResult(true); },
        GetMobileLibraryArtworkFunc = (key, _) => Task.FromResult<RemoteArtwork?>(key == new string('a', 32)
            ? new RemoteArtwork([1, 2, 3], "image/jpeg", "library-fixture") : null),
        PlayMobilePlaylistFromTrackFunc = (id, index, identity) =>
            Task.FromResult(id == 1 && index == 2 && identity == "local:1"),
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
            "/remote/api/library/artwork?key=" + new string('a', 32),
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
        var artworkEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var artworkReady = new TaskCompletionSource<RemoteArtwork?>();
        var revision = "radio-revision";
        await bridge.OnUiAsync(() =>
        {
            bridge.GetMobilePlaybackInfoFunc = () => new("radio", null, "Fixture station", "Jazz", "MP3 · 128 kbps", null, false, revision);
            bridge.GetCurrentArtworkFunc = () => { artworkEntered.TrySetResult(); return artworkReady.Task; };
        });
        var radioState = await client.GetFromJsonAsync<JsonElement>("/remote/api/state");
        Check(radioState.GetProperty("album").ValueKind == JsonValueKind.Null &&
            radioState.GetProperty("playback").GetProperty("sourceName").GetString() == "Fixture station" &&
            !radioState.GetProperty("playback").GetProperty("canSeek").GetBoolean(), "active radio details exclude stale queue album");
        var oldArtwork = client.GetAsync("/remote/api/artwork?key=radio-revision");
        await artworkEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await bridge.OnUiAsync(() => revision = "podcast-revision");
        artworkReady.SetResult(new RemoteArtwork([1, 2, 3], "image/jpeg", "thumbnail"));
        using (var rejected = await oldArtwork)
            Check(rejected.StatusCode == HttpStatusCode.NotFound, "late source artwork rejected");
        using (var artwork = await client.GetAsync("/remote/api/artwork?key=podcast-revision"))
        {
            Check(artwork.StatusCode == HttpStatusCode.OK &&
                (await artwork.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 1, 2, 3 }), "authenticated current artwork bytes");
            Check(artwork.Headers.CacheControl?.Private == true && artwork.Headers.ETag?.Tag == "\"thumbnail\"", "private artwork validator");
        }
        await bridge.OnUiAsync(() => bridge.GetMobilePlaybackInfoFunc = () =>
            new("podcasts", null, "Fixture publisher", "Culture", "AAC", DateTimeOffset.UnixEpoch, true, revision));
        var podcastState = await client.GetFromJsonAsync<JsonElement>("/remote/api/state");
        Check(podcastState.GetProperty("playback").GetProperty("publishedAt").ValueKind == JsonValueKind.String &&
            podcastState.GetProperty("playback").GetProperty("canSeek").GetBoolean(), "podcast publication and seeking details");
        var playlists = await client.GetStringAsync("/remote/api/playlists");
        Check(playlists.Contains("Fixture playlist"), "playlist enumeration");
        var tracks = await client.GetStringAsync("/remote/api/playlists/1/tracks");
        Check(tracks.Contains("local:1") && !tracks.Contains("path"), "safe playlist tracks");
        using var libraryImage = await client.GetAsync("/remote/api/library/artwork?key=" + new string('a', 32));
        Check(libraryImage.IsSuccessStatusCode && libraryImage.Headers.CacheControl?.Private == true &&
            libraryImage.Headers.ETag?.Tag == "\"library-fixture\"" && (await libraryImage.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 1, 2, 3 }), "private catalog artwork bytes and validator");
        using var missingLibraryImage = await client.GetAsync("/remote/api/library/artwork?key=" + new string('b', 32));
        Check(missingLibraryImage.StatusCode == HttpStatusCode.NotFound, "missing catalog artwork");
        using var invalidLibraryImage = await client.GetAsync("/remote/api/library/artwork?key=unregistered-source");
        Check(invalidLibraryImage.StatusCode == HttpStatusCode.NotFound, "catalog artwork rejects source locations");
        using var cachedLibraryRequest = new HttpRequestMessage(HttpMethod.Get, "/remote/api/library/artwork?key=" + new string('a', 32));
        cachedLibraryRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"library-fixture\""));
        using var cachedLibraryImage = await client.SendAsync(cachedLibraryRequest);
        Check(cachedLibraryImage.StatusCode == HttpStatusCode.NotModified, "catalog artwork cache validation");
        foreach (var action in new[] { "play", "append" })
        {
            using var result = await client.PostAsJsonAsync("/remote/api/playlists/1/queue", new { action });
            Check(result.StatusCode == HttpStatusCode.NoContent && lastAction == action, "playlist action");
        }
        using var invalid = await client.PostAsJsonAsync("/remote/api/playlists/1/queue", new { action = "delete" });
        Check(invalid.StatusCode == HttpStatusCode.BadRequest, "reject invalid action");
        using var suffix = await client.PostAsJsonAsync("/remote/api/playlists/1/queue",
            new { action = "play", startIndex = 2, trackId = "local:1" });
        Check(suffix.StatusCode == HttpStatusCode.NoContent, "playlist occurrence continuation");
        using var staleSuffix = await client.PostAsJsonAsync("/remote/api/playlists/1/queue",
            new { action = "play", startIndex = 2, trackId = "remote:1" });
        Check(staleSuffix.StatusCode == HttpStatusCode.BadRequest, "reject stale playlist identity");
        using var invalidSuffix = await client.PostAsJsonAsync("/remote/api/playlists/1/queue",
            new { action = "append", startIndex = 2, trackId = "local:1" });
        Check(invalidSuffix.StatusCode == HttpStatusCode.BadRequest, "reject invalid suffix action");
        using var librarySuffix = await client.PostAsJsonAsync("/remote/api/tracks/queue",
            new { action = "play", ids = new[] { "local:2", "server:one:1", "local:2" } });
        Check(librarySuffix.StatusCode == HttpStatusCode.NoContent && lastIdentities!.SequenceEqual(new[] { "local:2", "server:one:1", "local:2" }), "ordered library suffix and duplicate identity");
        using var oversizedSuffix = await client.PostAsJsonAsync("/remote/api/tracks/queue",
            new { action = "play", ids = Enumerable.Repeat("local:1", 501).ToArray() });
        Check(oversizedSuffix.StatusCode == HttpStatusCode.BadRequest, "bounded library suffix");
        BrowserAudioState? browserOutput = null;
        var browserEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browserReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await bridge.OnUiAsync(() =>
        {
            bridge.SelectBrowserOutputFunc = async id =>
            {
                Check(dispatcher.CheckAccess(), "browser output selection dispatcher");
                browserEntered.TrySetResult(); await browserReady.Task;
                browserOutput = new(id, "browser-stream", 12, false, .4, 1); return true;
            };
            bridge.GetBrowserAudioStateFunc = () => browserOutput;
            bridge.ReleaseBrowserOutputFunc = id => { if (browserOutput?.ClientId == id) browserOutput = null; return Task.CompletedTask; };
            bridge.OpenBrowserAudioStreamFunc = id => id == browserOutput?.StreamId
                ? async (output, ct) => { await output.WriteAsync(new byte[] { 1, 2, 3, 4 }, ct); } : null;
            bridge.ReportBrowserAudioFunc = report => report.ClientId == browserOutput?.ClientId && report.StreamId == browserOutput?.StreamId;
        });
        var browserClient = Guid.NewGuid().ToString("N");
        var claim = client.PostAsJsonAsync("/remote/api/browser-audio/select", new { clientId = browserClient });
        if (await Task.WhenAny(claim, browserEntered.Task) == claim)
            throw new InvalidOperationException("Browser output claim finished before adapter admission: " + (int)(await claim).StatusCode);
        await browserEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(!claim.IsCompleted, "browser output waits for full old-output release");
        browserReady.SetResult();
        using var claimed = await claim;
        Check(claimed.StatusCode == HttpStatusCode.NoContent, "browser output accepted");
        var originalCookie = claimed.Headers.GetValues("Set-Cookie").Single();
        Check(originalCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
            originalCookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) && !originalCookie.Contains("expires=", StringComparison.OrdinalIgnoreCase),
            "session-only HttpOnly media cookie");
        var browserOutputs = await client.GetFromJsonAsync<JsonElement>("/remote/api/outputs?clientId=" + browserClient);
        Check(browserOutputs.EnumerateArray().Any(x => x.GetProperty("isBrowser").GetBoolean() && x.GetProperty("selected").GetBoolean()), "own browser output listed");
        using (var unclaimed = new HttpClient { BaseAddress = client.BaseAddress })
        {
            unclaimed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var deniedStream = await unclaimed.GetAsync("/remote/api/browser-audio/stream/browser-stream");
            Check(deniedStream.StatusCode == HttpStatusCode.Unauthorized, "bearer alone cannot consume another page's media");
        }
        client.DefaultRequestHeaders.Authorization = null;
        using (var audio = await client.GetAsync("/remote/api/browser-audio/stream/browser-stream"))
            Check(audio.StatusCode == HttpStatusCode.OK && audio.Content.Headers.ContentType?.MediaType == "audio/mpeg" &&
                (await audio.Content.ReadAsByteArrayAsync()).Length == 4 && audio.Headers.CacheControl?.NoStore == true, "cookie-authenticated no-store media bytes");
        using (var cookieOnly = await client.GetAsync("/remote/api/state"))
            Check(cookieOnly.StatusCode == HttpStatusCode.Unauthorized, "media cookie grants no control API access");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using (var feedback = await client.PostAsJsonAsync("/remote/api/browser-audio/report", new BrowserAudioReport(browserClient, "browser-stream", 1, 15, true)))
            Check(feedback.StatusCode == HttpStatusCode.NoContent, "owned browser feedback");
        var secondBrowser = Guid.NewGuid().ToString("N");
        using (var transferred = await client.PostAsJsonAsync("/remote/api/browser-audio/select", new { clientId = secondBrowser }))
            Check(transferred.StatusCode == HttpStatusCode.NoContent, "second browser takeover");
        using (var oldClient = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = client.BaseAddress })
        {
            oldClient.DefaultRequestHeaders.Add("Cookie", originalCookie.Split(';')[0]);
            using var staleMedia = await oldClient.GetAsync("/remote/api/browser-audio/stream/browser-stream");
            Check(staleMedia.StatusCode == HttpStatusCode.Unauthorized, "previous browser cookie revoked");
        }
        using (var staleRelease = await client.PostAsJsonAsync("/remote/api/browser-audio/release", new { clientId = browserClient }))
            Check(staleRelease.StatusCode == HttpStatusCode.NoContent && browserOutput?.ClientId == secondBrowser, "old page cannot release new output");
        var releaseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await bridge.OnUiAsync(() => bridge.ReleaseBrowserOutputFunc = async id =>
        {
            if (id == secondBrowser) { releaseEntered.SetResult(); await releaseReady.Task; }
            if (browserOutput?.ClientId == id) browserOutput = null;
        });
        var delayedRelease = client.PostAsJsonAsync("/remote/api/browser-audio/release", new { clientId = secondBrowser });
        await releaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var thirdBrowser = Guid.NewGuid().ToString("N");
        try
        {
            using var reclaimed = await client.PostAsJsonAsync("/remote/api/browser-audio/select", new { clientId = thirdBrowser });
            Check(reclaimed.StatusCode == HttpStatusCode.NoContent, "takeover during old release cleanup");
        }
        finally { releaseReady.TrySetResult(); }
        using (var oldReleaseResult = await delayedRelease)
            Check(oldReleaseResult.StatusCode == HttpStatusCode.NoContent, "old output cleanup completes independently");
        using (var newMedia = await client.GetAsync("/remote/api/browser-audio/stream/browser-stream"))
            Check(newMedia.StatusCode == HttpStatusCode.OK && browserOutput?.ClientId == thirdBrowser,
                "late release must not erase the new media cookie");
        using (var released = await client.PostAsJsonAsync("/remote/api/browser-audio/release", new { clientId = thirdBrowser }))
            Check(released.StatusCode == HttpStatusCode.NoContent && browserOutput is null, "browser release without native fallback");
        using (var revoked = await client.GetAsync("/remote/api/browser-audio/stream/browser-stream"))
            Check(revoked.StatusCode == HttpStatusCode.Unauthorized, "released media cookie rejected");
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
        Console.WriteLine("PASS: production host authentication, safe DTOs, media/library readiness, browser output admission, scoped media cookies, streaming, takeover/release, controls, limits, QR and encrypted credential migration.");
    }
    finally { await host.StopAsync(); stop.Cancel(); }
});
try { dispatcher.MainLoop(stop.Token); } catch (OperationCanceledException) { }
await run.ConfigureAwait(false);

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("Failed: " + name);
}
