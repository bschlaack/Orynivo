using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Orynivo.Scrobbling;

/// <summary>
/// Minimal Last.fm scrobbling client for token/session authentication, now-playing
/// updates, and scrobble submission.
/// </summary>
/// <remarks>
/// The API secret and session key are only used to sign requests; they are never
/// logged or included in diagnostics.
/// </remarks>
public sealed class LastFmClient
{
    private const string ApiRoot = "https://ws.audioscrobbler.com/2.0/";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _apiSecret;

    /// <summary>Initializes a new client.</summary>
    /// <param name="http">HTTP client used for requests.</param>
    /// <param name="apiKey">The account's Last.fm API key.</param>
    /// <param name="apiSecret">The account's Last.fm API secret.</param>
    public LastFmClient(HttpClient http, string apiKey, string apiSecret)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _apiSecret = apiSecret ?? throw new ArgumentNullException(nameof(apiSecret));
    }

    /// <summary>Requests a temporary authentication token.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token, or <see langword="null"/> when the request failed.</returns>
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var response = await PostAsync(
            new Dictionary<string, string> { ["method"] = "auth.getToken" },
            sessionKey: null,
            cancellationToken).ConfigureAwait(false);
        return response is null ? null : GetString(response.Value, "token");
    }

    /// <summary>Exchanges an authorized token for a session.</summary>
    /// <param name="token">The token returned by <see cref="GetTokenAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session, or <see langword="null"/> when authorization failed.</returns>
    public async Task<LastFmSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync(
            new Dictionary<string, string>
            {
                ["method"] = "auth.getSession",
                ["token"] = token
            },
            sessionKey: null,
            cancellationToken).ConfigureAwait(false);

        if (response is null || !response.Value.TryGetProperty("session", out var session))
            return null;

        var username = GetString(session, "name");
        var key = GetString(session, "key");
        return string.IsNullOrEmpty(username) || string.IsNullOrEmpty(key)
            ? null
            : new LastFmSession(username, key);
    }

    /// <summary>Submits a now-playing notification for the current track.</summary>
    /// <param name="track">Track metadata.</param>
    /// <param name="sessionKey">The authenticated session key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when Last.fm accepted the request.</returns>
    public async Task<bool> UpdateNowPlayingAsync(
        LastFmTrack track,
        string sessionKey,
        CancellationToken cancellationToken = default)
    {
        var parameters = BuildTrackParameters("track.updateNowPlaying", track);
        var response = await PostAsync(parameters, sessionKey, cancellationToken).ConfigureAwait(false);
        return Succeeded(response);
    }

    /// <summary>Submits a scrobble for a completed playback.</summary>
    /// <param name="track">Track metadata.</param>
    /// <param name="playedAt">The time playback started.</param>
    /// <param name="sessionKey">The authenticated session key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when Last.fm accepted the request.</returns>
    public async Task<bool> ScrobbleAsync(
        LastFmTrack track,
        DateTimeOffset playedAt,
        string sessionKey,
        CancellationToken cancellationToken = default)
    {
        var parameters = BuildTrackParameters("track.scrobble", track);
        parameters["timestamp"] = playedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var response = await PostAsync(parameters, sessionKey, cancellationToken).ConfigureAwait(false);
        return Succeeded(response);
    }

    private static bool Succeeded(JsonElement? response) =>
        response is not null && !response.Value.TryGetProperty("error", out _);

    private static Dictionary<string, string> BuildTrackParameters(string method, LastFmTrack track)
    {
        var parameters = new Dictionary<string, string>
        {
            ["method"] = method,
            ["artist"] = track.Artist,
            ["track"] = track.Title
        };

        if (!string.IsNullOrWhiteSpace(track.Album))
            parameters["album"] = track.Album;
        if (track.Duration is int duration and > 0)
            parameters["duration"] = duration.ToString(CultureInfo.InvariantCulture);

        return parameters;
    }

    private async Task<JsonElement?> PostAsync(
        Dictionary<string, string> parameters,
        string? sessionKey,
        CancellationToken cancellationToken)
    {
        parameters["api_key"] = _apiKey;
        if (!string.IsNullOrEmpty(sessionKey))
            parameters["sk"] = sessionKey;

        // format and callback must not contribute to the signature.
        parameters["api_sig"] = LastFmSignature.Compute(parameters, _apiSecret);
        parameters["format"] = "json";

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _http.PostAsync(ApiRoot, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
