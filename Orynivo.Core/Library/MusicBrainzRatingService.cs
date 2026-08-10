using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orynivo.Library;

/// <summary>Resolved MusicBrainz recording identity and community rating.</summary>
/// <param name="RecordingMbid">Stable MusicBrainz recording identifier.</param>
/// <param name="Rating">Community rating on a zero-to-five scale, or <see langword="null"/>.</param>
/// <param name="Votes">Number of votes contributing to the rating.</param>
/// <param name="Genres">Curated MusicBrainz genres with positive community counts.</param>
/// <param name="Tags">Community tags with at least two positive votes.</param>
public sealed record MusicBrainzTrackRating(
    string RecordingMbid,
    double? Rating,
    int Votes,
    IReadOnlyList<string>? Genres = null,
    IReadOnlyList<string>? Tags = null);

/// <summary>Serializes and combines supplemental MusicBrainz genre metadata.</summary>
public static class MusicBrainzGenreMetadata
{
    /// <summary>Serializes bounded normalized names for database storage.</summary>
    /// <param name="values">Genre or tag names.</param>
    /// <returns>A JSON array, or <see langword="null"/> when no names remain.</returns>
    public static string? Serialize(IEnumerable<string>? values)
    {
        var normalized = Normalize(values).ToList();
        return normalized.Count == 0 ? null : JsonSerializer.Serialize(normalized);
    }

    /// <summary>Combines embedded genres with separately stored MusicBrainz genres and tags.</summary>
    /// <param name="embeddedGenre">Unmodified embedded genre text.</param>
    /// <param name="genresJson">Stored MusicBrainz genre JSON.</param>
    /// <param name="tagsJson">Stored MusicBrainz tag JSON.</param>
    /// <returns>Comma-separated effective genre text for search and classification.</returns>
    public static string? Combine(string? embeddedGenre, string? genresJson, string? tagsJson)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(embeddedGenre))
            values.Add(embeddedGenre.Trim());
        values.AddRange(Deserialize(genresJson));
        values.AddRange(Deserialize(tagsJson));
        var normalized = Normalize(values).ToList();
        return normalized.Count == 0 ? null : string.Join(", ", normalized);
    }

    private static IEnumerable<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static IEnumerable<string> Normalize(IEnumerable<string>? values) =>
        (values ?? [])
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32);
}

/// <summary>
/// Resolves recordings and retrieves MusicBrainz community ratings. Embedded recording MBIDs are
/// preferred; text fallback accepts only one exact title/artist match whose duration is compatible.
/// </summary>
public sealed class MusicBrainzRatingService
{
    private const string ApiBase = "https://musicbrainz.org/ws/2/";
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private readonly HttpClient _httpClient;

    /// <summary>Initializes a MusicBrainz rating client.</summary>
    /// <param name="httpClient">HTTP client used for MusicBrainz requests.</param>
    public MusicBrainzRatingService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0 (https://orynivo.app)");
    }

    /// <summary>Gets a community rating by MBID or a conservative metadata fallback.</summary>
    /// <param name="recordingMbid">Known MusicBrainz recording identifier, when available.</param>
    /// <param name="artist">Track artist used only for fallback matching.</param>
    /// <param name="title">Track title used only for fallback matching.</param>
    /// <param name="durationSeconds">Optional duration used to reject other versions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved rating, or <see langword="null"/> when no unambiguous recording exists.</returns>
    public async Task<MusicBrainzTrackRating?> GetRatingAsync(
        string? recordingMbid,
        string? artist,
        string? title,
        double? durationSeconds,
        CancellationToken cancellationToken = default)
    {
        var mbid = Guid.TryParse(recordingMbid, out var parsed) ? parsed.ToString() : null;
        if (mbid is null)
            mbid = await ResolveRecordingAsync(artist, title, durationSeconds, cancellationToken).ConfigureAwait(false);
        if (mbid is null)
            return null;

        var uri = $"{ApiBase}recording/{mbid}?inc=ratings+genres+tags&fmt=json";
        var response = await GetAsync<RecordingLookup>(uri, cancellationToken).ConfigureAwait(false);
        return response is null
            ? null
            : new MusicBrainzTrackRating(
                mbid,
                response.Rating?.Value,
                response.Rating?.VotesCount ?? 0,
                SelectNames(response.Genres, 1),
                SelectNames(response.Tags, 2));
    }

    private async Task<string?> ResolveRecordingAsync(
        string? artist,
        string? title,
        double? durationSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            return null;

        var query = $"recording:\"{EscapeQuery(title)}\" AND artist:\"{EscapeQuery(artist)}\"";
        var uri = $"{ApiBase}recording?query={Uri.EscapeDataString(query)}&limit=10&fmt=json";
        var response = await GetAsync<RecordingSearch>(uri, cancellationToken).ConfigureAwait(false);
        var matches = response?.Recordings
            .Where(item => string.Equals(item.Title?.Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => item.ArtistCredit.Any(credit =>
                string.Equals(credit.Name?.Trim(), artist.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(item => !durationSeconds.HasValue || !item.Length.HasValue ||
                Math.Abs(item.Length.Value / 1000d - durationSeconds.Value) <= 5d)
            .ToList() ?? [];
        return matches.Count == 1 && Guid.TryParse(matches[0].Id, out var mbid)
            ? mbid.ToString()
            : null;
    }

    private static string EscapeQuery(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private async Task<T?> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        await RequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var remaining = TimeSpan.FromMilliseconds(1100) - (DateTimeOffset.UtcNow - _lastRequestAt);
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            _lastRequestAt = DateTimeOffset.UtcNow;
            return await _httpClient.GetFromJsonAsync<T>(uri, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private sealed record RecordingSearch(
        [property: JsonPropertyName("recordings")] List<RecordingSearchItem> Recordings);

    private sealed record RecordingSearchItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("length")] int? Length,
        [property: JsonPropertyName("artist-credit")] List<ArtistCredit> ArtistCredit);

    private sealed record ArtistCredit([property: JsonPropertyName("name")] string? Name);

    private static IReadOnlyList<string> SelectNames(IEnumerable<TagValue>? values, int minimumCount) =>
        (values ?? [])
            .Where(value => value.Count >= minimumCount)
            .Select(value => value.Name?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();

    private sealed record RecordingLookup(
        [property: JsonPropertyName("rating")] RatingValue? Rating,
        [property: JsonPropertyName("genres")] List<TagValue>? Genres,
        [property: JsonPropertyName("tags")] List<TagValue>? Tags);

    private sealed record TagValue(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("count")] int Count);

    private sealed record RatingValue(
        [property: JsonPropertyName("value")] double? Value,
        [property: JsonPropertyName("votes-count")] int VotesCount);
}
