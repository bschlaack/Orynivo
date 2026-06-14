namespace Orynivo.Localization;

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
}
