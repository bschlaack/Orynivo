using System.Net;
using System.Text.Json;

namespace Orynivo.Library;

/// <summary>
/// Resolves artists through MusicBrainz and downloads curated artist thumbnails from Fanart.tv.
/// API keys are used only in authenticated Fanart.tv requests and are never included in diagnostics.
/// </summary>
public static class FanartTvArtistImageService
{
    private const int MaximumImageBytes = 12 * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim MusicBrainzThrottle = new(1, 1);
    private static DateTimeOffset _lastMusicBrainzRequest = DateTimeOffset.MinValue;

    /// <summary>
    /// Downloads the highest-rated Fanart.tv artist thumbnail.
    /// </summary>
    /// <param name="artistId">Local cache identifier used as the image filename.</param>
    /// <param name="artistName">Artist display name used for conservative MusicBrainz resolution.</param>
    /// <param name="musicBrainzArtistId">Known MusicBrainz artist ID, or <see langword="null"/> to resolve by name.</param>
    /// <param name="apiKey">Fanart.tv personal API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached image path, or <see langword="null"/> when no unambiguous image is available.</returns>
    public static async Task<string?> DownloadBestAsync(
        long artistId,
        string artistName,
        string? musicBrainzArtistId,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(artistName))
            return null;

        var mbid = NormalizeMbid(musicBrainzArtistId)
                   ?? await ResolveMusicBrainzArtistIdAsync(artistName, cancellationToken);
        if (mbid is null)
            return null;

        var endpoint =
            $"https://webservice.fanart.tv/v3.2/music/{Uri.EscapeDataString(mbid)}" +
            $"?api_key={Uri.EscapeDataString(apiKey.Trim())}";
        using var response = await Client.GetAsync(endpoint, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests
            or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var imageUrl = SelectBestArtistThumbnailUrl(document.RootElement);
        if (imageUrl is null)
            return null;

        using var imageResponse = await Client.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!imageResponse.IsSuccessStatusCode ||
            imageResponse.Content.Headers.ContentLength is > MaximumImageBytes)
        {
            return null;
        }

        var mediaType = imageResponse.Content.Headers.ContentType?.MediaType;
        if (mediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            return null;

        var imageData = await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        if (imageData.Length is 0 or > MaximumImageBytes)
            return null;

        return await ArtistImageSearchService.SaveImageAsync(
            artistId,
            imageData,
            mediaType,
            cancellationToken);
    }

    internal static string? SelectBestArtistThumbnailUrl(JsonElement root)
    {
        if (!root.TryGetProperty("artistthumb", out var thumbnails) ||
            thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return thumbnails.EnumerateArray()
            .Select(image => new
            {
                Url = image.TryGetProperty("url", out var url) ? url.GetString() : null,
                Likes = ReadInteger(image, "likes"),
                Width = ReadInteger(image, "width"),
                Height = ReadInteger(image, "height")
            })
            .Where(image =>
                Uri.TryCreate(image.Url, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps)
            .OrderByDescending(image => image.Likes)
            .ThenBy(image => Math.Abs(image.Width - image.Height))
            .ThenByDescending(image => image.Width * image.Height)
            .Select(image => image.Url)
            .FirstOrDefault();
    }

    private static async Task<string?> ResolveMusicBrainzArtistIdAsync(
        string artistName,
        CancellationToken cancellationToken)
    {
        await ThrottleMusicBrainzAsync(cancellationToken);
        var escapedName = EscapeLucenePhrase(artistName.Trim());
        var endpoint =
            "https://musicbrainz.org/ws/2/artist/" +
            $"?query={Uri.EscapeDataString($"artist:\"{escapedName}\"")}&limit=10&fmt=json";
        using var response = await Client.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("artists", out var artists) ||
            artists.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(artistName);
        var exactMatches = artists.EnumerateArray()
            .Where(candidate =>
                candidate.TryGetProperty("name", out var name) &&
                ArtistNameNormalizer.CreateComparisonKey(name.GetString()) == comparisonKey &&
                ReadInteger(candidate, "score") >= 95)
            .Select(candidate => candidate.TryGetProperty("id", out var id) ? NormalizeMbid(id.GetString()) : null)
            .Where(id => id is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return exactMatches.Count == 1 ? exactMatches[0] : null;
    }

    private static async Task ThrottleMusicBrainzAsync(CancellationToken cancellationToken)
    {
        await MusicBrainzThrottle.WaitAsync(cancellationToken);
        try
        {
            var delay = _lastMusicBrainzRequest.AddMilliseconds(1100) - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _lastMusicBrainzRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            MusicBrainzThrottle.Release();
        }
    }

    private static int ReadInteger(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;
        return property.ValueKind == JsonValueKind.String &&
               int.TryParse(property.GetString(), out number)
            ? number
            : 0;
    }

    private static string EscapeLucenePhrase(string value)
    {
        const string specialCharacters = @"+-&|!(){}[]^""~*?:\/";
        var result = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (specialCharacters.Contains(character))
                result.Append('\\');
            result.Append(character);
        }
        return result.ToString();
    }

    private static string? NormalizeMbid(string? value) =>
        Guid.TryParse(value?.Trim(), out var id) ? id.ToString() : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Orynivo/1.0 (music library; artist artwork)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}
