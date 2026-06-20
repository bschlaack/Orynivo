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
/// Adds a right-click column chooser popup to selectable <see cref="DataGrid"/> columns.
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
                column.IsVisible = visible.Contains((string)column.Tag!);

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
        var popup = BuildPopup(grid, tableKey, settings, header, selectable);
        popup.IsOpen = true;
    }

    private static Popup BuildPopup(
        DataGrid grid,
        string tableKey,
        AppSettings settings,
        DataGridColumnHeader header,
        IReadOnlyList<DataGridColumn> selectable)
    {
        var panel = new StackPanel { Spacing = 2 };
        var entries = new List<(DataGridColumn Column, Button Button, TextBlock CheckMark)>();
        panel.Children.Add(new TextBlock
        {
            Text = LocalizationManager.Current.SelectColumns,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(8, 5, 8, 7),
            Foreground = FindBrush("AppPrimaryTextBrush")
        });

        foreach (var column in selectable)
        {
            var checkMark = new TextBlock
            {
                Text = "✓",
                Width = 22,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#6C63FF")),
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = new TextBlock
            {
                Text = FormatHeader(column),
                Foreground = FindBrush("AppPrimaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { checkMark, label }
            };
            var button = new Button
            {
                Content = content,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 6),
                BorderThickness = new Thickness(0)
            };
            entries.Add((column, button, checkMark));
            button.Click += (_, _) =>
            {
                if (column.IsVisible && selectable.Count(candidate => candidate.IsVisible) <= 1)
                    return;

                DataGridColumnWidthStore.Capture(settings.DataGridColumnWidths, tableKey, grid);
                column.IsVisible = !column.IsVisible;
                settings.VisibleDataGridColumns[tableKey] = selectable
                    .Where(candidate => candidate.IsVisible)
                    .Select(candidate => (string)candidate.Tag!)
                    .ToList();

                UpdateEntries();
            };
            panel.Children.Add(button);
        }
        UpdateEntries();

        return new Popup
        {
            PlacementTarget = header,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                MinWidth = 220,
                MaxHeight = 520,
                Padding = new Thickness(7),
                CornerRadius = new CornerRadius(8),
                Background = FindBrush("AppSurfaceBrush"),
                BorderBrush = FindBrush("AppInputBorderBrush"),
                BorderThickness = new Thickness(1),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = panel
                }
            }
        };

        void UpdateEntries()
        {
            var visibleCount = selectable.Count(candidate => candidate.IsVisible);
            foreach (var entry in entries)
            {
                entry.CheckMark.IsVisible = entry.Column.IsVisible;
                entry.Button.Background = entry.Column.IsVisible
                    ? FindBrush("AppSurfaceSelectedBrush")
                    : Brushes.Transparent;
                entry.Button.IsEnabled = !entry.Column.IsVisible || visibleCount > 1;
            }
        }
    }

    private static IBrush? FindBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, ThemeVariant.Default, out var value) == true &&
            value is IBrush brush)
            return brush;
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
