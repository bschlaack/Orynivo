namespace Orynivo.Remote;

/// <summary>Maps bounded opaque artwork keys to trusted catalog sources without exposing locations.</summary>
internal sealed class MobileLibraryArtworkCatalog
{
    private readonly object _sync = new();
    private readonly Dictionary<string, string> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);
    private const int Capacity = 2048;

    /// <summary>Registers a catalog-provided image, retaining no more than the bounded key capacity.</summary>
    /// <param name="location">Trusted local path or authenticated source URL.</param>
    /// <returns>A random opaque key, or null when no image exists.</returns>
    internal string? Register(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        lock (_sync)
        {
            if (_keys.TryGetValue(location, out var existing)) return existing;
            while (_sources.Count >= Capacity)
            {
                var oldest = _sources.First();
                _sources.Remove(oldest.Key); _keys.Remove(oldest.Value);
            }
            var key = Guid.NewGuid().ToString("N");
            _sources.Add(key, location); _keys.Add(location, key);
            return key;
        }
    }

    /// <summary>Resolves only previously registered keys; browser input never becomes a source location.</summary>
    /// <param name="key">Opaque catalog artwork key.</param>
    /// <returns>The trusted location, or null for unknown or evicted entries.</returns>
    internal string? Resolve(string key)
    {
        lock (_sync) return _sources.GetValueOrDefault(key);
    }
}
