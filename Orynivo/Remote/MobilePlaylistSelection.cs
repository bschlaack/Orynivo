namespace Orynivo.Remote;

/// <summary>Selects an ordered playlist suffix without conflating duplicate or unavailable entries.</summary>
internal static class MobilePlaylistSelection
{
    /// <summary>Returns rows from a still-identical selected entry onwards.</summary>
    /// <typeparam name="T">Resolved playlist row type.</typeparam>
    /// <param name="rows">Current ordered rows, including unavailable entries.</param>
    /// <param name="index">Original selected entry index.</param>
    /// <param name="expected">Expected source-aware identity.</param>
    /// <param name="identity">Source identity accessor.</param>
    /// <param name="playable">Eligibility predicate.</param>
    /// <returns>The ordered suffix, or no rows when the selection changed or became unavailable.</returns>
    internal static IReadOnlyList<T> From<T>(IReadOnlyList<T> rows, int index, string? expected,
        Func<T, string> identity, Func<T, bool> playable) => index < 0 || index >= rows.Count ||
        !playable(rows[index]) || !string.Equals(expected, identity(rows[index]), StringComparison.Ordinal)
            ? [] : rows.Skip(index).ToArray();
}
