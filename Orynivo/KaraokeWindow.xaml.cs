using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>
/// Fullscreen karaoke view for the current track's synchronized lyrics. The
/// active line is centered and emphasized while neighbouring lines fade out;
/// position updates are pushed in by the main window's transport timer.
/// </summary>
public partial class KaraokeWindow : Window
{
    /// <summary>One synchronized lyric line shown by the karaoke view.</summary>
    /// <param name="Text">Display text.</param>
    /// <param name="Time">Line timestamp, or <see langword="null"/> for an untimed line.</param>
    public sealed record KaraokeLine(string Text, TimeSpan? Time);

    private const int VisibleLines = 7;
    private static readonly IBrush ActiveBrush = Brushes.White;
    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.Parse("#B8B8D0"));

    private readonly IReadOnlyList<KaraokeLine> _lines;
    private readonly List<TextBlock> _slots = [];
    private int _activeIndex = -2;

    /// <summary>Initializes a runtime-loader instance without lyrics.</summary>
    public KaraokeWindow()
        : this([], null)
    {
    }

    /// <summary>Initializes the karaoke view for one track.</summary>
    /// <param name="lines">Synchronized lyric lines ordered by timestamp.</param>
    /// <param name="artwork">Optional now-playing artwork used as a dimmed backdrop.</param>
    public KaraokeWindow(IReadOnlyList<KaraokeLine> lines, IImage? artwork)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _lines = lines;
        InitializeComponent();
        BackgroundImage.Source = artwork;
        HintTextBlock.Text = LocalizationManager.Current.KaraokeExitHint;
        BuildSlots();
        RenderLines();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
        PointerPressed += (_, _) => Close();
    }

    /// <summary>Sets the track title shown below the lyrics.</summary>
    /// <param name="title">Now-playing title.</param>
    /// <param name="artist">Now-playing artist, or <see langword="null"/>.</param>
    public void SetTrack(string? title, string? artist)
    {
        TrackTextBlock.Text = string.IsNullOrWhiteSpace(artist)
            ? title ?? string.Empty
            : $"{title} — {artist}";
    }

    /// <summary>Moves the karaoke highlight to the line for the current position.</summary>
    /// <param name="position">Current playback position.</param>
    public void UpdatePosition(TimeSpan position)
    {
        if (_lines.Count == 0)
            return;
        var index = LyricLineSelector.FindActiveIndex(_lines, line => line.Time, position);
        if (index == _activeIndex)
            return;
        _activeIndex = index;
        RenderLines();
    }

    private void BuildSlots()
    {
        var transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(260),
                Easing = new CubicEaseOut()
            },
            new DoubleTransition
            {
                Property = FontSizeProperty,
                Duration = TimeSpan.FromMilliseconds(260),
                Easing = new CubicEaseOut()
            }
        };
        for (var slot = 0; slot < VisibleLines; slot++)
        {
            var block = new TextBlock
            {
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                FontSize = 22,
                FontWeight = FontWeight.SemiBold,
                Foreground = InactiveBrush,
                Opacity = 0,
                Transitions = transitions
            };
            _slots.Add(block);
            LinesPanel.Children.Add(block);
        }
    }

    private void RenderLines()
    {
        var firstLine = _activeIndex < 0 ? 0 : _activeIndex - VisibleLines / 2;
        for (var slot = 0; slot < _slots.Count; slot++)
        {
            var block = _slots[slot];
            var lineIndex = firstLine + slot;
            if (lineIndex < 0 || lineIndex >= _lines.Count)
            {
                block.Text = string.Empty;
                block.Opacity = 0;
                continue;
            }

            var distance = _activeIndex < 0 ? int.MaxValue : Math.Abs(lineIndex - _activeIndex);
            block.Text = _lines[lineIndex].Text;
            block.Foreground = distance == 0 ? ActiveBrush : InactiveBrush;
            block.FontWeight = distance == 0 ? FontWeight.Bold : FontWeight.SemiBold;
            block.Opacity = distance switch
            {
                0 => 1d,
                1 => 0.62d,
                2 => 0.34d,
                _ => 0.18d
            };
            block.FontSize = distance switch
            {
                0 => 46d,
                1 => 32d,
                2 => 26d,
                _ => 22d
            };
        }
    }
}
