namespace Orynivo.Library;

/// <summary>Defines deterministic ordering for structured library-search results.</summary>
public enum LibrarySearchSortOrder
{
    /// <summary>Preserves the input order, normally Lucene relevance order.</summary>
    Relevance,
    /// <summary>Sorts most recently added tracks first.</summary>
    AddedNewest,
    /// <summary>Sorts oldest library additions first.</summary>
    AddedOldest,
    /// <summary>Sorts newest release years first.</summary>
    YearNewest,
    /// <summary>Sorts oldest release years first.</summary>
    YearOldest,
    /// <summary>Sorts by display title.</summary>
    Title
}

/// <summary>Structured year and library-addition filters shared by local and server searches.</summary>
/// <param name="MinimumYear">Inclusive minimum release year.</param>
/// <param name="MaximumYear">Inclusive maximum release year.</param>
/// <param name="AddedFrom">Inclusive lower library-added Unix timestamp.</param>
/// <param name="AddedBefore">Exclusive upper library-added Unix timestamp.</param>
/// <param name="SortOrder">Requested result ordering.</param>
public sealed record LibrarySearchFilterOptions(
    int? MinimumYear = null,
    int? MaximumYear = null,
    long? AddedFrom = null,
    long? AddedBefore = null,
    LibrarySearchSortOrder SortOrder = LibrarySearchSortOrder.Relevance);

/// <summary>Applies structured search filters to compact library track metadata.</summary>
public static class LibrarySearchFilter
{
    /// <summary>Filters and orders compact track candidates.</summary>
    /// <param name="candidates">Candidate tracks in relevance order when applicable.</param>
    /// <param name="options">Structured filter and ordering options.</param>
    /// <returns>Filtered tracks in the requested order.</returns>
    public static List<SmartPlaylistTrackInfo> Apply(
        IEnumerable<SmartPlaylistTrackInfo> candidates,
        LibrarySearchFilterOptions options)
    {
        var filtered = candidates.Where(track =>
            (!options.MinimumYear.HasValue || track.Year >= options.MinimumYear.Value) &&
            (!options.MaximumYear.HasValue || track.Year <= options.MaximumYear.Value) &&
            (!options.AddedFrom.HasValue || track.AddedAt >= options.AddedFrom.Value) &&
            (!options.AddedBefore.HasValue || track.AddedAt < options.AddedBefore.Value));

        filtered = options.SortOrder switch
        {
            LibrarySearchSortOrder.AddedNewest => filtered
                .OrderByDescending(static track => track.AddedAt)
                .ThenBy(static track => track.SortTitle, StringComparer.CurrentCultureIgnoreCase),
            LibrarySearchSortOrder.AddedOldest => filtered
                .OrderBy(static track => track.AddedAt)
                .ThenBy(static track => track.SortTitle, StringComparer.CurrentCultureIgnoreCase),
            LibrarySearchSortOrder.YearNewest => filtered
                .OrderByDescending(static track => track.Year.HasValue)
                .ThenByDescending(static track => track.Year)
                .ThenBy(static track => track.SortTitle, StringComparer.CurrentCultureIgnoreCase),
            LibrarySearchSortOrder.YearOldest => filtered
                .OrderByDescending(static track => track.Year.HasValue)
                .ThenBy(static track => track.Year)
                .ThenBy(static track => track.SortTitle, StringComparer.CurrentCultureIgnoreCase),
            LibrarySearchSortOrder.Title => filtered
                .OrderBy(static track => track.SortTitle, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered
        };
        return filtered.ToList();
    }
}
