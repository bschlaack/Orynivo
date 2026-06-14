using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace Orynivo.Controls;

public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private ScrollViewer? _scrollOwner;

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(200d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(255d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private static readonly DependencyProperty ItemIndexProperty =
        DependencyProperty.RegisterAttached("ItemIndex", typeof(int), typeof(VirtualizingWrapPanel),
            new PropertyMetadata(-1));

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var itemCount = owner?.Items.Count ?? 0;
        var width = double.IsInfinity(availableSize.Width) ? ActualWidth : availableSize.Width;
        var itemsPerRow = Math.Max(1, (int)Math.Floor(width / ItemWidth));
        var rowCount = (int)Math.Ceiling(itemCount / (double)itemsPerRow);

        _viewport = availableSize;
        _extent = new Size(width, rowCount * ItemHeight);
        _scrollOwner?.InvalidateScrollInfo();

        var firstVisibleRow = Math.Max(0, (int)Math.Floor(_offset.Y / ItemHeight));
        var visibleRowCount = Math.Max(1, (int)Math.Ceiling(availableSize.Height / ItemHeight) + 1);
        var firstIndex = firstVisibleRow * itemsPerRow;
        var lastIndex = Math.Min(itemCount - 1, ((firstVisibleRow + visibleRowCount) * itemsPerRow) - 1);

        var generator = ItemContainerGenerator;
        if (generator is null || itemCount == 0 || firstIndex > lastIndex)
        {
            CleanUpItems(0, -1);
            return availableSize;
        }

        var start = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = start.Offset == 0 ? start.Index : start.Index + 1;

        using (generator.StartAt(start, GeneratorDirection.Forward, true))
        {
            for (var itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                var newlyRealized = generator.GenerateNext(out var isNewlyRealized) as UIElement;
                if (newlyRealized is null)
                    continue;

                if (isNewlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                        AddInternalChild(newlyRealized);
                    else
                        InsertInternalChild(childIndex, newlyRealized);
                    generator.PrepareItemContainer(newlyRealized);
                }

                newlyRealized.SetValue(ItemIndexProperty, itemIndex);

                newlyRealized.Measure(new Size(ItemWidth, ItemHeight));
            }
        }

        CleanUpItems(firstIndex, lastIndex);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var itemCount = owner?.Items.Count ?? 0;
        var itemsPerRow = Math.Max(1, (int)Math.Floor(finalSize.Width / ItemWidth));

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var itemIndex = (int)child.GetValue(ItemIndexProperty);
            if (itemIndex < 0 || itemIndex >= itemCount)
                continue;

            var row = itemIndex / itemsPerRow;
            var column = itemIndex % itemsPerRow;
            child.Arrange(new Rect(
                column * ItemWidth,
                row * ItemHeight - _offset.Y,
                ItemWidth,
                ItemHeight));
        }

        return finalSize;
    }

    private void CleanUpItems(int minDesiredGenerated, int maxDesiredGenerated)
    {
        var generator = ItemContainerGenerator;
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var child = InternalChildren[i];
            var itemIndex = (int)child.GetValue(ItemIndexProperty);
            if (itemIndex >= minDesiredGenerated && itemIndex <= maxDesiredGenerated)
                continue;

            if (generator is not null)
            {
                var generatorPosition = generator.GeneratorPositionFromIndex(itemIndex);
                if (generatorPosition.Index >= 0)
                    generator.Remove(generatorPosition, 1);
            }
            RemoveInternalChildRange(i, 1);
        }
    }

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; } = true;
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer ScrollOwner
    {
        get => _scrollOwner!;
        set => _scrollOwner = value;
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - ItemHeight);
    public void LineDown() => SetVerticalOffset(VerticalOffset + ItemHeight);
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - ItemHeight);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + ItemHeight);
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageLeft() { }
    public void PageRight() { }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is not UIElement element)
            return rectangle;

        var itemIndex = (int)element.GetValue(ItemIndexProperty);
        if (itemIndex < 0)
            return rectangle;

        var itemsPerRow = Math.Max(1, (int)Math.Floor(ViewportWidth / ItemWidth));
        var itemTop = (itemIndex / itemsPerRow) * ItemHeight;
        var itemBottom = itemTop + ItemHeight;
        if (itemTop < VerticalOffset)
            SetVerticalOffset(itemTop);
        else if (itemBottom > VerticalOffset + ViewportHeight)
            SetVerticalOffset(itemBottom - ViewportHeight);

        return new Rect(0, itemTop - VerticalOffset, ItemWidth, ItemHeight);
    }

    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, Math.Max(0, ExtentHeight - ViewportHeight)));
        if (Math.Abs(offset - _offset.Y) < 0.1)
            return;
        _offset.Y = offset;
        InvalidateMeasure();
        _scrollOwner?.InvalidateScrollInfo();
    }
}
