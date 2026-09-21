using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Orynivo.Audio;
using Orynivo.Localization;
using Orynivo.Visualization;

namespace Orynivo;

/// <summary>
/// Fullscreen music visualizer. It renders a low-resolution frame through the preset engine
/// and lets the image control scale it up, which keeps the CPU cost bounded. Presets switch
/// with the keyboard, a click, or the arrow keys; Escape closes the window. While the window
/// is open it is the only consumer of <see cref="VisualizerAudioHub"/>, so playback is
/// unaffected when it is closed.
/// </summary>
public partial class VisualizerWindow : Window
{
    private static readonly TimeSpan ReducedMotionInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan OverlayIdleTimeout = TimeSpan.FromSeconds(3);
    private readonly int _renderWidth;
    private readonly int _renderHeight;
    private readonly TimeSpan _frameInterval;
    private readonly bool _alwaysShowOverlay;
    private readonly string? _presetDirectory;
    private bool _discoveryStarted;
    private bool _discoveryLogged;
    private int _discoveryMilliseconds = -1;
    private DateTimeOffset _lastPointerActivity = DateTimeOffset.MinValue;

    private readonly WriteableBitmap _bitmap;
    private readonly PixelBuffer _presentBuffer;
    private readonly object _presentLock = new();
    private readonly VisualizerPresetLibrary _library = new();
    private readonly SilentAudioSource _silent = new();
    private readonly Stopwatch _renderClock = new();
    private PresetRenderer _renderer;
    private Thread? _renderThread;
    private volatile bool _renderRunning;
    private volatile bool _closed;
    private volatile int _presetIndex;
    private int _renderedPresetIndex = -1;
    private volatile bool _resetRequested;
    private int _presentPending;
    private volatile string? _pendingDiagnostics;
    private double _lastFrame;
    private double _lastDiagnostics;
    private VisualizerTransport? _transport;
    private bool _isPlaying = true;

    /// <summary>Initializes the window for the Avalonia designer.</summary>
    public VisualizerWindow()
        : this(0, new VisualizerRenderOptions(480, 270, 30, false, true, null))
    {
    }

    /// <summary>Creates the visualizer window.</summary>
    /// <param name="presetIndex">Index of the preset to start with.</param>
    /// <param name="options">Render resolution, frame rate, reduce-motion state, and preset folder.</param>
    /// <param name="transport">
    /// Callbacks into the main window so the overlay buttons drive the real playback, or
    /// <see langword="null"/> to hide them.
    /// </param>
    public VisualizerWindow(
        int presetIndex,
        VisualizerRenderOptions options,
        VisualizerTransport? transport = null)
    {
        _transport = transport;
        _presetIndex = presetIndex;
        _renderWidth = Math.Clamp(options.Width, 160, 3840);
        _renderHeight = Math.Clamp(options.Height, 90, 2160);
        _frameInterval = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(options.FrameRate, 5, 240));
        // Only the built-ins are available here. Enumerating a real preset collection is a disk
        // walk over thousands of files and must never run while the window is being constructed,
        // because that would block the interface for as long as it takes.
        _presetDirectory = options.PresetDirectory;
        _library.LoadBuiltIns();
        _renderer = new PresetRenderer(_library.At(_presetIndex), _renderWidth, _renderHeight);
        _bitmap = new WriteableBitmap(
            new Avalonia.PixelSize(_renderWidth, _renderHeight),
            new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        _presentBuffer = new PixelBuffer(_renderWidth, _renderHeight);
        ReduceMotion = options.ReduceMotion;
        _alwaysShowOverlay = options.AlwaysShowOverlay;

        InitializeComponent();
        VisualizerImage.Source = _bitmap;
        HintTextBlock.Text = LocalizationManager.Current.VisualizerHint;
        UpdatePresetLabel();

        Opened += (_, _) =>
        {
            VisualizerAudioHub.Shared.Clear();
            VisualizerAudioHub.Shared.IsActive = true;
            StartRenderThread();
        };
        Closed += (_, _) =>
        {
            StopRenderThread();
            VisualizerAudioHub.Shared.IsActive = false;
            VisualizerAudioHub.Shared.Clear();
        };
        KeyDown += OnKeyDown;
        OverlayRoot.IsVisible = _alwaysShowOverlay;
        if (!_alwaysShowOverlay)
        {
            // PointerMoved only fires while the pointer is over this window, so moving the
            // mouse on another monitor never reveals the overlay.
            PointerMoved += (_, _) => ShowOverlay();
            PointerExited += (_, _) => _lastPointerActivity = DateTimeOffset.MinValue;
        }
        PointerPressed += (_, e) =>
        {
            // A click on one of the overlay buttons must not also switch the preset.
            if (e.Source is Visual source && source.GetVisualAncestors().OfType<Button>().Any())
                return;
            SelectPreset(_presetIndex + 1);
            ShowOverlay();
        };
    }

