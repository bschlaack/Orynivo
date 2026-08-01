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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private readonly string _filePath;
    private readonly ApplicationCredentialStore _credentialStore;

    /// <summary>Initialises the store, creating the data directory if it does not exist.</summary>
    public SettingsStore()
    {
        var directory = AppPaths.DataRoot;
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "settings.json");
        _credentialStore = new ApplicationCredentialStore();
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
            ApplyCredentials(defaultSettings, _credentialStore.Load());
            NormalizeEqualizerProfiles(defaultSettings);
            NormalizeOutputProfiles(defaultSettings);
            NormalizeInfiniteMix(defaultSettings);
            return defaultSettings;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json)
                ?? new AppSettings();
            var credentials = _credentialStore.Load();
            var migrated = MergeLegacyCredentials(json, credentials);
            ApplyCredentials(settings, credentials);
            NormalizeEqualizerProfiles(settings);
            NormalizeOutputProfiles(settings);
            NormalizeInfiniteMix(settings);
            if (migrated)
            {
                _credentialStore.Save(credentials);
                RemoteServerCache.ClearLegacyCredentialBearingTrackLists();
                SaveSettingsJson(settings);
            }

            return settings;
        }
        catch
        {
            var defaultSettings = new AppSettings();
            NormalizeEqualizerProfiles(defaultSettings);
            NormalizeOutputProfiles(defaultSettings);
            NormalizeInfiniteMix(defaultSettings);
            return defaultSettings;
        }
    }

    /// <summary>Serialises <paramref name="settings"/> and writes them to the settings file.</summary>
    /// <param name="settings">The settings object to persist.</param>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        NormalizeEqualizerProfiles(settings);
        NormalizeOutputProfiles(settings);
        NormalizeInfiniteMix(settings);
        var credentials = _credentialStore.Load();
        CaptureCredentials(settings, credentials);
        _credentialStore.Save(credentials);
        SaveSettingsJson(settings);
    }

    /// <summary>Repairs missing or malformed Infinite Mix collection values from older settings files.</summary>
    /// <param name="settings">Settings whose Infinite Mix profile is normalized.</param>
    private static void NormalizeInfiniteMix(AppSettings settings)
    {
        settings.InfiniteMix ??= new InfiniteMixSettings();
        settings.InfiniteMix.EnabledServerIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        settings.InfiniteMix.IncludedGenres ??= [];
        settings.InfiniteMix.ExcludedGenres ??= [];
        settings.InfiniteMix.GenreFeedback ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        settings.InfiniteMix.ExcludedTrackKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        settings.InfiniteMix.DiscoveryLevel = Math.Clamp(settings.InfiniteMix.DiscoveryLevel, 0, 100);
        if (settings.InfiniteMix.HistoryDays is not (3 or 7 or 30 or 90))
            settings.InfiniteMix.HistoryDays = 30;
    }

    /// <summary>Copies decrypted credentials into their runtime settings objects.</summary>
    /// <param name="settings">Settings receiving credentials.</param>
    /// <param name="credentials">Decrypted credential snapshot.</param>
    private static void ApplyCredentials(
        AppSettings settings,
        ApplicationCredentialSnapshot credentials)
    {
        settings.LastFmApiKey = credentials.LastFmApiKey;
        settings.FanartTvApiKey = credentials.FanartTvApiKey;
        settings.AiChat ??= new AI.AiChatSettings();
        settings.AiChat.ApiKey = credentials.AiChatApiKey;
        settings.OrynivoServers ??= [];
        foreach (var server in settings.OrynivoServers)
        {
            server.ApiKey = credentials.OrynivoServerApiKeys.GetValueOrDefault(server.Id)
                ?? string.Empty;
        }
    }

    /// <summary>Copies runtime credentials into the encrypted snapshot.</summary>
    /// <param name="settings">Settings containing the current credential values.</param>
    /// <param name="credentials">Snapshot to update.</param>
    private static void CaptureCredentials(
        AppSettings settings,
        ApplicationCredentialSnapshot credentials)
    {
        credentials.LastFmApiKey = settings.LastFmApiKey?.Trim() ?? string.Empty;
        credentials.FanartTvApiKey = settings.FanartTvApiKey?.Trim() ?? string.Empty;
        credentials.AiChatApiKey = settings.AiChat?.ApiKey?.Trim() ?? string.Empty;
        credentials.OrynivoServerApiKeys = (settings.OrynivoServers ?? [])
            .Where(server =>
                !string.IsNullOrWhiteSpace(server.Id) &&
                !string.IsNullOrWhiteSpace(server.ApiKey))
            .ToDictionary(
                server => server.Id,
                server => server.ApiKey.Trim(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Imports credentials written by older releases from JSON into an encrypted snapshot.
    /// </summary>
    /// <param name="json">Legacy settings JSON.</param>
    /// <param name="credentials">Encrypted snapshot to update.</param>
    /// <returns><see langword="true"/> when at least one legacy secret was found.</returns>
    private static bool MergeLegacyCredentials(
        string json,
        ApplicationCredentialSnapshot credentials)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var found = false;

        found |= ImportString(root, "LastFmApiKey", value => credentials.LastFmApiKey = value);
        found |= ImportString(root, "FanartTvApiKey", value => credentials.FanartTvApiKey = value);
        if (TryGetProperty(root, "AiChat", out var aiChat) &&
            aiChat.ValueKind == JsonValueKind.Object)
        {
            found |= ImportString(aiChat, "ApiKey", value => credentials.AiChatApiKey = value);
        }

        if (TryGetProperty(root, "OrynivoServers", out var servers) &&
            servers.ValueKind == JsonValueKind.Array)
        {
            foreach (var server in servers.EnumerateArray())
            {
                if (server.ValueKind != JsonValueKind.Object ||
                    !TryReadString(server, "Id", out var id) ||
                    !TryReadString(server, "ApiKey", out var apiKey) ||
                    string.IsNullOrWhiteSpace(id) ||
                    string.IsNullOrWhiteSpace(apiKey))
                {
                    continue;
                }

                credentials.OrynivoServerApiKeys[id] = apiKey;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Imports one non-empty string property into the credential snapshot.</summary>
    /// <param name="element">JSON object to inspect.</param>
    /// <param name="propertyName">Property name to read case-insensitively.</param>
    /// <param name="assign">Callback receiving the imported value.</param>
    /// <returns><see langword="true"/> when a value was imported.</returns>
    private static bool ImportString(
        JsonElement element,
        string propertyName,
        Action<string> assign)
    {
        if (!TryReadString(element, propertyName, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        assign(value);
        return true;
    }

    /// <summary>Reads one string property from a JSON object case-insensitively.</summary>
    /// <param name="element">JSON object to inspect.</param>
    /// <param name="propertyName">Property name to locate.</param>
    /// <param name="value">Receives the property value when present.</param>
    /// <returns><see langword="true"/> when a string property was found.</returns>
    private static bool TryReadString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    /// <summary>Finds one property in a JSON object case-insensitively.</summary>
    /// <param name="element">JSON object to inspect.</param>
    /// <param name="propertyName">Property name to locate.</param>
    /// <param name="value">Receives the matching value.</param>
    /// <returns><see langword="true"/> when the property exists.</returns>
    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>Atomically serializes settings without any ignored secret properties.</summary>
    /// <param name="settings">Settings to persist.</param>
    private void SaveSettingsJson(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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
