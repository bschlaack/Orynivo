namespace Orynivo.Library;

/// <summary>
/// Resolves the active synchronized lyric line for a playback position. The
/// lookup is a binary search over ascending timestamps, which is how parsed LRC
/// lines are stored.
/// </summary>
public static class LyricLineSelector
{
    /// <summary>
    /// Finds the index of the last line whose timestamp is at or before
    /// <paramref name="position"/>.
    /// </summary>
    /// <typeparam name="T">Line type.</typeparam>
    /// <param name="lines">Synchronized lyric lines ordered by timestamp.</param>
    /// <param name="timeSelector">Returns a line's timestamp, or <see langword="null"/> for an untimed line.</param>
    /// <param name="position">Current playback position.</param>
    /// <returns>The zero-based active line index, or <see langword="-1"/> when no line applies yet.</returns>
    public static int FindActiveIndex<T>(
        IReadOnlyList<T> lines,
        Func<T, TimeSpan?> timeSelector,
        TimeSpan position)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(timeSelector);
        var low = 0;
        var high = lines.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (timeSelector(lines[middle]) is { } time && time <= position)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return found;
    }
}
