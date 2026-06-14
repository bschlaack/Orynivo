using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orynivo.Library;

public sealed record ArtistImageSearchResult(
    string Title,
    string? Attribution,
    string? License,
    string SourceUrl,
    byte[] ImageData,
    string? MimeType);

public static class ArtistImageSearchService
{
    private static readonly HttpClient Client = CreateClient();

    public static async Task<List<ArtistImageSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var encodedQuery = Uri.EscapeDataString(query.Trim());
        var uri =
            "https://commons.wikimedia.org/w/api.php" +
            $"?action=query&generator=search&gsrsearch={encodedQuery}" +
            "&gsrnamespace=6&gsrlimit=12&prop=imageinfo" +
            "&iiprop=url%7Cmime%7Cextmetadata&iiurlwidth=600" +
            "&format=json&formatversion=2&origin=*";

        using var response = await Client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("query", out var queryElement) ||
            !queryElement.TryGetProperty("pages", out var pagesElement))
        {
            return [];
        }

        var results = new List<ArtistImageSearchResult>();
        foreach (var page in pagesElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!page.TryGetProperty("imageinfo", out var imageInfoArray))
                continue;

            var imageInfo = imageInfoArray.EnumerateArray().FirstOrDefault();
            if (imageInfo.ValueKind == JsonValueKind.Undefined ||
                !imageInfo.TryGetProperty("thumburl", out var thumbnailUrlElement))
            {
                continue;
            }

            var thumbnailUrl = thumbnailUrlElement.GetString();
            var sourceUrl = imageInfo.TryGetProperty("descriptionurl", out var sourceUrlElement)
                ? sourceUrlElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(thumbnailUrl) || string.IsNullOrWhiteSpace(sourceUrl))
                continue;

            using var imageResponse = await Client.GetAsync(thumbnailUrl, cancellationToken);
            if (!imageResponse.IsSuccessStatusCode)
                continue;

            var imageData = await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            if (imageData.Length == 0)
                continue;

            var metadata = imageInfo.TryGetProperty("extmetadata", out var metadataElement)
                ? metadataElement
                : default;
            results.Add(new ArtistImageSearchResult(
                CleanTitle(page.TryGetProperty("title", out var title) ? title.GetString() : null),
                ReadMetadata(metadata, "Artist"),
                ReadMetadata(metadata, "LicenseShortName"),
                sourceUrl,
                imageData,
                imageResponse.Content.Headers.ContentType?.MediaType));
        }

        return results;
    }

    public static async Task<string> SaveAsync(
        long artistId,
        ArtistImageSearchResult result,
        CancellationToken cancellationToken = default)
    {
        var extension = result.MimeType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var directory = AppPaths.GetDataPath("artist-images");
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, artistId + extension);
        var temporaryPath = targetPath + ".tmp";

        await File.WriteAllBytesAsync(temporaryPath, result.ImageData, cancellationToken);
        File.Move(temporaryPath, targetPath, true);

        foreach (var existingPath in Directory.EnumerateFiles(directory, artistId + ".*"))
        {
            if (!string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
                File.Delete(existingPath);
        }

        return targetPath;
    }

    private static string CleanTitle(string? title)
    {
        var value = title ?? string.Empty;
        if (value.StartsWith("File:", StringComparison.OrdinalIgnoreCase))
            value = value[5..];
        return Path.GetFileNameWithoutExtension(value.Replace('_', ' '));
    }

    private static string? ReadMetadata(JsonElement metadata, string propertyName)
    {
        if (metadata.ValueKind != JsonValueKind.Object ||
            !metadata.TryGetProperty(propertyName, out var property) ||
            !property.TryGetProperty("value", out var value))
        {
            return null;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Orynivo/1.0 (Windows music player; artist image search)");
        return client;
    }
}
