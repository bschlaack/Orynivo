namespace Orynivo.Library;

/// <summary>
/// Orders a playback batch so consecutive tracks mix harmonically on the Camelot
/// wheel. The walk is a deterministic greedy nearest-neighbour chain.
/// </summary>
public static class HarmonicOrdering
{
    /// <summary>
    /// Reorders <paramref name="items"/> into a greedy Camelot-adjacency chain.
    /// Items whose key is unknown keep their relative order after the chain, and
    /// the input is returned unchanged when fewer than two items carry a key.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="items">Items in their original ranking order.</param>
    /// <param name="keySelector">Returns the Camelot key of an item, or <see langword="null"/> when unknown.</param>
    /// <returns>The harmonically ordered items.</returns>
    public static IReadOnlyList<T> Order<T>(IReadOnlyList<T> items, Func<T, CamelotKey?> keySelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);
        if (items.Count < 2)
            return items;

        var keyed = new List<(T Item, CamelotKey Key)>();
        var keyless = new List<T>();
        foreach (var item in items)
        {
            if (keySelector(item) is { } key)
                keyed.Add((item, key));
            else
                keyless.Add(item);
        }

        if (keyed.Count < 2)
            return items;

        var remaining = new List<(T Item, CamelotKey Key)>(keyed);
        var ordered = new List<T>(items.Count) { remaining[0].Item };
        var current = remaining[0].Key;
        remaining.RemoveAt(0);
        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestDistance = int.MaxValue;
            for (var index = 0; index < remaining.Count; index++)
            {
                var distance = current.Distance(remaining[index].Key);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                bestIndex = index;
            }

            current = remaining[bestIndex].Key;
            ordered.Add(remaining[bestIndex].Item);
            remaining.RemoveAt(bestIndex);
        }

        ordered.AddRange(keyless);
        return ordered;
    }
}
