using System.Globalization;
using System.Text;

namespace Orynivo.Library;

/// <summary>Identifies one track candidate in the library that produced a genre-cloud snapshot.</summary>
/// <param name="TrackId">Provider-local track identifier.</param>
/// <param name="GenreKey">Normalized deepest taxonomy key assigned to the track.</param>
/// <param name="IsFavorite">Whether the provider marks the track as a favorite.</param>
public sealed record GenreCloudTrackCandidate(long TrackId, string GenreKey, bool IsFavorite);

/// <summary>Represents one visible child in an interactive genre cloud.</summary>
/// <param name="Key">Stable language-independent taxonomy key.</param>
/// <param name="DisplayName">English fallback display name.</param>
/// <param name="TrackCount">Number of matching tracks, including descendants.</param>
/// <param name="HasChildren">Whether a further taxonomy drill-down is available.</param>
public sealed record GenreCloudNode(string Key, string DisplayName, int TrackCount, bool HasChildren);

/// <summary>Compact provider-local data used to merge genre clouds across local and remote libraries.</summary>
/// <param name="ParentKey">Selected taxonomy key, or <see langword="null"/> for the root.</param>
/// <param name="BreadcrumbKeys">Taxonomy keys from the root to the selected node.</param>
/// <param name="Nodes">Visible child nodes ordered by track count.</param>
/// <param name="Candidates">Bounded matching track candidates for client-side personalization.</param>
public sealed record GenreCloudSnapshot(
    string? ParentKey,
    IReadOnlyList<string> BreadcrumbKeys,
    IReadOnlyList<GenreCloudNode> Nodes,
    IReadOnlyList<GenreCloudTrackCandidate> Candidates);

/// <summary>
/// Normalizes embedded genre tags into a stable hierarchy and builds compact genre-cloud snapshots.
/// </summary>
public static class GenreCloudService
{
    private sealed record Definition(string Key, string Name, string? ParentKey, string[] Aliases);

    private static readonly Definition[] Definitions =
    [
        D("rock", "Rock", null),
        D("alternative-rock", "Alternative Rock", "rock", "alternative", "alt rock", "alt. rock"),
        D("indie-rock", "Indie Rock", "alternative-rock", "indie"),
        D("grunge", "Grunge", "alternative-rock"),
        D("shoegaze", "Shoegaze", "alternative-rock"),
        D("progressive-rock", "Progressive Rock", "rock", "prog rock", "prog"),
        D("symphonic-prog", "Symphonic Prog", "progressive-rock", "symphonic progressive rock"),
        D("neo-prog", "Neo-Prog", "progressive-rock", "neo progressive rock"),
        D("psychedelic-rock", "Psychedelic Rock", "rock", "psychedelia"),
        D("hard-rock", "Hard Rock", "rock"),
        D("punk", "Punk", "rock", "punk rock"),
        D("post-rock", "Post-Rock", "rock"),
        D("metal", "Metal", null, "heavy metal"),
        D("progressive-metal", "Progressive Metal", "metal", "prog metal"),
        D("death-metal", "Death Metal", "metal"),
        D("black-metal", "Black Metal", "metal"),
        D("doom-metal", "Doom Metal", "metal"),
        D("electronic", "Electronic", null, "electronica", "electro"),
        D("ambient", "Ambient", "electronic"),
        D("house", "House", "electronic"),
        D("techno", "Techno", "electronic"),
        D("trance", "Trance", "electronic"),
        D("drum-and-bass", "Drum and Bass", "electronic", "drum & bass", "dnb", "d'n'b"),
        D("idm", "IDM", "electronic", "intelligent dance music"),
        D("pop", "Pop", null),
        D("synth-pop", "Synth-pop", "pop", "synthpop"),
        D("dance-pop", "Dance Pop", "pop"),
        D("jazz", "Jazz", null),
        D("bebop", "Bebop", "jazz"),
        D("fusion", "Jazz Fusion", "jazz", "jazz fusion", "fusion jazz"),
        D("smooth-jazz", "Smooth Jazz", "jazz"),
        D("free-jazz", "Free Jazz", "jazz"),
        D("classical", "Classical", null, "classical music"),
        D("baroque", "Baroque", "classical"),
        D("romantic", "Romantic", "classical", "romantic era"),
        D("contemporary-classical", "Contemporary Classical", "classical", "modern classical"),
        D("hip-hop", "Hip-Hop", null, "hip hop", "rap"),
        D("trip-hop", "Trip-Hop", "hip-hop", "trip hop"),
        D("soul-rnb", "Soul & R&B", null, "soul", "r&b", "rhythm and blues"),
        D("funk", "Funk", "soul-rnb"),
        D("blues", "Blues", null),
        D("country", "Country", null),
        D("folk", "Folk", null),
        D("singer-songwriter", "Singer-Songwriter", "folk", "singer songwriter"),
        D("reggae", "Reggae", null),
        D("ska", "Ska", "reggae"),
        D("world", "World", null, "world music"),
        D("latin", "Latin", "world", "latin music"),
        D("soundtrack", "Soundtrack", null, "ost", "film score", "score"),
        D("game-music", "Game Music", "soundtrack", "video game music", "vgm"),
        D("spoken-word", "Spoken Word", null, "audiobook", "audio book"),
        D("other", "Other", null)
    ];

