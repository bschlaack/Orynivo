using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orynivo.Library;

/// <summary>A cover-art search result from MusicBrainz and the Cover Art Archive.</summary>
/// <param name="ReleaseId">MusicBrainz release UUID.</param>
/// <param name="Title">Album title as returned by MusicBrainz.</param>
/// <param name="Artist">First credited artist, or <see langword="null"/> if unavailable.</param>
/// <param name="ImageData">Raw image bytes downloaded from the Cover Art Archive.</param>
/// <param name="MimeType">MIME type reported by the Cover Art Archive response.</param>
public sealed record CoverSearchResult(string ReleaseId, string Title, string? Artist, byte[] ImageData, string? MimeType);

/// <summary>
/// Searches MusicBrainz by album title and optional artist, then downloads front-cover images from the Cover Art Archive.
/// </summary>
public static class MusicBrainzCoverSearch
{
    private static readonly Regex NonAlphanumericCharacters = new(
        @"[^\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PunctuationCharacters = new(
        @"[^\p{L}\p{N}\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Queries MusicBrainz for releases matching <paramref name="albumTitle"/> and, when provided,
    /// <paramref name="artistName"/>, then fetches up to 12 front-cover images from the Cover Art
    /// Archive. The primary query preserves punctuation inside quoted phrases (for stylised
    /// titles such as <c>M!ssundaztood</c>) and URL-encodes the complete query; a punctuation-
    /// compact fallback broadens matching when the exact phrase has no cover results.
    /// </summary>
    /// <param name="albumTitle">Album title to search for.</param>
    /// <param name="artistName">Optional artist name to narrow broad album titles.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matched releases for which a front cover was available.</returns>
    public static async Task<List<CoverSearchResult>> SearchByAlbumTitleAsync(
        string albumTitle,
        string? artistName = null,
        CancellationToken cancellationToken = default)
    {
        var albumPhrase = EscapeQueryPhrase(albumTitle);
        if (albumPhrase.Length == 0)
            return [];
        var artistPhrase = EscapeQueryPhrase(artistName ?? string.Empty);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0 (album-artwork-search)");

        var results = new List<CoverSearchResult>();
        var seenReleaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var queryText in BuildQueryTexts(albumPhrase, artistPhrase))
        {
            await AddQueryResultsAsync(client, queryText, results, seenReleaseIds, cancellationToken);
            if (results.Count > 0)
                break;
        }

        return results;
    }

    private static IEnumerable<string> BuildQueryTexts(string albumPhrase, string artistPhrase)
    {
        yield return BuildQueryText(albumPhrase, artistPhrase);

        var compactAlbum = CompactPunctuation(albumPhrase);
        var compactArtist = CompactPunctuation(artistPhrase);
        if (!string.Equals(compactAlbum, albumPhrase, StringComparison.Ordinal) ||
            !string.Equals(compactArtist, artistPhrase, StringComparison.Ordinal))
        {
            yield return BuildQueryText(compactAlbum, compactArtist);
        }

        var spacedAlbum = SpacePunctuation(albumPhrase);
        var spacedArtist = SpacePunctuation(artistPhrase);
        if ((!string.Equals(spacedAlbum, albumPhrase, StringComparison.Ordinal) ||
             !string.Equals(spacedArtist, artistPhrase, StringComparison.Ordinal)) &&
            (!string.Equals(spacedAlbum, compactAlbum, StringComparison.Ordinal) ||
             !string.Equals(spacedArtist, compactArtist, StringComparison.Ordinal)))
        {
            yield return BuildQueryText(spacedAlbum, spacedArtist);
        }
    }

    private static string BuildQueryText(string albumPhrase, string artistPhrase) =>
        string.IsNullOrWhiteSpace(artistPhrase)
            ? $"release:\"{albumPhrase}\""
            : $"release:\"{albumPhrase}\" AND artist:\"{artistPhrase}\"";

    private static async Task AddQueryResultsAsync(
        HttpClient client,
        string queryText,
        List<CoverSearchResult> results,
        HashSet<string> seenReleaseIds,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(queryText);
        using var response = await client.GetAsync(
            $"https://musicbrainz.org/ws/2/release/?query={query}&fmt=json&limit=12",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("releases", out var releases))
            return;

        foreach (var release in releases.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = release.GetProperty("id").GetString();
            var title = release.GetProperty("title").GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                continue;
            if (!seenReleaseIds.Add(id))
                continue;

            var artist = release.TryGetProperty("artist-credit", out var credits)
                ? credits.EnumerateArray().FirstOrDefault().TryGetProperty("name", out var name)
                    ? name.GetString()
                    : null
                : null;

            using var artResponse = await client.GetAsync(
                $"https://coverartarchive.org/release/{Uri.EscapeDataString(id)}/front",
                cancellationToken);
            if (artResponse.StatusCode == HttpStatusCode.NotFound)
                continue;
            artResponse.EnsureSuccessStatusCode();

            var data = await artResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            if (data.Length == 0)
                continue;

            results.Add(new CoverSearchResult(
                id,
                title,
                artist,
                data,
                artResponse.Content.Headers.ContentType?.MediaType));
        }
    }

    private static string EscapeQueryPhrase(string value) =>
        value.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string CompactPunctuation(string value) =>
        NonAlphanumericCharacters.Replace(PunctuationCharacters.Replace(value, string.Empty), " ").Trim();

    private static string SpacePunctuation(string value) =>
        NonAlphanumericCharacters.Replace(value, " ").Trim();
}
