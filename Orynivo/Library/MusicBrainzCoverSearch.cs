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
/// Searches MusicBrainz by album title and downloads front-cover images from the Cover Art Archive.
/// </summary>
public static class MusicBrainzCoverSearch
{
    private static readonly Regex NonAlphanumericCharacters = new(
        @"[^\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Queries MusicBrainz for releases matching <paramref name="albumTitle"/> and fetches up to
    /// 12 front-cover images from the Cover Art Archive. Characters other than Unicode letters
    /// and numbers are replaced with spaces before the query is submitted.
    /// </summary>
    /// <param name="albumTitle">Album title to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matched releases for which a front cover was available.</returns>
    public static async Task<List<CoverSearchResult>> SearchByAlbumTitleAsync(
        string albumTitle,
        CancellationToken cancellationToken = default)
    {
        var sanitizedAlbumTitle = SanitizeAlbumTitle(albumTitle);
        if (sanitizedAlbumTitle.Length == 0)
            return [];

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0 (album-artwork-search)");

        var query = Uri.EscapeDataString($"release:\"{sanitizedAlbumTitle}\"");
        using var response = await client.GetAsync(
            $"https://musicbrainz.org/ws/2/release/?query={query}&fmt=json&limit=12",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var results = new List<CoverSearchResult>();
        if (!json.RootElement.TryGetProperty("releases", out var releases))
            return results;

        foreach (var release in releases.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = release.GetProperty("id").GetString();
            var title = release.GetProperty("title").GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
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

        return results;
    }

    private static string SanitizeAlbumTitle(string albumTitle) =>
        NonAlphanumericCharacters.Replace(albumTitle, " ").Trim();
}
