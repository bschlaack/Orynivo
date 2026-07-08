using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Orynivo.Library;
using Orynivo.Streaming;

namespace Orynivo.Mcp;

/// <summary>
/// MCP tool implementations that expose Orynivo playback control and library search to language-model clients.
/// Injected into the MCP server via <see cref="McpServerBuilderExtensions.WithTools{T}"/>.
/// </summary>
/// <param name="bridge">The player bridge used to invoke UI-thread operations and database queries.</param>
[McpServerToolType]
public sealed class McpTools(McpPlayerBridge bridge)
{
    // ------------------------------------------------------------------
    // Query tools
    // ------------------------------------------------------------------

    /// <summary>Returns the current playback state as a formatted text summary.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A multi-line string with status, track metadata, position, volume, and queue info.</returns>
    [McpServerTool(Name = "get_now_playing", ReadOnly = true, Idempotent = true)]
    [Description("Returns the current playback state: status, track title, artist, album, position, duration, volume, and queue position.")]
    public async Task<string> GetNowPlayingAsync(CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("get_now_playing")) return "Tool is disabled.";
        var state = await bridge.OnUiAsync(
            () => bridge.GetStateFunc?.Invoke()
                  ?? new PlayerState("stopped", null, null, null, null, 0, 0, 0, -1, 0),
            ct);
        return FormatState(state);
    }

    /// <summary>Returns all entries in the current playback queue.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A formatted list of queue entries, or a message when the queue is empty.</returns>
    [McpServerTool(Name = "get_queue", ReadOnly = true, Idempotent = true)]
    [Description("Returns all entries in the current playback queue with their file names and paths.")]
    public async Task<string> GetQueueAsync(CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("get_queue")) return "Tool is disabled.";
        var entries = await bridge.OnUiAsync(
            () => bridge.GetQueueFunc?.Invoke() ?? [],
            ct);
        if (entries.Count == 0)
            return "Queue is empty.";
        var sb = new System.Text.StringBuilder();
        foreach (var e in entries)
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{(e.IsCurrent ? "▶" : " ")} [{e.Index + 1}] {e.FileName}  ({RedactKey(e.Path)})");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Returns the current date and time in local and UTC form.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A multi-line string with the local time, day of week, ISO 8601 form, UTC time, and time-zone name.</returns>
    [McpServerTool(Name = "get_current_time", ReadOnly = true, Idempotent = true)]
    [Description("Returns the current date and time: local time, day of week, ISO 8601 form, UTC time, and the local time-zone name.")]
    public Task<string> GetCurrentTimeAsync(CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("get_current_time")) return Task.FromResult("Tool is disabled.");
        var now = DateTimeOffset.Now;
        var result = string.Join('\n',
            $"Local time: {now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} ({now.ToString("dddd", CultureInfo.InvariantCulture)})",
            $"ISO 8601: {now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)}",
            $"UTC: {now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}Z",
            $"Time zone: {TimeZoneInfo.Local.DisplayName}");
        return Task.FromResult(result);
    }

    // ------------------------------------------------------------------
    // Web tools (search + safe page fetch through the MCP server)
    // ------------------------------------------------------------------

    /// <summary>Searches the web through the configured SearXNG instance.</summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">Maximum number of results (1–25).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A formatted list of results, or a readable error message.</returns>
    [McpServerTool(Name = "search_web", ReadOnly = true, Idempotent = true)]
    [Description("Searches the web through the configured SearXNG instance and returns the top results (title, URL, snippet). Follow up with fetch_page to read a result.")]
    public async Task<string> SearchWebAsync(
        [Description("The search query.")] string query,
        [Description("Maximum number of results (1–25, default 5).")] int maxResults = 5,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("search_web")) return "Tool is disabled.";
        if (bridge.WebBrowsing is not { } web) return "Web browsing is not available.";
        return await web.SearchAsync(query, maxResults, ct);
    }

    /// <summary>Fetches a web page and returns its readable plain text.</summary>
    /// <param name="url">Absolute http/https URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page text, or a readable error message.</returns>
    [McpServerTool(Name = "fetch_page", ReadOnly = true, Idempotent = true)]
    [Description("Fetches an http/https page and returns its readable plain text. Private/loopback addresses, non-HTTP schemes, and non-text content are blocked for safety.")]
    public async Task<string> FetchPageAsync(
        [Description("Absolute http or https URL of the page to fetch.")] string url,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("fetch_page")) return "Tool is disabled.";
        if (bridge.WebBrowsing is not { } web) return "Web browsing is not available.";
        return await web.FetchTextAsync(url, ct);
    }

    /// <summary>Fetches a web page and returns a compact Markdown rendering.</summary>
    /// <param name="url">Absolute http/https URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page as Markdown, or a readable error message.</returns>
    [McpServerTool(Name = "fetch_page_as_markdown", ReadOnly = true, Idempotent = true)]
    [Description("Fetches an http/https page and returns a compact Markdown rendering that preserves headings, links, and lists. Uses the same safety limits as fetch_page.")]
    public async Task<string> FetchPageAsMarkdownAsync(
        [Description("Absolute http or https URL of the page to fetch.")] string url,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("fetch_page_as_markdown")) return "Tool is disabled.";
        if (bridge.WebBrowsing is not { } web) return "Web browsing is not available.";
        return await web.FetchMarkdownAsync(url, ct);
    }

    // ------------------------------------------------------------------
    // Playback control
    // ------------------------------------------------------------------

    /// <summary>Plays the specified file or resumes the current track.</summary>
    /// <param name="path">Absolute path to play. Omit to resume.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    [McpServerTool(Name = "play")]
    [Description("Plays an audio track by its path. Accepts a local absolute file path or an orynivo:// remote reference from search_library. Omit the path to resume the current paused track.")]
    public async Task<string> PlayAsync(
        [Description("Local absolute file path or orynivo:// remote reference to play. Leave empty to resume.")] string? path,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("play")) return "Tool is disabled.";
        if (string.IsNullOrWhiteSpace(path))
        {
            if (bridge.TogglePauseFunc is not null)
                await bridge.OnUiAsync(async () => await bridge.TogglePauseFunc(), ct);
            return "Resumed playback.";
        }
        var resolved = await ResolvePlayablePathAsync(path);
        if (resolved is null)
            return $"Could not resolve remote track reference: {path}";
        if (bridge.PlayFileFunc is not null)
            await bridge.OnUiAsync(async () => await bridge.PlayFileFunc(resolved), ct);
        return $"Playing: {DisplayNameForPath(path, resolved)}";
    }

    /// <summary>Toggles between paused and playing states.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    [McpServerTool(Name = "pause_resume")]
    [Description("Toggles between pause and resume. Has no effect when nothing is playing.")]
    public async Task<string> PauseResumeAsync(CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("pause_resume")) return "Tool is disabled.";
        if (bridge.TogglePauseFunc is not null)
            await bridge.OnUiAsync(async () => await bridge.TogglePauseFunc(), ct);
        return "Toggled pause/resume.";
    }

    /// <summary>Skips to the next track in the playback queue.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    [McpServerTool(Name = "next_track")]
    [Description("Skips to the next track in the playback queue.")]
    public async Task<string> NextTrackAsync(CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("next_track")) return "Tool is disabled.";
        if (bridge.SkipNextFunc is not null)
            await bridge.OnUiAsync(async () => await bridge.SkipNextFunc(), ct);
        return "Skipped to next track.";
    }

    /// <summary>Goes back to the previous track in the playback queue.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    [McpServerTool(Name = "previous_track")]
    [Description("Goes back to the previous track in the playback queue.")]
    public async Task<string> PreviousTrackAsync(CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("previous_track")) return "Tool is disabled.";
        if (bridge.SkipPreviousFunc is not null)
            await bridge.OnUiAsync(async () => await bridge.SkipPreviousFunc(), ct);
        return "Went to previous track.";
    }

    /// <summary>Stops playback immediately.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    [McpServerTool(Name = "stop")]
    [Description("Stops playback immediately.")]
    public async Task<string> StopAsync(CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("stop")) return "Tool is disabled.";
        await bridge.OnUiAsync(() => bridge.StopFunc?.Invoke(), ct);
        return "Playback stopped.";
    }

    /// <summary>Seeks to the given position in the current track.</summary>
    /// <param name="positionSeconds">Target position in seconds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    [McpServerTool(Name = "seek")]
    [Description("Seeks to the given position in the currently playing track.")]
    public async Task<string> SeekAsync(
        [Description("Target position in seconds.")] double positionSeconds,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("seek")) return "Tool is disabled.";
        if (bridge.SeekFunc is not null)
            await bridge.OnUiAsync(async () => await bridge.SeekFunc(positionSeconds), ct);
        return $"Seeked to {positionSeconds:F1} s.";
    }

    /// <summary>Sets the playback volume.</summary>
    /// <param name="volume">Target volume level between 0.0 (mute) and 1.0 (maximum).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    [McpServerTool(Name = "set_volume")]
    [Description("Sets the playback volume. Use 0.0 for mute and 1.0 for maximum volume.")]
    public async Task<string> SetVolumeAsync(
        [Description("Volume level between 0.0 and 1.0.")] double volume,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("set_volume")) return "Tool is disabled.";
        volume = Math.Clamp(volume, 0.0, 1.0);
        await bridge.OnUiAsync(() => bridge.SetVolumeFunc?.Invoke(volume), ct);
        return $"Volume set to {volume:P0}.";
    }

    // ------------------------------------------------------------------
    // Queue management
    // ------------------------------------------------------------------

    /// <summary>Appends a file to the end of the playback queue.</summary>
    /// <param name="path">Absolute path of the audio file to append.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    [McpServerTool(Name = "queue_append")]
    [Description("Appends an audio track to the end of the playback queue. Accepts a local absolute file path or an orynivo:// remote reference from search_library.")]
    public async Task<string> QueueAppendAsync(
        [Description("Local absolute file path or orynivo:// remote reference to append.")] string path,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("queue_append")) return "Tool is disabled.";
        var resolved = await ResolvePlayablePathAsync(path);
        if (resolved is null)
            return $"Could not resolve remote track reference: {path}";
        if (bridge.AppendToQueueFunc is not null)
            await bridge.OnUiAsync(async () => await bridge.AppendToQueueFunc(resolved), ct);
        return $"Appended to queue: {DisplayNameForPath(path, resolved)}";
    }

    /// <summary>Inserts a file as the next entry after the current queue position.</summary>
    /// <param name="path">Absolute path of the audio file to insert next.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    [McpServerTool(Name = "queue_play_next")]
    [Description("Inserts an audio track immediately after the current queue position so it plays next. Accepts a local absolute file path or an orynivo:// remote reference from search_library.")]
    public async Task<string> QueuePlayNextAsync(
        [Description("Local absolute file path or orynivo:// remote reference to play next.")] string path,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("queue_play_next")) return "Tool is disabled.";
        var resolved = await ResolvePlayablePathAsync(path);
        if (resolved is null)
            return $"Could not resolve remote track reference: {path}";
        if (bridge.PlayNextFunc is not null)
            await bridge.OnUiAsync(async () => await bridge.PlayNextFunc(resolved), ct);
        return $"Inserted as next: {DisplayNameForPath(path, resolved)}";
    }

    /// <summary>Clears all items from the playback queue without stopping the current track.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    [McpServerTool(Name = "clear_queue")]
    [Description("Clears all items from the playback queue. The current track continues playing until it finishes, after which playback stops.")]
    public async Task<string> ClearQueueAsync(CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("clear_queue")) return "Tool is disabled.";
        if (bridge.ClearQueueFunc is not null)
            await bridge.OnUiAsync(bridge.ClearQueueFunc, ct);
        return "Playback queue cleared.";
    }

    /// <summary>Replaces the entire playback queue with the given file paths and starts playback from the first track.</summary>
    /// <param name="paths">Ordered list of absolute audio file paths for the new queue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    [McpServerTool(Name = "replace_queue")]
    [Description("Replaces the entire playback queue with the given list of tracks and immediately starts playing the first one. Each entry may be a local absolute file path or an orynivo:// remote reference from search_library. Prefer this over clearing then appending individually. Use queue_append to add to the existing queue instead.")]
    public async Task<string> ReplaceQueueAsync(
        [Description("Ordered list of local absolute file paths and/or orynivo:// remote references to set as the new queue.")] string[] paths,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("replace_queue")) return "Tool is disabled.";
        var resolved = new List<string>(paths.Length);
        foreach (var p in paths)
        {
            var r = await ResolvePlayablePathAsync(p);
            if (r is not null)
                resolved.Add(r);
        }
        if (resolved.Count == 0)
            return "No playable tracks: could not resolve any of the supplied paths.";
        if (bridge.ReplaceQueueFunc is not null)
            await bridge.OnUiAsync(async () => await bridge.ReplaceQueueFunc(resolved), ct);
        var skipped = paths.Length - resolved.Count;
        return skipped > 0
            ? $"Queue replaced with {resolved.Count} track(s); {skipped} could not be resolved."
            : $"Queue replaced with {resolved.Count} track(s).";
    }

    // ------------------------------------------------------------------
    // Library search
    // ------------------------------------------------------------------

    /// <summary>Searches the local music library and returns matching tracks, albums, and artists.</summary>
    /// <param name="query">Free-text search query.</param>
    /// <param name="limit">Maximum number of results per category.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A formatted Markdown list of matching tracks, albums, and artists.</returns>
    [McpServerTool(Name = "search_library", ReadOnly = true, Idempotent = true)]
    [Description("Searches the music library for tracks, albums, and artists across the local library and every configured remote Orynivo Server. Local tracks return a file path; remote tracks return an orynivo:// reference. Both can be passed directly to the play and queue tools.")]
    public async Task<string> SearchLibraryAsync(
        [Description("Free-text search query, e.g. an artist name, album title, or track title.")] string query,
        [Description("Maximum number of results per category (1–50, default 10).")] int limit = 10,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("search_library")) return "Tool is disabled.";
        if (string.IsNullOrWhiteSpace(query))
            return "Provide a search query.";
        limit = Math.Clamp(limit, 1, 50);

        var ids = await Task.Run(() => TrackSearchIndex.SearchByCategory(query, limit * 3), ct);
        var sb = new System.Text.StringBuilder();

        using (var db = AudioDatabase.OpenDefault())
        {
            var localSection = new System.Text.StringBuilder();
            if (ids.Tracks.Ids.Count > 0)
            {
                var trackIds = ids.Tracks.Ids.Take(limit).ToList();
                var tracks = await Task.Run(() => db.GetTrackListByIds(trackIds), ct);
                if (tracks.Count > 0)
                {
                    localSection.AppendLine("## Tracks");
                    foreach (var t in tracks)
                        localSection.AppendLine(CultureInfo.InvariantCulture,
                            $"- {t.Title ?? t.FileName}  —  {t.Artist ?? "?"}  ({t.Path})");
                }
            }

            if (ids.Albums.Ids.Count > 0)
            {
                var albums = await Task.Run(() => db.GetAlbumsLite(includeArtwork: false), ct);
                var albumIdSet = ids.Albums.Ids.Take(limit).ToHashSet();
                var matched = albums.Where(a => albumIdSet.Contains(a.Id)).Take(limit).ToList();
                if (matched.Count > 0)
                {
                    localSection.AppendLine("## Albums");
                    foreach (var a in matched)
                        localSection.AppendLine(CultureInfo.InvariantCulture,
                            $"- {a.Album}  —  {a.DisplayArtist ?? "?"}");
                }
            }

            if (ids.Artists.Ids.Count > 0)
            {
                var artists = await Task.Run(() => db.GetArtistsLite(), ct);
                var artistIdSet = ids.Artists.Ids.Take(limit).ToHashSet();
                var matched = artists.Where(a => artistIdSet.Contains(a.Id)).Take(limit).ToList();
                if (matched.Count > 0)
                {
                    localSection.AppendLine("## Artists");
                    foreach (var a in matched)
                        localSection.AppendLine(CultureInfo.InvariantCulture, $"- {a.Artist}");
                }
            }

            if (localSection.Length > 0)
            {
                sb.AppendLine("# Local library");
                sb.Append(localSection);
            }
        }

        await AppendRemoteSearchResultsAsync(sb, query, limit, ct);

        return sb.Length == 0 ? "No results found." : sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Appends search results from every configured remote Orynivo Server to the search output.
    /// Remote tracks are emitted as <c>orynivo://serverId/track/trackId</c> references so no
    /// credential-bearing stream URL is exposed to the model; the references resolve to playable
    /// URLs when passed to the play/queue tools.
    /// </summary>
    /// <param name="sb">The output builder to append to.</param>
    /// <param name="query">The search query.</param>
    /// <param name="limit">Maximum results per category and server.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous search.</returns>
    private async Task AppendRemoteSearchResultsAsync(
        System.Text.StringBuilder sb,
        string query,
        int limit,
        CancellationToken ct)
    {
        var servers = bridge.GetOrynivoServersFunc?.Invoke() ?? [];
        if (servers.Count == 0)
            return;

        using var client = new OrynivoServerClient();
        foreach (var server in servers)
        {
            OrynivoFullSearchResult result;
            try { result = await client.SearchFullAsync(server, query, limit, ct); }
            catch { continue; }
            if (result.Tracks.Count == 0 && result.Albums.Count == 0 && result.Artists.Count == 0)
                continue;

            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"# Orynivo Server: {server.Name}");

            if (result.Tracks.Count > 0)
            {
                sb.AppendLine("## Tracks");
                foreach (var t in result.Tracks.Take(limit))
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"- {t.Title ?? t.FileName}  —  {t.Artist ?? "?"}  ({BuildOrynivoTrackReference(server, t.Id)})");
            }

            if (result.Albums.Count > 0)
            {
                sb.AppendLine("## Albums");
                foreach (var a in result.Albums.Take(limit))
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"- {a.Album}  —  {a.DisplayArtist ?? "?"}");
            }

            if (result.Artists.Count > 0)
            {
                sb.AppendLine("## Artists");
                foreach (var a in result.Artists.Take(limit))
                    sb.AppendLine(CultureInfo.InvariantCulture, $"- {a.Name}");
            }
        }
    }

    /// <summary>Builds the opaque remote-track reference used as a playable path for MCP/AI tools.</summary>
    /// <param name="server">The remote server owning the track.</param>
    /// <param name="trackId">The server-side track identifier.</param>
    /// <returns>An <c>orynivo://serverId/track/trackId</c> reference.</returns>
    private static string BuildOrynivoTrackReference(OrynivoServerSettings server, long trackId) =>
        $"orynivo://{Uri.EscapeDataString(server.Id)}/track/{trackId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Resolves a tool-supplied path (local file, real URL, or <c>orynivo://</c> remote reference)
    /// into a playable path, registering remote track metadata as a side effect.
    /// </summary>
    /// <param name="path">The path or reference to resolve.</param>
    /// <returns>The playable path, or <see langword="null"/> when a remote reference cannot be resolved.</returns>
    private async Task<string?> ResolvePlayablePathAsync(string path)
    {
        if (bridge.ResolveRemoteTrackFunc is null || string.IsNullOrWhiteSpace(path))
            return path;
        return await bridge.ResolveRemoteTrackFunc(path);
    }

    /// <summary>Masks the API key in a remote stream URL so it is never shown to the model.</summary>
    /// <param name="path">A path or URL that may contain a <c>key=</c> query parameter.</param>
    /// <returns>The path with any API-key value redacted.</returns>
    private static string RedactKey(string path) =>
        Regex.Replace(path, "([?&]key=)[^&]*", "$1***", RegexOptions.IgnoreCase);

    /// <summary>Builds a human-readable, credential-free display name for a resolved playback path.</summary>
    /// <param name="original">The original tool-supplied path or reference.</param>
    /// <param name="resolved">The resolved playable path.</param>
    /// <returns>The original opaque reference, or the resolved path's file name with the key redacted.</returns>
    private static string DisplayNameForPath(string original, string resolved) =>
        original.StartsWith("orynivo://", StringComparison.OrdinalIgnoreCase)
            ? original
            : System.IO.Path.GetFileName(RedactKey(resolved));

    // ------------------------------------------------------------------
    // Playlist tools
    // ------------------------------------------------------------------

    /// <summary>Lists all playlists in the library.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A formatted list of all playlists with their type and track count.</returns>
    [McpServerTool(Name = "list_playlists", ReadOnly = true, Idempotent = true)]
    [Description("Lists all playlists in the library, including regular and smart playlists, with their IDs, track counts, and types.")]
    public async Task<string> ListPlaylistsAsync(CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("list_playlists")) return "Tool is disabled.";
        var playlists = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetAllPlaylists().ToList();
        }, ct);

        if (playlists.Count == 0)
            return "No playlists found.";

        var sb = new System.Text.StringBuilder();
        foreach (var p in playlists)
        {
            var type = p.IsSmartPlaylist ? "smart" : "regular";
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"[{p.Id}] {p.Name}  ({type}, {p.TrackCount} tracks)");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Returns the tracks of a playlist identified by name or numeric ID.</summary>
    /// <param name="playlist">Playlist name (case-insensitive) or numeric ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A formatted list of playlist tracks with their paths.</returns>
    [McpServerTool(Name = "get_playlist_tracks", ReadOnly = true, Idempotent = true)]
    [Description("Returns the tracks of a playlist. Provide either the playlist name (case-insensitive) or its numeric ID from list_playlists.")]
    public async Task<string> GetPlaylistTracksAsync(
        [Description("Playlist name (case-insensitive) or numeric ID.")] string playlist,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("get_playlist_tracks")) return "Tool is disabled.";
        if (string.IsNullOrWhiteSpace(playlist))
            return "Provide a playlist name or ID.";

        var (record, tracks) = await Task.Run<(PlaylistRecord?, List<PlaylistTrackRecord>)>(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            PlaylistRecord? pr;
            if (long.TryParse(playlist.Trim(), out var id))
            {
                pr = db.GetPlaylistById(id);
            }
            else
            {
                pr = db.GetAllPlaylists()
                    .FirstOrDefault(p => string.Equals(p.Name, playlist.Trim(),
                        StringComparison.OrdinalIgnoreCase));
            }
            if (pr is null)
                return (null, []);
            return (pr, db.GetPlaylistTracks(pr.Id).ToList());
        }, ct);

        if (record is null)
            return $"Playlist not found: {playlist}";

        if (tracks.Count == 0)
            return $"Playlist \"{record.Name}\" is empty.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Playlist: {record.Name} ({(record.IsSmartPlaylist ? "smart" : "regular")}, {tracks.Count} tracks)");
        foreach (var t in tracks)
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  [{t.Position}] {System.IO.Path.GetFileName(t.Path)}  ({t.Path})");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Creates a new regular playlist, optionally pre-populated with file paths.</summary>
    /// <param name="name">Name for the new playlist.</param>
    /// <param name="paths">Comma-separated absolute file paths to include.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message with the new playlist ID.</returns>
    [McpServerTool(Name = "create_playlist")]
    [Description("Creates a new regular playlist. Optionally supply a comma-separated list of absolute file paths to add as initial tracks.")]
    public async Task<string> CreatePlaylistAsync(
        [Description("Name for the new playlist.")] string name,
        [Description("Comma-separated absolute file paths to include. Leave empty to create an empty playlist.")] string? paths = null,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("create_playlist")) return "Tool is disabled.";
        if (string.IsNullOrWhiteSpace(name))
            return "Provide a playlist name.";

        var pathList = string.IsNullOrWhiteSpace(paths)
            ? Array.Empty<string>()
            : paths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var id = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return pathList.Length > 0
                ? db.CreatePlaylist(name.Trim(), pathList)
                : db.CreatePlaylist(name.Trim());
        }, ct);

        await bridge.OnUiAsync(() => bridge.RefreshPlaylistsFunc?.Invoke(), ct);
        return $"Created playlist \"{name.Trim()}\" (ID {id}) with {pathList.Length} track(s).";
    }

    /// <summary>Creates a new smart playlist with filter criteria.</summary>
    /// <param name="name">Name for the new smart playlist.</param>
    /// <param name="favoritesOnly">Include only tracks marked as favorites.</param>
    /// <param name="genres">Comma-separated genre names to include; empty means all genres.</param>
    /// <param name="artistContains">Case-insensitive substring to match in artist names.</param>
    /// <param name="albumContains">Case-insensitive substring to match in album titles.</param>
    /// <param name="addedWithinDays">Include only tracks added within this many days.</param>
    /// <param name="neverPlayed">Include only tracks with no recorded playback.</param>
    /// <param name="sortOrder">Sort order: title, random, recent, leastrecent.</param>
    /// <param name="resultLimit">Maximum number of tracks to include; omit for unlimited.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Confirmation message with the new smart playlist ID.</returns>
    [McpServerTool(Name = "create_smart_playlist")]
    [Description("Creates a new smart playlist that resolves tracks dynamically from the library based on filter criteria.")]
    public async Task<string> CreateSmartPlaylistAsync(
        [Description("Name for the new smart playlist.")] string name,
        [Description("Include only tracks marked as favorites.")] bool favoritesOnly = false,
        [Description("Comma-separated genre names to filter by. Leave empty for all genres.")] string? genres = null,
        [Description("Case-insensitive substring to match in artist names.")] string? artistContains = null,
        [Description("Case-insensitive substring to match in album titles.")] string? albumContains = null,
        [Description("Include only tracks added within this many days.")] int? addedWithinDays = null,
        [Description("Include only tracks with no recorded playback.")] bool neverPlayed = false,
        [Description("Sort order: title, random, recent, leastrecent.")] string sortOrder = "title",
        [Description("Maximum number of tracks to include. Omit for unlimited.")] int? resultLimit = null,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("create_smart_playlist")) return "Tool is disabled.";
        if (string.IsNullOrWhiteSpace(name))
            return "Provide a playlist name.";

        var order = sortOrder.Trim().ToLowerInvariant() switch
        {
            "random"      => SmartPlaylistSortOrder.Random,
            "recent"      => SmartPlaylistSortOrder.LastPlayedNewest,
            "leastrecent" => SmartPlaylistSortOrder.LeastRecentlyPlayed,
            _             => SmartPlaylistSortOrder.Title,
        };

        var genreList = string.IsNullOrWhiteSpace(genres)
            ? new List<string>()
            : genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var criteria = new SmartPlaylistCriteria
        {
            FavoritesOnly      = favoritesOnly,
            Genres             = genreList,
            ArtistContains     = string.IsNullOrWhiteSpace(artistContains) ? null : artistContains.Trim(),
            AlbumContains      = string.IsNullOrWhiteSpace(albumContains)  ? null : albumContains.Trim(),
            AddedWithinDays    = addedWithinDays,
            NeverPlayed        = neverPlayed,
            SortOrder          = order,
            ResultLimit        = resultLimit,
        };

        var json = JsonSerializer.Serialize(criteria);
        var id = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.CreateSmartPlaylist(name.Trim(), json);
        }, ct);

        await bridge.OnUiAsync(() => bridge.RefreshPlaylistsFunc?.Invoke(), ct);
        return $"Created smart playlist \"{name.Trim()}\" (ID {id}).";
    }

    // ------------------------------------------------------------------
    // Play history
    // ------------------------------------------------------------------

    /// <summary>Returns play-history entries, optionally filtered to a specific day.</summary>
    /// <param name="date">Optional date in <c>yyyy-MM-dd</c> format; omit for the most recent entries.</param>
    /// <param name="limit">Maximum number of entries when no date is given.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A formatted list of history entries.</returns>
    [McpServerTool(Name = "get_play_history", ReadOnly = true, Idempotent = true)]
    [Description("Returns playback history. Supply a date (yyyy-MM-dd) to get entries for that day, or omit it for the most recent entries.")]
    public async Task<string> GetPlayHistoryAsync(
        [Description("Optional date in yyyy-MM-dd format (e.g. 2025-06-01). Omit to get recent history.")] string? date = null,
        [Description("Maximum number of entries when no date is provided (1–100, default 20).")] int limit = 20,
        CancellationToken ct = default)
    {
        if (!bridge.IsToolEnabled("get_play_history")) return "Tool is disabled.";
        limit = Math.Clamp(limit, 1, 100);

        List<DailyHistoryEntry> entries;

        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateTime.TryParseExact(date.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
                return "Invalid date format. Use yyyy-MM-dd, e.g. 2025-06-01.";
            entries = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetHistoryForDay(parsedDate);
            }, ct);
        }
        else
        {
            entries = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetRecentHistory(limit);
            }, ct);
        }

        if (entries.Count == 0)
            return date is not null ? $"No history for {date}." : "No history found.";

        var sb = new System.Text.StringBuilder();
        if (date is not null)
            sb.AppendLine(CultureInfo.InvariantCulture, $"History for {date}:");
        foreach (var e in entries)
        {
            var listened = TimeSpan.FromSeconds(e.ListenedSeconds);
            var total = e.DurationSeconds.HasValue
                ? $" / {TimeSpan.FromSeconds(e.DurationSeconds.Value):h\\:mm\\:ss}"
                : string.Empty;
            var artist = e.Artist is not null ? $"  —  {e.Artist}" : string.Empty;
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{e.StartedAt:HH:mm}  [{e.MediaType}]  {e.Title}{artist}  ({listened:h\\:mm\\:ss}{total})");
        }
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string FormatState(PlayerState s)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Status: {s.Status}");
        if (s.Title is not null)
            sb.AppendLine(CultureInfo.InvariantCulture, $"Title: {s.Title}");
        if (s.Artist is not null)
            sb.AppendLine(CultureInfo.InvariantCulture, $"Artist: {s.Artist}");
        if (s.Album is not null)
            sb.AppendLine(CultureInfo.InvariantCulture, $"Album: {s.Album}");
        if (s.FilePath is not null)
            sb.AppendLine(CultureInfo.InvariantCulture, $"Path: {RedactKey(s.FilePath)}");
        if (s.DurationSeconds > 0)
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"Position: {TimeSpan.FromSeconds(s.PositionSeconds):hh\\:mm\\:ss} / {TimeSpan.FromSeconds(s.DurationSeconds):hh\\:mm\\:ss}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Volume: {s.Volume:P0}");
        if (s.QueueCount > 0)
            sb.AppendLine(CultureInfo.InvariantCulture, $"Queue: {s.QueueIndex + 1} / {s.QueueCount}");
        return sb.ToString().TrimEnd();
    }
}
