using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace Orynivo.Library;

/// <summary>A cover-art search result from MusicBrainz and the Cover Art Archive.</summary>
/// <param name="ReleaseId">MusicBrainz release UUID.</param>
/// <param name="Title">Album title as returned by MusicBrainz.</param>
/// <param name="Artist">First credited artist, or <see langword="null"/> if unavailable.</param>
/// <param name="ImageData">Preview bytes during search; original bytes after explicit download.</param>
/// <param name="MimeType">MIME type reported by the Cover Art Archive response.</param>
public sealed record CoverSearchResult(string ReleaseId, string Title, string? Artist, byte[] ImageData, string? MimeType);

/// <summary>
/// Searches MusicBrainz by album title and optional artist, then downloads front-cover images from the Cover Art Archive.
/// </summary>
public static class MusicBrainzCoverSearch
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0 (album-artwork-search)");
        return client;
    }
    private static readonly Regex NonAlphanumericCharacters = new(
        @"[^\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PunctuationCharacters = new(
        @"[^\p{L}\p{N}\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Queries MusicBrainz for releases matching <paramref name="albumTitle"/> and, when provided,
    /// <paramref name="artistName"/>, then fetches up to 12 front-cover images from the Cover Art
    /// Archive as bounded 250-pixel previews. The primary query preserves punctuation inside quoted phrases (for stylised
    /// titles such as <c>M!ssundaztood</c>) and URL-encodes the complete query; a punctuation-
    /// compact fallback broadens matching when the exact phrase has no cover results.
    /// </summary>
    /// <param name="albumTitle">Album title to search for.</param>
    /// <param name="artistName">Optional artist name to narrow broad album titles.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onResult">Optional awaited callback for each completed preview; may run concurrently.</param>
    /// <returns>Matched releases for which a front cover was available.</returns>
    public static async Task<List<CoverSearchResult>> SearchByAlbumTitleAsync(
        string albumTitle,
        string? artistName = null,
        CancellationToken cancellationToken = default,
        Func<CoverSearchResult, Task>? onResult = null)
        => await SearchAsync(Client, albumTitle, artistName, onResult, cancellationToken).ConfigureAwait(false);

    /// <summary>Runs the bounded preview workflow with an injectable HTTP transport.</summary>
    /// <param name="client">Transport owned by the caller.</param>
    /// <param name="albumTitle">Album query.</param>
    /// <param name="artistName">Optional artist query.</param>
    /// <param name="onResult">Awaited concurrent preview callback.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>Successful previews; individual failures do not discard successes.</returns>
    internal static async Task<List<CoverSearchResult>> SearchAsync(HttpClient client, string albumTitle,
        string? artistName, Func<CoverSearchResult, Task>? onResult, CancellationToken cancellationToken)
    {
        var albumPhrase = EscapeQueryPhrase(albumTitle);
        if (albumPhrase.Length == 0)
            return [];
        var artistPhrase = EscapeQueryPhrase(artistName ?? string.Empty);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(35));
        var results = new ConcurrentBag<CoverSearchResult>();
        var seenReleaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var queryText in BuildQueryTexts(albumPhrase, artistPhrase))
            {
                await AddQueryResultsAsync(client, queryText, results, seenReleaseIds, onResult, budget.Token).ConfigureAwait(false);
                if (!results.IsEmpty)
                    break;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !results.IsEmpty) { }

        cancellationToken.ThrowIfCancellationRequested();
        return results.ToList();
    }

    /// <summary>Downloads the original only after the user selects a preview.</summary>
    /// <param name="preview">Selected preview and release identity.</param>
    /// <param name="cancellationToken">Cancellation, including dialog closure.</param>
    /// <returns>The selected result with original bytes, never a silent preview fallback.</returns>
    public static Task<CoverSearchResult> DownloadOriginalAsync(CoverSearchResult preview,
        CancellationToken cancellationToken = default) => DownloadOriginalAsync(Client, preview, cancellationToken);

    /// <summary>Downloads a selected original using an injectable transport.</summary>
    /// <param name="client">Caller-owned HTTP transport.</param>
    /// <param name="preview">Selected preview.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>Original image bytes and MIME type.</returns>
    internal static async Task<CoverSearchResult> DownloadOriginalAsync(HttpClient client, CoverSearchResult preview,
        CancellationToken cancellationToken)
    {
        var image = await DownloadAsync(client,
            $"https://coverartarchive.org/release/{Uri.EscapeDataString(preview.ReleaseId)}/front",
            64 * 1024 * 1024, TimeSpan.FromSeconds(45), cancellationToken).ConfigureAwait(false);
        return preview with { ImageData = image.Data, MimeType = image.Mime };
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
        ConcurrentBag<CoverSearchResult> results,
        HashSet<string> seenReleaseIds,
        Func<CoverSearchResult, Task>? onResult,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(queryText);
        var response = await DownloadAsync(client,
            $"https://musicbrainz.org/ws/2/release/?query={query}&fmt=json&limit=12",
            2 * 1024 * 1024, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);

        using var json = JsonDocument.Parse(response.Data);
        if (!json.RootElement.TryGetProperty("releases", out var releases))
            return;

        var candidates = new List<CoverSearchResult>();
        foreach (var release in releases.EnumerateArray().Take(12))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = release.GetProperty("id").GetString();
            var title = release.GetProperty("title").GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                continue;
            if (!seenReleaseIds.Add(id))
                continue;

            var artist = release.TryGetProperty("artist-credit", out var credits) && credits.GetArrayLength() > 0
                ? credits.EnumerateArray().FirstOrDefault().TryGetProperty("name", out var name)
                    ? name.GetString()
                    : null
                : null;

            candidates.Add(new CoverSearchResult(id, title, artist, [], null));
        }
        Exception? failure = null;
        await Parallel.ForEachAsync(candidates,
            new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cancellationToken },
            async (candidate, token) =>
            {
                try
                {
                    var image = await DownloadAsync(client,
                        $"https://coverartarchive.org/release/{Uri.EscapeDataString(candidate.ReleaseId)}/front-250",
                        2 * 1024 * 1024, TimeSpan.FromSeconds(8), token).ConfigureAwait(false);
                    var result = candidate with { ImageData = image.Data, MimeType = image.Mime };
                    if (onResult is not null)
                        await onResult(result).ConfigureAwait(false);
                    results.Add(result);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException ||
                    ex is OperationCanceledException && !token.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref failure, ex);
                }
            }).ConfigureAwait(false);
        if (results.IsEmpty && failure is not null)
            throw new HttpRequestException("Cover search failed.", failure);
    }

    private static async Task<(byte[] Data, string? Mime)> DownloadAsync(HttpClient client, string url,
        int limit, TimeSpan timeout, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                    deadline.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > limit)
                    throw new InvalidDataException("Cover response exceeds the size limit.");
                await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                using var output = new MemoryStream();
                var buffer = new byte[16384];
                int count;
                while ((count = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
                {
                    if (output.Length + count > limit)
                        throw new InvalidDataException("Cover response exceeds the size limit.");
                    output.Write(buffer, 0, count);
                }
                if (output.Length == 0)
                    throw new InvalidDataException("Empty cover response.");
                return (output.ToArray(), response.Content.Headers.ContentType?.MediaType);
            }
            catch (HttpRequestException ex) when (attempt == 0 &&
                (ex.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                 (int?)ex.StatusCode >= 500)) { }
            catch (OperationCanceledException) when (attempt == 0 && !cancellationToken.IsCancellationRequested) { }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
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
