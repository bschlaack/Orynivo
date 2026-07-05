namespace Orynivo.Library;

/// <summary>
/// Serialisable filter criteria for a smart playlist.
/// Persisted as JSON in <c>playlists.filter_criteria</c>.
/// </summary>
public sealed class SmartPlaylistCriteria
{
    /// <summary>Gets whether only tracks marked as favourites are included.</summary>
    public bool FavoritesOnly { get; init; }

    /// <summary>Gets the allowed genre names; an empty list means no restriction.</summary>
    public List<string> Genres { get; init; } = [];

    /// <summary>Gets the allowed file format identifiers; an empty list means no restriction.</summary>
    public List<string> Formats { get; init; } = [];

    /// <summary>Gets the allowed bitrate values in kbps; an empty list means no restriction.</summary>
    public List<int> Bitrates { get; init; } = [];

    /// <summary>
    /// Gets stable source keys included in the result; an empty list means no source restriction.
    /// </summary>
    public List<string> SourceKeys { get; init; } = [];

    /// <summary>Gets the inclusive minimum release year, or <see langword="null"/> when unrestricted.</summary>
    public int? MinimumYear { get; init; }

    /// <summary>Gets the inclusive maximum release year, or <see langword="null"/> when unrestricted.</summary>
    public int? MaximumYear { get; init; }

    /// <summary>
    /// Gets the case-insensitive free-text filter matched against a track's title, artist,
    /// and album. Captured from the Tracks search box when the smart playlist is created.
    /// </summary>
    public string? SearchText { get; init; }

    /// <summary>Gets the case-insensitive artist text filter.</summary>
    public string? ArtistContains { get; init; }

    /// <summary>Gets the case-insensitive album text filter.</summary>
    public string? AlbumContains { get; init; }

    /// <summary>Gets the inclusive minimum duration in seconds, or <see langword="null"/> when unrestricted.</summary>
    public double? MinimumDurationSeconds { get; init; }

    /// <summary>Gets the inclusive maximum duration in seconds, or <see langword="null"/> when unrestricted.</summary>
    public double? MaximumDurationSeconds { get; init; }

    /// <summary>Gets the maximum age in days since the track was added, or <see langword="null"/> when unrestricted.</summary>
    public int? AddedWithinDays { get; init; }

    /// <summary>Gets the maximum age in days since the track was last played, or <see langword="null"/> when unrestricted.</summary>
    public int? PlayedWithinDays { get; init; }

    /// <summary>Gets whether tracks with any recorded playback are excluded.</summary>
    public bool NeverPlayed { get; init; }

    /// <summary>Gets the inclusive minimum playback count, or <see langword="null"/> when unrestricted.</summary>
    public int? MinimumPlayCount { get; init; }

    /// <summary>Gets the inclusive maximum playback count, or <see langword="null"/> when unrestricted.</summary>
    public int? MaximumPlayCount { get; init; }

    /// <summary>Gets the ordering applied whenever the smart playlist is resolved.</summary>
    public SmartPlaylistSortOrder SortOrder { get; init; } = SmartPlaylistSortOrder.Title;

    /// <summary>Gets the maximum number of returned tracks, or <see langword="null"/> when unlimited.</summary>
    public int? ResultLimit { get; init; }

