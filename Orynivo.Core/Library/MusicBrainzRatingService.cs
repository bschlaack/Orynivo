using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Orynivo.Library;

/// <summary>Resolved MusicBrainz recording identity and community rating.</summary>
/// <param name="RecordingMbid">Stable MusicBrainz recording identifier.</param>
/// <param name="Rating">Community rating on a zero-to-five scale, or <see langword="null"/>.</param>
/// <param name="Votes">Number of votes contributing to the rating.</param>
public sealed record MusicBrainzTrackRating(string RecordingMbid, double? Rating, int Votes);

/// <summary>
/// Resolves recordings and retrieves MusicBrainz community ratings. Embedded recording MBIDs are
/// preferred; text fallback accepts only one exact title/artist match whose duration is compatible.
/// </summary>
public sealed class MusicBrainzRatingService
{
    private const string ApiBase = "https://musicbrainz.org/ws/2/";
    private const int BatchSize = 25;
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
            return await ResolveRecordingAsync(artist, title, durationSeconds, cancellationToken).ConfigureAwait(false);

        var uri = $"{ApiBase}recording/{mbid}?inc=ratings&fmt=json";
        var response = await GetAsync<RecordingLookup>(uri, cancellationToken).ConfigureAwait(false);
        return response is null
            ? null
            : new MusicBrainzTrackRating(mbid, response.Rating?.Value, response.Rating?.VotesCount ?? 0);
    }

    /// <summary>
    /// Gets cached community values for known recording MBIDs in bounded batch searches.
    /// Invalid identifiers and identifiers not returned by MusicBrainz are omitted.
    /// </summary>
    /// <param name="recordingMbids">Recording MBIDs to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ratings keyed by canonical recording MBID.</returns>
    public async Task<IReadOnlyDictionary<string, MusicBrainzTrackRating>> GetRatingsAsync(
        IEnumerable<string> recordingMbids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recordingMbids);
        var identifiers = recordingMbids
            .Select(value => Guid.TryParse(value, out var parsed) ? parsed.ToString() : null)
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new Dictionary<string, MusicBrainzTrackRating>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in identifiers.Chunk(BatchSize))
        {
            var query = $"rid:({string.Join(" OR ", batch)})";
            var uri = $"{ApiBase}recording?query={Uri.EscapeDataString(query)}&limit={batch.Length}&inc=ratings&fmt=json";
            var response = await GetAsync<RecordingSearch>(uri, cancellationToken).ConfigureAwait(false);
            foreach (var item in response?.Recordings ?? [])
            {
                if (!Guid.TryParse(item.Id, out var parsed))
                    continue;
                var mbid = parsed.ToString();
                result[mbid] = new MusicBrainzTrackRating(
                    mbid,
                    item.Rating?.Value,
                    item.Rating?.VotesCount ?? 0);
            }
        }
        return result;
    }

    private async Task<MusicBrainzTrackRating?> ResolveRecordingAsync(
        string? artist,
        string? title,
        double? durationSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            return null;

        var query = $"recording:\"{EscapeQuery(title)}\" AND artist:\"{EscapeQuery(artist)}\"";
        var uri = $"{ApiBase}recording?query={Uri.EscapeDataString(query)}&limit=10&inc=ratings&fmt=json";
        var response = await GetAsync<RecordingSearch>(uri, cancellationToken).ConfigureAwait(false);
        var matches = response?.Recordings
            .Where(item => string.Equals(item.Title?.Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => item.ArtistCredit.Any(credit =>
                string.Equals(credit.Name?.Trim(), artist.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(item => !durationSeconds.HasValue || !item.Length.HasValue ||
                Math.Abs(item.Length.Value / 1000d - durationSeconds.Value) <= 5d)
            .ToList() ?? [];
        if (matches.Count != 1 || !Guid.TryParse(matches[0].Id, out var mbid))
            return null;
        return new MusicBrainzTrackRating(
            mbid.ToString(),
            matches[0].Rating?.Value,
            matches[0].Rating?.VotesCount ?? 0);
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
        [property: JsonPropertyName("artist-credit")] List<ArtistCredit> ArtistCredit,
        [property: JsonPropertyName("rating")] RatingValue? Rating);

    private sealed record ArtistCredit([property: JsonPropertyName("name")] string? Name);

    private sealed record RecordingLookup([property: JsonPropertyName("rating")] RatingValue? Rating);

    private sealed record RatingValue(
        [property: JsonPropertyName("value")] double? Value,
        [property: JsonPropertyName("votes-count")] int VotesCount);
}
