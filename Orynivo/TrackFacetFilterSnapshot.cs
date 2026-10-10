using Orynivo.Library;

namespace Orynivo;

/// <summary>Owns copied facet selections for an asynchronous search without reading mutable window state.</summary>
internal sealed class TrackFacetFilterSnapshot
{
    private readonly bool _favoritesOnly;
    private readonly HashSet<string> _genres;
    private readonly HashSet<string> _formats;
    private readonly HashSet<int> _bitrates;
    private readonly HashSet<string> _sources;

    /// <summary>Copies the current facet choices before asynchronous work begins.</summary>
    /// <param name="favoritesOnly">Whether tracks must be favorites.</param>
    /// <param name="genres">Selected genres.</param>
    /// <param name="formats">Selected audio formats.</param>
    /// <param name="bitrates">Selected bitrates.</param>
    /// <param name="sources">Selected opaque source keys.</param>
    internal TrackFacetFilterSnapshot(bool favoritesOnly, IEnumerable<string> genres,
        IEnumerable<string> formats, IEnumerable<int> bitrates, IEnumerable<string> sources)
    {
        _favoritesOnly = favoritesOnly;
        _genres = new(genres, StringComparer.OrdinalIgnoreCase);
        _formats = new(formats, StringComparer.OrdinalIgnoreCase);
        _bitrates = new(bitrates);
        _sources = new(sources, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Determines whether the captured source facet includes one source.</summary>
    /// <param name="sourceKey">Opaque local or server identity.</param>
    /// <returns>Whether the source may contribute search results.</returns>
    internal bool IncludesSource(string sourceKey) => _sources.Count == 0 || _sources.Contains(sourceKey);

    /// <summary>Gets whether track metadata filtering requires local facet rows.</summary>
    internal bool HasTrackFilters => _favoritesOnly || _genres.Count > 0 || _formats.Count > 0 || _bitrates.Count > 0;

    /// <summary>Evaluates a track against the captured selections.</summary>
    /// <param name="facet">Track facet metadata.</param>
    /// <returns>Whether every active dimension matches.</returns>
    internal bool Matches(TrackFacetInfo facet) =>
        Matches(facet, _favoritesOnly, _genres, _formats, _bitrates, _sources);

    /// <summary>Shares facet semantics between live filter counts and captured search filters.</summary>
    /// <param name="facet">Track facet metadata.</param>
    /// <param name="favoritesOnly">Whether tracks must be favorites.</param>
    /// <param name="genres">Selected genres.</param>
    /// <param name="formats">Selected audio formats.</param>
    /// <param name="bitrates">Selected bitrates.</param>
    /// <param name="sources">Selected source keys.</param>
    /// <param name="ignoredDimension">Optional dimension omitted while calculating its option counts.</param>
    /// <returns>Whether every non-ignored active dimension matches.</returns>
    internal static bool Matches(TrackFacetInfo facet, bool favoritesOnly,
        IReadOnlySet<string> genres, IReadOnlySet<string> formats, IReadOnlySet<int> bitrates,
        IReadOnlySet<string> sources, string? ignoredDimension = null) =>
        (ignoredDimension == "favorite" || !favoritesOnly || facet.IsFavorite) &&
        (ignoredDimension == "genre" || genres.Count == 0 ||
         (facet.Genre ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(genres.Contains)) &&
        (ignoredDimension == "format" || formats.Count == 0 ||
         (!string.IsNullOrWhiteSpace(facet.Format) && formats.Contains(facet.Format))) &&
        (ignoredDimension == "bitrate" || bitrates.Count == 0 ||
         (facet.Bitrate.HasValue && bitrates.Contains(facet.Bitrate.Value))) &&
        (ignoredDimension == "source" || sources.Count == 0 || sources.Contains(facet.SourceKey));
}
