using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orynivo;

namespace Orynivo.Library;

public sealed record ArtistProfileDownload(
    string Biography,
    string? ImagePath,
    string SourceUrl,
    string Language);

public static class ArtistProfileService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public static ArtistInfoSource Source { get; set; } = ArtistInfoSource.Wikipedia;
    public static string? LastFmApiKey { get; set; }
    public static string? LastImageDiagnostic { get; private set; }

    public static async Task<ArtistProfileDownload?> DownloadAsync(
        long artistId,
        string artistName,
        string language,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistName))
            return null;

        language = language is "de" or "fr" ? language : "en";

        if (Source == ArtistInfoSource.LastFm)
        {
            var result = await DownloadFromLastFmAsync(artistId, artistName, language, cancellationToken);
            if (result is not null)
                return result;
            // Last.fm hat den Künstler nicht gefunden ("+nodirect", leere Bio, etc.) → Wikipedia
            return await DownloadFromWikipediaAsync(artistId, artistName, language, cancellationToken);
        }

        return await DownloadFromWikipediaAsync(artistId, artistName, language, cancellationToken);
    }

    private static async Task<ArtistProfileDownload?> DownloadFromWikipediaAsync(
        long artistId,
        string artistName,
        string language,
        CancellationToken cancellationToken)
    {
        var musicTerm = language switch
        {
            "de" => "Musiker",
            "fr" => "musicien",
            _ => "musician"
        };

        var page = await SearchPageAsync(language, $"{artistName} {musicTerm}", artistName, cancellationToken)
                   ?? await SearchPageAsync(language, $"\"{artistName}\"", artistName, cancellationToken);
        if (page is null)
            return null;

        var biography = page.Value.GetProperty("extract").GetString()?.Trim();
        var sourceUrl = page.Value.TryGetProperty("fullurl", out var fullUrl)
            ? fullUrl.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(biography) || string.IsNullOrWhiteSpace(sourceUrl))
            return null;

        var imageUrl = GetImageUrl(page.Value);
        var imagePath = imageUrl is null
            ? null
            : await DownloadImageAsync(artistId, imageUrl, cancellationToken);
        return new ArtistProfileDownload(biography, imagePath, sourceUrl, language);
    }

    private static async Task<ArtistProfileDownload?> DownloadFromLastFmAsync(
        long artistId,
        string artistName,
        string language,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(LastFmApiKey))
            return null;

        var name = Uri.EscapeDataString(artistName);
        var uri =
            $"https://ws.audioscrobbler.com/2.0/?method=artist.getinfo" +
            $"&artist={name}&api_key={LastFmApiKey}&format=json&lang={language}";

        await ThrottleAsync(cancellationToken);

        using var response = await Client.GetAsync(uri, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonSerializer.DeserializeAsync<JsonElement>(
            stream, cancellationToken: cancellationToken);

        if (!document.TryGetProperty("artist", out var artistEl))
            return null;

        string? biography = null;
        if (artistEl.TryGetProperty("bio", out var bio))
        {
            if (bio.TryGetProperty("content", out var content))
                biography = CleanLastFmBiography(content.GetString());
            if (string.IsNullOrWhiteSpace(biography) &&
                bio.TryGetProperty("summary", out var summary))
                biography = CleanLastFmBiography(summary.GetString());
        }
        if (string.IsNullOrWhiteSpace(biography))
            return null;

        var sourceUrl = artistEl.TryGetProperty("url", out var urlEl)
            ? urlEl.GetString()
            : null;
        // "+nodirect" signalisiert, dass Last.fm den Künstler nicht gefunden hat.
        if (string.IsNullOrWhiteSpace(sourceUrl) ||
            sourceUrl.Contains("+nodirect", StringComparison.OrdinalIgnoreCase))
            return null;

        LastImageDiagnostic = null;
        string? imagePath = null;
        if (artistEl.TryGetProperty("image", out var images))
        {
            var preference = new[] { "mega", "extralarge", "large", "medium", "small" };
            var bySize = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var img in images.EnumerateArray())
            {
                if (!img.TryGetProperty("size", out var sz)) continue;
                var s = sz.GetString();
                if (s is null) continue;
                if (!img.TryGetProperty("#text", out var t)) continue;
                var url = t.GetString();
                // Skip empty strings and the well-known Last.fm placeholder hash
                if (string.IsNullOrWhiteSpace(url) ||
                    url.Contains("2a96cbd8b46e442fc41c2b86b821562f"))
                    continue;
                bySize.TryAdd(s, url);
            }

            if (bySize.Count == 0)
            {
                LastImageDiagnostic = "Last.fm: keine Bilder (API deprecated seit 2019)";
            }
            else
            {
                string? imageUrl = null;
                foreach (var pref in preference)
                    if (bySize.TryGetValue(pref, out imageUrl)) break;
                if (imageUrl is not null)
                {
                    try
                    {
                        imagePath = await DownloadImageAsync(artistId, imageUrl, cancellationToken);
                        // DownloadImageAsync sets LastImageDiagnostic on failure; leave it as-is.
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { LastImageDiagnostic = $"Last.fm CDN: {ex.GetType().Name}: {ex.Message}"; }
                }
            }
        }
        else
        {
            LastImageDiagnostic = "Last.fm: kein 'image'-Feld in der API-Antwort";
        }

        // Last.fm deprecated community images in 2019 – fall back to Wikipedia for the image.
        // Try the UI language first, then English (English Wikipedia has the best coverage).
        if (imagePath is null)
        {
            try
            {
                var languagesToTry = language == "en"
                    ? new[] { "en" }
                    : new[] { language, "en" };
                foreach (var lang in languagesToTry)
                {
                    var musicTerm = lang switch { "de" => "Musiker", "fr" => "musicien", _ => "musician" };
                    var wikiPage = await SearchPageAsync(lang, $"{artistName} {musicTerm}", artistName, cancellationToken);
                    if (wikiPage is null)
                        wikiPage = await SearchPageAsync(lang, $"\"{artistName}\"", artistName, cancellationToken);
                    if (wikiPage is null)
                    {
                        LastImageDiagnostic = $"Wikipedia ({lang}): keine Seite für '{artistName}' gefunden";
                        continue;
                    }
                    var wikiImageUrl = GetImageUrl(wikiPage.Value);
                    if (wikiImageUrl is null)
                    {
                        LastImageDiagnostic = $"Wikipedia ({lang}): Seite gefunden, kein Bild-URL in Antwort";
                        continue;
                    }
                    imagePath = await DownloadImageAsync(artistId, wikiImageUrl, cancellationToken);
                    if (imagePath is null)
                    {
                        // DownloadImageAsync already set LastImageDiagnostic with the real reason.
                        continue;
                    }
                    LastImageDiagnostic = null;
                    break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { LastImageDiagnostic = $"Wikipedia Fallback: {ex.GetType().Name}: {ex.Message}"; }
        }

        return new ArtistProfileDownload(biography, imagePath, sourceUrl, language);
    }

    private static async Task<JsonElement?> SearchPageAsync(
        string language,
        string gsrSearch,
        string? preferTitle,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(gsrSearch);
        var uri =
            $"https://{language}.wikipedia.org/w/api.php" +
            $"?action=query&generator=search&gsrsearch={query}&gsrnamespace=0&gsrlimit=5" +
            "&prop=extracts%7Cpageimages%7Cinfo&exintro=1&explaintext=1" +
            "&piprop=original%7Cthumbnail&pithumbsize=1000&inprop=url" +
            "&format=json&formatversion=2&origin=*";

        await ThrottleAsync(cancellationToken);

        using var response = await Client.GetAsync(uri, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonSerializer.DeserializeAsync<JsonElement>(
            stream,
            cancellationToken: cancellationToken);
        if (!document.TryGetProperty("query", out var queryElement) ||
            !queryElement.TryGetProperty("pages", out var pagesElement))
        {
            return null;
        }

        var page = pagesElement.EnumerateArray()
            .Where(candidate =>
            {
                if (!candidate.TryGetProperty("extract", out var extract) ||
                    string.IsNullOrWhiteSpace(extract.GetString()))
                    return false;
                if (preferTitle is null)
                    return true;
                // Kandidaten ohne jeglichen Titelbezug verwerfen (z.B. Julian Lennon für "Toy Matinee").
                var title = candidate.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                return string.Equals(title, preferTitle, StringComparison.OrdinalIgnoreCase)
                    || title.StartsWith(preferTitle + " (", StringComparison.OrdinalIgnoreCase)
                    || title.StartsWith(preferTitle + " ", StringComparison.OrdinalIgnoreCase)
                    || title.Contains(preferTitle, StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(candidate =>
            {
                bool hasImage = candidate.TryGetProperty("thumbnail", out _) ||
                                candidate.TryGetProperty("original", out _);
                if (preferTitle is null)
                    return hasImage ? 1 : 0;
                var title = candidate.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                // Exact match ("Adele") outranks disambiguation ("Adele (singer)")
                // which outranks same-prefix ("Adele Girard") which outranks unrelated.
                int titleScore = string.Equals(title, preferTitle, StringComparison.OrdinalIgnoreCase) ? 4
                    : title.StartsWith(preferTitle + " (", StringComparison.OrdinalIgnoreCase) ? 3
                    : title.StartsWith(preferTitle + " ", StringComparison.OrdinalIgnoreCase) ? 2
                    : title.Contains(preferTitle, StringComparison.OrdinalIgnoreCase) ? 1
                    : 0;
                return titleScore * 2 + (hasImage ? 1 : 0);
            })
            .FirstOrDefault();
        return page.ValueKind == JsonValueKind.Undefined ? null : page;
    }

    private static async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            var wait = _lastRequest.AddMilliseconds(1500) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);
            _lastRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private static string? GetImageUrl(JsonElement page)
    {
        if (page.TryGetProperty("original", out var original) &&
            original.TryGetProperty("source", out var originalSource))
        {
            return originalSource.GetString();
        }
        if (page.TryGetProperty("thumbnail", out var thumbnail) &&
            thumbnail.TryGetProperty("source", out var thumbnailSource))
        {
            return thumbnailSource.GetString();
        }
        return null;
    }

    private static async Task<string?> DownloadImageAsync(
        long artistId,
        string imageUrl,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        // Wikimedia CDN erwartet einen Referer, der auf eine Wikipedia-Seite zeigt.
        request.Headers.Referrer = new Uri("https://en.wikipedia.org/");
        using var response = await Client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LastImageDiagnostic = $"Bild-Download HTTP {(int)response.StatusCode}: {imageUrl[..Math.Min(80, imageUrl.Length)]}";
            return null;
        }
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        string extension;
        if (mediaType == "image/png") extension = ".png";
        else if (mediaType == "image/webp") extension = ".webp";
        else if (mediaType?.StartsWith("image/") == true) extension = ".jpg";
        else
        {
            // Content-Type fehlt oder ist generisch (z.B. application/octet-stream bei Wikimedia CDN).
            // Dateiendung aus der URL ableiten.
            var urlPath = imageUrl.Split('?', '#')[0];
            var ext = Path.GetExtension(urlPath).ToLowerInvariant();
            extension = ext switch { ".png" => ".png", ".webp" => ".webp", ".jpeg" => ".jpg", ".jpg" => ".jpg", _ => "" };
            if (extension.Length == 0)
            {
                LastImageDiagnostic = $"Bild-Download: unbekannter Content-Type '{mediaType}', keine Bildendung in URL";
                return null;
            }
        }
        var directory = AppPaths.GetDataPath("artist-images");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, artistId + extension);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(output, cancellationToken);
        return path;
    }

    private static string? CleanLastFmBiography(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        // "Read more on Last.fm"-Link am Ende entfernen, dann alle übrigen Tags
        var text = Regex.Replace(raw,
            @"<a\s[^>]*>\s*Read more on Last\.fm\s*</a>",
            "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        text = text.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                   .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&nbsp;", " ");
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Orynivo/1.0 (Windows music player; artist metadata)");
        return client;
    }
}
