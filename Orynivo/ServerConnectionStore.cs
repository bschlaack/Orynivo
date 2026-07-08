using System.IO;
using System.Text.Json;

namespace Orynivo;

/// <summary>
/// Persists the timestamp of the last successful connection to each configured remote
/// server (Orynivo Server or Plex), keyed by the server's stable identifier. The data is
/// stored in a small standalone JSON file so recording a connection never contends with
/// the main <c>settings.json</c> write flow.
/// </summary>
internal static class ServerConnectionStore
{
    private static readonly object Sync = new();
    private static readonly string FilePath = AppPaths.GetDataPath("server-status.json");
    private static Dictionary<string, long>? _cache;

    /// <summary>Returns the Unix-second timestamp of the last successful connection to a server.</summary>
    /// <param name="serverId">Stable server identifier.</param>
    /// <returns>The last successful-connection timestamp, or <see langword="null"/> when never recorded.</returns>
    public static long? GetLastConnected(string serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId))
            return null;
        lock (Sync)
        {
            var map = Load();
            return map.TryGetValue(serverId, out var value) ? value : null;
        }
    }

    /// <summary>Records that a server was successfully reached just now.</summary>
    /// <param name="serverId">Stable server identifier.</param>
    public static void RecordSuccess(string serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId))
            return;
        lock (Sync)
        {
            var map = Load();
            map[serverId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Save(map);
        }
    }

    private static Dictionary<string, long> Load()
    {
        if (_cache is not null)
            return _cache;
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _cache = JsonSerializer.Deserialize<Dictionary<string, long>>(json)
                         ?? new Dictionary<string, long>(StringComparer.Ordinal);
            }
            else
            {
                _cache = new Dictionary<string, long>(StringComparer.Ordinal);
            }
        }
        catch
        {
            _cache = new Dictionary<string, long>(StringComparer.Ordinal);
        }
        return _cache;
    }

    private static void Save(Dictionary<string, long> map)
    {
        try
        {
            var json = JsonSerializer.Serialize(map);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best-effort persistence: a failed write must never affect the UI.
        }
    }
}
