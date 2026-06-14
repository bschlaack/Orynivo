using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Orynivo.Library;

public sealed record CoverSearchResult(string ReleaseId, string Title, string? Artist, byte[] ImageData, string? MimeType);

public static class MusicBrainzCoverSearch
{
    public static async Task<List<CoverSearchResult>> SearchByAlbumTitleAsync(
        string albumTitle,
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0 (album-artwork-search)");

        var query = Uri.EscapeDataString($"release:\"{albumTitle}\"");
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
}
