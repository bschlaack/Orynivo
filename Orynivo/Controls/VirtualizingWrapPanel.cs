using Avalonia;
using Avalonia.Controls;

namespace Orynivo.Controls;

/// <summary>
/// Non-virtualizing wrap panel with fixed item cells, replacing the WPF VirtualizingPanel.
/// Virtualization can be added later if needed.
/// </summary>
public sealed class VirtualizingWrapPanel : Panel
{
    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemWidth), 200d);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemHeight), 255d);

    public double ItemWidth
    {
        get => GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var cellW = ItemWidth;
        var cellH = ItemHeight;
        var availableW = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var perRow = Math.Max(1, (int)Math.Floor(availableW / cellW));

        foreach (var child in Children)
            child.Measure(new Size(cellW, cellH));

        var rows = (int)Math.Ceiling(Children.Count / (double)perRow);
        return new Size(perRow * cellW, rows * cellH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cellW = ItemWidth;
        var cellH = ItemHeight;
        var perRow = Math.Max(1, (int)Math.Floor(finalSize.Width / cellW));

        for (var i = 0; i < Children.Count; i++)
        {
            var row = i / perRow;
            var col = i % perRow;
            Children[i].Arrange(new Rect(col * cellW, row * cellH, cellW, cellH));
        }

        var rows = (int)Math.Ceiling(Children.Count / (double)perRow);
        return new Size(finalSize.Width, rows * cellH);
    }
}
