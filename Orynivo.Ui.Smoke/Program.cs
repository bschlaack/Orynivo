using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Orynivo;
using Orynivo.Audio;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo.Ui.Smoke;

/// <summary>Runs isolated Windows headless workflow checks without application startup, playback or external services.</summary>
internal static class Program
{
    /// <summary>Runs the tracked smoke scenarios in their own process and temporary data root.</summary>
    /// <returns>Zero for successful checks, or one for a failed scenario or unsupported host.</returns>
    [STAThread]
    private static int Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("This initial UI smoke suite is verified on Windows only.");
            return 1;
        }
        var root = Path.Combine(Path.GetTempPath(), "orynivo-ui-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".legacy-data-migrated"), "isolated synthetic fixture");
        Environment.SetEnvironmentVariable("ORYNIVO_DATA_DIR", root);
        Environment.SetEnvironmentVariable("FANART_TV_API_KEY", null);
        // Keep first-run normalization away from the real default endpoint.
        File.WriteAllText(Path.Combine(root, "settings.json"), JsonSerializer.Serialize(new AppSettings
        {
            OutputProfiles = [new OutputProfile { Name = "Synthetic output", Backend = OutputBackend.Wasapi,
                SelectedWasapiDeviceId = "synthetic-endpoint", SelectedWasapiDeviceName = "Synthetic output" }],
            SelectedOutputProfileName = "Synthetic output"
        }));
        try
        {
            DenyExternalRequests.Install();
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions
                { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
            EmptyLibraryWorkflows.Run(root);
            DirectorySetupWorkflows.Run(root);
            using var fixture = new ArtistWorkflowFixture(root);
            fixture.Run();
            SearchWorkflows.Run();
            AlbumWorkflows.Run();
            LocalAlbumWorkflows.Run(root);
            AlbumCacheWorkflows.Run();
            CatalogRecoveryWorkflows.Run();
            ViewRefreshWorkflows.Run();
            ShuffleWorkflows.Run();
            BrowserOutputReclaimWorkflows.Run();
            CoreAudioProfileWorkflows.Run();
            if (DenyExternalRequests.Requests != 0)
                throw new InvalidOperationException("Cached workflows attempted external provider requests.");
            Console.WriteLine("All Windows UI smoke workflows passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("UI smoke failed: " + exception.Message);
            return 1;
        }
    }
}

/// <summary>Fails closed for external profile/image transport inside this dedicated smoke process.</summary>
internal sealed class DenyExternalRequests : HttpMessageHandler
{
    private static int _requests;
    /// <summary>Gets the number of unexpected requests, without retaining addresses or headers.</summary>
    internal static int Requests => Volatile.Read(ref _requests);

    /// <summary>Replaces reachable external HTTP handlers before creating the application or window.</summary>
    internal static void Install()
    {
        var clients = new[]
        {
            (typeof(ArtistProfileService), "Client"),
            (typeof(ArtistImageSearchService), "Client"),
            (typeof(FanartTvArtistImageService), "Client"),
            (typeof(OrynivoServerNowPlayingMetadataProvider), "ImageClient"),
            (typeof(MainWindow), "RemoteArtworkHttpClient")
        };
        var handlerField = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The smoke HTTP isolation adapter needs updating.");
        foreach (var (type, name) in clients)
        {
            var client = (HttpClient)(type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
                ?? throw new InvalidOperationException("A smoke provider transport adapter needs updating."));
            var original = (HttpMessageHandler)handlerField.GetValue(client)!;
            handlerField.SetValue(client, new DenyExternalRequests());
            original.Dispose();
        }
    }

    /// <summary>Counts and rejects a request without using a network transport or logging its data.</summary>
    /// <param name="request">Unexpected provider request, never retained.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>A synthetic unavailable response.</returns>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}

/// <summary>Exercises the production artist detail and Back adapters using temporary SQLite and fake HTTP.</summary>
internal sealed class ArtistWorkflowFixture : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ArtistName = "Synthetic cached artist";
    private const string Biography = "Synthetic cached biography";
    private readonly MainWindow _window = new();
    private readonly DataGrid _grid;
    private readonly Control _view;
    private readonly SyntheticArtistHandler _handler;
    private readonly HttpClient _transport;
    private readonly OrynivoServerSettings _server = new()
        { Id = "smoke", Name = "Synthetic source", BaseUrl = "https://smoke.invalid" };
    private readonly long _artistId;
    private readonly object _origin;

    /// <summary>Seeds current cached metadata and replaces all Orynivo Server transport before navigation.</summary>
    /// <param name="root">Process-owned temporary data directory.</param>
    internal ArtistWorkflowFixture(string root)
    {
        using (var db = AudioDatabase.OpenDefault())
        {
            db.Upsert(new TrackRecord { Path = Path.Combine(root, "synthetic.flac"), FileName = "synthetic.flac",
                Title = "Synthetic track", Artist = ArtistName, AlbumArtist = ArtistName,
                Album = "Synthetic album", Format = "flac" });
            _artistId = db.GetArtistsLite().Single().Id;
            db.UpdateArtistProfile(_artistId, Biography, null, null, "en");
        }
        _handler = new SyntheticArtistHandler(_artistId, ArtistName, Biography);
        _transport = new HttpClient(_handler);
        typeof(OrynivoServerClient).GetField("_http", Private)!.SetValue(Get("_orynivoClient"), _transport);
        var settings = (AppSettings)Get("_settings");
        settings.Language = Language.English; settings.OrynivoServers = [_server];
        _grid = _window.FindControl<DataGrid>("ContentDataGrid")!;
        _view = _window.FindControl<Control>("ArtistInfoView")!;
        Set("_activeOrynivoServer", _server); Set("_activeOrynivoView", "Artists");
        Set("_currentTopLevelTag", "OrynivoServer:smoke:Artists");
        _view.IsVisible = false;
        _window.FindControl<ScrollViewer>("SearchResultsScrollViewer")!.IsVisible = false;
        _window.FindControl<Control>("GenreCloudPanel")!.IsVisible = false;
        var selected = RemoteRow();
        _grid.ItemsSource = new[] { new ContentRow { Id = _artistId, Title = "Colliding local", EntityType = "Artist" }, selected };
        _grid.IsVisible = true; _grid.SelectedItem = selected;
        _origin = Call("CaptureCurrentNavigationState")!;
    }

    /// <summary>Runs cached navigation, actual Back, rejected late work and compiled scroll scenarios.</summary>
    internal void Run()
    {
        Finish((Task)Call("ShowUnifiedArtistAlbumsAsync", ArtistName)!);
        Check(_view.IsVisible && _window.FindControl<TextBlock>("ArtistInfoBiographyTextBlock")!.Text == Biography &&
            _window.FindControl<Panel>("ArtistInfoAlbumsPanel")!.Children.Count > 0,
            "Cached unified artist did not publish its biography and local album.");
        Pass("cached local/server artist detail loads without external biography lookup");
        Back();
        Pass("actual Back restores server selection despite a colliding local ID");

        OpenForBack();
        _handler.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldProfile = (Task)Call("ShowOrynivoArtistInfoAsync", RemoteRow(), false, null)!;
        Check(!oldProfile.IsCompleted, "Synthetic metadata gate did not hold the profile.");
        Back(); var restored = _grid.ItemsSource; var selected = _grid.SelectedItem;
        var biography = _window.FindControl<TextBlock>("ArtistInfoBiographyTextBlock")!;
        biography.Text = "Restored surface sentinel";
        _handler.ReadGate.SetResult(); Finish(oldProfile); _handler.ReadGate = null;
        Check(ReferenceEquals(restored, _grid.ItemsSource) && ReferenceEquals(selected, _grid.SelectedItem) &&
            !_view.IsVisible && biography.Text == "Restored surface sentinel", "Late profile replaced Back state.");
        Pass("late server profile completion cannot replace the restored view");

        OpenForBack();
        var selectionGate = new TaskCompletionSource<(byte[] Data, string MimeType)?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var selectionTask = (Task)Call("UploadDisplayedArtistImageAsync",
            (Func<Task<(byte[], string)?>>)(() => selectionGate.Task))!;
        Back(); restored = _grid.ItemsSource; selected = _grid.SelectedItem;
        selectionGate.SetResult((CreateImage(), "image/png")); Finish(selectionTask);
        using (var db = AudioDatabase.OpenDefault())
            Check(db.GetArtistById(_artistId)!.ImagePath is null && _handler.Uploads == 0 &&
                ReferenceEquals(restored, _grid.ItemsSource) && ReferenceEquals(selected, _grid.SelectedItem),
                "Late image selection wrote data or replaced Back state.");
        Pass("image selection accepted after Back performs no mutation");
        CheckScrollWorkflow();
        Check(((ArtistImageIntentOwnership)Get("_artistImageWrites")).ActiveObservationCount == 0,
            "Completed workflows retained artist image observations.");
    }

    /// <summary>Checks actual compiled scroll capture, guarded callbacks and cached unified Back restoration.</summary>
    private void CheckScrollWorkflow()
    {
        Finish((Task)Call("ShowUnifiedArtistAlbumsAsync", ArtistName)!);
        var scroll = _window.FindControl<ScrollViewer>("ArtistInfoScrollViewer")!;
        var parent = (Grid)scroll.Parent!; parent.Children.Remove(scroll);
        var host = new Window { Width = 700, Height = 220, Content = scroll };
        try
        {
            host.Show(); Dispatcher.UIThread.RunJobs();
            scroll.Measure(new Size(700, 220)); scroll.Arrange(new Rect(0, 0, 700, 220));
            scroll.Offset = new Vector(0, 80);
            Check(scroll.Offset.Y == 80, "Compiled artist scroll lacks a bounded viewport.");
            var state = Call("CaptureCurrentNavigationState")!;
            Check((double?)state.GetType().GetProperty("VerticalOffset")!.GetValue(state) == 80 &&
                state.GetType().GetProperty("SelectedId")!.GetValue(state) is null,
                "Artist capture borrowed hidden table selection or offset.");
            var owns = true; scroll.Offset = new Vector(0, 25);
            Call("RestoreArtistInfoPositionAfterLayout", 80d, (Func<bool>)(() => owns));
            owns = false; Dispatcher.UIThread.RunJobs();
            Check(scroll.Offset.Y == 25, "Abandoned Back callback moved artist scroll.");
            Finish((Task)Call("RestoreNavigationStateAsync", state)!);
            Dispatcher.UIThread.RunJobs();
            Check(_view.IsVisible && scroll.Offset.Y == 80 &&
                _window.FindControl<TextBlock>("ArtistInfoBiographyTextBlock")!.Text == Biography,
                "Actual cached artist Back did not restore outer scroll and biography.");
            Pass("compiled artist scroll captures and restores only current Back ownership");
        }
        finally
        {
            host.Content = null; host.Close(); parent.Children.Insert(0, scroll);
        }
    }

    /// <summary>Opens a synthetic detail with one original server history entry.</summary>
    private void OpenForBack()
    {
        var stack = Get("_navigationStack");
        stack.GetType().GetMethod("Clear")!.Invoke(stack, null);
        stack.GetType().GetMethod("Push")!.Invoke(stack, [_origin]);
        Set("_currentTopLevelTag", "UnifiedArtistAlbums"); Set("_artistInfoUnifiedArtistName", ArtistName);
        Set("_activeArtistFilterName", ArtistName); Set("_artistInfoDisplayedId", (long?)_artistId);
        _view.IsVisible = true;
    }

    /// <summary>Clicks the real Back adapter and waits for source-aware origin restoration.</summary>
    private void Back()
    {
        Call("BackButton_OnClick", null, new RoutedEventArgs());
        PumpUntil(() => !_view.IsVisible && !((NavigationRestoreOwnership)Get("_navigationRestores")).IsRestoring &&
            (string?)Get("_currentTopLevelTag") == "OrynivoServer:smoke:Artists");
        Check(_grid.SelectedItem is ContentRow selected && selected.Id == _artistId && selected.OrynivoServer?.Id == "smoke",
            "Back lost its source-aware selection.");
        Check((int)Get("_navigationStack").GetType().GetProperty("Count")!.GetValue(Get("_navigationStack"))! == 0,
            "Back added replacement history.");
    }

    /// <summary>Creates valid tiny PNG bytes so rejection does not depend on invalid-input validation.</summary>
    /// <returns>The complete synthetic image bytes.</returns>
    private static byte[] CreateImage()
    {
        using var image = new RenderTargetBitmap(new PixelSize(4, 4));
        using var stream = new MemoryStream();
        image.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>Creates a colliding remote artist identity.</summary>
    /// <returns>The synthetic source-owned row.</returns>
    private ContentRow RemoteRow() => new() { Id = _artistId, ExternalId = _artistId.ToString(),
        Title = ArtistName, EntityType = "OrynivoArtist", OrynivoServer = _server };

    /// <summary>Reads one production adapter field.</summary>
    /// <param name="name">Private field name.</param>
    /// <returns>The adapter-owned value.</returns>
    private object Get(string name) => typeof(MainWindow).GetField(name, Private)!.GetValue(_window)!;

    /// <summary>Sets only the synthetic navigation context.</summary>
    /// <param name="name">Private context field name.</param>
    /// <param name="value">Synthetic value.</param>
    private void Set(string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(_window, value);

    /// <summary>Invokes the production adapter without introducing a competing navigation model.</summary>
    /// <param name="name">Private adapter method name.</param>
    /// <param name="arguments">Original adapter arguments.</param>
    /// <returns>The production adapter result.</returns>
    private object? Call(string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(_window, arguments);

    /// <summary>Pumps dispatcher work until an asynchronous adapter completes.</summary>
    /// <param name="task">Production adapter task.</param>
    private static void Finish(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }

    /// <summary>Uses a diagnostic timeout rather than a timing assertion.</summary>
    /// <param name="complete">Completion condition.</param>
    private static void PumpUntil(Func<bool> complete)
    {
        var timer = Stopwatch.StartNew();
        while (!complete() && timer.Elapsed < TimeSpan.FromSeconds(15))
        { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Dispatcher.UIThread.RunJobs(); Check(complete(), "UI workflow timed out.");
    }

    /// <summary>Throws a fixed diagnostic when a workflow invariant fails.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Credential-free diagnostic.</param>
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    /// <summary>Reports a completed fixed workflow name.</summary>
    /// <param name="name">Credential-free scenario name.</param>
    private static void Pass(string name) => Console.WriteLine("PASS: " + name);

    /// <summary>Releases fake transport and cancels remaining optional profile work without showing MainWindow.</summary>
    public void Dispose()
    {
        _handler.ReadGate?.TrySetResult();
        Call("CancelArtistProfileLoad"); Dispatcher.UIThread.RunJobs(); _transport.Dispose();
    }
}

/// <summary>Returns only synthetic cached metadata and never forwards HTTP to an external transport.</summary>
internal sealed class SyntheticArtistHandler : HttpMessageHandler
{
    private readonly OrynivoArtistInfo _artist;

    /// <summary>Creates a fresh cached profile with no remote artwork.</summary>
    /// <param name="id">Colliding source-local ID.</param>
    /// <param name="name">Synthetic canonical name.</param>
    /// <param name="biography">Synthetic cached biography.</param>
    internal SyntheticArtistHandler(long id, string name, string biography) => _artist =
        new(id, name, false, biography, null, "en", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), true, false, false);

    /// <summary>Gets or sets the intentionally cancellation-insensitive metadata gate.</summary>
    internal TaskCompletionSource? ReadGate { get; set; }
    /// <summary>Gets the number of attempted image mutations.</summary>
    internal int Uploads { get; private set; }

    /// <summary>Returns cached profiles, an empty remote album list or a controlled late profile response.</summary>
    /// <param name="request">Synthetic client request.</param>
    /// <param name="cancellationToken">Transport cancellation; the deliberate late gate ignores it.</param>
    /// <returns>A wholly synthetic response.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method != HttpMethod.Get) { Uploads++; return new(HttpStatusCode.BadRequest); }
        if (path == "/api/artists") return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { _artist }) };
        if (path == $"/api/artists/{_artist.Id}")
        {
            if (ReadGate is not null) await ReadGate.Task;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(_artist) };
        }
        if (path.Contains("albums", StringComparison.Ordinal))
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<OrynivoAlbumInfo>()) };
        return new(HttpStatusCode.NotFound);
    }
}
