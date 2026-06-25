using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Orynivo.Audio;

namespace Orynivo.Controls;

/// <summary>Draws the combined frequency response of a parametric equalizer profile.</summary>
internal sealed class EqualizerResponseControl : Control
{
    private const double MinimumFrequency = 20;
    private const double MaximumFrequency = 20000;
    private const double MinimumGain = -18;
    private const double MaximumGain = 18;
    private const double FrequencyScaleHeight = 24;
    private const double MarkerLabelAreaHeight = 24;
    private const int DisplaySampleRate = 48000;
    private EqualizerProfile? _profile;

    /// <summary>Sets the profile displayed by the response graph.</summary>
    /// <param name="profile">Profile to display, or <see langword="null"/> for a flat response.</param>
    internal void SetProfile(EqualizerProfile? profile)
    {
        _profile = profile?.Clone();
        InvalidateVisual();
    }

    /// <summary>Renders the graph grid and combined equalizer response.</summary>
    /// <param name="context">Drawing context used for rendering.</param>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 1 ||
            bounds.Height <= FrequencyScaleHeight + MarkerLabelAreaHeight + 1)
            return;
        var graphBounds = new Rect(
            0,
            0,
            bounds.Width,
            bounds.Height - FrequencyScaleHeight - MarkerLabelAreaHeight);

        var background = GetBrush("AppInputBrush", Color.FromRgb(28, 30, 38));
        var border = GetBrush("AppInputBorderBrush", Color.FromRgb(75, 78, 92));
        var grid = GetBrush("AppSeparatorBrush", Color.FromRgb(56, 59, 70));
        var accent = new SolidColorBrush(Color.FromRgb(108, 99, 255));
        context.DrawRectangle(background, new Pen(border, 1), graphBounds, 8);

        foreach (var frequency in new[] { 20d, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 })
        {
            var x = FrequencyToX(frequency, graphBounds.Width);
            context.DrawLine(
                new Pen(grid, frequency is 100 or 1000 or 10000 ? 1 : 0.5),
                new Point(x, 0),
                new Point(x, graphBounds.Height));
        }

        foreach (var gain in new[] { -12d, -6, 0, 6, 12 })
        {
            var y = GainToY(gain, graphBounds.Height);
            context.DrawLine(
                new Pen(gain == 0 ? border : grid, gain == 0 ? 1.2 : 0.5),
                new Point(0, y),
                new Point(graphBounds.Width, y));
        }

        DrawFilterMarkers(context, graphBounds, bounds, accent);

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            for (var pixel = 0; pixel < Math.Max(2, (int)graphBounds.Width); pixel++)
            {
                var frequency = XToFrequency(pixel, graphBounds.Width);
                var gain = CalculateGainDb(_profile, frequency);
                var point = new Point(pixel, GainToY(gain, graphBounds.Height));
                if (pixel == 0)
                    stream.BeginFigure(point, false);
                else
                    stream.LineTo(point);
            }
        }

        context.DrawGeometry(null, new Pen(accent, 2), geometry);
        DrawFrequencyScale(context, graphBounds, border);
    }

    /// <summary>Draws the logarithmic frequency labels below the response graph.</summary>
    /// <param name="context">Drawing context used for rendering.</param>
    /// <param name="graphBounds">Bounds of the response plotting area.</param>
    /// <param name="border">Brush used for scale ticks.</param>
    private static void DrawFrequencyScale(DrawingContext context, Rect graphBounds, IBrush border)
    {
        var foreground = GetBrush("AppMutedTextBrush", Color.FromRgb(150, 153, 170));
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);
        var labels = new (double Frequency, string Label)[]
        {
            (20, "20 Hz"),
            (50, "50"),
            (100, "100"),
            (200, "200"),
            (500, "500"),
            (1000, "1 kHz"),
            (2000, "2 k"),
            (5000, "5 k"),
            (10000, "10 k"),
            (20000, "20 kHz")
        };

        foreach (var (frequency, label) in labels)
        {
            var x = FrequencyToX(frequency, graphBounds.Width);
            context.DrawLine(
                new Pen(border, 1),
                new Point(x, graphBounds.Bottom),
                new Point(x, graphBounds.Bottom + 4));
            var text = new FormattedText(
                label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                9,
                foreground);
            var textX = Math.Clamp(x - text.Width / 2, 0, graphBounds.Width - text.Width);
            context.DrawText(text, new Point(textX, graphBounds.Bottom + 5));
        }
    }

    /// <summary>Draws numbered vertical markers for the frequencies of all profile filters.</summary>
    /// <param name="context">Drawing context used for rendering.</param>
    /// <param name="graphBounds">Available response-plot bounds.</param>
    /// <param name="controlBounds">Complete control bounds including scale and marker labels.</param>
    /// <param name="accent">Accent brush used for marker lines and labels.</param>
    private void DrawFilterMarkers(
        DrawingContext context,
        Rect graphBounds,
        Rect controlBounds,
        IBrush accent)
    {
        if (_profile is null || _profile.Filters.Count == 0)
            return;

        const double labelRadius = 10;
        const double labelBottomMargin = 4;
        const double minimumLabelDistance = labelRadius * 2 + 3;
        var occupiedLabelPositions = new List<double>();
        var markerPen = new Pen(
            accent,
            1,
            DashStyle.Dash,
            PenLineCap.Flat,
            PenLineJoin.Miter,
            10);
        var labelForeground = new SolidColorBrush(Colors.White);
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

        for (var index = 0; index < _profile.Filters.Count; index++)
        {
            var frequency = _profile.Filters[index].Frequency;
            if (frequency < MinimumFrequency || frequency > MaximumFrequency)
                continue;

            var x = FrequencyToX(frequency, graphBounds.Width);
            var labelX = FindMarkerLabelX(
                occupiedLabelPositions,
                x,
                labelRadius,
                minimumLabelDistance,
                graphBounds.Width);
            occupiedLabelPositions.Add(labelX);
            var labelY = controlBounds.Height - labelBottomMargin - labelRadius;
            var lineBottom = labelY - labelRadius - 2;
            context.DrawLine(markerPen, new Point(x, 2), new Point(x, lineBottom));
            if (Math.Abs(labelX - x) > 0.5)
            {
                context.DrawLine(
                    markerPen,
                    new Point(x, lineBottom),
                    new Point(labelX, labelY - labelRadius));
            }
            context.DrawEllipse(accent, null, new Point(labelX, labelY), labelRadius, labelRadius);

            var text = new FormattedText(
                (index + 1).ToString(CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                index >= 99 ? 8 : 9,
                labelForeground);
            context.DrawText(text, new Point(labelX - text.Width / 2, labelY - text.Height / 2));
        }
    }

    /// <summary>Finds a bottom-aligned horizontal label position without overlapping earlier markers.</summary>
    /// <param name="occupiedPositions">Horizontal positions already used by marker labels.</param>
    /// <param name="preferredX">Preferred position matching the exact filter frequency.</param>
    /// <param name="labelRadius">Radius of one numbered marker label.</param>
    /// <param name="minimumDistance">Required distance between label centers.</param>
    /// <param name="availableWidth">Available graph width.</param>
    /// <returns>The selected horizontal label position.</returns>
    private static double FindMarkerLabelX(
        IReadOnlyList<double> occupiedPositions,
        double preferredX,
        double labelRadius,
        double minimumDistance,
        double availableWidth)
    {
        var maximumOffsetSteps = Math.Max(1, (int)Math.Ceiling(availableWidth / minimumDistance));
        for (var step = 0; step <= maximumOffsetSteps; step++)
        {
            foreach (var direction in step == 0 ? new[] { 0 } : new[] { 1, -1 })
            {
                var candidate = Math.Clamp(
                    preferredX + direction * step * minimumDistance,
                    labelRadius,
                    availableWidth - labelRadius);
                if (occupiedPositions.All(position =>
                    Math.Abs(position - candidate) >= minimumDistance))
                {
                    return candidate;
                }
            }
        }

        return Math.Clamp(preferredX, labelRadius, availableWidth - labelRadius);
    }

    private static double CalculateGainDb(EqualizerProfile? profile, double frequency)
    {
        if (profile is null)
            return 0;

        var magnitude = Math.Pow(10, profile.PreampDb / 20);
        foreach (var filter in profile.Filters)
        {
            if (filter.Frequency <= 0 || filter.Frequency >= DisplaySampleRate / 2d)
                continue;
            magnitude *= CalculateFilterMagnitude(filter, frequency);
        }

        return Math.Clamp(20 * Math.Log10(Math.Max(magnitude, 1e-12)), MinimumGain, MaximumGain);
    }

    private static double CalculateFilterMagnitude(EqualizerFilter filter, double frequency)
    {
        var centerFrequency = Math.Clamp(filter.Frequency, 1, DisplaySampleRate * 0.499);
        var q = Math.Clamp(filter.Q, 0.05, 100);
        var omega = 2 * Math.PI * centerFrequency / DisplaySampleRate;
        var cosine = Math.Cos(omega);
        var sine = Math.Sin(omega);
        var alpha = sine / (2 * q);
        var amplitude = Math.Pow(10, filter.GainDb / 40);
        var coefficients = filter.Type switch
        {
            EqualizerFilterType.Peak => (
                B0: 1 + alpha * amplitude, B1: -2 * cosine, B2: 1 - alpha * amplitude,
                A0: 1 + alpha / amplitude, A1: -2 * cosine, A2: 1 - alpha / amplitude),
            EqualizerFilterType.LowShelf => CreateLowShelf(amplitude, cosine, sine, Math.Min(q, 1)),
            EqualizerFilterType.HighShelf => CreateHighShelf(amplitude, cosine, sine, Math.Min(q, 1)),
            EqualizerFilterType.LowPass => (
                B0: (1 - cosine) / 2, B1: 1 - cosine, B2: (1 - cosine) / 2,
                A0: 1 + alpha, A1: -2 * cosine, A2: 1 - alpha),
            EqualizerFilterType.HighPass => (
                B0: (1 + cosine) / 2, B1: -(1 + cosine), B2: (1 + cosine) / 2,
                A0: 1 + alpha, A1: -2 * cosine, A2: 1 - alpha),
            _ => (B0: 1d, B1: 0d, B2: 0d, A0: 1d, A1: 0d, A2: 0d)
        };

        var evaluationOmega = 2 * Math.PI * Math.Clamp(frequency, 1, DisplaySampleRate * 0.499)
            / DisplaySampleRate;
        var z1 = System.Numerics.Complex.FromPolarCoordinates(1, -evaluationOmega);
        var z2 = z1 * z1;
        var numerator = coefficients.B0 + coefficients.B1 * z1 + coefficients.B2 * z2;
        var denominator = coefficients.A0 + coefficients.A1 * z1 + coefficients.A2 * z2;
        return denominator.Magnitude <= 1e-12 ? 1 : (numerator / denominator).Magnitude;
    }

    private static (double B0, double B1, double B2, double A0, double A1, double A2)
        CreateLowShelf(double amplitude, double cosine, double sine, double q)
    {
        var alpha = sine / 2 * Math.Sqrt((amplitude + 1 / amplitude) * (1 / q - 1) + 2);
        var root = 2 * Math.Sqrt(amplitude) * alpha;
        return (
            amplitude * ((amplitude + 1) - (amplitude - 1) * cosine + root),
            2 * amplitude * ((amplitude - 1) - (amplitude + 1) * cosine),
            amplitude * ((amplitude + 1) - (amplitude - 1) * cosine - root),
            (amplitude + 1) + (amplitude - 1) * cosine + root,
            -2 * ((amplitude - 1) + (amplitude + 1) * cosine),
            (amplitude + 1) + (amplitude - 1) * cosine - root);
    }

    private static (double B0, double B1, double B2, double A0, double A1, double A2)
        CreateHighShelf(double amplitude, double cosine, double sine, double q)
    {
        var alpha = sine / 2 * Math.Sqrt((amplitude + 1 / amplitude) * (1 / q - 1) + 2);
        var root = 2 * Math.Sqrt(amplitude) * alpha;
        return (
            amplitude * ((amplitude + 1) + (amplitude - 1) * cosine + root),
            -2 * amplitude * ((amplitude - 1) + (amplitude + 1) * cosine),
            amplitude * ((amplitude + 1) + (amplitude - 1) * cosine - root),
            (amplitude + 1) - (amplitude - 1) * cosine + root,
            2 * ((amplitude - 1) - (amplitude + 1) * cosine),
            (amplitude + 1) - (amplitude - 1) * cosine - root);
    }

    private static double FrequencyToX(double frequency, double width) =>
        Math.Log10(frequency / MinimumFrequency) / Math.Log10(MaximumFrequency / MinimumFrequency) * width;

    private static double XToFrequency(double x, double width) =>
        MinimumFrequency * Math.Pow(MaximumFrequency / MinimumFrequency, x / Math.Max(1, width));

    private static double GainToY(double gain, double height) =>
        (MaximumGain - Math.Clamp(gain, MinimumGain, MaximumGain))
        / (MaximumGain - MinimumGain) * height;

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
