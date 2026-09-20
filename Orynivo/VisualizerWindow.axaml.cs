using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
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
    private const int RenderWidth = 480;
    private const int RenderHeight = 270;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan ReducedMotionInterval = TimeSpan.FromSeconds(1);

    private readonly DispatcherTimer _timer;
    private readonly WriteableBitmap _bitmap;
    private readonly VisualizerPresetLibrary _library = new();
    private PresetRenderer _renderer;
    private int _presetIndex;
    private DateTimeOffset _lastFrame = DateTimeOffset.UtcNow;

    /// <summary>Initializes the window for the Avalonia designer.</summary>
    public VisualizerWindow()
        : this(0, reduceMotion: false)
    {
    }

    /// <summary>Creates the visualizer window.</summary>
    /// <param name="presetIndex">Index of the preset to start with.</param>
    /// <param name="reduceMotion">
    /// When <see langword="true"/> the picture shows a static spectrum instead of animating.
    /// </param>
    /// <param name="presetDirectory">
    /// Folder to load user presets from, or <see langword="null"/> for the default folder.
    /// </param>
    public VisualizerWindow(int presetIndex, bool reduceMotion, string? presetDirectory = null)
    {
        _presetIndex = presetIndex;
        _library.Reload(presetDirectory);
        _renderer = new PresetRenderer(_library.At(_presetIndex), RenderWidth, RenderHeight);
        _bitmap = new WriteableBitmap(
            new Avalonia.PixelSize(RenderWidth, RenderHeight),
            new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        ReduceMotion = reduceMotion;

        InitializeComponent();
        VisualizerImage.Source = _bitmap;
        HintTextBlock.Text = LocalizationManager.Current.VisualizerHint;
        UpdatePresetLabel();

        _timer = new DispatcherTimer
        {
            Interval = reduceMotion ? ReducedMotionInterval : FrameInterval
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
        PointerPressed += (_, _) => SelectPreset(_presetIndex + 1);
    }

    /// <summary>
    /// Gets a value indicating whether the reduce-motion preference is active, which renders
    /// a static spectrum at a low frame rate instead of animating.
    /// </summary>
    public bool ReduceMotion { get; }

    /// <summary>Gets the index of the preset currently being rendered.</summary>
    public int PresetIndex => _presetIndex;

    /// <summary>Selects a preset by index, wrapping around the available presets.</summary>
    /// <param name="index">Requested preset index.</param>
    public void SelectPreset(int index)
    {
        var count = _library.Presets.Count;
        _presetIndex = ((index % count) + count) % count;
        var preset = _library.At(_presetIndex);
        _renderer = new PresetRenderer(preset, RenderWidth, RenderHeight);
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

        if (!VisualizerAudioHub.Shared.TryAnalyze(out var audio) || audio is null)
            return;

        if (ReduceMotion)
            _renderer.RenderOverlayOnly(audio);
        else
            _renderer.RenderFrame(audio, delta);

        Present();
    }

    private void Present()
    {
        using var buffer = _bitmap.Lock();
        var stride = RenderWidth * 4;
        if (buffer.RowBytes == stride)
        {
            unsafe
            {
                _renderer.Output.WriteBgra(new Span<byte>((void*)buffer.Address, stride * RenderHeight));
            }
        }
        else
        {
            // Padded rows: copy one row at a time so the padding stays untouched.
            var row = new byte[stride];
            for (var y = 0; y < RenderHeight; y++)
            {
                _renderer.Output.WriteRowBgra(y, row);
                System.Runtime.InteropServices.Marshal.Copy(
                    row,
                    0,
                    buffer.Address + (y * buffer.RowBytes),
                    stride);
            }
        }
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
