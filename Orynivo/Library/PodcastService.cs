using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Xml.Linq;

namespace Orynivo.Library;

public sealed class PodcastService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<IReadOnlyList<PodcastSearchResult>> SearchAsync(
        string? searchText,
        IReadOnlyCollection<string>? genreIds = null,
        CancellationToken cancellationToken = default)
    {
        var country = GetStoreCountry();
        var terms = genreIds is { Count: > 0 } ? genreIds : [string.Empty];
        var results = new List<SearchResultDto>();
        foreach (var genreId in terms)
        {
            var term = string.IsNullOrWhiteSpace(searchText) ? "podcast" : searchText.Trim();
            if (string.IsNullOrWhiteSpace(term) && string.IsNullOrWhiteSpace(genreId))
                continue;
            var genreParameter = string.IsNullOrWhiteSpace(genreId)
                ? string.Empty
                : $"&genreId={Uri.EscapeDataString(genreId)}";
            var uri = $"https://itunes.apple.com/search?media=podcast&entity=podcast&limit=200&country={country}{genreParameter}&term={Uri.EscapeDataString(term)}";
            var response = await HttpClient.GetFromJsonAsync<SearchResponse>(uri, cancellationToken);
            if (response is not null)
                results.AddRange(response.Results);
        }
        return MapSearchResults(results);
    }

    public async Task<IReadOnlyList<PodcastSearchResult>> GetPopularPodcastsAsync(
        CancellationToken cancellationToken = default)
    {
        var ids = await GetPopularPodcastIdsAsync(cancellationToken);
        if (ids.Count == 0)
            return [];
        var country = GetStoreCountry().ToLowerInvariant();
        var response = await HttpClient.GetFromJsonAsync<SearchResponse>(
            $"https://itunes.apple.com/lookup?entity=podcast&country={country}&id={string.Join(",", ids)}",
            cancellationToken);
        return MapSearchResults(response?.Results ?? []);
    }

    private static IReadOnlyList<PodcastSearchResult> MapSearchResults(
        IEnumerable<SearchResultDto> source) =>
        source
            .Where(result =>
                result.CollectionId > 0 &&
                !string.IsNullOrWhiteSpace(result.CollectionName) &&
                !string.IsNullOrWhiteSpace(result.FeedUrl))
            .Select(result =>
            {
                var genres = result.Genres?
                    .Where(genre =>
                        !string.IsNullOrWhiteSpace(genre) &&
                        !string.Equals(genre, "Podcasts", StringComparison.OrdinalIgnoreCase))
                    .Select(genre => genre.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? [];
                return new PodcastSearchResult(
                    result.CollectionId,
                    result.CollectionName!.Trim(),
                    NullIfWhiteSpace(result.ArtistName),
                    result.FeedUrl!.Trim(),
                    NullIfWhiteSpace(result.ArtworkUrl600 ?? result.ArtworkUrl100),
                    genres.FirstOrDefault(),
                    genres,
                    result.GenreIds?
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.Ordinal)
                        .ToList() ?? [],
                    null);
            })
            .GroupBy(result => result.CollectionId)
            .Select(group => group.First())
            .ToList();

    public async Task<string?> GetFeedLanguageAsync(
        string feedUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await HttpClient.GetAsync(
                feedUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            var root = document.Root;
            var container = root?.Elements().FirstOrDefault(element =>
                                element.Name.LocalName is "channel" or "feed")
                            ?? root;
            return NormalizeLanguage(
                container?.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "language")
                    ?.Value ??
                container?.Attribute(XNamespace.Xml + "lang")?.Value);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Xml.XmlException or IOException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<PodcastCategory>> GetCategoryCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var country = GetStoreCountry().ToLowerInvariant();
        using var document = await HttpClient.GetFromJsonAsync<JsonDocument>(
            $"https://itunes.apple.com/WebObjects/MZStoreServices.woa/ws/genres?id=26&cc={country}",
            cancellationToken);
        if (document is null ||
            !document.RootElement.TryGetProperty("26", out var podcasts))
            return [];

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectGenreNames(podcasts, result);
        return result
            .Where(item => item.Key != "26")
            .Select(item => new PodcastCategory(item.Key, item.Value))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetPopularFeedUrlsAsync(
        CancellationToken cancellationToken = default)
    {
        var ids = await GetPopularPodcastIdsAsync(cancellationToken);
        if (ids.Count == 0)
            return [];

        var country = GetStoreCountry().ToLowerInvariant();
        var response = await HttpClient.GetFromJsonAsync<SearchResponse>(
            $"https://itunes.apple.com/lookup?entity=podcast&country={country}&id={string.Join(",", ids)}",
            cancellationToken);
        return response?.Results
            .Select(result => NullIfWhiteSpace(result.FeedUrl))
            .Where(url => url is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
    }

    public async Task<PodcastFeed> GetFeedAsync(
        string feedUrl,
        CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync(feedUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

        var episodes = document.Descendants()
            .Where(element => element.Name.LocalName is "item" or "entry")
            .Select(ParseEpisode)
            .Where(episode => episode is not null)
            .Cast<PodcastEpisode>()
            .OrderByDescending(episode => episode.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();
        var root = document.Root;
        var container = root?.Elements().FirstOrDefault(element =>
                            element.Name.LocalName is "channel" or "feed")
                        ?? root;
        var categories = container?.Elements()
            .Where(element => element.Name.LocalName == "category")
            .Select(element =>
                element.Attribute("text")?.Value ??
                element.Attribute("term")?.Value ??
                element.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var website = container?.Elements()
            .Where(element => element.Name.LocalName == "link")
            .Select(element =>
                element.Attribute("href")?.Value ??
                (!string.IsNullOrWhiteSpace(element.Value) ? element.Value : null))
            .FirstOrDefault(value => Uri.TryCreate(value, UriKind.Absolute, out _));
        return new PodcastFeed(
            episodes,
            NullIfWhiteSpace(
                ChildValue(container, "description") ??
                ChildValue(container, "subtitle") ??
                ChildValue(container, "summary")),
            NormalizeLanguage(
                ChildValue(container, "language") ??
                container?.Attribute(XNamespace.Xml + "lang")?.Value),
            categories,
            NullIfWhiteSpace(website),
            NullIfWhiteSpace(ChildValue(container, "copyright")));
    }

    private static PodcastEpisode? ParseEpisode(XElement item)
    {
        var title = ChildValue(item, "title");
        var enclosure = item.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "enclosure")
            ?.Attribute("url")?.Value;
        var link = item.Elements()
            .Where(element => element.Name.LocalName == "link")
            .FirstOrDefault(element =>
                string.Equals(element.Attribute("rel")?.Value, "enclosure", StringComparison.OrdinalIgnoreCase) ||
                (element.Attribute("type")?.Value?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ?? false))
            ?.Attribute("href")?.Value;
        var audioUrl = NullIfWhiteSpace(enclosure ?? link);
        if (string.IsNullOrWhiteSpace(title) || audioUrl is null)
            return null;

        var episodeKey = ChildValue(item, "guid") ??
                         item.Elements().FirstOrDefault(element => element.Name.LocalName == "id")?.Value ??
                         audioUrl;
        var description = ChildValue(item, "description") ??
                          ChildValue(item, "summary") ??
                          ChildValue(item, "content");
        var dateText = ChildValue(item, "pubDate") ??
                       ChildValue(item, "published") ??
                       ChildValue(item, "updated");
        DateTimeOffset? publishedAt = DateTimeOffset.TryParse(dateText, out var date)
            ? date
            : null;
        return new PodcastEpisode(
            episodeKey.Trim(),
            title.Trim(),
            audioUrl,
            NullIfWhiteSpace(description),
            publishedAt,
            ParseDuration(ChildValue(item, "duration")));
    }

    private static string? ChildValue(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim().Replace('_', '-');
        try
        {
            return System.Globalization.CultureInfo
                .GetCultureInfo(normalized)
                .TwoLetterISOLanguageName
                .ToLowerInvariant();
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            var primary = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
            return primary.Length is 2 or 3 ? primary.ToLowerInvariant() : null;
        }
    }

    private static void CollectGenreNames(
        JsonElement genre,
        Dictionary<string, string> result)
    {
        if (genre.TryGetProperty("id", out var id) &&
            genre.TryGetProperty("name", out var name))
        {
            var key = id.GetString();
            var value = name.GetString();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                result[key] = value.Trim();
        }
        if (!genre.TryGetProperty("subgenres", out var subgenres) ||
            subgenres.ValueKind != JsonValueKind.Object)
            return;
        foreach (var property in subgenres.EnumerateObject())
            CollectGenreNames(property.Value, result);
    }

    private static async Task<IReadOnlyList<string>> GetPopularPodcastIdsAsync(
        CancellationToken cancellationToken)
    {
        var country = GetStoreCountry().ToLowerInvariant();
        using var document = await HttpClient.GetFromJsonAsync<JsonDocument>(
            $"https://rss.marketingtools.apple.com/api/v2/{country}/podcasts/top/100/podcasts.json",
            cancellationToken);
        if (document is null ||
            !document.RootElement.TryGetProperty("feed", out var feed) ||
            !feed.TryGetProperty("results", out var entries))
            return [];
        return entries.EnumerateArray()
            .Select(entry =>
                entry.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Take(100)
            .ToList();
    }

    private static string GetStoreCountry()
    {
        try
        {
            return new System.Globalization.RegionInfo(
                    System.Globalization.CultureInfo.CurrentCulture.Name)
                .TwoLetterISORegionName;
        }
        catch (ArgumentException)
        {
            return "US";
        }
    }

    private static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (TimeSpan.TryParse(value.Trim(), out var duration))
            return duration;
        return double.TryParse(value.Trim(), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0");
        return client;
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("results")]
        public List<SearchResultDto> Results { get; init; } = [];
    }

    private sealed class SearchResultDto
    {
        [JsonPropertyName("collectionId")]
        public long CollectionId { get; init; }

        [JsonPropertyName("collectionName")]
        public string? CollectionName { get; init; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; init; }

        [JsonPropertyName("feedUrl")]
        public string? FeedUrl { get; init; }

        [JsonPropertyName("artworkUrl100")]
        public string? ArtworkUrl100 { get; init; }

        [JsonPropertyName("artworkUrl600")]
        public string? ArtworkUrl600 { get; init; }

        [JsonPropertyName("genres")]
        public List<string>? Genres { get; init; }

        [JsonPropertyName("genreIds")]
        public List<string>? GenreIds { get; init; }
    }
}
