using System.IO;
using System.Text.Json;

namespace Orynivo.Library;

public sealed record CatalogFilterOption(
    string Value,
    int? Count = null,
    string? Key = null);

public sealed class CatalogFilterCache
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private readonly string _filePath = AppPaths.GetDataPath("catalog-filter-cache.json");

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

    public void Save(CatalogFilterCacheData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(
            _filePath,
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool IsFresh(DateTimeOffset? updatedAt) =>
        updatedAt is not null && DateTimeOffset.UtcNow - updatedAt.Value < MaxAge;
}

public sealed class CatalogFilterCacheData
{
    public DateTimeOffset? RadioGenresUpdatedAt { get; set; }
    public List<CatalogFilterOption> RadioGenres { get; set; } = [];
    public DateTimeOffset? PodcastCategoriesUpdatedAt { get; set; }
    public List<CatalogFilterOption> PodcastCategories { get; set; } = [];
    public DateTimeOffset? PodcastLanguagesUpdatedAt { get; set; }
    public List<CatalogFilterOption> PodcastLanguages { get; set; } = [];
}