    /// <summary>
    /// Gets a value indicating whether the reduce-motion preference is active, which renders
    /// a static spectrum at a low frame rate instead of animating.
    /// </summary>
    public bool ReduceMotion { get; }

    /// <summary>Gets the index of the preset currently being rendered.</summary>
    public int PresetIndex => _presetIndex;

    /// <summary>
    /// Shows the current track above the picture. Called from the render loop so the overlay
    /// follows track changes without an extra subscription.
    /// </summary>
    /// <param name="title">Track title.</param>
    /// <param name="artist">Track artist.</param>
    private void SetTrack(string? title, string? artist)
    {
        title ??= string.Empty;
        artist ??= string.Empty;
        if (NowPlayingTitleTextBlock.Text != title)
            NowPlayingTitleTextBlock.Text = title;
        if (NowPlayingArtistTextBlock.Text != artist)
        {
            NowPlayingArtistTextBlock.Text = artist;
            NowPlayingArtistTextBlock.IsVisible = artist.Length > 0;
        }
    }

    /// <summary>
    /// Selects a preset by index, wrapping around the available presets. The render thread picks
    /// the request up on its next frame, so this stays a cheap UI-thread operation.
    /// </summary>
    /// <param name="index">Requested preset index.</param>
    public void SelectPreset(int index)
    {
        var count = _library.Count;
        if (count == 0)
            return;

        _presetIndex = ((index % count) + count) % count;
        VisualizerAudioHub.Shared.Clear();
        UpdatePresetLabel();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.N or Key.Right or Key.Down or Key.Space:
                SelectPreset(_presetIndex + 1);
                break;
            case Key.P or Key.Left or Key.Up:
                SelectPreset(_presetIndex - 1);
                break;
            case Key.R:
                _resetRequested = true;
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Starts the render thread. Rendering a frame is the expensive part of the visualizer, so it
    /// runs away from the UI thread and only the finished frame is handed back for display.
    /// </summary>
    private void StartRenderThread()
    {
        if (_renderThread is not null)
            return;

        _closed = false;
        _renderRunning = true;
        _renderClock.Restart();
        _lastFrame = 0d;
        _lastDiagnostics = 0d;
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "Orynivo visualizer"
        };
        _renderThread.Start();
    }

