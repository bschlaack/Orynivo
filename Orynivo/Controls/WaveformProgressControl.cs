using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Orynivo.Controls;

/// <summary>Draws and edits the transport playback position as a compact waveform.</summary>
internal sealed class WaveformProgressControl : Control
{
    /// <summary>Defines the <see cref="Minimum"/> property.</summary>
    public static readonly DirectProperty<WaveformProgressControl, double> MinimumProperty =
        AvaloniaProperty.RegisterDirect<WaveformProgressControl, double>(
            nameof(Minimum),
            control => control.Minimum,
            (control, value) => control.Minimum = value);

    /// <summary>Defines the <see cref="Maximum"/> property.</summary>
    public static readonly DirectProperty<WaveformProgressControl, double> MaximumProperty =
        AvaloniaProperty.RegisterDirect<WaveformProgressControl, double>(
            nameof(Maximum),
            control => control.Maximum,
            (control, value) => control.Maximum = value);

    /// <summary>Defines the <see cref="Value"/> property.</summary>
    public static readonly DirectProperty<WaveformProgressControl, double> ValueProperty =
        AvaloniaProperty.RegisterDirect<WaveformProgressControl, double>(
            nameof(Value),
            control => control.Value,
            (control, value) => control.Value = value);

    private double _minimum;
    private double _maximum = 1;
    private double _value;
    private IReadOnlyList<float> _waveform = Array.Empty<float>();

    /// <summary>Raised when <see cref="Value"/> changes.</summary>
    internal event EventHandler? ValueChanged;

    /// <summary>Gets or sets the minimum position value in seconds.</summary>
    internal double Minimum
    {
        get => _minimum;
        set
        {
            if (SetAndRaise(MinimumProperty, ref _minimum, value))
            {
                Value = CoerceValue(Value);
                InvalidateVisual();
            }
        }
    }

    /// <summary>Gets or sets the maximum position value in seconds.</summary>
    internal double Maximum
    {
        get => _maximum;
        set
        {
            if (SetAndRaise(MaximumProperty, ref _maximum, Math.Max(Minimum, value)))
            {
                Value = CoerceValue(Value);
                InvalidateVisual();
            }
        }
    }

    /// <summary>Gets or sets the current position value in seconds.</summary>
    internal double Value
    {
        get => _value;
        set
        {
            var coerced = CoerceValue(value);
            if (SetAndRaise(ValueProperty, ref _value, coerced))
            {
                ValueChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            }
        }
    }

    /// <summary>Replaces the rendered waveform amplitudes.</summary>
    /// <param name="samples">Normalized amplitudes in the range 0..1, or <see langword="null"/> to clear the waveform.</param>
    internal void SetWaveform(IReadOnlyList<float>? samples)
    {
        _waveform = samples is { Count: > 0 } ? samples : Array.Empty<float>();
        InvalidateVisual();
    }

    /// <summary>Updates <see cref="Value"/> from a pointer position inside this control.</summary>
    /// <param name="point">Pointer position relative to the control.</param>
    internal void SetValueFromPoint(Point point)
    {
        if (Bounds.Width <= 0)
            return;

        var ratio = Math.Clamp(point.X / Bounds.Width, 0, 1);
        Value = Minimum + ratio * Math.Max(0, Maximum - Minimum);
    }

    /// <summary>Renders the waveform, played overlay, and current playhead.</summary>
    /// <param name="context">Drawing context used for rendering.</param>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 1 || bounds.Height <= 1)
            return;

        context.DrawRectangle(Brushes.Transparent, null, bounds);

        var neutral = GetBrush("AppSeparatorBrush", Color.FromRgb(84, 91, 104));
        var accent = GetBrush("AppTransportAccentBrush", Color.FromRgb(79, 221, 189));
        var disabled = GetBrush("AppGridLineBrush", Color.FromRgb(42, 48, 58));
        var waveformBrush = IsEnabled ? neutral : disabled;
        var playedBrush = IsEnabled ? accent : disabled;
        var centerY = bounds.Height / 2;
        var progress = Maximum <= Minimum ? 0 : Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1);
        var progressX = progress * bounds.Width;

        if (_waveform.Count == 0)
        {
            context.DrawRectangle(waveformBrush, null, new Rect(0, centerY - 1.5, bounds.Width, 3), 1.5);
            if (progressX > 0)
                context.DrawRectangle(playedBrush, null, new Rect(0, centerY - 1.5, progressX, 3), 1.5);
        }
        else
        {
            DrawWaveformBars(context, bounds, waveformBrush, playedBrush, progressX);
        }

        if (IsEnabled)
        {
            context.DrawLine(
                new Pen(playedBrush, 1.5),
                new Point(progressX, Math.Max(1, centerY - 18)),
                new Point(progressX, Math.Min(bounds.Height - 1, centerY + 18)));
        }
    }

    private void DrawWaveformBars(
        DrawingContext context,
        Rect bounds,
        IBrush waveformBrush,
        IBrush playedBrush,
        double progressX)
    {
        const double gap = 1;
        var barWidth = bounds.Width < 500 ? 1 : 2;
        var slotWidth = barWidth + gap;
        var slots = Math.Max(1, (int)(bounds.Width / slotWidth));
        var centerY = bounds.Height / 2;
        var maxHeight = Math.Max(3, bounds.Height - 6);

        for (var slot = 0; slot < slots; slot++)
        {
            var sampleIndex = Math.Min(_waveform.Count - 1, slot * _waveform.Count / slots);
            var amplitude = Math.Clamp(_waveform[sampleIndex], 0.02f, 1f);
            var height = Math.Max(3, amplitude * maxHeight);
            var x = slot * slotWidth;
            var rect = new Rect(x, centerY - height / 2, barWidth, height);
            context.DrawRectangle(x + barWidth <= progressX ? playedBrush : waveformBrush, null, rect, barWidth / 2);
        }
    }

    private double CoerceValue(double value) =>
        Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));

    private static IBrush GetBrush(string resourceKey, Color fallback)
    {
        if (Application.Current?.TryGetResource(resourceKey, null, out var value) == true
            && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}
