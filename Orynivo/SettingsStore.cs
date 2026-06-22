using System.IO;
using System.Text.Json;

namespace Orynivo;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as indented JSON at
/// <c>%LOCALAPPDATA%\Orynivo\settings.json</c>. Returns default settings on a missing or corrupt file.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _filePath;

    /// <summary>Initialises the store, creating the data directory if it does not exist.</summary>
    public SettingsStore()
    {
        var directory = AppPaths.DataRoot;
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "settings.json");
    }

    /// <summary>
    /// Reads and deserialises the settings file.
    /// Returns a default <see cref="AppSettings"/> instance when the file is missing or the JSON is invalid.
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath))
                ?? new AppSettings();
            NormalizeEqualizerProfiles(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>Serialises <paramref name="settings"/> and writes them to the settings file.</summary>
    /// <param name="settings">The settings object to persist.</param>
    public void Save(AppSettings settings)
    {
        NormalizeEqualizerProfiles(settings);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_filePath, json);
    }

    /// <summary>Normalizes multi-profile equalizer settings and migrates the legacy single profile.</summary>
    /// <param name="settings">Settings instance to normalize.</param>
    private static void NormalizeEqualizerProfiles(AppSettings settings)
    {
        settings.EqualizerProfiles ??= [];
        if (settings.EqualizerProfiles.Count == 0 && settings.EqualizerProfile is not null)
            settings.EqualizerProfiles.Add(settings.EqualizerProfile.Clone());

        if (string.IsNullOrWhiteSpace(settings.SelectedEqualizerProfileName)
            && settings.EqualizerProfile is not null)
        {
            settings.SelectedEqualizerProfileName = settings.EqualizerProfile.Name;
        }

        var selected = settings.EqualizerProfiles.FirstOrDefault(profile =>
            string.Equals(
                profile.Name,
                settings.SelectedEqualizerProfileName,
                StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            settings.SelectedEqualizerProfileName = null;
            settings.EqualizerProfile = null;
            settings.EqualizerEnabled = false;
            return;
        }

        settings.SelectedEqualizerProfileName = selected.Name;
        settings.EqualizerProfile = selected.Clone();
    }
}