    private static readonly IReadOnlyDictionary<string, Definition> ByKey =
        Definitions.ToDictionary(item => item.Key, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, Definition> ByAlias = BuildAliasMap();

    /// <summary>Builds a compact snapshot for the requested taxonomy level.</summary>
    /// <param name="tracks">Provider-local lightweight track facets.</param>
    /// <param name="parentKey">Selected taxonomy key, or <see langword="null"/> for root genres.</param>
    /// <param name="maximumCandidates">Maximum matching track identifiers included for recommendation ranking.</param>
    /// <returns>The visible genre nodes and bounded track candidates.</returns>
    public static GenreCloudSnapshot BuildSnapshot(
        IEnumerable<TrackFacetInfo> tracks,
        string? parentKey = null,
        int maximumCandidates = 250)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        maximumCandidates = Math.Clamp(maximumCandidates, 1, 2000);
        var selected = NormalizeSelectedKey(parentKey);
        var classified = tracks
            .Select(track => (Track: track, Genres: ResolveTrackGenres(track.Genre)))
            .Where(item => item.Genres.Count > 0)
            .ToList();

        var visibleDefinitions = Definitions
            .Where(item => string.Equals(item.ParentKey, selected, StringComparison.Ordinal))
            .Select(item => new GenreCloudNode(
                item.Key,
                item.Name,
                classified.Count(track => track.Genres.Any(key => IsDescendantOrSelf(key, item.Key))),
                Definitions.Any(child => string.Equals(child.ParentKey, item.Key, StringComparison.Ordinal))))
            .Where(item => item.TrackCount > 0)
            .OrderByDescending(item => item.TrackCount)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matching = selected is null
            ? classified
            : classified.Where(track => track.Genres.Any(key => IsDescendantOrSelf(key, selected))).ToList();
        var candidates = matching
            .OrderByDescending(item => item.Track.IsFavorite)
            .ThenBy(item => StableCandidateOrder(item.Track.Id, selected))
            .Take(maximumCandidates)
            .Select(item => new GenreCloudTrackCandidate(
                item.Track.Id,
                item.Genres.First(key => selected is null || IsDescendantOrSelf(key, selected)),
                item.Track.IsFavorite))
            .ToList();

        return new GenreCloudSnapshot(selected, BuildBreadcrumb(selected), visibleDefinitions, candidates);
    }

    /// <summary>Returns whether <paramref name="candidateKey"/> is the selected key or one of its descendants.</summary>
    /// <param name="candidateKey">Candidate taxonomy key.</param>
    /// <param name="ancestorKey">Expected ancestor taxonomy key.</param>
    /// <returns><see langword="true"/> when the candidate belongs to the requested subtree.</returns>
    public static bool IsDescendantOrSelf(string candidateKey, string ancestorKey)
    {
        var current = candidateKey;
        while (ByKey.TryGetValue(current, out var definition))
        {
            if (string.Equals(current, ancestorKey, StringComparison.Ordinal))
                return true;
            if (definition.ParentKey is null)
                return false;
            current = definition.ParentKey;
        }
        return false;
    }

    /// <summary>Maps one embedded genre value to its deepest stable taxonomy keys.</summary>
    /// <param name="value">Raw genre tag, optionally containing several delimited genres.</param>
    /// <returns>Distinct normalized taxonomy keys.</returns>
    public static IReadOnlyList<string> ResolveGenreKeys(string? value) => ResolveTrackGenres(value);

    /// <summary>Returns the English fallback name for a stable taxonomy key.</summary>
    /// <param name="key">Stable taxonomy key.</param>
    /// <returns>The taxonomy display name, or the key when it is unknown.</returns>
    public static string GetDisplayName(string key) =>
        ByKey.TryGetValue(key, out var definition) ? definition.Name : key;

    private static IReadOnlyList<string> ResolveTrackGenres(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in value.Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = Normalize(token);
            if (ByAlias.TryGetValue(normalized, out var definition))
                result.Add(definition.Key);
            else if (normalized.Length > 0)
                result.Add("other");
        }
        return result.ToList();
    }

    private static string? NormalizeSelectedKey(string? key) =>
        string.IsNullOrWhiteSpace(key) || !ByKey.ContainsKey(key.Trim()) ? null : key.Trim();

    private static IReadOnlyList<string> BuildBreadcrumb(string? selected)
    {
        var result = new List<string>();
        var current = selected;
        while (current is not null && ByKey.TryGetValue(current, out var definition))
        {
            result.Add(current);
            current = definition.ParentKey;
        }
        result.Reverse();
        return result;
    }

    private static int StableCandidateOrder(long id, string? selected)
    {
        unchecked
        {
            var hash = (int)(id ^ (id >> 32));
            foreach (var character in selected ?? string.Empty)
                hash = (hash * 397) ^ character;
            return hash & int.MaxValue;
        }
    }

    private static IReadOnlyDictionary<string, Definition> BuildAliasMap()
    {
        var result = new Dictionary<string, Definition>(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            result[Normalize(definition.Name)] = definition;
            result[Normalize(definition.Key)] = definition;
            foreach (var alias in definition.Aliases)
                result[Normalize(alias)] = definition;
        }
        return result;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static Definition D(string key, string name, string? parentKey, params string[] aliases) =>
        new(key, name, parentKey, aliases);
}
