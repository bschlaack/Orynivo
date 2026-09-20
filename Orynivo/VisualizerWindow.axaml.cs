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
    private DateTimeOffset _lastPointerActivity = DateTimeOffset.MinValue;

    private readonly DispatcherTimer _timer;
    private readonly WriteableBitmap _bitmap;
    private readonly VisualizerPresetLibrary _library = new();
    private readonly SilentAudioSource _silent = new();
    private PresetRenderer _renderer;
    private int _presetIndex;
    private VisualizerTransport? _transport;
    private bool _isPlaying = true;
    private DateTimeOffset _lastFrame = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastDiagnostics = DateTimeOffset.UtcNow;

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
        _library.Reload(options.PresetDirectory);
        _renderer = new PresetRenderer(_library.At(_presetIndex), _renderWidth, _renderHeight);
        _bitmap = new WriteableBitmap(
            new Avalonia.PixelSize(_renderWidth, _renderHeight),
            new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        ReduceMotion = options.ReduceMotion;
        _alwaysShowOverlay = options.AlwaysShowOverlay;

        InitializeComponent();
        VisualizerImage.Source = _bitmap;
        HintTextBlock.Text = LocalizationManager.Current.VisualizerHint;
        UpdatePresetLabel();

        _timer = new DispatcherTimer
        {
            Interval = options.ReduceMotion ? ReducedMotionInterval : _frameInterval
        };
        _timer.Tick += (_, _) => RenderOnce();
        Opened += (_, _) =>
        {
            VisualizerAudioHub.Shared.Clear();
            VisualizerAudioHub.Shared.IsActive = true;
            _lastFrame = DateTimeOffset.UtcNow;
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
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

    /// <summary>Selects a preset by index, wrapping around the available presets.</summary>
    /// <param name="index">Requested preset index.</param>
    public void SelectPreset(int index)
    {
        var count = _library.Presets.Count;
        _presetIndex = ((index % count) + count) % count;
        var preset = _library.At(_presetIndex);
        _renderer = new PresetRenderer(preset, _renderWidth, _renderHeight);
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
                _renderer.Reset();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void RenderOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var delta = (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        // Render even when nothing is playing, so the window never stays black.
        if (!VisualizerAudioHub.Shared.TryAnalyze(out var audio) || audio is null)
            audio = _silent;

        if (ReduceMotion)
            _renderer.RenderOverlayOnly(audio);
        else
            _renderer.RenderFrame(audio, delta);

        Present();
        UpdatePlayPauseIcon();
        UpdateOverlayVisibility();
        if (_transport is { } transport)
        {
            var nowPlaying = transport.NowPlaying();
            SetTrack(nowPlaying.Title, nowPlaying.Artist);
        }
        LogDiagnostics();
    }

    /// <summary>
    /// Writes one bounded diagnostic line per second so an empty window can be told apart
    /// from a picture that never reaches the screen. Only counts and a brightness average
    /// are recorded, never media names or paths.
    /// </summary>
    private void LogDiagnostics()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastDiagnostics < TimeSpan.FromSeconds(1))
            return;

        _lastDiagnostics = now;
        var pixels = _renderer.Output.Pixels;
        var total = 0f;
        var samples = 0;
        for (var index = 0; index < pixels.Length; index += 64)
        {
            total += pixels[index] + pixels[index + 1] + pixels[index + 2];
            samples += 3;
        }

        SeekDiagnostics.Log(
            "visualizer",
            $"frames={_renderer.FrameCount} audioFrames={VisualizerAudioHub.Shared.AnalyzedFrames} "
            + $"reduceMotion={ReduceMotion} brightness={(samples == 0 ? 0f : total / samples):F4} "
            + $"preset={_renderer.Preset.Name} userPresets={_library.Presets.Count - VisualizerPresets.BuiltIn.Count}");
    }

    private void Present()
    {
        using var buffer = _bitmap.Lock();
        var stride = _renderWidth * 4;
        if (buffer.RowBytes == stride)
        {
            unsafe
            {
                _renderer.Output.WriteBgra(new Span<byte>((void*)buffer.Address, stride * _renderHeight));
            }
        }
        else
        {
            // Padded rows: copy one row at a time so the padding stays untouched.
            var row = new byte[stride];
            for (var y = 0; y < _renderHeight; y++)
            {
                _renderer.Output.WriteRowBgra(y, row);
                System.Runtime.InteropServices.Marshal.Copy(
                    row,
                    0,
                    buffer.Address + (y * buffer.RowBytes),
                    stride);
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
