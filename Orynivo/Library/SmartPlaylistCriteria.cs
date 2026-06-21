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

    /// <summary>Gets the inclusive minimum release year, or <see langword="null"/> when unrestricted.</summary>
    public int? MinimumYear { get; init; }

    /// <summary>Gets the inclusive maximum release year, or <see langword="null"/> when unrestricted.</summary>
    public int? MaximumYear { get; init; }

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
