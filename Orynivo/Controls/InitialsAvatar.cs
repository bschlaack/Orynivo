using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Orynivo.Controls;

/// <summary>
/// Decorative placeholder that renders one or two initials derived from a display
/// name over a deterministic, tastefully tinted diagonal gradient. It is used
/// wherever album or artist artwork is missing so empty slots look intentional
/// instead of showing a flat grey rectangle. The same control is used for local
/// and remote entities so both look equally polished.
/// </summary>
public class InitialsAvatar : TemplatedControl
{
    /// <summary>Defines the <see cref="DisplayName"/> property.</summary>
    public static readonly StyledProperty<string?> DisplayNameProperty =
        AvaloniaProperty.Register<InitialsAvatar, string?>(nameof(DisplayName));

    /// <summary>Defines the <see cref="Initials"/> property.</summary>
    public static readonly StyledProperty<string> InitialsProperty =
        AvaloniaProperty.Register<InitialsAvatar, string>(nameof(Initials), "♪");

    /// <summary>The name (album title or artist name) the initials and tint are derived from.</summary>
    public string? DisplayName
    {
        get => GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    /// <summary>The computed one- or two-letter initials rendered by the control template.</summary>
    public string Initials
    {
        get => GetValue(InitialsProperty);
        private set => SetValue(InitialsProperty, value);
    }

    /// <summary>Recomputes the initials and gradient whenever the display name changes.</summary>
    /// <param name="change">The property-change data raised by the styling system.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DisplayNameProperty)
        {
            Initials = ComputeInitials(DisplayName);
            Background = BuildGradient(DisplayName);
        }
    }

    /// <summary>Extracts up to two upper-case initials from a display name.</summary>
    /// <param name="name">The album title or artist name.</param>
    /// <returns>One or two initials, or a musical note when no letters are available.</returns>
    private static string ComputeInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "♪";

        var words = name.Trim().Split(
            new[] { ' ', '-', '_', '·', '.', '/', '&', ',' },
            StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return "♪";
        if (words.Length == 1)
        {
            var word = words[0];
            return (word.Length >= 2 ? word.Substring(0, 2) : word).ToUpperInvariant();
        }

        return (words[0].Substring(0, 1) + words[^1].Substring(0, 1)).ToUpperInvariant();
    }

    /// <summary>Builds a deterministic diagonal gradient in the app's calm colour range.</summary>
    /// <param name="name">The display name used to seed a stable hue per entity.</param>
    /// <returns>A diagonal <see cref="LinearGradientBrush"/> tinted by the seeded hue.</returns>
    private static IBrush BuildGradient(string? name)
    {
        var hash = 0;
        foreach (var ch in name ?? string.Empty)
            hash = unchecked(hash * 31 + ch);

        var hue = ((hash % 360) + 360) % 360;
        var top = HslToColor(hue, 0.44, 0.44);
        var bottom = HslToColor((hue + 26) % 360, 0.52, 0.30);

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(top, 0),
                new GradientStop(bottom, 1),
            },
        };
    }

    /// <summary>Converts an HSL triple into an opaque <see cref="Color"/>.</summary>
    /// <param name="hue">Hue in degrees (0-360).</param>
    /// <param name="saturation">Saturation as a 0-1 fraction.</param>
    /// <param name="lightness">Lightness as a 0-1 fraction.</param>
    /// <returns>The corresponding opaque colour.</returns>
    private static Color HslToColor(double hue, double saturation, double lightness)
    {
        double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double hp = hue / 60.0;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        double r1 = 0, g1 = 0, b1 = 0;
        if (hp < 1) { r1 = c; g1 = x; }
        else if (hp < 2) { r1 = x; g1 = c; }
        else if (hp < 3) { g1 = c; b1 = x; }
        else if (hp < 4) { g1 = x; b1 = c; }
        else if (hp < 5) { r1 = x; b1 = c; }
        else { r1 = c; b1 = x; }

        double m = lightness - c / 2;
        return Color.FromRgb(
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
    }
}
