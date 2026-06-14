using System.IO;
using System.Text.Json;

namespace Orynivo;

public sealed class SettingsStore
{
    private readonly string _filePath;

    public SettingsStore()
    {
        var directory = AppPaths.DataRoot;
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath))
                ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_filePath, json);
    }
}
