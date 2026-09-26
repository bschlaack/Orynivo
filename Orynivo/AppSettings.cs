using Orynivo.AI;
using Orynivo.Audio;
using Orynivo.Library;
using Orynivo.Streaming;
using Orynivo.Web;
using System.Text.Json.Serialization;

namespace Orynivo;

/// <summary>
/// Application settings persisted by <see cref="SettingsStore"/> as JSON.
/// Covers output device selection, library paths, UI preferences, and permitted provider settings.
/// Secrets marked with <see cref="JsonIgnoreAttribute"/> are persisted separately by
/// <see cref="ApplicationCredentialStore"/> and never written to the JSON settings file.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Gets or sets all named audio output profiles.</summary>
    public List<OutputProfile> OutputProfiles { get; set; } = [];
    /// <summary>Gets or sets the name of the currently selected output profile.</summary>
    public string? SelectedOutputProfileName { get; set; }
    /// <summary>Gets or sets the audio output backend (derived from the selected profile on load).</summary>
    public OutputBackend OutputBackend { get; set; } = OutputBackend.Wasapi;
    /// <summary>Gets or sets the selected ASIO/cwASIO driver name (derived from the selected profile on load).</summary>
    public string? SelectedDriverName { get; set; }
    /// <summary>Gets or sets the MMDevice ID of the selected WASAPI render device (derived from the selected profile on load).</summary>
    public string? SelectedWasapiDeviceId { get; set; }
    /// <summary>Gets or sets the display name of the selected WASAPI render device (derived from the selected profile on load).</summary>
    public string? SelectedWasapiDeviceName { get; set; }
    /// <summary>Gets or sets the selected AirPlay DNS-SD service name (derived from the selected profile).</summary>
    public string? SelectedAirPlayDeviceId { get; set; }
    /// <summary>Gets or sets the selected AirPlay receiver display name (derived from the selected profile).</summary>
    public string? SelectedAirPlayDeviceName { get; set; }
    /// <summary>Gets or sets the last resolved AirPlay receiver address (derived from the selected profile).</summary>
    public string? SelectedAirPlayHost { get; set; }
    /// <summary>Gets or sets the last resolved AirPlay receiver port (derived from the selected profile).</summary>
    public int SelectedAirPlayPort { get; set; }
    /// <summary>Gets or sets the list of root directories scanned for audio files.</summary>
    public List<string> LibraryPaths { get; set; } = [];
    /// <summary>Gets or sets the locally defined user profiles.</summary>
    public List<UserProfile> UserProfiles { get; set; } = [];
    /// <summary>Gets or sets whether the first-start profile setup has been completed.</summary>
    public bool UserProfilesInitialized { get; set; }
    /// <summary>Gets or sets the identifier of the currently active user profile.</summary>
    public string ActiveUserProfileId { get; set; } = string.Empty;
    /// <summary>Gets or sets the identifier of the last active main-area view.</summary>
    public string LastMainView { get; set; } = "Tracks";
    /// <summary>Gets or sets a value indicating whether the album list uses the artwork grid view.</summary>
    public bool AlbumArtworkView { get; set; }
    /// <summary>Gets or sets a value indicating whether the artist list uses the artwork grid view.</summary>
    public bool ArtistArtworkView { get; set; }
    /// <summary>Gets or sets a value indicating whether Dashboard album recommendations use the stage view instead of the list.</summary>
    public bool DashboardRecommendationStageView { get; set; } = true;
    /// <summary>Gets or sets the master playback volume (0.0 – 1.0).</summary>
    public double Volume { get; set; } = 1.0;
    /// <summary>Gets or sets the ReplayGain mode used for PCM playback.</summary>
    public ReplayGainMode ReplayGainMode { get; set; } = ReplayGainMode.Off;
    /// <summary>Gets or sets a value indicating whether library scans calculate missing ReplayGain values through FFmpeg.</summary>
    public bool CalculateMissingReplayGainDuringScan { get; set; }
    /// <summary>Gets or sets a value indicating whether DSF and DFF sources always use the PCM playback path.</summary>
    public bool AlwaysConvertDsdToPcm { get; set; }
    /// <summary>Gets or sets a value indicating whether DSD sources are transported as bit-perfect DoP frames.</summary>
    public bool DsdOverPcmEnabled { get; set; }
    /// <summary>Gets or sets a value indicating whether PCM playback receives an additional +6 dB output boost.</summary>
    /// <summary>
    /// Gets or sets the maximum PCM output sample rate in hertz. Zero leaves the choice to
    /// the device and lets the player use the highest rate it can fill. A lower value caps
    /// the exclusive-mode output, which is useful when a driver advertises a rate it cannot
    /// reproduce cleanly.
    /// </summary>
    public int MaxOutputSampleRateHz { get; set; }

    /// <summary>
    /// Gets or sets the folder the visualizer loads user presets from. An empty value uses
    /// the per-user default folder.
    /// </summary>
    public string VisualizerPresetDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the visualizer always shows its title, hint,
    /// and playback buttons. When <see langword="false"/> they appear only while the mouse
    /// moves over the visualizer window.
    /// </summary>
    public bool VisualizerAlwaysShowOverlay { get; set; } = true;

    /// <summary>Gets or sets the width in pixels the visualizer renders at before scaling up.</summary>
    public int VisualizerRenderWidth { get; set; } = 640;

    /// <summary>Gets or sets the height in pixels the visualizer renders at before scaling up.</summary>
    public int VisualizerRenderHeight { get; set; } = 360;

    /// <summary>Gets or sets the visualizer target frame rate.</summary>
    public int VisualizerFrameRate { get; set; } = 60;

    /// <summary>Gets or sets a value indicating whether the visualizer advances to the next preset automatically.</summary>
    public bool VisualizerAutoAdvanceEnabled { get; set; }

    /// <summary>Gets or sets how many seconds the visualizer shows one preset before advancing.</summary>
    public int VisualizerAutoAdvanceSeconds { get; set; } = 15;

    /// <summary>
    /// Gets or sets the stable keys of the visualizer presets the user deactivated. The visualizer
    /// skips them while browsing and during automatic advance. A built-in preset uses the key
    /// <c>builtin:&lt;name&gt;</c>; a user preset file uses <c>file:&lt;path relative to the preset
    /// folder&gt;</c>, so deactivating a file deactivates every section it contains.
    /// </summary>
    public List<string> DisabledVisualizerPresets { get; set; } = [];
    public bool PcmOutputBoostEnabled { get; set; }
    /// <summary>Gets or sets the fade duration used when advancing non-gapless queues, in seconds.</summary>
    public double NonGaplessCrossfadeSeconds { get; set; }
    /// <summary>Gets or sets a value indicating whether radio and podcast streams are loudness-normalized.</summary>
    public bool StreamingLoudnessNormalizationEnabled { get; set; }
    /// <summary>Gets or sets a value indicating whether headphone crossfeed is applied to PCM playback.</summary>
    public bool CrossfeedEnabled { get; set; }
    /// <summary>Gets or sets the selected headphone crossfeed strength.</summary>
    public CrossfeedStrength CrossfeedStrength { get; set; } = CrossfeedStrength.Medium;
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
    /// <summary>Gets or sets the legacy JSON playback queue imported into SQLite on startup.</summary>
    public List<string> PlaybackQueuePaths { get; set; } = [];
    /// <summary>Gets or sets the legacy JSON queue position imported into SQLite on startup.</summary>
    public int PlaybackQueueIndex { get; set; } = -1;
    /// <summary>Gets or sets the application colour theme.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    /// <summary>Gets or sets the artwork source used behind the Genre Cloud.</summary>
    public GenreCloudBackgroundMode GenreCloudBackground { get; set; } = GenreCloudBackgroundMode.Artists;
    /// <summary>Gets or sets the Genre Cloud tile opacity from zero (hidden) to one (fully visible).</summary>
    public double GenreCloudBackgroundOpacity { get; set; } = 0.5;
    /// <summary>Gets or sets the active UI language.</summary>
    public Localization.Language Language { get; set; } = Localization.Language.German;
    /// <summary>Gets or sets the source used to fetch artist biography text.</summary>
    public ArtistInfoSource ArtistInfoSource { get; set; } = ArtistInfoSource.Wikipedia;
    /// <summary>Gets or sets the Last.fm API key used when <see cref="ArtistInfoSource"/> is <see cref="ArtistInfoSource.LastFm"/>.</summary>
    [JsonIgnore]
    public string LastFmApiKey { get; set; } = string.Empty;
    /// <summary>Gets or sets a value indicating whether completed tracks are scrobbled to Last.fm.</summary>
    public bool LastFmScrobblingEnabled { get; set; }
    /// <summary>Gets or sets the connected Last.fm username shown in Settings.</summary>
    public string LastFmUsername { get; set; } = string.Empty;
    /// <summary>Gets or sets the Last.fm API secret overlaid from the encrypted credential store.</summary>
    [JsonIgnore]
    public string LastFmApiSecret { get; set; } = string.Empty;
    /// <summary>Gets or sets the Last.fm session key overlaid from the encrypted credential store.</summary>
    [JsonIgnore]
    public string LastFmSessionKey { get; set; } = string.Empty;
    /// <summary>Gets or sets the Fanart.tv API key used for preferred curated artist thumbnails.</summary>
    [JsonIgnore]
    public string FanartTvApiKey { get; set; } = string.Empty;
    /// <summary>Gets or sets the Qobuz application ID for the streaming integration.</summary>
    public string QobuzApplicationId { get; set; } = string.Empty;
    /// <summary>Gets or sets the configured Plex Media Servers.</summary>
    public List<PlexServerSettings> PlexServers { get; set; } = [];
    /// <summary>Gets or sets the configured remote Orynivo Server instances.</summary>
    public List<OrynivoServerSettings> OrynivoServers { get; set; } = [];
    /// <summary>Gets or sets client-side favorite identifiers for remote Orynivo Server entities.</summary>
    public HashSet<string> OrynivoServerFavorites { get; set; } = [];
    /// <summary>Gets or sets persisted Infinite Mix preferences and recommendation feedback.</summary>
    public InfiniteMixSettings InfiniteMix { get; set; } = new();
    /// <summary>Gets or sets a value indicating whether the MCP server is enabled.</summary>
    public bool McpServerEnabled { get; set; }
    /// <summary>Gets or sets the TCP port the MCP server listens on.</summary>
    public int McpServerPort { get; set; } = 49200;
    /// <summary>Gets or sets a value indicating whether the MCP server accepts connections from the local network.</summary>
    public bool McpNetworkAccessEnabled { get; set; }
    /// <summary>Gets or sets the bearer token required when MCP network access is enabled.</summary>
    public string McpAccessToken { get; set; } = string.Empty;
    /// <summary>Gets or sets whether the mobile web remote is available on the local network.</summary>
    public bool MobileRemoteEnabled { get; set; }
    /// <summary>Gets or sets the TCP port used by the mobile web remote.</summary>
    public int MobileRemotePort { get; set; } = 49201;
    /// <summary>Gets or sets the mobile bearer token overlaid from the encrypted credential store.</summary>
    [JsonIgnore]
    public string MobileRemoteAccessToken { get; set; } = string.Empty;
    /// <summary>Gets or sets the set of MCP tool names that are individually disabled.</summary>
    public HashSet<string> DisabledMcpTools { get; set; } = [];
    /// <summary>Gets or sets a value indicating whether the Internet Radio sidebar item is visible.</summary>
    public bool ShowInternetRadioItem { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the Podcasts sidebar item is visible.</summary>
    public bool ShowPodcastsItem { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the Up Next sidebar item is visible.</summary>
    public bool ShowQueueItem { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the AI Chat sidebar item is visible.</summary>
    public bool ShowAiChatItem { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether Orynivo checks for signed updates at startup.</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the main window starts maximized.</summary>
    public bool StartMaximized { get; set; } = true;
    /// <summary>Gets or sets the last normal main-window width in logical pixels.</summary>
    public double MainWindowWidth { get; set; } = 1100;
    /// <summary>Gets or sets the last normal main-window height in logical pixels.</summary>
    public double MainWindowHeight { get; set; } = 760;
    /// <summary>Gets or sets the last normal main-window horizontal screen coordinate.</summary>
    public int? MainWindowX { get; set; }
    /// <summary>Gets or sets the last normal main-window vertical screen coordinate.</summary>
    public int? MainWindowY { get; set; }
    /// <summary>Gets or sets a value indicating whether the Library sidebar section is visible.</summary>
    public bool ShowLocalLibrarySection { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the Own Radios sidebar section is visible.</summary>
    public bool ShowOwnRadiosSection { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the My Podcasts sidebar section is visible.</summary>
    public bool ShowMyPodcastsSection { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the Plex sidebar section is visible.</summary>
    public bool ShowPlexSection { get; set; } = true;
    /// <summary>Gets or sets a legacy value for the removed Orynivo Server sidebar section.</summary>
    public bool ShowOrynivoServerSection { get; set; } = true;
    /// <summary>Gets or sets a legacy value for the removed Playlists sidebar section visibility toggle. Playlists are now a child group of the Local node and follow the Library section visibility.</summary>
    public bool ShowPlaylistsSection { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the Library section is expanded.</summary>
    public bool IsLocalLibrarySectionExpanded { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the local media group inside the Library section is expanded.</summary>
    public bool IsLocalMediaLibraryGroupExpanded { get; set; } = true;
    /// <summary>Gets or sets remote Orynivo Server identifiers whose Library sidebar groups are collapsed.</summary>
    public HashSet<string> CollapsedOrynivoServerLibraryGroups { get; set; } = [];
    /// <summary>Gets or sets remote Orynivo Server identifiers whose Playlists sidebar child group is collapsed.</summary>
    public HashSet<string> CollapsedOrynivoServerPlaylistGroups { get; set; } = [];
    /// <summary>Gets or sets a value indicating whether the Own Radios section is expanded.</summary>
    public bool IsOwnRadiosSectionExpanded { get; set; }
    /// <summary>Gets or sets a value indicating whether the My Podcasts section is expanded.</summary>
    public bool IsMyPodcastsSectionExpanded { get; set; }
    /// <summary>Gets or sets a value indicating whether the Plex section is expanded.</summary>
    public bool IsPlexSectionExpanded { get; set; }
    /// <summary>Gets or sets a legacy value for the removed Orynivo Server section expansion state.</summary>
    public bool IsOrynivoServerSectionExpanded { get; set; }
    /// <summary>Gets or sets a value indicating whether the Playlists section is expanded.</summary>
    public bool IsPlaylistsSectionExpanded { get; set; }
    /// <summary>Gets or sets the embedded AI chat configuration.</summary>
    public AiChatSettings AiChat { get; set; } = new();
    /// <summary>Gets or sets the web-browsing tool configuration (SearXNG endpoint and fetch safety limits).</summary>
    public WebBrowsingOptions WebBrowsing { get; set; } = new();
    /// <summary>
    /// Gets or sets the maximum size of the local podcast download cache in
    /// megabytes; zero or less keeps every downloaded episode.
    /// </summary>
    public int PodcastDownloadLimitMb { get; set; } = 2048;
    /// <summary>
    /// Gets or sets a value indicating whether optional UI motion (Genre Cloud,
    /// Dashboard cover stage, and karaoke transitions) is disabled.
    /// </summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Gets or sets the automatic library-backup schedule.</summary>
    public ScheduledBackupSettings ScheduledBackup { get; set; } = new();
    /// <summary>Gets or sets the optional cloud upload target for completed backups.</summary>
    public BackupTargetSettings BackupTarget { get; set; } = new();
}

/// <summary>
/// Persisted configuration for uploading completed backup archives to a WebDAV
/// collection. The password is persisted only by the encrypted credential store.
/// </summary>
public sealed class BackupTargetSettings
{
    /// <summary>Gets or sets a value indicating whether completed backups are uploaded.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the WebDAV collection base URL.</summary>
    public string UploadUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional sub-directory inside the collection.</summary>
    public string RemoteDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional user name for Basic authentication.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional password for Basic authentication. It is overlaid
    /// from the encrypted credential store and never written to JSON settings.
    /// </summary>
    [JsonIgnore]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Persisted configuration for automatic library backups.</summary>
public sealed class ScheduledBackupSettings
{
    /// <summary>Gets or sets a value indicating whether Orynivo backs up the library automatically.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the minimum number of days between automatic backups.</summary>
    public int IntervalDays { get; set; } = 7;

    /// <summary>Gets or sets how many automatic backups are kept before older ones are removed.</summary>
    public int RetentionCount { get; set; } = 3;

    /// <summary>Gets or sets the backup folder; an empty value uses the per-user default folder.</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>Gets or sets the Unix timestamp of the last successful automatic backup.</summary>
    public long LastRunAtUnix { get; set; }

    /// <summary>Resolves the configured backup folder, falling back to the per-user default.</summary>
    /// <returns>The absolute backup folder path.</returns>
    public string ResolveDirectory() =>
        string.IsNullOrWhiteSpace(Directory)
            ? Path.Combine(AppPaths.DataRoot, "backups")
            : Directory.Trim();
}

/// <summary>Application colour theme.</summary>
public enum AppTheme
{
    /// <summary>Light colour scheme.</summary>
    Light,
    /// <summary>Dark colour scheme.</summary>
    Dark
}

/// <summary>Artwork sources available for the Genre Cloud background.</summary>
public enum GenreCloudBackgroundMode
{
    /// <summary>Disables the decorative Genre Cloud background.</summary>
    None,
    /// <summary>Uses album covers from the current recommendations.</summary>
    Albums,
    /// <summary>Uses artist images from the current recommendations.</summary>
    Artists
}
