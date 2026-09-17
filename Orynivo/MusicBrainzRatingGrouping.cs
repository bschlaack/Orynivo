namespace Orynivo;

/// <summary>
/// Partitions album rows for a MusicBrainz rating refresh: rows with a valid
/// recording MBID are grouped case-insensitively so one direct lookup result can
/// be reused for every represented local or remote library, while rows without a
/// valid MBID are returned separately for individual resolution.
/// </summary>
internal static class MusicBrainzRatingGrouping
{
    /// <summary>
    /// Partitions <paramref name="items"/> by their recording MBID.
    /// </summary>
    /// <typeparam name="T">Row type.</typeparam>
    /// <param name="items">Rows in display order.</param>
    /// <param name="recordingMbidSelector">Selects a row's recording MBID, if any.</param>
    /// <returns>
    /// Groups in first-occurrence order plus the rows whose MBID is absent or not a
    /// valid <see cref="Guid"/>.
    /// </returns>
    internal static (IReadOnlyList<IReadOnlyList<T>> Groups, IReadOnlyList<T> Ungrouped) Partition<T>(
        IEnumerable<T> items,
        Func<T, string?> recordingMbidSelector)
    {
        var groups = new List<IReadOnlyList<T>>();
        var ungrouped = new List<T>();
        var byKey = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var item in items)
        {
            var mbid = recordingMbidSelector(item);
            if (mbid is null || !Guid.TryParse(mbid, out _))
            {
                ungrouped.Add(item);
                continue;
            }

            if (!byKey.TryGetValue(mbid, out var group))
            {
                group = [];
                byKey[mbid] = group;
                order.Add(mbid);
            }

            group.Add(item);
        }

        foreach (var key in order)
            groups.Add(byKey[key]);

        return (groups, ungrouped);
    }
}