    /// <summary>Stops the render thread and waits briefly for the frame in flight to finish.</summary>
    private void StopRenderThread()
    {
        _closed = true;
        _renderRunning = false;
        var thread = _renderThread;
        _renderThread = null;
        thread?.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>Renders frames at the configured rate until the window closes.</summary>
    private void RenderLoop()
    {
        while (_renderRunning)
        {
            var interval = (ReduceMotion ? ReducedMotionInterval : _frameInterval).TotalSeconds;
            var now = _renderClock.Elapsed.TotalSeconds;
            var wait = FramePacing.NextWait(interval, now - _lastFrame);
            if (wait > TimeSpan.Zero)
                Thread.Sleep(wait);

            var frameNow = _renderClock.Elapsed.TotalSeconds;
            var delta = frameNow - _lastFrame;
            _lastFrame = frameNow;
            RenderOneFrame(delta);
        }
    }

    /// <summary>Renders one frame on the render thread and hands it to the UI thread.</summary>
    /// <param name="deltaSeconds">Seconds since the previous frame.</param>
    private void RenderOneFrame(double deltaSeconds)
    {
        if (_renderedPresetIndex != _presetIndex)
        {
            _renderedPresetIndex = _presetIndex;
            _renderer = new PresetRenderer(_library.At(_presetIndex), _renderWidth, _renderHeight);
            _resetRequested = false;
        }

        if (_resetRequested)
        {
            _resetRequested = false;
            _renderer.Reset();
        }

        // Render even when nothing is playing, so the window never stays black.
        if (!VisualizerAudioHub.Shared.TryAnalyze(out var audio) || audio is null)
            audio = _silent;

        if (ReduceMotion)
            _renderer.RenderOverlayOnly(audio);
        else
            _renderer.RenderFrame(audio, deltaSeconds);

        // The preset collection is enumerated once, on a worker thread, after the first frame has
        // been shown; the built-ins keep the visualizer usable while that runs.
        if (!_discoveryStarted && _renderer.FrameCount >= 1)
        {
            _discoveryStarted = true;
            var folder = _presetDirectory;
            _ = Task.Run(() =>
            {
                var elapsed = _library.Discover(folder);
                Interlocked.Exchange(ref _discoveryMilliseconds, (int)elapsed.TotalMilliseconds);
            });
        }

        // Hand a finished copy to the UI thread instead of the live buffer, so the next frame can
        // start immediately without the two threads ever touching the same pixels.
        lock (_presentLock)
        {
            _presentBuffer.CopyFrom(_renderer.Output);
        }

        PostPresent(BuildDiagnostics());
    }

    /// <summary>
    /// Hands the finished frame to the UI thread. At most one present is queued at a time, so a
    /// busy UI thread can never build up a backlog of frames.
    /// </summary>
    /// <param name="diagnostics">Diagnostic line to write, or <see langword="null"/>.</param>
    private void PostPresent(string? diagnostics)
    {
        if (diagnostics is not null)
            _pendingDiagnostics = diagnostics;

        if (Interlocked.Exchange(ref _presentPending, 1) == 1)
            return;

        Dispatcher.UIThread.Post(
            () =>
            {
                Interlocked.Exchange(ref _presentPending, 0);
                if (_closed)
                    return;

                Present();
                UpdatePlayPauseIcon();
                UpdateOverlayVisibility();
                if (_transport is { } transport)
                {
                    var nowPlaying = transport.NowPlaying();
                    SetTrack(nowPlaying.Title, nowPlaying.Artist);
                }

                if (_pendingDiagnostics is { } message)
                {
                    _pendingDiagnostics = null;
                    SeekDiagnostics.Log("visualizer", message);
                }

                if (!_discoveryLogged && _discoveryMilliseconds >= 0)
                {
                    _discoveryLogged = true;
                    SeekDiagnostics.Log(
                        "visualizer",
                        $"presetDiscoveryMs={_discoveryMilliseconds} "
                        + $"userPresets={_library.Count - VisualizerPresets.BuiltIn.Count}");
                }
            },
            DispatcherPriority.Normal);
    }

    /// <summary>
    /// Builds the once-per-second diagnostic line so an empty window can be told apart from a
    /// picture that never reaches the screen, and so the render cost per stage is measurable
    /// instead of guessed. The timings are averaged over the frames of this second. Only counts,
    /// a brightness average, and stage timings are recorded, never media names or paths.
    /// </summary>
    /// <returns>The line, or <see langword="null"/> when a second has not passed yet.</returns>
    private string? BuildDiagnostics()
    {
        var now = _renderClock.Elapsed.TotalSeconds;
        if (now - _lastDiagnostics < 1d)
            return null;

        _lastDiagnostics = now;
        var pixels = _renderer.Output.Pixels;
        var total = 0f;
        var samples = 0;
        for (var index = 0; index < pixels.Length; index += 64)
        {
            total += pixels[index] + pixels[index + 1] + pixels[index + 2];
            samples += 3;
        }

        var timings = _renderer.AverageTimings;
        var message =
            $"frames={_renderer.FrameCount} audioFrames={VisualizerAudioHub.Shared.AnalyzedFrames} "
            + $"reduceMotion={ReduceMotion} brightness={(samples == 0 ? 0f : total / samples):F4} "
            + $"size={_renderWidth}x{_renderHeight} "
            + $"renderMs={timings.Total:F2} warpMs={timings.Warp:F2} blurMs={timings.Blur:F2} "
            + $"postMs={timings.PostProcess:F2} overlayMs={timings.Overlay:F2} "
            + $"compositeMs={timings.Composite:F2} compShaderMs={timings.Shader:F2} "
            + $"shaders=warp{_renderer.Preset.WarpShaders.Count}/comp{_renderer.Preset.CompShaders.Count} "
            + $"gridReduced={_renderer.ShaderGridReduced} "
            + $"preset={_renderer.Preset.Name} userPresets={_library.Count - VisualizerPresets.BuiltIn.Count}";
        // Start a fresh averaging window so the next line describes its own second.
        _renderer.ResetTimings();
        return message;
    }

    private void Present()
    {
        using var buffer = _bitmap.Lock();
        var stride = _renderWidth * 4;
        // The render thread keeps working on its own buffers, so this thread reads the finished
        // presentation copy. The lock is held for the copy only, never for a whole frame.
        lock (_presentLock)
        {
            if (buffer.RowBytes == stride)
            {
                unsafe
                {
                    _presentBuffer.WriteBgra(new Span<byte>((void*)buffer.Address, stride * _renderHeight));
                }
            }
            else
            {
                // Padded rows: copy one row at a time so the padding stays untouched.
                var row = new byte[stride];
                for (var y = 0; y < _renderHeight; y++)
                {
                    _presentBuffer.WriteRowBgra(y, row);
                    System.Runtime.InteropServices.Marshal.Copy(
                        row,
                        0,
                        buffer.Address + (y * buffer.RowBytes),
                        stride);
                }
            }
        }

        // Writing the bitmap is not enough on its own: the image has to be invalidated so
        // the freshly written frame is actually painted.
        VisualizerImage.InvalidateVisual();
    }

    /// <summary>Reveals the overlay after pointer activity and restarts its idle timeout.</summary>
    private void ShowOverlay()
    {
        _lastPointerActivity = DateTimeOffset.UtcNow;
        if (!OverlayRoot.IsVisible)
            OverlayRoot.IsVisible = true;
    }

    /// <summary>Hides the overlay again once the pointer has been idle for a while.</summary>
    private void UpdateOverlayVisibility()
    {
        if (_alwaysShowOverlay || !OverlayRoot.IsVisible)
            return;
        if (DateTimeOffset.UtcNow - _lastPointerActivity > OverlayIdleTimeout)
            OverlayRoot.IsVisible = false;
    }

    private void PreviousTrackButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _transport?.Previous();

    private void PlayPauseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _isPlaying = !_isPlaying;
        UpdatePlayPauseIcon();
        _transport?.PlayPause();
    }

