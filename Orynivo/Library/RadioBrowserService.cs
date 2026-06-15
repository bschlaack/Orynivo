using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Orynivo.Library;

public sealed record RadioBrowserStation(
    string StationUuid,
    string Name,
    string StreamUrl,
    string? Homepage,
    string? Favicon,
    string? CountryCode,
    string? Codec,
    int Bitrate,
    string? Tags);

public sealed record RadioBrowserTag(string Name, int StationCount);

public sealed class RadioBrowserService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private IReadOnlyList<string>? _servers;

    public async Task<IReadOnlyList<RadioBrowserStation>> SearchAsync(
        string? searchText,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var requestedTags = tags is { Count: > 0 } ? tags : [string.Empty];
        var allRows = new List<StationDto>();
        foreach (var tag in requestedTags)
        {
            var parameters = new List<string>
            {
                "hidebroken=true",
                "order=clickcount",
                "reverse=true",
                $"limit={(tags is { Count: > 0 } ? 10000 : 100)}"
            };
            if (!string.IsNullOrWhiteSpace(searchText))
                parameters.Add($"name={Uri.EscapeDataString(searchText.Trim())}");
            if (!string.IsNullOrWhiteSpace(tag))
            {
                parameters.Add($"tag={Uri.EscapeDataString(GetTagQuery(tag))}");
                parameters.Add("tagExact=false");
            }
            allRows.AddRange(await GetAsync<List<StationDto>>(
                $"/json/stations/search?{string.Join("&", parameters)}",
                cancellationToken));
        }

        return allRows
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.StationUuid) &&
                !string.IsNullOrWhiteSpace(row.Name) &&
                !string.IsNullOrWhiteSpace(row.Url ?? row.ResolvedUrl))
            .Select(row => new RadioBrowserStation(
                row.StationUuid!,
                row.Name!.Trim(),
                (row.Url ?? row.ResolvedUrl)!.Trim(),
                NullIfWhiteSpace(row.Homepage),
                NullIfWhiteSpace(row.Favicon),
                NullIfWhiteSpace(row.CountryCode),
                NullIfWhiteSpace(row.Codec),
                row.Bitrate,
                NullIfWhiteSpace(row.Tags)))
            .GroupBy(row => row.StationUuid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public async Task RegisterClickAsync(string stationUuid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stationUuid))
            return;

        try
        {
            await GetAsync<object>(
                $"/json/url/{Uri.EscapeDataString(stationUuid)}",
                cancellationToken);
        }
        catch
        {
            // Click counting must never prevent playback.
        }
    }

    public async Task<IReadOnlyList<RadioBrowserTag>> GetTagsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await GetAsync<List<TagDto>>(
            "/json/tags?order=stationcount&reverse=true&hidebroken=true&limit=100000",
            cancellationToken);
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Name) && row.StationCount > 0)
            .Select(row => new RadioBrowserTag(row.Name!.Trim(), row.StationCount))
            .ToList();
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var server in await GetServersAsync(cancellationToken))
        {
            try
            {
                return await HttpClient.GetFromJsonAsync<T>(
                           new Uri(new Uri(server), path),
                           cancellationToken)
                       ?? throw new InvalidOperationException("Radio Browser returned an empty response.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("Radio Browser is currently unavailable.", lastError);
    }

    private async Task<IReadOnlyList<string>> GetServersAsync(CancellationToken cancellationToken)
    {
        if (_servers is { Count: > 0 })
            return _servers;

        var servers = new List<string>();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(
                "all.api.radio-browser.info",
                cancellationToken);
            foreach (var address in addresses.OrderBy(_ => Random.Shared.Next()))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var host = await Dns.GetHostEntryAsync(address);
                    if (host.HostName.EndsWith(".api.radio-browser.info", StringComparison.OrdinalIgnoreCase))
                        servers.Add($"https://{host.HostName.TrimEnd('.')}");
                }
                catch
                {
                    // Try the next mirror address.
                }
            }
        }
        catch
        {
            // The maintained fallback list below is used when DNS discovery fails.
        }

        servers.AddRange(
        [
            "https://de1.api.radio-browser.info",
            "https://de2.api.radio-browser.info",
            "https://at1.api.radio-browser.info"
        ]);
        _servers = servers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return _servers;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0");
        return client;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetTagQuery(string genre) =>
        genre switch
        {
            "R&B / Soul" => "soul",
            "Hip-Hop" => "hip hop",
            "Public Radio" => "public radio",
            "Easy Listening" => "easy listening",
            "Adult Contemporary" => "adult contemporary",
            _ => genre.ToLowerInvariant()
        };

    private sealed class StationDto
    {
        [JsonPropertyName("stationuuid")]
        public string? StationUuid { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("url_resolved")]
        public string? ResolvedUrl { get; init; }

        [JsonPropertyName("homepage")]
        public string? Homepage { get; init; }

        [JsonPropertyName("favicon")]
        public string? Favicon { get; init; }

        [JsonPropertyName("countrycode")]
        public string? CountryCode { get; init; }

        [JsonPropertyName("codec")]
        public string? Codec { get; init; }

        [JsonPropertyName("bitrate")]
        public int Bitrate { get; init; }

        [JsonPropertyName("tags")]
        public string? Tags { get; init; }
    }

    private sealed class TagDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("stationcount")]
        public int StationCount { get; init; }
    }
}
