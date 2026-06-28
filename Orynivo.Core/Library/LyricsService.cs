using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orynivo.Library;

/// <summary>Plain and synchronised lyrics downloaded from LRCLIB.</summary>
/// <param name="PlainLyrics">Unsynced plain-text lyrics, or <see langword="null"/>.</param>
/// <param name="SyncedLyrics">LRC-formatted synchronised lyrics, or <see langword="null"/>.</param>
public sealed record LyricsDownloadResult(string? PlainLyrics, string? SyncedLyrics);

/// <summary>A single search result from the LRCLIB search endpoint.</summary>
/// <param name="Id">LRCLIB track ID.</param>
/// <param name="TrackName">Track title.</param>
/// <param name="ArtistName">Artist name.</param>
/// <param name="AlbumName">Album name, or <see langword="null"/>.</param>
/// <param name="Duration">Duration in seconds, or <see langword="null"/>.</param>
/// <param name="PlainLyrics">Unsynced lyrics, or <see langword="null"/>.</param>
/// <param name="SyncedLyrics">LRC-formatted lyrics, or <see langword="null"/>.</param>
public sealed record LyricsSearchResult(
    long Id,
    string TrackName,
    string ArtistName,
    string? AlbumName,
    double? Duration,
    string? PlainLyrics,
    string? SyncedLyrics);

/// <summary>A single timestamped line from an LRC lyrics file.</summary>
/// <param name="Time">Playback offset of the line.</param>
/// <param name="Text">Lyric text for this line.</param>
public sealed record TimedLyricLine(TimeSpan Time, string Text);

/// <summary>
/// Client for the LRCLIB lyrics API supporting automatic download, manual search, and LRC parsing.
/// </summary>
public static partial class LyricsService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Downloads plain and/or synchronised lyrics for <paramref name="track"/> from LRCLIB.
    /// Returns <see langword="null"/> when the track lacks required tags or LRCLIB returns no result.
    /// </summary>
    /// <param name="track">Track whose lyrics to fetch; <c>Title</c> and <c>Artist</c> must be set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<LyricsDownloadResult?> DownloadAsync(
        TrackRecord track,
        CancellationToken cancellationToken = default)
        => DownloadAsync(track.Title, track.Artist, track.Album, track.Duration, cancellationToken);

    /// <summary>
    /// Downloads plain and/or synchronised lyrics for the given track metadata from LRCLIB.
    /// Returns <see langword="null"/> when title or artist is missing or LRCLIB returns no result.
    /// </summary>
    /// <param name="title">Track title; required.</param>
    /// <param name="artist">Track artist; required.</param>
    /// <param name="album">Album name, or <see langword="null"/>.</param>
    /// <param name="duration">Duration in seconds, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<LyricsDownloadResult?> DownloadAsync(
        string? title,
        string? artist,
        string? album,
        double? duration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return null;

        var parameters = new List<string>
        {
            $"track_name={Uri.EscapeDataString(title)}",
            $"artist_name={Uri.EscapeDataString(artist)}"
        };
        if (!string.IsNullOrWhiteSpace(album))
            parameters.Add($"album_name={Uri.EscapeDataString(album)}");
        if (duration is > 0)
            parameters.Add($"duration={Math.Round(duration.Value).ToString(CultureInfo.InvariantCulture)}");

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

    /// <summary>
    /// Searches LRCLIB for lyrics matching the given track and artist name.
    /// Instrumental results and entries with neither plain nor synced lyrics are excluded.
    /// </summary>
    /// <param name="trackName">Track title query.</param>
    /// <param name="artistName">Artist name query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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

    /// <summary>
    /// Parses an LRC-formatted string into a list of timestamped lyric lines sorted by time.
    /// Returns an empty list when <paramref name="lyrics"/> is null or whitespace.
    /// </summary>
    /// <param name="lyrics">LRC content with <c>[mm:ss.xx]</c> timestamp tags.</param>
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
