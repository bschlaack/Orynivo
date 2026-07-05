using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Orynivo.Localization;

namespace Orynivo.Controls;

/// <summary>
/// Adds a right-click column chooser flyout to selectable <see cref="DataGrid"/> columns.
/// Selectable columns use a stable string in <see cref="DataGridColumn.Tag"/>.
/// </summary>
internal static class DataGridColumnChooser
{
    /// <summary>
    /// Attaches the chooser to a grid and restores its persisted visibility selection.
    /// </summary>
    /// <param name="grid">Grid receiving the column chooser.</param>
    /// <param name="tableKey">Stable settings key for the table context.</param>
    /// <param name="settings">Application settings containing visible-column selections.</param>
    internal static void Attach(DataGrid grid, string tableKey, AppSettings settings)
        => Attach(grid, () => tableKey, settings);

    /// <summary>
    /// Attaches the chooser to a grid whose table key changes with the active view.
    /// </summary>
    /// <param name="grid">Grid receiving the column chooser.</param>
    /// <param name="tableKeyProvider">Function returning the current stable table key.</param>
    /// <param name="settings">Application settings containing visible-column selections.</param>
    internal static void Attach(
        DataGrid grid,
        Func<string> tableKeyProvider,
        AppSettings settings)
    {
        grid.CanUserReorderColumns = true;
        Apply(grid, tableKeyProvider(), settings);
        grid.ColumnDisplayIndexChanged += (_, _) =>
            DataGridColumnOrderStore.Capture(
                settings.DataGridColumnOrders,
                tableKeyProvider(),
                grid);
        grid.AddHandler(
            InputElement.PointerPressedEvent,
            (_, args) => OnPointerPressed(grid, tableKeyProvider(), settings, args),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    /// <summary>
    /// Applies a persisted selection after a dynamic column collection has been rebuilt.
    /// </summary>
    /// <param name="grid">Grid whose columns are updated.</param>
    /// <param name="tableKey">Stable settings key for the table context.</param>
    /// <param name="settings">Application settings containing visible-column selections.</param>
    internal static void Apply(DataGrid grid, string tableKey, AppSettings settings)
    {
        if (settings.VisibleDataGridColumns.TryGetValue(tableKey, out var visibleKeys))
        {
            var visible = visibleKeys.ToHashSet(StringComparer.Ordinal);
            foreach (var column in grid.Columns.Where(column => column.Tag is string))
            {
                var tag = (string)column.Tag!;
                column.IsVisible = visible.Contains(tag) ||
                                   (tag == "source" && !visible.Contains("__sourceHidden")) ||
                                   (tag == "thumbnail" &&
                                    IsDefaultVisibleThumbnail(tableKey) &&
                                    !visible.Contains("__thumbnailHidden"));
            }

            if (!grid.Columns.Any(column => column.Tag is string && column.IsVisible))
            {
                var first = grid.Columns.FirstOrDefault(column => column.Tag is string);
                if (first is not null)
                    first.IsVisible = true;
            }
        }

        foreach (var column in grid.Columns.Where(column => column.Tag is not string))
            column.CanUserReorder = false;
        DataGridColumnOrderStore.Restore(settings.DataGridColumnOrders, tableKey, grid);
    }

    private static void OnPointerPressed(
        DataGrid grid,
        string tableKey,
        AppSettings settings,
        PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(grid).Properties.IsRightButtonPressed ||
            args.Source is not Visual source ||
            source.GetSelfAndVisualAncestors().OfType<DataGridColumnHeader>().FirstOrDefault() is not { } header)
            return;

        var selectable = grid.Columns
            .Where(column => column.Tag is string)
            .ToList();
        if (selectable.Count == 0)
            return;

        args.Handled = true;
        BuildFlyout(grid, tableKey, settings, selectable)
            .ShowAt(header, showAtPointer: true);
    }

    private static MenuFlyout BuildFlyout(
        DataGrid grid,
        string tableKey,
        AppSettings settings,
        IReadOnlyList<DataGridColumn> selectable)
    {
        var flyout = new MenuFlyout
        {
            FlyoutPresenterTheme = FindTheme("AppMenuFlyoutPresenterTheme"),
            ItemContainerTheme = FindTheme("AppMenuFlyoutItemTheme")
        };
        var itemTheme = FindTheme("AppMenuFlyoutItemTheme");
        var entries = new List<(DataGridColumn Column, MenuItem Item, TextBlock CheckMark)>();
        flyout.Items.Add(new MenuItem
        {
            Header = LocalizationManager.Current.SelectColumns,
            Theme = itemTheme,
            FontWeight = FontWeight.SemiBold,
            Foreground = FindBrush("AppPrimaryTextBrush"),
            IsHitTestVisible = false,
            Focusable = false
        });
        flyout.Items.Add(new Separator());

        foreach (var column in selectable)
        {
            var checkMark = new TextBlock
            {
                Text = "✓",
                Width = 22,
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#6C63FF")),
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = new TextBlock
            {
                Text = FormatHeader(column),
                FontSize = 13,
                Foreground = FindBrush("AppPrimaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { checkMark, label }
            };
            var item = new MenuItem
            {
                Header = content,
                Theme = itemTheme,
                Foreground = FindBrush("AppPrimaryTextBrush"),
                StaysOpenOnClick = true
            };
            entries.Add((column, item, checkMark));
            item.Click += (_, _) =>
            {
                if (column.IsVisible && selectable.Count(candidate => candidate.IsVisible) <= 1)
                    return;

                DataGridColumnWidthStore.Capture(settings.DataGridColumnWidths, tableKey, grid);
                var keepSourceHidden = settings.VisibleDataGridColumns.TryGetValue(tableKey, out var existingVisible) &&
                                       existingVisible.Contains("__sourceHidden", StringComparer.Ordinal);
                var keepThumbnailHidden = settings.VisibleDataGridColumns.TryGetValue(tableKey, out existingVisible) &&
                                          existingVisible.Contains("__thumbnailHidden", StringComparer.Ordinal);
                column.IsVisible = !column.IsVisible;
                settings.VisibleDataGridColumns[tableKey] = selectable
                    .Where(candidate => candidate.IsVisible)
                    .Select(candidate => (string)candidate.Tag!)
                    .ToList();
                var changedTag = column.Tag as string;
                if (changedTag == "source")
                {
                    if (column.IsVisible)
                        settings.VisibleDataGridColumns[tableKey].Remove("__sourceHidden");
                    else
                        settings.VisibleDataGridColumns[tableKey].Add("__sourceHidden");
                }
                else if (keepSourceHidden)
                {
                    settings.VisibleDataGridColumns[tableKey].Add("__sourceHidden");
                }

                if (changedTag == "thumbnail" && IsDefaultVisibleThumbnail(tableKey))
                {
                    if (column.IsVisible)
                        settings.VisibleDataGridColumns[tableKey].Remove("__thumbnailHidden");
                    else
                        settings.VisibleDataGridColumns[tableKey].Add("__thumbnailHidden");
                }
                else if (keepThumbnailHidden && IsDefaultVisibleThumbnail(tableKey))
                {
                    settings.VisibleDataGridColumns[tableKey].Add("__thumbnailHidden");
                }

                UpdateEntries();
            };
            flyout.Items.Add(item);
        }
        UpdateEntries();
        return flyout;

        void UpdateEntries()
        {
            var visibleCount = selectable.Count(candidate => candidate.IsVisible);
            foreach (var entry in entries)
            {
                entry.CheckMark.IsVisible = entry.Column.IsVisible;
                entry.Item.Background = entry.Column.IsVisible
                    ? FindBrush("AppSurfaceSelectedBrush")
                    : Brushes.Transparent;
                entry.Item.IsEnabled = !entry.Column.IsVisible || visibleCount > 1;
            }
        }
    }

    private static bool IsDefaultVisibleThumbnail(string tableKey) =>
        tableKey is "Artists" or "Albums";

    private static IBrush? FindBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, ThemeVariant.Default, out var value) == true &&
            value is IBrush brush)
            return brush;
        return null;
    }

    private static ControlTheme? FindTheme(string key)
    {
        if (Application.Current?.TryGetResource(key, ThemeVariant.Default, out var value) == true &&
            value is ControlTheme theme)
            return theme;
        return null;
    }

    private static string FormatHeader(DataGridColumn column)
    {
        var label = column.Header?.ToString();
        return string.IsNullOrWhiteSpace(label)
            ? column.Tag?.ToString() ?? string.Empty
            : label;
    }
}
