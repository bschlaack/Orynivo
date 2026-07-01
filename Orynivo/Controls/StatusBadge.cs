using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Orynivo.Controls;

/// <summary>
/// The visual state a <see cref="StatusBadge"/> conveys through its coloured dot.
/// </summary>
public enum StatusBadgeState
{
    /// <summary>Neutral / disabled state, rendered with a muted grey dot.</summary>
    Off,

    /// <summary>Healthy / available / enabled state, rendered with a green dot.</summary>
    Ok,

    /// <summary>Attention state, rendered with an amber dot.</summary>
    Warning,
}

/// <summary>
/// A small pill-shaped status indicator showing a coloured dot and a short label,
/// used in Settings to surface the availability of subsystems such as FFmpeg,
/// Steinberg ASIO, cwASIO, and the MCP server.
/// </summary>
public class StatusBadge : TemplatedControl
{
    /// <summary>Defines the <see cref="Text"/> property.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StatusBadge, string?>(nameof(Text));

    /// <summary>Defines the <see cref="State"/> property.</summary>
    public static readonly StyledProperty<StatusBadgeState> StateProperty =
        AvaloniaProperty.Register<StatusBadge, StatusBadgeState>(nameof(State));

    /// <summary>Defines the <see cref="DotBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush> DotBrushProperty =
        AvaloniaProperty.Register<StatusBadge, IBrush>(
            nameof(DotBrush),
            new SolidColorBrush(Color.Parse("#8A96A8")));

    /// <summary>The short label shown next to the coloured status dot.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The status conveyed by the badge, driving the dot colour.</summary>
    public StatusBadgeState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The brush used for the status dot; derived from <see cref="State"/>.</summary>
    public IBrush DotBrush
    {
        get => GetValue(DotBrushProperty);
        private set => SetValue(DotBrushProperty, value);
    }

    /// <summary>Recomputes the dot brush whenever the state changes.</summary>
    /// <param name="change">The property-change data raised by the styling system.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
        {
            DotBrush = State switch
            {
                StatusBadgeState.Ok => new SolidColorBrush(Color.Parse("#3FB77E")),
                StatusBadgeState.Warning => new SolidColorBrush(Color.Parse("#E0A83E")),
                _ => new SolidColorBrush(Color.Parse("#8A96A8")),
            };
        }
    }
}