    /// <summary>
    /// Filters and orders <paramref name="candidates"/> against this criteria and returns the matching tracks.
    /// </summary>
    /// <param name="candidates">Full set of compact track metadata returned by <see cref="AudioDatabase.GetSmartPlaylistTracks"/>.</param>
    /// <returns>Ordered, optionally limited list of matching <see cref="SmartPlaylistTrackInfo"/> records.</returns>
    public List<SmartPlaylistTrackInfo> Resolve(List<SmartPlaylistTrackInfo> candidates)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        IEnumerable<SmartPlaylistTrackInfo> filtered = candidates.Where(f => Matches(f, now));
        IEnumerable<SmartPlaylistTrackInfo> ordered = SortOrder switch
        {
            SmartPlaylistSortOrder.Random => filtered.OrderBy(_ => Random.Shared.Next()),
            SmartPlaylistSortOrder.LastPlayedNewest => filtered
                .OrderByDescending(t => t.LastPlayedAt.HasValue)
                .ThenByDescending(t => t.LastPlayedAt),
            SmartPlaylistSortOrder.LeastRecentlyPlayed => filtered
                .OrderBy(t => t.LastPlayedAt.HasValue)
                .ThenBy(t => t.LastPlayedAt),
            _ => filtered.OrderBy(t => t.SortTitle, StringComparer.CurrentCultureIgnoreCase)
        };
        var result = ResultLimit.HasValue ? ordered.Take(ResultLimit.Value).ToList() : ordered.ToList();
        return result;
    }

    private bool Matches(SmartPlaylistTrackInfo facet, long nowUnixSeconds)
    {
        if (FavoritesOnly && !facet.IsFavorite) return false;
        if (Genres is { Count: > 0 })
        {
            var trackGenres = string.IsNullOrWhiteSpace(facet.Genre)
                ? Array.Empty<string>()
                : facet.Genre.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!trackGenres.Any(g => Genres.Contains(g, StringComparer.OrdinalIgnoreCase)))
                return false;
        }
        if (Formats is { Count: > 0 } &&
            (string.IsNullOrWhiteSpace(facet.Format) ||
             !Formats.Contains(facet.Format, StringComparer.OrdinalIgnoreCase)))
            return false;
        if (Bitrates is { Count: > 0 } &&
            (!facet.Bitrate.HasValue || !Bitrates.Contains(facet.Bitrate.Value)))
            return false;
        if (SourceKeys is { Count: > 0 } &&
            !SourceKeys.Contains(facet.SourceKey, StringComparer.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim();
            var matchesText =
                (facet.SortTitle?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (facet.Artist?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (facet.Album?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false);
            if (!matchesText)
                return false;
        }
        if (MinimumYear is int minYear && (!facet.Year.HasValue || facet.Year.Value < minYear)) return false;
        if (MaximumYear is int maxYear && (!facet.Year.HasValue || facet.Year.Value > maxYear)) return false;
        if (!string.IsNullOrWhiteSpace(ArtistContains) &&
            (string.IsNullOrWhiteSpace(facet.Artist) ||
             !facet.Artist.Contains(ArtistContains.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            return false;
        if (!string.IsNullOrWhiteSpace(AlbumContains) &&
            (string.IsNullOrWhiteSpace(facet.Album) ||
             !facet.Album.Contains(AlbumContains.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            return false;
        if (MinimumDurationSeconds is double minDur && (!facet.Duration.HasValue || facet.Duration.Value < minDur)) return false;
        if (MaximumDurationSeconds is double maxDur && (!facet.Duration.HasValue || facet.Duration.Value > maxDur)) return false;
        if (AddedWithinDays is > 0 && facet.AddedAt < nowUnixSeconds - (long)AddedWithinDays.Value * 86400) return false;
        if (PlayedWithinDays is > 0 &&
            (!facet.LastPlayedAt.HasValue || facet.LastPlayedAt.Value < nowUnixSeconds - (long)PlayedWithinDays.Value * 86400))
            return false;
        if (NeverPlayed && facet.PlayCount > 0) return false;
        if (MinimumPlayCount is int minPlays && facet.PlayCount < minPlays) return false;
        if (MaximumPlayCount is int maxPlays && facet.PlayCount > maxPlays) return false;
        return true;
    }
}

/// <summary>
/// Defines the ordering applied to tracks resolved from a smart playlist.
/// </summary>
public enum SmartPlaylistSortOrder
{
    /// <summary>Sorts tracks alphabetically by their configured sort title or display title.</summary>
    Title,

    /// <summary>Returns tracks in a newly randomised order each time the playlist is opened.</summary>
    Random,

    /// <summary>Sorts most recently played tracks first and never-played tracks last.</summary>
    LastPlayedNewest,

    /// <summary>Sorts never-played tracks first, followed by the least recently played tracks.</summary>
    LeastRecentlyPlayed
}
