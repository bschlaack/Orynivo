using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;

namespace Player.Library;

public sealed record ArtistProfileDownload(
    string Biography,
    string? ImagePath,
    string SourceUrl,
    string Language);

public static class ArtistProfileService
{
    private static readonly HttpClient Client = CreateClient();

    public static async Task<ArtistProfileDownload?> DownloadAsync(
        long artistId,
        string artistName,
        string language,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistName))
            return null;

        language = language is "de" or "fr" ? language : "en";
        var query = Uri.EscapeDataString($"\"{artistName}\"");
        var uri =
            $"https://{language}.wikipedia.org/w/api.php" +
            $"?action=query&generator=search&gsrsearch={query}&gsrnamespace=0&gsrlimit=5" +
            "&prop=extracts%7Cpageimages%7Cinfo&exintro=1&explaintext=1" +
            "&piprop=original%7Cthumbnail&pithumbsize=1000&inprop=url" +
            "&format=json&formatversion=2&origin=*";

        using var response = await Client.GetAsync(uri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
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
                candidate.TryGetProperty("extract", out var extract) &&
                !string.IsNullOrWhiteSpace(extract.GetString()))
            .OrderByDescending(candidate =>
                candidate.TryGetProperty("thumbnail", out _) ||
                candidate.TryGetProperty("original", out _))
            .FirstOrDefault();
        if (page.ValueKind == JsonValueKind.Undefined)
            return null;

        var biography = page.GetProperty("extract").GetString()?.Trim();
        var sourceUrl = page.TryGetProperty("fullurl", out var fullUrl)
            ? fullUrl.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(biography) || string.IsNullOrWhiteSpace(sourceUrl))
            return null;

        var imageUrl = GetImageUrl(page);
        var imagePath = imageUrl is null
            ? null
            : await DownloadImageAsync(artistId, imageUrl, cancellationToken);
        return new ArtistProfileDownload(biography, imagePath, sourceUrl, language);
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
        using var response = await Client.GetAsync(imageUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var extension = mediaType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create),
            "Player",
            "artist-images");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, artistId + extension);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(output, cancellationToken);
        return path;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Player/1.0 (Windows music player; artist metadata)");
        return client;
    }
}
