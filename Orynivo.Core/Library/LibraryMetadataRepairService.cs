using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;

namespace Orynivo.Library;

/// <summary>A physical folder whose indexed tracks contain potentially inconsistent album metadata.</summary>
/// <param name="FolderPath">Physical album-candidate folder.</param>
/// <param name="Tracks">Tracks grouped into the candidate.</param>
/// <param name="AlbumCount">Number of distinct non-empty album titles.</param>
/// <param name="AlbumArtistCount">Number of distinct non-empty album artists.</param>
/// <param name="MissingTitleCount">Number of tracks without a title.</param>
/// <param name="MissingTrackNumberCount">Number of tracks without a track number.</param>
/// <param name="DuplicateTrackNumbers">Whether track numbers are duplicated within a disc.</param>
public sealed record MetadataFolderCandidate(
    string FolderPath,
    IReadOnlyList<MetadataRepairTrack> Tracks,
    int AlbumCount,
    int AlbumArtistCount,
    int MissingTitleCount,
    int MissingTrackNumberCount,
    bool DuplicateTrackNumbers)
{
    /// <summary>Gets whether the candidate contains a metadata inconsistency worth reviewing.</summary>
    public bool HasProblems =>
        AlbumCount != 1 ||
        AlbumArtistCount != 1 ||
        MissingTitleCount > 0 ||
        MissingTrackNumberCount > 0 ||
        DuplicateTrackNumbers;
}

/// <summary>One MusicBrainz track proposed for a physical local track.</summary>
/// <param name="Position">One-based track position on the medium.</param>
/// <param name="Title">Canonical track title.</param>
/// <param name="Artist">Canonical track artist.</param>
/// <param name="RecordingId">MusicBrainz recording identifier.</param>
/// <param name="LengthMilliseconds">Published track duration in milliseconds.</param>
public sealed record MetadataMatchTrack(
    int Position,
    string Title,
    string Artist,
    string? RecordingId,
    int? LengthMilliseconds);

/// <summary>A MusicBrainz release/medium matched against a local folder candidate.</summary>
/// <param name="ReleaseId">MusicBrainz release identifier.</param>
/// <param name="Title">Canonical release title.</param>
/// <param name="AlbumArtist">Canonical release artist credit.</param>
/// <param name="ArtistId">Primary MusicBrainz artist identifier.</param>
/// <param name="Year">Release year, or <see langword="null"/>.</param>
/// <param name="MediumPosition">One-based medium/disc position.</param>
/// <param name="MediumCount">Total medium/disc count.</param>
/// <param name="Tracks">Canonical medium track list.</param>
/// <param name="Confidence">Locally calculated match confidence from zero to one.</param>
public sealed record MetadataReleaseMatch(
    string ReleaseId,
    string Title,
    string AlbumArtist,
    string? ArtistId,
    int? Year,
    int MediumPosition,
    int MediumCount,
    IReadOnlyList<MetadataMatchTrack> Tracks,
    double Confidence);

