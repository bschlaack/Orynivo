using System.Collections.Concurrent;
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
    private bool _useGlPresenter;
    private byte[] _glBytes = [];
    private byte[] _glOverlayBytes = [];
    private string? _glWarpShader;
    private string? _glCompShader;
    private IReadOnlyList<string>? _glPerPixelUniforms;
    private readonly Dictionary<string, ShaderValue> _glUniforms = new(StringComparer.Ordinal);
    private bool _glErrorLogged;
    private bool _glInfoLogged;
    private bool _glPipelineActive;
    private int _glMeshX;
    private int _glMeshY;
    private VisualizerFrameParameters _glParameters;
    private readonly float[] _glMeshSnapshot =
        new float[(PresetRenderer.MeshGridX + 1) * (PresetRenderer.MeshGridY + 1) * PresetRenderer.MeshValues];

    /// <summary>
    /// Applies the GL mode's renderer settings. The GPU pipeline owns the frame when the preset has no
    /// shaders, and also when its shaders emit GLSL, because the pipeline can then run them; a preset
    /// whose shaders the GLSL dialect cannot express keeps the CPU frame path.
    /// </summary>
    /// <param name="renderer">Renderer to configure.</param>
    private void ConfigureRenderer(PresetRenderer renderer)
    {
        if (!_useGlPresenter)
            return;

        renderer.MeshRequested = true;
        _glWarpShader = null;
        _glCompShader = null;
        _glPerPixelUniforms = null;
        if (!renderer.HasShaders)
        {
            renderer.ExpressionsOnly = true;
            return;
        }

        if (TryEmitGlsl(renderer, out var warp, out var comp, out var uniforms))
        {
            renderer.ExpressionsOnly = true;
            _glWarpShader = warp;
            _glCompShader = comp;
            _glPerPixelUniforms = uniforms;
        }
        else
        {
            renderer.ExpressionsOnly = false;
        }
    }

    /// <summary>
    /// Emits the preset's shaders as GLSL for the OpenGL pipeline. The warp shader composes the
    /// preset's per-pixel block and the comp shader its own, exactly like the Skia path, so the two
    /// produce the same picture. A shader the dialect cannot express reports failure so the caller
    /// keeps the CPU path.
    /// </summary>
    /// <param name="renderer">Renderer whose preset to translate.</param>
    /// <param name="warp">Receives the warp GLSL, or <see langword="null"/>.</param>
    /// <param name="comp">Receives the comp GLSL, or <see langword="null"/>.</param>
    /// <param name="uniforms">Receives the per-pixel variables both shaders read.</param>
    /// <returns><see langword="true"/> when every shader translated.</returns>
    private static bool TryEmitGlsl(
        PresetRenderer renderer,
        out string? warp,
        out string? comp,
        out IReadOnlyList<string>? uniforms)
    {
        warp = null;
        comp = null;
        uniforms = null;
        var preset = renderer.Preset;
        var names = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            if (preset.WarpShaders.Count > 0)
            {
                var perPixel = preset.PerPixel.IsEmpty ? null : preset.PerPixel;
                warp = ShaderTranspiler.TranspileGlslWarpMesh(preset.WarpShaders[0].Program, perPixel, out _, out var warpUniforms);
                names.UnionWith(warpUniforms);
            }

            if (preset.CompShaders.Count > 0)
            {
                var shader = preset.CompShaders[0];
                var perPixel = shader.PerPixel.IsEmpty ? null : shader.PerPixel;
                comp = ShaderTranspiler.TranspileGlslComp(shader.Program, perPixel, out _, out var compUniforms);
                names.UnionWith(compUniforms);
            }
        }
        catch (PresetExpressionException)
        {
            return false;
        }

        uniforms = [.. names];
        return true;
    }
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
    private string? _renderError;
    private int _renderErrorFrames;
    private string? _presentError;
    private int _presentErrorFrames;
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
            _renderWidth = Math.Clamp(options.Width, 160, 7680);
            _renderHeight = Math.Clamp(options.Height, 90, 4320);

        _frameInterval = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(options.FrameRate, 5, 240));
        // Only the built-ins are available here. Enumerating a real preset collection is a disk
        // walk over thousands of files and must never run while the window is being constructed,
        // because that would block the interface for as long as it takes.
        _presetDirectory = options.PresetDirectory;
        _library.LoadBuiltIns();
        // Roadmap 40f: the GL pipeline and its presentation. It is opt-in while the GL path is
        // proven per platform, and the bitmap presentation stays the fallback.
        _useGlPresenter = Environment.GetEnvironmentVariable("ORYNIVO_VISUALIZER_OPENGL") == "1";
        _renderer = new PresetRenderer(_library.At(_presetIndex), _renderWidth, _renderHeight)
        {
            // The Skia runtime-effect passes are the default: they measured faster than the
            // interpreter at the default resolution and keep the interpreter as the fallback.
            UseSkiaPasses = true
        };
        ConfigureRenderer(_renderer);
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

        if (_useGlPresenter)
        {
            VisualizerImage.IsVisible = false;
            GlPresenter.IsVisible = true;
        }

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

    /// <summary>
    /// Renders one frame and hands it to the UI thread. Any exception here used to end the render
    /// thread, which froze the picture for good and left no trace of why; it is caught, reported,
    /// and the loop continues so the next preset still works.
    /// </summary>
    /// <param name="deltaSeconds">Seconds since the previous frame.</param>
    private void RenderOneFrame(double deltaSeconds)
    {
        try
        {
            RenderOneFrameCore(deltaSeconds);
            _renderError = null;
        }
        catch (Exception exception)
        {
            ReportRenderError(exception);
        }
    }

    /// <summary>Reports a render failure, logging the first one and then at most every thirtieth frame.</summary>
    /// <param name="exception">Failure to report.</param>
    private void ReportRenderError(Exception exception)
    {
        if (_renderError is not null && ++_renderErrorFrames % 30 != 0)
            return;

        _renderErrorFrames = 0;
        _renderError = $"{exception.GetType().Name}: {exception.Message}";
        SeekDiagnostics.Log("visualizer", $"render error preset={_renderer?.Preset.Name} {_renderError}");
    }

    /// <summary>Renders one frame on the render thread and hands it to the UI thread.</summary>
    /// <param name="deltaSeconds">Seconds since the previous frame.</param>
    private void RenderOneFrameCore(double deltaSeconds)
    {
        if (_useGlPresenter && _renderer.ExpressionsOnly && GlPresenter.PipelineFailed)
        {
            // The GPU pipeline failed, so the CPU takes the frame back rather than showing an overlay
            // over nothing.
            _renderer.ExpressionsOnly = false;
            SeekDiagnostics.Log("visualizer", "GL pipeline disabled; the CPU frame path is back");
        }

        if (_renderedPresetIndex != _presetIndex)
        {
            var switchClock = System.Diagnostics.Stopwatch.StartNew();
            _renderedPresetIndex = _presetIndex;
            var preset = _library.At(_presetIndex);
            var loadMs = switchClock.ElapsedMilliseconds;
            _renderer.Dispose();
            _renderer = new PresetRenderer(preset, _renderWidth, _renderHeight);
            ConfigureRenderer(_renderer);
            // The first frames are traced stage by stage so a frozen frame names its own stage, and
            // every frame reports the brightness as it enters each stage. The renderer probes the
            // buffer the stage reads, so the values follow the current frame instead of only the
            // first frame the trace happens to cover; that is what tells a white picture apart from
            // a warp, a comp, or an overlay that went white.
            _renderer.StageLogger = message => SeekDiagnostics.Log("visualizer", message);
            _renderer.StageBrightnessLogger = (stage, value) => _stageBrightness[stage] = value;
            // Parsing and compiling a preset happen here, on the render thread, so a preset that
            // takes seconds to build looks exactly like a frozen window. Log both halves.
            SeekDiagnostics.Log(
                "visualizer",
                $"preset switch index={_presetIndex} loadMs={loadMs} " +
                $"constructMs={switchClock.ElapsedMilliseconds - loadMs} name={preset.Name} " +
                $"shaders=warp{preset.WarpShaders.Count}/comp{preset.CompShaders.Count} " +
                $"failedBlocks={preset.FailedBlocks.Count}");
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

        var frameClock = System.Diagnostics.Stopwatch.StartNew();
        if (ReduceMotion)
            _renderer.RenderOverlayOnly(audio);
        else
            _renderer.RenderFrame(audio, deltaSeconds);
        frameClock.Stop();

        // A frame that takes far too long is the visible freeze, and the last line the log holds is
        // then the preset it happened in. Only slow frames are logged, so this cannot flood.
        if (frameClock.ElapsedMilliseconds >= 250)
        {
            var timings = _renderer.Timings;
            SeekDiagnostics.Log(
                "visualizer",
                $"slow frame frame={_renderer.FrameCount} preset={_renderer.Preset.Name} " +
                $"totalMs={frameClock.ElapsedMilliseconds} warpMs={timings.Warp:F0} blurMs={timings.Blur:F0} " +
                $"postMs={timings.PostProcess:F0} overlayMs={timings.Overlay:F0} " +
                $"compositeMs={timings.Composite:F0} compShaderMs={timings.Shader:F0}");
        }

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
        // start immediately without the two threads ever touching the same pixels. A GL frame also
        // carries the mesh and the pass values, copied under the same lock for the same reason.
        lock (_presentLock)
        {
            if (_renderer.ExpressionsOnly &&
                _renderer.TryCopyMeshMotion(_glMeshSnapshot, out var meshX, out var meshY))
            {
                // The GPU pipeline owns the frame: the CPU hands over the overlay, the mesh, and the
                // per-frame pass values. The overlay buffer is the render size, like the frame copy.
                var overlaySize = _renderWidth * _renderHeight * 4;
                if (_glOverlayBytes.Length != overlaySize)
                    _glOverlayBytes = new byte[overlaySize];
                _renderer.OverlayFrame.WriteBgra(_glOverlayBytes);
                _glMeshX = meshX;
                _glMeshY = meshY;
                _glParameters = _renderer.ReadFrameParameters();
                // The uniforms are filled under the same lock the presenter reads them under, so the
                // GPU pipeline seeds the frame the CPU just computed.
                _renderer.WriteShaderUniforms(_glUniforms, _glPerPixelUniforms);
                _glPipelineActive = true;
            }
            else
            {
                // Either the CPU rendered the whole frame, or it fell back to it because the preset's
                // warp cannot be represented by the mesh; either way the frame copy is the picture.
                _glPipelineActive = false;
                _presentBuffer.CopyFrom(_renderer.Output);
            }
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
    /// The mean brightness the frame had as it entered each stage. It is written by the render thread
    /// through the renderer's stage probe and read by the diagnostics line, so it is concurrent.
    /// </summary>
    private readonly ConcurrentDictionary<string, float> _stageBrightness = new(StringComparer.Ordinal);

    /// <summary>
    /// The mean brightness of the frame the presenter last copied, sampled from the render thread's
    /// presentation buffer before the copy. It is written by the UI thread under
    /// <see cref="_presentLock"/> and read by the diagnostics line.
    /// </summary>
    private float _presentSourceBrightness = -1f;

    /// <summary>
    /// The mean brightness of the bytes the presenter last copied into, sampled after the copy. A
    /// presentation fault shows up as this value differing from <see cref="_presentSourceBrightness"/>.
    /// </summary>
    private float _presentDestinationBrightness = -1f;

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
        // The rendered frame is the post-comp frame the presenter copies. Its brightness and
        // saturated share are the two numbers that tell a genuinely white picture apart from one
        // that never reaches the screen, because a presentation fault leaves them untouched.
        var output = _renderer.Output;
        var brightness = output.MeanBrightness();
        var saturated = output.SaturatedShare();

        var timings = _renderer.AverageTimings;
        var message =
            $"frames={_renderer.FrameCount} audioFrames={VisualizerAudioHub.Shared.AnalyzedFrames} "
            + $"reduceMotion={ReduceMotion} brightness={brightness:F4} "
            + $"saturated={saturated:P1} "
            + $"presentBrightness=source:{_presentSourceBrightness:F4}/destination:{_presentDestinationBrightness:F4} "
            + $"size={_renderWidth}x{_renderHeight} "
            + $"renderMs={timings.Total:F2} warpMs={timings.Warp:F2} blurMs={timings.Blur:F2} "
            + $"postMs={timings.PostProcess:F2} overlayMs={timings.Overlay:F2} "
            + $"compositeMs={timings.Composite:F2} compShaderMs={timings.Shader:F2} "
            + $"shaders=warp{_renderer.Preset.WarpShaders.Count}/comp{_renderer.Preset.CompShaders.Count} "
            + $"gridReduced={_renderer.ShaderGridReduced} "
            + $"perPixelSuspended={_renderer.PerPixelSuspended} "
            + $"stageBrightness={string.Join('/', _stageBrightness.Select(pair => $"{pair.Key}:{pair.Value:F3}"))} "
            + (_renderer.ShaderError is { } shaderError ? $"shaderError=[{shaderError}] " : string.Empty)
            + (_renderError is { } renderError ? $"renderError=[{renderError}] " : string.Empty)
            + (_renderer.PresetError is { } presetError ? $"presetError=[{presetError}] " : string.Empty)
            + (_presentError is { } presentError ? $"presentError=[{presentError}] " : string.Empty)
            + $"preset={_renderer.Preset.Name} userPresets={_library.Count - VisualizerPresets.BuiltIn.Count}";
        // Start a fresh averaging window so the next line describes its own second.
        _renderer.ResetTimings();
        return message;
    }

    /// <summary>
    /// Writes the finished frame into the presented bitmap. A failure here is invisible: the render
    /// thread keeps producing frames and the once-per-second diagnostic line keeps being written, so
    /// the log looks healthy while the screen never changes. It is therefore reported.
    /// </summary>
    private void Present()
    {
        try
        {
            PresentCore();
            _presentError = null;
        }
        catch (Exception exception)
        {
            if (_presentError is not null && ++_presentErrorFrames % 30 != 0)
                return;

            _presentErrorFrames = 0;
            _presentError = $"{exception.GetType().Name}: {exception.Message}";
            SeekDiagnostics.Log("visualizer", $"present failed {_presentError}");
        }
    }

    private void PresentCore()
    {
        if (_useGlPresenter)
        {
            bool pipeline;
            lock (_presentLock)
            {
                pipeline = _glPipelineActive;
                if (!pipeline)
                {
                    var size = _renderWidth * _renderHeight * 4;
                    if (_glBytes.Length != size)
                        _glBytes = new byte[size];
                    // The brightness before and after the copy tells a white GL upload from a white
                    // source frame. The pipeline path has no full-frame CPU buffer, so the CPU half
                    // (the overlay) is what it reports.
                    _presentSourceBrightness = _presentBuffer.MeanBrightness();
                    _presentBuffer.WriteBgra(_glBytes);
                    _presentDestinationBrightness = MeanBrightnessBgra(_glBytes);
                }
                else
                {
                    _presentSourceBrightness = MeanBrightnessBgra(_glOverlayBytes);
                    _presentDestinationBrightness = _presentSourceBrightness;
                    // Published under the lock so the presenter's copy of the uniform dictionary
                    // cannot race the render thread that fills it.
                    GlPresenter.SetPipeline(
                        _glOverlayBytes,
                        _renderWidth,
                        _renderHeight,
                        _glMeshSnapshot,
                        _glMeshX,
                        _glMeshY,
                        _glParameters,
                        _glWarpShader,
                        _glCompShader,
                        _glUniforms);
                }
            }

            if (!pipeline)
                GlPresenter.SetFrame(_glBytes, _renderWidth, _renderHeight);

            GlPresenter.RequestNextFrameRendering();
            if (!_glInfoLogged && GlPresenter.Frames > 0)
            {
                _glInfoLogged = true;
                if (GlPresenter.PipelineDiagnostics is { Length: > 0 } pipelineDiagnostics)
                    SeekDiagnostics.Log("visualizer", pipelineDiagnostics);
                SeekDiagnostics.Log(
                    "visualizer",
                    $"OpenGL presenter active: {GlPresenter.GlInfo} " +
                    $"(pipeline={pipeline}, bitmap presentation disabled)");
            }

            if (GlPresenter.GlError is { Length: > 0 } glError && !_glErrorLogged)
            {
                _glErrorLogged = true;
                SeekDiagnostics.Log("visualizer", $"OpenGL presenter failed: {glError}");
            }

            return;
        }

        using var buffer = _bitmap.Lock();
        var stride = _renderWidth * 4;
        // The render thread keeps working on its own buffers, so this thread reads the finished
        // presentation copy. The lock is held for the copy only, never for a whole frame.
        lock (_presentLock)
        {
            // Sample the render thread's finished frame before the copy and the destination bytes
            // after it, so a copy or bitmap fault shows up as the two numbers disagreeing.
            _presentSourceBrightness = _presentBuffer.MeanBrightness();
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

            _presentDestinationBrightness = MeanBrightnessBgra(buffer.Address, buffer.RowBytes, _renderWidth, _renderHeight);
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

    /// <summary>
    /// Measures the mean RGB brightness of a BGRA byte buffer over a fixed strided sample, so the
    /// presenter can report what it copied without walking every byte of a full-resolution frame.
    /// </summary>
    /// <param name="bytes">BGRA bytes.</param>
    /// <returns>The mean channel value in the range zero to one.</returns>
    private static float MeanBrightnessBgra(ReadOnlySpan<byte> bytes)
    {
        var total = 0L;
        var samples = 0;
        for (var index = 0; index + 2 < bytes.Length; index += 64)
        {
            total += bytes[index] + bytes[index + 1] + bytes[index + 2];
            samples += 3;
        }

        return samples == 0 ? 0f : total / (samples * 255f);
    }

    /// <summary>
    /// Measures the mean RGB brightness of a locked bitmap, respecting a padded row stride so the
    /// padding is not counted.
    /// </summary>
    /// <param name="address">Start of the first row.</param>
    /// <param name="rowBytes">Bytes per row, including padding.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <returns>The mean channel value in the range zero to one.</returns>
    private static float MeanBrightnessBgra(IntPtr address, int rowBytes, int width, int height)
    {
        var total = 0L;
        var samples = 0;
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = new ReadOnlySpan<byte>((void*)(address + (y * rowBytes)), width * 4);
                for (var index = 0; index + 2 < row.Length; index += 64)
                {
                    total += row[index] + row[index + 1] + row[index + 2];
                    samples += 3;
                }
            }
        }

        return samples == 0 ? 0f : total / (samples * 255f);
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



