using System.IO;
using System.Text.Json;

namespace Orynivo.Library;

/// <summary>A single selectable value in a catalogue filter dropdown.</summary>
/// <param name="Value">Display label shown in the UI.</param>
/// <param name="Count">Optional station or podcast count associated with this option.</param>
/// <param name="Key">Optional machine-readable identifier, e.g. an Apple genre ID.</param>
public sealed record CatalogFilterOption(
    string Value,
    int? Count = null,
    string? Key = null);

/// <summary>
/// Persists radio genre and podcast category/language catalogue options in
/// <c>%LOCALAPPDATA%\Orynivo\catalog-filter-cache.json</c>.
/// Cached data remains usable while stale and is refreshed after seven days.
/// </summary>
public sealed class CatalogFilterCache
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private readonly string _filePath = AppPaths.GetDataPath("catalog-filter-cache.json");

    /// <summary>Loads the cached catalogue data from disk, or returns an empty instance on any error.</summary>
    public CatalogFilterCacheData Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new CatalogFilterCacheData();
            return JsonSerializer.Deserialize<CatalogFilterCacheData>(
                       File.ReadAllText(_filePath))
                   ?? new CatalogFilterCacheData();
        }
        catch
        {
            return new CatalogFilterCacheData();
        }
    }

    /// <summary>Serialises <paramref name="data"/> and writes it to the cache file.</summary>
    /// <param name="data">Data to persist.</param>
    public void Save(CatalogFilterCacheData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(
            _filePath,
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="updatedAt"/> is within the seven-day freshness window.</summary>
    /// <param name="updatedAt">Timestamp of the last successful catalogue refresh.</param>
    public static bool IsFresh(DateTimeOffset? updatedAt) =>
        updatedAt is not null && DateTimeOffset.UtcNow - updatedAt.Value < MaxAge;
}

/// <summary>Container for all cached catalogue filter options.</summary>
public sealed class CatalogFilterCacheData
{
    /// <summary>Timestamp of the last Radio Browser genre refresh.</summary>
    public DateTimeOffset? RadioGenresUpdatedAt { get; set; }

    /// <summary>Cached radio genre options.</summary>
    public List<CatalogFilterOption> RadioGenres { get; set; } = [];

    /// <summary>Timestamp of the last Apple Podcasts category refresh.</summary>
    public DateTimeOffset? PodcastCategoriesUpdatedAt { get; set; }

    /// <summary>Cached podcast category options.</summary>
    public List<CatalogFilterOption> PodcastCategories { get; set; } = [];

    /// <summary>Timestamp of the last podcast language refresh.</summary>
    public DateTimeOffset? PodcastLanguagesUpdatedAt { get; set; }

    /// <summary>Cached podcast language options.</summary>
    public List<CatalogFilterOption> PodcastLanguages { get; set; } = [];
}
