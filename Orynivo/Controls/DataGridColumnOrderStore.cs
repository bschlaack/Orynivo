using Avalonia.Controls;

namespace Orynivo.Controls;

/// <summary>
/// Captures and restores the display order of identified <see cref="DataGrid"/> columns.
/// </summary>
internal static class DataGridColumnOrderStore
{
    /// <summary>
    /// Stores identified columns in their current visual order.
    /// </summary>
    /// <param name="orders">Application settings dictionary receiving the order.</param>
    /// <param name="key">Stable table or view key.</param>
    /// <param name="grid">Grid whose order is captured.</param>
    internal static void Capture(
        IDictionary<string, List<string>> orders,
        string key,
        DataGrid grid)
    {
        var identifiers = grid.Columns
            .Where(column => column.Tag is string)
            .OrderBy(column => column.DisplayIndex)
            .Select(column => (string)column.Tag!)
            .ToList();
        if (identifiers.Count > 0)
            orders[key] = identifiers;
    }

    /// <summary>
    /// Restores identified columns into the saved visual order while retaining fixed-column slots.
    /// </summary>
    /// <param name="orders">Application settings dictionary containing saved orders.</param>
    /// <param name="key">Stable table or view key.</param>
    /// <param name="grid">Grid whose order is restored.</param>
    internal static void Restore(
        IReadOnlyDictionary<string, List<string>> orders,
        string key,
        DataGrid grid)
    {
        var identified = grid.Columns
            .Where(column => column.Tag is string)
            .ToList();
        if (identified.Count == 0 ||
            !orders.TryGetValue(key, out var saved))
            return;

        var byId = identified.ToDictionary(
            column => (string)column.Tag!,
            StringComparer.Ordinal);
        var ordered = saved
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .Concat(identified.Where(column => !saved.Contains((string)column.Tag!, StringComparer.Ordinal)))
            .ToList();
        var slots = identified
            .Select(column => column.DisplayIndex)
            .Order()
            .ToArray();

        for (var index = 0; index < ordered.Count; index++)
            ordered[index].DisplayIndex = slots[index];
    }
}
