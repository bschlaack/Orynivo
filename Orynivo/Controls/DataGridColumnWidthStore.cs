using Avalonia.Controls;

namespace Orynivo.Controls;

/// <summary>
/// Captures and restores user-adjusted <see cref="DataGrid"/> column widths.
/// </summary>
internal static class DataGridColumnWidthStore
{
    private const double MinimumStoredWidth = 24;
    private const double MaximumStoredWidth = 2400;

    /// <summary>
    /// Stores the current pixel width of every realized column under a stable table key.
    /// </summary>
    /// <param name="widths">Application settings dictionary receiving the values.</param>
    /// <param name="key">Stable table or view key.</param>
    /// <param name="grid">Grid whose columns are captured.</param>
    internal static void Capture(
        IDictionary<string, List<double>> widths,
        string key,
        DataGrid grid)
    {
        if (grid.Columns.Count == 0)
            return;

        widths.TryGetValue(key, out var previous);
        var captured = grid.Columns
            .Select((column, index) =>
            {
                if (double.IsFinite(column.ActualWidth) && column.ActualWidth >= MinimumStoredWidth)
                    return column.ActualWidth;
                if (previous is not null &&
                    index < previous.Count &&
                    double.IsFinite(previous[index]) &&
                    previous[index] >= MinimumStoredWidth)
                    return previous[index];
                if (column.Width.UnitType == DataGridLengthUnitType.Pixel &&
                    double.IsFinite(column.Width.Value) &&
                    column.Width.Value >= MinimumStoredWidth)
                    return column.Width.Value;
                return 120;
            })
            .Select(width => Math.Clamp(width, MinimumStoredWidth, MaximumStoredWidth))
            .ToList();
        widths[key] = captured;
    }

    /// <summary>
    /// Restores pixel widths when a complete, valid value set exists for the table.
    /// </summary>
    /// <param name="widths">Application settings dictionary containing saved values.</param>
    /// <param name="key">Stable table or view key.</param>
    /// <param name="grid">Grid whose columns are restored.</param>
    internal static void Restore(
        IReadOnlyDictionary<string, List<double>> widths,
        string key,
        DataGrid grid)
    {
        if (!widths.TryGetValue(key, out var saved) || saved.Count != grid.Columns.Count)
            return;

        for (var index = 0; index < saved.Count; index++)
        {
            var width = saved[index];
            if (!double.IsFinite(width) || width < MinimumStoredWidth || width > MaximumStoredWidth)
                return;
        }

        for (var index = 0; index < saved.Count; index++)
            grid.Columns[index].Width = new DataGridLength(saved[index]);
    }
}
