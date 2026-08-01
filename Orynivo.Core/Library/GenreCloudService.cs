using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

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
/// <param name="AlbumCount">Number of distinct matching albums, including descendants.</param>
/// <param name="HasChildren">Whether a further taxonomy drill-down is available.</param>
public sealed record GenreCloudNode(string Key, string DisplayName, int TrackCount, int AlbumCount, bool HasChildren);

/// <summary>Compact provider-local data used to merge genre clouds across local and remote libraries.</summary>
/// <param name="ParentKey">Selected taxonomy key, or <see langword="null"/> for the root.</param>
/// <param name="BreadcrumbKeys">One deterministic taxonomy path from a root to the selected node.</param>
/// <param name="Nodes">Visible child nodes ordered by track count.</param>
/// <param name="Candidates">Bounded matching track candidates for client-side personalization.</param>
public sealed record GenreCloudSnapshot(
    string? ParentKey,
    IReadOnlyList<string> BreadcrumbKeys,
    IReadOnlyList<GenreCloudNode> Nodes,
    IReadOnlyList<GenreCloudTrackCandidate> Candidates);

/// <summary>
/// Loads the curated genre graph, normalizes embedded tags, and builds compact provider-local cloud snapshots.
/// </summary>
public static class GenreCloudService
{
    private const string TaxonomyResourceName = "Orynivo.Library.GenreTaxonomy.json";
    private const string MoreKey = "more-genres";
    private const string UnmappedPrefix = "unmapped:";

    private sealed record Definition(
        string Key,
        string Name,
        bool TopLevel,
        string[] Parents,
        string[] Aliases);

    private sealed record ClassifiedTrack(TrackFacetInfo Track, IReadOnlyList<string> Genres);

    private static readonly Definition[] Definitions = LoadDefinitions();
    private static readonly IReadOnlyDictionary<string, Definition> ByKey =
        Definitions.ToDictionary(item => item.Key, StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, Definition> ByAlias = BuildAliasMap();

    /// <summary>Builds one compact level of the genre graph.</summary>
    /// <param name="tracks">Provider-local lightweight track facets.</param>
    /// <param name="parentKey">Selected taxonomy key, or <see langword="null"/> for top-level genres.</param>
    /// <param name="maximumCandidates">Maximum matching track identifiers included for recommendation ranking.</param>
    /// <returns>The visible nodes and bounded track candidates.</returns>
    public static GenreCloudSnapshot BuildSnapshot(
        IEnumerable<TrackFacetInfo> tracks,
        string? parentKey = null,
        int maximumCandidates = 250)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        maximumCandidates = Math.Clamp(maximumCandidates, 1, 2000);
        var selected = NormalizeSelectedKey(parentKey);
        var classified = tracks
            .Select(track => new ClassifiedTrack(track, ResolveTrackGenres(track.Genre)))
            .Where(item => item.Genres.Count > 0)
            .ToList();

        List<GenreCloudNode> nodes;
        if (selected == MoreKey)
        {
            nodes = classified
                .SelectMany(item => item.Genres)
                .Where(IsUnmapped)
                .Distinct(StringComparer.Ordinal)
                .Select(key => new GenreCloudNode(
                    key,
                    GetDisplayName(key),
                    classified.Count(track => track.Genres.Contains(key, StringComparer.Ordinal)),
                    CountAlbums(classified, key),
                    false))
                .OrderByDescending(node => node.TrackCount)
                .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            var visible = selected is null
                ? Definitions.Where(item => item.TopLevel)
                : Definitions.Where(item => item.Parents.Contains(selected, StringComparer.Ordinal));
            nodes = visible
                .Select(item => new GenreCloudNode(
                    item.Key,
                    item.Name,
                    classified.Count(track => track.Genres.Any(key => IsDescendantOrSelf(key, item.Key))),
                    CountAlbums(classified, item.Key),
                    Definitions.Any(child => child.Parents.Contains(item.Key, StringComparer.Ordinal))))
                .Where(item => item.TrackCount > 0)
                .OrderByDescending(item => item.TrackCount)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selected is null)
            {
                var unmappedCount = classified.Count(track => track.Genres.Any(IsUnmapped));
                if (unmappedCount > 0)
                    nodes.Add(new GenreCloudNode(
                        MoreKey,
                        "More Genres",
                        unmappedCount,
                        classified.Where(track => track.Genres.Any(IsUnmapped))
                            .Select(track => track.Track.AlbumId)
                            .Where(albumId => albumId.HasValue)
                            .Distinct()
                            .Count(),
                        true));
            }
        }

        var matching = selected switch
        {
            null => classified,
            MoreKey => classified.Where(track => track.Genres.Any(IsUnmapped)).ToList(),
            _ when IsUnmapped(selected) => classified.Where(track => track.Genres.Contains(selected, StringComparer.Ordinal)).ToList(),
            _ => classified.Where(track => track.Genres.Any(key => IsDescendantOrSelf(key, selected))).ToList()
        };
        var candidates = matching
            .OrderByDescending(item => item.Track.IsFavorite)
            .ThenBy(item => StableCandidateOrder(item.Track.Id, selected))
            .Take(maximumCandidates)
            .Select(item => new GenreCloudTrackCandidate(
                item.Track.Id,
                item.Genres.First(key => selected is null || BelongsToSelection(key, selected)),
                item.Track.IsFavorite))
            .ToList();

