using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orynivo.Library;

public sealed record LyricsDownloadResult(string? PlainLyrics, string? SyncedLyrics);
public sealed record LyricsSearchResult(
    long Id,
    string TrackName,
    string ArtistName,
    string? AlbumName,
    double? Duration,
    string? PlainLyrics,
    string? SyncedLyrics);
public sealed record TimedLyricLine(TimeSpan Time, string Text);

public static partial class LyricsService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<LyricsDownloadResult?> DownloadAsync(
        TrackRecord track,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
            return null;

        var parameters = new List<string>
        {
            $"track_name={Uri.EscapeDataString(track.Title)}",
            $"artist_name={Uri.EscapeDataString(track.Artist)}"
        };
        if (!string.IsNullOrWhiteSpace(track.Album))
            parameters.Add($"album_name={Uri.EscapeDataString(track.Album)}");
        if (track.Duration is > 0)
            parameters.Add($"duration={Math.Round(track.Duration.Value).ToString(CultureInfo.InvariantCulture)}");

        using var response = await Client.GetAsync(
            "https://lrclib.net/api/get?" + string.Join("&", parameters),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<LrclibResponse>(
            stream,
            JsonOptions,
            cancellationToken: cancellationToken);
        if (result is null || result.Instrumental)
            return null;

        var plain = NullIfWhiteSpace(result.PlainLyrics);
        var synced = NullIfWhiteSpace(result.SyncedLyrics);
        return plain is null && synced is null
            ? null
            : new LyricsDownloadResult(plain, synced);
    }

    public static async Task<IReadOnlyList<LyricsSearchResult>> SearchAsync(
        string trackName,
        string artistName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackName) && string.IsNullOrWhiteSpace(artistName))
            return [];

        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(trackName))
            parameters.Add($"track_name={Uri.EscapeDataString(trackName.Trim())}");
        if (!string.IsNullOrWhiteSpace(artistName))
            parameters.Add($"artist_name={Uri.EscapeDataString(artistName.Trim())}");

        using var response = await Client.GetAsync(
            "https://lrclib.net/api/search?" + string.Join("&", parameters),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var results = await JsonSerializer.DeserializeAsync<List<LrclibSearchResponse>>(
            stream,
            JsonOptions,
            cancellationToken);
        if (results is null)
            return [];

        return results
            .Where(result => !result.Instrumental)
            .Select(result => new LyricsSearchResult(
                result.Id,
                result.TrackName,
                result.ArtistName,
                result.AlbumName,
                result.Duration,
                NullIfWhiteSpace(result.PlainLyrics),
                NullIfWhiteSpace(result.SyncedLyrics)))
            .Where(result => result.PlainLyrics is not null || result.SyncedLyrics is not null)
            .ToList();
    }

    public static IReadOnlyList<TimedLyricLine> ParseLrc(string? lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyrics))
            return [];

        var result = new List<TimedLyricLine>();
        foreach (var rawLine in lyrics.Replace("\r\n", "\n").Split('\n'))
        {
            var matches = TimestampRegex().Matches(rawLine);
            if (matches.Count == 0)
                continue;

            var text = TimestampRegex().Replace(rawLine, string.Empty).Trim();
            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups["minutes"].Value, out var minutes) ||
                    !double.TryParse(
                        match.Groups["seconds"].Value,
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var seconds))
                {
                    continue;
                }

                result.Add(new TimedLyricLine(
                    TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds),
                    text));
            }
        }

        return result
            .OrderBy(line => line.Time)
            .ToList();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0 (WPF music player)");
        client.DefaultRequestHeaders.Add("Lrclib-Client", "Orynivo-WPF");
        return client;
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex(@"\[(?<minutes>\d+):(?<seconds>\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex TimestampRegex();

    private sealed record LrclibResponse(
        bool Instrumental,
        string? PlainLyrics,
        string? SyncedLyrics);

    private sealed record LrclibSearchResponse(
        long Id,
        string TrackName,
        string ArtistName,
        string? AlbumName,
        double? Duration,
        bool Instrumental,
        string? PlainLyrics,
        string? SyncedLyrics);
}
