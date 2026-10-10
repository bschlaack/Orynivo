namespace Orynivo;

/// <summary>Retains bounded complete catalog results and rejects writes from invalidated loads.</summary>
/// <typeparam name="T">In-memory catalog row type.</typeparam>
internal sealed class LibraryViewCache<T>
{
    private readonly object _sync = new();
    private readonly Dictionary<string, (LibraryLoadResult<T> Result, long Access)> _entries = new(StringComparer.Ordinal);
    private readonly int _maximumEntries;
    private int _generation;
    private long _access;

    /// <summary>Creates a bounded session cache.</summary>
    /// <param name="maximumEntries">Positive maximum number of retained views.</param>
    internal LibraryViewCache(int maximumEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        _maximumEntries = maximumEntries;
    }

    /// <summary>Gets the catalog generation to capture before starting a load.</summary>
    internal int Generation { get { lock (_sync) return _generation; } }

    /// <summary>Reads and marks a current view as most recently used.</summary>
    /// <param name="key">Credential-free view and source configuration key.</param>
    /// <param name="generation">Generation captured by the caller.</param>
    /// <param name="result">Complete cached result when found.</param>
    /// <returns>Whether the requested current snapshot exists.</returns>
    internal bool TryGet(string key, int generation, out LibraryLoadResult<T>? result)
    {
        lock (_sync)
        {
            result = null;
            if (generation != _generation || !_entries.TryGetValue(key, out var entry))
                return false;
            _entries[key] = (entry.Result, ++_access);
            result = entry.Result;
            return true;
        }
    }

    /// <summary>Stores only complete results belonging to the current catalog generation.</summary>
    /// <param name="key">Credential-free view and source configuration key.</param>
    /// <param name="generation">Generation captured before loading.</param>
    /// <param name="result">Final sorted, merged result with all source outcomes.</param>
    /// <returns>Whether a complete current result backed by at least one loaded source was retained.</returns>
    internal bool TryStore(string key, int generation, LibraryLoadResult<T> result)
    {
        lock (_sync)
        {
            if (generation != _generation || result.Sources.Count == 0 || result.Status != LibraryLoadStatus.Complete)
                return false;
            _entries[key] = (result, ++_access);
            while (_entries.Count > _maximumEntries)
                _entries.Remove(_entries.MinBy(pair => pair.Value.Access).Key);
            return true;
        }
    }

    /// <summary>Atomically advances catalog identity and clears completed snapshots.</summary>
    internal void Invalidate()
    {
        lock (_sync)
        {
            _generation++;
            _entries.Clear();
        }
    }
}