/// <summary>
/// Detects album candidates from physical folders and resolves their approximate CD table of
/// contents against MusicBrainz without modifying media files.
/// </summary>
public static class LibraryMetadataRepairService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim MusicBrainzThrottle = new(1, 1);
    private static DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    /// <summary>Groups indexed tracks by physical album folder and returns inconsistent candidates.</summary>
    /// <param name="tracks">Compact indexed track metadata.</param>
    /// <param name="includeHealthy">Whether candidates without detected inconsistencies are included.</param>
    /// <returns>Physical folder candidates ordered by path.</returns>
    public static List<MetadataFolderCandidate> Analyze(
        IEnumerable<MetadataRepairTrack> tracks,
        bool includeHealthy = false)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        return tracks
            .GroupBy(track => ResolveAlbumFolder(track.SourcePath), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group =>
            {
                var ordered = group
                    .OrderBy(track => track.DiscNumber ?? 1)
                    .ThenBy(track => track.TrackNumber ?? int.MaxValue)
                    .ThenBy(track => track.SourcePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var albumCount = CountDistinct(ordered.Select(track => track.Album));
                var albumArtistCount = CountDistinct(ordered.Select(track => track.AlbumArtist));
                var duplicateNumbers = ordered
                    .Where(track => track.TrackNumber.HasValue)
                    .GroupBy(track => (Disc: track.DiscNumber ?? 1, Track: track.TrackNumber!.Value))
                    .Any(grouping => grouping.Count() > 1);
                return new MetadataFolderCandidate(
                    group.Key,
                    ordered,
                    albumCount,
                    albumArtistCount,
                    ordered.Count(track => string.IsNullOrWhiteSpace(track.Title)),
                    ordered.Count(track => !track.TrackNumber.HasValue),
                    duplicateNumbers);
            })
            .Where(candidate => includeHealthy || candidate.HasProblems)
            .OrderBy(candidate => candidate.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Looks up a folder candidate through MusicBrainz, optionally using editable release and
    /// artist search terms before validating candidates against track count and durations.
    /// </summary>
    /// <param name="candidate">Physical folder candidate.</param>
    /// <param name="albumQuery">Editable release-title query, or empty to use only fuzzy CD-TOC lookup.</param>
    /// <param name="artistQuery">Optional editable release-artist query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Up to ten ranked release-medium matches.</returns>
    public static async Task<List<MetadataReleaseMatch>> LookupAsync(
        MetadataFolderCandidate candidate,
        string? albumQuery = null,
        string? artistQuery = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var tracks = candidate.Tracks
            .OrderBy(track => track.TrackNumber ?? int.MaxValue)
            .ThenBy(track => track.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (tracks.Count == 0)
            return [];

        List<JsonElement> releases;
        if (string.IsNullOrWhiteSpace(albumQuery))
        {
            if (tracks.Any(track => track.Duration is null or <= 0))
                return [];
            var offsets = new List<int>(tracks.Count);
            var sector = 150;
            foreach (var track in tracks)
            {
                offsets.Add(sector);
                sector += Math.Max(1, (int)Math.Round(track.Duration!.Value * 75));
            }
            var toc = $"1+{tracks.Count}+{sector}+{string.Join('+', offsets)}";
            releases = await LookupFuzzyTocReleasesAsync(toc, cancellationToken);
        }
        else
        {
            releases = await SearchReleaseDetailsAsync(
                albumQuery.Trim(),
                artistQuery?.Trim(),
                cancellationToken);
        }

        var result = new List<MetadataReleaseMatch>();
        foreach (var release in releases)
        {
            var releaseId = GetString(release, "id");
            var title = GetString(release, "title");
            if (string.IsNullOrWhiteSpace(releaseId) || string.IsNullOrWhiteSpace(title) ||
                !release.TryGetProperty("media", out var media))
            {
                continue;
            }
            var albumArtist = ReadArtistCredit(release);
            var artistId = ReadPrimaryArtistId(release);
            var mediumCount = media.GetArrayLength();
            foreach (var medium in media.EnumerateArray())
            {
                if (!medium.TryGetProperty("tracks", out var remoteTracks) ||
                    remoteTracks.GetArrayLength() != tracks.Count)
                {
                    continue;
                }
                var mapped = new List<MetadataMatchTrack>();
                var totalDifference = 0d;
                var durationComparisons = 0;
                foreach (var remoteTrack in remoteTracks.EnumerateArray())
                {
                    var recording = remoteTrack.TryGetProperty("recording", out var recordingElement)
                        ? recordingElement
                        : default;
                    var length = GetInt(remoteTrack, "length") ??
                                 (recording.ValueKind == JsonValueKind.Object
                                     ? GetInt(recording, "length")
                                     : null);
                    var position = GetInt(remoteTrack, "position") ?? mapped.Count + 1;
                    var localDuration = tracks[mapped.Count].Duration;
                    if (length.HasValue && localDuration.HasValue)
                    {
                        totalDifference += Math.Abs(length.Value / 1000d - localDuration.Value);
                        durationComparisons++;
                    }
                    mapped.Add(new MetadataMatchTrack(
                        position,
                        GetString(remoteTrack, "title") ??
                        (recording.ValueKind == JsonValueKind.Object
                            ? GetString(recording, "title")
                            : null) ??
                        string.Empty,
                        ReadArtistCredit(remoteTrack) is { Length: > 0 } trackArtist
                            ? trackArtist
                            : recording.ValueKind == JsonValueKind.Object
                                ? ReadArtistCredit(recording)
                                : albumArtist,
                        recording.ValueKind == JsonValueKind.Object
                            ? GetString(recording, "id")
                            : null,
                        length));
                }
                var confidence = CalculateMatchConfidence(
                    tracks,
                    mapped,
                    totalDifference,
                    durationComparisons);
                result.Add(new MetadataReleaseMatch(
                    releaseId,
                    title,
                    albumArtist,
                    artistId,
                    ParseYear(GetString(release, "date")),
                    GetInt(medium, "position") ?? 1,
                    mediumCount,
                    mapped,
                    confidence));
            }
        }
        return result
            .OrderByDescending(match => match.Confidence)
            .ThenBy(match => match.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(10)
            .ToList();
    }

    private static async Task<List<JsonElement>> LookupFuzzyTocReleasesAsync(
        string toc,
        CancellationToken cancellationToken)
    {
        var url = "https://musicbrainz.org/ws/2/discid/-" +
                  $"?fmt=json&cdstubs=no&media-format=all&inc=artist-credits+recordings&toc={toc}";
        using var response = await GetMusicBrainzAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("releases", out var releases)
            ? releases.EnumerateArray().Select(release => release.Clone()).ToList()
            : [];
    }

    private static async Task<List<JsonElement>> SearchReleaseDetailsAsync(
        string albumQuery,
        string? artistQuery,
        CancellationToken cancellationToken)
    {
        var exactQuery = $"release:\"{EscapeSearchValue(albumQuery)}\"";
        if (!string.IsNullOrWhiteSpace(artistQuery))
            exactQuery += $" AND artist:\"{EscapeSearchValue(artistQuery)}\"";

        var releaseIds = await SearchReleaseIdsAsync(exactQuery, cancellationToken);
        if (releaseIds.Count < 10)
        {
            var relaxedQuery = $"release:({EscapeSearchTerm(albumQuery)})";
            foreach (var id in await SearchReleaseIdsAsync(relaxedQuery, cancellationToken))
            {
                if (!releaseIds.Contains(id, StringComparer.Ordinal))
                    releaseIds.Add(id);
                if (releaseIds.Count >= 10)
                    break;
            }
        }

        var result = new List<JsonElement>();
        foreach (var id in releaseIds.Take(10))
        {
            using var detailResponse = await GetMusicBrainzAsync(
                $"https://musicbrainz.org/ws/2/release/{id}?fmt=json&inc=artist-credits+recordings",
                cancellationToken);
            detailResponse.EnsureSuccessStatusCode();
            using var detailDocument = await JsonDocument.ParseAsync(
                await detailResponse.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            result.Add(detailDocument.RootElement.Clone());
        }
        return result;
    }

    private static async Task<List<string>> SearchReleaseIdsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var searchUrl =
            $"https://musicbrainz.org/ws/2/release?fmt=json&limit=10&query={Uri.EscapeDataString(query)}";
        using var searchResponse = await GetMusicBrainzAsync(searchUrl, cancellationToken);
        searchResponse.EnsureSuccessStatusCode();
        using var searchDocument = await JsonDocument.ParseAsync(
            await searchResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return searchDocument.RootElement.TryGetProperty("releases", out var releases)
            ? releases.EnumerateArray()
                .Select(release => GetString(release, "id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList()
            : [];
    }

    /// <summary>Calculates a match score from title similarity and available duration evidence.</summary>
    /// <param name="localTracks">Local tracks in medium order.</param>
    /// <param name="remoteTracks">MusicBrainz tracks in medium order.</param>
    /// <param name="totalDurationDifference">Summed absolute duration difference in seconds.</param>
    /// <param name="durationComparisons">Number of tracks with durations on both sides.</param>
    /// <returns>A confidence value from zero to one.</returns>
    internal static double CalculateMatchConfidence(
        IReadOnlyList<MetadataRepairTrack> localTracks,
        IReadOnlyList<MetadataMatchTrack> remoteTracks,
        double totalDurationDifference,
        int durationComparisons)
    {
        var titleScore = localTracks
            .Zip(remoteTracks, (local, remote) =>
                TextSimilarity(local.Title, remote.Title))
            .DefaultIfEmpty(0.5)
            .Average();
        if (durationComparisons == 0)
            return Math.Clamp(0.35 + titleScore * 0.45, 0, 1);

        var averageDifference = totalDurationDifference / durationComparisons;
        var durationScore = Math.Clamp(1 - averageDifference / 20d, 0, 1);
        var durationCoverage = durationComparisons / (double)localTracks.Count;
        return Math.Clamp(
            titleScore * 0.35 +
            durationScore * (0.45 + durationCoverage * 0.15) +
            0.05,
            0,
            1);
    }

    private static double TextSimilarity(string? left, string? right)
    {
        var leftWords = NormalizeWords(left);
        var rightWords = NormalizeWords(right);
        if (leftWords.Count == 0 || rightWords.Count == 0)
            return 0.5;
        var intersection = leftWords.Intersect(rightWords, StringComparer.Ordinal).Count();
        var union = leftWords.Union(rightWords, StringComparer.Ordinal).Count();
        return union == 0 ? 0.5 : intersection / (double)union;
    }

    private static HashSet<string> NormalizeWords(string? value)
    {
        var characters = (value ?? string.Empty)
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) !=
                                UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : ' ')
            .ToArray();
        return new string(characters)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string EscapeSearchTerm(string value)
    {
        const string specialCharacters = @"+-!(){}[]^""~*?:\/";
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (specialCharacters.Contains(character, StringComparison.Ordinal))
                builder.Append('\\');
            builder.Append(character);
        }
        return builder.ToString();
    }

    private static string EscapeSearchValue(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>Creates persistent library-only overrides from a confirmed release match.</summary>
    /// <param name="candidate">Local folder candidate.</param>
    /// <param name="match">Confirmed MusicBrainz release medium.</param>
    /// <returns>Per-track overrides in matched track order.</returns>
    public static List<TrackMetadataOverride> CreateOverrides(
        MetadataFolderCandidate candidate,
        MetadataReleaseMatch match)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(match);
        var local = candidate.Tracks
            .OrderBy(track => track.TrackNumber ?? int.MaxValue)
            .ThenBy(track => track.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var count = Math.Min(local.Count, match.Tracks.Count);
        var result = new List<TrackMetadataOverride>(count);
        for (var index = 0; index < count; index++)
        {
            var remote = match.Tracks[index];
            result.Add(new TrackMetadataOverride(
                local[index].Path,
                remote.Title,
                string.IsNullOrWhiteSpace(remote.Artist) ? match.AlbumArtist : remote.Artist,
                match.AlbumArtist,
                match.Title,
                remote.Position,
                match.Tracks.Count,
                match.MediumPosition,
                match.MediumCount,
                remote.RecordingId,
                match.ReleaseId,
                match.ArtistId));
        }
        return result;
    }

    private static string ResolveAlbumFolder(string sourcePath)
        => Path.GetDirectoryName(sourcePath) ?? string.Empty;

    private static int CountDistinct(IEnumerable<string?> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ArtistNameNormalizer.CreateComparisonKey(value))
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        await MusicBrainzThrottle.WaitAsync(cancellationToken);
        try
        {
            var delay = _lastRequest.AddMilliseconds(1100) - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _lastRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            MusicBrainzThrottle.Release();
        }
    }

    private static async Task<HttpResponseMessage> GetMusicBrainzAsync(
        string url,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await ThrottleAsync(cancellationToken);
            var response = await Client.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode ||
                attempt >= 2 ||
                response.StatusCode is not (System.Net.HttpStatusCode.TooManyRequests or
                    System.Net.HttpStatusCode.BadGateway or
                    System.Net.HttpStatusCode.ServiceUnavailable or
                    System.Net.HttpStatusCode.GatewayTimeout))
            {
                return response;
            }

            var retryAfter = response.Headers.RetryAfter?.Delta ??
                             TimeSpan.FromSeconds(attempt + 2);
            response.Dispose();
            await Task.Delay(retryAfter, cancellationToken);
        }
    }

    private static string ReadArtistCredit(JsonElement element)
    {
        if (!element.TryGetProperty("artist-credit", out var credits))
            return string.Empty;
        return string.Concat(credits.EnumerateArray().Select(credit =>
            (GetString(credit, "name") ??
             (credit.TryGetProperty("artist", out var artist) ? GetString(artist, "name") : null) ??
             string.Empty) +
            (GetString(credit, "joinphrase") ?? string.Empty))).Trim();
    }

    private static string? ReadPrimaryArtistId(JsonElement element)
    {
        if (!element.TryGetProperty("artist-credit", out var credits))
            return null;
        var first = credits.EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Object &&
               first.TryGetProperty("artist", out var artist)
            ? GetString(artist, "id")
            : null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.TryGetInt32(out var result)
            ? result
            : null;

    private static int? ParseYear(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), out var year)
            ? year
            : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0 (https://github.com/bschlaack/Orynivo)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}
