using System.Text.Json.Nodes;

namespace Orynivo.AI;

/// <summary>
/// Builds OpenAI function-calling tool definitions for all 23 Orynivo player tools.
/// Each definition matches a method in <see cref="Orynivo.Mcp.McpTools"/> and is
/// forwarded to the chat-completions API so the model can invoke tools.
/// </summary>
internal static class AiToolDefinitions
{
    /// <summary>Returns the complete list of tool definitions.</summary>
    /// <returns>A list of JSON objects describing each tool in OpenAI function-calling format.</returns>
    public static List<JsonObject> GetAll() =>
    [
        Make("get_now_playing",
            "Returns the current playback state: status, track title, artist, album, position, duration, volume, and queue position.",
            new JsonObject()),

        Make("get_queue",
            "Returns all entries in the current playback queue with their file names and paths.",
            new JsonObject()),

        Make("get_current_time",
            "Returns the current date and time: local time, day of week, ISO 8601 form, UTC time, and the local time-zone name.",
            new JsonObject()),

        Make("search_web",
            "Searches the web through the configured SearXNG instance and returns the top results (title, URL, snippet). Follow up with fetch_page to read a result.",
            new JsonObject
            {
                ["query"]      = Str("The search query."),
                ["maxResults"] = Int("Maximum number of results (1–25, default 5).")
            },
            ["query"]),

        Make("fetch_page",
            "Fetches an http/https page and returns its readable plain text. Private/loopback addresses, non-HTTP schemes, and non-text content are blocked for safety.",
            new JsonObject
            {
                ["url"] = Str("Absolute http or https URL of the page to fetch.")
            },
            ["url"]),

        Make("fetch_page_as_markdown",
            "Fetches an http/https page and returns a compact Markdown rendering that preserves headings, links, and lists. Same safety limits as fetch_page.",
            new JsonObject
            {
                ["url"] = Str("Absolute http or https URL of the page to fetch.")
            },
            ["url"]),

        Make("play",
            "Plays a local audio file by its absolute path. Omit the path to resume the current paused track.",
            new JsonObject
            {
                ["path"] = Str("Absolute path of the audio file to play. Leave empty to resume.")
            }),

        Make("pause_resume",
            "Toggles between pause and resume. Has no effect when nothing is playing.",
            new JsonObject()),

        Make("next_track",
            "Skips to the next track in the playback queue.",
            new JsonObject()),

        Make("previous_track",
            "Goes back to the previous track in the playback queue.",
            new JsonObject()),

        Make("stop",
            "Stops playback immediately.",
            new JsonObject()),

        Make("seek",
            "Seeks to the given position in the currently playing track.",
            new JsonObject
            {
                ["positionSeconds"] = Num("Target position in seconds.")
            },
            ["positionSeconds"]),

        Make("set_volume",
            "Sets the playback volume. Use 0.0 for mute and 1.0 for maximum volume.",
            new JsonObject
            {
                ["volume"] = Num("Volume level between 0.0 and 1.0.")
            },
            ["volume"]),

        Make("queue_append",
            "Appends a local audio file to the end of the playback queue.",
            new JsonObject
            {
                ["path"] = Str("Absolute path of the audio file to append.")
            },
            ["path"]),

        Make("queue_play_next",
            "Inserts a local audio file immediately after the current queue position so it plays next.",
            new JsonObject
            {
                ["path"] = Str("Absolute path of the audio file to play next.")
            },
            ["path"]),

        Make("clear_queue",
            "Clears all items from the playback queue. The current track continues playing until it finishes, after which playback stops. Use this when the user wants to empty the queue without starting new content.",
            new JsonObject()),

        Make("replace_queue",
            "Replaces the entire playback queue with the given list of audio file paths and immediately starts playing the first track. Use this when the user wants to play a specific set of tracks — it removes whatever was queued before. Use queue_append to add to the existing queue instead.",
            new JsonObject
            {
                ["paths"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Ordered list of absolute audio file paths to set as the new queue."
                }
            },
            ["paths"]),

        Make("search_library",
            "Searches the local music library for tracks, albums, and artists. Returns file paths that can be passed to play or queue tools.",
            new JsonObject
            {
                ["query"] = Str("Free-text search query, e.g. an artist name, album title, or track title."),
                ["limit"] = Int("Maximum number of results per category (1–50, default 10).")
            },
            ["query"]),

        Make("list_playlists",
            "Lists all playlists in the library, including regular and smart playlists, with their IDs, track counts, and types.",
            new JsonObject()),

        Make("get_playlist_tracks",
            "Returns the tracks of a playlist. Provide either the playlist name (case-insensitive) or its numeric ID from list_playlists.",
            new JsonObject
            {
                ["playlist"] = Str("Playlist name (case-insensitive) or numeric ID.")
            },
            ["playlist"]),

        Make("create_playlist",
            "Creates a new regular playlist. Optionally supply a comma-separated list of absolute file paths to add as initial tracks.",
            new JsonObject
            {
                ["name"]  = Str("Name for the new playlist."),
                ["paths"] = Str("Comma-separated absolute file paths to include. Leave empty to create an empty playlist.")
            },
            ["name"]),

        Make("create_smart_playlist",
            "Creates a new smart playlist that resolves tracks dynamically from the library based on filter criteria.",
            new JsonObject
            {
                ["name"]           = Str("Name for the new smart playlist."),
                ["favoritesOnly"]  = Bool("Include only tracks marked as favorites."),
                ["genres"]         = Str("Comma-separated genre names to filter by. Leave empty for all genres."),
                ["artistContains"] = Str("Case-insensitive substring to match in artist names."),
                ["albumContains"]  = Str("Case-insensitive substring to match in album titles."),
                ["addedWithinDays"]= Int("Include only tracks added within this many days."),
                ["neverPlayed"]    = Bool("Include only tracks with no recorded playback."),
                ["sortOrder"]      = Str("Sort order: title, random, recent, leastrecent."),
                ["resultLimit"]    = Int("Maximum number of tracks to include.")
            },
            ["name"]),

        Make("get_play_history",
            "Returns playback history. Supply a date (yyyy-MM-dd) to get entries for that day, or omit it for the most recent entries.",
            new JsonObject
            {
                ["date"]  = Str("Optional date in yyyy-MM-dd format (e.g. 2025-06-01). Omit to get recent history."),
                ["limit"] = Int("Maximum number of entries when no date is provided (1–100, default 20).")
            })
    ];

    private static JsonObject Str(string description) =>
        new() { ["type"] = "string", ["description"] = description };

    private static JsonObject Num(string description) =>
        new() { ["type"] = "number", ["description"] = description };

    private static JsonObject Int(string description) =>
        new() { ["type"] = "integer", ["description"] = description };

    private static JsonObject Bool(string description) =>
        new() { ["type"] = "boolean", ["description"] = description };

    private static JsonObject Make(
        string name,
        string description,
        JsonObject properties,
        string[]? required = null)
    {
        var parameters = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required is { Length: > 0 })
            parameters["required"] = new JsonArray(required.Select(r => JsonValue.Create(r)!).ToArray());

        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = parameters
            }
        };
    }
}
