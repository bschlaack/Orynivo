using Orynivo.Audio;

namespace Orynivo;

/// <summary>
/// Application settings persisted by <see cref="SettingsStore"/> as JSON.
/// Covers output device selection, library paths, UI preferences, and third-party API keys.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Gets or sets the audio output backend.</summary>
    public OutputBackend OutputBackend { get; set; } = OutputBackend.Wasapi;
    /// <summary>Gets or sets the selected ASIO/cwASIO driver name.</summary>
    public string? SelectedDriverName { get; set; }
    /// <summary>Gets or sets the MMDevice ID of the selected WASAPI render device.</summary>
    public string? SelectedWasapiDeviceId { get; set; }
    /// <summary>Gets or sets the display name of the selected WASAPI render device.</summary>
    public string? SelectedWasapiDeviceName { get; set; }
    /// <summary>Gets or sets the list of root directories scanned for audio files.</summary>
    public List<string> LibraryPaths { get; set; } = [];
    /// <summary>Gets or sets the identifier of the last active main-area view.</summary>
    public string LastMainView { get; set; } = "Tracks";
    /// <summary>Gets or sets a value indicating whether the album list uses the artwork grid view.</summary>
    public bool AlbumArtworkView { get; set; }
    /// <summary>Gets or sets a value indicating whether the artist list uses the artwork grid view.</summary>
    public bool ArtistArtworkView { get; set; }
    /// <summary>Gets or sets the master playback volume (0.0 – 1.0).</summary>
    public double Volume { get; set; } = 1.0;
    /// <summary>Gets or sets the ReplayGain mode used for PCM playback.</summary>
    public ReplayGainMode ReplayGainMode { get; set; } = ReplayGainMode.Off;
    /// <summary>Gets or sets a value indicating whether DSF and DFF sources always use the PCM playback path.</summary>
    public bool AlwaysConvertDsdToPcm { get; set; }
    /// <summary>Gets or sets a value indicating whether the imported PCM equalizer profile is active.</summary>
    public bool EqualizerEnabled { get; set; }
    /// <summary>Gets or sets the selected Equalizer APO or AutoEQ profile compatibility snapshot.</summary>
    public EqualizerProfile? EqualizerProfile { get; set; }
    /// <summary>Gets or sets all persisted parametric equalizer profiles.</summary>
    public List<EqualizerProfile> EqualizerProfiles { get; set; } = [];
    /// <summary>Gets or sets the name of the selected equalizer profile.</summary>
    public string? SelectedEqualizerProfileName { get; set; }
    /// <summary>Gets or sets user-adjusted table column widths grouped by stable table key.</summary>
    public Dictionary<string, List<double>> DataGridColumnWidths { get; set; } =
        new(StringComparer.Ordinal);
    /// <summary>Gets or sets visible selectable column identifiers grouped by stable table key.</summary>
    public Dictionary<string, List<string>> VisibleDataGridColumns { get; set; } =
        new(StringComparer.Ordinal);
    /// <summary>Gets or sets user-defined column orders grouped by stable table key.</summary>
    public Dictionary<string, List<string>> DataGridColumnOrders { get; set; } =
        new(StringComparer.Ordinal);
    /// <summary>Gets or sets the file path of the last played track, used to restore transport metadata on restart.</summary>
    public string? LastTrackPath { get; set; }
    /// <summary>Gets or sets the persisted playback queue in display order.</summary>
    public List<string> PlaybackQueuePaths { get; set; } = [];
    /// <summary>Gets or sets the zero-based current position in the persisted playback queue.</summary>
    public int PlaybackQueueIndex { get; set; } = -1;
    /// <summary>Gets or sets the application colour theme.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    /// <summary>Gets or sets the active UI language.</summary>
    public Localization.Language Language { get; set; } = Localization.Language.German;
    /// <summary>Gets or sets the source used to fetch artist biography text.</summary>
    public ArtistInfoSource ArtistInfoSource { get; set; } = ArtistInfoSource.Wikipedia;
    /// <summary>Gets or sets the Last.fm API key used when <see cref="ArtistInfoSource"/> is <see cref="ArtistInfoSource.LastFm"/>.</summary>
    public string LastFmApiKey { get; set; } = string.Empty;
    /// <summary>Gets or sets the Qobuz application ID for the streaming integration.</summary>
    public string QobuzApplicationId { get; set; } = string.Empty;
    /// <summary>Gets or sets the configured Plex Media Servers.</summary>
    public List<PlexServerSettings> PlexServers { get; set; } = [];
    /// <summary>Gets or sets a value indicating whether the Local Library sidebar section is visible.</summary>
    public bool ShowLocalLibrarySection { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the Own Radios sidebar section is visible.</summary>
    public bool ShowOwnRadiosSection { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the My Podcasts sidebar section is visible.</summary>
    public bool ShowMyPodcastsSection { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the Plex sidebar section is visible.</summary>
    public bool ShowPlexSection { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the Playlists sidebar section is visible.</summary>
    public bool ShowPlaylistsSection { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the Local Library section is expanded.</summary>
    public bool IsLocalLibrarySectionExpanded { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the Own Radios section is expanded.</summary>
    public bool IsOwnRadiosSectionExpanded { get; set; }
    /// <summary>Gets or sets a value indicating whether the My Podcasts section is expanded.</summary>
    public bool IsMyPodcastsSectionExpanded { get; set; }
    /// <summary>Gets or sets a value indicating whether the Plex section is expanded.</summary>
    public bool IsPlexSectionExpanded { get; set; }
    /// <summary>Gets or sets a value indicating whether the Playlists section is expanded.</summary>
    public bool IsPlaylistsSectionExpanded { get; set; }
}

/// <summary>Connection settings for a single Plex Media Server entry.</summary>
public sealed class PlexServerSettings
{
    /// <summary>Gets or sets the unique server identifier (GUID, no dashes).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Gets or sets the user-chosen display name for this server.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the base URL of the Plex server (e.g. <c>http://192.168.1.10:32400</c>).</summary>
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>Application colour theme.</summary>
public enum AppTheme
{
    /// <summary>Light colour scheme.</summary>
    Light,
    /// <summary>Dark colour scheme.</summary>
    Dark
}

/// <summary>Data source for artist biography text.</summary>
public enum ArtistInfoSource
{
    /// <summary>Fetch artist biography from Wikipedia.</summary>
    Wikipedia,
    /// <summary>Fetch artist biography from Last.fm (requires an API key).</summary>
    LastFm
}
