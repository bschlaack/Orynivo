using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace Orynivo.Streaming;

public sealed record PlexLibrarySection(string Key, string Title);
/// <summary>Describes one artist, album, track, or folder returned by Plex.</summary>
/// <param name="Key">Plex navigation key.</param>
/// <param name="RatingKey">Stable Plex metadata identifier.</param>
/// <param name="Title">Display title.</param>
/// <param name="Artist">Optional artist name.</param>
/// <param name="Album">Optional album title.</param>
/// <param name="Year">Optional release year.</param>
/// <param name="DurationMilliseconds">Optional logical item duration in milliseconds.</param>
/// <param name="Format">Optional media container or codec.</param>
/// <param name="PartKeys">Ordered direct-media part keys forming the logical track.</param>
/// <param name="IsFolder">Whether the item represents a folder.</param>
public sealed record PlexMediaItem(
    string Key,
    string RatingKey,
    string Title,
    string? Artist,
    string? Album,
    int? Year,
    long? DurationMilliseconds,
    string? Format,
    IReadOnlyList<string> PartKeys,
    bool IsFolder);

public sealed record PlexMediaPage(IReadOnlyList<PlexMediaItem> Items, int TotalSize);

public sealed class PlexServerClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task<IReadOnlyList<PlexLibrarySection>> GetAudioLibrariesAsync(
        PlexServerSettings server,
        string? token,
        CancellationToken cancellationToken = default)
    {
        var baseUri = NormalizeBaseUri(server.BaseUrl);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseUri, "library/sections"));
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Orynivo");
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", "Orynivo.AudioPlayer");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.TryAddWithoutValidation("X-Plex-Token", token.Trim());

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseAudioLibraries(content);
    }

    public Task<PlexMediaPage> GetLibraryItemsAsync(
        PlexServerSettings server,
        string? token,
        string sectionKey,
        int mediaType,
        int start,
        int size,
        CancellationToken cancellationToken = default) =>
        GetMediaPageAsync(
            server,
            token,
            $"library/sections/{Uri.EscapeDataString(sectionKey)}/all?type={mediaType}" +
            $"&X-Plex-Container-Start={start}&X-Plex-Container-Size={size}",
            cancellationToken);

    public Task<PlexMediaPage> GetChildrenAsync(
        PlexServerSettings server,
        string? token,
        string ratingKey,
        CancellationToken cancellationToken = default) =>
        GetMediaPageAsync(
            server,
            token,
            $"library/metadata/{Uri.EscapeDataString(ratingKey)}/children" +
            "?X-Plex-Container-Start=0&X-Plex-Container-Size=10000",
            cancellationToken);

    public Task<PlexMediaPage> GetFoldersAsync(
        PlexServerSettings server,
        string? token,
        string sectionKey,
        string? parent,
        CancellationToken cancellationToken = default)
    {
        var path = string.IsNullOrWhiteSpace(parent)
            ? $"library/sections/{Uri.EscapeDataString(sectionKey)}/folder"
            : parent.TrimStart('/');
        path += (path.Contains('?') ? "&" : "?") +
                "X-Plex-Container-Start=0&X-Plex-Container-Size=10000";
        return GetMediaPageAsync(server, token, path, cancellationToken);
    }

    public static string CreateStreamUrl(
        PlexServerSettings server,
        string partKey,
        string? token)
    {
        var uri = new Uri(NormalizeBaseUri(server.BaseUrl), partKey.TrimStart('/'));
        if (string.IsNullOrWhiteSpace(token))
            return uri.ToString();
        return uri + (uri.Query.Length == 0 ? "?" : "&") +
               "X-Plex-Token=" + Uri.EscapeDataString(token.Trim());
    }

    public static Uri NormalizeBaseUri(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Plex server URL must be an absolute HTTP or HTTPS URL.");
        }

        var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" };
        return builder.Uri;
    }

    private static IReadOnlyList<PlexLibrarySection> ParseAudioLibraries(string content)
    {
        var trimmed = content.TrimStart();
        return trimmed.StartsWith('<')
            ? ParseXmlAudioLibraries(content)
            : ParseJsonAudioLibraries(content);
    }

    private async Task<PlexMediaPage> GetMediaPageAsync(
        PlexServerSettings server,
        string? token,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var baseUri = NormalizeBaseUri(server.BaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativePath));
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Orynivo");
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", "Orynivo.AudioPlayer");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.TryAddWithoutValidation("X-Plex-Token", token.Trim());

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseMediaPage(content);
    }

    private static PlexMediaPage ParseMediaPage(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("MediaContainer", out var container))
            return new PlexMediaPage([], 0);

        var totalSize = GetInt(container, "totalSize") ?? GetInt(container, "size") ?? 0;
        var elementName = container.TryGetProperty("Metadata", out var metadata)
            ? "Metadata"
            : container.TryGetProperty("Directory", out metadata)
                ? "Directory"
                : null;
        if (elementName is null || metadata.ValueKind != JsonValueKind.Array)
            return new PlexMediaPage([], totalSize);

        var items = metadata.EnumerateArray().Select(item =>
        {
            var media = item.TryGetProperty("Media", out var mediaArray) &&
                        mediaArray.ValueKind == JsonValueKind.Array
                ? mediaArray.EnumerateArray().FirstOrDefault()
                : default;
            var partKeys = media.ValueKind == JsonValueKind.Object &&
                           media.TryGetProperty("Part", out var partArray) &&
                           partArray.ValueKind == JsonValueKind.Array
                ? partArray.EnumerateArray()
                    .Select(part => GetString(part, "key"))
                    .Where(static key => !string.IsNullOrWhiteSpace(key))
                    .Cast<string>()
                    .ToArray()
                : [];
            return new PlexMediaItem(
                GetString(item, "key") ?? string.Empty,
                GetString(item, "ratingKey") ?? string.Empty,
                GetString(item, "title") ?? string.Empty,
                GetString(item, "grandparentTitle") ?? GetString(item, "parentTitle"),
                GetString(item, "parentTitle"),
                GetInt(item, "year") ?? GetInt(item, "parentYear"),
                GetLong(item, "duration"),
                GetString(media, "container") ?? GetString(media, "audioCodec"),
                partKeys,
                string.IsNullOrWhiteSpace(GetString(item, "ratingKey")));
        }).Where(item => item.Title.Length > 0).ToList();
        return new PlexMediaPage(items, totalSize);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value)
            ? value.ToString()
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.TryGetInt32(out var result)
            ? result
            : null;

    private static long? GetLong(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.TryGetInt64(out var result)
            ? result
            : null;

    private static IReadOnlyList<PlexLibrarySection> ParseJsonAudioLibraries(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("MediaContainer", out var container) ||
            !container.TryGetProperty("Directory", out var directories) ||
            directories.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return directories.EnumerateArray()
            .Where(directory =>
                directory.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "artist", StringComparison.OrdinalIgnoreCase))
            .Select(directory => new PlexLibrarySection(
                directory.TryGetProperty("key", out var key) ? key.ToString() : string.Empty,
                directory.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty))
            .Where(section => section.Key.Length > 0 && section.Title.Length > 0)
            .ToList();
    }

    private static IReadOnlyList<PlexLibrarySection> ParseXmlAudioLibraries(string content)
    {
        var document = XDocument.Parse(content);
        return document.Descendants("Directory")
            .Where(directory =>
                string.Equals((string?)directory.Attribute("type"), "artist", StringComparison.OrdinalIgnoreCase))
            .Select(directory => new PlexLibrarySection(
                (string?)directory.Attribute("key") ?? string.Empty,
                (string?)directory.Attribute("title") ?? string.Empty))
            .Where(section => section.Key.Length > 0 && section.Title.Length > 0)
            .ToList();
    }
}