        return new GenreCloudSnapshot(selected, BuildBreadcrumb(selected), nodes, candidates);
    }

    /// <summary>Returns whether a genre is identical to or descends through any path from an ancestor.</summary>
    /// <param name="candidateKey">Candidate taxonomy key.</param>
    /// <param name="ancestorKey">Expected ancestor taxonomy key.</param>
    /// <returns><see langword="true"/> when at least one graph path reaches the ancestor.</returns>
    public static bool IsDescendantOrSelf(string candidateKey, string ancestorKey)
    {
        if (string.Equals(candidateKey, ancestorKey, StringComparison.Ordinal))
            return true;
        if (ancestorKey == MoreKey)
            return IsUnmapped(candidateKey);
        if (!ByKey.ContainsKey(candidateKey) || !ByKey.ContainsKey(ancestorKey))
            return false;

        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(candidateKey);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current) || !ByKey.TryGetValue(current, out var definition))
                continue;
            foreach (var parent in definition.Parents)
            {
                if (string.Equals(parent, ancestorKey, StringComparison.Ordinal))
                    return true;
                pending.Push(parent);
            }
        }
        return false;
    }

    /// <summary>Maps one embedded genre value to its deepest stable or dynamically preserved keys.</summary>
    /// <param name="value">Raw genre tag, optionally containing several delimited genres.</param>
    /// <returns>Distinct normalized taxonomy keys.</returns>
    public static IReadOnlyList<string> ResolveGenreKeys(string? value) => ResolveTrackGenres(value);

    private static int CountAlbums(IEnumerable<ClassifiedTrack> tracks, string genreKey) =>
        tracks.Where(track => track.Genres.Any(key => IsDescendantOrSelf(key, genreKey)))
            .Select(track => track.Track.AlbumId)
            .Where(albumId => albumId.HasValue)
            .Distinct()
            .Count();

    /// <summary>Returns the English fallback name for a stable or dynamically preserved taxonomy key.</summary>
    /// <param name="key">Stable taxonomy key.</param>
    /// <returns>The taxonomy display name.</returns>
    public static string GetDisplayName(string key)
    {
        if (key == MoreKey)
            return "More Genres";
        if (ByKey.TryGetValue(key, out var definition))
            return definition.Name;
        if (IsUnmapped(key))
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key[UnmappedPrefix.Length..].Replace('-', ' '));
        return key;
    }

    private static IReadOnlyList<string> ResolveTrackGenres(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in value.Split([';', ',', '|', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = Normalize(token);
            if (TryResolveDefinition(normalized, out var definition))
                result.Add(definition.Key);
            else if (normalized.Length > 0)
                result.Add(UnmappedPrefix + normalized.Replace(' ', '-'));
        }
        return result.ToList();
    }

    private static bool TryResolveDefinition(string normalized, out Definition definition)
    {
        if (ByAlias.TryGetValue(normalized, out definition!))
            return true;
        var padded = $" {normalized} ";
        foreach (var candidate in Definitions.OrderByDescending(item => Normalize(item.Name).Length))
        {
            var phrase = Normalize(candidate.Name);
            if (phrase.Length >= 4 && padded.Contains($" {phrase} ", StringComparison.Ordinal))
            {
                definition = candidate;
                return true;
            }
        }
        definition = null!;
        return false;
    }

    private static bool BelongsToSelection(string key, string selected) =>
        selected == MoreKey ? IsUnmapped(key) :
        IsUnmapped(selected) ? string.Equals(key, selected, StringComparison.Ordinal) :
        IsDescendantOrSelf(key, selected);

    private static string? NormalizeSelectedKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var trimmed = key.Trim();
        return trimmed == MoreKey || IsUnmapped(trimmed) || ByKey.ContainsKey(trimmed) ? trimmed : null;
    }

    private static IReadOnlyList<string> BuildBreadcrumb(string? selected)
    {
        if (selected is null)
            return [];
        if (selected == MoreKey)
            return [MoreKey];
        if (IsUnmapped(selected))
            return [MoreKey, selected];

        var result = new List<string>();
        var current = selected;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current) && ByKey.TryGetValue(current, out var definition))
        {
            result.Add(current);
            current = definition.Parents
                .OrderByDescending(parent => ByKey.GetValueOrDefault(parent)?.TopLevel == true)
                .ThenBy(parent => parent, StringComparer.Ordinal)
                .FirstOrDefault();
            if (current is null)
                break;
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

    private static Definition[] LoadDefinitions()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(TaxonomyResourceName)
            ?? throw new InvalidOperationException($"Embedded genre taxonomy '{TaxonomyResourceName}' is missing.");
        return JsonSerializer.Deserialize<Definition[]>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("The embedded genre taxonomy is empty or invalid.");
    }

    private static bool IsUnmapped(string key) => key.StartsWith(UnmappedPrefix, StringComparison.Ordinal);

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
}
