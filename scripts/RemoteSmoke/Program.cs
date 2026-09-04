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
    var bridge = new McpPlayerBridge
    {
        GetStateFunc = () => new PlayerState("playing", "Fixture track", "Fixture artist", "Fixture album",
            "private-path-must-not-escape", 1, 120, .25, 0, 1),
        GetQueueFunc = () => [new QueueEntry(0, true, "secret-path", "Fixture track")],
        BrowseMobilePlaylistsFunc = _ => Task.FromResult<IReadOnlyList<MobileRemotePlaylist>>([new(1, "Fixture playlist", 1, false)]),
        BrowseMobilePlaylistTracksFunc = (_, _) => Task.FromResult<IReadOnlyList<MobileRemoteTrack>>([new("local:1", "Fixture track", null, null, 1998, "Local")]),
        QueueMobilePlaylistFunc = (id, action) => { lastAction = action; return Task.FromResult(id == 1); }
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
        foreach (var route in new[] { "/remote/api/state", "/remote/api/playlists", "/remote/api/playlists/1/tracks", "/remote/api/events" })
        {
            using var denied = await client.GetAsync(route);
            Check(denied.StatusCode == HttpStatusCode.Unauthorized, "unauthenticated request");
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
        Console.WriteLine("PASS: production host authentication, embedded resources, safe DTOs, playlist routes/actions, body limit, PNG QR generation, encrypted token migration and rotation.");
    }
    finally { await host.StopAsync(); stop.Cancel(); }
});
try { dispatcher.MainLoop(stop.Token); } catch (OperationCanceledException) { }
await run.ConfigureAwait(false);

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("Failed: " + name);
}
