using System.Text.Json;
using Orynivo.Mcp;

namespace Orynivo.AI;

/// <summary>
/// Dispatches tool calls from the AI chat to <see cref="McpTools"/> methods using
/// JSON arguments supplied by the model. No MCP transport is involved; tools are
/// invoked directly against the player bridge and library database.
/// </summary>
/// <param name="tools">Shared MCP tool implementations used by both the MCP server and the embedded AI chat.</param>
internal sealed class AiToolExecutor(McpTools tools)
{
    /// <summary>Executes a tool call and returns its result as a string.</summary>
    /// <param name="name">Tool name as defined in <see cref="AiToolDefinitions"/>.</param>
    /// <param name="argumentsJson">JSON object string containing the tool arguments.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tool's text result.</returns>
    public Task<string> ExecuteAsync(string name, string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        var args = doc.RootElement;

        return name switch
        {
            "get_now_playing"       => tools.GetNowPlayingAsync(ct),
            "get_queue"             => tools.GetQueueAsync(ct),
            "play"                  => tools.PlayAsync(Str(args, "path"), ct),
            "pause_resume"          => tools.PauseResumeAsync(ct),
            "next_track"            => tools.NextTrackAsync(ct),
            "previous_track"        => tools.PreviousTrackAsync(ct),
            "stop"                  => tools.StopAsync(ct),
            "seek"                  => tools.SeekAsync(Dbl(args, "positionSeconds", 0), ct),
            "set_volume"            => tools.SetVolumeAsync(Dbl(args, "volume", 1.0), ct),
            "queue_append"          => tools.QueueAppendAsync(Str(args, "path") ?? "", ct),
            "queue_play_next"       => tools.QueuePlayNextAsync(Str(args, "path") ?? "", ct),
            "clear_queue"           => tools.ClearQueueAsync(ct),
            "replace_queue"         => tools.ReplaceQueueAsync(
                                           args.TryGetProperty("paths", out var pathsEl)
                                               ? pathsEl.EnumerateArray()
                                                   .Select(e => e.GetString() ?? "")
                                                   .Where(s => s.Length > 0)
                                                   .ToArray()
                                               : [],
                                           ct),
            "search_library"        => tools.SearchLibraryAsync(Str(args, "query") ?? "", Int(args, "limit", 10), ct),
            "list_playlists"        => tools.ListPlaylistsAsync(ct),
            "get_playlist_tracks"   => tools.GetPlaylistTracksAsync(Str(args, "playlist") ?? "", ct),
            "create_playlist"       => tools.CreatePlaylistAsync(Str(args, "name") ?? "", Str(args, "paths"), ct),
            "create_smart_playlist" => tools.CreateSmartPlaylistAsync(
                                           Str(args, "name") ?? "",
                                           Bool(args, "favoritesOnly", false),
                                           Str(args, "genres"),
                                           Str(args, "artistContains"),
                                           Str(args, "albumContains"),
                                           OptInt(args, "addedWithinDays"),
                                           Bool(args, "neverPlayed", false),
                                           Str(args, "sortOrder") ?? "title",
                                           OptInt(args, "resultLimit"),
                                           ct),
            "get_play_history"      => tools.GetPlayHistoryAsync(Str(args, "date"), Int(args, "limit", 20), ct),
            _                       => Task.FromResult($"Unknown tool: {name}")
        };
    }

    private static string? Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static double Dbl(JsonElement e, string key, double def) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : def;

    private static int Int(JsonElement e, string key, int def) =>
        e.TryGetProperty(key, out var v) && v.TryGetInt32(out var i) ? i : def;

    private static bool Bool(JsonElement e, string key, bool def)
    {
        if (!e.TryGetProperty(key, out var v)) return def;
        return v.ValueKind == JsonValueKind.True;
    }

    private static int? OptInt(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.TryGetInt32(out var i) ? i : null;
}
