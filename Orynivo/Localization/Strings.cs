namespace Orynivo.Localization;

/// <summary>
/// Holds all localised UI strings for a single language.
/// One instance per language is created in <see cref="LocalizationManager"/>.
/// </summary>
public sealed record LocalizedStrings(
    string LocalLibrary,
    string Artists,
    string Albums,
    string Tracks,
    string FolderStructure,
    string Search,
    string Playlists,
    string About,
    string Settings,
    string Filter,
    string Favorites,
    string AudioTypes,
    string Bitrate,
    string NoDeviceSelected,
    string Appearance,
    string ColorScheme,
    string Language,
    string Playback,
    string OutputDevice,
    string Library,
    string Directories,
    string AddDirectory,
    string DatabaseMaintenance,
    string OptimizeDatabase,
    string RepairAlbumArtwork,
    string DownloadMissingArtwork,
    string DownloadMissingArtworkHint,
    string CoverNotFound,
    string SearchCover,
    string CoverSearchTitle,
    string CoverSearchRunning,
    string CoverSearchNoResults,
    string CoverSearchQuery,
    string SearchAgain,
    string UseSelectedCover,
    string DeleteCover,
    string ReassignCover,
    string Author,
    string Licenses,
    string Save,
    string Cancel,
    string Table,
    string Artwork,
    string Unknown,
    string AlbumArtist,
    string Year,
    string Title,
    string Artist,
    string Album,
    string Genre,
    string Duration,
    string Format,
    string SearchTermNotFoundInTracks,
    string SearchTermNotFoundInAlbums,
    string SearchTermNotFoundInArtists,
    string CountEntries,
    string CountTracks,
    string SelectTrackFirst,
    string PlaybackStopped,
    string PlaybackFinished,
    string SelectAsioDevice,
    string SelectWasapiDevice,
    string NotImplemented,
    string SettingsSaved,
    string DeviceInfoFailed,
    string NoWasapiDevices,
    string NoAsioDrivers,
    string SelectAndSave,
    string ScanRunning,
    string FolderNotFound,
    string ScanCanceled,
    string DatabaseOptimizing,
    string DatabaseOptimized,
    string DatabaseOptimizeFailed,
    string AlbumArtworkRepairing,
    string AlbumArtworkRepaired,
    string AlbumArtworkRepairFailed,
    string MissingArtworkDownloading,
    string MissingArtworkDownloaded,
    string MissingArtworkDownloadFailed,
    string AddToPlaylist,
    string NewPlaylist,
    string NewPlaylistDialogTitle,
    string NewPlaylistNameLabel,
    string CreatePlaylist,
    string TrackAddedToPlaylist,
    string TracksAddedToPlaylist,
    string DeletePlaylist,
    string RemoveFromPlaylist,
    string PlaylistDeleted,
    string TrackRemovedFromPlaylist,
    string SaveSmartPlaylist,
    string SmartPlaylistSaved,
    string SaveSmartPlaylistDisabledTooltip,
    string LibraryBackup,
    string LibraryBackupHint,
    string ExportLibrary,
    string ImportLibrary,
    string LibraryExporting,
    string LibraryExported,
    string LibraryExportFailed,
    string LibraryImportConfirm,
    string LibraryImporting,
    string LibraryImported,
    string LibraryImportFailed,
    string LibraryOperationScanActive,
    string LibraryArchiveFilter,
    string LibraryExportProgress,
    string LibraryImportProgress,
    string Lyrics,
    string ShowLyrics,
    string RefreshLyrics,
    string CloseLyrics,
    string LyricsLoading,
    string LyricsDownloading,
    string LyricsUnavailable,
    string LyricsNotFound,
    string LyricsDownloadFailed,
    string ArtistInfo,
    string ShowArtistInfo,
    string RefreshArtistInfo,
    string CloseArtistInfo,
    string ArtistInfoLoading,
    string ArtistInfoDownloading,
    string ArtistInfoNotFound,
    string ArtistInfoDownloadFailed,
    string ArtistInfoNoImage,
    string ArtistInfoImageMissing,
    string ArtistInfoImageLoadError,
    string ArtistInfoSource,
    string ArtistInfoSourceLastFm,
    string ArtistInfoSourceSetting,
    string LastFmApiKey,
    string LastFmApiKeyHint,
    string ShowAllAlbumTracks,
    string CrashTitle,
    string CrashMessage,
    string CrashMessageWithoutLog)
{
    public string OutputType { get; init; } = "";
    public string AsioOutputDevice { get; init; } = "";
    public string CwAsioOutputDevice { get; init; } = "";
    public string SteinbergAsio { get; init; } = "";
    public string CwAsio { get; init; } = "";
    public string WasapiOutputDevice { get; init; } = "";
    public string DeviceInfo { get; init; } = "";
    public string DatabaseOptimizeHint { get; init; } = "";
    public string NormalizeArtists { get; init; } = "";
    public string NormalizeArtistsHint { get; init; } = "";
    public string ArtistsNormalizing { get; init; } = "";
    public string ArtistsNormalized { get; init; } = "";
    public string ArtistNormalizationFailed { get; init; } = "";
    public string AsioBridgeMissing { get; init; } = "";
    public string KernelStreamingUnavailable { get; init; } = "";
    public string AddMusicDirectory { get; init; } = "";
    public string TrackCountTooltip { get; init; } = "";
    public string Scan { get; init; } = "";
    public string RemoveDirectory { get; init; } = "";
    public string ScanCompleted { get; init; } = "";
    public string ScanFailed { get; init; } = "";
    public string StartupPreparingLibrary { get; init; } = "";
    public string Back { get; init; } = "";
    public string MarkAsFavorite { get; init; } = "";
    public string PlaybackThrough { get; init; } = "";
    public string PlaybackThroughWithDsdConversion { get; init; } = "";
    public string NativeDsdOutput { get; init; } = "";
    public string DsdToPcmOutput { get; init; } = "";
    /// <summary>Gets the ReplayGain settings label.</summary>
    public string ReplayGain { get; init; } = "";
    /// <summary>Gets the explanatory ReplayGain settings text.</summary>
    public string ReplayGainHint { get; init; } = "";
    /// <summary>Gets the disabled ReplayGain mode label.</summary>
    public string ReplayGainOff { get; init; } = "";
    /// <summary>Gets the track ReplayGain mode label.</summary>
    public string ReplayGainTrack { get; init; } = "";
    /// <summary>Gets the album ReplayGain mode label.</summary>
    public string ReplayGainAlbum { get; init; } = "";
    /// <summary>Gets the column-selection menu heading.</summary>
    public string SelectColumns { get; init; } = "";
    /// <summary>Gets the file-name column label.</summary>
    public string FileName { get; init; } = "";
    /// <summary>Gets the file-size column label.</summary>
    public string FileSize { get; init; } = "";
    /// <summary>Gets the added-date column label.</summary>
    public string AddedAt { get; init; } = "";
    /// <summary>Gets the sample-rate column label.</summary>
    public string SampleRate { get; init; } = "";
    /// <summary>Gets the bit-depth column label.</summary>
    public string BitDepth { get; init; } = "";
    /// <summary>Gets the channel-count column label.</summary>
    public string Channels { get; init; } = "";
    /// <summary>Gets the track-number column label.</summary>
    public string TrackNumber { get; init; } = "";
    /// <summary>Gets the disc-number column label.</summary>
    public string DiscNumber { get; init; } = "";
    /// <summary>Gets the composer column label.</summary>
    public string Composer { get; init; } = "";
    /// <summary>Gets the beats-per-minute column label.</summary>
    public string Bpm { get; init; } = "";
    /// <summary>Gets the track ReplayGain column label.</summary>
    public string ReplayGainTrackColumn { get; init; } = "";
    /// <summary>Gets the album ReplayGain column label.</summary>
    public string ReplayGainAlbumColumn { get; init; } = "";
    /// <summary>Gets the codec column label.</summary>
    public string Codec { get; init; } = "";
    /// <summary>Gets the tags column label.</summary>
    public string Tags { get; init; } = "";
    /// <summary>Gets the homepage column label.</summary>
    public string Homepage { get; init; } = "";
    /// <summary>Gets the feed-address column label.</summary>
    public string FeedUrl { get; init; } = "";
    public string SearchResultSummary { get; init; } = "";
    public string RecentAlbums { get; init; } = "";
    public string Calendar { get; init; } = "";
    public string TopGenres { get; init; } = "";
    public string NoData { get; init; } = "";
    public string DevicePcmSampleRates { get; init; } = "";
    public string DeviceDsdRates { get; init; } = "";
    public string DevicePcmFormats { get; init; } = "";
    public string DeviceDsdFormats { get; init; } = "";
    public string DeviceChannelSummary { get; init; } = "";
    public string DeviceBufferSummary { get; init; } = "";
    public string DriverProvidedNoInformation { get; init; } = "";
    public string DsdSupportedWithoutFormats { get; init; } = "";
    public string Unsupported { get; init; } = "";
    public string DeviceProbeInconclusive { get; init; } = "";
    public string WasapiEndpointSummary { get; init; } = "";
    public string WasapiNoExclusiveFormats { get; init; } = "";
    public string WasapiDsdNotRelevant { get; init; } = "";
    public string NativeDsdUsesAsio { get; init; } = "";
    public string Dashboard { get; init; } = "";
    public string DashboardIntroTitle { get; init; } = "";
    public string DashboardIntroHint { get; init; } = "";
    public string ArtistsIntroTitle { get; init; } = "";
    public string ArtistsIntroHint { get; init; } = "";
    public string AlbumsIntroTitle { get; init; } = "";
    public string AlbumsIntroHint { get; init; } = "";
    public string TracksIntroTitle { get; init; } = "";
    public string TracksIntroHint { get; init; } = "";
    public string FoldersIntroTitle { get; init; } = "";
    public string FoldersIntroHint { get; init; } = "";
    public string ThemeLight { get; init; } = "";
    public string ThemeDark { get; init; } = "";
    public string LanguageGerman { get; init; } = "";
    public string LanguageEnglish { get; init; } = "";
    public string LanguageFrench { get; init; } = "";
    public string LanguageSpanish { get; init; } = "";
    public string PcmIntegerFormat { get; init; } = "";
    public string PcmContainerFormat { get; init; } = "";
    public string PcmFloatFormat { get; init; } = "";
    public string NativeDsdLsbFormat { get; init; } = "";
    public string NativeDsdMsbFormat { get; init; } = "";
    public string NativeDsdWordFormat { get; init; } = "";
    public string CountEntrySingular { get; init; } = "";
    public string CountTrackSingular { get; init; } = "";
    public string Streaming { get; init; } = "";
    public string StreamingServices { get; init; } = "";
    public string Qobuz { get; init; } = "";
    public string QobuzApplicationId { get; init; } = "";
    public string QobuzIntegrationHint { get; init; } = "";
    public string QobuzCredentialsHint { get; init; } = "";
    public string SearchArtistImage { get; init; } = "";
    public string ArtistImageSearchTitle { get; init; } = "";
    public string ArtistImageSearchRunning { get; init; } = "";
    public string ArtistImageSearchNoResults { get; init; } = "";
    public string ArtistImageSearchQuery { get; init; } = "";
    public string ArtistImageSearchFailed { get; init; } = "";
    public string UseSelectedArtistImage { get; init; } = "";
    public string ArtistImageDownloadFailed { get; init; } = "";
    public string EditArtistName { get; init; } = "";
    public string ArtistName { get; init; } = "";
    public string RenameArtist { get; init; } = "";
    public string MergeArtistsTitle { get; init; } = "";
    public string ArtistNameExistsMessage { get; init; } = "";
    public string KeepArtistProfile { get; init; } = "";
    public string ArtistRenameFailed { get; init; } = "";
    public string Shuffle { get; init; } = "";
    public string SearchLyrics { get; init; } = "";
    public string LyricsSearchTitle { get; init; } = "";
    public string LyricsSearchRunning { get; init; } = "";
    public string LyricsSearchNoResults { get; init; } = "";
    public string LyricsSearchFailed { get; init; } = "";
    public string UseSelectedLyrics { get; init; } = "";
    public string SelectLyricsResult { get; init; } = "";
    public string SynchronizedLyrics { get; init; } = "";
    public string InternetRadio { get; init; } = "";
    public string OwnRadios { get; init; } = "";
    public string RadioDirectory { get; init; } = "";
    public string RadioDirectoryHint { get; init; } = "";
    public string RadioSearch { get; init; } = "";
    public string RadioStation { get; init; } = "";
    public string Country { get; init; } = "";
    public string PlayRadio { get; init; } = "";
    public string AddToOwnRadios { get; init; } = "";
    public string DeleteRadio { get; init; } = "";
    public string RadioLoading { get; init; } = "";
    public string RadioNoResults { get; init; } = "";
    public string RadioAdded { get; init; } = "";
    public string RadioDeleted { get; init; } = "";
    public string RadioSearchFailed { get; init; } = "";
    public string RadioNowPlaying { get; init; } = "";
    public string RadioMetadataUnavailable { get; init; } = "";
    public string RadioGenres { get; init; } = "";
    public string ClearFilter { get; init; } = "";
    public string Podcasts { get; init; } = "";
    public string MyPodcasts { get; init; } = "";
    public string PodcastDirectory { get; init; } = "";
    public string PodcastDirectoryHint { get; init; } = "";
    public string PodcastSearch { get; init; } = "";
    public string Podcast { get; init; } = "";
    public string PodcastAuthor { get; init; } = "";
    public string PlayLatestEpisode { get; init; } = "";
    public string AddToMyPodcasts { get; init; } = "";
    public string DeletePodcast { get; init; } = "";
    public string PodcastLoading { get; init; } = "";
    public string PodcastNoResults { get; init; } = "";
    public string PodcastAdded { get; init; } = "";
    public string PodcastDeleted { get; init; } = "";
    public string PodcastSearchFailed { get; init; } = "";
    public string PodcastFeedFailed { get; init; } = "";
    public string ShowEpisodes { get; init; } = "";
    public string Published { get; init; } = "";
    public string Progress { get; init; } = "";
    public string PodcastStatus { get; init; } = "";
    public string PodcastUnplayed { get; init; } = "";
    public string PodcastInProgress { get; init; } = "";
    public string PodcastPlayed { get; init; } = "";
    public string PodcastEpisodesLoading { get; init; } = "";
    public string PodcastNoEpisodes { get; init; } = "";
    public string PodcastCategories { get; init; } = "";
    public string PodcastLanguages { get; init; } = "";
    public string PodcastLanguage { get; init; } = "";
    public string PodcastLanguagesLoading { get; init; } = "";
    public string PodcastOverview { get; init; } = "";
    public string PodcastEpisodeTotal { get; init; } = "";
    public string PodcastEpisodeUnheard { get; init; } = "";
    public string PodcastEpisodeStarted { get; init; } = "";
    public string PodcastLatestEpisode { get; init; } = "";
    public string DailyHistoryTitle { get; init; } = "";
    public string PlayedAt { get; init; } = "";
    public string ListenedDuration { get; init; } = "";
    public string MediaType { get; init; } = "";
    public string Close { get; init; } = "";
    public string DailyHistoryNoEntries { get; init; } = "";
    public string SidebarSections { get; init; } = "";
    public string SidebarSectionsHint { get; init; } = "";
    public string PodcastInfo { get; init; } = "";
    public string ShowPodcastInfo { get; init; } = "";
    public string ClosePodcastInfo { get; init; } = "";
    public string PodcastPublishedOn { get; init; } = "";
    public string PodcastEpisodeDuration { get; init; } = "";
    public string PodcastDescriptionUnavailable { get; init; } = "";
    public string PlexServers { get; init; } = "";
    public string PlexServersHint { get; init; } = "";
    public string AddPlexServer { get; init; } = "";
    public string PlexServerDialogTitle { get; init; } = "";
    public string PlexServerName { get; init; } = "";
    public string PlexServerUrl { get; init; } = "";
    public string PlexToken { get; init; } = "";
    public string PlexTestConnection { get; init; } = "";
    public string PlexTestingConnection { get; init; } = "";
    public string PlexConnectionSuccessful { get; init; } = "";
    public string PlexConnectionFailed { get; init; } = "";
    public string PlexServerFieldsRequired { get; init; } = "";
    public string PlexServerUrlInvalid { get; init; } = "";
    public string PlexEditServer { get; init; } = "";
    public string PlexRemoveServer { get; init; } = "";
    public string PlexNoAudioLibraries { get; init; } = "";
    public string PlexLoading { get; init; } = "";
    public string LoadMore { get; init; } = "";
    public string FfmpegDownloading { get; init; } = "";
    public string FfmpegDownloadFailed { get; init; } = "";
    /// <summary>Gets the smart-playlist editor window title.</summary>
    public string SmartPlaylistDialogTitle { get; init; } = "";
    /// <summary>Gets the smart-playlist name label.</summary>
    public string SmartPlaylistName { get; init; } = "";
    /// <summary>Gets the basic smart-playlist filters section title.</summary>
    public string SmartPlaylistBasicFilters { get; init; } = "";
    /// <summary>Gets the comma-separated genre filter label.</summary>
    public string SmartPlaylistGenres { get; init; } = "";
    /// <summary>Gets the comma-separated format filter label.</summary>
    public string SmartPlaylistFormats { get; init; } = "";
    /// <summary>Gets the comma-separated bitrate filter label.</summary>
    public string SmartPlaylistBitrates { get; init; } = "";
    /// <summary>Gets the metadata filter section title.</summary>
    public string SmartPlaylistMetadata { get; init; } = "";
    /// <summary>Gets the minimum release-year label.</summary>
    public string SmartPlaylistMinimumYear { get; init; } = "";
    /// <summary>Gets the maximum release-year label.</summary>
    public string SmartPlaylistMaximumYear { get; init; } = "";
    /// <summary>Gets the artist text-filter label.</summary>
    public string SmartPlaylistArtistContains { get; init; } = "";
    /// <summary>Gets the album text-filter label.</summary>
    public string SmartPlaylistAlbumContains { get; init; } = "";
    /// <summary>Gets the minimum duration label.</summary>
    public string SmartPlaylistMinimumDuration { get; init; } = "";
    /// <summary>Gets the maximum duration label.</summary>
    public string SmartPlaylistMaximumDuration { get; init; } = "";
    /// <summary>Gets the playback-history filter section title.</summary>
    public string SmartPlaylistHistory { get; init; } = "";
    /// <summary>Gets the recently-added day-count label.</summary>
    public string SmartPlaylistAddedWithinDays { get; init; } = "";
    /// <summary>Gets the recently-played day-count label.</summary>
    public string SmartPlaylistPlayedWithinDays { get; init; } = "";
    /// <summary>Gets the never-played filter label.</summary>
    public string SmartPlaylistNeverPlayed { get; init; } = "";
    /// <summary>Gets the minimum playback-count label.</summary>
    public string SmartPlaylistMinimumPlayCount { get; init; } = "";
    /// <summary>Gets the maximum playback-count label.</summary>
    public string SmartPlaylistMaximumPlayCount { get; init; } = "";
    /// <summary>Gets the smart-playlist result section title.</summary>
    public string SmartPlaylistResult { get; init; } = "";
    /// <summary>Gets the smart-playlist ordering label.</summary>
    public string SmartPlaylistSortOrder { get; init; } = "";
    /// <summary>Gets the alphabetical ordering label.</summary>
    public string SmartPlaylistSortTitle { get; init; } = "";
    /// <summary>Gets the random ordering label.</summary>
    public string SmartPlaylistSortRandom { get; init; } = "";
    /// <summary>Gets the most-recently-played ordering label.</summary>
    public string SmartPlaylistSortLastPlayed { get; init; } = "";
    /// <summary>Gets the least-recently-played ordering label.</summary>
    public string SmartPlaylistSortLeastRecentlyPlayed { get; init; } = "";
    /// <summary>Gets the smart-playlist result-limit label.</summary>
    public string SmartPlaylistResultLimit { get; init; } = "";
    /// <summary>Gets the smart-playlist creation button label.</summary>
    public string CreateSmartPlaylist { get; init; } = "";
    /// <summary>Gets the validation error shown for contradictory or malformed criteria.</summary>
    public string InvalidSmartPlaylistCriteria { get; init; } = "";
    /// <summary>Gets the sidebar action label for editing a smart playlist.</summary>
    public string EditSmartPlaylist { get; init; } = "";
    /// <summary>Gets the status message shown after a smart playlist was updated.</summary>
    public string SmartPlaylistUpdated { get; init; } = "";
    /// <summary>Gets the command label for importing an M3U8 playlist.</summary>
    public string ImportM3u8Playlist { get; init; } = "";
    /// <summary>Gets the command label for exporting an M3U8 playlist.</summary>
    public string ExportM3u8Playlist { get; init; } = "";
    /// <summary>Gets the successful M3U8 import status message.</summary>
    public string M3u8ImportCompleted { get; init; } = "";
    /// <summary>Gets the empty M3U8 import status message.</summary>
    public string M3u8ImportNoEntries { get; init; } = "";
    /// <summary>Gets the failed M3U8 import status message.</summary>
    public string M3u8ImportFailed { get; init; } = "";
    /// <summary>Gets the successful M3U8 export status message.</summary>
    public string M3u8ExportCompleted { get; init; } = "";
    /// <summary>Gets the failed M3U8 export status message.</summary>
    public string M3u8ExportFailed { get; init; } = "";
}
