using System.Net.Http;

namespace Orynivo.Scrobbling;

/// <summary>
/// Coordinates Last.fm scrobbling for the desktop client: authorization,
/// now-playing notifications, eligible scrobble submission, and an offline queue
/// for submissions that could not be delivered.
/// </summary>
/// <remarks>
/// The API secret and session key are only used to sign requests. They are never
/// logged, and every failure is swallowed so scrobbling can never affect playback.
/// </remarks>
internal sealed class LastFmScrobblingService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly PendingScrobbleStore _pending;

    private bool _enabled;
    private string _apiKey = string.Empty;
    private string _apiSecret = string.Empty;
    private string _sessionKey = string.Empty;
    private LastFmClient? _client;
    private LastFmTrack? _current;
    private DateTimeOffset _currentStartedAt;
    private string _pendingToken = string.Empty;

    /// <summary>Initializes the service.</summary>
    /// <param name="pending">Store for scrobbles awaiting submission.</param>
    internal LastFmScrobblingService(PendingScrobbleStore pending) => _pending = pending;

    /// <summary>Gets the connected Last.fm username, or an empty string.</summary>
    internal string Username { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether an API key and secret are present.</summary>
    internal bool IsConfigured => _apiKey.Length > 0 && _apiSecret.Length > 0;

    /// <summary>Gets a value indicating whether an authorized session key is present.</summary>
    internal bool IsConnected => _sessionKey.Length > 0;

    /// <summary>Applies the current settings and credential values.</summary>
    /// <param name="enabled">Whether scrobbling is enabled.</param>
    /// <param name="apiKey">Last.fm API key.</param>
    /// <param name="apiSecret">Last.fm API secret.</param>
    /// <param name="sessionKey">Authorized session key.</param>
    /// <param name="username">Connected username.</param>
    internal void Configure(
        bool enabled,
        string? apiKey,
        string? apiSecret,
        string? sessionKey,
        string? username)
    {
        _enabled = enabled;
        _apiKey = apiKey?.Trim() ?? string.Empty;
        _apiSecret = apiSecret?.Trim() ?? string.Empty;
        _sessionKey = sessionKey?.Trim() ?? string.Empty;
        Username = username?.Trim() ?? string.Empty;
        _client = IsConfigured ? new LastFmClient(Http, _apiKey, _apiSecret) : null;
    }

    /// <summary>Requests a token and returns the Last.fm authorization page URL.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization URL, or <see langword="null"/> when unconfigured or offline.</returns>
    internal async Task<string?> BeginAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return null;

        var token = await _client.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
            return null;

        _pendingToken = token;
        return $"https://www.last.fm/api/auth/?api_key={Uri.EscapeDataString(_apiKey)}&token={Uri.EscapeDataString(token)}";
    }

    /// <summary>Exchanges the pending authorized token for a session key.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorized session, or <see langword="null"/> on failure.</returns>
    internal async Task<LastFmSession?> CompleteAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null || _pendingToken.Length == 0)
            return null;

        var session = await _client.GetSessionAsync(_pendingToken, cancellationToken).ConfigureAwait(false);
        if (session is null)
            return null;

        _pendingToken = string.Empty;
        _sessionKey = session.SessionKey;
        Username = session.Username;
        return session;
    }

    /// <summary>Clears the authorized session.</summary>
    internal void Disconnect()
    {
        _pendingToken = string.Empty;
        _sessionKey = string.Empty;
        Username = string.Empty;
    }

    /// <summary>Records the current track and sends a now-playing notification.</summary>
    /// <param name="track">Track metadata.</param>
    /// <param name="startedAt">Playback start time used for a later scrobble.</param>
    internal void SetNowPlaying(LastFmTrack track, DateTimeOffset startedAt)
    {
        _current = track;
        _currentStartedAt = startedAt;
        if (!_enabled || !IsConnected || _client is not { } client)
            return;

        _ = SafeAsync(async () => { await client.UpdateNowPlayingAsync(track, _sessionKey).ConfigureAwait(false); });
    }

    /// <summary>Submits the current track when it satisfies the Last.fm scrobble rules.</summary>
    /// <param name="played">Audible playback time.</param>
    /// <param name="duration">Total track duration.</param>
    internal void Complete(TimeSpan played, TimeSpan duration)
    {
        var track = _current;
        _current = null;
        if (track is null || !_enabled || !IsConnected || _client is not { } client)
            return;
        if (!ScrobbleRules.ShouldScrobble(played, duration))
            return;

        var playedAt = _currentStartedAt;
        _ = SafeAsync(async () =>
        {
            if (!await client.ScrobbleAsync(track, playedAt, _sessionKey).ConfigureAwait(false))
                _pending.Enqueue(ToPending(track, playedAt));
        });
    }

    /// <summary>Attempts to submit every queued scrobble.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the flush.</returns>
    internal async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled || !IsConnected || _client is not { } client)
            return;

        var entries = _pending.Load();
        if (entries.Count == 0)
            return;

        var remaining = new List<PendingScrobble>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = new LastFmTrack(entry.Artist, entry.Title, entry.Album, entry.Duration);
            var playedAt = DateTimeOffset.FromUnixTimeSeconds(entry.PlayedAtUnix);
            if (!await client.ScrobbleAsync(track, playedAt, _sessionKey, cancellationToken).ConfigureAwait(false))
                remaining.Add(entry);
        }

        _pending.Save(PendingScrobbleStore.Trim(remaining));
    }

    private static PendingScrobble ToPending(LastFmTrack track, DateTimeOffset playedAt) =>
        new(track.Artist, track.Title, track.Album, track.Duration, playedAt.ToUnixTimeSeconds());

    private static async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch
        {
            // Scrobbling failures must never surface or affect playback.
        }
    }
}
