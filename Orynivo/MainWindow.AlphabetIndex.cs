using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using Orynivo.Audio;
using Orynivo.Controls;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;
using Windows.Media;

namespace Orynivo;

/// <summary>
/// A-Z index construction, active-letter tracking, and scrollbar wiring for
/// <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private void UpdateAlphabetIndex(IEnumerable<ContentRow>? source, bool visible)
    {
        var rows = source?.ToList() ?? [];
        LogUiDiagnostics(
            $"UpdateAlphabetIndex start visible={visible} rows={rows.Count} currentTag={_currentTopLevelTag ?? "<null>"}");
        var firstRows = rows
            .GroupBy(GetAlphabetIndexKey)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        AlphabetIndexPanel.Children.Clear();
        foreach (var label in AlphabetIndexLabels)
        {
            var button = new Button
            {
                Content = label,
                Tag = firstRows.GetValueOrDefault(label),
                IsEnabled = firstRows.ContainsKey(label),
                Theme = FindResource<ControlTheme>("AlphabetIndexButtonTheme")
            };
            button.Click += AlphabetIndexButton_OnClick;
            AlphabetIndexPanel.Children.Add(button);
        }

        var showIndex = visible && rows.Count > 0;
        AlphabetIndexBorder.IsVisible = showIndex;
        var indexMargin = showIndex ? new Thickness(0, 0, 46, 0) : new Thickness(0);
        ContentDataGrid.Margin = indexMargin;
        AlbumArtworkListBox.Margin = indexMargin;
        ArtistArtworkListBox.Margin = indexMargin;
        FolderTreeView.Margin = new Thickness(0);
        LogUiDiagnostics(
            $"UpdateAlphabetIndex finish showIndex={showIndex} enabledLetters={firstRows.Count} currentTag={_currentTopLevelTag ?? "<null>"}");
        Dispatcher.UIThread.Post(UpdateActiveAlphabetButton, DispatcherPriority.Loaded);
    }

    private void UpdatePlexFolderAlphabetIndex()
    {
        var roots = FolderTreeView.Items
            .OfType<TreeViewItem>()
            .Where(item => item.Tag is PlexFolderTag { IsTrack: false })
            .Select(item => new AlphabetTreeTarget(
                GetAlphabetIndexKey(GetTreeItemHeader(item)),
                item))
            .ToList();
        var firstTargets = roots
            .GroupBy(target => target.Key)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        AlphabetIndexPanel.Children.Clear();
        foreach (var label in AlphabetIndexLabels)
        {
            var button = new Button
            {
                Content = label,
                Tag = firstTargets.GetValueOrDefault(label),
                IsEnabled = firstTargets.ContainsKey(label),
                Theme = FindResource<ControlTheme>("AlphabetIndexButtonTheme")
            };
            button.Click += AlphabetIndexButton_OnClick;
            AlphabetIndexPanel.Children.Add(button);
        }

        var showIndex = roots.Count > 0;
        AlphabetIndexBorder.IsVisible = showIndex;
        FolderTreeView.Margin = showIndex
            ? new Thickness(0, 0, 46, 0)
            : new Thickness(0);
        ContentDataGrid.Margin = new Thickness(0);
        AlbumArtworkListBox.Margin = new Thickness(0);
        ArtistArtworkListBox.Margin = new Thickness(0);
        Dispatcher.UIThread.Post(UpdateActiveAlphabetButton, DispatcherPriority.Loaded);
    }

    private static string GetAlphabetIndexKey(ContentRow row) =>
        GetAlphabetIndexKey(row.AlphabetIndexText ?? row.Title);

    private static string GetAlphabetIndexKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "#";

        var trimmed = value.TrimStart();
        if (!Rune.TryGetRuneAt(trimmed, 0, out var first))
            return "#";

        var mapped = first.Value switch
        {
            'ß' or 'ẞ' => "S",
            'Æ' or 'æ' => "A",
            'Ø' or 'ø' => "O",
            _ => first.ToString()
        };

        var normalized = mapped.Normalize(NormalizationForm.FormD);
        foreach (var character in normalized.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            var upper = Rune.ToUpperInvariant(character);
            return upper.Value is >= 'A' and <= 'Z'
                ? ((char)upper.Value).ToString()
                : "#";
        }

        return "#";
    }

    private void AlphabetIndexButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;
        if (button.Tag is ContentRow row)
            ScrollToAlphabetRow(row);
        else if (button.Tag is AlphabetTreeTarget target)
            ScrollToAlphabetTreeTarget(target);
    }

    private void ScrollToAlphabetTreeTarget(AlphabetTreeTarget target)
    {
        _isAlphabetProgrammaticScroll = true;
        SetActiveAlphabetButton(target.Key);
        FolderTreeView.ScrollIntoView(target.Item);
        Dispatcher.UIThread.Post(() =>
        {
            _isAlphabetProgrammaticScroll = false;
        }, DispatcherPriority.Loaded);
    }

    private void ScrollToAlphabetRow(ContentRow row)
    {
        var targetKey = GetAlphabetIndexKey(row);
        _isAlphabetProgrammaticScroll = true;
        SetActiveAlphabetButton(targetKey);

        if (ContentDataGrid.IsVisible)
        {
            // Let DataGrid translate the logical item into its internal pixel offset.
            ContentDataGrid.ScrollIntoView(row, null);
            Dispatcher.UIThread.Post(() =>
            {
                _isAlphabetProgrammaticScroll = false;
            }, DispatcherPriority.Loaded);
        }
        else
        {
            var listBox = AlbumArtworkListBox.IsVisible
                ? AlbumArtworkListBox
                : ArtistArtworkListBox;
            EnsureArtworkRowBound(listBox, row);
            ScrollArtworkRowIntoViewAfterLayout(listBox, row);
        }
    }

    private void AlphabetTarget_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ListBox listBox && (ReferenceEquals(listBox, AlbumArtworkListBox) || ReferenceEquals(listBox, ArtistArtworkListBox)))
        {
            AppendArtworkRowsIfNeeded(listBox);
            QueueHydrateVisibleArtworkRows(listBox);
        }

        if (!AlphabetIndexBorder.IsVisible ||
            _isAlphabetProgrammaticScroll ||
            _alphabetScrollUpdatePending)
            return;

        _alphabetScrollUpdatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _alphabetScrollUpdatePending = false;
            UpdateActiveAlphabetButton();
        }, DispatcherPriority.Background);
    }

    private void ContentDataGrid_OnVerticalScroll(object? sender, Avalonia.Controls.Primitives.ScrollEventArgs e)
    {
        var scrollEventCount = Interlocked.Increment(ref _diagnosticScrollEventCount);
        if (scrollEventCount <= 5 || scrollEventCount % 20 == 0)
        {
            LogUiDiagnostics(
                $"ContentDataGrid_OnVerticalScroll event={scrollEventCount} value={e.NewValue:F2} scrollbarValue={_contentDataGridVerticalScrollBar?.Value.ToString("F2", CultureInfo.InvariantCulture) ?? "<null>"} rowHeight={_contentDataGridAverageRowHeight:F2}");
        }
        UpdateContentDataGridPageSize();
        if (!AlphabetIndexBorder.IsVisible || _isAlphabetProgrammaticScroll || _alphabetScrollUpdatePending)
            return;

        _alphabetScrollUpdatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _alphabetScrollUpdatePending = false;
            UpdateActiveAlphabetButton();
        }, DispatcherPriority.Background);
    }

    private void UpdateActiveAlphabetButton()
    {
        if (!AlphabetIndexBorder.IsVisible)
            return;

        if (FolderTreeView.IsVisible &&
            string.Equals(_activePlexView, "Folders", StringComparison.Ordinal))
        {
            SetActiveAlphabetButton(GetTopVisiblePlexFolderAlphabetKey());
            return;
        }

        var row = GetTopVisibleAlphabetRow();
        var activeKey = row is null ? null : GetAlphabetIndexKey(row);
        SetActiveAlphabetButton(activeKey);
    }

    private void SetActiveAlphabetButton(string? activeKey)
    {
        foreach (var button in AlphabetIndexPanel.Children.OfType<Button>())
        {
            var isActive = string.Equals(button.Content as string, activeKey, StringComparison.Ordinal);
            button.Classes.Set("active", isActive);
        }
    }

    private void AttachContentDataGridVerticalScrollBar()
    {
        // PART_VerticalScrollbar is created by the DataGrid template after layout.
        _contentDataGridVerticalScrollBar = ContentDataGrid
            .GetVisualDescendants()
            .OfType<ScrollBar>()
            .FirstOrDefault(scrollBar =>
                scrollBar.Orientation == Orientation.Vertical &&
                string.Equals(scrollBar.Name, "PART_VerticalScrollbar", StringComparison.Ordinal));
        if (_contentDataGridVerticalScrollBar is not null)
        {
            LogUiDiagnostics(
                $"AttachContentDataGridVerticalScrollBar attached value={_contentDataGridVerticalScrollBar.Value:F2} viewport={_contentDataGridVerticalScrollBar.ViewportSize:F2} max={_contentDataGridVerticalScrollBar.Maximum:F2}");
            UpdateContentDataGridPageSize();
            UpdateActiveAlphabetButton();
        }
        else
        {
            LogUiDiagnostics("AttachContentDataGridVerticalScrollBar no PART_VerticalScrollbar found");
        }
    }

    private void UpdateContentDataGridPageSize()
    {
        if (_contentDataGridVerticalScrollBar is null)
            return;

        // DataGrid scroll values are pixels. One page intentionally overlaps by one
        // row so users retain visual context after clicking the scrollbar track.
        var rowHeight = FindVisualChildren<DataGridRow>(ContentDataGrid)
            .Where(row => row.IsVisible && row.Bounds.Height > 0)
            .Select(row => row.Bounds.Height)
            .FirstOrDefault();
        if (rowHeight > 0 && double.IsFinite(rowHeight))
            _contentDataGridAverageRowHeight = rowHeight;
        var pageSize = _contentDataGridVerticalScrollBar.ViewportSize - rowHeight;
        if (pageSize > 0 && double.IsFinite(pageSize))
            _contentDataGridVerticalScrollBar.LargeChange = pageSize;
    }

    private ContentRow? GetTopVisibleAlphabetRow()
    {
        Control? targetControl = ContentDataGrid.IsVisible
            ? ContentDataGrid
            : AlbumArtworkListBox.IsVisible
                ? AlbumArtworkListBox
                : ArtistArtworkListBox.IsVisible
                    ? ArtistArtworkListBox
                    : null;
        if (targetControl is null)
            return null;

        if (targetControl is DataGrid &&
            _contentDataGridVerticalScrollBar is not null &&
            ContentDataGrid.ItemsSource is System.Collections.IList items &&
            items.Count > 0)
        {
            var rowHeight = _contentDataGridAverageRowHeight > 0
                ? _contentDataGridAverageRowHeight
                : 32d;
            var topIndex = (int)Math.Floor(_contentDataGridVerticalScrollBar.Value / rowHeight);
            topIndex = Math.Clamp(topIndex, 0, items.Count - 1);
            return items[topIndex] as ContentRow;
        }

        Control? bestContainer = null;
        var bestTop = double.PositiveInfinity;
        var visibleTop = 0d;
        if (targetControl is DataGrid &&
            FindVisualChild<Avalonia.Controls.Primitives.DataGridColumnHeadersPresenter>(targetControl) is { } headers)
        {
            visibleTop = (headers.TranslatePoint(new Point(0, headers.Bounds.Height), targetControl)?.Y ?? 0);
        }
        var candidates = targetControl is DataGrid
            ? FindVisualChildren<DataGridRow>(targetControl).Cast<Control>()
            : FindVisualChildren<ListBoxItem>(targetControl).Cast<Control>();
        foreach (var container in candidates)
        {
            if (!container.IsVisible || container.DataContext is not ContentRow)
                continue;

            var topLeft = container.TranslatePoint(new Point(0, 0), targetControl) ?? new Point(0, 0);
            var bounds = new Rect(topLeft.X, topLeft.Y, container.Bounds.Width, container.Bounds.Height);
            if (bounds.Bottom <= visibleTop || bounds.Top >= targetControl.Bounds.Height)
                continue;
            if (bounds.Top < bestTop)
            {
                bestTop = bounds.Top;
                bestContainer = container;
            }
        }

        return bestContainer?.DataContext as ContentRow;
    }

    private string? GetTopVisiblePlexFolderAlphabetKey()
    {
        TreeViewItem? bestItem = null;
        var bestTop = double.PositiveInfinity;
        foreach (var item in FolderTreeView.Items.OfType<TreeViewItem>())
        {
            if (!item.IsVisible || item.Tag is not PlexFolderTag { IsTrack: false })
                continue;
            var top = item.TranslatePoint(new Point(0, 0), FolderTreeView)?.Y;
            if (top is null || top + item.Bounds.Height <= 0 || top >= FolderTreeView.Bounds.Height)
                continue;
            if (top < bestTop)
            {
                bestTop = top.Value;
                bestItem = item;
            }
        }

        return bestItem is null
            ? null
            : GetAlphabetIndexKey(GetTreeItemHeader(bestItem));
    }

    private static string? GetTreeItemHeader(TreeViewItem item) =>
        item.Header switch
        {
            string text => text,
            TextBlock textBlock => textBlock.Text,
            _ => item.Header?.ToString()
        };

    private void AlphabetIndexBorder_OnPreviewMouseLeftButtonDown(object sender, PointerPressedEventArgs e)
    {
        _isDraggingAlphabetIndex = true;
        e.Pointer.Capture(AlphabetIndexBorder);
        ScrollToAlphabetPosition(e.GetPosition(AlphabetIndexPanel));
        e.Handled = true;
    }

    private void AlphabetIndexBorder_OnPreviewMouseMove(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingAlphabetIndex || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;
        ScrollToAlphabetPosition(e.GetPosition(AlphabetIndexPanel));
        e.Handled = true;
    }

    private void AlphabetIndexBorder_OnPreviewMouseLeftButtonUp(object sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingAlphabetIndex)
            return;
        ScrollToAlphabetPosition(e.GetPosition(AlphabetIndexPanel));
        _isDraggingAlphabetIndex = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ScrollToAlphabetPosition(Point point)
    {
        if (AlphabetIndexPanel.Children.Count == 0 || AlphabetIndexPanel.Bounds.Height <= 0)
            return;

        var button = AlphabetIndexPanel.Children
            .OfType<Button>()
            .OrderBy(candidate =>
            {
                var top = candidate.TranslatePoint(new Point(0, 0), AlphabetIndexPanel)?.Y ?? 0;
                return Math.Abs(point.Y - (top + candidate.Bounds.Height / 2));
            })
            .FirstOrDefault();

        if (button is not { IsEnabled: true })
            return;
        if (button.Tag is ContentRow row)
            ScrollToAlphabetRow(row);
        else if (button.Tag is AlphabetTreeTarget target)
            ScrollToAlphabetTreeTarget(target);
    }
}
