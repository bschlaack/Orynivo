using System.Text.Json;

namespace Orynivo.Scrobbling;

/// <summary>Describes one Last.fm scrobble awaiting submission.</summary>
/// <param name="Artist">Primary track artist.</param>
/// <param name="Title">Track title.</param>
/// <param name="Album">Album title, or <see langword="null"/>.</param>
/// <param name="Duration">Known duration in seconds, or <see langword="null"/>.</param>
/// <param name="PlayedAtUnix">Playback start time in Unix seconds.</param>
internal sealed record PendingScrobble(
    string Artist,
    string Title,
    string? Album,
    int? Duration,
    long PlayedAtUnix);

/// <summary>
/// Persists Last.fm scrobbles that could not be submitted immediately so they can
/// be flushed once the network is available.
/// </summary>
internal sealed class PendingScrobbleStore
{
    /// <summary>The maximum number of queued scrobbles retained.</summary>
    internal const int MaximumEntries = 500;

    private readonly string _path;

    /// <summary>Initializes a store backed by the supplied file path.</summary>
    /// <param name="path">Absolute path of the pending-scrobble JSON file.</param>
    internal PendingScrobbleStore(string path) => _path = path;

    /// <summary>Loads the queued scrobbles in submission order.</summary>
    /// <returns>The queued scrobbles, or an empty list when none are readable.</returns>
    internal IReadOnlyList<PendingScrobble> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return [];
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<PendingScrobble>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Overwrites the queued scrobbles.</summary>
    /// <param name="entries">Entries to persist, already trimmed.</param>
    internal void Save(IReadOnlyList<PendingScrobble> entries)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(entries));
        }
        catch
        {
        }
    }

    /// <summary>Appends one scrobble and trims the queue to its maximum length.</summary>
    /// <param name="entry">Entry to enqueue.</param>
    internal void Enqueue(PendingScrobble entry)
    {
        var entries = Load().ToList();
        entries.Add(entry);
        Save(Trim(entries));
    }

    /// <summary>Removes every entry, used after a successful flush.</summary>
    internal void Clear() => Save([]);

    /// <summary>Retains only the newest entries when the queue exceeds its bound.</summary>
    /// <param name="entries">Entries in submission order.</param>
    /// <returns>The trimmed entries.</returns>
    internal static IReadOnlyList<PendingScrobble> Trim(IReadOnlyList<PendingScrobble> entries)
        => entries.Count <= MaximumEntries
            ? entries
            : entries.Skip(entries.Count - MaximumEntries).ToList();
}