    private void NextTrackButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _transport?.Next();

    private void UpdatePlayPauseIcon()
    {
        if (_transport is { IsPlaying: { } isPlaying })
            _isPlaying = isPlaying();

        PlayPauseIconPath.Data = (Avalonia.Media.Geometry?)this.FindResource(
            _isPlaying ? "IconPauseGlyph" : "IconPlayGlyph");
    }

    /// <summary>Silent fallback so a preset renders before playback starts or while paused.</summary>
    private sealed class SilentAudioSource : IVisualizerAudioSource
    {
        private readonly float[] _bands = new float[AudioSpectrumAnalyzer.BandCount];
        private readonly float[] _waveform = new float[AudioSpectrumAnalyzer.WaveformPoints];

        public ReadOnlySpan<float> Bands => _bands;

        public ReadOnlySpan<float> Waveform => _waveform;

        public float Bass => 0f;

        public float Mid => 0f;

        public float Treble => 0f;

        public float Volume => 0f;
    }

    private void UpdatePresetLabel()
    {
        var label = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Current.VisualizerPresetLabel,
            _renderer.Preset.Name);
        if (_library.RejectedFiles.Count > 0)
            label = $"{label}  ·  {_library.RejectedFiles.Count} x {LocalizationManager.Current.VisualizerPresetRejected}";
        PresetTextBlock.Text = label;
    }
}
