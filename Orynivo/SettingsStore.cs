using System.IO;
using System.Text.Json;
using Orynivo.Audio;

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
            var defaultSettings = new AppSettings();
            NormalizeEqualizerProfiles(defaultSettings);
            NormalizeOutputProfiles(defaultSettings);
            return defaultSettings;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath))
                ?? new AppSettings();
            NormalizeEqualizerProfiles(settings);
            NormalizeOutputProfiles(settings);
            return settings;
        }
        catch
        {
            var defaultSettings = new AppSettings();
            NormalizeEqualizerProfiles(defaultSettings);
            NormalizeOutputProfiles(defaultSettings);
            return defaultSettings;
        }
    }

    /// <summary>Serialises <paramref name="settings"/> and writes them to the settings file.</summary>
    /// <param name="settings">The settings object to persist.</param>
    public void Save(AppSettings settings)
    {
        NormalizeEqualizerProfiles(settings);
        NormalizeOutputProfiles(settings);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_filePath, json);
    }

    /// <summary>
    /// Normalizes named output profiles and migrates a previously configured single device to a profile.
    /// Also derives the flat <see cref="AppSettings.OutputBackend"/> and device fields from the active profile.
    /// </summary>
    /// <param name="settings">Settings instance to normalize.</param>
    private static void NormalizeOutputProfiles(AppSettings settings)
    {
        settings.OutputProfiles ??= [];

        if (settings.OutputProfiles.Count == 0 &&
            (!string.IsNullOrEmpty(settings.SelectedDriverName) ||
             !string.IsNullOrEmpty(settings.SelectedWasapiDeviceId)))
        {
            var legacy = new OutputProfile
            {
                Name = "Standard",
                Backend = settings.OutputBackend,
                SelectedDriverName = settings.SelectedDriverName,
                SelectedWasapiDeviceId = settings.SelectedWasapiDeviceId,
                SelectedWasapiDeviceName = settings.SelectedWasapiDeviceName
            };
            settings.OutputProfiles.Add(legacy);
            settings.SelectedOutputProfileName = legacy.Name;
        }

        var selected = settings.OutputProfiles.FirstOrDefault(p =>
            string.Equals(p.Name, settings.SelectedOutputProfileName, StringComparison.OrdinalIgnoreCase) &&
            IsUsableOutputProfile(p))
            ?? settings.OutputProfiles.FirstOrDefault(IsUsableOutputProfile);

        if (selected is null)
        {
            selected = CreateDefaultWasapiProfile(settings.OutputProfiles);
            if (selected is null)
            {
                settings.SelectedOutputProfileName = null;
                settings.OutputBackend = OutputBackend.Wasapi;
                settings.SelectedDriverName = null;
                settings.SelectedWasapiDeviceId = null;
                settings.SelectedWasapiDeviceName = null;
                return;
            }
        }

        settings.SelectedOutputProfileName = selected.Name;
        settings.OutputBackend = selected.Backend;
        settings.SelectedDriverName = selected.SelectedDriverName;
        settings.SelectedWasapiDeviceId = selected.SelectedWasapiDeviceId;
        settings.SelectedWasapiDeviceName = selected.SelectedWasapiDeviceName;
    }

    /// <summary>
    /// Determines whether an output profile contains the minimum device selection needed for playback.
    /// </summary>
    /// <param name="profile">Output profile to inspect.</param>
    /// <returns><see langword="true"/> when the profile can be selected for playback; otherwise <see langword="false"/>.</returns>
    private static bool IsUsableOutputProfile(OutputProfile profile) =>
        profile.Backend switch
        {
            OutputBackend.Wasapi => !string.IsNullOrWhiteSpace(profile.SelectedWasapiDeviceId),
            OutputBackend.Asio or OutputBackend.CwAsio => !string.IsNullOrWhiteSpace(profile.SelectedDriverName),
            _ => false
        };

    /// <summary>
    /// Creates or updates the first-run WASAPI output profile from the default Windows render endpoint when available.
    /// </summary>
    /// <param name="profiles">Existing output profiles to update when a profile named <c>Default</c> already exists.</param>
    /// <returns>The default output profile, or <see langword="null"/> when no WASAPI device is available.</returns>
    private static OutputProfile? CreateDefaultWasapiProfile(IList<OutputProfile> profiles)
    {
        try
        {
            var device = WasapiDeviceProvider.GetDefaultRenderDevice();
            if (device is null)
            {
                return null;
            }

            var profile = profiles.FirstOrDefault(p =>
                string.Equals(p.Name, "Default", StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                profile = new OutputProfile { Name = "Default" };
                profiles.Add(profile);
            }

            profile.Backend = OutputBackend.Wasapi;
            profile.SelectedDriverName = null;
            profile.SelectedWasapiDeviceId = device.Id;
            profile.SelectedWasapiDeviceName = device.Name;
            return profile;
        }
        catch
        {
            return null;
        }
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
