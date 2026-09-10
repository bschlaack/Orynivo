using System.Globalization;
using AvaloniaApp = Avalonia.Application;

namespace Orynivo.Localization;

/// <summary>
/// Manages the active UI language. Call <see cref="Apply"/> to switch languages at runtime;
/// read <see cref="Current"/> to access the active string set.
/// </summary>
public static class LocalizationManager
{
    /// <summary>
    /// Returns a pluralised entry-count string using the current language's singular and plural forms.
    /// </summary>
    /// <param name="count">Number of entries to format.</param>
    public static string FormatEntryCount(int count) =>
        count == 1 ? string.Format(Current.CountEntrySingular, count) : string.Format(Current.CountEntries, count);

    /// <summary>
    /// Returns a pluralised track-count string using the current language's singular and plural forms.
    /// </summary>
    /// <param name="count">Number of tracks to format.</param>
    public static string FormatTrackCount(int count) =>
        count == 1 ? string.Format(Current.CountTrackSingular, count) : string.Format(Current.CountTracks, count);

    /// <summary>
    /// Applies <paramref name="language"/> by updating <see cref="System.Globalization.CultureInfo.CurrentCulture"/>,
    /// <see cref="Current"/>, and all <c>L_*</c> resource keys in the current Avalonia application's resource dictionary.
    /// </summary>
    /// <param name="language">The language to activate.</param>
    public static void Apply(Language language)
    {
        var culture = language switch
        {
            Language.English => CultureInfo.GetCultureInfo("en-US"),
            Language.French => CultureInfo.GetCultureInfo("fr-FR"),
            Language.Spanish => CultureInfo.GetCultureInfo("es-ES"),
            Language.Russian => CultureInfo.GetCultureInfo("ru-RU"),
            Language.ChineseSimplified => CultureInfo.GetCultureInfo("zh-CN"),
            _ => CultureInfo.GetCultureInfo("de-DE")
        };
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        Current = language switch
        {
            Language.English => English,
            Language.French => French,
            Language.Spanish => Spanish,
            Language.Russian => Russian,
            Language.ChineseSimplified => ChineseSimplified,
            _ => German
        };

        var resources = AvaloniaApp.Current!.Resources;
        resources["L_LocalLibrary"] = Current.LocalLibrary;
        resources["L_LocalMedia"] = Current.LocalMedia;
        resources["L_LibraryEmptyHint"] = Current.LibraryEmptyHint;
        resources["L_Artists"] = Current.Artists;
        resources["L_Albums"] = Current.Albums;
        resources["L_Tracks"] = Current.Tracks;
        resources["L_UpNext"] = Current.UpNext;
        resources["L_GenreExplorer"] = Current.GenreExplorer;
        resources["L_GenreCloudHint"] = Current.GenreCloudHint;
        resources["L_AllGenres"] = Current.AllGenres;
        resources["L_GenreRecommendations"] = Current.GenreRecommendations;
        resources["L_GenreCloudEmpty"] = Current.GenreCloudEmpty;
        resources["L_MoreGenres"] = Current.MoreGenres;
        resources["L_FolderStructure"] = Current.FolderStructure;
        resources["L_MetadataProblems"] = Current.MetadataProblems;
        resources["L_MetadataProblemsHint"] = Current.MetadataProblemsHint;
        resources["L_MetadataWorkflow"] = Current.MetadataWorkflow;
        resources["L_MetadataInspectFiles"] = Current.MetadataInspectFiles;
        resources["L_MetadataQuickHint"] = Current.MetadataQuickHint;
        resources["L_MetadataSelectHint"] = Current.MetadataSelectHint;
        resources["L_MetadataActionGuide"] = Current.MetadataActionGuide;
        resources["L_MetadataRemoteReadOnly"] = Current.MetadataRemoteReadOnly;
        resources["L_MetadataReviewGuide"] = Current.MetadataReviewGuide;
        resources["L_MetadataPhaseDatabase"] = Current.MetadataPhaseDatabase;
        resources["L_MetadataPhaseFolders"] = Current.MetadataPhaseFolders;
        resources["L_MetadataPhaseHashes"] = Current.MetadataPhaseHashes;
        resources["L_MetadataPhaseServers"] = Current.MetadataPhaseServers;
        resources["L_MetadataPhaseReleases"] = Current.MetadataPhaseReleases;
        resources["L_MetadataPhaseSaving"] = Current.MetadataPhaseSaving;
        resources["L_MetadataRemaining"] = Current.MetadataRemaining;
        resources["L_MetadataRemainingUnknown"] = Current.MetadataRemainingUnknown;
        resources["L_MetadataElapsed"] = Current.MetadataElapsed;
        resources["L_FileName"] = Current.FileName;
        resources["L_DiscNumber"] = Current.DiscNumber;
        resources["L_MetadataFolder"] = Current.MetadataFolder;
        resources["L_MetadataIssues"] = Current.MetadataIssues;
        resources["L_MetadataTrackCount"] = Current.MetadataTrackCount;
        resources["L_MetadataRefreshAnalysis"] = Current.MetadataRefreshAnalysis;
        resources["L_MetadataIssueMissingReplayGain"] = Current.MetadataIssueMissingReplayGain;
        resources["L_MetadataIssueMissingMusicBrainzIds"] = Current.MetadataIssueMissingMusicBrainzIds;
        resources["L_MetadataCurrentValues"] = Current.MetadataCurrentValues;
        resources["L_MetadataProposedValues"] = Current.MetadataProposedValues;
        resources["L_MetadataSeverity"] = Current.MetadataSeverity;
        resources["L_IdentifyFolderAsAlbum"] = Current.IdentifyFolderAsAlbum;
        resources["L_Search"] = Current.Search;
        resources["L_Playlists"] = Current.Playlists;
        resources["L_About"] = Current.About;
        resources["L_VersionLabel"] = Current.VersionLabel;
        resources["L_CheckForUpdates"] = Current.CheckForUpdates;
        resources["L_Updates"] = Current.Updates;
        resources["L_CheckForUpdatesOnStartup"] = Current.CheckForUpdatesOnStartup;
        resources["L_WindowBehavior"] = Current.WindowBehavior;
        resources["L_StartMaximized"] = Current.StartMaximized;
        resources["L_DownloadAndInstall"] = Current.DownloadAndInstall;
        resources["L_UpdateServer"] = Current.UpdateServer;
        resources["L_Settings"] = Current.Settings;
        resources["L_Filter"] = Current.Filter;
        resources["L_Favorites"] = Current.Favorites;
        resources["L_AudioTypes"] = Current.AudioTypes;
        resources["L_Bitrate"] = Current.Bitrate;
        resources["L_Title"] = Current.Title;
        resources["L_Artist"] = Current.Artist;
        resources["L_Album"] = Current.Album;
        resources["L_Duration"] = Current.Duration;
        resources["L_NoDeviceSelected"] = Current.NoDeviceSelected;
        resources["L_Table"] = Current.Table;
        resources["L_Artwork"] = Current.Artwork;
        resources["L_Appearance"] = Current.Appearance;
        resources["L_ColorScheme"] = Current.ColorScheme;
        resources["L_Language"] = Current.Language;
        resources["L_Playback"] = Current.Playback;
        resources["L_OutputDevice"] = Current.OutputDevice;
        resources["L_ReleaseOutputDevice"] = Current.ReleaseOutputDevice;
        resources["L_AppearanceNavItem"] = Current.AppearanceNavItem;
        resources["L_ArtistInfoNavItem"] = Current.ArtistInfoNavItem;
        resources["L_Library"] = Current.Library;
        resources["L_Directories"] = Current.Directories;
        resources["L_AddDirectory"] = Current.AddDirectory;
        resources["L_DatabaseMaintenance"] = Current.DatabaseMaintenance;
        resources["L_GenreCloudCache"] = Current.GenreCloudCache;
        resources["L_GenreCloudCacheHint"] = Current.GenreCloudCacheHint;
        resources["L_GenreCloudBackground"] = Current.GenreCloudBackground;
        resources["L_GenreCloudBackgroundHint"] = Current.GenreCloudBackgroundHint;
        resources["L_GenreCloudVisibility"] = Current.GenreCloudVisibility;
        resources["L_ClearGenreCloudCache"] = Current.ClearGenreCloudCache;
        resources["L_OptimizeDatabase"] = Current.OptimizeDatabase;
        resources["L_RepairAlbumArtwork"] = Current.RepairAlbumArtwork;
        resources["L_DownloadMissingArtwork"] = Current.DownloadMissingArtwork;
        resources["L_DownloadMissingArtworkHint"] = Current.DownloadMissingArtworkHint;
        resources["L_CoverNotFound"] = Current.CoverNotFound;
        resources["L_SearchCover"] = Current.SearchCover;
        resources["L_InfiniteMixStart"] = Current.InfiniteMixStart;
        resources["L_GenreCloudInfiniteMix"] = Current.GenreCloudInfiniteMix;
        resources["L_InfiniteMixCalculating"] = Current.InfiniteMixCalculating;
        resources["L_InfiniteMixSettingsTitle"] = Current.InfiniteMixSettingsTitle;
        resources["L_InfiniteMixSettingsHint"] = Current.InfiniteMixSettingsHint;
        resources["L_InfiniteMixMood"] = Current.InfiniteMixMood;
        resources["L_InfiniteMixDiscovery"] = Current.InfiniteMixDiscovery;
        resources["L_InfiniteMixFamiliar"] = Current.InfiniteMixFamiliar;
        resources["L_InfiniteMixAdventurous"] = Current.InfiniteMixAdventurous;
        resources["L_InfiniteMixPeriod"] = Current.InfiniteMixPeriod;
        resources["L_InfiniteMixSources"] = Current.InfiniteMixSources;
        resources["L_InfiniteMixWeightFavorites"] = Current.InfiniteMixWeightFavorites;
        resources["L_InfiniteMixPreferRare"] = Current.InfiniteMixPreferRare;
        resources["L_InfiniteMixIncludeGenres"] = Current.InfiniteMixIncludeGenres;
        resources["L_InfiniteMixExcludeGenres"] = Current.InfiniteMixExcludeGenres;
        resources["L_InfiniteMixGenresWatermark"] = Current.InfiniteMixGenresWatermark;
        resources["L_InfiniteMixAddGenre"] = Current.InfiniteMixAddGenre;
        resources["L_InfiniteMixRemoveGenre"] = Current.InfiniteMixRemoveGenre;
        resources["L_InfiniteMixAdjust"] = Current.InfiniteMixAdjust;
        resources["L_InfiniteMixReplaceNext"] = Current.InfiniteMixReplaceNext;
        resources["L_CoverSearchTitle"] = Current.CoverSearchTitle;
        resources["L_CoverSearchRunning"] = Current.CoverSearchRunning;
        resources["L_CoverSearchNoResults"] = Current.CoverSearchNoResults;
        resources["L_CoverSearchQuery"] = Current.CoverSearchQuery;
        resources["L_CoverSearchArtistQuery"] = Current.CoverSearchArtistQuery;
        resources["L_SearchAgain"] = Current.SearchAgain;
        resources["L_UseSelectedCover"] = Current.UseSelectedCover;
        resources["L_DeleteCover"] = Current.DeleteCover;
        resources["L_ReassignCover"] = Current.ReassignCover;
        resources["L_Author"] = Current.Author;
        resources["L_Licenses"] = Current.Licenses;
        resources["L_Save"] = Current.Save;
        resources["L_Cancel"] = Current.Cancel;
        resources["L_AddToPlaylist"] = Current.AddToPlaylist;
        resources["L_SaveAlbumAsPlaylist"] = Current.SaveAlbumAsPlaylist;
        resources["L_AlbumPath"] = Current.AlbumPath;
        resources["L_SaveQueueAsPlaylist"] = Current.SaveQueueAsPlaylist;
        resources["L_NewPlaylist"] = Current.NewPlaylist;
        resources["L_NewPlaylistDialogTitle"] = Current.NewPlaylistDialogTitle;
        resources["L_NewPlaylistNameLabel"] = Current.NewPlaylistNameLabel;
        resources["L_CreatePlaylist"] = Current.CreatePlaylist;
        resources["L_SaveSmartPlaylistDisabledTooltip"] = Current.SaveSmartPlaylistDisabledTooltip;
        resources["L_SaveSmartPlaylist"] = Current.SaveSmartPlaylist;
        resources["L_SmartPlaylistDialogTitle"] = Current.SmartPlaylistDialogTitle;
        resources["L_SmartPlaylistName"] = Current.SmartPlaylistName;
        resources["L_SmartPlaylistBasicFilters"] = Current.SmartPlaylistBasicFilters;
        resources["L_SmartPlaylistGenres"] = Current.SmartPlaylistGenres;
        resources["L_SmartPlaylistFormats"] = Current.SmartPlaylistFormats;
        resources["L_SmartPlaylistBitrates"] = Current.SmartPlaylistBitrates;
        resources["L_SmartPlaylistSources"] = Current.SmartPlaylistSources;
        resources["L_SmartPlaylistMetadata"] = Current.SmartPlaylistMetadata;
        resources["L_SmartPlaylistMinimumYear"] = Current.SmartPlaylistMinimumYear;
        resources["L_SmartPlaylistMaximumYear"] = Current.SmartPlaylistMaximumYear;
        resources["L_SmartPlaylistSearchText"] = Current.SmartPlaylistSearchText;
        resources["L_SmartPlaylistArtistContains"] = Current.SmartPlaylistArtistContains;
        resources["L_SmartPlaylistAlbumContains"] = Current.SmartPlaylistAlbumContains;
        resources["L_SmartPlaylistMinimumDuration"] = Current.SmartPlaylistMinimumDuration;
        resources["L_SmartPlaylistMaximumDuration"] = Current.SmartPlaylistMaximumDuration;
        resources["L_SmartPlaylistHistory"] = Current.SmartPlaylistHistory;
        resources["L_SmartPlaylistAddedWithinDays"] = Current.SmartPlaylistAddedWithinDays;
        resources["L_SmartPlaylistPlayedWithinDays"] = Current.SmartPlaylistPlayedWithinDays;
        resources["L_SmartPlaylistNeverPlayed"] = Current.SmartPlaylistNeverPlayed;
        resources["L_SmartPlaylistMinimumPlayCount"] = Current.SmartPlaylistMinimumPlayCount;
        resources["L_SmartPlaylistMaximumPlayCount"] = Current.SmartPlaylistMaximumPlayCount;
        resources["L_SmartPlaylistResult"] = Current.SmartPlaylistResult;
        resources["L_SmartPlaylistSortOrder"] = Current.SmartPlaylistSortOrder;
        resources["L_SmartPlaylistResultLimit"] = Current.SmartPlaylistResultLimit;
        resources["L_CreateSmartPlaylist"] = Current.CreateSmartPlaylist;
        resources["L_LibraryBackup"] = Current.LibraryBackup;
        resources["L_LibraryBackupHint"] = Current.LibraryBackupHint;
        resources["L_ExportLibrary"] = Current.ExportLibrary;
        resources["L_ImportLibrary"] = Current.ImportLibrary;
        resources["L_Lyrics"] = Current.Lyrics;
        resources["L_ShowLyrics"] = Current.ShowLyrics;
        resources["L_RefreshLyrics"] = Current.RefreshLyrics;
        resources["L_CloseLyrics"] = Current.CloseLyrics;
        resources["L_ArtistInfo"] = Current.ArtistInfo;
        resources["L_ShowArtistInfo"] = Current.ShowArtistInfo;
        resources["L_RefreshArtistInfo"] = Current.RefreshArtistInfo;
        resources["L_CloseArtistInfo"] = Current.CloseArtistInfo;
        resources["L_ArtistInfoSource"] = Current.ArtistInfoSource;
        resources["L_ArtistInfoSourceLastFm"] = Current.ArtistInfoSourceLastFm;
        resources["L_ArtistInfoSourceSetting"] = Current.ArtistInfoSourceSetting;
        resources["L_LastFmApiKey"] = Current.LastFmApiKey;
        resources["L_LastFmApiKeyHint"] = Current.LastFmApiKeyHint;
        resources["L_FanartTvApiKey"] = Current.FanartTvApiKey;
        resources["L_FanartTvApiKeyHint"] = Current.FanartTvApiKeyHint;
        resources["L_DownloadMissingArtistImages"] = Current.DownloadMissingArtistImages;
        resources["L_DownloadMissingArtistImagesHint"] = Current.DownloadMissingArtistImagesHint;
        resources["L_ArtistImageSuggestionTitle"] = Current.ArtistImageSuggestionTitle;
        resources["L_ArtistImageSuggestionHint"] = Current.ArtistImageSuggestionHint;
        resources["L_AutoAcceptFanartTvImages"] = Current.AutoAcceptFanartTvImages;
        resources["L_AcceptArtistImage"] = Current.AcceptArtistImage;
        resources["L_RejectArtistImage"] = Current.RejectArtistImage;
        resources["L_ShowAllAlbumTracks"] = Current.ShowAllAlbumTracks;
        resources["L_TrackInfo"] = Current.TrackInfo;
        resources["L_ShowTrackInfo"] = Current.ShowTrackInfo;
        resources["L_PhysicalPath"] = Current.PhysicalPath;
        resources["L_OutputType"] = Current.OutputType;
        resources["L_AsioOutputDevice"] = Current.AsioOutputDevice;
        resources["L_CwAsioOutputDevice"] = Current.CwAsioOutputDevice;
        resources["L_DeviceInfo"] = Current.DeviceInfo;
        resources["L_OutputProfile"] = Current.OutputProfile;
        resources["L_OutputProfileCreate"] = Current.OutputProfileCreate;
        resources["L_OutputProfileConfigure"] = Current.OutputProfileConfigure;
        resources["L_OutputProfileDelete"] = Current.OutputProfileDelete;
        resources["L_OutputProfileCreateTitle"] = Current.OutputProfileCreateTitle;
        resources["L_OutputProfileConfigureTitle"] = Current.OutputProfileConfigureTitle;
        resources["L_OutputProfileName"] = Current.OutputProfileName;
        resources["L_OutputProfileNameExists"] = Current.OutputProfileNameExists;
        resources["L_OutputProfileDeleteTitle"] = Current.OutputProfileDeleteTitle;
        resources["L_OutputProfileDeleteConfirm"] = Current.OutputProfileDeleteConfirm;
        resources["L_UserProfiles"] = Current.UserProfiles;
        resources["L_UserProfileActive"] = Current.UserProfileActive;
        resources["L_UserProfileCreate"] = Current.UserProfileCreate;
        resources["L_UserProfileRename"] = Current.UserProfileRename;
        resources["L_UserProfileDelete"] = Current.UserProfileDelete;
        resources["L_UserProfileName"] = Current.UserProfileName;
        resources["L_UserProfileMigrateFavorites"] = Current.UserProfileMigrateFavorites;
        resources["L_UserProfileDeleteConfirm"] = Current.UserProfileDeleteConfirm;
        resources["L_ReplayGain"] = Current.ReplayGain;
        resources["L_ReplayGainHint"] = Current.ReplayGainHint;
        resources["L_CalculateReplayGainDuringScan"] = Current.CalculateReplayGainDuringScan;
        resources["L_RefreshAllMetadata"] = Current.RefreshAllMetadata;
        resources["L_RefreshAllMetadataHint"] = Current.RefreshAllMetadataHint;
        resources["L_CalculateReplayGain"] = Current.CalculateReplayGain;
        resources["L_NonGaplessCrossfade"] = Current.NonGaplessCrossfade;
        resources["L_NonGaplessCrossfadeHint"] = Current.NonGaplessCrossfadeHint;
        resources["L_ReplayGainBadge"] = Current.ReplayGainBadge;
        resources["L_DsdPlayback"] = Current.DsdPlayback;
        resources["L_AlwaysConvertDsdToPcm"] = Current.AlwaysConvertDsdToPcm;
        resources["L_AlwaysConvertDsdToPcmHint"] = Current.AlwaysConvertDsdToPcmHint;
        resources["L_DsdOverPcm"] = Current.DsdOverPcm;
        resources["L_DsdOverPcmHint"] = Current.DsdOverPcmHint;
        resources["L_PcmOutputBoost"] = Current.PcmOutputBoost;
        resources["L_PcmOutputBoostHint"] = Current.PcmOutputBoostHint;
        resources["L_Equalizer"] = Current.Equalizer;
        resources["L_EqualizerHint"] = Current.EqualizerHint;
        resources["L_EqualizerEnabled"] = Current.EqualizerEnabled;
        resources["L_EqualizerImport"] = Current.EqualizerImport;
        resources["L_EqualizerPreamp"] = Current.EqualizerPreamp;
        resources["L_EqualizerFilterType"] = Current.EqualizerFilterType;
        resources["L_EqualizerFrequency"] = Current.EqualizerFrequency;
        resources["L_EqualizerGain"] = Current.EqualizerGain;
        resources["L_EqualizerQ"] = Current.EqualizerQ;
        resources["L_EqualizerAddFilter"] = Current.EqualizerAddFilter;
        resources["L_EqualizerCreate"] = Current.EqualizerCreate;
        resources["L_EqualizerCreateTitle"] = Current.EqualizerCreateTitle;
        resources["L_EqualizerName"] = Current.EqualizerName;
        resources["L_EqualizerDelete"] = Current.EqualizerDelete;
        resources["L_ReacquireOutputDevice"] = Current.ReacquireOutputDevice;
        resources["L_OutputDeviceReleased"] = Current.OutputDeviceReleased;
        resources["L_SelectColumns"] = Current.SelectColumns;
        resources["L_Codec"] = Current.Codec;
        resources["L_Tags"] = Current.Tags;
        resources["L_Homepage"] = Current.Homepage;
        resources["L_FeedUrl"] = Current.FeedUrl;
        resources["L_DatabaseOptimizeHint"] = Current.DatabaseOptimizeHint;
        resources["L_NormalizeArtists"] = Current.NormalizeArtists;
        resources["L_NormalizeArtistsHint"] = Current.NormalizeArtistsHint;
        resources["L_Back"] = Current.Back;
        resources["L_MarkAsFavorite"] = Current.MarkAsFavorite;
        resources["L_OpenAlbum"] = Current.OpenAlbum;
        resources["L_OpenArtist"] = Current.OpenArtist;
        resources["L_ToggleFavorite"] = Current.ToggleFavorite;
        resources["L_DevicePcmSampleRates"] = Current.DevicePcmSampleRates;
        resources["L_DeviceDsdRates"] = Current.DeviceDsdRates;
        resources["L_DevicePcmFormats"] = Current.DevicePcmFormats;
        resources["L_DeviceDsdFormats"] = Current.DeviceDsdFormats;
        resources["L_Dashboard"] = Current.Dashboard;
        resources["L_StartupPreparingLibrary"] = Current.StartupPreparingLibrary;
        resources["L_StartupCheckingSearchIndex"] = Current.StartupCheckingSearchIndex;
        resources["L_SearchIndexRebuilding"] = Current.SearchIndexRebuilding;
        resources["L_SearchIndexReady"] = Current.SearchIndexReady;
        resources["L_SearchIndexFailed"] = Current.SearchIndexFailed;
        resources["L_Streaming"] = Current.Streaming;
        resources["L_StreamingServices"] = Current.StreamingServices;
        resources["L_Qobuz"] = Current.Qobuz;
        resources["L_QobuzApplicationId"] = Current.QobuzApplicationId;
        resources["L_QobuzIntegrationHint"] = Current.QobuzIntegrationHint;
        resources["L_QobuzCredentialsHint"] = Current.QobuzCredentialsHint;
        resources["L_SearchArtistImage"] = Current.SearchArtistImage;
        resources["L_UploadArtistImage"] = Current.UploadArtistImage;
        resources["L_DeleteArtistImage"] = Current.DeleteArtistImage;
        resources["L_UploadCover"] = Current.UploadCover;
        resources["L_ImageFileType"] = Current.ImageFileType;
        resources["L_ArtistImageSearchTitle"] = Current.ArtistImageSearchTitle;
        resources["L_ArtistImageSearchRunning"] = Current.ArtistImageSearchRunning;
        resources["L_ArtistImageSearchNoResults"] = Current.ArtistImageSearchNoResults;
        resources["L_ArtistImageSearchQuery"] = Current.ArtistImageSearchQuery;
        resources["L_ArtistImageSearchFailed"] = Current.ArtistImageSearchFailed;
        resources["L_UseSelectedArtistImage"] = Current.UseSelectedArtistImage;
        resources["L_ArtistImageDownloadFailed"] = Current.ArtistImageDownloadFailed;
        resources["L_ArtistProfileSearchTitle"] = Current.ArtistProfileSearchTitle;
        resources["L_ArtistProfileSearchHint"] = Current.ArtistProfileSearchHint;
        resources["L_ArtistProfileSearchQuery"] = Current.ArtistProfileSearchQuery;
        resources["L_ArtistProfileSearchLoad"] = Current.ArtistProfileSearchLoad;
        resources["L_EditArtistName"] = Current.EditArtistName;
        resources["L_ArtistName"] = Current.ArtistName;
        resources["L_RenameArtist"] = Current.RenameArtist;
        resources["L_MergeArtistsTitle"] = Current.MergeArtistsTitle;
        resources["L_Shuffle"] = Current.Shuffle;
        resources["L_SearchLyrics"] = Current.SearchLyrics;
        resources["L_LyricsSearchTitle"] = Current.LyricsSearchTitle;
        resources["L_LyricsSearchRunning"] = Current.LyricsSearchRunning;
        resources["L_LyricsSearchNoResults"] = Current.LyricsSearchNoResults;
        resources["L_LyricsSearchFailed"] = Current.LyricsSearchFailed;
        resources["L_UseSelectedLyrics"] = Current.UseSelectedLyrics;
        resources["L_SelectLyricsResult"] = Current.SelectLyricsResult;
        resources["L_InternetRadio"] = Current.InternetRadio;
        resources["L_OwnRadios"] = Current.OwnRadios;
        resources["L_RadioDirectory"] = Current.RadioDirectory;
        resources["L_RadioDirectoryHint"] = Current.RadioDirectoryHint;
        resources["L_RadioSearch"] = Current.RadioSearch;
        resources["L_RadioStation"] = Current.RadioStation;
        resources["L_Country"] = Current.Country;
        resources["L_PlayRadio"] = Current.PlayRadio;
        resources["L_AddToOwnRadios"] = Current.AddToOwnRadios;
        resources["L_DeleteRadio"] = Current.DeleteRadio;
        resources["L_RadioNowPlaying"] = Current.RadioNowPlaying;
        resources["L_RadioGenres"] = Current.RadioGenres;
        resources["L_RadioEmptyState"] = Current.RadioEmptyState;
        resources["L_OwnRadiosEmptyHint"] = Current.OwnRadiosEmptyHint;
        resources["L_ClearFilter"] = Current.ClearFilter;
        resources["L_Podcasts"] = Current.Podcasts;
        resources["L_MyPodcasts"] = Current.MyPodcasts;
        resources["L_SidebarSections"] = Current.SidebarSections;
        resources["L_SidebarSectionsHint"] = Current.SidebarSectionsHint;
        resources["L_PlexServers"] = Current.PlexServers;
        resources["L_PlexServersSettings"] = Current.PlexServersSettings;
        resources["L_PlexServersHint"] = Current.PlexServersHint;
        resources["L_AddPlexServer"] = Current.AddPlexServer;
        resources["L_PlexServerDialogTitle"] = Current.PlexServerDialogTitle;
        resources["L_PlexServerName"] = Current.PlexServerName;
        resources["L_PlexServerUrl"] = Current.PlexServerUrl;
        resources["L_PlexToken"] = Current.PlexToken;
        resources["L_PlexTestConnection"] = Current.PlexTestConnection;
        resources["L_OrynivoServers"] = Current.OrynivoServers;
        resources["L_SourceColumn"] = Current.SourceColumn;
        resources["L_OrynivoServersSettings"] = Current.OrynivoServersSettings;
        resources["L_OrynivoServersHint"] = Current.OrynivoServersHint;
        resources["L_AddOrynivoServer"] = Current.AddOrynivoServer;
        resources["L_OrynivoServerDialogTitle"] = Current.OrynivoServerDialogTitle;
        resources["L_OrynivoServerName"] = Current.OrynivoServerName;
        resources["L_OrynivoServerUrl"] = Current.OrynivoServerUrl;
        resources["L_OrynivoServerApiKey"] = Current.OrynivoServerApiKey;
        resources["L_OrynivoTestConnection"] = Current.OrynivoTestConnection;
        resources["L_OrynivoServerDirectories"] = Current.OrynivoServerDirectories;
        resources["L_OrynivoLoadServerDirectories"] = Current.OrynivoLoadServerDirectories;
        resources["L_OrynivoAddServerDirectory"] = Current.OrynivoAddServerDirectory;
        resources["L_OrynivoCalculateReplayGainDuringScan"] = Current.OrynivoCalculateReplayGainDuringScan;
        resources["L_OrynivoServerDirectoryBrowserTitle"] = Current.OrynivoServerDirectoryBrowserTitle;
        resources["L_OrynivoServerDirectoryRoots"] = Current.OrynivoServerDirectoryRoots;
        resources["L_OrynivoServerDirectoryUp"] = Current.OrynivoServerDirectoryUp;
        resources["L_OrynivoSelectServerDirectory"] = Current.OrynivoSelectServerDirectory;
        resources["L_OrynivoServerScan"] = Current.OrynivoServerScan;
        resources["L_OrynivoStartServerScan"] = Current.OrynivoStartServerScan;
        resources["L_OrynivoCalculateServerReplayGain"] = Current.OrynivoCalculateServerReplayGain;
        resources["L_OrynivoServerBackup"] = Current.OrynivoServerBackup;
        resources["L_OrynivoDownloadBackup"] = Current.OrynivoDownloadBackup;
        resources["L_OrynivoRestoreBackup"] = Current.OrynivoRestoreBackup;
        resources["L_LoadMore"] = Current.LoadMore;
        resources["L_PodcastInfo"] = Current.PodcastInfo;
        resources["L_ClosePodcastInfo"] = Current.ClosePodcastInfo;
        resources["L_PodcastDirectory"] = Current.PodcastDirectory;
        resources["L_PodcastDirectoryHint"] = Current.PodcastDirectoryHint;
        resources["L_PodcastSearch"] = Current.PodcastSearch;
        resources["L_PodcastEmptyState"] = Current.PodcastEmptyState;
        resources["L_MyPodcastsEmptyHint"] = Current.MyPodcastsEmptyHint;
        resources["L_Podcast"] = Current.Podcast;
        resources["L_PodcastAuthor"] = Current.PodcastAuthor;
        resources["L_PlayLatestEpisode"] = Current.PlayLatestEpisode;
        resources["L_AddToMyPodcasts"] = Current.AddToMyPodcasts;
        resources["L_DeletePodcast"] = Current.DeletePodcast;
        resources["L_ShowEpisodes"] = Current.ShowEpisodes;
        resources["L_Published"] = Current.Published;
        resources["L_Progress"] = Current.Progress;
        resources["L_PodcastStatus"] = Current.PodcastStatus;
        resources["L_PodcastCategories"] = Current.PodcastCategories;
        resources["L_PodcastLanguages"] = Current.PodcastLanguages;
        resources["L_PodcastLanguage"] = Current.PodcastLanguage;
        resources["L_PodcastOverview"] = Current.PodcastOverview;
        resources["L_DailyHistoryTitle"] = Current.DailyHistoryTitle;
        resources["L_PlayedAt"] = Current.PlayedAt;
        resources["L_ListenedDuration"] = Current.ListenedDuration;
        resources["L_MediaType"] = Current.MediaType;
        resources["L_Close"] = Current.Close;
        resources["L_DailyHistoryNoEntries"] = Current.DailyHistoryNoEntries;
        resources["L_RefreshView"] = Current.RefreshView;
        resources["L_LibraryDataAvailable"] = Current.LibraryDataAvailable;
        resources["L_Integration"]      = Current.Integration;
        resources["L_McpServer"]        = Current.McpServer;
        resources["L_McpServerHint"]    = Current.McpServerHint;
        resources["L_McpServerEnabled"] = Current.McpServerEnabled;
        resources["L_McpServerPort"]    = Current.McpServerPort;
        resources["L_McpNetworkAccess"] = Current.McpNetworkAccess;
        resources["L_McpNetworkAccessHint"] = Current.McpNetworkAccessHint;
        resources["L_McpAccessToken"] = Current.McpAccessToken;
        resources["L_McpAccessTokenWatermark"] = Current.McpAccessTokenWatermark;
        resources["L_McpGenerateToken"] = Current.McpGenerateToken;
        resources["L_MobileRemote"] = Current.MobileRemote;
        resources["L_MobileRemoteHint"] = Current.MobileRemoteHint;
        resources["L_MobileRemoteEnabled"] = Current.MobileRemoteEnabled;
        resources["L_MobileRemotePort"] = Current.MobileRemotePort;
        resources["L_MobileRemoteToken"] = Current.MobileRemoteToken;
        resources["L_MobileRemoteNetworkAddress"] = Current.MobileRemoteNetworkAddress;
        resources["L_MobileRemoteQrHint"] = Current.MobileRemoteQrHint;
        resources["L_McpToolsHeader"]          = Current.McpToolsHeader;
        resources["L_McpToolsHint"]            = Current.McpToolsHint;
        resources["L_WebBrowsing"]             = Current.WebBrowsing;
        resources["L_WebBrowsingHint"]         = Current.WebBrowsingHint;
        resources["L_WebBrowsingEnabled"]      = Current.WebBrowsingEnabled;
        resources["L_SearxngUrl"]              = Current.SearxngUrl;
        resources["L_WebBlockPrivate"]         = Current.WebBlockPrivate;
        resources["L_WebMaxResults"]           = Current.WebMaxResults;
        resources["L_WebTimeoutSeconds"]       = Current.WebTimeoutSeconds;
        resources["L_WebMaxResponseKb"]        = Current.WebMaxResponseKb;
        resources["L_AiChat"]                  = Current.AiChat;
        resources["L_AiChatSettings"]          = Current.AiChatSettings;
        resources["L_AiChatHint"]              = Current.AiChatHint;
        resources["L_AiChatEnabled"]           = Current.AiChatEnabled;
        resources["L_AiChatEndpointUrl"]       = Current.AiChatEndpointUrl;
        resources["L_AiChatApiKey"]            = Current.AiChatApiKey;
        resources["L_AiChatLocalNote"]         = Current.AiChatLocalNote;
        resources["L_AiChatModel"]             = Current.AiChatModel;
        resources["L_AiChatLoadModels"]        = Current.AiChatLoadModels;
        resources["L_AiChatAvailableModels"]   = Current.AiChatAvailableModels;
        resources["L_AiChatTestConnection"]    = Current.AiChatTestConnection;
        resources["L_AiChatMaxTokens"]         = Current.AiChatMaxTokens;
        resources["L_AiChatInputPlaceholder"]  = Current.AiChatInputPlaceholder;
        resources["L_AiChatSend"]              = Current.AiChatSend;
        resources["L_AiChatClear"]             = Current.AiChatClear;
        resources["L_AiChatCopy"]              = Current.AiChatCopy;
        resources["L_AiChatNotEnabled"]        = Current.AiChatNotEnabled;
        resources["L_RemoteCache"]             = Current.RemoteCache;
        resources["L_ClearRemoteCacheAll"]     = Current.ClearRemoteCacheAll;
        resources["L_RestoreQueue"]            = Current.RestoreQueue;
        resources["L_RestoreQueueTooltip"]     = Current.RestoreQueueTooltip;
        resources["L_ClearQueue"]              = Current.ClearQueue;
        resources["L_AiChatEmptyResponse"]     = Current.AiChatEmptyResponse;
        resources["L_AiChatToolResultFallback"] = Current.AiChatToolResultFallback;
    }


    private static readonly LocalizedStrings German = new(
        "BIBLIOTHEK", "Künstler", "Alben", "Tracks", "Ordnerstruktur", "Suche", "Playlists", "Über", "Einstellungen",
        "Filter", "Favoriten", "Audiotypen", "Bitrate",
        "Kein Gerät ausgewählt.", "DARSTELLUNG", "Farbschema", "Sprache", "WIEDERGABE", "Ausgabegerät",
        "BIBLIOTHEK", "Verzeichnisse", "+ Verzeichnis hinzufügen", "Datenbankwartung",
        "Datenbank optimieren", "Album-Cover reparieren", "Fehlende Cover-Artworks herunterladen",
        "Automatischer Download findet nur Cover, wenn eine MusicBrainz-ID gesetzt ist. Für freiere Suchen nutze die Schaltfläche direkt in der Albumansicht.",
        "Cover nicht gefunden", "Cover suchen", "Cover suchen", "Suche nach passenden Covern …",
        "Keine Cover gefunden.", "Album-Suchbegriff", "Künstler (optional)", "Erneut suchen", "Ausgewähltes Cover übernehmen",
        "Cover löschen", "Cover neu zuordnen", "Autor", "Lizenzen", "Speichern", "Abbrechen", "Tabelle", "Artwork",
        "(Unbekannt)", "Album-Künstler", "Jahr", "Titel", "Künstler", "Album", "Genre", "Dauer", "Format",
        "Suchbegriff {0} wurde nicht in Tracks gefunden.",
        "Suchbegriff {0} wurde nicht in Alben gefunden.",
        "Suchbegriff {0} wurde nicht in Künstlern gefunden.",
        "{0:N0} Einträge", "{0:N0} Titel",
        "Bitte zuerst einen Track doppelklicken.", "Wiedergabe gestoppt.", "Wiedergabe beendet.",
        "Bitte zuerst ein ASIO-Gerät in den Einstellungen auswählen.", "Bitte zuerst ein WASAPI-Gerät in den Einstellungen auswählen.",
        "{0} ist noch nicht implementiert.", "Einstellungen gespeichert.", "Geräteinfo konnte nicht gelesen werden: {0}",
        "Keine aktiven WASAPI-Ausgabegeräte gefunden.", "Keine ASIO-Treiber gefunden.", "Gerät auswählen und speichern.",
        "Scan läuft…", "Verzeichnis nicht gefunden.", "Scan abgebrochen.", "Datenbank wird optimiert …",
        "Optimierung abgeschlossen.", "Optimierung fehlgeschlagen: {0}", "Album-Cover werden repariert …",
        "{0:N0} Album-Cover repariert.", "Cover-Reparatur fehlgeschlagen: {0}",
        "Fehlende Cover-Artworks werden heruntergeladen …", "{0:N0} fehlende Cover-Artworks heruntergeladen.",
        "Cover-Download fehlgeschlagen: {0}",
        "Zur Playlist hinzufügen", "Neue Playlist …", "Neue Playlist", "Name der Playlist",
        "Erstellen",
        "Track zur Playlist »{0}« hinzugefügt.", "{0} Tracks zur Playlist »{1}« hinzugefügt.",
        "Playlist löschen", "Von Playlist entfernen",
        "Playlist »{0}« gelöscht.", "Track von Playlist entfernt.",
        "Filter als Smart-Playlist speichern", "Smart-Playlist »{0}« gespeichert.",
        "Bitte zuerst einen Filter auswählen.",
        "Bibliothek sichern",
        "Exportiert Datenbank, Playlists, Verlauf, Cover und Verzeichnisliste als ZIP. Audiodateien sind nicht enthalten.",
        "Bibliothek exportieren", "Bibliothek importieren",
        "Bibliothek wird exportiert …", "Bibliothek wurde nach »{0}« exportiert.",
        "Bibliothek konnte nicht exportiert werden: {0}",
        "Der Import ersetzt die aktuelle Bibliothek, Playlists, den Verlauf und alle Cover. Fortfahren?",
        "Bibliothek wird importiert und der Suchindex neu aufgebaut …",
        "Bibliothek wurde importiert. Orynivo wird jetzt beendet und kann anschließend neu gestartet werden.",
        "Bibliothek konnte nicht importiert werden: {0}",
        "Bitte laufende Bibliotheksscans oder Wartungsarbeiten zuerst beenden.",
        "Orynivo-Bibliothek (*.zip)|*.zip",
        "Bibliothek wird exportiert: {0}% – {1}",
        "Bibliothek wird importiert: {0}% – {1}",
        "Songtext", "Songtext anzeigen", "Songtext neu laden", "Songtext schließen",
        "Songtext wird geladen …", "Songtext wird von LRCLIB heruntergeladen …",
        "Für diesen Track sind keine Metadaten verfügbar.", "Kein Songtext gefunden.",
        "Songtext konnte nicht heruntergeladen werden.",
        "KÜNSTLERINFO", "Künstlerinfo anzeigen", "Künstlerinfo neu laden", "Künstlerinfo schließen",
        "Künstlerinfo wird geladen …", "Künstlerinfo wird heruntergeladen …",
        "Keine Künstlerinfo gefunden.", "Künstlerinfo konnte nicht heruntergeladen werden.",
        "Kein Bild heruntergeladen", "Bilddatei fehlt", "Bild konnte nicht geladen werden",
        "Quelle: Wikipedia", "Quelle: Last.fm",
        "Quelle für Künstlerinfos", "Last.fm API-Schlüssel",
        "Kostenlosen API-Schlüssel erstellen unter: last.fm/api/account/create",
        "Fanart.tv API-Schlüssel",
        "Bevorzugt kuratierte Künstlerbilder. Der Schlüssel wird verschlüsselt im benutzergebundenen Zugangsdaten-Tresor gespeichert; alternativ kann FANART_TV_API_KEY gesetzt werden. Für bereits geladene Künstler »Künstlerinfo neu laden« wählen. Schlüssel unter fanart.tv/get-an-api-key/ erstellen.",
        "Fehlende Künstlerbilder herunterladen",
        "Durchsucht die lokale Bibliothek und alle konfigurierten Orynivo Server sequentiell. Pro Künstler wird zuerst Fanart.tv (mit API-Schlüssel), danach Wikimedia versucht. Fanart.tv-Funde können automatisch übernommen werden; Wikimedia-Funde müssen immer bestätigt werden.",
        "Künstlerbild {0}/{1} wird gesucht: {2} · {3}",
        "{0:N0} Künstlerbilder angenommen, {1:N0} abgelehnt; {2:N0} Abfragen fehlgeschlagen.",
        "Künstlerbild-Download fehlgeschlagen: {0}",
        "Künstlerbild-Download abgebrochen.",
        "Vorschlag {0}/{1}: {2} · {3}",
        "Lokale und Server-Künstler werden geladen …",
        "Restzeit wird ermittelt …",
        "Geschätzte Restzeit: {0}",
        "Künstlerbild-Vorschlag",
        "Quelle: {0}",
        "Dieses Bild wird erst nach Ihrer Bestätigung gespeichert.",
        "Annehmen",
        "Ablehnen",
        "Alle Tracks des Albums anzeigen",
        "Orynivo ist abgestürzt",
        "Ein unerwarteter Fehler ist aufgetreten. Ein Fehlerbericht wurde hier gespeichert:\n\n{0}\n\nOrynivo wird beendet.",
        "Ein unerwarteter Fehler ist aufgetreten. Der Fehlerbericht konnte nicht gespeichert werden. Orynivo wird beendet.")
    {
        AutoAcceptFanartTvImages = "Fanart.tv-Funde automatisch übernehmen",
        OutputType = "Ausgabeart",
        AsioOutputDevice = "ASIO-Ausgabegerät",
        CwAsioOutputDevice = "cwASIO-Ausgabegerät",
        SteinbergAsio = "Steinberg ASIO",
        CwAsio = "cwASIO",
        WasapiOutputDevice = "WASAPI-Ausgabegerät",
        AirPlay = "AirPlay 2",
        AirPlayOutputDevice = "AirPlay-2-Ausgabegerät",
        NoAirPlayDevices = "Keine AirPlay-2-Geräte im lokalen Netzwerk gefunden.",
        AirPlaySenderMissing = "Die native AirPlay-2-Bridge fehlt; alternativ wird das kompatible Hilfsprogramm „raop_play“ benötigt.",
        SelectAirPlayDevice = "Bitte zuerst ein AirPlay-Ausgabegerät auswählen.",
        OpenAl = "OpenAL",
        OpenAlOutputDevice = "OpenAL-Ausgabegerät",
        DirectAlsa = "ALSA (direkt, exklusiv)",
        AlsaOutputDevice = "Direktes ALSA-Ausgabegerät",
        LinuxDefaultAudioDevice = "Systemstandard (OpenAL)",
        OpenAlInitializationFailed = "OpenAL konnte die System-Audioausgabe nicht initialisieren.",
        AlsaExactOpenFailed = "Das ALSA-Gerät „{0}“ kann nicht ohne Resampling mit {1} Hz geöffnet werden: {2}",
        AlsaDeviceBusy = "Das direkte ALSA-Gerät „{0}“ ist bereits durch PipeWire oder eine andere Anwendung belegt. Das Umleiten der Systemausgabe gibt das Gerät nicht frei. Deaktivieren Sie das Sound-Geräteprofil im System oder wählen Sie den OpenAL-Ausgang.",
        AlsaPrepareFailed = "ALSA konnte das Ausgabegerät nach dem Spulen nicht vorbereiten.",
        DeviceInfo = "Geräteinfo",
        LocalMedia = "Lokal",
        OutputProfile = "Ausgabe",
        OutputProfileCreate = "Ausgabe erstellen",
        OutputProfileConfigure = "Ausgabe konfigurieren",
        OutputProfileDelete = "Ausgabe löschen",
        OutputProfileCreateTitle = "Neue Ausgabe anlegen",
        OutputProfileConfigureTitle = "Ausgabe konfigurieren",
        OutputProfileName = "Name der Ausgabe",
        OutputProfileNameExists = "Eine Ausgabe mit diesem Namen ist bereits vorhanden.",
        OutputProfileDeleteTitle = "Ausgabe löschen",
        OutputProfileDeleteConfirm = "Soll die Ausgabe »{0}« wirklich gelöscht werden?",
        UserProfiles = "Benutzerprofile", UserProfileActive = "Aktives Profil", UserProfileCreate = "Profil anlegen", UserProfileRename = "Profil umbenennen", UserProfileDelete = "Profil löschen", UserProfileName = "Profilname", UserProfileMigrateFavorites = "Sollen die bisherigen persönlichen Daten (Favoriten, Bewertungen und Verlauf) in das neue Profil übernommen werden?", UserProfileDeleteConfirm = "Soll das Profil »{0}« wirklich gelöscht werden?",
        DatabaseOptimizeHint = "Freigegebene Seiten werden entfernt; danach ist die Datei physisch kleiner.",
        GenreCloudCache = "Genre-Wolken-Hintergründe",
        GenreCloudCacheHint = "Zwischengespeicherte Künstler-Mosaike löschen. Sie werden beim nächsten Öffnen einer Genre-Ebene neu erstellt.",
        GenreCloudCacheCleared = "Der Cache der Genre-Wolken-Hintergründe wurde geleert.",
        GenreCloudBackground = "Genre-Wolke",
        GenreCloudBackgroundHint = "Hintergrundbilder auswählen oder für eine geringere Systemlast vollständig deaktivieren.",
        GenreCloudBackgroundNone = "Keine Hintergrundbilder",
        GenreCloudBackgroundAlbums = "Albumcover",
        GenreCloudBackgroundArtists = "Künstlerbilder",
        GenreCloudVisibility = "Sichtbarkeit der Bilder",
        ClearGenreCloudCache = "Hintergrund-Cache leeren",
        AppearanceNavItem = "Darstellung",
        ArtistInfoNavItem = "Künstlerinfo",
        AsioBridgeMissing = "Dieser Build enthält keine ASIO-Unterstützung. Bitte WASAPI verwenden.",
        KernelStreamingUnavailable = "Kernel Streaming ist auswählbar, aber noch nicht als Wiedergabe-Backend implementiert.",
        AddMusicDirectory = "Musikverzeichnis hinzufügen",
        TrackCountTooltip = "Anzahl Titel in der Datenbank",
        Scan = "Scannen",
        RefreshAllMetadata = "Metadaten neu einlesen",
        RefreshAllMetadataHint = "Liest die Metadaten aller Dateien erneut ein, auch wenn ihr Zeitstempel unverändert ist. Dies kann deutlich länger dauern.",
        RemoveDirectory = "Verzeichnis entfernen",
        ScanCompleted = "Fertig: {0} Dateien · {1} neu · {2} aktualisiert · {3} entfernt{4}",
        ScanFailed = "Fehler: {0}",
        StartupPreparingLibrary = "Bibliothek wird vorbereitet …",
        StartupCheckingSearchIndex = "Suchindex wird geprüft …",
        SearchIndexRebuilding = "Suchindex wird im Hintergrund neu aufgebaut ({0}/{1}) …",
        SearchIndexReady = "Suchindex ist aktualisiert.",
        SearchIndexFailed = "Suchindex konnte nicht aktualisiert werden: {0}",
        Back = "Zurück",
        MarkAsFavorite = "Als Favorit markieren",
        OpenAlbum = "Album öffnen",
        OpenArtist = "Künstler öffnen",
        ToggleFavorite = "Favorit umschalten",
        PlaybackThrough = "Wiedergabe über {0}",
        PlaybackThroughWithDsdConversion = "Wiedergabe über {0} · DSD wird in PCM ({1:N0} Hz) konvertiert",
        NativeDsdOutput = "DSD nativ",
        DsdToPcmOutput = "DSD → PCM",
        DopOutput = "DSD über DoP",
        DopRequiresDirectAlsa = "DSD über DoP benötigt unter Linux ein direktes ALSA-Ausgabegerät ohne Resampling.",
        ReplayGain = "ReplayGain-Lautstärkeanpassung",
        ReplayGainHint = "Gilt für PCM-Wiedergabe. Im Track-Modus wird bevorzugt der Track-Wert verwendet, im Album-Modus der Album-Wert. Native DSD-Ausgabe bleibt bitgenau.",
        ReplayGainOff = "Aus",
        ReplayGainTrack = "Track",
        ReplayGainAlbum = "Album",
        CalculateReplayGainDuringScan = "Fehlendes ReplayGain automatisch während lokaler Bibliotheksscans berechnen (langsamer)",
        CalculateReplayGain = "Fehlendes ReplayGain berechnen",
        ReplayGainCalculating = "ReplayGain wird berechnet …",
        ReplayGainCalculated = "ReplayGain berechnet: {0} Tracks aktualisiert.",
        ReplayGainCalculationFailed = "ReplayGain-Berechnung fehlgeschlagen: {0}",
        NonGaplessCrossfade = "Fade für nicht-gapless Queues (Sekunden)",
        NonGaplessCrossfadeHint = "0 deaktiviert den Übergang. Gilt nur für Queue-Wechsel, die nicht bereits über die Gapless-PCM-Engine laufen.",
        ReplayGainBadge = "RG",
        DsdPlayback = "DSD-Wiedergabe",
        AlwaysConvertDsdToPcm = "DSD-Dateien immer in PCM umwandeln",
        AlwaysConvertDsdToPcmHint = "Verwendet auch mit ASIO/cwASIO den PCM-Pfad, damit Lautstärke, ReplayGain und Equalizer wirken. Bei deaktivierter Option bleibt native DSD-Ausgabe bitgenau.",
        DsdOverPcm = "DSD über DoP ausgeben",
        DsdOverPcmHint = "Verpackt DSD bitgenau in PCM-Frames (DSD over PCM). Erfordert einen DoP-fähigen DAC und eine exakte, nicht resampelnde Ausgabe; Lautstärke, ReplayGain und Equalizer bleiben wirkungslos.",
        PcmOutputBoost = "PCM-Ausgabe um +6 dB anheben",
        PcmOutputBoostHint = "Hebt alle PCM-Wiedergabewege an, um sie näher an die Lautheit nativer DSD-Ausgabe anzugleichen. Native DSD-Ausgabe bleibt bitgenau und unverändert.",
        OutputDevicesLoading = "Ausgabegeräte werden geladen …",
        Equalizer = "Parametrischer Equalizer",
        ReleaseOutputDevice = "Ausgabegerät freigeben",
        ReacquireOutputDevice = "Ausgabegerät wieder übernehmen und Wiedergabe fortsetzen",
        OutputDeviceReleased = "Ausgabegerät ist freigegeben",
        EqualizerHint = "Importiert Equalizer-APO- und AutoEQ-Profile für PCM sowie DSD-zu-PCM. Native DSD-Ausgabe bleibt bitgenau.",
        EqualizerEnabled = "Equalizer aktivieren",
        EqualizerImport = "APO-/AutoEQ-Profil importieren",
        EqualizerImporting = "Equalizer-Profil wird importiert …",
        EqualizerImportTitle = "Equalizer-APO- oder AutoEQ-Profil importieren",
        EqualizerNoProfile = "Kein Profil importiert.",
        EqualizerProfileSummary = "{0} · Vorverstärkung {1:+0.##;-0.##;0} dB · {2} Filter",
        EqualizerImportFailed = "Das Profil konnte nicht importiert werden.",
        EqualizerProfileFileType = "Equalizer-APO-/AutoEQ-Profil",
        EqualizerPreamp = "Vorverstärkung (dB)",
        EqualizerFilterType = "Filtertyp",
        EqualizerFrequency = "Frequenz (Hz)",
        EqualizerGain = "Pegel (dB)",
        EqualizerQ = "Q-Faktor",
        EqualizerAddFilter = "Filter hinzufügen",
        EqualizerRemoveFilter = "Filter entfernen",
        EqualizerPeak = "Peak",
        EqualizerLowShelf = "Tiefen-Shelf",
        EqualizerHighShelf = "Höhen-Shelf",
        EqualizerLowPass = "Tiefpass",
        EqualizerHighPass = "Hochpass",
        EqualizerCreate = "Equalizer anlegen",
        EqualizerCreateTitle = "Neuen Equalizer anlegen",
        EqualizerName = "Name des Equalizers",
        EqualizerNameExists = "Ein Equalizer mit diesem Namen ist bereits vorhanden.",
        EqualizerDelete = "Equalizer löschen",
        EqualizerDeleteTitle = "Equalizer löschen",
        EqualizerDeleteConfirm = "Soll der Equalizer „{0}“ wirklich gelöscht werden?",
        SelectColumns = "Spalten auswählen",
        FileName = "Dateiname", FileSize = "Dateigröße", AddedAt = "Hinzugefügt",
        SampleRate = "Samplerate", BitDepth = "Bittiefe", Channels = "Kanäle",
        TrackNumber = "Tracknummer", DiscNumber = "Discnummer", Composer = "Komponist",
        Bpm = "BPM", ReplayGainTrackColumn = "ReplayGain Track",
        ReplayGainAlbumColumn = "ReplayGain Album", Codec = "Codec", Tags = "Tags",
        PersonalRating = "Eigene Bewertung", MusicBrainzRating = "MusicBrainz-Bewertung", MusicBrainzLoadRating = "Bewertung laden", MusicBrainzLoadingRating = "Wird geladen …", MusicBrainzRetryRating = "Erneut versuchen", MusicBrainzNoRating = "Keine Bewertung",
        RatingSetHint = "Eigene Bewertung festlegen", RatingUpdateFailed = "Bewertung konnte nicht gespeichert werden.",
        Homepage = "Homepage", FeedUrl = "Feed-Adresse",
        SearchResultSummary = "{0:N0} Titel · {1:N0} Alben · {2:N0} Künstler",
        RecentAlbums = "Zuletzt hinzugefügte Alben",
        AlbumRecommendations = "Album-Empfehlungen",
        RecommendationMoodAll = "Alle Stimmungen",
        RecommendationMoodRelaxed = "Entspannt",
        RecommendationMoodEnergetic = "Energiegeladen",
        RecommendationMoodHappy = "Fröhlich",
        RecommendationMoodMelancholic = "Melancholisch",
        RecommendationNoMatches = "Noch nicht genügend passende Hörhistorie für Empfehlungen.",
        PlayMoreLikeThis = "Mehr wie dieser Titel", PlayMoodMix = "Stimmungs-Mix", SimilarTracksLoading = "Ähnliche Titel werden geladen …", SimilarTracksUnavailable = "Ähnlichkeitsdaten sind für diesen Titel nicht verfügbar.", SimilarTracksNoMatches = "Keine ähnlichen Titel gefunden.", SimilarTracksQueued = "{0:N0} ähnliche Titel wurden zur Wiedergabe vorbereitet.",
        RecommendationListView = "Liste",
        RecommendationStageView = "Bühne",
        MetadataProblems = "Metadaten prüfen",
        MetadataNoFindings = "Bisher keine Befunde für die gewählten Filter.",
        MetadataWorkflow = "1. Befunde ansehen   →   2. Ordner auswählen   →   3. Vorschlag vergleichen und bestätigen",
        MetadataInspectFiles = "Auch Dateien prüfen (Lesbarkeit und Duplikat-Prüfsummen; langsamer)",
        MetadataQuickHint = "Beim Öffnen werden nur gespeicherte Metadaten geprüft – keine Musikdateien gelesen. Für die gründliche Prüfung die Option aktivieren und „Analyse aktualisieren“ wählen. Ältere Server benötigen für die schnelle Prüfung ein Update.",
        MetadataSelectHint = "Wähle einen Ordner. Die Prüfung ändert nichts und löscht keine Dateien.",
        MetadataActionGuide = "„Ordner als Album erkennen“ sucht Titel, Künstler und Nummerierung bei MusicBrainz. Fehlendes ReplayGain: Einstellungen → Wiedergabe. Bilder: Album-/Künstleransicht. Fehlende Dateien und Duplikate bitte manuell prüfen. Änderungen werden erst nach Bestätigung in der Bibliothek gespeichert; Audiodateien bleiben unverändert.",
        MetadataRemoteReadOnly = "Dieser Eintrag stammt vom Server und ist hier nur ein Bericht. Die MusicBrainz-Ordnerkorrektur ist nur für lokale Einträge verfügbar. ReplayGain lässt sich unter Orynivo Server berechnen; Bilder in der Album-/Künstleransicht ergänzen.",
        MetadataReviewGuide = "Oben stehen deine aktuellen Tracks. Wähle unten eine Veröffentlichung und vergleiche die Vorschau zeilenweise. Suchbegriffe kannst du ändern. Nur „Korrektur übernehmen“ speichert Änderungen in der Bibliothek.",
        MetadataPhaseDatabase = "Gespeicherte Metadaten werden geladen…",
        MetadataPhaseFolders = "Ordner und Metadaten werden geprüft…",
        MetadataPhaseHashes = "Prüfsummen möglicher Duplikate werden berechnet…",
        MetadataPhaseServers = "Warte auf Serverberichte. Der Server liefert keine Restzeit; fertige Befunde sind bereits nutzbar.",
        MetadataPhaseReleases = "MusicBrainz-Veröffentlichungen und Tracklisten werden geladen…",
        MetadataPhaseSaving = "Bestätigte Korrektur wird gespeichert und Suchindex aktualisiert…",
        MetadataRemaining = "Geschätzte Restzeit dieses Schritts: {0}",
        MetadataRemainingUnknown = "Restzeit noch nicht abschätzbar",
        MetadataElapsed = "Verstrichen: {0}",
        MetadataProblemsHint = "Orynivo prüft physische Ordner unabhängig von möglicherweise zerrissenen Albumzuordnungen. Doppelklicke einen Eintrag, um passende MusicBrainz-Veröffentlichungen zu suchen.",
        IdentifyFolderAsAlbum = "Ordner als Album identifizieren",
        MetadataFolder = "Ordner",
        MetadataIssues = "Erkannte Probleme",
        MetadataTrackCount = "Titel",
        MetadataReviewTitle = "Album-Metadaten prüfen",
        MetadataSearching = "MusicBrainz wird anhand von Titelanzahl und Laufzeiten durchsucht…",
        MetadataNoMatch = "Keine ausreichend passende Veröffentlichung gefunden.",
        MetadataSearchFailed = "MusicBrainz ist momentan nicht erreichbar. Bitte versuche die Suche erneut.",
        MetadataFoundReleases = "Gefundene Veröffentlichungen",
        MetadataApplyCorrection = "Korrektur übernehmen",
        MetadataCorrectionPreview = "Vorschau der Korrektur",
        MetadataCurrentValues = "Aktuell: Titel — Künstler",
        MetadataProposedValues = "Vorschlag: Titel — Künstler",
        MetadataRefreshAnalysis = "Analyse aktualisieren",
        MetadataAlbumQuery = "Album-Suchbegriff",
        MetadataArtistQuery = "Künstler-Suchbegriff",
        MetadataRepairSuccess = "Die Metadaten wurden in Orynivos Bibliothek korrigiert.",
        MetadataIssueAlbums = "unterschiedliche Albumtitel",
        MetadataIssueArtists = "unterschiedliche Albumkünstler",
        MetadataIssueMissingTitles = "fehlende Titelnamen",
        MetadataIssueMissingNumbers = "fehlende Tracknummern",
        MetadataIssueDuplicateNumbers = "doppelte Tracknummern",
        MetadataIssueMissingReplayGain = "{0} ohne ReplayGain",
        MetadataIssueMissingMusicBrainzIds = "{0} ohne MusicBrainz-ID",
        MetadataSeverity = "Priorität",
        MetadataSeverityAll = "Alle Prioritäten",
        MetadataIssueAll = "Alle Befundarten",
        MetadataIssueReplayGain = "Fehlendes ReplayGain",
        MetadataIssueMusicBrainzIds = "Fehlende MusicBrainz-ID",
        MetadataIssueIncompleteAlbum = "Unvollständiges Album ({0} Tracks fehlen)",
        MetadataIssueAlbumArtwork = "Fehlendes Albumcover",
        MetadataIssueArtistImage = "Fehlendes Künstlerbild",
        MetadataIssueMissingFiles = "{0} Quelldateien fehlen",
        MetadataIssueUnreadableFiles = "{0} Quelldateien nicht lesbar",
        MetadataIssueLikelyDuplicates = "{0} wahrscheinliche Dateidubletten",
        MetadataIssueExactDuplicates = "{0} byte-identische Dateidubletten",
        MetadataIssueAlternateRecordings = "{0} Aufnahmen in anderer Datei oder Edition",
        MetadataIssueArtistNameVariants = "{0} abweichende Künstler-Schreibweisen",
        MetadataSeverityInformation = "Hinweis",
        MetadataSeverityWarning = "Warnung",
        MetadataSeverityError = "Fehler",
        MetadataDoctorSummary = "{0} Fehler · {1} Warnungen · {2} Hinweise",
        MetadataAnalysisFailed = "Die Analyse ist fehlgeschlagen. Details wurden im Fehlerprotokoll gespeichert.",
        MetadataDoctorServersUnavailable = "{0} Server nicht erreichbar oder noch ohne Library Doctor",
        MetadataAnalysisCancelled = "Die Analyse wurde abgebrochen.",
        Calendar = "Kalender – {0}",
        TopGenres = "Meistgehörte Genres",
        TopAlbums = "Meistgehörte Alben",
        TopArtists = "Meistgehörte Künstler",
        ListeningStats = "Hör-Statistik",
        PeriodAllTime = "Gesamt",
        PeriodThisYear = "Dieses Jahr",
        PeriodThisMonth = "Dieser Monat",
        PeriodLast30Days = "Letzte 30 Tage",
        PeriodLast7Days = "Letzte 7 Tage",
        HistorySourceRemote = "Remote",
        HistorySourcePlex = "Plex",
        LibraryUpdating = "Bibliothek wird aktualisiert…",
        LibraryUpdatingWithCount = "Bibliothek wird aktualisiert… {0} / {1} Dateien",
        RefreshView = "Aktualisieren",
        LibraryDataAvailable = "Neue Bibliotheksdaten verfügbar",
        ServerUnreachable = "Nicht erreichbar",
        ServerLastConnected = "Zuletzt verbunden: {0}",
        ServerNeverConnected = "Noch nie verbunden",
        ServerMissingFeatures = "Server unterstützt nicht: {0}",
        CapabilityTrackFacets = "Track-Facets",
        CapabilityRecentAlbums = "Recent Albums",
        CapabilityWaveforms = "Waveforms",
        RemoteCache = "Remote-Cache",
        RemoteCacheSize = "Cache-Größe: {0}",
        ClearRemoteCacheAll = "Gesamten Cache leeren",
        ClearCache = "Cache leeren",
        RemoteScanning = "{0} wird aktualisiert…",
        RemoteScanningWithCount = "{0} wird aktualisiert… {1} / {2} Dateien",
        SmartPlaylistPreviewCount = "{0} Tracks passen",
        SmartPlaylistPreviewComputing = "Wird berechnet…",
        SmartPlaylistPreviewInvalid = "Ungültige Kriterien",
        RestoreQueue = "Letzte Queue",
        RestoreQueueTooltip = "Zuletzt gespielte Queue wiederherstellen",
        NoPreviousQueue = "Keine vorherige Queue vorhanden.",
        ClearQueue = "Queue leeren",
        QueueCleared = "Queue geleert.",
        NoData = "Keine Daten vorhanden.",
        RecentlyPlayed = "Zuletzt gespielt",
        GreetingMorning = "Guten Morgen",
        GreetingAfternoon = "Guten Tag",
        GreetingEvening = "Guten Abend",
        DashboardTagline = "Deine persönliche Musikzentrale",
        DashboardWelcomeBack = "WILLKOMMEN ZURÜCK",
        DashboardHeroHint = "Bereit für großartige Musik? Entdecke neue Klänge oder höre deine Favoriten.",
        DashboardRandomPlayback = "Zufällige Wiedergabe",
        InfiniteMixStart = "Endlos-Mix starten",
        GenreCloudInfiniteMix = "Endlos-Mix aus Wolke",
        InfiniteMixStop = "Endlos-Mix stoppen",
        InfiniteMixActive = "Endlos-Mix aktiv · wird automatisch ergänzt",
        InfiniteMixCalculating = "Dein Endlos-Mix wird gerade berechnet …",
        InfiniteMixSettingsTitle = "Endlos-Mix anpassen",
        InfiniteMixSettingsHint = "Lege fest, wie Orynivo deine nächste fortlaufende Mischung zusammenstellt.",
        InfiniteMixMood = "Stimmung", InfiniteMixMoodCalm = "Ruhig", InfiniteMixMoodBalanced = "Ausgewogen", InfiniteMixMoodEnergetic = "Energiegeladen",
        InfiniteMixDiscovery = "Entdeckungsgrad", InfiniteMixFamiliar = "Vertraut", InfiniteMixAdventurous = "Abenteuerlich",
        InfiniteMixPeriod = "Hörverlauf", InfiniteMixSources = "Quellen",
        InfiniteMixWeightFavorites = "Favoriten stärker gewichten", InfiniteMixPreferRare = "Selten gehörte Titel bevorzugen",
        InfiniteMixIncludeGenres = "Genres einschließen", InfiniteMixExcludeGenres = "Genres ausschließen", InfiniteMixGenresWatermark = "Genre eingeben …", InfiniteMixAddGenre = "Hinzufügen", InfiniteMixRemoveGenre = "Genre entfernen",
        InfiniteMixPaused = "Endlos-Mix pausiert", InfiniteMixPause = "Endlos-Mix pausieren", InfiniteMixResume = "Endlos-Mix fortsetzen",
        InfiniteMixAdjust = "Mischung anpassen", InfiniteMixReplaceNext = "Nächsten Vorschlag neu wählen",
        InfiniteMixMoreLikeThis = "Mehr davon", InfiniteMixLessLikeThis = "Weniger davon", InfiniteMixExcludeTrack = "Titel künftig ausschließen",
        DashboardQuickAccess = "Schnellzugriff",
        DashboardTotalMinutes = "Gesamtminuten",
        DashboardMinutesShort = "Min.",
        PeriodPrevious = "zum vorherigen Zeitraum",
        ShowAll = "Alle anzeigen",
        DevicePcmSampleRates = "Unterstützte PCM-Sampleraten",
        DeviceDsdRates = "DSD-Stufen",
        DevicePcmFormats = "PCM-Ausgabeformate",
        DeviceDsdFormats = "DSD-Ausgabeformate",
        DeviceChannelSummary = "{0} Ausgangskanäle · {1} Eingangskanäle",
        DeviceBufferSummary = "Puffer: min. {0}, bevorzugt {1}, max. {2}, Granularität {3}",
        DriverProvidedNoInformation = "Keine Angaben vom Treiber.",
        DsdSupportedWithoutFormats = "DSD-Modus unterstützt; keine konkreten Kanalformate gemeldet.",
        Unsupported = "Nicht unterstützt.",
        DeviceProbeInconclusive = "Konnte nicht eindeutig geprüft werden. Das Gerät wird möglicherweise von einer anderen Anwendung verwendet.",
        WasapiEndpointSummary = "WASAPI-Endpunkt · {0} Kanäle\nMix-Format: {1} · {2} Bit",
        WasapiNoExclusiveFormats = "Keine exklusiven PCM-Formate erkannt.",
        WasapiDsdNotRelevant = "Für WASAPI in Orynivo nicht relevant.",
        LinuxAlsaEndpointSummary = "Direktes ALSA-Gerät · {0} Kanäle\nPCM: {1} Bit · exakte Titel-Samplerate\nALSA-Resampling deaktiviert",
        LinuxOpenAlEndpointSummary = "OpenAL-Gerät · {0} Kanäle\nPCM: {1} Bit · Mixer-Samplerate wird beim Abspielen ermittelt",
        LinuxDsdOutputUnavailable = "Für diesen PCM-Ausgabepfad derzeit nicht verfügbar.",
        NativeDsdUsesAsio = "Native DSD-Wiedergabe läuft in Orynivo über ASIO.",
        Dashboard = "Dashboard", ThemeLight = "Hell", ThemeDark = "Dunkel",
        StatusAvailable = "Verfügbar", StatusUnavailable = "Nicht verfügbar",
        StatusEnabled = "Aktiviert", StatusDisabled = "Deaktiviert", StatusReady = "Bereit",
        StatusChecking = "Prüfe …",
        DashboardIntroTitle = "Dein Hörüberblick",
        DashboardIntroHint = "Sieh zuletzt hinzugefügte Alben, Hörzeiten im Kalender und deine meistgehörten Genres auf einen Blick.",
        ArtistsIntroTitle = "Künstler entdecken",
        ArtistsIntroHint = "Durchstöbere deine Bibliothek nach Künstlern, öffne Alben direkt und pflege Favoriten sowie Künstlerbilder.",
        AlbumsIntroTitle = "Alben durchsuchen",
        AlbumsIntroHint = "Wechsle zwischen Tabellen- und Artwork-Ansicht, öffne Albumtracks und ergänze fehlende Cover.",
        TracksIntroTitle = "Tracks verwalten",
        TracksIntroHint = "Suche, filtere und spiele deine lokale Musikbibliothek mit Genre-, Format- und Bitraten-Facetten.",
        FoldersIntroTitle = "Ordnerstruktur",
        FoldersIntroHint = "Navigiere deine Musik entlang der eingebundenen Bibliotheksordner und spiele Tracks direkt aus ihrem Ordnerkontext.",
        LanguageGerman = "Deutsch", LanguageEnglish = "Englisch", LanguageFrench = "Französisch", LanguageSpanish = "Spanisch",
        LanguageRussian = "Russisch", LanguageChineseSimplified = "Chinesisch (vereinfacht)",
        PcmIntegerFormat = "{0}-Bit PCM, Little Endian ({1})",
        PcmContainerFormat = "{0}-Bit PCM im {1}-Bit-Container, Little Endian ({2})",
        PcmFloatFormat = "{0}-Bit-Gleitkomma-PCM, Little Endian ({1})",
        NativeDsdLsbFormat = "Natives DSD, 1-Bit-Daten, erstes Sample im niederwertigsten Bit ({0})",
        NativeDsdMsbFormat = "Natives DSD, 1-Bit-Daten, erstes Sample im höchstwertigen Bit ({0})",
        NativeDsdWordFormat = "Natives DSD, 8-Bit-Wörter ohne Endian-Relevanz ({0})",
        CountEntrySingular = "{0:N0} Eintrag", CountTrackSingular = "{0:N0} Titel"
        , NormalizeArtists = "Künstlernamen normalisieren"
        , NormalizeArtistsHint = "Entfernt »feat.«-Zusätze vom Hauptkünstler und führt eindeutige Schreibvarianten wie Satzzeichen- und Leerzeichenunterschiede zusammen. Audiodateien werden nicht verändert."
        , ArtistsNormalizing = "Künstlernamen werden normalisiert und der Suchindex wird neu aufgebaut …"
        , ArtistsNormalized = "{0:N0} Künstlervarianten zusammengeführt, {1:N0} Tracks aktualisiert."
        , ArtistNormalizationFailed = "Künstlernormalisierung fehlgeschlagen: {0}"
        , Streaming = "STREAMING"
        , StreamingServices = "Streamingdienste"
        , Qobuz = "Qobuz"
        , QobuzApplicationId = "Qobuz-Anwendungs-ID"
        , QobuzIntegrationHint = "Die Qobuz-Integration ist vorbereitet. Katalog und Wiedergabe werden aktiviert, sobald ein genehmigter Partnerzugang und die offizielle API-Dokumentation vorliegen."
        , QobuzCredentialsHint = "Geheime Schlüssel und Anmeldetokens werden nicht in settings.json gespeichert, sondern benutzergebunden durch Windows geschützt."
        , SearchArtistImage = "Künstlerbild suchen"
        , UploadArtistImage = "Künstlerbild hochladen"
        , DeleteArtistImage = "Künstlerbild löschen"
        , UploadCover = "Cover hochladen"
        , ImageFileType = "Bilddateien"
        , ArtistImageSearchTitle = "Künstlerbild suchen"
        , ArtistImageSearchRunning = "Passende Künstlerbilder werden gesucht …"
        , ArtistImageSearchNoResults = "Keine Künstlerbilder gefunden."
        , ArtistImageSearchQuery = "Suchbegriff"
        , ArtistImageSearchFailed = "Die Künstlerbildsuche ist fehlgeschlagen."
        , UseSelectedArtistImage = "Ausgewähltes Bild übernehmen"
        , ArtistImageDownloadFailed = "Das ausgewählte Künstlerbild konnte nicht gespeichert werden."
        , ArtistProfileSearchTitle = "Künstlerinfo neu laden"
        , ArtistProfileSearchHint = "Passe bei Bedarf den Namen an, mit dem Wikipedia oder Last.fm nach der Künstlerinfo sucht. Der Künstlername in deiner Bibliothek wird dadurch nicht geändert."
        , ArtistProfileSearchQuery = "Name für die Profilsuche"
        , ArtistProfileSearchLoad = "Info laden"
        , EditArtistName = "Künstlername ändern"
        , ArtistName = "Künstlername"
        , RenameArtist = "Umbenennen"
        , MergeArtistsTitle = "Künstler zusammenführen"
        , ArtistNameExistsMessage = "Ein Künstler mit dem Namen „{0}“ ist bereits vorhanden. Sollen beide Künstler zusammengeführt werden? Wähle, welcher Datensatz und dessen Profilinformationen erhalten bleiben sollen."
        , KeepArtistProfile = "„{0}“ priorisieren und zusammenführen"
        , ArtistRenameFailed = "Der Künstler konnte nicht umbenannt oder zusammengeführt werden."
        , Shuffle = "Zufallswiedergabe"
        , SearchLyrics = "Songtext suchen"
        , LyricsSearchTitle = "Songtext suchen"
        , LyricsSearchRunning = "Passende Songtexte werden gesucht …"
        , LyricsSearchNoResults = "Keine passenden Songtexte gefunden."
        , LyricsSearchFailed = "Die Songtextsuche ist fehlgeschlagen."
        , UseSelectedLyrics = "Ausgewählten Songtext übernehmen"
        , SelectLyricsResult = "Wähle links einen Songtext für die Vorschau aus."
        , SynchronizedLyrics = "Synchronisiert"
        , InternetRadio = "Internet Radio"
        , OwnRadios = "EIGENE RADIOS"
        , SidebarSections = "Sidebar-Bereiche"
        , SidebarSectionsHint = "Legt fest, welche aufklappbaren Bereiche in der Hauptnavigation angezeigt werden."
        , PodcastInfo = "Podcast-Informationen"
        , ShowPodcastInfo = "Podcast-Informationen anzeigen"
        , ClosePodcastInfo = "Podcast-Informationen schließen"
        , PodcastPublishedOn = "Veröffentlicht am {0}"
        , PodcastEpisodeDuration = "Laufzeit {0}"
        , PodcastDescriptionUnavailable = "Für diese Folge ist keine Zusammenfassung verfügbar."
        , RadioDirectory = "Sender entdecken"
        , RadioDirectoryHint = "Durchsuche das freie Radio-Browser-Verzeichnis und füge Sender dauerhaft zu deinen eigenen Radios hinzu."
        , RadioSearch = "Sender suchen"
        , RadioStation = "Sender"
        , Country = "Land"
        , PlayRadio = "Abspielen"
        , AddToOwnRadios = "Zu eigenen Radios"
        , DeleteRadio = "Sender löschen"
        , RadioLoading = "Radiosender werden geladen …"
        , RadioNoResults = "Keine passenden Radiosender gefunden."
        , RadioEmptyState = "Suche nach Sendername, Land oder Genre, oder speichere gefundene Sender unter „Eigene Radios“."
        , OwnRadiosEmptyHint = "Noch keine eigenen Radios gespeichert. Suche im Internetradio-Verzeichnis und füge Sender hinzu."
        , RadioAdded = "Radiosender „{0}“ wurde hinzugefügt."
        , RadioDeleted = "Radiosender „{0}“ wurde gelöscht."
        , RadioSearchFailed = "Radiosender konnten nicht geladen werden."
        , RadioNowPlaying = "JETZT IM RADIO"
        , RadioMetadataUnavailable = "Der Sender stellt aktuell keine Titelinformationen bereit."
        , RadioGenres = "Genres"
        , ClearFilter = "Filter löschen"
        , Podcasts = "Podcasts"
        , MyPodcasts = "MEINE PODCASTS"
        , PodcastDirectory = "Podcasts entdecken"
        , PodcastDirectoryHint = "Durchsuche das Apple-Podcast-Verzeichnis, pinne Podcasts dauerhaft an und spiele die neueste Episode aus dem RSS-Feed ab."
        , PodcastSearch = "Podcasts suchen"
        , PodcastEmptyState = "Suche nach einem Podcast, filtere nach Kategorie oder Sprache und pinne Favoriten unter „Meine Podcasts“."
        , Podcast = "Podcast"
        , PodcastAuthor = "Autor"
        , PlayLatestEpisode = "Neueste abspielen"
        , AddToMyPodcasts = "Zu meinen Podcasts"
        , DeletePodcast = "Podcast löschen"
        , PodcastLoading = "Podcasts werden geladen …"
        , PodcastNoResults = "Keine passenden Podcasts gefunden."
        , MyPodcastsEmptyHint = "Noch keine Podcasts angepinnt. Suche im Podcast-Verzeichnis und füge Podcasts hinzu."
        , PodcastAdded = "Podcast „{0}“ wurde hinzugefügt."
        , PodcastDeleted = "Podcast „{0}“ wurde gelöscht."
        , PodcastSearchFailed = "Podcasts konnten nicht geladen werden."
        , PodcastFeedFailed = "Im Podcast-Feed wurde keine abspielbare Episode gefunden."
        , ShowEpisodes = "Folgen anzeigen"
        , Published = "Veröffentlicht"
        , Progress = "Fortschritt"
        , PodcastStatus = "Status"
        , PodcastUnplayed = "Neu"
        , PodcastInProgress = "Begonnen"
        , PodcastPlayed = "Gehört"
        , PodcastEpisodesLoading = "Podcast-Folgen werden geladen …"
        , PodcastNoEpisodes = "In diesem Feed wurden keine abspielbaren Folgen gefunden."
        , PodcastCategories = "Kategorien"
        , PodcastLanguages = "Sprachen"
        , PodcastLanguage = "Sprache"
        , PodcastLanguagesLoading = "Podcast-Sprachen werden aus den Feeds ermittelt …"
        , PodcastOverview = "PODCAST-ÜBERSICHT"
        , PodcastEpisodeTotal = "{0:N0} Folgen insgesamt"
        , PodcastEpisodeUnheard = "{0:N0} noch nicht gehört"
        , PodcastEpisodeStarted = "{0:N0} begonnen"
        , PodcastLatestEpisode = "Neueste Folge: {0}"
        , DailyHistoryTitle = "Hörverlauf – {0}"
        , PlayedAt = "Gehört um"
        , ListenedDuration = "Gehört"
        , MediaType = "Typ"
        , Close = "Schließen"
        , DailyHistoryNoEntries = "Für diesen Tag sind keine Wiedergaben vorhanden."
        , PlexServers = "PLEX-SERVER"
        , PlexServersSettings = "Plex-Server"
        , PlexServersHint = "Richte einen oder mehrere Plex Media Server ein. Zugriffstokens werden geschützt für das aktuelle Windows-Benutzerkonto gespeichert."
        , AddPlexServer = "Plex-Server hinzufügen"
        , PlexServerDialogTitle = "Plex-Server"
        , PlexServerName = "Anzeigename"
        , PlexServerUrl = "Server-URL"
        , PlexToken = "X-Plex-Token (optional)"
        , PlexTestConnection = "Verbindung testen"
        , PlexTestingConnection = "Verbindung wird geprüft …"
        , PlexConnectionSuccessful = "Verbindung erfolgreich. {0:N0} Audio-Bibliotheken gefunden."
        , PlexConnectionFailed = "Verbindung fehlgeschlagen: {0}"
        , PlexServerFieldsRequired = "Name und Server-URL sind erforderlich."
        , PlexServerUrlInvalid = "Bitte eine gültige HTTP- oder HTTPS-URL eingeben."
        , PlexEditServer = "Bearbeiten"
        , PlexRemoveServer = "Entfernen"
        , PlexNoAudioLibraries = "Keine Audio-Bibliotheken gefunden."
        , PlexLoading = "Plex-Inhalte werden geladen …"
        , OrynivoServers = "ORYNIVO-SERVER"
        , VersionLabel = "Version {0}"
        , CheckForUpdates = "Nach Updates suchen"
        , Updates = "Updates"
        , CheckForUpdatesOnStartup = "Beim Programmstart nach Updates suchen"
        , WindowBehavior = "Fensterverhalten"
        , StartMaximized = "Maximiert starten"
        , CheckingForUpdates = "Updates werden gesucht …"
        , UpdateAvailable = "Version {0} ist verfügbar."
        , UpToDate = "Orynivo ist auf dem neuesten Stand."
        , UpdateUnavailable = "Verifizierte Updates sind für diesen Build nicht eingerichtet."
        , DownloadAndInstall = "Herunterladen und installieren"
        , DownloadingUpdate = "Update wird heruntergeladen und geprüft …"
        , InstallingUpdate = "Update wird installiert …"
        , UpdateFailed = "Das Update konnte nicht geprüft oder installiert werden."
        , UpdateServer = "Server aktualisieren"
        , UpdatingServer = "Update wird übertragen …"
        , ServerUpdateQueued = "Update gestartet"
        , ServerUpdateUnavailable = "Kein neueres, unterstütztes Server-Update verfügbar."
        , ServerUpdateFailed = "Server-Update fehlgeschlagen"
        , ServerUpdateRejected = "Server lehnt Update ab (HTTP {0})"
        , UpdatingNamedServer = "Server »{0}« wird aktualisiert …"
        , ServerUpdatesFailedContinue = "Folgende Server konnten nicht aktualisiert werden: {0}. Desktop-Update trotzdem fortsetzen?"
        , SourceColumn = "Quelle"
        , LocalSource = "Lokal"
        , LocalSourceShort = "L"
        , OrynivoServersSettings = "Orynivo-Server"
        , OrynivoServersHint = "Verbinde den Player mit einem oder mehreren Orynivo-Server-Instanzen im lokalen Netzwerk. Der API-Key wird verschlüsselt im benutzergebundenen Zugangsdaten-Tresor gespeichert."
        , AddOrynivoServer = "Server hinzufügen"
        , OrynivoServerDialogTitle = "Orynivo-Server"
        , OrynivoServerName = "Anzeigename"
        , OrynivoServerUrl = "Server-URL (z. B. http://192.168.1.10:5280)"
        , OrynivoServerApiKey = "API-Key"
        , OrynivoTestConnection = "Verbindung testen"
        , OrynivoTestingConnection = "Verbindung wird geprüft …"
        , OrynivoConnectionSuccessful = "Verbindung erfolgreich. Server: {0} v{1}"
        , OrynivoConnectionFailed = "Verbindung fehlgeschlagen. URL und API-Key prüfen."
        , OrynivoServerFieldsRequired = "Name, URL und API-Key sind erforderlich."
        , OrynivoEditServer = "Bearbeiten"
        , OrynivoRemoveServer = "Entfernen"
        , OrynivoLoading = "Server-Inhalte werden geladen …"
        , OrynivoServerDirectories = "Server-Musikverzeichnisse"
        , OrynivoLoadServerDirectories = "Vom Server laden"
        , OrynivoAddServerDirectory = "Verzeichnis hinzufügen"
        , OrynivoLoadingServerDirectories = "Server-Verzeichnisse werden geladen …"
        , OrynivoServerDirectoriesLoaded = "Server-Verzeichnisse wurden geladen."
        , OrynivoServerDirectoriesLoadFailed = "Server-Verzeichnisse konnten nicht geladen werden."
        , OrynivoSavingServerDirectories = "Server-Verzeichnisse werden gespeichert …"
        , OrynivoServerDirectoriesSaveFailed = "Server-Verzeichnisse konnten nicht gespeichert werden."
        , OrynivoCalculateReplayGainDuringScan = "Fehlendes ReplayGain während Server-Scans berechnen (langsamer)"
        , OrynivoSavingReplayGainSettings = "ReplayGain-Scaneinstellung wird auf dem Server gespeichert …"
        , OrynivoReplayGainSettingsSaveFailed = "Die ReplayGain-Scaneinstellung konnte nicht auf dem Server gespeichert werden."
        , OrynivoReplayGainSettingsUnsupported = "Verzeichnisse geladen. Dieser Server unterstützt die ReplayGain-Scaneinstellung noch nicht."
        , OrynivoNoServerDirectories = "Keine Server-Verzeichnisse gesetzt."
        , OrynivoServerDirectoryBrowserTitle = "Server-Verzeichnis auswählen"
        , OrynivoServerDirectoryRoots = "Laufwerke"
        , OrynivoServerDirectoryUp = "Nach oben"
        , OrynivoSelectServerDirectory = "Auswählen"
        , OrynivoServerDirectoryLoading = "Verzeichnis wird geladen …"
        , OrynivoServerDirectoryLoadFailed = "Verzeichnis konnte nicht geladen werden."
        , OrynivoServerDirectoryEmpty = "Keine Unterverzeichnisse vorhanden."
        , OrynivoServerScan = "Server-Scan"
        , OrynivoStartServerScan = "Bibliothek scannen"
        , OrynivoCalculateServerReplayGain = "ReplayGain berechnen"
        , OrynivoServerScanStarting = "Server-Scan wird gestartet …"
        , OrynivoServerScanStartFailed = "Server-Scan konnte nicht gestartet werden."
        , OrynivoServerScanIdle = "Kein Server-Scan aktiv."
        , OrynivoServerScanDiscovering = "Dateien werden gesucht: {0}"
        , OrynivoServerScanProgress = "{0}/{1} · {2}"
        , OrynivoServerScanCompleted = "Scan abgeschlossen: {0} Dateien, {1} neu, {2} aktualisiert, {3} entfernt, {4} fehlgeschlagen."
        , OrynivoServerScanFailed = "Server-Scan fehlgeschlagen: {0}"
        , OrynivoServerBackup = "Server-Bibliothek sichern"
        , OrynivoDownloadBackup = "Sicherung herunterladen"
        , OrynivoRestoreBackup = "Sicherung einspielen"
        , OrynivoBackupDownloading = "Server-Sicherung wird heruntergeladen …"
        , OrynivoBackupDownloaded = "Server-Sicherung wurde gespeichert: {0}"
        , OrynivoBackupRestoring = "Server-Sicherung wird geprüft und eingespielt …"
        , OrynivoBackupRestored = "Server-Sicherung wurde erfolgreich eingespielt."
        , OrynivoBackupFailed = "Server-Sicherung fehlgeschlagen: {0}"
        , OrynivoRestoreBackupConfirm = "Der Import ersetzt Datenbank, Playlists, Verlauf, Cover, Künstlerbilder und Verzeichnisliste des Servers. Audiodateien bleiben unverändert. Fortfahren?"
        , LoadMore = "Mehr laden"
        , FfmpegDownloading = "FFmpeg wird heruntergeladen …"
        , FfmpegDownloadFailed = "FFmpeg konnte nicht heruntergeladen werden. Bitte manuell installieren: ffmpeg.org"
        , SmartPlaylistDialogTitle = "Smart-Playlist bearbeiten"
        , SmartPlaylistName = "Name"
        , SmartPlaylistBasicFilters = "Grundfilter"
        , SmartPlaylistGenres = "Genres (durch Komma getrennt)"
        , SmartPlaylistFormats = "Formate (z. B. FLAC, MP3; durch Komma getrennt)"
        , SmartPlaylistBitrates = "Bitraten in kbps (durch Komma getrennt)"
        , SmartPlaylistSources = "Quellen (local oder server:<id>; durch Komma getrennt)"
        , SmartPlaylistMetadata = "Metadaten"
        , SmartPlaylistMinimumYear = "Jahr von"
        , SmartPlaylistMaximumYear = "Jahr bis"
        , SmartPlaylistSearchText = "Suchtext enthält"
        , SmartPlaylistArtistContains = "Künstler enthält"
        , SmartPlaylistAlbumContains = "Album enthält"
        , SmartPlaylistMinimumDuration = "Mindestdauer in Minuten"
        , SmartPlaylistMaximumDuration = "Maximaldauer in Minuten"
        , SmartPlaylistHistory = "Bibliothek und Wiedergabeverlauf"
        , SmartPlaylistAddedWithinDays = "Hinzugefügt innerhalb der letzten X Tage"
        , SmartPlaylistPlayedWithinDays = "Gespielt innerhalb der letzten X Tage"
        , SmartPlaylistNeverPlayed = "Noch nie gespielt"
        , SmartPlaylistMinimumPlayCount = "Mindestens so oft gespielt"
        , SmartPlaylistMaximumPlayCount = "Höchstens so oft gespielt"
        , SmartPlaylistResult = "Ergebnis"
        , SmartPlaylistSortOrder = "Sortierung"
        , SmartPlaylistSortTitle = "Titel A–Z"
        , SmartPlaylistSortRandom = "Zufällig"
        , SmartPlaylistSortLastPlayed = "Zuletzt gespielt zuerst"
        , SmartPlaylistSortLeastRecentlyPlayed = "Lange nicht gehört zuerst"
        , SmartPlaylistResultLimit = "Maximale Titelanzahl (leer = unbegrenzt)"
        , CreateSmartPlaylist = "Smart-Playlist erstellen"
        , InvalidSmartPlaylistCriteria = "Bitte gültige Zahlen und widerspruchsfreie Mindest-/Maximalwerte eingeben. „Noch nie gespielt“ kann nicht mit einer kürzlichen Wiedergabe oder einer Mindestanzahl kombiniert werden."
        , EditSmartPlaylist = "Smart-Playlist bearbeiten"
        , LibraryEmptyHint = "Noch keine Medienquelle eingerichtet. Öffne Einstellungen > Bibliothek, füge lokale Musikordner hinzu oder verbinde einen Orynivo Server."
        , SmartPlaylistUpdated = "Smart-Playlist »{0}« aktualisiert."
        , ImportM3u8Playlist = "M3U8-Playlist importieren"
        , ExportM3u8Playlist = "Als M3U8 exportieren"
        , SaveAlbumAsPlaylist = "Als Playlist speichern"
        , AlbumPath = "Albumpfad"
        , TrackInfo = "Titelinformationen"
        , ShowTrackInfo = "Titelinformationen anzeigen"
        , PhysicalPath = "Physischer Dateipfad"
        , UpNext = "Als Nächstes"
        , GenreExplorer = "Genre-Wolke"
        , GenreCloudHint = "Entdecke Genres aus deiner lokalen Bibliothek und allen verbundenen Orynivo Servern. Wähle ein Genre, um tiefer einzusteigen."
        , AllGenres = "Alle Genres"
        , GenreRecommendations = "Passende Titel"
        , GenreCloudEmpty = "Noch keine Genres gefunden. Prüfe, ob deine Titel Genre-Tags enthalten."
        , MoreGenres = "Weitere Genres"
        , PlayNext = "Als Nächstes abspielen"
        , AppendToQueue = "An Warteschlange anhängen"
        , RemoveFromQueue = "Aus Warteschlange entfernen"
        , MoveUp = "Nach oben"
        , MoveDown = "Nach unten"
        , SaveQueueAsPlaylist = "Warteschlange als Playlist speichern"
        , TracksQueuedNext = "{0:N0} Titel werden als Nächstes abgespielt."
        , TracksAppendedToQueue = "{0:N0} Titel an die Warteschlange angehängt."
        , M3u8ImportCompleted = "Playlist »{0}« importiert: {1} Einträge · {2} lokale Dateien fehlen · {3} HTTP-Einträge · {4} übersprungen."
        , M3u8ImportNoEntries = "Die M3U8-Datei enthält keine importierbaren Einträge."
        , M3u8ImportFailed = "M3U8-Import fehlgeschlagen: {0}"
        , M3u8ExportCompleted = "Playlist »{0}« als M3U8 exportiert: {1} Einträge · {2} übersprungen."
        , M3u8ExportFailed = "M3U8-Export fehlgeschlagen: {0}"
        , Integration      = "INTEGRATION"
        , McpServer        = "MCP-Server"
        , McpServerHint    = "Öffnet einen lokalen HTTP/SSE-Server, über den KI-Assistenten (z. B. Claude Desktop) den Player steuern und die Bibliothek durchsuchen können."
        , McpServerEnabled = "MCP-Server aktivieren"
        , McpServerPort    = "Port"
        , McpNetworkAccess = "Zugriff aus dem lokalen Netzwerk erlauben"
        , McpNetworkAccessHint = "Bindet MCP an alle Netzwerkschnittstellen. Netzwerkzugriffe benötigen den Bearer-Token. Ohne HTTPS oder VPN kann der Token im Netzwerk mitgelesen werden."
        , McpAccessToken = "Zugriffstoken"
        , McpAccessTokenWatermark = "Wird beim Aktivieren automatisch erzeugt"
        , McpGenerateToken = "Neu erzeugen"
        , MobileRemote = "Mobile Web-Fernbedienung"
        , MobileRemoteHint = "Stellt eine Fernbedienung im lokalen Netzwerk bereit. Verwende den eigenen Zugriffstoken und schütze den Zugriff außerhalb des Heimnetzes mit HTTPS oder VPN."
        , MobileRemoteEnabled = "Mobile Fernbedienung im Netzwerk aktivieren"
        , MobileRemotePort = "Port"
        , MobileRemoteToken = "Zugriffstoken der Fernbedienung"
        , MobileRemoteNetworkAddress = "IP-Adresse im Heimnetz"
        , MobileRemoteQrHint = "Zuerst Einstellungen speichern. Im selben WLAN den QR-Code für die direkte Anmeldung scannen. Er enthält den Zugriffstoken: nicht teilen! Beim manuellen URL-Aufruf wird der Token abgefragt. Bei mehreren Adressen die vom Telefon erreichbare wählen."
        , McpToolsHeader   = "Tools"
        , McpToolsHint     = "Einzelne Tools aktivieren oder deaktivieren."
        , WebBrowsing        = "Web-Browsing"
        , WebBrowsingHint    = "Stellt der KI kontrollierte Web-Werkzeuge bereit: SearXNG-Suche und sicheres Laden von Seiten. Private und lokale Adressen werden blockiert (SSRF-Schutz)."
        , WebBrowsingEnabled = "Web-Tools aktivieren"
        , SearxngUrl         = "SearXNG-Adresse"
        , WebBlockPrivate    = "Private/lokale Adressen blockieren (SSRF-Schutz)"
        , WebMaxResults      = "Maximale Suchergebnisse"
        , WebTimeoutSeconds  = "Timeout (Sekunden)"
        , WebMaxResponseKb   = "Maximale Antwortgröße (KB)"
        , AiChat                = "KI-Chat"
        , AiChatSettings        = "KI-Chat"
        , AiChatHint            = "Verbindet sich mit einem lokalen oder cloudbasierten KI-Modell über eine OpenAI-kompatible API (z. B. LM Studio, Ollama, OpenAI). Dem Modell stehen alle 32 Orynivo-Tools zur Verfügung, um die Bibliothek zu durchsuchen, Playlisten zu verwalten und die Wiedergabe zu steuern."
        , AiChatEnabled         = "KI-Chat aktivieren"
        , AiChatEndpointUrl     = "Endpunkt-URL"
        , AiChatApiKey          = "API-Schlüssel (optional)"
        , AiChatLocalNote       = "LM Studio und Ollama benötigen keinen API-Schlüssel."
        , AiChatModel           = "Modell"
        , AiChatLoadModels      = "Modelle laden"
        , AiChatAvailableModels = "Verfügbares Modell auswählen"
        , AiChatTestConnection  = "Verbindung testen"
        , AiChatConnectionTesting = "Verbindung wird geprüft …"
        , AiChatModelsLoaded    = "{0} Modelle geladen."
        , AiChatConnectionSucceeded = "Verbindung erfolgreich; {0} Modelle verfügbar."
        , AiChatNoModels        = "Verbindung erfolgreich, aber der Endpunkt hat keine Modelle gemeldet."
        , AiChatConnectionFailed = "Verbindung fehlgeschlagen. Bitte Endpunkt-URL und API-Schlüssel prüfen."
        , AiChatMaxTokens       = "Maximale Token"
        , AiChatInputPlaceholder = "Frage stellen …"
        , AiChatSend            = "Senden"
        , AiChatClear           = "Löschen"
        , AiChatCopy            = "Kopieren"
        , AiChatNotEnabled      = "KI-Chat ist nicht aktiviert. Bitte unter Einstellungen › KI-Chat aktivieren."
        , AiChatEmptyResponse   = "Das Modell hat eine leere Antwort zurückgegeben."
        , AiChatToolResultFallback = "Das Modell hat nach dem Tool-Aufruf keine finale Antwort erzeugt. Tool-Ergebnis:"
    };

    private static readonly LocalizedStrings English = new(
        "LIBRARY", "Artists", "Albums", "Tracks", "Folder structure", "Search", "Playlists", "About", "Settings",
        "Filter", "Favorites", "Audio types", "Bitrate",
        "No device selected.", "APPEARANCE", "Color scheme", "Language", "PLAYBACK", "Output device",
        "LIBRARY", "Directories", "+ Add directory", "Database maintenance",
        "Optimize database", "Repair album artwork", "Download missing artwork",
        "Automatic download only finds covers when a MusicBrainz ID is present. For freer searches, use the button directly in the album view.",
        "Cover not found", "Search cover", "Search cover", "Searching for matching covers …",
        "No covers found.", "Album search term", "Artist (optional)", "Search again", "Use selected cover",
        "Delete cover", "Reassign cover", "Author", "Licenses", "Save", "Cancel", "Table", "Artwork",
        "(Unknown)", "Album artist", "Year", "Title", "Artist", "Album", "Genre", "Duration", "Format",
        "Search term {0} was not found in tracks.",
        "Search term {0} was not found in albums.",
        "Search term {0} was not found in artists.",
        "{0:N0} entries", "{0:N0} tracks",
        "Please double-click a track first.", "Playback stopped.", "Playback finished.",
        "Please select an ASIO device in settings first.", "Please select a WASAPI device in settings first.",
        "{0} is not implemented yet.", "Settings saved.", "Device info could not be read: {0}",
        "No active WASAPI output devices found.", "No ASIO drivers found.", "Select a device and save.",
        "Scanning…", "Directory not found.", "Scan canceled.", "Optimizing database …",
        "Optimization completed.", "Optimization failed: {0}", "Repairing album artwork …",
        "{0:N0} album covers repaired.", "Artwork repair failed: {0}",
        "Downloading missing artwork …", "{0:N0} missing artworks downloaded.",
        "Artwork download failed: {0}",
        "Add to playlist", "New playlist …", "New playlist", "Playlist name",
        "Create",
        "Track added to playlist '{0}'.", "{0} tracks added to playlist '{1}'.",
        "Delete playlist", "Remove from playlist",
        "Playlist '{0}' deleted.", "Track removed from playlist.",
        "Save filters as smart playlist", "Smart playlist '{0}' saved.",
        "Please select a filter first.",
        "Library backup",
        "Exports the database, playlists, history, artwork, and directory list as ZIP. Audio files are not included.",
        "Export library", "Import library",
        "Exporting library …", "Library exported to '{0}'.",
        "Library export failed: {0}",
        "Import replaces the current library, playlists, history, and all artwork. Continue?",
        "Importing library and rebuilding the search index …",
        "Library imported. Orynivo will now close and can then be restarted.",
        "Library import failed: {0}",
        "Please finish active library scans or maintenance operations first.",
        "Orynivo library (*.zip)|*.zip",
        "Exporting library: {0}% – {1}",
        "Importing library: {0}% – {1}",
        "Lyrics", "Show lyrics", "Refresh lyrics", "Close lyrics",
        "Loading lyrics …", "Downloading lyrics from LRCLIB …",
        "No metadata is available for this track.", "No lyrics found.",
        "Lyrics could not be downloaded.",
        "ARTIST INFORMATION", "Show artist information", "Refresh artist information", "Close artist information",
        "Loading artist information …", "Downloading artist information …",
        "No artist information found.", "Artist information could not be downloaded.",
        "No image downloaded", "Image file missing", "Failed to load image",
        "Source: Wikipedia", "Source: Last.fm",
        "Artist info source", "Last.fm API key",
        "Create a free API key at: last.fm/api/account/create",
        "Fanart.tv API key",
        "Prefers curated artist images. The key is stored encrypted in the current user's credential vault; alternatively set FANART_TV_API_KEY. Use “Refresh artist information” for existing artists. Create a key at fanart.tv/get-an-api-key/.",
        "Download missing artist images",
        "Searches the local library and all configured Orynivo Servers sequentially. For each artist it tries Fanart.tv first (with an API key), then Wikimedia. Fanart.tv results can be accepted automatically; Wikimedia results always require confirmation.",
        "Searching for artist image {0}/{1}: {2} · {3}",
        "Accepted {0:N0} artist images, rejected {1:N0}; {2:N0} requests failed.",
        "Artist image download failed: {0}",
        "Artist image download cancelled.",
        "Suggestion {0}/{1}: {2} · {3}",
        "Loading local and server artists …",
        "Estimating remaining time …",
        "Estimated remaining time: {0}",
        "Artist image suggestion",
        "Source: {0}",
        "This image will only be saved after you confirm it.",
        "Accept",
        "Reject",
        "Show all album tracks",
        "Orynivo crashed",
        "An unexpected error occurred. A crash report was saved here:\n\n{0}\n\nOrynivo will now close.",
        "An unexpected error occurred. The crash report could not be saved. Orynivo will now close.")
    {
        AutoAcceptFanartTvImages = "Automatically accept Fanart.tv results",
        OutputType = "Output type", AsioOutputDevice = "ASIO output device", WasapiOutputDevice = "WASAPI output device",
        AirPlay = "AirPlay 2", AirPlayOutputDevice = "AirPlay 2 output device",
        NoAirPlayDevices = "No AirPlay 2 devices were found on the local network.",
        AirPlaySenderMissing = "The native AirPlay 2 bridge is missing; the compatible “raop_play” helper can be used as a fallback.",
        SelectAirPlayDevice = "Select an AirPlay output device first.",
        CwAsioOutputDevice = "cwASIO output device", SteinbergAsio = "Steinberg ASIO", CwAsio = "cwASIO",
        OpenAl = "OpenAL", OpenAlOutputDevice = "OpenAL output device",
        DirectAlsa = "ALSA (direct, exclusive)", AlsaOutputDevice = "Direct ALSA output device",
        LinuxDefaultAudioDevice = "System default (OpenAL)",
        OpenAlInitializationFailed = "OpenAL could not initialize the system audio output.",
        AlsaExactOpenFailed = "ALSA device “{0}” cannot be opened at {1} Hz without resampling: {2}",
        AlsaDeviceBusy = "Direct ALSA device “{0}” is already owned by PipeWire or another application. Redirecting system output does not release the device. Disable the sound device profile in the system or select the OpenAL output.",
        AlsaPrepareFailed = "ALSA could not prepare the output device after seeking.",
        DeviceInfo = "Device information",
        OutputProfile = "Output",
        UserProfiles = "User profiles", UserProfileActive = "Active profile", UserProfileCreate = "Create profile", UserProfileRename = "Rename profile", UserProfileDelete = "Delete profile", UserProfileName = "Profile name", UserProfileMigrateFavorites = "Copy the existing personal data (favorites, ratings, and history) to the new profile?", UserProfileDeleteConfirm = "Delete profile “{0}”?",
        LocalMedia = "Local",
        OutputProfileCreate = "Create output",
        OutputProfileConfigure = "Configure output",
        OutputProfileDelete = "Delete output",
        OutputProfileCreateTitle = "Create new output",
        OutputProfileConfigureTitle = "Configure output",
        OutputProfileName = "Output name",
        OutputProfileNameExists = "An output with this name already exists.",
        OutputProfileDeleteTitle = "Delete output",
        OutputProfileDeleteConfirm = "Are you sure you want to delete the output “{0}”?", DatabaseOptimizeHint = "Released pages are removed so the file becomes physically smaller.",
        GenreCloudCache = "Genre Cloud backgrounds",
        GenreCloudCacheHint = "Clear cached artist mosaics. They are rebuilt the next time a genre level is opened.",
        GenreCloudCacheCleared = "The Genre Cloud background cache has been cleared.",
        GenreCloudBackground = "Genre Cloud",
        GenreCloudBackgroundHint = "Choose background artwork or disable it completely to reduce system load.",
        GenreCloudBackgroundNone = "No background images",
        GenreCloudBackgroundAlbums = "Album covers",
        GenreCloudBackgroundArtists = "Artist images",
        GenreCloudVisibility = "Image visibility",
        ClearGenreCloudCache = "Clear background cache",
        AppearanceNavItem = "Appearance", ArtistInfoNavItem = "Artist information",
        AsioBridgeMissing = "This build does not include ASIO support. Please use WASAPI.",
        KernelStreamingUnavailable = "Kernel Streaming can be selected but is not implemented as a playback backend yet.",
        AddMusicDirectory = "Add music directory", TrackCountTooltip = "Number of tracks in the database",
        Scan = "Scan",
        RefreshAllMetadata = "Re-read metadata",
        RefreshAllMetadataHint = "Re-reads metadata from every file even when its timestamp is unchanged. This can take considerably longer.",
        RemoveDirectory = "Remove directory",
        ScanCompleted = "Finished: {0} files · {1} new · {2} updated · {3} removed{4}", ScanFailed = "Error: {0}",
        StartupPreparingLibrary = "Preparing library …",
        StartupCheckingSearchIndex = "Checking search index …",
        SearchIndexRebuilding = "Rebuilding search index in the background ({0}/{1}) …",
        SearchIndexReady = "Search index is up to date.",
        SearchIndexFailed = "Search index could not be updated: {0}",
        Back = "Back", MarkAsFavorite = "Mark as favorite",
        OpenAlbum = "Open album",
        OpenArtist = "Open artist",
        ToggleFavorite = "Toggle favorite",
        PlaybackThrough = "Playback through {0}",
        PlaybackThroughWithDsdConversion = "Playback through {0} · DSD is converted to PCM ({1:N0} Hz)",
        NativeDsdOutput = "Native DSD", DsdToPcmOutput = "DSD → PCM",
        DopOutput = "DSD over DoP",
        DopRequiresDirectAlsa = "DSD over DoP requires a direct ALSA output device without resampling on Linux.",
        ReplayGain = "ReplayGain volume adjustment",
        ReplayGainHint = "Applies to PCM playback. Track mode prefers track gain; album mode prefers album gain. Native DSD output remains bit-perfect.",
        ReplayGainOff = "Off", ReplayGainTrack = "Track", ReplayGainAlbum = "Album",
        CalculateReplayGainDuringScan = "Automatically calculate missing ReplayGain during local library scans (slower)",
        CalculateReplayGain = "Calculate missing ReplayGain",
        ReplayGainCalculating = "Calculating ReplayGain …",
        ReplayGainCalculated = "ReplayGain calculated: {0} tracks updated.",
        ReplayGainCalculationFailed = "ReplayGain calculation failed: {0}",
        NonGaplessCrossfade = "Fade for non-gapless queues (seconds)",
        NonGaplessCrossfadeHint = "0 disables the transition. Applies only to queue changes that are not already handled by the gapless PCM engine.",
        ReplayGainBadge = "RG",
        DsdPlayback = "DSD playback",
        AlwaysConvertDsdToPcm = "Always convert DSD files to PCM",
        AlwaysConvertDsdToPcmHint = "Uses the PCM path with ASIO/cwASIO as well, allowing volume, ReplayGain, and the equalizer to apply. With this option disabled, native DSD output remains bit-perfect.",
        DsdOverPcm = "Output DSD over DoP",
        DsdOverPcmHint = "Packs DSD bit-perfectly into PCM frames (DSD over PCM). Requires a DoP-capable DAC and exact output without resampling; volume, ReplayGain, and the equalizer have no effect.",
        PcmOutputBoost = "Boost PCM output by +6 dB",
        PcmOutputBoostHint = "Raises all PCM playback paths so they are closer to native DSD output loudness. Native DSD output remains bit-perfect and unchanged.",
        OutputDevicesLoading = "Loading output devices …",
        Equalizer = "Parametric equalizer",
        ReleaseOutputDevice = "Release output device",
        ReacquireOutputDevice = "Reacquire output device and resume playback",
        OutputDeviceReleased = "Output device is released",
        EqualizerHint = "Imports Equalizer APO and AutoEQ profiles for PCM and DSD-to-PCM playback. Native DSD output remains bit-perfect.",
        EqualizerEnabled = "Enable equalizer",
        EqualizerImport = "Import APO/AutoEQ profile",
        EqualizerImporting = "Importing equalizer profile …",
        EqualizerImportTitle = "Import Equalizer APO or AutoEQ profile",
        EqualizerNoProfile = "No profile imported.",
        EqualizerProfileSummary = "{0} · preamp {1:+0.##;-0.##;0} dB · {2} filters",
        EqualizerImportFailed = "The profile could not be imported.",
        EqualizerProfileFileType = "Equalizer APO / AutoEQ profile",
        EqualizerPreamp = "Preamp (dB)",
        EqualizerFilterType = "Filter type",
        EqualizerFrequency = "Frequency (Hz)",
        EqualizerGain = "Gain (dB)",
        EqualizerQ = "Q factor",
        EqualizerAddFilter = "Add filter",
        EqualizerRemoveFilter = "Remove filter",
        EqualizerPeak = "Peak",
        EqualizerLowShelf = "Low shelf",
        EqualizerHighShelf = "High shelf",
        EqualizerLowPass = "Low pass",
        EqualizerHighPass = "High pass",
        EqualizerCreate = "Create equalizer",
        EqualizerCreateTitle = "Create new equalizer",
        EqualizerName = "Equalizer name",
        EqualizerNameExists = "An equalizer with this name already exists.",
        EqualizerDelete = "Delete equalizer",
        EqualizerDeleteTitle = "Delete equalizer",
        EqualizerDeleteConfirm = "Are you sure you want to delete the equalizer “{0}”?",
        SelectColumns = "Select columns",
        FileName = "File name", FileSize = "File size", AddedAt = "Added",
        SampleRate = "Sample rate", BitDepth = "Bit depth", Channels = "Channels",
        TrackNumber = "Track number", DiscNumber = "Disc number", Composer = "Composer",
        Bpm = "BPM", ReplayGainTrackColumn = "ReplayGain track",
        ReplayGainAlbumColumn = "ReplayGain album", Codec = "Codec", Tags = "Tags",
        PersonalRating = "My rating", MusicBrainzRating = "MusicBrainz rating", MusicBrainzLoadRating = "Load rating", MusicBrainzLoadingRating = "Loading …", MusicBrainzRetryRating = "Try again", MusicBrainzNoRating = "Not rated",
        RatingSetHint = "Set personal rating", RatingUpdateFailed = "The rating could not be saved.",
        Homepage = "Homepage", FeedUrl = "Feed address",
        SearchResultSummary = "{0:N0} tracks · {1:N0} albums · {2:N0} artists",
        RecentAlbums = "Recently added albums",
        AlbumRecommendations = "Album recommendations",
        RecommendationMoodAll = "All moods",
        RecommendationMoodRelaxed = "Relaxed",
        RecommendationMoodEnergetic = "Energetic",
        RecommendationMoodHappy = "Happy",
        RecommendationMoodMelancholic = "Melancholic",
        RecommendationNoMatches = "Not enough matching listening history for recommendations yet.",
        PlayMoreLikeThis = "Play more like this", PlayMoodMix = "Mood mix", SimilarTracksLoading = "Loading similar tracks …", SimilarTracksUnavailable = "Similarity data is unavailable for this track.", SimilarTracksNoMatches = "No similar tracks found.", SimilarTracksQueued = "{0:N0} similar tracks are ready to play.",
        RecommendationListView = "List",
        RecommendationStageView = "Stage",
        MetadataProblems = "Review metadata",
        MetadataNoFindings = "No findings so far for the selected filters.",
        MetadataWorkflow = "1. Review findings   →   2. Select a folder   →   3. Compare and confirm a proposal",
        MetadataInspectFiles = "Also inspect files (readability and duplicate checksums; slower)",
        MetadataQuickHint = "Opening this page checks stored metadata without reading music files. Enable the option and refresh the analysis for physical-file checks. Older servers need an update for the quick review.",
        MetadataSelectHint = "Select a folder. Analysis changes nothing and deletes no files.",
        MetadataActionGuide = "Identify folder as album searches MusicBrainz for titles, artists and numbering. Missing ReplayGain: Settings → Playback. Images: album/artist views. Review missing files and duplicates manually. Only confirmed changes are saved to the library; audio files stay unchanged.",
        MetadataRemoteReadOnly = "This server entry is a read-only report. MusicBrainz folder correction is available only for local entries. Calculate ReplayGain under Orynivo Server; add images in album/artist views.",
        MetadataReviewGuide = "Your current tracks appear above. Select a release below and compare the preview row by row. Search terms are editable. Only Apply correction saves library changes.",
        MetadataPhaseDatabase = "Loading stored metadata…",
        MetadataPhaseFolders = "Reviewing folders and metadata…",
        MetadataPhaseHashes = "Hashing possible duplicate files…",
        MetadataPhaseServers = "Waiting for server reports. The server provides no time estimate; completed findings are already available.",
        MetadataPhaseReleases = "Loading MusicBrainz releases and track lists…",
        MetadataPhaseSaving = "Saving confirmed correction and updating search index…",
        MetadataRemaining = "Estimated time left in this step: {0}",
        MetadataRemainingUnknown = "Time remaining cannot yet be estimated",
        MetadataElapsed = "Elapsed: {0}",
        MetadataProblemsHint = "Orynivo reviews physical folders independently of possibly fragmented album assignments. Double-click an entry to search for matching MusicBrainz releases.",
        IdentifyFolderAsAlbum = "Identify folder as album",
        MetadataFolder = "Folder",
        MetadataIssues = "Detected problems",
        MetadataTrackCount = "Tracks",
        MetadataReviewTitle = "Review album metadata",
        MetadataSearching = "Searching MusicBrainz by track count and durations…",
        MetadataNoMatch = "No sufficiently matching release was found.",
        MetadataSearchFailed = "MusicBrainz is currently unavailable. Please try the search again.",
        MetadataFoundReleases = "Matching releases",
        MetadataApplyCorrection = "Apply correction",
        MetadataCorrectionPreview = "Correction preview",
        MetadataCurrentValues = "Current: title — artist",
        MetadataProposedValues = "Proposed: title — artist",
        MetadataRefreshAnalysis = "Refresh analysis",
        MetadataAlbumQuery = "Album search term",
        MetadataArtistQuery = "Artist search term",
        MetadataRepairSuccess = "The metadata was corrected in Orynivo's library.",
        MetadataIssueAlbums = "inconsistent album titles",
        MetadataIssueArtists = "inconsistent album artists",
        MetadataIssueMissingTitles = "missing track titles",
        MetadataIssueMissingNumbers = "missing track numbers",
        MetadataIssueDuplicateNumbers = "duplicate track numbers",
        MetadataIssueMissingReplayGain = "{0} without ReplayGain",
        MetadataIssueMissingMusicBrainzIds = "{0} without a MusicBrainz ID",
        MetadataSeverity = "Priority",
        MetadataSeverityAll = "All priorities",
        MetadataIssueAll = "All finding types",
        MetadataIssueReplayGain = "Missing ReplayGain",
        MetadataIssueMusicBrainzIds = "Missing MusicBrainz ID",
        MetadataIssueIncompleteAlbum = "Incomplete album ({0} tracks missing)",
        MetadataIssueAlbumArtwork = "Missing album artwork",
        MetadataIssueArtistImage = "Missing artist image",
        MetadataIssueMissingFiles = "{0} source files missing",
        MetadataIssueUnreadableFiles = "{0} source files unreadable",
        MetadataIssueLikelyDuplicates = "{0} likely duplicate files",
        MetadataIssueExactDuplicates = "{0} byte-identical duplicate files",
        MetadataIssueAlternateRecordings = "{0} recordings in another file or edition",
        MetadataIssueArtistNameVariants = "{0} variant artist spellings",
        MetadataSeverityInformation = "Information",
        MetadataSeverityWarning = "Warning",
        MetadataSeverityError = "Error",
        MetadataDoctorSummary = "{0} errors · {1} warnings · {2} information",
        MetadataAnalysisFailed = "Analysis failed. Details were saved to the error log.",
        MetadataDoctorServersUnavailable = "{0} server(s) unavailable or not yet supporting Library Doctor",
        MetadataAnalysisCancelled = "Analysis was cancelled.",
        Calendar = "Calendar – {0}", TopGenres = "Most listened genres",
        TopAlbums = "Most listened albums",
        TopArtists = "Most listened artists",
        ListeningStats = "Listening stats",
        PeriodAllTime = "All time",
        PeriodThisYear = "This year",
        PeriodThisMonth = "This month",
        PeriodLast30Days = "Last 30 days",
        PeriodLast7Days = "Last 7 days",
        HistorySourceRemote = "Remote",
        HistorySourcePlex = "Plex",
        LibraryUpdating = "Updating library…",
        LibraryUpdatingWithCount = "Updating library… {0} / {1} files",
        RefreshView = "Refresh",
        LibraryDataAvailable = "New library data available",
        ServerUnreachable = "Unreachable",
        ServerLastConnected = "Last connected: {0}",
        ServerNeverConnected = "Never connected",
        ServerMissingFeatures = "Server does not support: {0}",
        CapabilityTrackFacets = "Track facets",
        CapabilityRecentAlbums = "Recent albums",
        CapabilityWaveforms = "Waveforms",
        RemoteCache = "Remote cache",
        RemoteCacheSize = "Cache size: {0}",
        ClearRemoteCacheAll = "Clear entire cache",
        ClearCache = "Clear cache",
        RemoteScanning = "Updating {0}…",
        RemoteScanningWithCount = "Updating {0}… {1} / {2} files",
        SmartPlaylistPreviewCount = "{0} tracks match",
        SmartPlaylistPreviewComputing = "Computing…",
        SmartPlaylistPreviewInvalid = "Invalid criteria",
        RestoreQueue = "Last queue",
        RestoreQueueTooltip = "Restore the last played queue",
        NoPreviousQueue = "No previous queue available.",
        ClearQueue = "Clear queue",
        QueueCleared = "Queue cleared.",
        NoData = "No data available.",
        RecentlyPlayed = "Recently played",
        GreetingMorning = "Good morning", GreetingAfternoon = "Good afternoon", GreetingEvening = "Good evening",
        DashboardTagline = "Your personal music hub",
        DashboardWelcomeBack = "WELCOME BACK",
        DashboardHeroHint = "Ready for great music? Discover something new or return to your favorites.",
        DashboardRandomPlayback = "Random playback",
        InfiniteMixStart = "Start Infinite Mix",
        GenreCloudInfiniteMix = "Start Infinite Mix from cloud",
        InfiniteMixStop = "Stop Infinite Mix",
        InfiniteMixActive = "Infinite Mix active · replenishes automatically",
        InfiniteMixCalculating = "Your Infinite Mix is being prepared …",
        InfiniteMixSettingsTitle = "Adjust Infinite Mix",
        InfiniteMixSettingsHint = "Choose how Orynivo should build your next continuously replenished mix.",
        InfiniteMixMood = "Mood", InfiniteMixMoodCalm = "Calm", InfiniteMixMoodBalanced = "Balanced", InfiniteMixMoodEnergetic = "Energetic",
        InfiniteMixDiscovery = "Discovery level", InfiniteMixFamiliar = "Familiar", InfiniteMixAdventurous = "Adventurous",
        InfiniteMixPeriod = "Listening history", InfiniteMixSources = "Sources",
        InfiniteMixWeightFavorites = "Give favorites more weight", InfiniteMixPreferRare = "Prefer rarely played tracks",
        InfiniteMixIncludeGenres = "Include genres", InfiniteMixExcludeGenres = "Exclude genres", InfiniteMixGenresWatermark = "Enter a genre…", InfiniteMixAddGenre = "Add", InfiniteMixRemoveGenre = "Remove genre",
        InfiniteMixPaused = "Infinite Mix paused", InfiniteMixPause = "Pause Infinite Mix", InfiniteMixResume = "Resume Infinite Mix",
        InfiniteMixAdjust = "Adjust mix", InfiniteMixReplaceNext = "Replace next suggestion",
        InfiniteMixMoreLikeThis = "More like this", InfiniteMixLessLikeThis = "Less like this", InfiniteMixExcludeTrack = "Exclude track in future",
        DashboardQuickAccess = "Quick access",
        DashboardTotalMinutes = "Total minutes",
        DashboardMinutesShort = "min",
        PeriodPrevious = "vs. previous period",
        ShowAll = "Show all",
        DevicePcmSampleRates = "Supported PCM sample rates", DeviceDsdRates = "DSD rates",
        DevicePcmFormats = "PCM output formats", DeviceDsdFormats = "DSD output formats",
        DeviceChannelSummary = "{0} output channels · {1} input channels",
        DeviceBufferSummary = "Buffer: min {0}, preferred {1}, max {2}, granularity {3}",
        DriverProvidedNoInformation = "No information provided by the driver.",
        DsdSupportedWithoutFormats = "DSD mode is supported; no specific channel formats were reported.",
        Unsupported = "Not supported.",
        DeviceProbeInconclusive = "Could not be checked conclusively. Another application may be using the device.",
        WasapiEndpointSummary = "WASAPI endpoint · {0} channels\nMix format: {1} · {2} bit",
        WasapiNoExclusiveFormats = "No exclusive PCM formats detected.",
        WasapiDsdNotRelevant = "Not relevant for WASAPI in this player.",
        LinuxAlsaEndpointSummary = "Direct ALSA device · {0} channels\nPCM: {1} bit · exact track sample rate\nALSA resampling disabled",
        LinuxOpenAlEndpointSummary = "OpenAL device · {0} channels\nPCM: {1} bit · mixer sample rate detected during playback",
        LinuxDsdOutputUnavailable = "Currently unavailable for this PCM output path.",
        NativeDsdUsesAsio = "Native DSD playback in this player uses ASIO.",
        Dashboard = "Dashboard", ThemeLight = "Light", ThemeDark = "Dark",
        StatusAvailable = "Available", StatusUnavailable = "Unavailable",
        StatusEnabled = "Enabled", StatusDisabled = "Disabled", StatusReady = "Ready",
        StatusChecking = "Checking …",
        DashboardIntroTitle = "Your listening overview",
        DashboardIntroHint = "See recently added albums, calendar listening time, and your top genres at a glance.",
        ArtistsIntroTitle = "Discover artists",
        ArtistsIntroHint = "Browse your library by artist, open albums directly, and manage favorites and artist images.",
        AlbumsIntroTitle = "Browse albums",
        AlbumsIntroHint = "Switch between table and artwork views, open album tracks, and fill in missing covers.",
        TracksIntroTitle = "Manage tracks",
        TracksIntroHint = "Search, filter, and play your local music library with genre, format, and bitrate facets.",
        FoldersIntroTitle = "Folder structure",
        FoldersIntroHint = "Navigate your music through configured library folders and play tracks from their folder context.",
        LanguageGerman = "German", LanguageEnglish = "English", LanguageFrench = "French", LanguageSpanish = "Spanish",
        LanguageRussian = "Russian", LanguageChineseSimplified = "Simplified Chinese",
        PcmIntegerFormat = "{0}-bit PCM, little endian ({1})",
        PcmContainerFormat = "{0}-bit PCM in a {1}-bit container, little endian ({2})",
        PcmFloatFormat = "{0}-bit floating-point PCM, little endian ({1})",
        NativeDsdLsbFormat = "Native DSD, 1-bit data, first sample in the least significant bit ({0})",
        NativeDsdMsbFormat = "Native DSD, 1-bit data, first sample in the most significant bit ({0})",
        NativeDsdWordFormat = "Native DSD, 8-bit words without endian relevance ({0})",
        CountEntrySingular = "{0:N0} entry", CountTrackSingular = "{0:N0} track"
        , NormalizeArtists = "Normalize artist names"
        , NormalizeArtistsHint = "Removes “feat.” additions from the primary artist and merges unambiguous punctuation and spacing variants. Audio files are not modified."
        , ArtistsNormalizing = "Normalizing artist names and rebuilding the search index …"
        , ArtistsNormalized = "Merged {0:N0} artist variants and updated {1:N0} tracks."
        , ArtistNormalizationFailed = "Artist normalization failed: {0}"
        , Streaming = "STREAMING"
        , StreamingServices = "Streaming services"
        , Qobuz = "Qobuz"
        , QobuzApplicationId = "Qobuz application ID"
        , QobuzIntegrationHint = "The Qobuz integration is prepared. Catalog and playback will be enabled when approved partner access and the official API documentation are available."
        , QobuzCredentialsHint = "Secrets and sign-in tokens are not stored in settings.json. Windows protects them for the current user."
        , SearchArtistImage = "Search artist image"
        , UploadArtistImage = "Upload artist image"
        , DeleteArtistImage = "Delete artist image"
        , UploadCover = "Upload cover"
        , ImageFileType = "Image files"
        , ArtistImageSearchTitle = "Search artist image"
        , ArtistImageSearchRunning = "Searching for matching artist images …"
        , ArtistImageSearchNoResults = "No artist images found."
        , ArtistImageSearchQuery = "Search term"
        , ArtistImageSearchFailed = "The artist image search failed."
        , UseSelectedArtistImage = "Use selected image"
        , ArtistImageDownloadFailed = "The selected artist image could not be saved."
        , ArtistProfileSearchTitle = "Reload artist information"
        , ArtistProfileSearchHint = "If necessary, adjust the name Wikipedia or Last.fm uses to find the artist profile. This does not rename the artist in your library."
        , ArtistProfileSearchQuery = "Profile search name"
        , ArtistProfileSearchLoad = "Load information"
        , EditArtistName = "Edit artist name"
        , ArtistName = "Artist name"
        , RenameArtist = "Rename"
        , MergeArtistsTitle = "Merge artists"
        , ArtistNameExistsMessage = "An artist named “{0}” already exists. Should both artists be merged? Choose which record and profile information should be retained."
        , KeepArtistProfile = "Prioritize “{0}” and merge"
        , ArtistRenameFailed = "The artist could not be renamed or merged."
        , Shuffle = "Shuffle"
        , SearchLyrics = "Search lyrics"
        , LyricsSearchTitle = "Search lyrics"
        , LyricsSearchRunning = "Searching for matching lyrics …"
        , LyricsSearchNoResults = "No matching lyrics found."
        , LyricsSearchFailed = "The lyrics search failed."
        , UseSelectedLyrics = "Use selected lyrics"
        , SelectLyricsResult = "Select lyrics on the left to preview them."
        , SynchronizedLyrics = "Synchronized"
        , InternetRadio = "Internet Radio"
        , OwnRadios = "MY RADIOS"
        , SidebarSections = "Sidebar sections"
        , SidebarSectionsHint = "Choose which collapsible sections are shown in the main navigation."
        , PodcastInfo = "Podcast information"
        , ShowPodcastInfo = "Show podcast information"
        , ClosePodcastInfo = "Close podcast information"
        , PodcastPublishedOn = "Published on {0}"
        , PodcastEpisodeDuration = "Duration {0}"
        , PodcastDescriptionUnavailable = "No summary is available for this episode."
        , RadioDirectory = "Discover stations"
        , RadioDirectoryHint = "Search the free Radio Browser directory and permanently add stations to your own radios."
        , RadioSearch = "Search stations"
        , RadioStation = "Station"
        , Country = "Country"
        , PlayRadio = "Play"
        , AddToOwnRadios = "Add to my radios"
        , DeleteRadio = "Delete station"
        , RadioLoading = "Loading radio stations …"
        , RadioNoResults = "No matching radio stations found."
        , RadioEmptyState = "Search by station name, country, or genre, or save discovered stations under “Own radios”."
        , OwnRadiosEmptyHint = "No saved radio stations yet. Search the internet radio directory and add stations here."
        , RadioAdded = "Radio station “{0}” was added."
        , RadioDeleted = "Radio station “{0}” was deleted."
        , RadioSearchFailed = "Radio stations could not be loaded."
        , RadioNowPlaying = "NOW ON AIR"
        , RadioMetadataUnavailable = "The station is not currently providing track information."
        , RadioGenres = "Genres"
        , ClearFilter = "Clear filter"
        , Podcasts = "Podcasts"
        , MyPodcasts = "MY PODCASTS"
        , PodcastDirectory = "Discover podcasts"
        , PodcastDirectoryHint = "Search the Apple Podcasts directory, pin podcasts permanently, and play the latest episode from the RSS feed."
        , PodcastSearch = "Search podcasts"
        , PodcastEmptyState = "Search for a podcast, filter by category or language, and pin favorites under “My podcasts”."
        , Podcast = "Podcast"
        , PodcastAuthor = "Author"
        , PlayLatestEpisode = "Play latest"
        , AddToMyPodcasts = "Add to my podcasts"
        , DeletePodcast = "Delete podcast"
        , PodcastLoading = "Loading podcasts …"
        , PodcastNoResults = "No matching podcasts found."
        , MyPodcastsEmptyHint = "No pinned podcasts yet. Search the podcast directory and add podcasts here."
        , PodcastAdded = "Podcast “{0}” was added."
        , PodcastDeleted = "Podcast “{0}” was deleted."
        , PodcastSearchFailed = "Podcasts could not be loaded."
        , PodcastFeedFailed = "No playable episode was found in the podcast feed."
        , ShowEpisodes = "Show episodes"
        , Published = "Published"
        , Progress = "Progress"
        , PodcastStatus = "Status"
        , PodcastUnplayed = "New"
        , PodcastInProgress = "Started"
        , PodcastPlayed = "Played"
        , PodcastEpisodesLoading = "Loading podcast episodes …"
        , PodcastNoEpisodes = "No playable episodes were found in this feed."
        , PodcastCategories = "Categories"
        , PodcastLanguages = "Languages"
        , PodcastLanguage = "Language"
        , PodcastLanguagesLoading = "Detecting podcast languages from the feeds …"
        , PodcastOverview = "PODCAST OVERVIEW"
        , PodcastEpisodeTotal = "{0:N0} episodes total"
        , PodcastEpisodeUnheard = "{0:N0} not yet played"
        , PodcastEpisodeStarted = "{0:N0} started"
        , PodcastLatestEpisode = "Latest episode: {0}"
        , DailyHistoryTitle = "Listening history – {0}"
        , PlayedAt = "Played at"
        , ListenedDuration = "Listened"
        , MediaType = "Type"
        , Close = "Close"
        , DailyHistoryNoEntries = "There are no playback entries for this day."
        , PlexServers = "PLEX SERVERS"
        , PlexServersSettings = "Plex servers"
        , PlexServersHint = "Configure one or more Plex Media Servers. Access tokens are protected for the current Windows user account."
        , AddPlexServer = "Add Plex server"
        , PlexServerDialogTitle = "Plex server"
        , PlexServerName = "Display name"
        , PlexServerUrl = "Server URL"
        , PlexToken = "X-Plex-Token (optional)"
        , PlexTestConnection = "Test connection"
        , PlexTestingConnection = "Testing connection…"
        , PlexConnectionSuccessful = "Connection successful. Found {0:N0} audio libraries."
        , PlexConnectionFailed = "Connection failed: {0}"
        , PlexServerFieldsRequired = "Name and server URL are required."
        , PlexServerUrlInvalid = "Enter a valid HTTP or HTTPS URL."
        , PlexEditServer = "Edit"
        , PlexRemoveServer = "Remove"
        , PlexNoAudioLibraries = "No audio libraries found."
        , PlexLoading = "Loading Plex content…"
        , OrynivoServers = "ORYNIVO SERVER"
        , VersionLabel = "Version {0}"
        , CheckForUpdates = "Check for updates"
        , Updates = "Updates"
        , CheckForUpdatesOnStartup = "Check for updates when the application starts"
        , WindowBehavior = "Window behavior"
        , StartMaximized = "Start maximized"
        , CheckingForUpdates = "Checking for updates…"
        , UpdateAvailable = "Version {0} is available."
        , UpToDate = "Orynivo is up to date."
        , UpdateUnavailable = "Verified updates are not configured for this build."
        , DownloadAndInstall = "Download and install"
        , DownloadingUpdate = "Downloading and verifying the update…"
        , InstallingUpdate = "Installing the update…"
        , UpdateFailed = "The update could not be checked or installed."
        , UpdateServer = "Update server"
        , UpdatingServer = "Transferring update…"
        , ServerUpdateQueued = "Update started"
        , ServerUpdateUnavailable = "No newer supported server update is available."
        , ServerUpdateFailed = "Server update failed"
        , ServerUpdateRejected = "Server rejected update (HTTP {0})"
        , UpdatingNamedServer = "Updating server '{0}'…"
        , ServerUpdatesFailedContinue = "The following servers could not be updated: {0}. Continue with the desktop update?"
        , SourceColumn = "Source"
        , LocalSource = "Local"
        , LocalSourceShort = "L"
        , OrynivoServersSettings = "Orynivo servers"
        , OrynivoServersHint = "Connect the player to one or more Orynivo Server instances on your local network. The API key is stored encrypted in the current user's credential vault."
        , AddOrynivoServer = "Add server"
        , OrynivoServerDialogTitle = "Orynivo Server"
        , OrynivoServerName = "Display name"
        , OrynivoServerUrl = "Server URL (e.g. http://192.168.1.10:5280)"
        , OrynivoServerApiKey = "API key"
        , OrynivoTestConnection = "Test connection"
        , OrynivoTestingConnection = "Testing connection…"
        , OrynivoConnectionSuccessful = "Connected. Server: {0} v{1}"
        , OrynivoConnectionFailed = "Connection failed. Check URL and API key."
        , OrynivoServerFieldsRequired = "Name, URL and API key are required."
        , OrynivoEditServer = "Edit"
        , OrynivoRemoveServer = "Remove"
        , OrynivoLoading = "Loading server content…"
        , OrynivoServerDirectories = "Server music directories"
        , OrynivoLoadServerDirectories = "Load from server"
        , OrynivoAddServerDirectory = "Add directory"
        , OrynivoLoadingServerDirectories = "Loading server directories…"
        , OrynivoServerDirectoriesLoaded = "Server directories loaded."
        , OrynivoServerDirectoriesLoadFailed = "Server directories could not be loaded."
        , OrynivoSavingServerDirectories = "Saving server directories…"
        , OrynivoServerDirectoriesSaveFailed = "Server directories could not be saved."
        , OrynivoCalculateReplayGainDuringScan = "Calculate missing ReplayGain during server scans (slower)"
        , OrynivoSavingReplayGainSettings = "Saving the ReplayGain scan setting on the server…"
        , OrynivoReplayGainSettingsSaveFailed = "The ReplayGain scan setting could not be saved on the server."
        , OrynivoReplayGainSettingsUnsupported = "Directories loaded. This server does not yet support the ReplayGain scan setting."
        , OrynivoNoServerDirectories = "No server directories configured."
        , OrynivoServerDirectoryBrowserTitle = "Select server directory"
        , OrynivoServerDirectoryRoots = "Roots"
        , OrynivoServerDirectoryUp = "Up"
        , OrynivoSelectServerDirectory = "Select"
        , OrynivoServerDirectoryLoading = "Loading directory…"
        , OrynivoServerDirectoryLoadFailed = "Directory could not be loaded."
        , OrynivoServerDirectoryEmpty = "No subdirectories available."
        , OrynivoServerScan = "Server scan"
        , OrynivoStartServerScan = "Scan library"
        , OrynivoCalculateServerReplayGain = "Calculate ReplayGain"
        , OrynivoServerScanStarting = "Starting server scan…"
        , OrynivoServerScanStartFailed = "Server scan could not be started."
        , OrynivoServerScanIdle = "No server scan is running."
        , OrynivoServerScanDiscovering = "Discovering files: {0}"
        , OrynivoServerScanProgress = "{0}/{1} · {2}"
        , OrynivoServerScanCompleted = "Scan complete: {0} files, {1} added, {2} updated, {3} removed, {4} failed."
        , OrynivoServerScanFailed = "Server scan failed: {0}"
        , OrynivoServerBackup = "Back up server library"
        , OrynivoDownloadBackup = "Download backup"
        , OrynivoRestoreBackup = "Restore backup"
        , OrynivoBackupDownloading = "Downloading server backup…"
        , OrynivoBackupDownloaded = "Server backup saved: {0}"
        , OrynivoBackupRestoring = "Validating and restoring server backup…"
        , OrynivoBackupRestored = "Server backup restored successfully."
        , OrynivoBackupFailed = "Server backup failed: {0}"
        , OrynivoRestoreBackupConfirm = "Import replaces the server database, playlists, history, artwork, artist images, and directory list. Audio files remain unchanged. Continue?"
        , LoadMore = "Load more"
        , FfmpegDownloading = "Downloading FFmpeg …"
        , FfmpegDownloadFailed = "FFmpeg could not be downloaded. Please install it manually: ffmpeg.org"
        , SmartPlaylistDialogTitle = "Edit smart playlist"
        , SmartPlaylistName = "Name"
        , SmartPlaylistBasicFilters = "Basic filters"
        , SmartPlaylistGenres = "Genres (comma-separated)"
        , SmartPlaylistFormats = "Formats (for example FLAC, MP3; comma-separated)"
        , SmartPlaylistBitrates = "Bitrates in kbps (comma-separated)"
        , SmartPlaylistSources = "Sources (local or server:<id>; comma-separated)"
        , SmartPlaylistMetadata = "Metadata"
        , SmartPlaylistMinimumYear = "Year from"
        , SmartPlaylistMaximumYear = "Year to"
        , SmartPlaylistSearchText = "Search text contains"
        , SmartPlaylistArtistContains = "Artist contains"
        , SmartPlaylistAlbumContains = "Album contains"
        , SmartPlaylistMinimumDuration = "Minimum duration in minutes"
        , SmartPlaylistMaximumDuration = "Maximum duration in minutes"
        , SmartPlaylistHistory = "Library and playback history"
        , SmartPlaylistAddedWithinDays = "Added within the last X days"
        , SmartPlaylistPlayedWithinDays = "Played within the last X days"
        , SmartPlaylistNeverPlayed = "Never played"
        , SmartPlaylistMinimumPlayCount = "Minimum play count"
        , SmartPlaylistMaximumPlayCount = "Maximum play count"
        , SmartPlaylistResult = "Result"
        , SmartPlaylistSortOrder = "Order"
        , SmartPlaylistSortTitle = "Title A–Z"
        , SmartPlaylistSortRandom = "Random"
        , SmartPlaylistSortLastPlayed = "Most recently played first"
        , SmartPlaylistSortLeastRecentlyPlayed = "Least recently played first"
        , SmartPlaylistResultLimit = "Maximum number of tracks (blank = unlimited)"
        , CreateSmartPlaylist = "Create smart playlist"
        , InvalidSmartPlaylistCriteria = "Enter valid numbers and consistent minimum/maximum values. “Never played” cannot be combined with recent playback or a positive minimum play count."
        , EditSmartPlaylist = "Edit smart playlist"
        , LibraryEmptyHint = "No media source is configured yet. Open Settings > Library, add local music folders, or connect an Orynivo Server."
        , SmartPlaylistUpdated = "Smart playlist '{0}' updated."
        , ImportM3u8Playlist = "Import M3U8 playlist"
        , ExportM3u8Playlist = "Export as M3U8"
        , SaveAlbumAsPlaylist = "Save as playlist"
        , AlbumPath = "Album path"
        , TrackInfo = "Track information"
        , ShowTrackInfo = "Show track information"
        , PhysicalPath = "Physical file path"
        , UpNext = "Up next"
        , GenreExplorer = "Genre cloud"
        , GenreCloudHint = "Explore genres from your local library and every connected Orynivo Server. Select a genre to drill down."
        , AllGenres = "All genres"
        , GenreRecommendations = "Recommended tracks"
        , GenreCloudEmpty = "No genres found yet. Check whether your tracks contain genre tags."
        , MoreGenres = "More genres"
        , PlayNext = "Play next"
        , AppendToQueue = "Append to queue"
        , RemoveFromQueue = "Remove from queue"
        , MoveUp = "Move up"
        , MoveDown = "Move down"
        , SaveQueueAsPlaylist = "Save queue as playlist"
        , TracksQueuedNext = "{0:N0} tracks will play next."
        , TracksAppendedToQueue = "{0:N0} tracks appended to the queue."
        , M3u8ImportCompleted = "Playlist '{0}' imported: {1} entries · {2} local files missing · {3} HTTP entries · {4} skipped."
        , M3u8ImportNoEntries = "The M3U8 file contains no importable entries."
        , M3u8ImportFailed = "M3U8 import failed: {0}"
        , M3u8ExportCompleted = "Playlist '{0}' exported as M3U8: {1} entries · {2} skipped."
        , M3u8ExportFailed = "M3U8 export failed: {0}"
        , Integration      = "INTEGRATION"
        , McpServer        = "MCP Server"
        , McpServerHint    = "Opens a local HTTP/SSE server that lets AI assistants (e.g. Claude Desktop) control the player and search the library."
        , McpServerEnabled = "Enable MCP server"
        , McpServerPort    = "Port"
        , McpNetworkAccess = "Allow access from the local network"
        , McpNetworkAccessHint = "Binds MCP to all network interfaces. Network requests require the bearer token. Without HTTPS or a VPN, the token can be observed on the network."
        , McpAccessToken = "Access token"
        , McpAccessTokenWatermark = "Generated automatically when enabled"
        , McpGenerateToken = "Generate new"
        , MobileRemote = "Mobile web remote"
        , MobileRemoteHint = "Provides a remote control on the local network. Use its dedicated access token and protect access outside your home network with HTTPS or a VPN."
        , MobileRemoteEnabled = "Enable mobile remote on the network"
        , MobileRemotePort = "Port"
        , MobileRemoteToken = "Remote access token"
        , MobileRemoteNetworkAddress = "Home network IP address"
        , MobileRemoteQrHint = "Save settings first. Scan the QR code on the same Wi-Fi to sign in directly. It contains the access token: do not share it! Opening the URL manually asks for the token. If multiple addresses appear, choose one reachable from your phone."
        , McpToolsHeader   = "Tools"
        , McpToolsHint     = "Enable or disable individual tools."
        , WebBrowsing        = "Web browsing"
        , WebBrowsingHint    = "Gives the AI a controlled set of web tools: SearXNG search and safe page fetching. Private and local addresses are blocked (SSRF protection)."
        , WebBrowsingEnabled = "Enable web tools"
        , SearxngUrl         = "SearXNG URL"
        , WebBlockPrivate    = "Block private/local addresses (SSRF protection)"
        , WebMaxResults      = "Maximum search results"
        , WebTimeoutSeconds  = "Timeout (seconds)"
        , WebMaxResponseKb   = "Maximum response size (KB)"
        , AiChat                = "AI Chat"
        , AiChatSettings        = "AI Chat"
        , AiChatHint            = "Connects to a local or cloud-based AI model via an OpenAI-compatible API (e.g. LM Studio, Ollama, OpenAI). The model has access to all 32 Orynivo tools to search the library, manage playlists, and control playback."
        , AiChatEnabled         = "Enable AI Chat"
        , AiChatEndpointUrl     = "Endpoint URL"
        , AiChatApiKey          = "API key (optional)"
        , AiChatLocalNote       = "LM Studio and Ollama do not require an API key."
        , AiChatModel           = "Model"
        , AiChatLoadModels      = "Load models"
        , AiChatAvailableModels = "Select an available model"
        , AiChatTestConnection  = "Test connection"
        , AiChatConnectionTesting = "Testing connection …"
        , AiChatModelsLoaded    = "Loaded {0} models."
        , AiChatConnectionSucceeded = "Connection successful; {0} models available."
        , AiChatNoModels        = "Connection successful, but the endpoint reported no models."
        , AiChatConnectionFailed = "Connection failed. Check the endpoint URL and API key."
        , AiChatMaxTokens       = "Max tokens"
        , AiChatInputPlaceholder = "Ask a question …"
        , AiChatSend            = "Send"
        , AiChatClear           = "Clear"
        , AiChatCopy            = "Copy"
        , AiChatNotEnabled      = "AI Chat is not enabled. Enable it in Settings › AI Chat."
        , AiChatEmptyResponse   = "The model returned an empty answer."
        , AiChatToolResultFallback = "The model did not return a final answer after the tool call. Tool result:"
    };

    private static readonly LocalizedStrings French = new(
        "BIBLIOTHÈQUE", "Artistes", "Albums", "Titres", "Arborescence", "Recherche", "Playlists", "À propos", "Paramètres",
        "Filtre", "Favoris", "Types audio", "Débit",
        "Aucun appareil sélectionné.", "APPARENCE", "Thème", "Langue", "LECTURE", "Périphérique de sortie",
        "BIBLIOTHÈQUE", "Dossiers", "+ Ajouter un dossier", "Maintenance de la base",
        "Optimiser la base", "Réparer les pochettes", "Télécharger les pochettes manquantes",
        "Le téléchargement automatique ne trouve des pochettes que si un identifiant MusicBrainz est présent. Pour une recherche plus libre, utilisez le bouton directement dans la vue des albums.",
        "Pochette introuvable", "Rechercher une pochette", "Rechercher une pochette", "Recherche de pochettes correspondantes …",
        "Aucune pochette trouvée.", "Recherche d’album", "Artiste (facultatif)", "Relancer la recherche", "Utiliser la pochette sélectionnée",
        "Supprimer la pochette", "Réattribuer la pochette", "Auteur", "Licences", "Enregistrer", "Annuler", "Tableau", "Pochettes",
        "(Inconnu)", "Artiste de l’album", "Année", "Titre", "Artiste", "Album", "Genre", "Durée", "Format",
        "Le terme de recherche {0} est introuvable dans les titres.",
        "Le terme de recherche {0} est introuvable dans les albums.",
        "Le terme de recherche {0} est introuvable dans les artistes.",
        "{0:N0} entrées", "{0:N0} titres",
        "Veuillez d’abord double-cliquer sur un titre.", "Lecture arrêtée.", "Lecture terminée.",
        "Veuillez d’abord sélectionner un périphérique ASIO dans les paramètres.", "Veuillez d’abord sélectionner un périphérique WASAPI dans les paramètres.",
        "{0} n’est pas encore implémenté.", "Paramètres enregistrés.", "Impossible de lire les informations du périphérique : {0}",
        "Aucun périphérique de sortie WASAPI actif trouvé.", "Aucun pilote ASIO trouvé.", "Sélectionnez un périphérique puis enregistrez.",
        "Analyse en cours…", "Dossier introuvable.", "Analyse annulée.", "Optimisation de la base …",
        "Optimisation terminée.", "Échec de l’optimisation : {0}", "Réparation des pochettes …",
        "{0:N0} pochettes réparées.", "Échec de la réparation des pochettes : {0}",
        "Téléchargement des pochettes manquantes …", "{0:N0} pochettes manquantes téléchargées.",
        "Échec du téléchargement des pochettes : {0}",
        "Ajouter à la playlist", "Nouvelle playlist …", "Nouvelle playlist", "Nom de la playlist",
        "Créer",
        "Titre ajouté à la playlist « {0} ».", "{0} titres ajoutés à la playlist « {1} ».",
        "Supprimer la playlist", "Retirer de la playlist",
        "Playlist « {0} » supprimée.", "Titre retiré de la playlist.",
        "Enregistrer les filtres comme playlist intelligente", "Playlist intelligente « {0} » enregistrée.",
        "Veuillez d'abord sélectionner un filtre.",
        "Sauvegarde de la bibliothèque",
        "Exporte la base, les playlists, l’historique, les pochettes et la liste des dossiers au format ZIP. Les fichiers audio ne sont pas inclus.",
        "Exporter la bibliothèque", "Importer la bibliothèque",
        "Exportation de la bibliothèque …", "Bibliothèque exportée vers « {0} ».",
        "Échec de l’exportation de la bibliothèque : {0}",
        "L’importation remplace la bibliothèque, les playlists, l’historique et toutes les pochettes actuelles. Continuer ?",
        "Importation de la bibliothèque et reconstruction de l’index de recherche …",
        "Bibliothèque importée. Le lecteur va maintenant se fermer et pourra ensuite être redémarré.",
        "Échec de l’importation de la bibliothèque : {0}",
        "Veuillez d’abord terminer les analyses ou opérations de maintenance en cours.",
        "Bibliothèque Orynivo (*.zip)|*.zip",
        "Exportation de la bibliothèque : {0}% – {1}",
        "Importation de la bibliothèque : {0}% – {1}",
        "Paroles", "Afficher les paroles", "Actualiser les paroles", "Fermer les paroles",
        "Chargement des paroles …", "Téléchargement des paroles depuis LRCLIB …",
        "Aucune métadonnée n’est disponible pour ce titre.", "Aucune parole trouvée.",
        "Impossible de télécharger les paroles.",
        "INFORMATIONS SUR L’ARTISTE", "Afficher les informations sur l’artiste",
        "Actualiser les informations sur l’artiste", "Fermer les informations sur l’artiste",
        "Chargement des informations sur l’artiste …",
        "Téléchargement des informations sur l’artiste …",
        "Aucune information trouvée sur l’artiste.",
        "Impossible de télécharger les informations sur l’artiste.",
        "Aucune image téléchargée", "Fichier image introuvable", "Échec du chargement de l’image",
        "Source : Wikipédia", "Source : Last.fm",
        "Source des informations artiste", "Clé API Last.fm",
        "Créez une clé API gratuite sur : last.fm/api/account/create",
        "Clé API Fanart.tv",
        "Préfère les images d’artistes sélectionnées. La clé est chiffrée dans le coffre d’identifiants de l’utilisateur actuel ; vous pouvez aussi définir FANART_TV_API_KEY. Utilisez « Actualiser les informations artiste » pour les artistes existants. Créez une clé sur fanart.tv/get-an-api-key/.",
        "Télécharger les images d’artistes manquantes",
        "Parcourt séquentiellement la bibliothèque locale et tous les serveurs Orynivo configurés. Pour chaque artiste, Fanart.tv est essayé en premier (avec une clé API), puis Wikimedia. Les résultats Fanart.tv peuvent être acceptés automatiquement ; ceux de Wikimedia doivent toujours être confirmés.",
        "Recherche de l’image d’artiste {0}/{1} : {2} · {3}",
        "{0:N0} images d’artistes acceptées, {1:N0} refusées ; {2:N0} requêtes ont échoué.",
        "Échec du téléchargement des images d’artistes : {0}",
        "Téléchargement des images d’artistes annulé.",
        "Suggestion {0}/{1} : {2} · {3}",
        "Chargement des artistes locaux et des serveurs …",
        "Estimation du temps restant …",
        "Temps restant estimé : {0}",
        "Suggestion d’image d’artiste",
        "Source : {0}",
        "Cette image ne sera enregistrée qu’après votre confirmation.",
        "Accepter",
        "Refuser",
        "Afficher tous les titres de l’album",
        "Orynivo a cessé de fonctionner",
        "Une erreur inattendue s’est produite. Un rapport a été enregistré ici :\n\n{0}\n\nOrynivo va maintenant se fermer.",
        "Une erreur inattendue s’est produite. Le rapport n’a pas pu être enregistré. Orynivo va maintenant se fermer.")
    {
        AutoAcceptFanartTvImages = "Accepter automatiquement les résultats Fanart.tv",
        OutputType = "Type de sortie", AsioOutputDevice = "Périphérique de sortie ASIO", WasapiOutputDevice = "Périphérique de sortie WASAPI",
        AirPlay = "AirPlay 2", AirPlayOutputDevice = "Périphérique de sortie AirPlay 2",
        NoAirPlayDevices = "Aucun appareil AirPlay 2 n’a été trouvé sur le réseau local.",
        AirPlaySenderMissing = "Le pont AirPlay 2 natif est absent ; l’utilitaire compatible « raop_play » peut servir de solution de repli.",
        SelectAirPlayDevice = "Sélectionnez d’abord un périphérique de sortie AirPlay.",
        CwAsioOutputDevice = "Périphérique de sortie cwASIO", SteinbergAsio = "Steinberg ASIO", CwAsio = "cwASIO",
        OpenAl = "OpenAL", OpenAlOutputDevice = "Périphérique de sortie OpenAL",
        DirectAlsa = "ALSA (direct, exclusif)", AlsaOutputDevice = "Périphérique de sortie ALSA direct",
        LinuxDefaultAudioDevice = "Périphérique système par défaut (OpenAL)",
        OpenAlInitializationFailed = "OpenAL n’a pas pu initialiser la sortie audio du système.",
        AlsaExactOpenFailed = "Le périphérique ALSA « {0} » ne peut pas être ouvert à {1} Hz sans rééchantillonnage : {2}",
        AlsaDeviceBusy = "Le périphérique ALSA direct « {0} » est déjà utilisé par PipeWire ou une autre application. Rediriger la sortie système ne libère pas le périphérique. Désactivez le profil du périphérique audio dans le système ou sélectionnez la sortie OpenAL.",
        AlsaPrepareFailed = "ALSA n’a pas pu préparer le périphérique de sortie après le déplacement.",
        DeviceInfo = "Informations sur le périphérique",
        OutputProfile = "Sortie",
        UserProfiles = "Profils utilisateur", UserProfileActive = "Profil actif", UserProfileCreate = "Créer un profil", UserProfileRename = "Renommer le profil", UserProfileDelete = "Supprimer le profil", UserProfileName = "Nom du profil", UserProfileMigrateFavorites = "Copier les données personnelles existantes (favoris, évaluations et historique) dans le nouveau profil ?", UserProfileDeleteConfirm = "Supprimer le profil « {0} » ?",
        LocalMedia = "Local",
        OutputProfileCreate = "Créer une sortie",
        OutputProfileConfigure = "Configurer la sortie",
        OutputProfileDelete = "Supprimer la sortie",
        OutputProfileCreateTitle = "Créer une nouvelle sortie",
        OutputProfileConfigureTitle = "Configurer la sortie",
        OutputProfileName = "Nom de la sortie",
        OutputProfileNameExists = "Une sortie portant ce nom existe déjà.",
        OutputProfileDeleteTitle = "Supprimer la sortie",
        OutputProfileDeleteConfirm = "Voulez-vous vraiment supprimer la sortie « {0} » ?", DatabaseOptimizeHint = "Les pages libérées sont supprimées afin de réduire physiquement le fichier.",
        GenreCloudCache = "Arrière-plans du nuage de genres",
        GenreCloudCacheHint = "Efface les mosaïques d’artistes en cache. Elles seront recréées à la prochaine ouverture d’un niveau de genre.",
        GenreCloudCacheCleared = "Le cache des arrière-plans du nuage de genres a été vidé.",
        GenreCloudBackground = "Nuage de genres",
        GenreCloudBackgroundHint = "Choisissez les images d’arrière-plan ou désactivez-les complètement pour réduire la charge du système.",
        GenreCloudBackgroundNone = "Aucune image d’arrière-plan",
        GenreCloudBackgroundAlbums = "Pochettes d’album",
        GenreCloudBackgroundArtists = "Images d’artistes",
        GenreCloudVisibility = "Visibilité des images",
        ClearGenreCloudCache = "Vider le cache d’arrière-plan",
        AppearanceNavItem = "Apparence", ArtistInfoNavItem = "Informations sur l’artiste",
        AsioBridgeMissing = "Cette version ne comprend pas la prise en charge ASIO. Utilisez WASAPI.",
        KernelStreamingUnavailable = "Kernel Streaming peut être sélectionné, mais ce mode de lecture n’est pas encore implémenté.",
        AddMusicDirectory = "Ajouter un dossier musical", TrackCountTooltip = "Nombre de titres dans la base",
        Scan = "Analyser",
        RefreshAllMetadata = "Relire les métadonnées",
        RefreshAllMetadataHint = "Relit les métadonnées de chaque fichier, même si son horodatage est inchangé. Cette opération peut être nettement plus longue.",
        RemoveDirectory = "Supprimer le dossier",
        ScanCompleted = "Terminé : {0} fichiers · {1} nouveaux · {2} actualisés · {3} supprimés{4}", ScanFailed = "Erreur : {0}",
        StartupPreparingLibrary = "Préparation de la bibliothèque …",
        StartupCheckingSearchIndex = "Vérification de l’index de recherche …",
        SearchIndexRebuilding = "Reconstruction de l’index de recherche en arrière-plan ({0}/{1}) …",
        SearchIndexReady = "L’index de recherche est à jour.",
        SearchIndexFailed = "L’index de recherche n’a pas pu être mis à jour : {0}",
        Back = "Retour", MarkAsFavorite = "Ajouter aux favoris",
        OpenAlbum = "Ouvrir l’album",
        OpenArtist = "Ouvrir l’artiste",
        ToggleFavorite = "Basculer le favori",
        PlaybackThrough = "Lecture via {0}",
        PlaybackThroughWithDsdConversion = "Lecture via {0} · Le DSD est converti en PCM ({1:N0} Hz)",
        NativeDsdOutput = "DSD natif", DsdToPcmOutput = "DSD → PCM",
        DopOutput = "DSD via DoP",
        DopRequiresDirectAlsa = "Sous Linux, le DSD via DoP nécessite un périphérique de sortie ALSA direct sans rééchantillonnage.",
        ReplayGain = "Ajustement du volume ReplayGain",
        ReplayGainHint = "S’applique à la lecture PCM. Le mode piste privilégie le gain de piste, le mode album le gain d’album. La sortie DSD native reste bit-perfect.",
        ReplayGainOff = "Désactivé", ReplayGainTrack = "Piste", ReplayGainAlbum = "Album",
        CalculateReplayGainDuringScan = "Calculer automatiquement les ReplayGain manquants pendant l’analyse de la bibliothèque locale (plus lent)",
        CalculateReplayGain = "Calculer les ReplayGain manquants",
        ReplayGainCalculating = "Calcul des ReplayGain …",
        ReplayGainCalculated = "ReplayGain calculé : {0} pistes mises à jour.",
        ReplayGainCalculationFailed = "Échec du calcul ReplayGain : {0}",
        NonGaplessCrossfade = "Fondu pour files non gapless (secondes)",
        NonGaplessCrossfadeHint = "0 désactive la transition. S’applique uniquement aux changements de file qui ne passent pas déjà par le moteur PCM gapless.",
        ReplayGainBadge = "RG",
        DsdPlayback = "Lecture DSD",
        AlwaysConvertDsdToPcm = "Toujours convertir les fichiers DSD en PCM",
        AlwaysConvertDsdToPcmHint = "Utilise également le chemin PCM avec ASIO/cwASIO afin d’appliquer le volume, ReplayGain et l’égaliseur. Lorsque cette option est désactivée, la sortie DSD native reste bit-perfect.",
        DsdOverPcm = "Sortir le DSD via DoP",
        DsdOverPcmHint = "Encapsule le DSD bit-perfect dans des trames PCM (DSD over PCM). Nécessite un DAC compatible DoP et une sortie exacte sans rééchantillonnage ; le volume, ReplayGain et l’égaliseur restent sans effet.",
        PcmOutputBoost = "Augmenter la sortie PCM de +6 dB",
        PcmOutputBoostHint = "Augmente tous les chemins de lecture PCM pour les rapprocher du niveau perçu de la sortie DSD native. La sortie DSD native reste bit-perfect et inchangée.",
        OutputDevicesLoading = "Chargement des périphériques de sortie …",
        Equalizer = "Égaliseur paramétrique",
        ReleaseOutputDevice = "Libérer le périphérique de sortie",
        ReacquireOutputDevice = "Réacquérir le périphérique et reprendre la lecture",
        OutputDeviceReleased = "Le périphérique de sortie est libéré",
        EqualizerHint = "Importe les profils Equalizer APO et AutoEQ pour la lecture PCM et DSD vers PCM. La sortie DSD native reste bit-perfect.",
        EqualizerEnabled = "Activer l’égaliseur",
        EqualizerImport = "Importer un profil APO/AutoEQ",
        EqualizerImporting = "Importation du profil d’égaliseur …",
        EqualizerImportTitle = "Importer un profil Equalizer APO ou AutoEQ",
        EqualizerNoProfile = "Aucun profil importé.",
        EqualizerProfileSummary = "{0} · préampli {1:+0.##;-0.##;0} dB · {2} filtres",
        EqualizerImportFailed = "Impossible d’importer le profil.",
        EqualizerProfileFileType = "Profil Equalizer APO / AutoEQ",
        EqualizerPreamp = "Préampli (dB)",
        EqualizerFilterType = "Type de filtre",
        EqualizerFrequency = "Fréquence (Hz)",
        EqualizerGain = "Gain (dB)",
        EqualizerQ = "Facteur Q",
        EqualizerAddFilter = "Ajouter un filtre",
        EqualizerRemoveFilter = "Supprimer le filtre",
        EqualizerPeak = "Crête",
        EqualizerLowShelf = "Plateau grave",
        EqualizerHighShelf = "Plateau aigu",
        EqualizerLowPass = "Passe-bas",
        EqualizerHighPass = "Passe-haut",
        EqualizerCreate = "Créer un égaliseur",
        EqualizerCreateTitle = "Créer un nouvel égaliseur",
        EqualizerName = "Nom de l’égaliseur",
        EqualizerNameExists = "Un égaliseur portant ce nom existe déjà.",
        EqualizerDelete = "Supprimer l’égaliseur",
        EqualizerDeleteTitle = "Supprimer l’égaliseur",
        EqualizerDeleteConfirm = "Voulez-vous vraiment supprimer l’égaliseur « {0} » ?",
        SelectColumns = "Sélectionner les colonnes",
        FileName = "Nom du fichier", FileSize = "Taille du fichier", AddedAt = "Ajouté",
        SampleRate = "Fréquence d’échantillonnage", BitDepth = "Profondeur de bits", Channels = "Canaux",
        TrackNumber = "Numéro de piste", DiscNumber = "Numéro de disque", Composer = "Compositeur",
        Bpm = "BPM", ReplayGainTrackColumn = "ReplayGain piste",
        ReplayGainAlbumColumn = "ReplayGain album", Codec = "Codec", Tags = "Tags",
        PersonalRating = "Mon évaluation", MusicBrainzRating = "Évaluation MusicBrainz", MusicBrainzLoadRating = "Charger l’évaluation", MusicBrainzLoadingRating = "Chargement …", MusicBrainzRetryRating = "Réessayer", MusicBrainzNoRating = "Non évalué",
        RatingSetHint = "Définir l’évaluation personnelle", RatingUpdateFailed = "L’évaluation n’a pas pu être enregistrée.",
        Homepage = "Page d’accueil", FeedUrl = "Adresse du flux",
        SearchResultSummary = "{0:N0} titres · {1:N0} albums · {2:N0} artistes",
        RecentAlbums = "Albums ajoutés récemment",
        AlbumRecommendations = "Recommandations d’albums",
        RecommendationMoodAll = "Toutes les ambiances",
        RecommendationMoodRelaxed = "Détendue",
        RecommendationMoodEnergetic = "Énergique",
        RecommendationMoodHappy = "Joyeuse",
        RecommendationMoodMelancholic = "Mélancolique",
        RecommendationNoMatches = "Pas encore assez d’historique d’écoute correspondant pour proposer des recommandations.",
        PlayMoreLikeThis = "Écouter des titres similaires", PlayMoodMix = "Mix d’ambiance", SimilarTracksLoading = "Chargement des titres similaires …", SimilarTracksUnavailable = "Les données de similarité ne sont pas disponibles pour ce titre.", SimilarTracksNoMatches = "Aucun titre similaire trouvé.", SimilarTracksQueued = "{0:N0} titres similaires sont prêts à être lus.",
        RecommendationListView = "Liste",
        RecommendationStageView = "Scène",
        MetadataProblems = "Vérifier les métadonnées",
        MetadataNoFindings = "Aucun résultat pour les filtres choisis pour le moment.",
        MetadataWorkflow = "1. Lire les résultats   →   2. Choisir un dossier   →   3. Comparer et confirmer",
        MetadataInspectFiles = "Vérifier aussi les fichiers (lecture et empreintes des doublons ; plus lent)",
        MetadataQuickHint = "À l’ouverture, seules les métadonnées enregistrées sont vérifiées. Activez l’option puis actualisez pour vérifier les fichiers. Les anciens serveurs nécessitent une mise à jour pour l’analyse rapide.",
        MetadataSelectHint = "Choisissez un dossier. L’analyse ne modifie ni ne supprime aucun fichier.",
        MetadataActionGuide = "Identifier le dossier comme album recherche titres, artistes et numéros sur MusicBrainz. ReplayGain : Paramètres → Lecture. Images : vues albums/artistes. Vérifiez manuellement fichiers manquants et doublons. Seules les corrections confirmées sont enregistrées dans la bibliothèque, sans modifier les fichiers audio.",
        MetadataRemoteReadOnly = "Cette entrée serveur est un rapport en lecture seule. La correction MusicBrainz concerne uniquement les dossiers locaux. Calculez ReplayGain sous Orynivo Server ; ajoutez les images dans les vues albums/artistes.",
        MetadataReviewGuide = "Vos pistes actuelles figurent en haut. Choisissez une édition puis comparez chaque ligne. Les termes de recherche sont modifiables. Seule l’application de la correction enregistre les changements.",
        MetadataPhaseDatabase = "Chargement des métadonnées…",
        MetadataPhaseFolders = "Vérification des dossiers et métadonnées…",
        MetadataPhaseHashes = "Calcul des empreintes des doublons possibles…",
        MetadataPhaseServers = "En attente des rapports serveur. Aucune estimation distante ; les résultats terminés sont disponibles.",
        MetadataPhaseReleases = "Chargement des éditions et pistes MusicBrainz…",
        MetadataPhaseSaving = "Enregistrement de la correction et mise à jour de l’index…",
        MetadataRemaining = "Temps restant estimé pour cette étape : {0}",
        MetadataRemainingUnknown = "Temps restant pas encore estimable",
        MetadataElapsed = "Écoulé : {0}",
        MetadataProblemsHint = "Orynivo vérifie les dossiers physiques indépendamment des albums éventuellement fragmentés. Double-cliquez sur une entrée pour rechercher les éditions MusicBrainz correspondantes.",
        IdentifyFolderAsAlbum = "Identifier le dossier comme album",
        MetadataFolder = "Dossier",
        MetadataIssues = "Problèmes détectés",
        MetadataTrackCount = "Pistes",
        MetadataReviewTitle = "Vérifier les métadonnées de l’album",
        MetadataSearching = "Recherche MusicBrainz selon le nombre et la durée des pistes…",
        MetadataNoMatch = "Aucune édition suffisamment correspondante n’a été trouvée.",
        MetadataSearchFailed = "MusicBrainz est momentanément indisponible. Veuillez relancer la recherche.",
        MetadataFoundReleases = "Éditions correspondantes",
        MetadataApplyCorrection = "Appliquer la correction",
        MetadataCorrectionPreview = "Aperçu de la correction",
        MetadataCurrentValues = "Actuel : titre — artiste",
        MetadataProposedValues = "Proposé : titre — artiste",
        MetadataRefreshAnalysis = "Actualiser l’analyse",
        MetadataAlbumQuery = "Terme de recherche de l’album",
        MetadataArtistQuery = "Terme de recherche de l’artiste",
        MetadataRepairSuccess = "Les métadonnées ont été corrigées dans la bibliothèque Orynivo.",
        MetadataIssueAlbums = "titres d’album incohérents",
        MetadataIssueArtists = "artistes d’album incohérents",
        MetadataIssueMissingTitles = "titres de piste manquants",
        MetadataIssueMissingNumbers = "numéros de piste manquants",
        MetadataIssueDuplicateNumbers = "numéros de piste en double",
        MetadataIssueMissingReplayGain = "{0} sans ReplayGain",
        MetadataIssueMissingMusicBrainzIds = "{0} sans identifiant MusicBrainz",
        MetadataSeverity = "Priorité",
        MetadataSeverityAll = "Toutes les priorités",
        MetadataIssueAll = "Tous les types de problème",
        MetadataIssueReplayGain = "ReplayGain manquant",
        MetadataIssueMusicBrainzIds = "Identifiant MusicBrainz manquant",
        MetadataIssueIncompleteAlbum = "Album incomplet ({0} pistes manquantes)",
        MetadataIssueAlbumArtwork = "Pochette d’album manquante",
        MetadataIssueArtistImage = "Image d’artiste manquante",
        MetadataIssueMissingFiles = "{0} fichiers source manquants",
        MetadataIssueUnreadableFiles = "{0} fichiers source illisibles",
        MetadataIssueLikelyDuplicates = "{0} fichiers probablement en double",
        MetadataIssueExactDuplicates = "{0} fichiers en double identiques octet par octet",
        MetadataIssueAlternateRecordings = "{0} enregistrements dans un autre fichier ou une autre édition",
        MetadataIssueArtistNameVariants = "{0} variantes d’écriture du nom d’artiste",
        MetadataSeverityInformation = "Information",
        MetadataSeverityWarning = "Avertissement",
        MetadataSeverityError = "Erreur",
        MetadataDoctorSummary = "{0} erreurs · {1} avertissements · {2} informations",
        MetadataAnalysisFailed = "L’analyse a échoué. Les détails ont été enregistrés dans le journal d’erreurs.",
        MetadataDoctorServersUnavailable = "{0} serveur(s) indisponible(s) ou sans Library Doctor",
        MetadataAnalysisCancelled = "L’analyse a été annulée.",
        Calendar = "Calendrier – {0}", TopGenres = "Genres les plus écoutés",
        TopAlbums = "Albums les plus écoutés",
        TopArtists = "Artistes les plus écoutés",
        ListeningStats = "Statistiques d’écoute",
        PeriodAllTime = "Tout",
        PeriodThisYear = "Cette année",
        PeriodThisMonth = "Ce mois-ci",
        PeriodLast30Days = "30 derniers jours",
        PeriodLast7Days = "7 derniers jours",
        HistorySourceRemote = "Distant",
        HistorySourcePlex = "Plex",
        LibraryUpdating = "Mise à jour de la bibliothèque…",
        LibraryUpdatingWithCount = "Mise à jour de la bibliothèque… {0} / {1} fichiers",
        RefreshView = "Actualiser",
        LibraryDataAvailable = "Nouvelles données de bibliothèque disponibles",
        ServerUnreachable = "Injoignable",
        ServerLastConnected = "Dernière connexion : {0}",
        ServerNeverConnected = "Jamais connecté",
        ServerMissingFeatures = "Le serveur ne prend pas en charge : {0}",
        CapabilityTrackFacets = "Facettes de pistes",
        CapabilityRecentAlbums = "Albums récents",
        CapabilityWaveforms = "Formes d’onde",
        RemoteCache = "Cache distant",
        RemoteCacheSize = "Taille du cache : {0}",
        ClearRemoteCacheAll = "Vider tout le cache",
        ClearCache = "Vider le cache",
        RemoteScanning = "Mise à jour de {0}…",
        RemoteScanningWithCount = "Mise à jour de {0}… {1} / {2} fichiers",
        SmartPlaylistPreviewCount = "{0} pistes correspondent",
        SmartPlaylistPreviewComputing = "Calcul…",
        SmartPlaylistPreviewInvalid = "Critères non valides",
        RestoreQueue = "Dernière file",
        RestoreQueueTooltip = "Restaurer la dernière file d’attente lue",
        NoPreviousQueue = "Aucune file d’attente précédente.",
        ClearQueue = "Vider la file",
        QueueCleared = "File d’attente vidée.",
        NoData = "Aucune donnée disponible.",
        RecentlyPlayed = "Écoutés récemment",
        GreetingMorning = "Bonjour", GreetingAfternoon = "Bon après-midi", GreetingEvening = "Bonsoir",
        DashboardTagline = "Votre centre musical personnel",
        DashboardWelcomeBack = "BON RETOUR",
        DashboardHeroHint = "Prêt pour de la bonne musique ? Découvrez de nouveaux sons ou retrouvez vos favoris.",
        DashboardRandomPlayback = "Lecture aléatoire",
        InfiniteMixStart = "Démarrer le mix infini",
        GenreCloudInfiniteMix = "Lancer le mix infini depuis le nuage",
        InfiniteMixStop = "Arrêter le mix infini",
        InfiniteMixActive = "Mix infini actif · complété automatiquement",
        InfiniteMixCalculating = "Votre mix infini est en cours de préparation …",
        InfiniteMixSettingsTitle = "Ajuster le mix infini",
        InfiniteMixSettingsHint = "Choisissez comment Orynivo doit composer votre prochain mix alimenté en continu.",
        InfiniteMixMood = "Ambiance", InfiniteMixMoodCalm = "Calme", InfiniteMixMoodBalanced = "Équilibrée", InfiniteMixMoodEnergetic = "Énergique",
        InfiniteMixDiscovery = "Niveau de découverte", InfiniteMixFamiliar = "Familier", InfiniteMixAdventurous = "Aventureux",
        InfiniteMixPeriod = "Historique d’écoute", InfiniteMixSources = "Sources",
        InfiniteMixWeightFavorites = "Renforcer le poids des favoris", InfiniteMixPreferRare = "Préférer les titres rarement écoutés",
        InfiniteMixIncludeGenres = "Inclure les genres", InfiniteMixExcludeGenres = "Exclure les genres", InfiniteMixGenresWatermark = "Saisir un genre…", InfiniteMixAddGenre = "Ajouter", InfiniteMixRemoveGenre = "Supprimer le genre",
        InfiniteMixPaused = "Mix infini en pause", InfiniteMixPause = "Mettre le mix en pause", InfiniteMixResume = "Reprendre le mix infini",
        InfiniteMixAdjust = "Ajuster le mix", InfiniteMixReplaceNext = "Remplacer la prochaine suggestion",
        InfiniteMixMoreLikeThis = "Plus comme ceci", InfiniteMixLessLikeThis = "Moins comme ceci", InfiniteMixExcludeTrack = "Exclure ce titre à l’avenir",
        DashboardQuickAccess = "Accès rapide",
        DashboardTotalMinutes = "Minutes au total",
        DashboardMinutesShort = "min",
        PeriodPrevious = "par rapport à la période précédente",
        ShowAll = "Tout afficher",
        DevicePcmSampleRates = "Fréquences PCM prises en charge", DeviceDsdRates = "Niveaux DSD",
        DevicePcmFormats = "Formats de sortie PCM", DeviceDsdFormats = "Formats de sortie DSD",
        DeviceChannelSummary = "{0} canaux de sortie · {1} canaux d’entrée",
        DeviceBufferSummary = "Tampon : min. {0}, préféré {1}, max. {2}, granularité {3}",
        DriverProvidedNoInformation = "Aucune information fournie par le pilote.",
        DsdSupportedWithoutFormats = "Le mode DSD est pris en charge, mais aucun format de canal précis n’a été signalé.",
        Unsupported = "Non pris en charge.",
        DeviceProbeInconclusive = "La vérification n’a pas été concluante. Une autre application utilise peut-être le périphérique.",
        WasapiEndpointSummary = "Point de terminaison WASAPI · {0} canaux\nFormat de mixage : {1} · {2} bits",
        WasapiNoExclusiveFormats = "Aucun format PCM exclusif détecté.",
        WasapiDsdNotRelevant = "Non pertinent pour WASAPI dans ce lecteur.",
        LinuxAlsaEndpointSummary = "Périphérique ALSA direct · {0} canaux\nPCM : {1} bits · fréquence exacte du morceau\nRééchantillonnage ALSA désactivé",
        LinuxOpenAlEndpointSummary = "Périphérique OpenAL · {0} canaux\nPCM : {1} bits · fréquence du mélangeur détectée pendant la lecture",
        LinuxDsdOutputUnavailable = "Actuellement indisponible pour ce chemin de sortie PCM.",
        NativeDsdUsesAsio = "La lecture DSD native de ce lecteur utilise ASIO.",
        Dashboard = "Tableau de bord", ThemeLight = "Clair", ThemeDark = "Sombre",
        StatusAvailable = "Disponible", StatusUnavailable = "Indisponible",
        StatusEnabled = "Activé", StatusDisabled = "Désactivé", StatusReady = "Prêt",
        StatusChecking = "Vérification …",
        DashboardIntroTitle = "Vue d'ensemble d'écoute",
        DashboardIntroHint = "Consultez les albums récemment ajoutés, le temps d’écoute dans le calendrier et vos genres principaux.",
        ArtistsIntroTitle = "Découvrir les artistes",
        ArtistsIntroHint = "Parcourez votre bibliothèque par artiste, ouvrez les albums et gérez les favoris et images d’artiste.",
        AlbumsIntroTitle = "Parcourir les albums",
        AlbumsIntroHint = "Passez de la table aux pochettes, ouvrez les titres d’un album et complétez les couvertures manquantes.",
        TracksIntroTitle = "Gérer les titres",
        TracksIntroHint = "Recherchez, filtrez et écoutez votre bibliothèque locale par genre, format et débit.",
        FoldersIntroTitle = "Arborescence",
        FoldersIntroHint = "Naviguez dans vos dossiers de bibliothèque et lancez les titres depuis leur contexte de dossier.",
        LanguageGerman = "Allemand", LanguageEnglish = "Anglais", LanguageFrench = "Français", LanguageSpanish = "Espagnol",
        LanguageRussian = "Russe", LanguageChineseSimplified = "Chinois simplifié",
        PcmIntegerFormat = "PCM {0} bits, petit-boutiste ({1})",
        PcmContainerFormat = "PCM {0} bits dans un conteneur {1} bits, petit-boutiste ({2})",
        PcmFloatFormat = "PCM flottant {0} bits, petit-boutiste ({1})",
        NativeDsdLsbFormat = "DSD natif, données 1 bit, premier échantillon dans le bit de poids faible ({0})",
        NativeDsdMsbFormat = "DSD natif, données 1 bit, premier échantillon dans le bit de poids fort ({0})",
        NativeDsdWordFormat = "DSD natif, mots de 8 bits sans dépendance d’ordre des octets ({0})",
        CountEntrySingular = "{0:N0} entrée", CountTrackSingular = "{0:N0} titre"
        , NormalizeArtists = "Normaliser les noms d’artistes"
        , NormalizeArtistsHint = "Supprime les mentions « feat. » de l’artiste principal et fusionne les variantes non ambiguës de ponctuation et d’espacement. Les fichiers audio ne sont pas modifiés."
        , ArtistsNormalizing = "Normalisation des artistes et reconstruction de l’index de recherche …"
        , ArtistsNormalized = "{0:N0} variantes d’artistes fusionnées, {1:N0} titres mis à jour."
        , ArtistNormalizationFailed = "Échec de la normalisation des artistes : {0}"
        , Streaming = "STREAMING"
        , StreamingServices = "Services de streaming"
        , Qobuz = "Qobuz"
        , QobuzApplicationId = "Identifiant d’application Qobuz"
        , QobuzIntegrationHint = "L’intégration Qobuz est préparée. Le catalogue et la lecture seront activés dès qu’un accès partenaire approuvé et la documentation officielle de l’API seront disponibles."
        , QobuzCredentialsHint = "Les secrets et jetons de connexion ne sont pas stockés dans settings.json. Windows les protège pour l’utilisateur actuel."
        , SearchArtistImage = "Rechercher une image d’artiste"
        , UploadArtistImage = "Importer une image d’artiste"
        , DeleteArtistImage = "Supprimer l’image d’artiste"
        , UploadCover = "Importer une pochette"
        , ImageFileType = "Fichiers image"
        , ArtistImageSearchTitle = "Rechercher une image d’artiste"
        , ArtistImageSearchRunning = "Recherche d’images d’artiste correspondantes …"
        , ArtistImageSearchNoResults = "Aucune image d’artiste trouvée."
        , ArtistImageSearchQuery = "Terme de recherche"
        , ArtistImageSearchFailed = "La recherche d’images d’artiste a échoué."
        , UseSelectedArtistImage = "Utiliser l’image sélectionnée"
        , ArtistImageDownloadFailed = "Impossible d’enregistrer l’image d’artiste sélectionnée."
        , ArtistProfileSearchTitle = "Recharger les informations de l’artiste"
        , ArtistProfileSearchHint = "Si nécessaire, modifiez le nom utilisé par Wikipédia ou Last.fm pour rechercher le profil. Cela ne renomme pas l’artiste dans votre bibliothèque."
        , ArtistProfileSearchQuery = "Nom pour la recherche du profil"
        , ArtistProfileSearchLoad = "Charger les informations"
        , EditArtistName = "Modifier le nom de l’artiste"
        , ArtistName = "Nom de l’artiste"
        , RenameArtist = "Renommer"
        , MergeArtistsTitle = "Fusionner les artistes"
        , ArtistNameExistsMessage = "Un artiste nommé « {0} » existe déjà. Faut-il fusionner les deux artistes ? Choisissez l’enregistrement et les informations de profil à conserver."
        , KeepArtistProfile = "Prioriser « {0} » et fusionner"
        , ArtistRenameFailed = "Impossible de renommer ou de fusionner l’artiste."
        , Shuffle = "Lecture aléatoire"
        , SearchLyrics = "Rechercher des paroles"
        , LyricsSearchTitle = "Rechercher des paroles"
        , LyricsSearchRunning = "Recherche de paroles correspondantes …"
        , LyricsSearchNoResults = "Aucune parole correspondante trouvée."
        , LyricsSearchFailed = "La recherche de paroles a échoué."
        , UseSelectedLyrics = "Utiliser les paroles sélectionnées"
        , SelectLyricsResult = "Sélectionnez des paroles à gauche pour les prévisualiser."
        , SynchronizedLyrics = "Synchronisées"
        , InternetRadio = "Radio Internet"
        , OwnRadios = "MES RADIOS"
        , SidebarSections = "Sections de la barre latérale"
        , SidebarSectionsHint = "Choisissez les sections repliables affichées dans la navigation principale."
        , PodcastInfo = "Informations sur le podcast"
        , ShowPodcastInfo = "Afficher les informations du podcast"
        , ClosePodcastInfo = "Fermer les informations du podcast"
        , PodcastPublishedOn = "Publié le {0}"
        , PodcastEpisodeDuration = "Durée {0}"
        , PodcastDescriptionUnavailable = "Aucun résumé n’est disponible pour cet épisode."
        , RadioDirectory = "Découvrir des stations"
        , RadioDirectoryHint = "Recherchez dans l’annuaire libre Radio Browser et ajoutez durablement des stations à vos radios."
        , RadioSearch = "Rechercher des stations"
        , RadioStation = "Station"
        , Country = "Pays"
        , PlayRadio = "Écouter"
        , AddToOwnRadios = "Ajouter à mes radios"
        , DeleteRadio = "Supprimer la station"
        , RadioLoading = "Chargement des stations de radio …"
        , RadioNoResults = "Aucune station de radio correspondante trouvée."
        , RadioEmptyState = "Recherchez par nom de station, pays ou genre, ou enregistrez des stations dans « Mes radios »."
        , OwnRadiosEmptyHint = "Aucune radio enregistrée. Recherchez dans l’annuaire de radio Internet et ajoutez des stations ici."
        , RadioAdded = "La station « {0} » a été ajoutée."
        , RadioDeleted = "La station « {0} » a été supprimée."
        , RadioSearchFailed = "Impossible de charger les stations de radio."
        , RadioNowPlaying = "À L’ANTENNE"
        , RadioMetadataUnavailable = "La station ne fournit actuellement aucune information sur le titre."
        , RadioGenres = "Genres"
        , ClearFilter = "Effacer le filtre"
        , Podcasts = "Podcasts"
        , MyPodcasts = "MES PODCASTS"
        , PodcastDirectory = "Découvrir des podcasts"
        , PodcastDirectoryHint = "Recherchez dans l’annuaire Apple Podcasts, épinglez durablement des podcasts et écoutez le dernier épisode du flux RSS."
        , PodcastSearch = "Rechercher des podcasts"
        , PodcastEmptyState = "Recherchez un podcast, filtrez par catégorie ou langue, puis épinglez vos favoris dans « Mes podcasts »."
        , Podcast = "Podcast"
        , PodcastAuthor = "Auteur"
        , PlayLatestEpisode = "Écouter le dernier"
        , AddToMyPodcasts = "Ajouter à mes podcasts"
        , DeletePodcast = "Supprimer le podcast"
        , PodcastLoading = "Chargement des podcasts …"
        , PodcastNoResults = "Aucun podcast correspondant trouvé."
        , MyPodcastsEmptyHint = "Aucun podcast épinglé. Recherchez dans l’annuaire de podcasts et ajoutez-en ici."
        , PodcastAdded = "Le podcast « {0} » a été ajouté."
        , PodcastDeleted = "Le podcast « {0} » a été supprimé."
        , PodcastSearchFailed = "Impossible de charger les podcasts."
        , PodcastFeedFailed = "Aucun épisode lisible n’a été trouvé dans le flux du podcast."
        , ShowEpisodes = "Afficher les épisodes"
        , Published = "Publié"
        , Progress = "Progression"
        , PodcastStatus = "Statut"
        , PodcastUnplayed = "Nouveau"
        , PodcastInProgress = "Commencé"
        , PodcastPlayed = "Écouté"
        , PodcastEpisodesLoading = "Chargement des épisodes …"
        , PodcastNoEpisodes = "Aucun épisode lisible n’a été trouvé dans ce flux."
        , PodcastCategories = "Catégories"
        , PodcastLanguages = "Langues"
        , PodcastLanguage = "Langue"
        , PodcastLanguagesLoading = "Détection des langues depuis les flux …"
        , PodcastOverview = "APERÇU DU PODCAST"
        , PodcastEpisodeTotal = "{0:N0} épisodes au total"
        , PodcastEpisodeUnheard = "{0:N0} non écoutés"
        , PodcastEpisodeStarted = "{0:N0} commencés"
        , PodcastLatestEpisode = "Dernier épisode : {0}"
        , DailyHistoryTitle = "Historique d’écoute – {0}"
        , PlayedAt = "Écouté à"
        , ListenedDuration = "Durée écoutée"
        , MediaType = "Type"
        , Close = "Fermer"
        , DailyHistoryNoEntries = "Aucune lecture n’est enregistrée pour ce jour."
        , PlexServers = "SERVEURS PLEX"
        , PlexServersSettings = "Serveurs Plex"
        , PlexServersHint = "Configurez un ou plusieurs serveurs Plex Media Server. Les jetons d’accès sont protégés pour le compte Windows actuel."
        , AddPlexServer = "Ajouter un serveur Plex"
        , PlexServerDialogTitle = "Serveur Plex"
        , PlexServerName = "Nom affiché"
        , PlexServerUrl = "URL du serveur"
        , PlexToken = "X-Plex-Token (facultatif)"
        , PlexTestConnection = "Tester la connexion"
        , PlexTestingConnection = "Test de la connexion…"
        , PlexConnectionSuccessful = "Connexion réussie. {0:N0} bibliothèques audio trouvées."
        , PlexConnectionFailed = "Échec de la connexion : {0}"
        , PlexServerFieldsRequired = "Le nom et l’URL du serveur sont obligatoires."
        , PlexServerUrlInvalid = "Saisissez une URL HTTP ou HTTPS valide."
        , PlexEditServer = "Modifier"
        , PlexRemoveServer = "Supprimer"
        , PlexNoAudioLibraries = "Aucune bibliothèque audio trouvée."
        , PlexLoading = "Chargement du contenu Plex…"
        , OrynivoServers = "SERVEURS ORYNIVO"
        , VersionLabel = "Version {0}"
        , CheckForUpdates = "Rechercher des mises à jour"
        , Updates = "Mises à jour"
        , CheckForUpdatesOnStartup = "Rechercher des mises à jour au démarrage de l’application"
        , WindowBehavior = "Comportement de la fenêtre"
        , StartMaximized = "Démarrer agrandie"
        , CheckingForUpdates = "Recherche de mises à jour…"
        , UpdateAvailable = "La version {0} est disponible."
        , UpToDate = "Orynivo est à jour."
        , UpdateUnavailable = "Les mises à jour vérifiées ne sont pas configurées pour cette version."
        , DownloadAndInstall = "Télécharger et installer"
        , DownloadingUpdate = "Téléchargement et vérification de la mise à jour…"
        , InstallingUpdate = "Installation de la mise à jour…"
        , UpdateFailed = "La mise à jour n’a pas pu être vérifiée ou installée."
        , UpdateServer = "Mettre à jour le serveur"
        , UpdatingServer = "Transfert de la mise à jour…"
        , ServerUpdateQueued = "Mise à jour lancée"
        , ServerUpdateUnavailable = "Aucune mise à jour serveur plus récente et compatible n’est disponible."
        , ServerUpdateFailed = "Échec de la mise à jour du serveur"
        , ServerUpdateRejected = "Le serveur a refusé la mise à jour (HTTP {0})"
        , UpdatingNamedServer = "Mise à jour du serveur « {0} »…"
        , ServerUpdatesFailedContinue = "Les serveurs suivants n’ont pas pu être mis à jour : {0}. Continuer la mise à jour de l’application ?"
        , SourceColumn = "Source"
        , LocalSource = "Local"
        , LocalSourceShort = "L"
        , OrynivoServersSettings = "Serveurs Orynivo"
        , OrynivoServersHint = "Connectez le lecteur à une ou plusieurs instances Orynivo Server sur votre réseau local. La clé API est chiffrée dans le coffre d'identifiants de l'utilisateur actuel."
        , AddOrynivoServer = "Ajouter un serveur"
        , OrynivoServerDialogTitle = "Orynivo Server"
        , OrynivoServerName = "Nom d'affichage"
        , OrynivoServerUrl = "URL du serveur (ex. http://192.168.1.10:5280)"
        , OrynivoServerApiKey = "Clé API"
        , OrynivoTestConnection = "Tester la connexion"
        , OrynivoTestingConnection = "Test de connexion en cours…"
        , OrynivoConnectionSuccessful = "Connexion réussie. Serveur : {0} v{1}"
        , OrynivoConnectionFailed = "Connexion échouée. Vérifiez l'URL et la clé API."
        , OrynivoServerFieldsRequired = "Le nom, l'URL et la clé API sont requis."
        , OrynivoEditServer = "Modifier"
        , OrynivoRemoveServer = "Supprimer"
        , OrynivoLoading = "Chargement du contenu du serveur…"
        , OrynivoServerDirectories = "Dossiers musicaux du serveur"
        , OrynivoLoadServerDirectories = "Charger depuis le serveur"
        , OrynivoAddServerDirectory = "Ajouter un dossier"
        , OrynivoLoadingServerDirectories = "Chargement des dossiers du serveur…"
        , OrynivoServerDirectoriesLoaded = "Dossiers du serveur chargés."
        , OrynivoServerDirectoriesLoadFailed = "Impossible de charger les dossiers du serveur."
        , OrynivoSavingServerDirectories = "Enregistrement des dossiers du serveur…"
        , OrynivoServerDirectoriesSaveFailed = "Impossible d'enregistrer les dossiers du serveur."
        , OrynivoCalculateReplayGainDuringScan = "Calculer les ReplayGain manquants pendant les analyses du serveur (plus lent)"
        , OrynivoSavingReplayGainSettings = "Enregistrement du paramètre d’analyse ReplayGain sur le serveur…"
        , OrynivoReplayGainSettingsSaveFailed = "Impossible d’enregistrer le paramètre d’analyse ReplayGain sur le serveur."
        , OrynivoReplayGainSettingsUnsupported = "Dossiers chargés. Ce serveur ne prend pas encore en charge le paramètre d’analyse ReplayGain."
        , OrynivoNoServerDirectories = "Aucun dossier serveur configuré."
        , OrynivoServerDirectoryBrowserTitle = "Sélectionner un dossier serveur"
        , OrynivoServerDirectoryRoots = "Racines"
        , OrynivoServerDirectoryUp = "Monter"
        , OrynivoSelectServerDirectory = "Sélectionner"
        , OrynivoServerDirectoryLoading = "Chargement du dossier…"
        , OrynivoServerDirectoryLoadFailed = "Impossible de charger le dossier."
        , OrynivoServerDirectoryEmpty = "Aucun sous-dossier disponible."
        , OrynivoServerScan = "Analyse du serveur"
        , OrynivoStartServerScan = "Analyser la bibliothèque"
        , OrynivoCalculateServerReplayGain = "Calculer ReplayGain"
        , OrynivoServerScanStarting = "Démarrage de l'analyse du serveur…"
        , OrynivoServerScanStartFailed = "Impossible de démarrer l'analyse du serveur."
        , OrynivoServerScanIdle = "Aucune analyse serveur en cours."
        , OrynivoServerScanDiscovering = "Recherche des fichiers : {0}"
        , OrynivoServerScanProgress = "{0}/{1} · {2}"
        , OrynivoServerScanCompleted = "Analyse terminée : {0} fichiers, {1} ajoutés, {2} mis à jour, {3} supprimés, {4} échecs."
        , OrynivoServerScanFailed = "Échec de l'analyse du serveur : {0}"
        , OrynivoServerBackup = "Sauvegarder la bibliothèque du serveur"
        , OrynivoDownloadBackup = "Télécharger la sauvegarde"
        , OrynivoRestoreBackup = "Restaurer la sauvegarde"
        , OrynivoBackupDownloading = "Téléchargement de la sauvegarde du serveur…"
        , OrynivoBackupDownloaded = "Sauvegarde du serveur enregistrée : {0}"
        , OrynivoBackupRestoring = "Validation et restauration de la sauvegarde du serveur…"
        , OrynivoBackupRestored = "Sauvegarde du serveur restaurée avec succès."
        , OrynivoBackupFailed = "Échec de la sauvegarde du serveur : {0}"
        , OrynivoRestoreBackupConfirm = "L'importation remplace la base, les playlists, l'historique, les pochettes, les images d'artistes et la liste des dossiers du serveur. Les fichiers audio restent inchangés. Continuer ?"
        , LoadMore = "Charger plus"
        , FfmpegDownloading = "Téléchargement de FFmpeg …"
        , FfmpegDownloadFailed = "FFmpeg n'a pas pu être téléchargé. Veuillez l'installer manuellement : ffmpeg.org"
        , SmartPlaylistDialogTitle = "Modifier la playlist intelligente"
        , SmartPlaylistName = "Nom"
        , SmartPlaylistBasicFilters = "Filtres de base"
        , SmartPlaylistGenres = "Genres (séparés par des virgules)"
        , SmartPlaylistFormats = "Formats (par ex. FLAC, MP3 ; séparés par des virgules)"
        , SmartPlaylistBitrates = "Débits en kbps (séparés par des virgules)"
        , SmartPlaylistSources = "Sources (local ou server:<id> ; séparées par des virgules)"
        , SmartPlaylistMetadata = "Métadonnées"
        , SmartPlaylistMinimumYear = "Année de début"
        , SmartPlaylistMaximumYear = "Année de fin"
        , SmartPlaylistSearchText = "Le texte de recherche contient"
        , SmartPlaylistArtistContains = "L’artiste contient"
        , SmartPlaylistAlbumContains = "L’album contient"
        , SmartPlaylistMinimumDuration = "Durée minimale en minutes"
        , SmartPlaylistMaximumDuration = "Durée maximale en minutes"
        , SmartPlaylistHistory = "Bibliothèque et historique de lecture"
        , SmartPlaylistAddedWithinDays = "Ajouté au cours des X derniers jours"
        , SmartPlaylistPlayedWithinDays = "Lu au cours des X derniers jours"
        , SmartPlaylistNeverPlayed = "Jamais lu"
        , SmartPlaylistMinimumPlayCount = "Nombre minimal de lectures"
        , SmartPlaylistMaximumPlayCount = "Nombre maximal de lectures"
        , SmartPlaylistResult = "Résultat"
        , SmartPlaylistSortOrder = "Tri"
        , SmartPlaylistSortTitle = "Titre A–Z"
        , SmartPlaylistSortRandom = "Aléatoire"
        , SmartPlaylistSortLastPlayed = "Écoutés récemment en premier"
        , SmartPlaylistSortLeastRecentlyPlayed = "Écoutés il y a longtemps en premier"
        , SmartPlaylistResultLimit = "Nombre maximal de titres (vide = illimité)"
        , CreateSmartPlaylist = "Créer la playlist intelligente"
        , InvalidSmartPlaylistCriteria = "Saisissez des nombres valides et des valeurs minimum/maximum cohérentes. « Jamais lu » ne peut pas être combiné avec une lecture récente ou un minimum de lectures positif."
        , EditSmartPlaylist = "Modifier la playlist intelligente"
        , LibraryEmptyHint = "Aucune source multimédia configurée. Ouvrez Paramètres > Bibliothèque, ajoutez des dossiers musicaux locaux ou connectez un serveur Orynivo."
        , SmartPlaylistUpdated = "Playlist intelligente « {0} » mise à jour."
        , ImportM3u8Playlist = "Importer une playlist M3U8"
        , ExportM3u8Playlist = "Exporter au format M3U8"
        , SaveAlbumAsPlaylist = "Enregistrer comme playlist"
        , AlbumPath = "Chemin de l’album"
        , TrackInfo = "Informations sur le titre"
        , ShowTrackInfo = "Afficher les informations du titre"
        , PhysicalPath = "Chemin physique du fichier"
        , UpNext = "À suivre"
        , GenreExplorer = "Nuage de genres"
        , GenreCloudHint = "Explorez les genres de votre bibliothèque locale et de tous les serveurs Orynivo connectés. Sélectionnez un genre pour l’affiner."
        , AllGenres = "Tous les genres"
        , GenreRecommendations = "Titres recommandés"
        , GenreCloudEmpty = "Aucun genre trouvé. Vérifiez que vos titres contiennent des balises de genre."
        , MoreGenres = "Autres genres"
        , PlayNext = "Lire ensuite"
        , AppendToQueue = "Ajouter à la file d’attente"
        , RemoveFromQueue = "Retirer de la file d’attente"
        , MoveUp = "Monter"
        , MoveDown = "Descendre"
        , SaveQueueAsPlaylist = "Enregistrer la file comme playlist"
        , TracksQueuedNext = "{0:N0} titres seront lus ensuite."
        , TracksAppendedToQueue = "{0:N0} titres ajoutés à la file d’attente."
        , M3u8ImportCompleted = "Playlist « {0} » importée : {1} entrées · {2} fichiers locaux manquants · {3} entrées HTTP · {4} ignorées."
        , M3u8ImportNoEntries = "Le fichier M3U8 ne contient aucune entrée importable."
        , M3u8ImportFailed = "Échec de l’importation M3U8 : {0}"
        , M3u8ExportCompleted = "Playlist « {0} » exportée au format M3U8 : {1} entrées · {2} ignorées."
        , M3u8ExportFailed = "Échec de l’exportation M3U8 : {0}"
        , Integration      = "INTÉGRATION"
        , McpServer        = "Serveur MCP"
        , McpServerHint    = "Ouvre un serveur HTTP/SSE local permettant aux assistants IA (ex. Claude Desktop) de contrôler le lecteur et de parcourir la bibliothèque."
        , McpServerEnabled = "Activer le serveur MCP"
        , McpServerPort    = "Port"
        , McpNetworkAccess = "Autoriser l'accès depuis le réseau local"
        , McpNetworkAccessHint = "Lie MCP à toutes les interfaces réseau. Les requêtes réseau nécessitent le jeton Bearer. Sans HTTPS ou VPN, le jeton peut être observé sur le réseau."
        , McpAccessToken = "Jeton d'accès"
        , McpAccessTokenWatermark = "Généré automatiquement lors de l'activation"
        , McpGenerateToken = "Régénérer"
        , MobileRemote = "Télécommande web mobile"
        , MobileRemoteHint = "Fournit une télécommande sur le réseau local. Utilisez son jeton dédié et protégez tout accès extérieur par HTTPS ou VPN."
        , MobileRemoteEnabled = "Activer la télécommande mobile sur le réseau"
        , MobileRemotePort = "Port"
        , MobileRemoteToken = "Jeton d’accès de la télécommande"
        , MobileRemoteNetworkAddress = "Adresse IP du réseau domestique"
        , MobileRemoteQrHint = "Enregistrez d’abord les paramètres. Scannez le QR code sur le même Wi-Fi pour vous connecter directement. Il contient le jeton : ne le partagez pas ! L’URL saisie manuellement demande le jeton. Choisissez une adresse accessible depuis le téléphone."
        , McpToolsHeader   = "Outils"
        , McpToolsHint     = "Activer ou désactiver des outils individuels."
        , WebBrowsing        = "Navigation web"
        , WebBrowsingHint    = "Fournit à l'IA un ensemble contrôlé d'outils web : recherche SearXNG et récupération sécurisée de pages. Les adresses privées et locales sont bloquées (protection SSRF)."
        , WebBrowsingEnabled = "Activer les outils web"
        , SearxngUrl         = "Adresse SearXNG"
        , WebBlockPrivate    = "Bloquer les adresses privées/locales (protection SSRF)"
        , WebMaxResults      = "Nombre maximal de résultats"
        , WebTimeoutSeconds  = "Délai d'attente (secondes)"
        , WebMaxResponseKb   = "Taille de réponse maximale (Ko)"
        , AiChat                = "Chat IA"
        , AiChatSettings        = "Chat IA"
        , AiChatHint            = "Se connecte à un modèle IA local ou en nuage via une API compatible OpenAI (p. ex. LM Studio, Ollama, OpenAI). Le modèle dispose des 32 outils Orynivo pour rechercher la bibliothèque, gérer les listes de lecture et contrôler la lecture."
        , AiChatEnabled         = "Activer le Chat IA"
        , AiChatEndpointUrl     = "URL de l'endpoint"
        , AiChatApiKey          = "Clé API (optionnel)"
        , AiChatLocalNote       = "LM Studio et Ollama ne nécessitent pas de clé API."
        , AiChatModel           = "Modèle"
        , AiChatLoadModels      = "Charger les modèles"
        , AiChatAvailableModels = "Sélectionner un modèle disponible"
        , AiChatTestConnection  = "Tester la connexion"
        , AiChatConnectionTesting = "Test de la connexion …"
        , AiChatModelsLoaded    = "{0} modèles chargés."
        , AiChatConnectionSucceeded = "Connexion réussie ; {0} modèles disponibles."
        , AiChatNoModels        = "Connexion réussie, mais le point de terminaison n’a signalé aucun modèle."
        , AiChatConnectionFailed = "Échec de la connexion. Vérifiez l’URL du point de terminaison et la clé API."
        , AiChatMaxTokens       = "Tokens max."
        , AiChatInputPlaceholder = "Posez une question …"
        , AiChatSend            = "Envoyer"
        , AiChatClear           = "Effacer"
        , AiChatCopy            = "Copier"
        , AiChatNotEnabled      = "Le Chat IA n'est pas activé. Activez-le dans Paramètres › Chat IA."
        , AiChatEmptyResponse   = "Le modèle a renvoyé une réponse vide."
        , AiChatToolResultFallback = "Le modèle n'a pas renvoyé de réponse finale après l'appel d'outil. Résultat de l'outil :"
    };

    private static readonly LocalizedStrings Spanish = new(
        "BIBLIOTECA", "Artistas", "Álbumes", "Pistas", "Estructura de carpetas", "Búsqueda", "Listas", "Acerca de", "Ajustes",
        "Filtro", "Favoritos", "Tipos de audio", "Tasa de bits",
        "Ningún dispositivo seleccionado.", "APARIENCIA", "Esquema de color", "Idioma", "REPRODUCCIÓN", "Dispositivo de salida",
        "BIBLIOTECA", "Directorios", "+ Agregar directorio", "Mantenimiento de base de datos",
        "Optimizar base de datos", "Reparar portadas de álbum", "Descargar portadas faltantes",
        "La descarga automática solo encuentra portadas cuando hay un ID de MusicBrainz presente. Para búsquedas más libres, usa el botón directamente en la vista del álbum.",
        "Portada no encontrada", "Buscar portada", "Buscar portada", "Buscando portadas coincidentes …",
        "No se encontraron portadas.", "Búsqueda de álbum", "Artista (opcional)", "Buscar de nuevo", "Usar portada seleccionada",
        "Eliminar portada", "Reasignar portada", "Autor", "Licencias", "Guardar", "Cancelar", "Tabla", "Portada",
        "(Desconocido)", "Artista del álbum", "Año", "Título", "Artista", "Álbum", "Género", "Duración", "Formato",
        "El término de búsqueda {0} no se encontró en las pistas.",
        "El término de búsqueda {0} no se encontró en los álbumes.",
        "El término de búsqueda {0} no se encontró en los artistas.",
        "{0:N0} entradas", "{0:N0} pistas",
        "Por favor, haz doble clic en una pista primero.", "Reproducción detenida.", "Reproducción finalizada.",
        "Por favor, selecciona primero un dispositivo ASIO en los ajustes.", "Por favor, selecciona primero un dispositivo WASAPI en los ajustes.",
        "{0} aún no está implementado.", "Ajustes guardados.", "No se pudo leer la información del dispositivo: {0}",
        "No se encontraron dispositivos de salida WASAPI activos.", "No se encontraron controladores ASIO.", "Selecciona un dispositivo y guarda.",
        "Escaneando…", "Directorio no encontrado.", "Escaneo cancelado.", "Optimizando base de datos …",
        "Optimización completada.", "Error en la optimización: {0}", "Reparando portadas de álbum …",
        "{0:N0} portadas de álbum reparadas.", "Error al reparar portadas: {0}",
        "Descargando portadas faltantes …", "{0:N0} portadas faltantes descargadas.",
        "Error al descargar portadas: {0}",
        "Agregar a lista", "Nueva lista …", "Nueva lista", "Nombre de la lista",
        "Crear",
        "Pista agregada a la lista '{0}'.", "{0} pistas agregadas a la lista '{1}'.",
        "Eliminar lista", "Quitar de la lista",
        "Lista '{0}' eliminada.", "Pista eliminada de la lista.",
        "Guardar filtros como lista inteligente", "Lista inteligente '{0}' guardada.",
        "Por favor, selecciona primero un filtro.",
        "Copia de seguridad de la biblioteca",
        "Exporta la base de datos, listas, historial, portadas y lista de directorios como ZIP. Los archivos de audio no están incluidos.",
        "Exportar biblioteca", "Importar biblioteca",
        "Exportando biblioteca …", "Biblioteca exportada a '{0}'.",
        "Error al exportar la biblioteca: {0}",
        "La importación reemplaza la biblioteca actual, listas, historial y todas las portadas. ¿Continuar?",
        "Importando biblioteca y reconstruyendo el índice de búsqueda …",
        "Biblioteca importada. El reproductor se cerrará ahora y podrá reiniciarse a continuación.",
        "Error al importar la biblioteca: {0}",
        "Por favor, finaliza primero los escaneos o tareas de mantenimiento activos.",
        "Biblioteca de Orynivo (*.zip)|*.zip",
        "Exportando biblioteca: {0}% – {1}",
        "Importando biblioteca: {0}% – {1}",
        "Letra", "Mostrar letra", "Actualizar letra", "Cerrar letra",
        "Cargando letra …", "Descargando letra desde LRCLIB …",
        "No hay metadatos disponibles para esta pista.", "No se encontró letra.",
        "No se pudo descargar la letra.",
        "INFORMACIÓN DEL ARTISTA", "Mostrar información del artista", "Actualizar información del artista", "Cerrar información del artista",
        "Cargando información del artista …", "Descargando información del artista …",
        "No se encontró información del artista.", "No se pudo descargar la información del artista.",
        "Ninguna imagen descargada", "Archivo de imagen faltante", "Error al cargar la imagen",
        "Fuente: Wikipedia", "Fuente: Last.fm",
        "Fuente de información del artista", "Clave de API de Last.fm",
        "Crea una clave de API gratuita en: last.fm/api/account/create",
        "Clave API de Fanart.tv",
        "Da prioridad a imágenes de artistas seleccionadas. La clave se guarda cifrada en el almacén de credenciales del usuario actual; también puedes definir FANART_TV_API_KEY. Usa «Actualizar información del artista» para artistas existentes. Crea una clave en fanart.tv/get-an-api-key/.",
        "Descargar imágenes de artistas que faltan",
        "Busca secuencialmente en la biblioteca local y en todos los servidores Orynivo configurados. Para cada artista intenta primero Fanart.tv (con una clave API) y después Wikimedia. Los resultados de Fanart.tv pueden aceptarse automáticamente; los de Wikimedia siempre requieren confirmación.",
        "Buscando imagen del artista {0}/{1}: {2} · {3}",
        "Se aceptaron {0:N0} imágenes de artistas y se rechazaron {1:N0}; fallaron {2:N0} solicitudes.",
        "Error al descargar imágenes de artistas: {0}",
        "Descarga de imágenes de artistas cancelada.",
        "Sugerencia {0}/{1}: {2} · {3}",
        "Cargando artistas locales y de servidores …",
        "Calculando el tiempo restante …",
        "Tiempo restante estimado: {0}",
        "Sugerencia de imagen del artista",
        "Fuente: {0}",
        "Esta imagen solo se guardará después de confirmarla.",
        "Aceptar",
        "Rechazar",
        "Mostrar todas las pistas del álbum",
        "Orynivo se ha bloqueado",
        "Se produjo un error inesperado. Se guardó un informe aquí:\n\n{0}\n\nOrynivo se cerrará ahora.",
        "Se produjo un error inesperado. No se pudo guardar el informe. Orynivo se cerrará ahora.")
    {
        AutoAcceptFanartTvImages = "Aceptar automáticamente los resultados de Fanart.tv",
        OutputType = "Tipo de salida", AsioOutputDevice = "Dispositivo de salida ASIO", WasapiOutputDevice = "Dispositivo de salida WASAPI",
        AirPlay = "AirPlay 2", AirPlayOutputDevice = "Dispositivo de salida AirPlay 2",
        NoAirPlayDevices = "No se encontraron dispositivos AirPlay 2 en la red local.",
        AirPlaySenderMissing = "Falta el puente nativo de AirPlay 2; se puede usar la utilidad compatible «raop_play» como alternativa.",
        SelectAirPlayDevice = "Seleccione primero un dispositivo de salida AirPlay.",
        CwAsioOutputDevice = "Dispositivo de salida cwASIO", SteinbergAsio = "Steinberg ASIO", CwAsio = "cwASIO",
        OpenAl = "OpenAL", OpenAlOutputDevice = "Dispositivo de salida OpenAL",
        DirectAlsa = "ALSA (directo, exclusivo)", AlsaOutputDevice = "Dispositivo de salida ALSA directo",
        LinuxDefaultAudioDevice = "Dispositivo predeterminado del sistema (OpenAL)",
        OpenAlInitializationFailed = "OpenAL no pudo inicializar la salida de audio del sistema.",
        AlsaExactOpenFailed = "El dispositivo ALSA «{0}» no se puede abrir a {1} Hz sin remuestreo: {2}",
        AlsaDeviceBusy = "El dispositivo ALSA directo «{0}» ya está siendo utilizado por PipeWire u otra aplicación. Redirigir la salida del sistema no libera el dispositivo. Desactive el perfil del dispositivo de sonido en el sistema o seleccione la salida OpenAL.",
        AlsaPrepareFailed = "ALSA no pudo preparar el dispositivo de salida después de buscar.",
        DeviceInfo = "Información del dispositivo",
        OutputProfile = "Salida",
        UserProfiles = "Perfiles de usuario", UserProfileActive = "Perfil activo", UserProfileCreate = "Crear perfil", UserProfileRename = "Renombrar perfil", UserProfileDelete = "Eliminar perfil", UserProfileName = "Nombre del perfil", UserProfileMigrateFavorites = "¿Copiar los datos personales existentes (favoritos, valoraciones e historial) al nuevo perfil?", UserProfileDeleteConfirm = "¿Eliminar el perfil «{0}»?",
        LocalMedia = "Local",
        OutputProfileCreate = "Crear salida",
        OutputProfileConfigure = "Configurar salida",
        OutputProfileDelete = "Eliminar salida",
        OutputProfileCreateTitle = "Crear nueva salida",
        OutputProfileConfigureTitle = "Configurar salida",
        OutputProfileName = "Nombre de la salida",
        OutputProfileNameExists = "Ya existe una salida con este nombre.",
        OutputProfileDeleteTitle = "Eliminar salida",
        OutputProfileDeleteConfirm = "¿Seguro que quieres eliminar la salida «{0}»?", DatabaseOptimizeHint = "Las páginas liberadas se eliminan para reducir físicamente el archivo.",
        GenreCloudCache = "Fondos de la nube de géneros",
        GenreCloudCacheHint = "Borra los mosaicos de artistas almacenados en caché. Se volverán a crear al abrir de nuevo un nivel de género.",
        GenreCloudCacheCleared = "Se ha vaciado la caché de fondos de la nube de géneros.",
        GenreCloudBackground = "Nube de géneros",
        GenreCloudBackgroundHint = "Elige las imágenes de fondo o desactívalas por completo para reducir la carga del sistema.",
        GenreCloudBackgroundNone = "Sin imágenes de fondo",
        GenreCloudBackgroundAlbums = "Carátulas de álbumes",
        GenreCloudBackgroundArtists = "Imágenes de artistas",
        GenreCloudVisibility = "Visibilidad de las imágenes",
        ClearGenreCloudCache = "Vaciar caché de fondos",
        AppearanceNavItem = "Apariencia", ArtistInfoNavItem = "Información del artista",
        AsioBridgeMissing = "Esta compilación no incluye compatibilidad con ASIO. Utiliza WASAPI.",
        KernelStreamingUnavailable = "Kernel Streaming se puede seleccionar, pero todavía no está implementado como backend de reproducción.",
        AddMusicDirectory = "Agregar directorio de música", TrackCountTooltip = "Número de pistas en la base de datos",
        Scan = "Analizar",
        RefreshAllMetadata = "Volver a leer metadatos",
        RefreshAllMetadataHint = "Vuelve a leer los metadatos de todos los archivos aunque su marca de tiempo no haya cambiado. Puede tardar bastante más.",
        RemoveDirectory = "Eliminar directorio",
        ScanCompleted = "Finalizado: {0} archivos · {1} nuevos · {2} actualizados · {3} eliminados{4}", ScanFailed = "Error: {0}",
        StartupPreparingLibrary = "Preparando biblioteca …",
        StartupCheckingSearchIndex = "Comprobando índice de búsqueda …",
        SearchIndexRebuilding = "Reconstruyendo el índice de búsqueda en segundo plano ({0}/{1}) …",
        SearchIndexReady = "El índice de búsqueda está actualizado.",
        SearchIndexFailed = "No se pudo actualizar el índice de búsqueda: {0}",
        Back = "Atrás", MarkAsFavorite = "Marcar como favorita",
        OpenAlbum = "Abrir álbum",
        OpenArtist = "Abrir artista",
        ToggleFavorite = "Alternar favorito",
        PlaybackThrough = "Reproducción mediante {0}",
        PlaybackThroughWithDsdConversion = "Reproducción mediante {0} · DSD se convierte a PCM ({1:N0} Hz)",
        NativeDsdOutput = "DSD nativo", DsdToPcmOutput = "DSD → PCM",
        DopOutput = "DSD mediante DoP",
        DopRequiresDirectAlsa = "En Linux, DSD mediante DoP requiere un dispositivo de salida ALSA directo sin remuestreo.",
        ReplayGain = "Ajuste de volumen ReplayGain",
        ReplayGainHint = "Se aplica a la reproducción PCM. El modo pista prioriza la ganancia de pista y el modo álbum la ganancia de álbum. La salida DSD nativa sigue siendo bit-perfect.",
        ReplayGainOff = "Desactivado", ReplayGainTrack = "Pista", ReplayGainAlbum = "Álbum",
        CalculateReplayGainDuringScan = "Calcular automáticamente ReplayGain faltante durante el análisis de la biblioteca local (más lento)",
        CalculateReplayGain = "Calcular ReplayGain faltante",
        ReplayGainCalculating = "Calculando ReplayGain …",
        ReplayGainCalculated = "ReplayGain calculado: {0} pistas actualizadas.",
        ReplayGainCalculationFailed = "Error al calcular ReplayGain: {0}",
        NonGaplessCrossfade = "Fundido para colas sin gapless (segundos)",
        NonGaplessCrossfadeHint = "0 desactiva la transición. Solo se aplica a cambios de cola que no usa ya el motor PCM gapless.",
        ReplayGainBadge = "RG",
        DsdPlayback = "Reproducción DSD",
        AlwaysConvertDsdToPcm = "Convertir siempre los archivos DSD a PCM",
        AlwaysConvertDsdToPcmHint = "También usa la ruta PCM con ASIO/cwASIO para aplicar volumen, ReplayGain y ecualizador. Con esta opción desactivada, la salida DSD nativa sigue siendo bit-perfect.",
        DsdOverPcm = "Reproducir DSD mediante DoP",
        DsdOverPcmHint = "Empaqueta DSD sin alteraciones en tramas PCM (DSD over PCM). Requiere un DAC compatible con DoP y una salida exacta sin remuestreo; el volumen, ReplayGain y el ecualizador no tienen efecto.",
        PcmOutputBoost = "Aumentar la salida PCM +6 dB",
        PcmOutputBoostHint = "Eleva todas las rutas de reproducción PCM para acercarlas al volumen percibido de la salida DSD nativa. La salida DSD nativa sigue siendo bit-perfect y no cambia.",
        OutputDevicesLoading = "Cargando dispositivos de salida …",
        Equalizer = "Ecualizador paramétrico",
        ReleaseOutputDevice = "Liberar el dispositivo de salida",
        ReacquireOutputDevice = "Recuperar el dispositivo y reanudar la reproducción",
        OutputDeviceReleased = "El dispositivo de salida está liberado",
        EqualizerHint = "Importa perfiles de Equalizer APO y AutoEQ para PCM y DSD convertido a PCM. La salida DSD nativa sigue siendo bit-perfect.",
        EqualizerEnabled = "Activar ecualizador",
        EqualizerImport = "Importar perfil APO/AutoEQ",
        EqualizerImporting = "Importando perfil de ecualizador …",
        EqualizerImportTitle = "Importar perfil de Equalizer APO o AutoEQ",
        EqualizerNoProfile = "No se ha importado ningún perfil.",
        EqualizerProfileSummary = "{0} · preamplificación {1:+0.##;-0.##;0} dB · {2} filtros",
        EqualizerImportFailed = "No se pudo importar el perfil.",
        EqualizerProfileFileType = "Perfil de Equalizer APO / AutoEQ",
        EqualizerPreamp = "Preamplificación (dB)",
        EqualizerFilterType = "Tipo de filtro",
        EqualizerFrequency = "Frecuencia (Hz)",
        EqualizerGain = "Ganancia (dB)",
        EqualizerQ = "Factor Q",
        EqualizerAddFilter = "Añadir filtro",
        EqualizerRemoveFilter = "Eliminar filtro",
        EqualizerPeak = "Pico",
        EqualizerLowShelf = "Estante de graves",
        EqualizerHighShelf = "Estante de agudos",
        EqualizerLowPass = "Paso bajo",
        EqualizerHighPass = "Paso alto",
        EqualizerCreate = "Crear ecualizador",
        EqualizerCreateTitle = "Crear nuevo ecualizador",
        EqualizerName = "Nombre del ecualizador",
        EqualizerNameExists = "Ya existe un ecualizador con este nombre.",
        EqualizerDelete = "Eliminar ecualizador",
        EqualizerDeleteTitle = "Eliminar ecualizador",
        EqualizerDeleteConfirm = "¿Seguro que quieres eliminar el ecualizador «{0}»?",
        SelectColumns = "Seleccionar columnas",
        FileName = "Nombre de archivo", FileSize = "Tamaño de archivo", AddedAt = "Añadido",
        SampleRate = "Frecuencia de muestreo", BitDepth = "Profundidad de bits", Channels = "Canales",
        TrackNumber = "Número de pista", DiscNumber = "Número de disco", Composer = "Compositor",
        Bpm = "BPM", ReplayGainTrackColumn = "ReplayGain pista",
        ReplayGainAlbumColumn = "ReplayGain álbum", Codec = "Códec", Tags = "Etiquetas",
        PersonalRating = "Mi valoración", MusicBrainzRating = "Valoración de MusicBrainz", MusicBrainzLoadRating = "Cargar valoración", MusicBrainzLoadingRating = "Cargando …", MusicBrainzRetryRating = "Reintentar", MusicBrainzNoRating = "Sin valoración",
        RatingSetHint = "Establecer valoración personal", RatingUpdateFailed = "No se pudo guardar la valoración.",
        Homepage = "Página principal", FeedUrl = "Dirección del feed",
        SearchResultSummary = "{0:N0} pistas · {1:N0} álbumes · {2:N0} artistas",
        RecentAlbums = "Álbumes añadidos recientemente",
        AlbumRecommendations = "Recomendaciones de álbumes",
        RecommendationMoodAll = "Todos los estados de ánimo",
        RecommendationMoodRelaxed = "Relajado",
        RecommendationMoodEnergetic = "Enérgico",
        RecommendationMoodHappy = "Alegre",
        RecommendationMoodMelancholic = "Melancólico",
        RecommendationNoMatches = "Aún no hay suficiente historial de escucha coincidente para ofrecer recomendaciones.",
        PlayMoreLikeThis = "Reproducir temas similares", PlayMoodMix = "Mezcla por estado de ánimo", SimilarTracksLoading = "Cargando temas similares …", SimilarTracksUnavailable = "Los datos de similitud no están disponibles para este tema.", SimilarTracksNoMatches = "No se encontraron temas similares.", SimilarTracksQueued = "{0:N0} temas similares están listos para reproducirse.",
        RecommendationListView = "Lista",
        RecommendationStageView = "Escenario",
        MetadataProblems = "Revisar metadatos",
        MetadataNoFindings = "Aún no hay resultados para los filtros seleccionados.",
        MetadataWorkflow = "1. Revisar resultados   →   2. Elegir carpeta   →   3. Comparar y confirmar",
        MetadataInspectFiles = "Comprobar también archivos (lectura y sumas de duplicados; más lento)",
        MetadataQuickHint = "Al abrir se revisan los metadatos guardados sin leer archivos de música. Active la opción y actualice para revisar archivos. Los servidores antiguos necesitan actualizarse para la revisión rápida.",
        MetadataSelectHint = "Elija una carpeta. El análisis no cambia ni elimina archivos.",
        MetadataActionGuide = "Identificar carpeta como álbum busca títulos, artistas y números en MusicBrainz. ReplayGain: Ajustes → Reproducción. Imágenes: vistas de álbumes/artistas. Revise archivos ausentes y duplicados manualmente. Solo se guardan cambios confirmados en la biblioteca; el audio no se modifica.",
        MetadataRemoteReadOnly = "Esta entrada del servidor es un informe de solo lectura. La corrección MusicBrainz solo está disponible para carpetas locales. Calcule ReplayGain en Orynivo Server y añada imágenes en las vistas de álbumes/artistas.",
        MetadataReviewGuide = "Arriba aparecen sus pistas actuales. Elija una edición y compare cada fila. Puede editar la búsqueda. Solo Aplicar corrección guarda los cambios.",
        MetadataPhaseDatabase = "Cargando metadatos guardados…",
        MetadataPhaseFolders = "Revisando carpetas y metadatos…",
        MetadataPhaseHashes = "Calculando sumas de posibles duplicados…",
        MetadataPhaseServers = "Esperando informes del servidor. No hay estimación remota; los resultados completados ya están disponibles.",
        MetadataPhaseReleases = "Cargando ediciones y pistas de MusicBrainz…",
        MetadataPhaseSaving = "Guardando corrección y actualizando el índice…",
        MetadataRemaining = "Tiempo estimado restante de este paso: {0}",
        MetadataRemainingUnknown = "Tiempo restante aún no estimable",
        MetadataElapsed = "Transcurrido: {0}",
        MetadataProblemsHint = "Orynivo revisa las carpetas físicas independientemente de posibles álbumes fragmentados. Haz doble clic en una entrada para buscar publicaciones coincidentes en MusicBrainz.",
        IdentifyFolderAsAlbum = "Identificar carpeta como álbum",
        MetadataFolder = "Carpeta",
        MetadataIssues = "Problemas detectados",
        MetadataTrackCount = "Pistas",
        MetadataReviewTitle = "Revisar metadatos del álbum",
        MetadataSearching = "Buscando en MusicBrainz por número y duración de pistas…",
        MetadataNoMatch = "No se encontró una publicación suficientemente coincidente.",
        MetadataSearchFailed = "MusicBrainz no está disponible en este momento. Vuelve a intentar la búsqueda.",
        MetadataFoundReleases = "Publicaciones coincidentes",
        MetadataApplyCorrection = "Aplicar corrección",
        MetadataCorrectionPreview = "Vista previa de la corrección",
        MetadataCurrentValues = "Actual: título — artista",
        MetadataProposedValues = "Propuesto: título — artista",
        MetadataRefreshAnalysis = "Actualizar análisis",
        MetadataAlbumQuery = "Término de búsqueda del álbum",
        MetadataArtistQuery = "Término de búsqueda del artista",
        MetadataRepairSuccess = "Los metadatos se corrigieron en la biblioteca de Orynivo.",
        MetadataIssueAlbums = "títulos de álbum incoherentes",
        MetadataIssueArtists = "artistas de álbum incoherentes",
        MetadataIssueMissingTitles = "títulos de pista ausentes",
        MetadataIssueMissingNumbers = "números de pista ausentes",
        MetadataIssueDuplicateNumbers = "números de pista duplicados",
        MetadataIssueMissingReplayGain = "{0} sin ReplayGain",
        MetadataIssueMissingMusicBrainzIds = "{0} sin identificador de MusicBrainz",
        MetadataSeverity = "Prioridad",
        MetadataSeverityAll = "Todas las prioridades",
        MetadataIssueAll = "Todos los tipos de problema",
        MetadataIssueReplayGain = "ReplayGain ausente",
        MetadataIssueMusicBrainzIds = "Identificador de MusicBrainz ausente",
        MetadataIssueIncompleteAlbum = "Álbum incompleto (faltan {0} pistas)",
        MetadataIssueAlbumArtwork = "Carátula del álbum ausente",
        MetadataIssueArtistImage = "Imagen del artista ausente",
        MetadataIssueMissingFiles = "Faltan {0} archivos de origen",
        MetadataIssueUnreadableFiles = "{0} archivos de origen no legibles",
        MetadataIssueLikelyDuplicates = "{0} archivos probablemente duplicados",
        MetadataIssueExactDuplicates = "{0} archivos duplicados idénticos byte a byte",
        MetadataIssueAlternateRecordings = "{0} grabaciones en otro archivo o edición",
        MetadataIssueArtistNameVariants = "{0} variantes de escritura del artista",
        MetadataSeverityInformation = "Información",
        MetadataSeverityWarning = "Advertencia",
        MetadataSeverityError = "Error",
        MetadataDoctorSummary = "{0} errores · {1} advertencias · {2} avisos",
        MetadataAnalysisFailed = "El análisis falló. Los detalles se guardaron en el registro de errores.",
        MetadataDoctorServersUnavailable = "{0} servidor(es) no disponible(s) o todavía sin Library Doctor",
        MetadataAnalysisCancelled = "El análisis fue cancelado.",
        Calendar = "Calendario – {0}", TopGenres = "Géneros más escuchados",
        TopAlbums = "Álbumes más escuchados",
        TopArtists = "Artistas más escuchados",
        ListeningStats = "Estadísticas de escucha",
        PeriodAllTime = "Todo",
        PeriodThisYear = "Este año",
        PeriodThisMonth = "Este mes",
        PeriodLast30Days = "Últimos 30 días",
        PeriodLast7Days = "Últimos 7 días",
        HistorySourceRemote = "Remoto",
        HistorySourcePlex = "Plex",
        LibraryUpdating = "Actualizando biblioteca…",
        LibraryUpdatingWithCount = "Actualizando biblioteca… {0} / {1} archivos",
        RefreshView = "Actualizar",
        LibraryDataAvailable = "Nuevos datos de biblioteca disponibles",
        ServerUnreachable = "No accesible",
        ServerLastConnected = "Última conexión: {0}",
        ServerNeverConnected = "Nunca conectado",
        ServerMissingFeatures = "El servidor no admite: {0}",
        CapabilityTrackFacets = "Facetas de pistas",
        CapabilityRecentAlbums = "Álbumes recientes",
        CapabilityWaveforms = "Formas de onda",
        RemoteCache = "Caché remota",
        RemoteCacheSize = "Tamaño de caché: {0}",
        ClearRemoteCacheAll = "Vaciar toda la caché",
        ClearCache = "Vaciar caché",
        RemoteScanning = "Actualizando {0}…",
        RemoteScanningWithCount = "Actualizando {0}… {1} / {2} archivos",
        SmartPlaylistPreviewCount = "{0} pistas coinciden",
        SmartPlaylistPreviewComputing = "Calculando…",
        SmartPlaylistPreviewInvalid = "Criterios no válidos",
        RestoreQueue = "Última cola",
        RestoreQueueTooltip = "Restaurar la última cola reproducida",
        NoPreviousQueue = "No hay ninguna cola anterior.",
        ClearQueue = "Vaciar cola",
        QueueCleared = "Cola vaciada.",
        NoData = "No hay datos disponibles.",
        RecentlyPlayed = "Reproducidos recientemente",
        GreetingMorning = "Buenos días", GreetingAfternoon = "Buenas tardes", GreetingEvening = "Buenas noches",
        DashboardTagline = "Tu centro musical personal",
        DashboardWelcomeBack = "TE DAMOS LA BIENVENIDA",
        DashboardHeroHint = "¿Listo para disfrutar de buena música? Descubre nuevos sonidos o vuelve a tus favoritos.",
        DashboardRandomPlayback = "Reproducción aleatoria",
        InfiniteMixStart = "Iniciar mezcla infinita",
        GenreCloudInfiniteMix = "Iniciar mezcla infinita desde la nube",
        InfiniteMixStop = "Detener mezcla infinita",
        InfiniteMixActive = "Mezcla infinita activa · se completa automáticamente",
        InfiniteMixCalculating = "Se está preparando tu mezcla infinita …",
        InfiniteMixSettingsTitle = "Ajustar mezcla infinita",
        InfiniteMixSettingsHint = "Elige cómo debe crear Orynivo tu próxima mezcla que se repone continuamente.",
        InfiniteMixMood = "Estado de ánimo", InfiniteMixMoodCalm = "Tranquilo", InfiniteMixMoodBalanced = "Equilibrado", InfiniteMixMoodEnergetic = "Enérgico",
        InfiniteMixDiscovery = "Nivel de descubrimiento", InfiniteMixFamiliar = "Familiar", InfiniteMixAdventurous = "Aventurero",
        InfiniteMixPeriod = "Historial de escucha", InfiniteMixSources = "Fuentes",
        InfiniteMixWeightFavorites = "Dar más peso a los favoritos", InfiniteMixPreferRare = "Preferir pistas poco escuchadas",
        InfiniteMixIncludeGenres = "Incluir géneros", InfiniteMixExcludeGenres = "Excluir géneros", InfiniteMixGenresWatermark = "Introduce un género…", InfiniteMixAddGenre = "Añadir", InfiniteMixRemoveGenre = "Eliminar género",
        InfiniteMixPaused = "Mezcla infinita en pausa", InfiniteMixPause = "Pausar mezcla infinita", InfiniteMixResume = "Reanudar mezcla infinita",
        InfiniteMixAdjust = "Ajustar mezcla", InfiniteMixReplaceNext = "Cambiar la siguiente sugerencia",
        InfiniteMixMoreLikeThis = "Más de esto", InfiniteMixLessLikeThis = "Menos de esto", InfiniteMixExcludeTrack = "Excluir pista en el futuro",
        DashboardQuickAccess = "Acceso rápido",
        DashboardTotalMinutes = "Minutos totales",
        DashboardMinutesShort = "min",
        PeriodPrevious = "frente al período anterior",
        ShowAll = "Ver todo",
        DevicePcmSampleRates = "Frecuencias PCM compatibles", DeviceDsdRates = "Niveles DSD",
        DevicePcmFormats = "Formatos de salida PCM", DeviceDsdFormats = "Formatos de salida DSD",
        DeviceChannelSummary = "{0} canales de salida · {1} canales de entrada",
        DeviceBufferSummary = "Búfer: mín. {0}, preferido {1}, máx. {2}, granularidad {3}",
        DriverProvidedNoInformation = "El controlador no proporcionó información.",
        DsdSupportedWithoutFormats = "El modo DSD es compatible, pero no se informaron formatos de canal concretos.",
        Unsupported = "No compatible.",
        DeviceProbeInconclusive = "No se pudo comprobar de forma concluyente. Es posible que otra aplicación esté usando el dispositivo.",
        WasapiEndpointSummary = "Punto final WASAPI · {0} canales\nFormato de mezcla: {1} · {2} bits",
        WasapiNoExclusiveFormats = "No se detectaron formatos PCM exclusivos.",
        WasapiDsdNotRelevant = "No es relevante para WASAPI en este reproductor.",
        LinuxAlsaEndpointSummary = "Dispositivo ALSA directo · {0} canales\nPCM: {1} bits · frecuencia exacta de la pista\nRemuestreo de ALSA desactivado",
        LinuxOpenAlEndpointSummary = "Dispositivo OpenAL · {0} canales\nPCM: {1} bits · frecuencia del mezclador detectada durante la reproducción",
        LinuxDsdOutputUnavailable = "Actualmente no disponible para esta ruta de salida PCM.",
        NativeDsdUsesAsio = "La reproducción DSD nativa de este reproductor utiliza ASIO.",
        Dashboard = "Panel", ThemeLight = "Claro", ThemeDark = "Oscuro",
        StatusAvailable = "Disponible", StatusUnavailable = "No disponible",
        StatusEnabled = "Activado", StatusDisabled = "Desactivado", StatusReady = "Listo",
        StatusChecking = "Comprobando …",
        DashboardIntroTitle = "Resumen de escucha",
        DashboardIntroHint = "Consulta álbumes añadidos recientemente, tiempo de escucha en el calendario y tus géneros principales.",
        ArtistsIntroTitle = "Descubrir artistas",
        ArtistsIntroHint = "Explora tu biblioteca por artista, abre álbumes directamente y gestiona favoritos e imágenes.",
        AlbumsIntroTitle = "Explorar álbumes",
        AlbumsIntroHint = "Cambia entre tabla y carátulas, abre pistas del álbum y completa portadas que falten.",
        TracksIntroTitle = "Gestionar pistas",
        TracksIntroHint = "Busca, filtra y reproduce tu biblioteca local con facetas de género, formato y bitrate.",
        FoldersIntroTitle = "Estructura de carpetas",
        FoldersIntroHint = "Navega tu música por las carpetas configuradas y reproduce pistas desde su contexto de carpeta.",
        LanguageGerman = "Alemán", LanguageEnglish = "Inglés", LanguageFrench = "Francés", LanguageSpanish = "Español",
        LanguageRussian = "Ruso", LanguageChineseSimplified = "Chino simplificado",
        PcmIntegerFormat = "PCM de {0} bits, little endian ({1})",
        PcmContainerFormat = "PCM de {0} bits en contenedor de {1} bits, little endian ({2})",
        PcmFloatFormat = "PCM de coma flotante de {0} bits, little endian ({1})",
        NativeDsdLsbFormat = "DSD nativo, datos de 1 bit, primera muestra en el bit menos significativo ({0})",
        NativeDsdMsbFormat = "DSD nativo, datos de 1 bit, primera muestra en el bit más significativo ({0})",
        NativeDsdWordFormat = "DSD nativo, palabras de 8 bits sin relevancia de endian ({0})",
        CountEntrySingular = "{0:N0} entrada", CountTrackSingular = "{0:N0} pista"
        , NormalizeArtists = "Normalizar nombres de artistas"
        , NormalizeArtistsHint = "Elimina los añadidos «feat.» del artista principal y combina variantes inequívocas de puntuación y espacios. Los archivos de audio no se modifican."
        , ArtistsNormalizing = "Normalizando artistas y reconstruyendo el índice de búsqueda …"
        , ArtistsNormalized = "Se combinaron {0:N0} variantes de artistas y se actualizaron {1:N0} pistas."
        , ArtistNormalizationFailed = "Error al normalizar artistas: {0}"
        , Streaming = "STREAMING"
        , StreamingServices = "Servicios de streaming"
        , Qobuz = "Qobuz"
        , QobuzApplicationId = "ID de aplicación de Qobuz"
        , QobuzIntegrationHint = "La integración con Qobuz está preparada. El catálogo y la reproducción se activarán cuando estén disponibles un acceso de socio aprobado y la documentación oficial de la API."
        , QobuzCredentialsHint = "Los secretos y tokens de inicio de sesión no se guardan en settings.json. Windows los protege para el usuario actual."
        , SearchArtistImage = "Buscar imagen del artista"
        , UploadArtistImage = "Subir imagen del artista"
        , DeleteArtistImage = "Eliminar imagen del artista"
        , UploadCover = "Subir portada"
        , ImageFileType = "Archivos de imagen"
        , ArtistImageSearchTitle = "Buscar imagen del artista"
        , ArtistImageSearchRunning = "Buscando imágenes del artista …"
        , ArtistImageSearchNoResults = "No se encontraron imágenes del artista."
        , ArtistImageSearchQuery = "Término de búsqueda"
        , ArtistImageSearchFailed = "La búsqueda de imágenes del artista ha fallado."
        , UseSelectedArtistImage = "Usar imagen seleccionada"
        , ArtistImageDownloadFailed = "No se pudo guardar la imagen del artista seleccionada."
        , ArtistProfileSearchTitle = "Volver a cargar la información del artista"
        , ArtistProfileSearchHint = "Si es necesario, ajusta el nombre que Wikipedia o Last.fm utiliza para buscar el perfil. Esto no cambia el nombre del artista en tu biblioteca."
        , ArtistProfileSearchQuery = "Nombre para buscar el perfil"
        , ArtistProfileSearchLoad = "Cargar información"
        , EditArtistName = "Cambiar nombre del artista"
        , ArtistName = "Nombre del artista"
        , RenameArtist = "Cambiar nombre"
        , MergeArtistsTitle = "Combinar artistas"
        , ArtistNameExistsMessage = "Ya existe un artista llamado «{0}». ¿Se deben combinar ambos artistas? Elige qué registro y datos de perfil deben conservarse."
        , KeepArtistProfile = "Priorizar «{0}» y combinar"
        , ArtistRenameFailed = "No se pudo cambiar el nombre ni combinar el artista."
        , Shuffle = "Reproducción aleatoria"
        , SearchLyrics = "Buscar letra"
        , LyricsSearchTitle = "Buscar letra"
        , LyricsSearchRunning = "Buscando letras coincidentes …"
        , LyricsSearchNoResults = "No se encontraron letras coincidentes."
        , LyricsSearchFailed = "La búsqueda de letras ha fallado."
        , UseSelectedLyrics = "Usar letra seleccionada"
        , SelectLyricsResult = "Selecciona una letra a la izquierda para previsualizarla."
        , SynchronizedLyrics = "Sincronizada"
        , InternetRadio = "Radio por Internet"
        , OwnRadios = "MIS RADIOS"
        , SidebarSections = "Secciones de la barra lateral"
        , SidebarSectionsHint = "Elige qué secciones desplegables se muestran en la navegación principal."
        , PodcastInfo = "Información del podcast"
        , ShowPodcastInfo = "Mostrar información del podcast"
        , ClosePodcastInfo = "Cerrar información del podcast"
        , PodcastPublishedOn = "Publicado el {0}"
        , PodcastEpisodeDuration = "Duración {0}"
        , PodcastDescriptionUnavailable = "No hay ningún resumen disponible para este episodio."
        , RadioDirectory = "Descubrir emisoras"
        , RadioDirectoryHint = "Busca en el directorio libre Radio Browser y añade emisoras permanentemente a tus radios."
        , RadioSearch = "Buscar emisoras"
        , RadioStation = "Emisora"
        , Country = "País"
        , PlayRadio = "Reproducir"
        , AddToOwnRadios = "Añadir a mis radios"
        , DeleteRadio = "Eliminar emisora"
        , RadioLoading = "Cargando emisoras de radio …"
        , RadioNoResults = "No se encontraron emisoras de radio coincidentes."
        , RadioEmptyState = "Busca por nombre de emisora, país o género, o guarda emisoras encontradas en «Mis radios»."
        , OwnRadiosEmptyHint = "Aún no hay emisoras guardadas. Busca en el directorio de radio por Internet y añade emisoras aquí."
        , RadioAdded = "Se añadió la emisora «{0}»."
        , RadioDeleted = "Se eliminó la emisora «{0}»."
        , RadioSearchFailed = "No se pudieron cargar las emisoras de radio."
        , RadioNowPlaying = "AHORA EN ANTENA"
        , RadioMetadataUnavailable = "La emisora no proporciona información de la pista en este momento."
        , RadioGenres = "Géneros"
        , ClearFilter = "Borrar filtro"
        , Podcasts = "Podcasts"
        , MyPodcasts = "MIS PODCASTS"
        , PodcastDirectory = "Descubrir podcasts"
        , PodcastDirectoryHint = "Busca en el directorio de Apple Podcasts, fija podcasts de forma permanente y reproduce el episodio más reciente del canal RSS."
        , PodcastSearch = "Buscar podcasts"
        , PodcastEmptyState = "Busca un podcast, filtra por categoría o idioma y fija favoritos en «Mis podcasts»."
        , Podcast = "Podcast"
        , PodcastAuthor = "Autor"
        , PlayLatestEpisode = "Reproducir el último"
        , AddToMyPodcasts = "Añadir a mis podcasts"
        , DeletePodcast = "Eliminar podcast"
        , PodcastLoading = "Cargando podcasts …"
        , PodcastNoResults = "No se encontraron podcasts coincidentes."
        , MyPodcastsEmptyHint = "Aún no hay podcasts fijados. Busca en el directorio de podcasts y añádelos aquí."
        , PodcastAdded = "Se añadió el podcast «{0}»."
        , PodcastDeleted = "Se eliminó el podcast «{0}»."
        , PodcastSearchFailed = "No se pudieron cargar los podcasts."
        , PodcastFeedFailed = "No se encontró ningún episodio reproducible en el canal del podcast."
        , ShowEpisodes = "Mostrar episodios"
        , Published = "Publicado"
        , Progress = "Progreso"
        , PodcastStatus = "Estado"
        , PodcastUnplayed = "Nuevo"
        , PodcastInProgress = "Empezado"
        , PodcastPlayed = "Escuchado"
        , PodcastEpisodesLoading = "Cargando episodios del podcast …"
        , PodcastNoEpisodes = "No se encontraron episodios reproducibles en este canal."
        , PodcastCategories = "Categorías"
        , PodcastLanguages = "Idiomas"
        , PodcastLanguage = "Idioma"
        , PodcastLanguagesLoading = "Detectando los idiomas desde los canales …"
        , PodcastOverview = "RESUMEN DEL PODCAST"
        , PodcastEpisodeTotal = "{0:N0} episodios en total"
        , PodcastEpisodeUnheard = "{0:N0} sin escuchar"
        , PodcastEpisodeStarted = "{0:N0} empezados"
        , PodcastLatestEpisode = "Último episodio: {0}"
        , DailyHistoryTitle = "Historial de escucha – {0}"
        , PlayedAt = "Escuchado a las"
        , ListenedDuration = "Tiempo escuchado"
        , MediaType = "Tipo"
        , Close = "Cerrar"
        , DailyHistoryNoEntries = "No hay reproducciones registradas para este día."
        , PlexServers = "SERVIDORES PLEX"
        , PlexServersSettings = "Servidores Plex"
        , PlexServersHint = "Configura uno o varios Plex Media Server. Los tokens de acceso se protegen para la cuenta actual de Windows."
        , AddPlexServer = "Añadir servidor Plex"
        , PlexServerDialogTitle = "Servidor Plex"
        , PlexServerName = "Nombre para mostrar"
        , PlexServerUrl = "URL del servidor"
        , PlexToken = "X-Plex-Token (opcional)"
        , PlexTestConnection = "Probar conexión"
        , PlexTestingConnection = "Probando la conexión…"
        , PlexConnectionSuccessful = "Conexión correcta. Se encontraron {0:N0} bibliotecas de audio."
        , PlexConnectionFailed = "Error de conexión: {0}"
        , PlexServerFieldsRequired = "El nombre y la URL del servidor son obligatorios."
        , PlexServerUrlInvalid = "Introduce una URL HTTP o HTTPS válida."
        , PlexEditServer = "Editar"
        , PlexRemoveServer = "Eliminar"
        , PlexNoAudioLibraries = "No se encontraron bibliotecas de audio."
        , PlexLoading = "Cargando contenido de Plex…"
        , OrynivoServers = "SERVIDORES ORYNIVO"
        , VersionLabel = "Versión {0}"
        , CheckForUpdates = "Buscar actualizaciones"
        , Updates = "Actualizaciones"
        , CheckForUpdatesOnStartup = "Buscar actualizaciones al iniciar la aplicación"
        , WindowBehavior = "Comportamiento de la ventana"
        , StartMaximized = "Iniciar maximizada"
        , CheckingForUpdates = "Buscando actualizaciones…"
        , UpdateAvailable = "La versión {0} está disponible."
        , UpToDate = "Orynivo está actualizado."
        , UpdateUnavailable = "Las actualizaciones verificadas no están configuradas para esta compilación."
        , DownloadAndInstall = "Descargar e instalar"
        , DownloadingUpdate = "Descargando y verificando la actualización…"
        , InstallingUpdate = "Instalando la actualización…"
        , UpdateFailed = "No se pudo comprobar o instalar la actualización."
        , UpdateServer = "Actualizar servidor"
        , UpdatingServer = "Transfiriendo actualización…"
        , ServerUpdateQueued = "Actualización iniciada"
        , ServerUpdateUnavailable = "No hay disponible una actualización de servidor más reciente y compatible."
        , ServerUpdateFailed = "Error al actualizar el servidor"
        , ServerUpdateRejected = "El servidor rechazó la actualización (HTTP {0})"
        , UpdatingNamedServer = "Actualizando el servidor «{0}»…"
        , ServerUpdatesFailedContinue = "No se pudieron actualizar los siguientes servidores: {0}. ¿Continuar con la actualización de la aplicación?"
        , SourceColumn = "Origen"
        , LocalSource = "Local"
        , LocalSourceShort = "L"
        , OrynivoServersSettings = "Servidores Orynivo"
        , OrynivoServersHint = "Conecta el reproductor a una o varias instancias de Orynivo Server en tu red local. La clave API se guarda cifrada en el almacén de credenciales del usuario actual."
        , AddOrynivoServer = "Agregar servidor"
        , OrynivoServerDialogTitle = "Orynivo Server"
        , OrynivoServerName = "Nombre para mostrar"
        , OrynivoServerUrl = "URL del servidor (p. ej. http://192.168.1.10:5280)"
        , OrynivoServerApiKey = "Clave API"
        , OrynivoTestConnection = "Probar conexión"
        , OrynivoTestingConnection = "Probando conexión…"
        , OrynivoConnectionSuccessful = "Conexión exitosa. Servidor: {0} v{1}"
        , OrynivoConnectionFailed = "Conexión fallida. Comprueba la URL y la clave API."
        , OrynivoServerFieldsRequired = "El nombre, la URL y la clave API son obligatorios."
        , OrynivoEditServer = "Editar"
        , OrynivoRemoveServer = "Eliminar"
        , OrynivoLoading = "Cargando contenido del servidor…"
        , OrynivoServerDirectories = "Directorios de música del servidor"
        , OrynivoLoadServerDirectories = "Cargar del servidor"
        , OrynivoAddServerDirectory = "Agregar directorio"
        , OrynivoLoadingServerDirectories = "Cargando directorios del servidor…"
        , OrynivoServerDirectoriesLoaded = "Directorios del servidor cargados."
        , OrynivoServerDirectoriesLoadFailed = "No se pudieron cargar los directorios del servidor."
        , OrynivoSavingServerDirectories = "Guardando directorios del servidor…"
        , OrynivoServerDirectoriesSaveFailed = "No se pudieron guardar los directorios del servidor."
        , OrynivoCalculateReplayGainDuringScan = "Calcular ReplayGain faltante durante los análisis del servidor (más lento)"
        , OrynivoSavingReplayGainSettings = "Guardando el ajuste de análisis ReplayGain en el servidor…"
        , OrynivoReplayGainSettingsSaveFailed = "No se pudo guardar el ajuste de análisis ReplayGain en el servidor."
        , OrynivoReplayGainSettingsUnsupported = "Directorios cargados. Este servidor aún no admite el ajuste de análisis ReplayGain."
        , OrynivoNoServerDirectories = "No hay directorios de servidor configurados."
        , OrynivoServerDirectoryBrowserTitle = "Seleccionar directorio del servidor"
        , OrynivoServerDirectoryRoots = "Raíces"
        , OrynivoServerDirectoryUp = "Subir"
        , OrynivoSelectServerDirectory = "Seleccionar"
        , OrynivoServerDirectoryLoading = "Cargando directorio…"
        , OrynivoServerDirectoryLoadFailed = "No se pudo cargar el directorio."
        , OrynivoServerDirectoryEmpty = "No hay subdirectorios disponibles."
        , OrynivoServerScan = "Escaneo del servidor"
        , OrynivoStartServerScan = "Analizar biblioteca"
        , OrynivoCalculateServerReplayGain = "Calcular ReplayGain"
        , OrynivoServerScanStarting = "Iniciando escaneo del servidor…"
        , OrynivoServerScanStartFailed = "No se pudo iniciar el escaneo del servidor."
        , OrynivoServerScanIdle = "No hay ningún escaneo del servidor en curso."
        , OrynivoServerScanDiscovering = "Buscando archivos: {0}"
        , OrynivoServerScanProgress = "{0}/{1} · {2}"
        , OrynivoServerScanCompleted = "Escaneo completado: {0} archivos, {1} agregados, {2} actualizados, {3} eliminados, {4} errores."
        , OrynivoServerScanFailed = "Error en el escaneo del servidor: {0}"
        , OrynivoServerBackup = "Guardar biblioteca del servidor"
        , OrynivoDownloadBackup = "Descargar copia"
        , OrynivoRestoreBackup = "Restaurar copia"
        , OrynivoBackupDownloading = "Descargando la copia del servidor…"
        , OrynivoBackupDownloaded = "Copia del servidor guardada: {0}"
        , OrynivoBackupRestoring = "Validando y restaurando la copia del servidor…"
        , OrynivoBackupRestored = "Copia del servidor restaurada correctamente."
        , OrynivoBackupFailed = "Error en la copia del servidor: {0}"
        , OrynivoRestoreBackupConfirm = "La importación reemplaza la base de datos, listas, historial, carátulas, imágenes de artistas y lista de directorios del servidor. Los archivos de audio no cambian. ¿Continuar?"
        , LoadMore = "Cargar más"
        , FfmpegDownloading = "Descargando FFmpeg …"
        , FfmpegDownloadFailed = "No se pudo descargar FFmpeg. Instálelo manualmente: ffmpeg.org"
        , SmartPlaylistDialogTitle = "Editar lista inteligente"
        , SmartPlaylistName = "Nombre"
        , SmartPlaylistBasicFilters = "Filtros básicos"
        , SmartPlaylistGenres = "Géneros (separados por comas)"
        , SmartPlaylistFormats = "Formatos (por ejemplo FLAC, MP3; separados por comas)"
        , SmartPlaylistBitrates = "Bitrates en kbps (separados por comas)"
        , SmartPlaylistSources = "Orígenes (local o server:<id>; separados por comas)"
        , SmartPlaylistMetadata = "Metadatos"
        , SmartPlaylistMinimumYear = "Año desde"
        , SmartPlaylistMaximumYear = "Año hasta"
        , SmartPlaylistSearchText = "El texto de búsqueda contiene"
        , SmartPlaylistArtistContains = "El artista contiene"
        , SmartPlaylistAlbumContains = "El álbum contiene"
        , SmartPlaylistMinimumDuration = "Duración mínima en minutos"
        , SmartPlaylistMaximumDuration = "Duración máxima en minutos"
        , SmartPlaylistHistory = "Biblioteca e historial de reproducción"
        , SmartPlaylistAddedWithinDays = "Añadido en los últimos X días"
        , SmartPlaylistPlayedWithinDays = "Reproducido en los últimos X días"
        , SmartPlaylistNeverPlayed = "Nunca reproducido"
        , SmartPlaylistMinimumPlayCount = "Número mínimo de reproducciones"
        , SmartPlaylistMaximumPlayCount = "Número máximo de reproducciones"
        , SmartPlaylistResult = "Resultado"
        , SmartPlaylistSortOrder = "Orden"
        , SmartPlaylistSortTitle = "Título A–Z"
        , SmartPlaylistSortRandom = "Aleatorio"
        , SmartPlaylistSortLastPlayed = "Reproducidos recientemente primero"
        , SmartPlaylistSortLeastRecentlyPlayed = "Menos recientes primero"
        , SmartPlaylistResultLimit = "Número máximo de pistas (vacío = sin límite)"
        , CreateSmartPlaylist = "Crear lista inteligente"
        , InvalidSmartPlaylistCriteria = "Introduce números válidos y valores mínimos/máximos coherentes. «Nunca reproducido» no puede combinarse con reproducción reciente ni con un mínimo positivo de reproducciones."
        , EditSmartPlaylist = "Editar lista inteligente"
        , LibraryEmptyHint = "Aún no hay ninguna fuente multimedia configurada. Abre Ajustes > Biblioteca, añade carpetas de música locales o conecta un servidor Orynivo."
        , SmartPlaylistUpdated = "Lista inteligente '{0}' actualizada."
        , ImportM3u8Playlist = "Importar lista M3U8"
        , ExportM3u8Playlist = "Exportar como M3U8"
        , SaveAlbumAsPlaylist = "Guardar como lista"
        , AlbumPath = "Ruta del álbum"
        , TrackInfo = "Información de la pista"
        , ShowTrackInfo = "Mostrar información de la pista"
        , PhysicalPath = "Ruta física del archivo"
        , UpNext = "A continuación"
        , GenreExplorer = "Nube de géneros"
        , GenreCloudHint = "Explora los géneros de tu biblioteca local y de todos los servidores Orynivo conectados. Selecciona un género para profundizar."
        , AllGenres = "Todos los géneros"
        , GenreRecommendations = "Pistas recomendadas"
        , GenreCloudEmpty = "Todavía no se encontraron géneros. Comprueba que tus pistas tengan etiquetas de género."
        , MoreGenres = "Más géneros"
        , PlayNext = "Reproducir a continuación"
        , AppendToQueue = "Añadir a la cola"
        , RemoveFromQueue = "Quitar de la cola"
        , MoveUp = "Subir"
        , MoveDown = "Bajar"
        , SaveQueueAsPlaylist = "Guardar cola como lista"
        , TracksQueuedNext = "{0:N0} pistas se reproducirán a continuación."
        , TracksAppendedToQueue = "{0:N0} pistas añadidas a la cola."
        , M3u8ImportCompleted = "Lista '{0}' importada: {1} entradas · faltan {2} archivos locales · {3} entradas HTTP · {4} omitidas."
        , M3u8ImportNoEntries = "El archivo M3U8 no contiene entradas importables."
        , M3u8ImportFailed = "Error al importar M3U8: {0}"
        , M3u8ExportCompleted = "Lista '{0}' exportada como M3U8: {1} entradas · {2} omitidas."
        , M3u8ExportFailed = "Error al exportar M3U8: {0}"
        , Integration      = "INTEGRACIÓN"
        , McpServer        = "Servidor MCP"
        , McpServerHint    = "Abre un servidor HTTP/SSE local que permite a los asistentes de IA (p. ej. Claude Desktop) controlar el reproductor y buscar en la biblioteca."
        , McpServerEnabled = "Activar servidor MCP"
        , McpServerPort    = "Puerto"
        , McpNetworkAccess = "Permitir acceso desde la red local"
        , McpNetworkAccessHint = "Vincula MCP a todas las interfaces de red. Las solicitudes de red requieren el token Bearer. Sin HTTPS o VPN, el token puede observarse en la red."
        , McpAccessToken = "Token de acceso"
        , McpAccessTokenWatermark = "Se genera automáticamente al activarlo"
        , McpGenerateToken = "Generar nuevo"
        , MobileRemote = "Mando web móvil"
        , MobileRemoteHint = "Ofrece un mando en la red local. Usa su token dedicado y protege el acceso externo mediante HTTPS o VPN."
        , MobileRemoteEnabled = "Activar el mando móvil en la red"
        , MobileRemotePort = "Puerto"
        , MobileRemoteToken = "Token de acceso del mando"
        , MobileRemoteNetworkAddress = "Dirección IP de la red doméstica"
        , MobileRemoteQrHint = "Guarda primero los ajustes. Escanea el QR en la misma Wi-Fi para acceder directamente. Contiene el token: ¡no lo compartas! La URL introducida manualmente solicita el token. Elige una dirección accesible desde el teléfono."
        , McpToolsHeader   = "Herramientas"
        , McpToolsHint     = "Activar o desactivar herramientas individuales."
        , WebBrowsing        = "Navegación web"
        , WebBrowsingHint    = "Ofrece a la IA un conjunto controlado de herramientas web: búsqueda con SearXNG y obtención segura de páginas. Se bloquean las direcciones privadas y locales (protección SSRF)."
        , WebBrowsingEnabled = "Activar herramientas web"
        , SearxngUrl         = "Dirección de SearXNG"
        , WebBlockPrivate    = "Bloquear direcciones privadas/locales (protección SSRF)"
        , WebMaxResults      = "Máximo de resultados de búsqueda"
        , WebTimeoutSeconds  = "Tiempo de espera (segundos)"
        , WebMaxResponseKb   = "Tamaño máximo de respuesta (KB)"
        , AiChat                = "Chat IA"
        , AiChatSettings        = "Chat IA"
        , AiChatHint            = "Se conecta a un modelo de IA local o en la nube a través de una API compatible con OpenAI (p. ej. LM Studio, Ollama, OpenAI). El modelo tiene acceso a las 32 herramientas de Orynivo para buscar la biblioteca, gestionar listas de reproducción y controlar la reproducción."
        , AiChatEnabled         = "Activar Chat IA"
        , AiChatEndpointUrl     = "URL del endpoint"
        , AiChatApiKey          = "Clave API (opcional)"
        , AiChatLocalNote       = "LM Studio y Ollama no requieren clave API."
        , AiChatModel           = "Modelo"
        , AiChatLoadModels      = "Cargar modelos"
        , AiChatAvailableModels = "Seleccionar un modelo disponible"
        , AiChatTestConnection  = "Probar conexión"
        , AiChatConnectionTesting = "Probando la conexión …"
        , AiChatModelsLoaded    = "Se cargaron {0} modelos."
        , AiChatConnectionSucceeded = "Conexión correcta; {0} modelos disponibles."
        , AiChatNoModels        = "La conexión fue correcta, pero el endpoint no informó de ningún modelo."
        , AiChatConnectionFailed = "Error de conexión. Comprueba la URL del endpoint y la clave API."
        , AiChatMaxTokens       = "Tokens máximos"
        , AiChatInputPlaceholder = "Haz una pregunta …"
        , AiChatSend            = "Enviar"
        , AiChatClear           = "Borrar"
        , AiChatCopy            = "Copiar"
        , AiChatNotEnabled      = "El Chat IA no está activado. Actívalo en Ajustes › Chat IA."
        , AiChatEmptyResponse   = "El modelo devolvió una respuesta vacía."
        , AiChatToolResultFallback = "El modelo no devolvió una respuesta final después de llamar a la herramienta. Resultado de la herramienta:"
    };

private static readonly LocalizedStrings Russian = new(
        "Локальная библиотека",
        "Исполнители",
        "Альбомы",
        "Треки",
        "Структура папок",
        "Поиск",
        "Плейлисты",
        "О программе",
        "Настройки",
        "Фильтр",
        "Избранное",
        "Типы аудио",
        "Битрейт",
        "Устройство не выбрано.",
        "Внешний вид",
        "Цветовая схема",
        "Язык",
        "Воспроизведение",
        "Устройство вывода",
        "Библиотека",
        "Папки",
        "Добавить папку",
        "Обслуживание базы данных",
        "Оптимизировать базу данных",
        "Восстановить обложки альбомов",
        "Загрузить отсутствующие обложки",
        "Автоматическая загрузка находит обложки только при наличии идентификатора MusicBrainz. Для произвольного поиска используйте кнопку на странице альбома.",
        "Обложка не найдена",
        "Искать обложку",
        "Поиск обложки",
        "Поиск подходящих обложек …",
        "Обложки не найдены.",
        "Название альбома для поиска",
        "Исполнитель (необязательно)",
        "Искать снова",
        "Использовать выбранную обложку",
        "Удалить обложку",
        "Заменить обложку",
        "Автор",
        "Лицензии",
        "Сохранить",
        "Отмена",
        "Таблица",
        "Обложка",
        "Неизвестно",
        "Исполнитель альбома",
        "Год",
        "Название",
        "Исполнитель",
        "Альбом",
        "Жанр",
        "Длительность",
        "Формат",
        "В треках не найдено: {0}.",
        "В альбомах не найдено: {0}.",
        "Среди исполнителей не найдено: {0}.",
        "Записей: {0:N0}",
        "Треков: {0:N0}",
        "Сначала дважды щёлкните трек.",
        "Воспроизведение остановлено",
        "Воспроизведение завершено",
        "Сначала выберите устройство ASIO в настройках.",
        "Сначала выберите устройство WASAPI в настройках.",
        "Функция «{0}» пока не реализована.",
        "Настройки сохранены.",
        "Не удалось прочитать сведения об устройстве: {0}",
        "Активные устройства вывода WASAPI не найдены.",
        "Драйверы ASIO не найдены.",
        "Выберите устройство и сохраните.",
        "Сканирование выполняется …",
        "Папка не найдена.",
        "Сканирование отменено.",
        "Оптимизация базы данных …",
        "Оптимизация завершена.",
        "Ошибка оптимизации: {0}",
        "Восстановление обложек альбомов …",
        "Восстановлено обложек: {0:N0}.",
        "Ошибка восстановления обложек: {0}",
        "Загрузка недостающих обложек …",
        "Загружено недостающих обложек: {0:N0}.",
        "Ошибка загрузки обложек: {0}",
        "Добавить в плейлист",
        "Новый плейлист",
        "Новый плейлист",
        "Название плейлиста",
        "Создать плейлист",
        "Трек добавлен в плейлист «{0}».",
        "В плейлист «{1}» добавлено треков: {0}.",
        "Удалить плейлист",
        "Удалить из плейлиста",
        "Плейлист «{0}» удалён.",
        "Трек удалён из плейлиста.",
        "Сохранить фильтры как смарт-плейлист",
        "Умный плейлист «{0}» сохранён.",
        "Сначала выберите фильтр.",
        "Резервная копия библиотеки",
        "Экспортирует базу данных, плейлисты, историю, обложки и список папок в ZIP. Аудиофайлы не включаются.",
        "Экспортировать библиотеку",
        "Импортировать библиотеку",
        "Экспорт библиотеки …",
        "Библиотека экспортирована в «{0}».",
        "Ошибка экспорта библиотеки: {0}",
        "Импорт заменит текущую библиотеку, плейлисты, историю и все изображения. Продолжить?",
        "Импорт библиотеки и перестроение поискового индекса …",
        "Библиотека импортирована. Orynivo будет закрыт, после чего его можно запустить снова.",
        "Ошибка импорта библиотеки: {0}",
        "Сначала завершите сканирование или обслуживание библиотеки.",
        "Библиотека Orynivo (*.zip)|*.zip",
        "Экспорт библиотеки: {0}% — {1}",
        "Импорт библиотеки: {0}% — {1}",
        "Тексты песен",
        "Показать текст",
        "Обновить текст песни",
        "Закрыть текст",
        "Загрузка текста песни …",
        "Загрузка текста песни из LRCLIB …",
        "Для этого трека нет метаданных.",
        "Текст песни не найден.",
        "Не удалось загрузить текст песни.",
        "Информация об исполнителе",
        "Показать информацию",
        "Обновить информацию",
        "Закрыть информацию об исполнителе",
        "Загрузка информации об исполнителе …",
        "Загрузка информации об исполнителе …",
        "Информация об исполнителе не найдена",
        "Не удалось загрузить информацию об исполнителе.",
        "Изображение не загружено",
        "Файл изображения отсутствует",
        "Не удалось загрузить изображение",
        "Источник: Wikipedia",
        "Источник: Last.fm",
        "Источник информации об исполнителе",
        "API-ключ Last.fm",
        "Создайте бесплатный API-ключ на last.fm/api/account/create",
        "API-ключ Fanart.tv",
        "Предпочитает отобранные изображения исполнителей. Ключ хранится зашифрованным в хранилище учётных данных текущего пользователя; также можно задать FANART_TV_API_KEY. Для уже загруженных исполнителей используйте «Обновить информацию». Создайте ключ на fanart.tv/get-an-api-key/.",
        "Загрузить отсутствующие изображения исполнителей",
        "Поочерёдно проверяет локальную библиотеку и все настроенные серверы Orynivo. Для каждого исполнителя сначала выполняется поиск на Fanart.tv (с API-ключом), затем в Wikimedia. Результаты Fanart.tv можно принимать автоматически; результаты Wikimedia всегда требуют подтверждения.",
        "Поиск изображения исполнителя {0}/{1}: {2} · {3}",
        "Принято изображений: {0:N0}, отклонено: {1:N0}; ошибок запросов: {2:N0}.",
        "Ошибка загрузки изображений исполнителей: {0}",
        "Загрузка изображений исполнителей отменена.",
        "Предложение {0}/{1}: {2} · {3}",
        "Загрузка исполнителей из локальной библиотеки и серверов …",
        "Оценка оставшегося времени …",
        "Примерно осталось: {0}",
        "Предлагаемое изображение исполнителя",
        "Источник: {0}",
        "Изображение будет сохранено только после вашего подтверждения.",
        "Принять",
        "Отклонить",
        "Показать все треки альбома",
        "Сбой Orynivo",
        "Произошла непредвиденная ошибка. Отчёт о сбое сохранён здесь:\n\n{0}\n\nOrynivo будет закрыт.",
        "Произошла непредвиденная ошибка. Не удалось сохранить отчёт о сбое. Orynivo будет закрыт.")
    {
        TrackInfo = "Информация о треке",
        ShowTrackInfo = "Показать информацию о треке",
        PhysicalPath = "Физический путь",
        ReleaseOutputDevice = "Освободить устройство вывода",
        ReacquireOutputDevice = "Занять устройство вывода",
        OutputDeviceReleased = "Устройство вывода освобождено",
        VersionLabel = "Версия {0}",
        CheckForUpdates = "Проверить обновления",
        Updates = "Обновления",
        CheckForUpdatesOnStartup = "Проверять обновления при запуске приложения",
        WindowBehavior = "Поведение окна",
        StartMaximized = "Запускать развёрнутым",
        CheckingForUpdates = "Проверка обновлений…",
        UpdateAvailable = "Доступна версия {0}.",
        UpToDate = "Установлена актуальная версия Orynivo.",
        UpdateUnavailable = "Проверенные обновления не настроены для этой сборки.",
        DownloadAndInstall = "Загрузить и установить",
        DownloadingUpdate = "Загрузка и проверка обновления…",
        InstallingUpdate = "Установка обновления…",
        UpdateFailed = "Не удалось проверить или установить обновление.",
        UpdateServer = "Обновить сервер",
        UpdatingServer = "Передача обновления…",
        ServerUpdateQueued = "Обновление сервера поставлено в очередь.",
        ServerUpdateUnavailable = "Нет более нового поддерживаемого обновления сервера.",
        ServerUpdateFailed = "Ошибка обновления сервера",
        ServerUpdateRejected = "Сервер отклонил обновление (HTTP {0})",
        UpdatingNamedServer = "Обновление сервера «{0}»…",
        ServerUpdatesFailedContinue = "Не удалось обновить следующие серверы: {0}. Продолжить обновление приложения?",
        OutputType = "Тип вывода",
        LocalMedia = "Локально",
        AsioOutputDevice = "Устройство вывода ASIO",
        CwAsioOutputDevice = "Устройство вывода cwASIO",
        SteinbergAsio = "Steinberg ASIO",
        CwAsio = "cwASIO",
        WasapiOutputDevice = "Устройство вывода WASAPI",
        AirPlay = "AirPlay 2",
        AirPlayOutputDevice = "Устройство вывода AirPlay 2",
        NoAirPlayDevices = "В локальной сети не найдены устройства AirPlay 2.",
        AirPlaySenderMissing = "Отсутствует нативный модуль AirPlay 2; можно использовать совместимую утилиту «raop_play».",
        SelectAirPlayDevice = "Сначала выберите устройство вывода AirPlay.",
        OpenAl = "OpenAL",
        OpenAlOutputDevice = "Устройство вывода OpenAL",
        DirectAlsa = "ALSA (прямой, эксклюзивный)",
        AlsaOutputDevice = "Устройство прямого вывода ALSA",
        LinuxDefaultAudioDevice = "Системное устройство по умолчанию (OpenAL)",
        OpenAlInitializationFailed = "OpenAL не удалось инициализировать системный аудиовыход.",
        AlsaExactOpenFailed = "Не удалось открыть устройство ALSA «{0}» на частоте {1} Гц без передискретизации: {2}",
        AlsaDeviceBusy = "Устройство прямого вывода ALSA «{0}» занято PipeWire или другим приложением. Перенаправление системного вывода не освобождает устройство. Отключите профиль звукового устройства в системе или выберите вывод OpenAL.",
        AlsaPrepareFailed = "ALSA не удалось подготовить устройство вывода после перемотки.",
        DeviceInfo = "Информация об устройстве",
        DatabaseOptimizeHint = "Освобождённые страницы удаляются, поэтому файл становится физически меньше.",
        GenreCloudCache = "Фоны облака жанров",
        GenreCloudCacheHint = "Очистить кэш мозаик исполнителей. Они будут созданы заново при следующем открытии уровня жанров.",
        GenreCloudCacheCleared = "Кэш фонов облака жанров очищен.",
        GenreCloudBackground = "Фон облака жанров",
        GenreCloudBackgroundHint = "Выберите фон или отключите его, чтобы снизить нагрузку на систему.",
        GenreCloudBackgroundNone = "Без фоновых изображений",
        GenreCloudBackgroundAlbums = "Обложки альбомов",
        GenreCloudBackgroundArtists = "Изображения исполнителей",
        GenreCloudVisibility = "Видимость изображений",
        ClearGenreCloudCache = "Очистить кэш фона",
        AppearanceNavItem = "Внешний вид",
        ArtistInfoNavItem = "Информация об исполнителе",
        NormalizeArtists = "Нормализовать имена исполнителей",
        NormalizeArtistsHint = "Удаляет добавления «feat.» и объединяет однозначные варианты пунктуации и пробелов. Аудиофайлы не изменяются.",
        ArtistsNormalizing = "Нормализация имён исполнителей и перестроение поискового индекса …",
        ArtistsNormalized = "Объединено вариантов исполнителей: {0:N0}; обновлено треков: {1:N0}.",
        ArtistNormalizationFailed = "Ошибка нормализации исполнителей: {0}",
        AsioBridgeMissing = "Эта сборка не включает поддержку ASIO. Используйте WASAPI.",
        KernelStreamingUnavailable = "Kernel Streaming можно выбрать, но воспроизведение через него пока не реализовано.",
        AddMusicDirectory = "Добавить музыкальную папку",
        TrackCountTooltip = "Количество треков в базе данных",
        Scan = "Сканировать",
        RefreshAllMetadata = "Повторно прочитать метаданные",
        RefreshAllMetadataHint = "Обновить анализ библиотеки без изменения файлов.",
        RemoveDirectory = "Удалить папку",
        ScanCompleted = "Готово: файлов {0} · новых {1} · обновлено {2} · удалено {3}{4}",
        ScanFailed = "Ошибка: {0}",
        StartupPreparingLibrary = "Подготовка библиотеки …",
        StartupCheckingSearchIndex = "Проверка поискового индекса …",
        SearchIndexRebuilding = "Перестроение поискового индекса в фоне ({0}/{1}) …",
        SearchIndexReady = "Поисковый индекс актуален.",
        SearchIndexFailed = "Не удалось обновить поисковый индекс: {0}",
        Back = "Назад",
        MarkAsFavorite = "Добавить в избранное",
        OpenAlbum = "Открыть альбом",
        OpenArtist = "Открыть исполнителя",
        ToggleFavorite = "Добавить в избранное / убрать из избранного",
        PlaybackThrough = "Воспроизведение через {0}",
        PlaybackThroughWithDsdConversion = "Воспроизведение через {0} · DSD преобразуется в PCM ({1:N0} Гц)",
        NativeDsdOutput = "Нативный DSD",
        DsdToPcmOutput = "DSD → PCM",
        DopOutput = "DSD через DoP",
        DopRequiresDirectAlsa = "Требуется совместимый с DoP ЦАП и точный вывод без передискретизации.",
        ReplayGain = "Регулировка громкости ReplayGain",
        ReplayGainHint = "Применяется к PCM. Режим трека предпочитает усиление трека, режим альбома — усиление альбома. Нативный DSD остаётся побитово точным.",
        ReplayGainOff = "Выкл.",
        ReplayGainTrack = "Трек",
        ReplayGainAlbum = "Альбом",
        CalculateReplayGainDuringScan = "Автоматически рассчитывать отсутствующий ReplayGain при сканировании локальной библиотеки (медленнее)",
        CalculateReplayGain = "Рассчитать отсутствующий ReplayGain",
        ReplayGainCalculating = "Расчёт ReplayGain …",
        ReplayGainCalculated = "ReplayGain рассчитан: обновлено треков — {0}.",
        ReplayGainCalculationFailed = "Ошибка расчёта ReplayGain: {0}",
        NonGaplessCrossfade = "Плавный переход для очередей без gapless (секунды)",
        NonGaplessCrossfadeHint = "0 отключает переход. Применяется только к смене очереди, которая ещё не обрабатывается gapless PCM-движком.",
        ReplayGainBadge = "RG",
        DsdPlayback = "Воспроизведение DSD",
        AlwaysConvertDsdToPcm = "Всегда преобразовывать DSD в PCM",
        AlwaysConvertDsdToPcmHint = "Использует путь PCM также для ASIO/cwASIO, чтобы применялись громкость, ReplayGain и эквалайзер. При отключении сохраняется битово точный вывод DSD.",
        DsdOverPcm = "Выводить DSD через DoP",
        DsdOverPcmHint = "Упаковывает DSD без потерь в PCM-кадры (DSD over PCM). Требуется DoP-совместимый ЦАП и точный вывод без передискретизации; громкость, ReplayGain и эквалайзер не действуют.",
        PcmOutputBoost = "Усилить PCM-вывод на +6 дБ",
        PcmOutputBoostHint = "Повышает громкость всех PCM-путей, приближая её к уровню нативного DSD. Нативный DSD остаётся битово точным и неизменным.",
        OutputDevicesLoading = "Загрузка устройств вывода …",
        Equalizer = "Параметрический эквалайзер",
        EqualizerHint = "Импорт профилей Equalizer APO и AutoEQ для PCM и воспроизведения DSD с преобразованием в PCM. Нативный DSD остаётся побитово точным.",
        EqualizerEnabled = "Включить эквалайзер",
        EqualizerImport = "Импортировать профиль APO/AutoEQ",
        EqualizerImporting = "Импорт профиля эквалайзера …",
        EqualizerImportTitle = "Импорт профиля Equalizer APO или AutoEQ",
        EqualizerNoProfile = "Профиль не импортирован.",
        EqualizerProfileSummary = "{0} · предусиление {1:+0.##;-0.##;0} дБ · фильтров: {2}",
        EqualizerImportFailed = "Не удалось импортировать профиль.",
        EqualizerProfileFileType = "Профиль Equalizer APO / AutoEQ",
        EqualizerPreamp = "Предусиление (дБ)",
        EqualizerFilterType = "Тип фильтра",
        EqualizerFrequency = "Частота (Гц)",
        EqualizerGain = "Усиление (дБ)",
        EqualizerQ = "Добротность Q",
        EqualizerAddFilter = "Добавить фильтр",
        EqualizerRemoveFilter = "Удалить фильтр",
        EqualizerPeak = "Пиковый",
        EqualizerLowShelf = "Низкочастотная полка",
        EqualizerHighShelf = "Высокочастотная полка",
        EqualizerLowPass = "Фильтр низких частот",
        EqualizerHighPass = "Фильтр высоких частот",
        EqualizerCreate = "Создать эквалайзер",
        EqualizerCreateTitle = "Создать новый эквалайзер",
        EqualizerName = "Название эквалайзера",
        EqualizerNameExists = "Эквалайзер с таким именем уже существует.",
        OutputProfile = "Вывод",
        OutputProfileCreate = "Создать устройство вывода",
        OutputProfileConfigure = "Настроить устройство вывода",
        OutputProfileDelete = "Удалить устройство вывода",
        OutputProfileCreateTitle = "Создать новый выход",
        OutputProfileConfigureTitle = "Настройка устройства вывода",
        OutputProfileName = "Название устройства вывода",
        OutputProfileNameExists = "Выход с таким именем уже существует.",
        OutputProfileDeleteTitle = "Удалить выход",
        OutputProfileDeleteConfirm = "Вы действительно хотите удалить выход «{0}»?",
        UserProfiles = "Профили пользователей",
        UserProfileActive = "Активный профиль",
        UserProfileCreate = "Создать профиль",
        UserProfileRename = "Переименовать профиль",
        UserProfileDelete = "Удалить профиль",
        UserProfileName = "Имя профиля",
        UserProfileMigrateFavorites = "Скопировать существующие личные данные (избранное, оценки и историю) в новый профиль?",
        UserProfileDeleteConfirm = "Удалить профиль «{0}»?",
        EqualizerDelete = "Удалить эквалайзер",
        EqualizerDeleteTitle = "Удаление эквалайзера",
        EqualizerDeleteConfirm = "Удалить эквалайзер «{0}»?",
        SelectColumns = "Выбрать столбцы",
        FileName = "Имя файла",
        FileSize = "Размер файла",
        AddedAt = "Добавлено",
        SampleRate = "Частота дискретизации",
        BitDepth = "Разрядность",
        Channels = "Каналы",
        TrackNumber = "Номер трека",
        DiscNumber = "Номер диска",
        Composer = "Композитор",
        Bpm = "BPM",
        ReplayGainTrackColumn = "ReplayGain трека",
        ReplayGainAlbumColumn = "ReplayGain альбома",
        PersonalRating = "Моя оценка",
        MusicBrainzRating = "Оценка MusicBrainz",
        MusicBrainzLoadRating = "Загрузить оценку",
        MusicBrainzLoadingRating = "Загрузка …",
        MusicBrainzRetryRating = "Повторить",
        MusicBrainzNoRating = "Нет оценки",
        RatingSetHint = "Установить личную оценку",
        RatingUpdateFailed = "Не удалось сохранить оценку.",
        Codec = "Кодек",
        Tags = "Теги",
        Homepage = "Домашняя страница",
        FeedUrl = "Адрес ленты",
        SearchResultSummary = "Треков: {0:N0} · альбомов: {1:N0} · исполнителей: {2:N0}",
        RecentAlbums = "Недавно добавленные альбомы",
        AlbumRecommendations = "Рекомендации альбомов",
        RecommendationMoodAll = "Все настроения",
        RecommendationMoodRelaxed = "Расслабленное",
        RecommendationMoodEnergetic = "Энергичное",
        RecommendationMoodHappy = "Радостное",
        RecommendationMoodMelancholic = "Меланхоличное",
        RecommendationNoMatches = "Пока недостаточно подходящей истории прослушивания для рекомендаций.",
        PlayMoreLikeThis = "Слушать похожее",
        PlayMoodMix = "Микс по настроению",
        SimilarTracksLoading = "Загрузка похожих треков …",
        SimilarTracksUnavailable = "Для этого трека нет данных о сходстве.",
        SimilarTracksNoMatches = "Похожие треки не найдены.",
        SimilarTracksQueued = "Готово к воспроизведению похожих треков: {0:N0}.",
        RecommendationListView = "Список",
        RecommendationStageView = "Сцена",
        MetadataProblems = "Проверка метаданных",
        MetadataProblemsHint = "Orynivo проверяет физические папки независимо от возможного разделения одного альбома на несколько записей. Дважды щёлкните запись для поиска подходящих изданий в MusicBrainz.",
        MetadataWorkflow = "1. Просмотрите результаты → 2. Выберите папку → 3. Сравните и подтвердите предложение",
        MetadataNoFindings = "Для выбранных фильтров замечаний пока нет.",
        MetadataInspectFiles = "Также проверить файлы (чтение и дубликаты контрольных сумм; медленнее)",
        MetadataQuickHint = "Страница проверяет сохранённые метаданные без чтения музыкальных файлов.",
        MetadataSelectHint = "Выберите папку для просмотра найденных проблем.",
        MetadataActionGuide = "«Определить альбом по папке» ищет названия, исполнителей и номера треков в MusicBrainz. Недостающий ReplayGain: Настройки → Воспроизведение. Изображения: страницы альбомов и исполнителей. Отсутствующие файлы и дубликаты проверяйте вручную. В библиотеку сохраняются только подтверждённые изменения; аудиофайлы не меняются.",
        MetadataRemoteReadOnly = "Эта запись сервера доступна только для просмотра. Исправление папок через MusicBrainz доступно лишь для локальных записей. Рассчитайте ReplayGain в разделе сервера Orynivo; добавьте изображения на страницах альбомов и исполнителей.",
        MetadataReviewGuide = "Выберите папку. Анализ ничего не изменяет и не удаляет файлы.",
        MetadataPhaseDatabase = "Загрузка сохранённых метаданных…",
        MetadataPhaseFolders = "Проверка папок и метаданных…",
        MetadataPhaseHashes = "Вычисление хешей возможных дубликатов…",
        MetadataPhaseServers = "Ожидание отчётов серверов. Сервер не сообщает оставшееся время; готовые результаты уже доступны.",
        MetadataPhaseReleases = "Загрузка изданий и списков треков из MusicBrainz…",
        MetadataPhaseSaving = "Сохранение подтверждённых исправлений и обновление поискового индекса…",
        MetadataRemaining = "Примерное оставшееся время этого этапа: {0}",
        MetadataRemainingUnknown = "Оставшееся время пока невозможно оценить",
        MetadataElapsed = "Прошло: {0}",
        IdentifyFolderAsAlbum = "Определить альбом по папке",
        MetadataFolder = "Папка",
        MetadataIssues = "Обнаруженные проблемы",
        MetadataTrackCount = "Треки",
        MetadataReviewTitle = "Проверка метаданных альбома",
        MetadataSearching = "Поиск в MusicBrainz по количеству и длительности треков…",
        MetadataNoMatch = "Достаточно подходящее издание не найдено.",
        MetadataSearchFailed = "MusicBrainz сейчас недоступен. Повторите поиск позже.",
        MetadataFoundReleases = "Подходящие издания",
        MetadataApplyCorrection = "Применить исправление",
        MetadataCorrectionPreview = "Предпросмотр исправления",
        MetadataCurrentValues = "Сейчас: название — исполнитель",
        MetadataProposedValues = "Предложено: название — исполнитель",
        MetadataRefreshAnalysis = "Обновить анализ",
        MetadataAlbumQuery = "Название альбома для поиска",
        MetadataArtistQuery = "Имя исполнителя для поиска",
        MetadataRepairSuccess = "Метаданные исправлены в библиотеке Orynivo.",
        MetadataIssueAlbums = "несогласованные названия альбомов",
        MetadataIssueArtists = "несогласованные исполнители альбомов",
        MetadataIssueMissingTitles = "отсутствуют названия треков",
        MetadataIssueMissingNumbers = "отсутствуют номера треков",
        MetadataIssueDuplicateNumbers = "повторяющиеся номера треков",
        MetadataIssueMissingReplayGain = "Без ReplayGain: {0}",
        MetadataIssueMissingMusicBrainzIds = "Без идентификатора MusicBrainz: {0}",
        MetadataSeverity = "Приоритет",
        MetadataSeverityAll = "Все приоритеты",
        MetadataIssueAll = "Все типы проблем",
        MetadataIssueReplayGain = "Отсутствует ReplayGain",
        MetadataIssueMusicBrainzIds = "Отсутствует идентификатор MusicBrainz",
        MetadataIssueIncompleteAlbum = "Неполный альбом (не хватает треков: {0})",
        MetadataIssueAlbumArtwork = "Отсутствует обложка альбома",
        MetadataIssueArtistImage = "Отсутствует изображение исполнителя",
        MetadataIssueMissingFiles = "Отсутствует исходных файлов: {0}",
        MetadataIssueUnreadableFiles = "Не читается исходных файлов: {0}",
        MetadataIssueLikelyDuplicates = "Возможных дубликатов файлов: {0}",
        MetadataIssueExactDuplicates = "Побайтово идентичных дубликатов: {0}",
        MetadataIssueAlternateRecordings = "Записей в другом файле или издании: {0}",
        MetadataIssueArtistNameVariants = "Вариантов написания имён исполнителей: {0}",
        MetadataSeverityInformation = "Информация",
        MetadataSeverityWarning = "Предупреждение",
        MetadataSeverityError = "Ошибка",
        MetadataDoctorSummary = "{0} ошибок · {1} предупреждений · {2} информационных сообщений",
        MetadataAnalysisFailed = "Ошибка анализа. Подробности сохранены в журнал ошибок.",
        MetadataDoctorServersUnavailable = "Недоступны или ещё не поддерживают проверку библиотеки: {0} серверов",
        MetadataAnalysisCancelled = "Анализ отменён.",
        Calendar = "Календарь — {0}",
        TopGenres = "Самые прослушиваемые жанры",
        TopAlbums = "Самые прослушиваемые альбомы",
        TopArtists = "Самые прослушиваемые исполнители",
        ListeningStats = "Статистика прослушивания",
        PeriodAllTime = "За всё время",
        PeriodThisYear = "Этот год",
        PeriodThisMonth = "Этот месяц",
        PeriodLast30Days = "Последние 30 дней",
        PeriodLast7Days = "Последние 7 дней",
        HistorySourceRemote = "Удалённый источник",
        HistorySourcePlex = "Plex",
        NoData = "Нет данных.",
        LibraryUpdating = "Обновление библиотеки…",
        LibraryUpdatingWithCount = "Обновление библиотеки… {0} / {1} файлов",
        RefreshView = "Обновить",
        LibraryDataAvailable = "Доступны новые данные библиотеки",
        ServerUnreachable = "Недоступен",
        ServerLastConnected = "Последнее подключение: {0}",
        ServerNeverConnected = "Подключений ещё не было",
        ServerMissingFeatures = "Сервер не поддерживает: {0}",
        CapabilityTrackFacets = "Фильтры треков",
        CapabilityRecentAlbums = "Недавние альбомы",
        CapabilityWaveforms = "Звуковые волны",
        RemoteCache = "Удалённый кэш",
        RemoteCacheSize = "Размер кэша: {0}",
        ClearRemoteCacheAll = "Очистить весь кэш",
        ClearCache = "Очистить кэш",
        RemoteScanning = "Обновление {0}…",
        RemoteScanningWithCount = "Обновление {0}… {1} / {2} файлов",
        SmartPlaylistPreviewCount = "Подходящих треков: {0}",
        SmartPlaylistPreviewComputing = "Вычисление…",
        SmartPlaylistPreviewInvalid = "Неверные условия",
        RestoreQueue = "Последняя очередь",
        RestoreQueueTooltip = "Восстановить последнюю очередь воспроизведения",
        NoPreviousQueue = "Предыдущая очередь отсутствует.",
        ClearQueue = "Очистить очередь",
        QueueCleared = "Очередь очищена.",
        RecentlyPlayed = "Недавно прослушанные",
        GreetingMorning = "Доброе утро",
        GreetingAfternoon = "Добрый день",
        GreetingEvening = "Добрый вечер",
        DashboardTagline = "Ваш музыкальный обзор",
        DashboardWelcomeBack = "С возвращением",
        DashboardHeroHint = "Недавно добавленные альбомы и статистика прослушивания.",
        DashboardRandomPlayback = "Случайное воспроизведение",
        InfiniteMixStart = "Запустить бесконечный микс",
        GenreCloudInfiniteMix = "Бесконечный микс из облака жанров",
        InfiniteMixStop = "Остановить бесконечный микс",
        InfiniteMixActive = "Бесконечный микс активен",
        InfiniteMixCalculating = "Расчёт бесконечного микса …",
        InfiniteMixSettingsTitle = "Настройки бесконечного микса",
        InfiniteMixSettingsHint = "Настройте настроение, источники и период истории.",
        InfiniteMixMood = "Настроение",
        InfiniteMixMoodCalm = "Спокойное",
        InfiniteMixMoodBalanced = "Сбалансированное",
        InfiniteMixMoodEnergetic = "Энергичное",
        InfiniteMixDiscovery = "Новизна",
        InfiniteMixFamiliar = "Знакомое",
        InfiniteMixAdventurous = "Необычное",
        InfiniteMixPeriod = "Период истории",
        InfiniteMixSources = "Источники",
        InfiniteMixWeightFavorites = "Учитывать избранное",
        InfiniteMixPreferRare = "Предпочитать редкие треки",
        InfiniteMixIncludeGenres = "Включаемые жанры",
        InfiniteMixExcludeGenres = "Исключаемые жанры",
        InfiniteMixGenresWatermark = "Введите жанр…",
        InfiniteMixAddGenre = "Добавить жанр",
        InfiniteMixRemoveGenre = "Удалить жанр",
        InfiniteMixPaused = "Бесконечный микс приостановлен",
        InfiniteMixPause = "Пауза микса",
        InfiniteMixResume = "Продолжить микс",
        InfiniteMixAdjust = "Настроить микс",
        InfiniteMixReplaceNext = "Выбрать другое предложение",
        InfiniteMixMoreLikeThis = "Больше похожего",
        InfiniteMixLessLikeThis = "Меньше похожего",
        InfiniteMixExcludeTrack = "Исключить трек",
        DashboardQuickAccess = "Быстрый доступ",
        DashboardTotalMinutes = "Всего минут",
        DashboardMinutesShort = "мин",
        PeriodPrevious = "по сравнению с предыдущим периодом",
        ShowAll = "Показать всё",
        DevicePcmSampleRates = "Поддерживаемые частоты PCM",
        DeviceDsdRates = "Частоты DSD",
        DevicePcmFormats = "Форматы вывода PCM",
        DeviceDsdFormats = "Форматы вывода DSD",
        DeviceChannelSummary = "Выходных каналов: {0} · входных каналов: {1}",
        DeviceBufferSummary = "Буфер: мин. {0}, рекомендуемый {1}, макс. {2}, шаг {3}",
        DriverProvidedNoInformation = "Драйвер не предоставил информацию.",
        DsdSupportedWithoutFormats = "Режим DSD поддерживается; конкретные форматы каналов не указаны.",
        Unsupported = "Не поддерживается.",
        DeviceProbeInconclusive = "Не удалось однозначно проверить. Возможно, устройство используется другим приложением.",
        WasapiEndpointSummary = "Устройство WASAPI · каналов: {0}\nФормат микширования: {1} · {2} бит",
        WasapiNoExclusiveFormats = "Эксклюзивные форматы PCM не обнаружены.",
        WasapiDsdNotRelevant = "Не применимо к WASAPI в этом проигрывателе.",
        LinuxAlsaEndpointSummary = "Устройство прямого вывода ALSA · каналов: {0}\nPCM: {1} бит · исходная частота трека\nПередискретизация ALSA отключена",
        LinuxOpenAlEndpointSummary = "Устройство OpenAL · каналов: {0}\nPCM: {1} бит · частота микшера определяется при воспроизведении",
        LinuxDsdOutputUnavailable = "Недоступно для этого пути вывода PCM.",
        NativeDsdUsesAsio = "Нативное воспроизведение DSD в этом проигрывателе использует ASIO.",
        Dashboard = "Панель управления",
        DashboardIntroTitle = "Ваш музыкальный обзор",
        DashboardIntroHint = "Недавно добавленные альбомы, история прослушивания и любимые жанры — в одном месте.",
        ArtistsIntroTitle = "Исполнители",
        ArtistsIntroHint = "Просматривайте библиотеку по исполнителям, открывайте альбомы и управляйте избранным и изображениями исполнителей.",
        AlbumsIntroTitle = "Альбомы",
        AlbumsIntroHint = "Переключайтесь между табличным и графическим представлением, открывайте треки альбомов и добавляйте отсутствующие обложки.",
        TracksIntroTitle = "Треки",
        TracksIntroHint = "Ищите, фильтруйте и воспроизводите локальную музыкальную библиотеку по жанру, формату и битрейту.",
        FoldersIntroTitle = "Структура папок",
        FoldersIntroHint = "Просматривайте музыку через настроенные папки библиотеки и воспроизводите треки из их контекста.",
        ThemeLight = "Светлая",
        ThemeDark = "Тёмная",
        StatusAvailable = "Доступно",
        StatusUnavailable = "Недоступно",
        StatusEnabled = "Включено",
        StatusDisabled = "Отключено",
        StatusReady = "Готово",
        StatusChecking = "Проверка …",
        LanguageGerman = "Немецкий",
        LanguageEnglish = "Английский",
        LanguageFrench = "Французский",
        LanguageSpanish = "Испанский",
        LanguageRussian = "Русский",
        LanguageChineseSimplified = "Китайский (упрощённый)",
        PcmIntegerFormat = "{0}-битный PCM, little endian ({1})",
        PcmContainerFormat = "{0}-битный PCM в {1}-битном контейнере, little endian ({2})",
        PcmFloatFormat = "{0}-битный PCM с плавающей точкой, little endian ({1})",
        NativeDsdLsbFormat = "Нативный DSD, 1-битные данные, первый отсчёт в младшем бите ({0})",
        NativeDsdMsbFormat = "Нативный DSD, 1-битные данные, первый отсчёт в старшем бите ({0})",
        NativeDsdWordFormat = "Нативный DSD, 8-битные слова без значимого порядка байтов ({0})",
        CountEntrySingular = "Записей: {0:N0}",
        CountTrackSingular = "Треков: {0:N0}",
        Streaming = "Потоковое воспроизведение",
        StreamingServices = "Сервисы потоковой передачи",
        Qobuz = "Qobuz",
        QobuzApplicationId = "Идентификатор приложения Qobuz",
        QobuzIntegrationHint = "Интеграция Qobuz подготовлена. Каталог и воспроизведение будут доступны после утверждения доступа партнёра.",
        QobuzCredentialsHint = "Секретные данные и токены входа не хранятся в settings.json. Windows защищает их для текущего пользователя.",
        SearchArtistImage = "Найти изображение исполнителя",
        UploadArtistImage = "Загрузить изображение исполнителя",
        DeleteArtistImage = "Удалить изображение исполнителя",
        UploadCover = "Загрузить обложку",
        ImageFileType = "Файлы изображений",
        ArtistImageSearchTitle = "Поиск изображения исполнителя",
        ArtistImageSearchRunning = "Поиск подходящих изображений исполнителя …",
        ArtistImageSearchNoResults = "Изображения исполнителя не найдены.",
        ArtistImageSearchQuery = "Поисковый запрос",
        ArtistImageSearchFailed = "Ошибка поиска изображений исполнителя.",
        UseSelectedArtistImage = "Использовать выбранное изображение",
        ArtistImageDownloadFailed = "Не удалось сохранить выбранное изображение исполнителя.",
        ArtistProfileSearchTitle = "Обновить информацию об исполнителе",
        ArtistProfileSearchHint = "При необходимости измените имя, по которому Wikipedia или Last.fm ищет информацию об исполнителе. Имя в вашей библиотеке не изменится.",
        ArtistProfileSearchQuery = "Имя для поиска информации",
        ArtistProfileSearchLoad = "Загрузить информацию",
        EditArtistName = "Изменить имя исполнителя",
        ArtistName = "Имя исполнителя",
        RenameArtist = "Переименовать",
        MergeArtistsTitle = "Объединить исполнителей",
        ArtistNameExistsMessage = "Исполнитель «{0}» уже существует. Объединить обоих исполнителей? Выберите, какую запись и информацию сохранить.",
        KeepArtistProfile = "Сохранить «{0}» и объединить",
        ArtistRenameFailed = "Не удалось переименовать или объединить исполнителя.",
        Shuffle = "Перемешать",
        SearchLyrics = "Поиск текста песни",
        LyricsSearchTitle = "Поиск текста песни",
        LyricsSearchRunning = "Поиск подходящих текстов песен …",
        LyricsSearchNoResults = "Подходящие тексты песен не найдены.",
        LyricsSearchFailed = "Ошибка поиска текста песни.",
        UseSelectedLyrics = "Использовать выбранный текст",
        SelectLyricsResult = "Выберите текст слева для предпросмотра.",
        SynchronizedLyrics = "Синхронизированный",
        InternetRadio = "Интернет-радио",
        OwnRadios = "МОИ РАДИОСТАНЦИИ",
        RadioDirectory = "Поиск радиостанций",
        RadioDirectoryHint = "Ищите станции в бесплатном каталоге Radio Browser и добавляйте их в свои радиостанции.",
        RadioSearch = "Искать станции",
        RadioStation = "Станция",
        Country = "Страна",
        PlayRadio = "Воспроизвести",
        AddToOwnRadios = "Добавить в мои радиостанции",
        DeleteRadio = "Удалить радиостанцию",
        RadioLoading = "Загрузка радиостанций …",
        RadioNoResults = "Радиостанции не найдены.",
        RadioEmptyState = "Пока нет собственных радиостанций.",
        OwnRadiosEmptyHint = "Добавьте станции из каталога Radio Browser, чтобы они появились здесь.",
        RadioAdded = "Радиостанция «{0}» добавлена.",
        RadioDeleted = "Радиостанция «{0}» удалена.",
        RadioSearchFailed = "Не удалось найти радиостанции.",
        RadioNowPlaying = "Сейчас играет",
        RadioMetadataUnavailable = "Метаданные станции недоступны",
        RadioGenres = "Жанры",
        ClearFilter = "Сбросить фильтр",
        Podcasts = "Подкасты",
        MyPodcasts = "МОИ ПОДКАСТЫ",
        PodcastDirectory = "Поиск подкастов",
        PodcastDirectoryHint = "Ищите подкасты в каталоге и добавляйте их в свои подписки.",
        PodcastSearch = "Искать подкасты",
        Podcast = "Подкаст",
        PodcastAuthor = "Автор",
        PlayLatestEpisode = "Воспроизвести последний выпуск",
        AddToMyPodcasts = "Добавить в мои подкасты",
        DeletePodcast = "Удалить подкаст",
        PodcastLoading = "Загрузка подкастов …",
        PodcastNoResults = "Подкасты не найдены.",
        PodcastEmptyState = "Пока нет подкастов.",
        MyPodcastsEmptyHint = "Добавьте подкасты из каталога, чтобы они появились здесь.",
        PodcastAdded = "Подкаст «{0}» добавлен.",
        PodcastDeleted = "Подкаст «{0}» удалён.",
        PodcastSearchFailed = "Не удалось найти подкасты.",
        PodcastFeedFailed = "Не удалось загрузить ленту подкаста.",
        ShowEpisodes = "Показать выпуски",
        Published = "Опубликовано",
        Progress = "Прогресс",
        PodcastStatus = "Статус",
        PodcastUnplayed = "Не прослушано",
        PodcastInProgress = "В процессе",
        PodcastPlayed = "Прослушано",
        PodcastEpisodesLoading = "Загрузка выпусков …",
        PodcastNoEpisodes = "Выпуски не найдены.",
        PodcastCategories = "Категории",
        PodcastLanguages = "Языки",
        PodcastLanguage = "Язык",
        PodcastLanguagesLoading = "Загрузка языков …",
        PodcastOverview = "Обзор подкаста",
        PodcastEpisodeTotal = "Всего выпусков: {0:N0}",
        PodcastEpisodeUnheard = "Ещё не прослушано: {0:N0}",
        PodcastEpisodeStarted = "Начато: {0:N0}",
        PodcastLatestEpisode = "Последний выпуск: {0}",
        DailyHistoryTitle = "История прослушивания — {0}",
        PlayedAt = "Время воспроизведения",
        ListenedDuration = "Прослушано",
        MediaType = "Тип",
        Close = "Закрыть",
        DailyHistoryNoEntries = "За этот день нет записей о воспроизведении.",
        SidebarSections = "Разделы боковой панели",
        SidebarSectionsHint = "Выберите сворачиваемые разделы, отображаемые в главной навигации.",
        PodcastInfo = "Информация о подкасте",
        ShowPodcastInfo = "Показать информацию о подкасте",
        ClosePodcastInfo = "Закрыть информацию о подкасте",
        PodcastPublishedOn = "Опубликовано: {0}",
        PodcastEpisodeDuration = "Длительность: {0}",
        PodcastDescriptionUnavailable = "Описание недоступно",
        PlexServers = "PLEX-СЕРВЕРЫ",
        PlexServersSettings = "Серверы Plex",
        PlexServersHint = "Настройте один или несколько Plex Media Server.",
        AddPlexServer = "Добавить сервер Plex",
        PlexServerDialogTitle = "Сервер Plex",
        PlexServerName = "Название сервера",
        PlexServerUrl = "URL сервера",
        PlexToken = "Токен Plex",
        PlexTestConnection = "Проверить соединение",
        PlexTestingConnection = "Проверка подключения…",
        PlexConnectionSuccessful = "Подключение установлено. Найдено аудиобиблиотек: {0:N0}.",
        PlexConnectionFailed = "Ошибка подключения: {0}",
        PlexServerFieldsRequired = "Укажите имя и URL сервера.",
        PlexServerUrlInvalid = "Введите корректный URL HTTP или HTTPS.",
        PlexEditServer = "Изменить",
        PlexRemoveServer = "Удалить",
        PlexNoAudioLibraries = "Аудиобиблиотеки не найдены.",
        PlexLoading = "Загрузка содержимого Plex…",
        OrynivoServers = "Серверы Orynivo",
        SourceColumn = "Источник",
        LocalSource = "Локально",
        LocalSourceShort = "L",
        OrynivoServersSettings = "Серверы Orynivo",
        OrynivoServersHint = "Подключите проигрыватель к одному или нескольким серверам Orynivo в локальной сети.",
        AddOrynivoServer = "Добавить сервер",
        OrynivoServerDialogTitle = "Сервер Orynivo",
        OrynivoServerName = "Отображаемое имя",
        OrynivoServerUrl = "URL сервера (например, http://192.168.1.10:5280)",
        OrynivoServerApiKey = "API-ключ",
        OrynivoTestConnection = "Проверить соединение",
        OrynivoTestingConnection = "Проверка подключения…",
        OrynivoConnectionSuccessful = "Подключено. Сервер: {0} v{1}",
        OrynivoConnectionFailed = "Ошибка подключения. Проверьте URL и API-ключ.",
        OrynivoServerFieldsRequired = "Укажите имя, URL и API-ключ.",
        OrynivoEditServer = "Изменить",
        OrynivoRemoveServer = "Удалить",
        OrynivoLoading = "Загрузка содержимого сервера…",
        OrynivoServerDirectories = "Папки с музыкой на сервере",
        OrynivoLoadServerDirectories = "Загрузить с сервера",
        OrynivoAddServerDirectory = "Добавить папку",
        OrynivoLoadingServerDirectories = "Загрузка папок сервера…",
        OrynivoServerDirectoriesLoaded = "Папки сервера загружены.",
        OrynivoServerDirectoriesLoadFailed = "Не удалось загрузить папки сервера.",
        OrynivoSavingServerDirectories = "Сохранение папок сервера…",
        OrynivoServerDirectoriesSaveFailed = "Не удалось сохранить папки сервера.",
        OrynivoCalculateReplayGainDuringScan = "Рассчитывать отсутствующий ReplayGain при сканировании сервера (медленнее)",
        OrynivoSavingReplayGainSettings = "Сохранение настройки ReplayGain для сканирования на сервере…",
        OrynivoReplayGainSettingsSaveFailed = "Не удалось сохранить настройку ReplayGain для сканирования на сервере.",
        OrynivoReplayGainSettingsUnsupported = "Папки загружены. Этот сервер пока не поддерживает настройку ReplayGain для сканирования.",
        OrynivoNoServerDirectories = "Папки сервера не настроены.",
        OrynivoServerDirectoryBrowserTitle = "Выбрать папку сервера",
        OrynivoServerDirectoryRoots = "Папки с музыкой на сервере",
        OrynivoServerDirectoryUp = "На уровень вверх",
        OrynivoSelectServerDirectory = "Выбрать",
        OrynivoServerDirectoryLoading = "Загрузка папки…",
        OrynivoServerDirectoryLoadFailed = "Не удалось загрузить папку.",
        OrynivoServerDirectoryEmpty = "Нет вложенных папок.",
        OrynivoServerScan = "Сканирование сервера",
        OrynivoStartServerScan = "Сканировать библиотеку",
        OrynivoCalculateServerReplayGain = "Рассчитать ReplayGain",
        OrynivoServerScanStarting = "Запуск сканирования сервера …",
        OrynivoServerScanStartFailed = "Не удалось запустить сканирование сервера.",
        OrynivoServerScanIdle = "Сканирование сервера не выполняется.",
        OrynivoServerScanDiscovering = "Поиск файлов: {0}",
        OrynivoServerScanProgress = "{0}/{1} · {2}",
        OrynivoServerScanCompleted = "Сканирование завершено: файлов {0}, добавлено {1}, обновлено {2}, удалено {3}, ошибок {4}.",
        OrynivoServerScanFailed = "Ошибка сканирования сервера: {0}",
        OrynivoServerBackup = "Резервная копия библиотеки сервера",
        OrynivoDownloadBackup = "Скачать резервную копию",
        OrynivoRestoreBackup = "Восстановить резервную копию",
        OrynivoBackupDownloading = "Загрузка резервной копии сервера…",
        OrynivoBackupDownloaded = "Резервная копия сервера сохранена: {0}",
        OrynivoBackupRestoring = "Проверка и восстановление резервной копии сервера…",
        OrynivoBackupRestored = "Резервная копия сервера успешно восстановлена.",
        OrynivoBackupFailed = "Ошибка резервного копирования сервера: {0}",
        OrynivoRestoreBackupConfirm = "Импорт заменит базу данных сервера, плейлисты, историю, обложки, изображения исполнителей и список папок. Аудиофайлы не изменятся. Продолжить?",
        LoadMore = "Загрузить ещё",
        FfmpegDownloading = "Загрузка FFmpeg …",
        FfmpegDownloadFailed = "Не удалось загрузить FFmpeg. Установите его вручную: ffmpeg.org",
        SmartPlaylistDialogTitle = "Изменить умный плейлист",
        SmartPlaylistName = "Название",
        SmartPlaylistBasicFilters = "Основные фильтры",
        SmartPlaylistGenres = "Жанры (через запятую)",
        SmartPlaylistFormats = "Форматы (например, FLAC, MP3; через запятую)",
        SmartPlaylistBitrates = "Битрейты в кбит/с (через запятую)",
        SmartPlaylistSources = "Источники (local или server:<id>; через запятую)",
        SmartPlaylistMetadata = "Метаданные",
        SmartPlaylistMinimumYear = "Год от",
        SmartPlaylistMaximumYear = "Год до",
        SmartPlaylistSearchText = "Текст поиска содержит",
        SmartPlaylistArtistContains = "Исполнитель содержит",
        SmartPlaylistAlbumContains = "Альбом содержит",
        SmartPlaylistMinimumDuration = "Минимальная длительность в минутах",
        SmartPlaylistMaximumDuration = "Максимальная длительность в минутах",
        SmartPlaylistHistory = "История библиотеки и воспроизведения",
        SmartPlaylistAddedWithinDays = "Добавлено за последние X дней",
        SmartPlaylistPlayedWithinDays = "Прослушано за последние X дней",
        SmartPlaylistNeverPlayed = "Никогда не воспроизводилось",
        SmartPlaylistMinimumPlayCount = "Минимальное число воспроизведений",
        SmartPlaylistMaximumPlayCount = "Максимальное число воспроизведений",
        SmartPlaylistResult = "Результат",
        SmartPlaylistSortOrder = "Порядок",
        SmartPlaylistSortTitle = "По названию А–Я",
        SmartPlaylistSortRandom = "Случайный",
        SmartPlaylistSortLastPlayed = "Недавно прослушанные сначала",
        SmartPlaylistSortLeastRecentlyPlayed = "Давно не прослушанные сначала",
        SmartPlaylistResultLimit = "Максимум треков (пусто = без ограничений)",
        CreateSmartPlaylist = "Создать умный плейлист",
        InvalidSmartPlaylistCriteria = "Введите корректные числа и согласованные минимальные и максимальные значения. Условие «Никогда не воспроизводилось» нельзя сочетать с недавним прослушиванием или положительным минимумом воспроизведений.",
        EditSmartPlaylist = "Изменить умный плейлист",
        LibraryEmptyHint = "Источники музыки ещё не настроены. Откройте Настройки → Библиотека, добавьте локальные музыкальные папки или подключите сервер Orynivo.",
        SmartPlaylistUpdated = "Умный плейлист «{0}» обновлён.",
        ImportM3u8Playlist = "Импортировать плейлист M3U8",
        ExportM3u8Playlist = "Экспортировать в M3U8",
        SaveAlbumAsPlaylist = "Сохранить как плейлист",
        AlbumPath = "Путь к альбому",
        UpNext = "Далее",
        GenreExplorer = "Облако жанров",
        GenreCloudHint = "Изучайте жанры локальной библиотеки и подключённых серверов Orynivo. Выберите жанр, чтобы перейти глубже.",
        AllGenres = "Все жанры",
        GenreRecommendations = "Рекомендуемые треки",
        GenreCloudEmpty = "Жанры пока не найдены. Проверьте, есть ли теги жанров в ваших треках.",
        MoreGenres = "Другие жанры",
        PlayNext = "Воспроизвести следующим",
        AppendToQueue = "Добавить в конец очереди",
        RemoveFromQueue = "Удалить из очереди",
        MoveUp = "Переместить вверх",
        MoveDown = "Переместить вниз",
        SaveQueueAsPlaylist = "Сохранить очередь как плейлист",
        TracksQueuedNext = "Следующими будут воспроизведены треки: {0:N0}.",
        TracksAppendedToQueue = "В конец очереди добавлено треков: {0:N0}.",
        M3u8ImportCompleted = "Плейлист «{0}» импортирован: записей {1} · отсутствует локальных файлов {2} · HTTP-записей {3} · пропущено {4}.",
        M3u8ImportNoEntries = "Файл M3U8 не содержит записей для импорта.",
        M3u8ImportFailed = "Ошибка импорта M3U8: {0}",
        M3u8ExportCompleted = "Плейлист «{0}» экспортирован в M3U8: записей {1} · пропущено {2}.",
        M3u8ExportFailed = "Ошибка экспорта M3U8: {0}",
        McpServer = "MCP-сервер",
        McpServerHint = "Предоставляет MCP-интерфейс для управления проигрывателем и библиотекой.",
        McpServerEnabled = "Включить MCP-сервер",
        McpServerPort = "Порт",
        McpNetworkAccess = "Разрешить доступ из локальной сети",
        McpNetworkAccessHint = "Привязывает MCP ко всем сетевым интерфейсам. Для сетевых запросов требуется bearer-токен. Без HTTPS или VPN токен может быть перехвачен в сети.",
        McpAccessToken = "Токен доступа",
        McpAccessTokenWatermark = "Создаётся автоматически при включении",
        McpGenerateToken = "Создать новый",
        MobileRemote = "Мобильный веб-пульт",
        MobileRemoteHint = "Предоставляет пульт управления в локальной сети. Используйте отдельный токен доступа и защищайте доступ извне домашней сети с помощью HTTPS или VPN.",
        MobileRemoteEnabled = "Включить мобильный веб-пульт",
        MobileRemotePort = "Порт",
        MobileRemoteToken = "Токен удалённого доступа",
        MobileRemoteNetworkAddress = "IP-адрес домашней сети",
        MobileRemoteQrHint = "Сначала сохраните настройки. Отсканируйте QR-код в той же сети Wi-Fi для прямого входа. Он содержит токен доступа: не передавайте его другим! При ручном открытии URL потребуется токен. Если показано несколько адресов, выберите доступный с телефона.",
        Integration = "Интеграция",
        McpToolsHeader = "Инструменты",
        McpToolsHint = "Включайте или отключайте отдельные инструменты.",
        WebBrowsing = "Веб-доступ",
        WebBrowsingHint = "Предоставляет ИИ контролируемый набор веб-инструментов: поиск SearXNG и безопасная загрузка страниц.",
        WebBrowsingEnabled = "Включить веб-инструменты",
        SearxngUrl = "URL SearXNG",
        WebBlockPrivate = "Блокировать частные и локальные адреса (защита SSRF)",
        WebMaxResults = "Максимум результатов поиска",
        WebTimeoutSeconds = "Тайм-аут (секунды)",
        WebMaxResponseKb = "Максимальный размер ответа (КБ)",
        AiChat = "Чат с ИИ",
        AiChatSettings = "Настройки AI-чата",
        AiChatHint = "Подключается к локальной или облачной AI-модели через OpenAI-совместимый API.",
        AiChatEnabled = "Включить AI-чат",
        AiChatEndpointUrl = "URL конечной точки",
        AiChatApiKey = "API-ключ (необязательно)",
        AiChatLocalNote = "LM Studio и Ollama не требуют API-ключ.",
        AiChatModel = "Модель",
        AiChatLoadModels = "Загрузить модели",
        AiChatAvailableModels = "Выберите доступную модель",
        AiChatTestConnection = "Проверить соединение",
        AiChatConnectionTesting = "Проверка соединения …",
        AiChatModelsLoaded = "Загружено моделей: {0}.",
        AiChatConnectionSucceeded = "Подключение установлено; доступно моделей: {0}.",
        AiChatNoModels = "Модели не найдены",
        AiChatConnectionFailed = "Не удалось установить соединение",
        AiChatMaxTokens = "Максимум токенов",
        AiChatInputPlaceholder = "Задайте вопрос …",
        AiChatSend = "Отправить",
        AiChatClear = "Очистить чат",
        AiChatCopy = "Копировать",
        AiChatNotEnabled = "Чат с ИИ отключён. Включите его в настройках.",
        AiChatEmptyResponse = "Модель вернула пустой ответ.",
        AiChatToolResultFallback = "После вызова инструмента модель не вернула окончательный ответ. Результат инструмента:",
        AutoAcceptFanartTvImages = "Автоматически принимать результаты Fanart.tv"
    };

private static readonly LocalizedStrings ChineseSimplified = new(
        "本地媒体库",
        "艺术家",
        "专辑",
        "曲目",
        "文件夹结构",
        "搜索",
        "播放列表",
        "关于",
        "设置",
        "筛选",
        "收藏",
        "音频类型",
        "比特率",
        "未选择设备。",
        "外观",
        "配色方案",
        "语言",
        "播放",
        "输出设备",
        "媒体库",
        "目录",
        "添加目录",
        "数据库维护",
        "优化数据库",
        "修复专辑封面",
        "下载缺失的封面",
        "仅在存在 MusicBrainz ID 时才能自动查找封面。若要自由搜索，请使用专辑视图中的按钮。",
        "未找到封面",
        "搜索封面",
        "搜索封面",
        "正在搜索匹配的封面…",
        "未找到封面。",
        "专辑搜索词",
        "艺术家（可选）",
        "再次搜索",
        "使用所选封面",
        "删除封面",
        "更换封面",
        "作者",
        "许可证",
        "保存",
        "取消",
        "表格",
        "封面",
        "未知",
        "专辑艺术家",
        "年份",
        "标题",
        "艺术家",
        "专辑",
        "流派",
        "时长",
        "格式",
        "曲目中未找到搜索词 {0}。",
        "专辑中未找到搜索词 {0}。",
        "艺术家中未找到搜索词 {0}。",
        "{0:N0} 个条目",
        "{0:N0} 首曲目",
        "请先双击一首曲目。",
        "播放已停止",
        "播放已完成",
        "请先在设置中选择 ASIO 设备。",
        "请先在设置中选择 WASAPI 设备。",
        "尚未实现 {0}。",
        "设置已保存。",
        "无法读取设备信息：{0}",
        "未找到可用的 WASAPI 输出设备。",
        "未找到 ASIO 驱动程序。",
        "请选择设备并保存。",
        "正在扫描 …",
        "未找到文件夹。",
        "扫描已取消。",
        "正在优化数据库…",
        "优化完成。",
        "优化失败：{0}",
        "正在修复专辑封面…",
        "已修复 {0:N0} 张专辑封面。",
        "封面修复失败：{0}",
        "正在下载缺失的封面…",
        "已下载 {0:N0} 张缺失的封面。",
        "封面下载失败：{0}",
        "添加到播放列表",
        "新建播放列表",
        "新建播放列表",
        "播放列表名称",
        "创建播放列表",
        "曲目已添加到播放列表“{0}”。",
        "已向播放列表“{1}”添加 {0} 首曲目。",
        "删除播放列表",
        "从播放列表移除",
        "播放列表“{0}”已删除。",
        "曲目已从播放列表中移除。",
        "将筛选条件保存为智能播放列表",
        "智能播放列表“{0}”已保存。",
        "请先选择筛选条件。",
        "媒体库备份",
        "将数据库、播放列表、历史记录、封面和文件夹列表导出为 ZIP。不包含音频文件。",
        "导出媒体库",
        "导入媒体库",
        "正在导出媒体库…",
        "媒体库已导出到“{0}”。",
        "媒体库导出失败：{0}",
        "导入将替换当前媒体库、播放列表、历史记录和所有图片。是否继续？",
        "正在导入媒体库并重建搜索索引…",
        "媒体库已导入。Orynivo 即将关闭，随后可重新启动。",
        "媒体库导入失败：{0}",
        "请先完成正在进行的媒体库扫描或维护操作。",
        "Orynivo 媒体库 (*.zip)|*.zip",
        "正在导出媒体库：{0}% — {1}",
        "正在导入媒体库：{0}% — {1}",
        "歌词",
        "显示歌词",
        "刷新歌词",
        "关闭歌词",
        "正在加载歌词…",
        "正在从 LRCLIB 下载歌词…",
        "此曲目没有可用的元数据。",
        "未找到歌词。",
        "无法下载歌词。",
        "艺术家信息",
        "显示艺术家信息",
        "刷新艺术家信息",
        "关闭艺术家信息",
        "正在加载艺术家信息 …",
        "正在下载艺术家信息 …",
        "未找到艺术家信息",
        "无法下载艺术家信息。",
        "未下载图片",
        "图片文件缺失",
        "无法加载图片",
        "来源：Wikipedia",
        "来源：Last.fm",
        "艺术家信息来源",
        "Last.fm API 密钥",
        "在 last.fm/api/account/create 创建免费的 API 密钥",
        "Fanart.tv API 密钥",
        "优先使用精选艺术家图片。密钥加密保存在当前用户的凭据存储中；也可设置 FANART_TV_API_KEY。对于现有艺术家，请使用“刷新艺术家信息”。在 fanart.tv/get-an-api-key/ 创建密钥。",
        "下载缺失的艺术家图片",
        "依次搜索本地媒体库和所有已配置的 Orynivo 服务器。每位艺术家先在 Fanart.tv（需 API 密钥）中搜索，再搜索 Wikimedia。Fanart.tv 的结果可自动接受；Wikimedia 的结果始终需要确认。",
        "正在搜索艺术家图片 {0}/{1}：{2} · {3}",
        "已接受 {0:N0} 张艺术家图片，拒绝 {1:N0} 张；{2:N0} 个请求失败。",
        "艺术家图片下载失败：{0}",
        "艺术家图片下载已取消。",
        "候选图片 {0}/{1}：{2} · {3}",
        "正在加载本地和服务器中的艺术家…",
        "正在估算剩余时间…",
        "预计剩余时间：{0}",
        "艺术家候选图片",
        "来源：{0}",
        "此图片仅在您确认后保存。",
        "接受",
        "拒绝",
        "显示专辑的所有曲目",
        "Orynivo 崩溃",
        "发生意外错误。崩溃报告已保存到：\n\n{0}\n\nOrynivo 即将关闭。",
        "发生意外错误。无法保存崩溃报告。Orynivo 即将关闭。")
    {
        TrackInfo = "曲目信息",
        ShowTrackInfo = "显示曲目信息",
        PhysicalPath = "物理路径",
        ReleaseOutputDevice = "释放输出设备",
        ReacquireOutputDevice = "重新占用输出设备",
        OutputDeviceReleased = "输出设备已释放",
        VersionLabel = "版本 {0}",
        CheckForUpdates = "检查更新",
        Updates = "更新",
        CheckForUpdatesOnStartup = "应用启动时检查更新",
        WindowBehavior = "窗口行为",
        StartMaximized = "启动时最大化",
        CheckingForUpdates = "正在检查更新…",
        UpdateAvailable = "版本 {0} 已可用。",
        UpToDate = "Orynivo 已是最新版本。",
        UpdateUnavailable = "此版本未配置经过验证的更新。",
        DownloadAndInstall = "下载并安装",
        DownloadingUpdate = "正在下载并验证更新…",
        InstallingUpdate = "正在安装更新…",
        UpdateFailed = "无法检查或安装更新。",
        UpdateServer = "更新服务器",
        UpdatingServer = "正在传输更新…",
        ServerUpdateQueued = "服务器更新已排队。",
        ServerUpdateUnavailable = "没有更新的受支持服务器版本。",
        ServerUpdateFailed = "服务器更新失败",
        ServerUpdateRejected = "服务器拒绝更新（HTTP {0}）",
        UpdatingNamedServer = "正在更新服务器“{0}”…",
        ServerUpdatesFailedContinue = "以下服务器无法更新：{0}。是否继续更新桌面应用？",
        OutputType = "输出类型",
        LocalMedia = "本地",
        AsioOutputDevice = "ASIO 输出设备",
        CwAsioOutputDevice = "cwASIO 输出设备",
        SteinbergAsio = "Steinberg ASIO",
        CwAsio = "cwASIO",
        WasapiOutputDevice = "WASAPI 输出设备",
        AirPlay = "AirPlay 2",
        AirPlayOutputDevice = "AirPlay 2 输出设备",
        NoAirPlayDevices = "在局域网中未找到 AirPlay 2 设备。",
        AirPlaySenderMissing = "缺少原生 AirPlay 2 桥接模块；可使用兼容的“raop_play”辅助程序。",
        SelectAirPlayDevice = "请先选择 AirPlay 输出设备。",
        OpenAl = "OpenAL",
        OpenAlOutputDevice = "OpenAL 输出设备",
        DirectAlsa = "ALSA（直接、独占）",
        AlsaOutputDevice = "ALSA 直接输出设备",
        LinuxDefaultAudioDevice = "系统默认设备（OpenAL）",
        OpenAlInitializationFailed = "OpenAL 无法初始化系统音频输出。",
        AlsaExactOpenFailed = "无法以 {1} Hz 打开 ALSA 设备“{0}”且不进行重采样：{2}",
        AlsaDeviceBusy = "ALSA 直接输出设备“{0}”已被 PipeWire 或其他应用占用。更改系统输出不会释放该设备。请在系统中禁用该音频设备配置，或选择 OpenAL 输出。",
        AlsaPrepareFailed = "跳转播放位置后，ALSA 无法准备输出设备。",
        DeviceInfo = "设备信息",
        DatabaseOptimizeHint = "删除已释放的页面，使文件在物理上变小。",
        GenreCloudCache = "流派云背景",
        GenreCloudCacheHint = "清除缓存的艺术家拼图。下次打开流派层级时将重新生成。",
        GenreCloudCacheCleared = "流派云背景缓存已清除。",
        GenreCloudBackground = "流派云背景",
        GenreCloudBackgroundHint = "选择背景图片，或完全禁用以降低系统负载。",
        GenreCloudBackgroundNone = "无背景图片",
        GenreCloudBackgroundAlbums = "专辑封面",
        GenreCloudBackgroundArtists = "艺术家图片",
        GenreCloudVisibility = "图片可见度",
        ClearGenreCloudCache = "清空背景缓存",
        AppearanceNavItem = "外观",
        ArtistInfoNavItem = "艺术家信息",
        NormalizeArtists = "规范化艺术家名称",
        NormalizeArtistsHint = "移除主要艺术家名称中的“feat.”并合并明确的标点和空格变体。不修改音频文件。",
        ArtistsNormalizing = "正在规范化艺术家名称并重建搜索索引…",
        ArtistsNormalized = "已合并 {0:N0} 个艺术家变体并更新 {1:N0} 首曲目。",
        ArtistNormalizationFailed = "艺术家规范化失败：{0}",
        AsioBridgeMissing = "此版本不包含 ASIO 支持。请使用 WASAPI。",
        KernelStreamingUnavailable = "可以选择 Kernel Streaming，但尚未实现该播放后端。",
        AddMusicDirectory = "添加音乐文件夹",
        TrackCountTooltip = "数据库中的曲目数量",
        Scan = "扫描",
        RefreshAllMetadata = "重新读取元数据",
        RefreshAllMetadataHint = "刷新媒体库分析，不修改文件。",
        RemoveDirectory = "移除文件夹",
        ScanCompleted = "完成：{0} 个文件 · {1} 个新增 · {2} 个更新 · {3} 个移除{4}",
        ScanFailed = "错误：{0}",
        StartupPreparingLibrary = "正在准备媒体库…",
        StartupCheckingSearchIndex = "正在检查搜索索引…",
        SearchIndexRebuilding = "正在后台重建搜索索引（{0}/{1}）…",
        SearchIndexReady = "搜索索引已是最新。",
        SearchIndexFailed = "无法更新搜索索引：{0}",
        Back = "返回",
        MarkAsFavorite = "添加到收藏",
        OpenAlbum = "打开专辑",
        OpenArtist = "打开艺术家",
        ToggleFavorite = "切换收藏状态",
        PlaybackThrough = "通过 {0} 播放",
        PlaybackThroughWithDsdConversion = "通过 {0} 播放 · DSD 转换为 PCM（{1:N0} Hz）",
        NativeDsdOutput = "原生 DSD",
        DsdToPcmOutput = "DSD → PCM",
        DopOutput = "通过 DoP 输出 DSD",
        DopRequiresDirectAlsa = "需要支持 DoP 的 DAC，并且必须使用无重采样的精确输出。",
        ReplayGain = "ReplayGain 音量调整",
        ReplayGainHint = "适用于 PCM 播放。曲目模式优先使用曲目增益，专辑模式优先使用专辑增益。原生 DSD 输出保持位完美。",
        ReplayGainOff = "关闭",
        ReplayGainTrack = "曲目",
        ReplayGainAlbum = "专辑",
        CalculateReplayGainDuringScan = "在本地媒体库扫描期间自动计算缺失的 ReplayGain（较慢）",
        CalculateReplayGain = "计算缺失的 ReplayGain",
        ReplayGainCalculating = "正在计算 ReplayGain…",
        ReplayGainCalculated = "ReplayGain 计算完成：已更新 {0} 首曲目。",
        ReplayGainCalculationFailed = "ReplayGain 计算失败：{0}",
        NonGaplessCrossfade = "非无缝队列的淡入淡出（秒）",
        NonGaplessCrossfadeHint = "0 表示禁用过渡。仅适用于未由无缝 PCM 引擎处理的队列切换。",
        ReplayGainBadge = "RG",
        DsdPlayback = "DSD 播放",
        AlwaysConvertDsdToPcm = "始终将 DSD 文件转换为 PCM",
        AlwaysConvertDsdToPcmHint = "ASIO/cwASIO 也使用 PCM 路径，因此音量、ReplayGain 和均衡器可以生效。关闭后使用位完美的原生 DSD 输出。",
        DsdOverPcm = "通过 DoP 输出 DSD",
        DsdOverPcmHint = "将 DSD 无损封装到 PCM 帧（DSD over PCM）中。需要支持 DoP 的 DAC 和无重采样的精确输出；音量、ReplayGain 和均衡器不起作用。",
        PcmOutputBoost = "将 PCM 输出提升 +6 dB",
        PcmOutputBoostHint = "提升所有 PCM 播放路径的音量，使其更接近原生 DSD 的响度。原生 DSD 输出保持位完美且不变。",
        OutputDevicesLoading = "正在加载输出设备 …",
        Equalizer = "参数均衡器",
        EqualizerHint = "导入 Equalizer APO 和 AutoEQ 配置，用于 PCM 以及 DSD 转 PCM 播放。原生 DSD 输出保持位完美。",
        EqualizerEnabled = "启用均衡器",
        EqualizerImport = "导入 APO/AutoEQ 配置",
        EqualizerImporting = "正在导入均衡器配置…",
        EqualizerImportTitle = "导入 Equalizer APO 或 AutoEQ 配置",
        EqualizerNoProfile = "尚未导入配置。",
        EqualizerProfileSummary = "{0} · 前置增益 {1:+0.##;-0.##;0} dB · {2} 个滤波器",
        EqualizerImportFailed = "无法导入配置。",
        EqualizerProfileFileType = "Equalizer APO / AutoEQ 配置",
        EqualizerPreamp = "前置增益（dB）",
        EqualizerFilterType = "滤波器类型",
        EqualizerFrequency = "频率（Hz）",
        EqualizerGain = "增益（dB）",
        EqualizerQ = "Q 值",
        EqualizerAddFilter = "添加滤波器",
        EqualizerRemoveFilter = "移除滤波器",
        EqualizerPeak = "峰值",
        EqualizerLowShelf = "低架滤波",
        EqualizerHighShelf = "高架滤波",
        EqualizerLowPass = "低通",
        EqualizerHighPass = "高通",
        EqualizerCreate = "创建均衡器",
        EqualizerCreateTitle = "创建新均衡器",
        EqualizerName = "均衡器名称",
        EqualizerNameExists = "已存在同名均衡器。",
        OutputProfile = "输出",
        OutputProfileCreate = "创建输出设备",
        OutputProfileConfigure = "配置输出设备",
        OutputProfileDelete = "删除输出设备",
        OutputProfileCreateTitle = "创建新输出",
        OutputProfileConfigureTitle = "配置输出设备",
        OutputProfileName = "输出设备名称",
        OutputProfileNameExists = "已存在同名输出。",
        OutputProfileDeleteTitle = "删除输出",
        OutputProfileDeleteConfirm = "确定删除输出“{0}”？",
        UserProfiles = "用户配置文件",
        UserProfileActive = "当前配置文件",
        UserProfileCreate = "创建配置文件",
        UserProfileRename = "重命名配置文件",
        UserProfileDelete = "删除配置文件",
        UserProfileName = "用户配置名称",
        UserProfileMigrateFavorites = "是否将现有个人数据（收藏、评分和历史记录）复制到新用户配置？",
        UserProfileDeleteConfirm = "是否删除用户配置“{0}”？",
        EqualizerDelete = "删除均衡器",
        EqualizerDeleteTitle = "删除均衡器",
        EqualizerDeleteConfirm = "确定删除均衡器“{0}”吗？",
        SelectColumns = "选择列",
        FileName = "文件名",
        FileSize = "文件大小",
        AddedAt = "添加时间",
        SampleRate = "采样率",
        BitDepth = "位深",
        Channels = "声道",
        TrackNumber = "曲目编号",
        DiscNumber = "光盘编号",
        Composer = "作曲家",
        Bpm = "BPM",
        ReplayGainTrackColumn = "曲目 ReplayGain",
        ReplayGainAlbumColumn = "专辑 ReplayGain",
        PersonalRating = "我的评分",
        MusicBrainzRating = "MusicBrainz 评分",
        MusicBrainzLoadRating = "加载评分",
        MusicBrainzLoadingRating = "正在加载…",
        MusicBrainzRetryRating = "重试",
        MusicBrainzNoRating = "暂无评分",
        RatingSetHint = "设置个人评分",
        RatingUpdateFailed = "无法保存评分。",
        Codec = "编码",
        Tags = "标签",
        Homepage = "主页",
        FeedUrl = "订阅源地址",
        SearchResultSummary = "{0:N0} 首曲目 · {1:N0} 张专辑 · {2:N0} 位艺术家",
        RecentAlbums = "最近添加的专辑",
        AlbumRecommendations = "专辑推荐",
        RecommendationMoodAll = "所有心情",
        RecommendationMoodRelaxed = "放松",
        RecommendationMoodEnergetic = "活力",
        RecommendationMoodHappy = "愉快",
        RecommendationMoodMelancholic = "忧郁",
        RecommendationNoMatches = "尚无足够的匹配收听记录来生成推荐。",
        PlayMoreLikeThis = "播放更多类似曲目",
        PlayMoodMix = "心情混音",
        SimilarTracksLoading = "正在加载相似曲目…",
        SimilarTracksUnavailable = "此曲目没有可用的相似度数据。",
        SimilarTracksNoMatches = "未找到相似曲目。",
        SimilarTracksQueued = "{0:N0} 首相似曲目已准备播放。",
        RecommendationListView = "列表",
        RecommendationStageView = "舞台",
        MetadataProblems = "检查元数据",
        MetadataProblemsHint = "Orynivo 按实际文件夹检查，不受专辑可能被拆分成多个条目的影响。双击条目以搜索匹配的 MusicBrainz 发行版本。",
        MetadataWorkflow = "1. 查看结果 → 2. 选择文件夹 → 3. 比较并确认建议",
        MetadataNoFindings = "当前筛选条件下暂无检测结果。",
        MetadataInspectFiles = "同时检查文件（可读性和重复校验；较慢）",
        MetadataQuickHint = "此页面检查已保存的元数据，不读取音乐文件。",
        MetadataSelectHint = "选择文件夹以查看发现的问题。",
        MetadataActionGuide = "“将文件夹识别为专辑”会在 MusicBrainz 搜索曲名、艺术家和编号。缺失的 ReplayGain：设置 → 播放。图片：专辑或艺术家页面。缺失文件和重复文件需手动检查。仅将已确认的更改保存到媒体库；音频文件保持不变。",
        MetadataRemoteReadOnly = "此服务器条目为只读报告。通过 MusicBrainz 修正文件夹仅适用于本地条目。请在 Orynivo 服务器设置中计算 ReplayGain，在专辑或艺术家页面添加图片。",
        MetadataReviewGuide = "选择文件夹。分析不会修改或删除文件。",
        MetadataPhaseDatabase = "正在加载已保存的元数据…",
        MetadataPhaseFolders = "正在检查文件夹和元数据…",
        MetadataPhaseHashes = "正在计算疑似重复文件的哈希值…",
        MetadataPhaseServers = "正在等待服务器报告。服务器不提供预计剩余时间；已完成的检测结果可查看。",
        MetadataPhaseReleases = "正在从 MusicBrainz 加载发行版本和曲目列表…",
        MetadataPhaseSaving = "正在保存已确认的修正并更新搜索索引…",
        MetadataRemaining = "此步骤预计剩余时间：{0}",
        MetadataRemainingUnknown = "暂时无法估算剩余时间",
        MetadataElapsed = "已用时间：{0}",
        IdentifyFolderAsAlbum = "将文件夹识别为专辑",
        MetadataFolder = "文件夹",
        MetadataIssues = "检测到的问题",
        MetadataTrackCount = "曲目",
        MetadataReviewTitle = "检查专辑元数据",
        MetadataSearching = "正在根据曲目数量和时长搜索 MusicBrainz…",
        MetadataNoMatch = "未找到足够匹配的发行版本。",
        MetadataSearchFailed = "MusicBrainz 暂时不可用，请稍后重试。",
        MetadataFoundReleases = "匹配的发行版本",
        MetadataApplyCorrection = "应用修正",
        MetadataCorrectionPreview = "修正预览",
        MetadataCurrentValues = "当前：曲名 — 艺术家",
        MetadataProposedValues = "建议：曲名 — 艺术家",
        MetadataRefreshAnalysis = "重新分析",
        MetadataAlbumQuery = "专辑搜索词",
        MetadataArtistQuery = "艺术家搜索词",
        MetadataRepairSuccess = "Orynivo 媒体库中的元数据已修正。",
        MetadataIssueAlbums = "专辑名称不一致",
        MetadataIssueArtists = "专辑艺术家不一致",
        MetadataIssueMissingTitles = "缺少曲名",
        MetadataIssueMissingNumbers = "缺少曲目编号",
        MetadataIssueDuplicateNumbers = "曲目编号重复",
        MetadataIssueMissingReplayGain = "{0} 首缺少 ReplayGain",
        MetadataIssueMissingMusicBrainzIds = "{0} 首缺少 MusicBrainz ID",
        MetadataSeverity = "优先级",
        MetadataSeverityAll = "所有优先级",
        MetadataIssueAll = "所有问题类型",
        MetadataIssueReplayGain = "缺少 ReplayGain",
        MetadataIssueMusicBrainzIds = "缺少 MusicBrainz ID",
        MetadataIssueIncompleteAlbum = "专辑不完整（缺少 {0} 首曲目）",
        MetadataIssueAlbumArtwork = "缺少专辑封面",
        MetadataIssueArtistImage = "缺少艺术家图片",
        MetadataIssueMissingFiles = "{0} 个源文件缺失",
        MetadataIssueUnreadableFiles = "{0} 个源文件无法读取",
        MetadataIssueLikelyDuplicates = "{0} 个疑似重复文件",
        MetadataIssueExactDuplicates = "{0} 个逐字节相同的重复文件",
        MetadataIssueAlternateRecordings = "{0} 个录音存在于其他文件或版本中",
        MetadataIssueArtistNameVariants = "{0} 个艺术家名称拼写变体",
        MetadataSeverityInformation = "信息",
        MetadataSeverityWarning = "警告",
        MetadataSeverityError = "错误",
        MetadataDoctorSummary = "{0} 个错误 · {1} 个警告 · {2} 条信息",
        MetadataAnalysisFailed = "分析失败。详情已保存到错误日志。",
        MetadataDoctorServersUnavailable = "{0} 个服务器不可用或尚不支持媒体库检查",
        MetadataAnalysisCancelled = "分析已取消。",
        Calendar = "日历 — {0}",
        TopGenres = "最常聆听的流派",
        TopAlbums = "最常聆听的专辑",
        TopArtists = "最常聆听的艺术家",
        ListeningStats = "聆听统计",
        PeriodAllTime = "全部时间",
        PeriodThisYear = "今年",
        PeriodThisMonth = "本月",
        PeriodLast30Days = "最近 30 天",
        PeriodLast7Days = "最近 7 天",
        HistorySourceRemote = "远程",
        HistorySourcePlex = "Plex",
        NoData = "暂无数据。",
        LibraryUpdating = "正在更新媒体库…",
        LibraryUpdatingWithCount = "正在更新媒体库… {0} / {1} 个文件",
        RefreshView = "刷新",
        LibraryDataAvailable = "有新的媒体库数据可用",
        ServerUnreachable = "无法连接",
        ServerLastConnected = "上次连接：{0}",
        ServerNeverConnected = "从未连接",
        ServerMissingFeatures = "服务器不支持：{0}",
        CapabilityTrackFacets = "曲目筛选",
        CapabilityRecentAlbums = "最近添加的专辑",
        CapabilityWaveforms = "波形",
        RemoteCache = "远程缓存",
        RemoteCacheSize = "缓存大小：{0}",
        ClearRemoteCacheAll = "清空全部缓存",
        ClearCache = "清除缓存",
        RemoteScanning = "正在更新 {0}…",
        RemoteScanningWithCount = "正在更新 {0}… {1} / {2} 个文件",
        SmartPlaylistPreviewCount = "{0} 首曲目匹配",
        SmartPlaylistPreviewComputing = "正在计算…",
        SmartPlaylistPreviewInvalid = "条件无效",
        RestoreQueue = "上次队列",
        RestoreQueueTooltip = "恢复上次播放队列",
        NoPreviousQueue = "没有可恢复的队列。",
        ClearQueue = "清空队列",
        QueueCleared = "队列已清空。",
        RecentlyPlayed = "最近播放",
        GreetingMorning = "早上好",
        GreetingAfternoon = "下午好",
        GreetingEvening = "晚上好",
        DashboardTagline = "您的音乐概览",
        DashboardWelcomeBack = "欢迎回来",
        DashboardHeroHint = "查看最近添加的专辑和聆听统计。",
        DashboardRandomPlayback = "随机播放",
        InfiniteMixStart = "开始无限混音",
        GenreCloudInfiniteMix = "根据流派云开始无限混音",
        InfiniteMixStop = "停止无限混音",
        InfiniteMixActive = "无限混音已启用",
        InfiniteMixCalculating = "正在计算无限混音 …",
        InfiniteMixSettingsTitle = "无限混音设置",
        InfiniteMixSettingsHint = "设置心情、来源和历史时间范围。",
        InfiniteMixMood = "心情",
        InfiniteMixMoodCalm = "平静",
        InfiniteMixMoodBalanced = "均衡",
        InfiniteMixMoodEnergetic = "活力",
        InfiniteMixDiscovery = "探索程度",
        InfiniteMixFamiliar = "熟悉",
        InfiniteMixAdventurous = "新颖",
        InfiniteMixPeriod = "历史时间范围",
        InfiniteMixSources = "来源",
        InfiniteMixWeightFavorites = "偏好收藏",
        InfiniteMixPreferRare = "优先播放较少播放的曲目",
        InfiniteMixIncludeGenres = "包含的流派",
        InfiniteMixExcludeGenres = "排除的流派",
        InfiniteMixGenresWatermark = "输入流派…",
        InfiniteMixAddGenre = "添加流派",
        InfiniteMixRemoveGenre = "移除流派",
        InfiniteMixPaused = "无限混音已暂停",
        InfiniteMixPause = "暂停混音",
        InfiniteMixResume = "继续混音",
        InfiniteMixAdjust = "调整混音",
        InfiniteMixReplaceNext = "换一个推荐",
        InfiniteMixMoreLikeThis = "更多类似曲目",
        InfiniteMixLessLikeThis = "更少类似曲目",
        InfiniteMixExcludeTrack = "排除此曲",
        DashboardQuickAccess = "快速访问",
        DashboardTotalMinutes = "总分钟数",
        DashboardMinutesShort = "分钟",
        PeriodPrevious = "与上一时段相比",
        ShowAll = "显示全部",
        DevicePcmSampleRates = "支持的 PCM 采样率",
        DeviceDsdRates = "DSD 采样率",
        DevicePcmFormats = "PCM 输出格式",
        DeviceDsdFormats = "DSD 输出格式",
        DeviceChannelSummary = "{0} 个输出声道 · {1} 个输入声道",
        DeviceBufferSummary = "缓冲区：最小 {0}，建议 {1}，最大 {2}，步长 {3}",
        DriverProvidedNoInformation = "驱动程序未提供信息。",
        DsdSupportedWithoutFormats = "支持 DSD 模式；未报告具体声道格式。",
        Unsupported = "不支持。",
        DeviceProbeInconclusive = "无法确定检测结果。设备可能正被其他应用使用。",
        WasapiEndpointSummary = "WASAPI 端点 · {0} 声道\n混音格式：{1} · {2} 位",
        WasapiNoExclusiveFormats = "未检测到独占 PCM 格式。",
        WasapiDsdNotRelevant = "不适用于此播放器中的 WASAPI。",
        LinuxAlsaEndpointSummary = "ALSA 直接输出设备 · {0} 声道\nPCM：{1} 位 · 曲目原始采样率\nALSA 重采样已禁用",
        LinuxOpenAlEndpointSummary = "OpenAL 设备 · {0} 声道\nPCM：{1} 位 · 混音器采样率在播放时检测",
        LinuxDsdOutputUnavailable = "此 PCM 输出路径目前不支持。",
        NativeDsdUsesAsio = "此播放器通过 ASIO 进行原生 DSD 播放。",
        Dashboard = "仪表板",
        DashboardIntroTitle = "聆听概览",
        DashboardIntroHint = "查看最近添加的专辑、聆听历史和喜爱的流派。",
        ArtistsIntroTitle = "艺术家",
        ArtistsIntroHint = "按艺术家浏览媒体库，直接打开专辑，并管理收藏和艺术家图片。",
        AlbumsIntroTitle = "专辑",
        AlbumsIntroHint = "在表格和封面视图之间切换，打开专辑曲目并补充缺失的封面。",
        TracksIntroTitle = "曲目",
        TracksIntroHint = "按流派、格式和比特率搜索、筛选并播放本地音乐库。",
        FoldersIntroTitle = "文件夹结构",
        FoldersIntroHint = "通过已配置的媒体库文件夹浏览音乐，并在文件夹上下文中播放曲目。",
        ThemeLight = "浅色",
        ThemeDark = "深色",
        StatusAvailable = "可用",
        StatusUnavailable = "不可用",
        StatusEnabled = "已启用",
        StatusDisabled = "已禁用",
        StatusReady = "就绪",
        StatusChecking = "正在检查…",
        LanguageGerman = "德语",
        LanguageEnglish = "英语",
        LanguageFrench = "法语",
        LanguageSpanish = "西班牙语",
        LanguageRussian = "俄语",
        LanguageChineseSimplified = "简体中文",
        PcmIntegerFormat = "{0} 位 PCM，小端序（{1}）",
        PcmContainerFormat = "{0} 位 PCM，{1} 位容器，小端序（{2}）",
        PcmFloatFormat = "{0} 位浮点 PCM，小端序（{1}）",
        NativeDsdLsbFormat = "原生 DSD，1 位数据，首个采样位于最低有效位（{0}）",
        NativeDsdMsbFormat = "原生 DSD，1 位数据，首个采样位于最高有效位（{0}）",
        NativeDsdWordFormat = "原生 DSD，8 位字，无字节序差异（{0}）",
        CountEntrySingular = "{0:N0} 个条目",
        CountTrackSingular = "{0:N0} 首曲目",
        Streaming = "流媒体播放",
        StreamingServices = "流媒体服务",
        Qobuz = "Qobuz",
        QobuzApplicationId = "Qobuz 应用程序 ID",
        QobuzIntegrationHint = "Qobuz 集成已准备就绪。合作方访问权限和官方 API 文档可用后将启用目录和播放。",
        QobuzCredentialsHint = "密钥和登录令牌不存储在 settings.json 中。Windows 会为当前用户保护这些数据。",
        SearchArtistImage = "搜索艺术家图片",
        UploadArtistImage = "上传艺术家图片",
        DeleteArtistImage = "删除艺术家图片",
        UploadCover = "上传封面",
        ImageFileType = "图片文件",
        ArtistImageSearchTitle = "搜索艺术家图片",
        ArtistImageSearchRunning = "正在搜索匹配的艺术家图片…",
        ArtistImageSearchNoResults = "未找到艺术家图片。",
        ArtistImageSearchQuery = "搜索词",
        ArtistImageSearchFailed = "艺术家图片搜索失败。",
        UseSelectedArtistImage = "使用所选艺术家图片",
        ArtistImageDownloadFailed = "无法保存所选艺术家图片。",
        ArtistProfileSearchTitle = "重新加载艺术家信息",
        ArtistProfileSearchHint = "如有需要，可调整 Wikipedia 或 Last.fm 用于查找艺术家资料的名称。这不会更改媒体库中的艺术家名称。",
        ArtistProfileSearchQuery = "资料搜索名称",
        ArtistProfileSearchLoad = "加载信息",
        EditArtistName = "编辑艺术家名称",
        ArtistName = "艺术家名称",
        RenameArtist = "重命名",
        MergeArtistsTitle = "合并艺术家",
        ArtistNameExistsMessage = "已存在名为“{0}”的艺术家。是否合并两位艺术家？请选择保留哪条记录及资料。",
        KeepArtistProfile = "优先保留“{0}”并合并",
        ArtistRenameFailed = "无法重命名或合并艺术家。",
        Shuffle = "随机播放",
        SearchLyrics = "搜索歌词",
        LyricsSearchTitle = "搜索歌词",
        LyricsSearchRunning = "正在搜索匹配的歌词…",
        LyricsSearchNoResults = "未找到匹配的歌词。",
        LyricsSearchFailed = "歌词搜索失败。",
        UseSelectedLyrics = "使用所选歌词",
        SelectLyricsResult = "选择左侧的歌词以预览。",
        SynchronizedLyrics = "同步歌词",
        InternetRadio = "网络电台",
        OwnRadios = "我的电台",
        RadioDirectory = "发现电台",
        RadioDirectoryHint = "搜索免费的 Radio Browser 目录，并将电台永久添加到我的电台。",
        RadioSearch = "搜索电台",
        RadioStation = "电台",
        Country = "国家/地区",
        PlayRadio = "播放",
        AddToOwnRadios = "添加到我的电台",
        DeleteRadio = "删除电台",
        RadioLoading = "正在加载电台 …",
        RadioNoResults = "未找到电台。",
        RadioEmptyState = "还没有我的电台。",
        OwnRadiosEmptyHint = "从 Radio Browser 目录添加电台后，它们会显示在这里。",
        RadioAdded = "已添加电台“{0}”。",
        RadioDeleted = "电台“{0}”已删除。",
        RadioSearchFailed = "搜索电台失败。",
        RadioNowPlaying = "正在播放",
        RadioMetadataUnavailable = "电台元数据不可用",
        RadioGenres = "流派",
        ClearFilter = "清除筛选",
        Podcasts = "播客",
        MyPodcasts = "我的播客",
        PodcastDirectory = "发现播客",
        PodcastDirectoryHint = "搜索目录中的播客，并将其添加到我的播客。",
        PodcastSearch = "搜索播客",
        Podcast = "播客",
        PodcastAuthor = "作者",
        PlayLatestEpisode = "播放最新一期",
        AddToMyPodcasts = "添加到我的播客",
        DeletePodcast = "删除播客",
        PodcastLoading = "正在加载播客 …",
        PodcastNoResults = "未找到播客。",
        PodcastEmptyState = "还没有播客。",
        MyPodcastsEmptyHint = "从目录添加播客后，它们会显示在这里。",
        PodcastAdded = "播客“{0}”已添加。",
        PodcastDeleted = "播客“{0}”已删除。",
        PodcastSearchFailed = "搜索播客失败。",
        PodcastFeedFailed = "加载播客订阅源失败。",
        ShowEpisodes = "显示单集",
        Published = "发布时间",
        Progress = "进度",
        PodcastStatus = "状态",
        PodcastUnplayed = "未播放",
        PodcastInProgress = "播放中",
        PodcastPlayed = "已播放",
        PodcastEpisodesLoading = "正在加载节目 …",
        PodcastNoEpisodes = "未找到节目。",
        PodcastCategories = "分类",
        PodcastLanguages = "语言",
        PodcastLanguage = "语言",
        PodcastLanguagesLoading = "正在加载语言 …",
        PodcastOverview = "播客概览",
        PodcastEpisodeTotal = "共 {0:N0} 期",
        PodcastEpisodeUnheard = "{0:N0} 期尚未播放",
        PodcastEpisodeStarted = "{0:N0} 期已开始",
        PodcastLatestEpisode = "最新一期：{0}",
        DailyHistoryTitle = "收听历史 — {0}",
        PlayedAt = "播放时间",
        ListenedDuration = "已收听",
        MediaType = "类型",
        Close = "关闭",
        DailyHistoryNoEntries = "这一天没有播放记录。",
        SidebarSections = "侧栏分区",
        SidebarSectionsHint = "选择主导航中显示的可折叠分区。",
        PodcastInfo = "播客信息",
        ShowPodcastInfo = "显示播客信息",
        ClosePodcastInfo = "关闭播客信息",
        PodcastPublishedOn = "发布于 {0}",
        PodcastEpisodeDuration = "时长 {0}",
        PodcastDescriptionUnavailable = "描述不可用",
        PlexServers = "PLEX 服务器",
        PlexServersSettings = "Plex 服务器",
        PlexServersHint = "配置一个或多个 Plex Media Server。",
        AddPlexServer = "添加 Plex 服务器",
        PlexServerDialogTitle = "Plex 服务器",
        PlexServerName = "服务器名称",
        PlexServerUrl = "服务器 URL",
        PlexToken = "Plex 令牌",
        PlexTestConnection = "测试连接",
        PlexTestingConnection = "正在测试连接…",
        PlexConnectionSuccessful = "连接成功。找到 {0:N0} 个音频库。",
        PlexConnectionFailed = "连接失败：{0}",
        PlexServerFieldsRequired = "名称和服务器 URL 为必填项。",
        PlexServerUrlInvalid = "请输入有效的 HTTP 或 HTTPS URL。",
        PlexEditServer = "编辑",
        PlexRemoveServer = "移除",
        PlexNoAudioLibraries = "未找到音频库。",
        PlexLoading = "正在加载 Plex 内容…",
        OrynivoServers = "Orynivo 服务器",
        SourceColumn = "来源",
        LocalSource = "本地",
        LocalSourceShort = "L",
        OrynivoServersSettings = "Orynivo 服务器",
        OrynivoServersHint = "将播放器连接到本地网络中的一个或多个 Orynivo 服务器。",
        AddOrynivoServer = "添加服务器",
        OrynivoServerDialogTitle = "Orynivo 服务器",
        OrynivoServerName = "显示名称",
        OrynivoServerUrl = "服务器 URL（例如 http://192.168.1.10:5280）",
        OrynivoServerApiKey = "API 密钥",
        OrynivoTestConnection = "测试连接",
        OrynivoTestingConnection = "正在测试连接…",
        OrynivoConnectionSuccessful = "已连接。服务器：{0} v{1}",
        OrynivoConnectionFailed = "连接失败。请检查 URL 和 API 密钥。",
        OrynivoServerFieldsRequired = "名称、URL 和 API 密钥为必填项。",
        OrynivoEditServer = "编辑",
        OrynivoRemoveServer = "移除",
        OrynivoLoading = "正在加载服务器内容…",
        OrynivoServerDirectories = "服务器音乐文件夹",
        OrynivoLoadServerDirectories = "从服务器加载",
        OrynivoAddServerDirectory = "添加文件夹",
        OrynivoLoadingServerDirectories = "正在加载服务器文件夹…",
        OrynivoServerDirectoriesLoaded = "服务器文件夹已加载。",
        OrynivoServerDirectoriesLoadFailed = "无法加载服务器文件夹。",
        OrynivoSavingServerDirectories = "正在保存服务器文件夹…",
        OrynivoServerDirectoriesSaveFailed = "无法保存服务器文件夹。",
        OrynivoCalculateReplayGainDuringScan = "扫描服务器时计算缺失的 ReplayGain（较慢）",
        OrynivoSavingReplayGainSettings = "正在服务器上保存扫描时的 ReplayGain 设置…",
        OrynivoReplayGainSettingsSaveFailed = "无法在服务器上保存扫描时的 ReplayGain 设置。",
        OrynivoReplayGainSettingsUnsupported = "文件夹已加载。此服务器尚不支持扫描时的 ReplayGain 设置。",
        OrynivoNoServerDirectories = "尚未配置服务器文件夹。",
        OrynivoServerDirectoryBrowserTitle = "选择服务器文件夹",
        OrynivoServerDirectoryRoots = "服务器音乐文件夹",
        OrynivoServerDirectoryUp = "上一级",
        OrynivoSelectServerDirectory = "选择",
        OrynivoServerDirectoryLoading = "正在加载文件夹…",
        OrynivoServerDirectoryLoadFailed = "无法加载文件夹。",
        OrynivoServerDirectoryEmpty = "没有子文件夹。",
        OrynivoServerScan = "服务器扫描",
        OrynivoStartServerScan = "扫描媒体库",
        OrynivoCalculateServerReplayGain = "计算 ReplayGain",
        OrynivoServerScanStarting = "正在启动服务器扫描 …",
        OrynivoServerScanStartFailed = "无法启动服务器扫描。",
        OrynivoServerScanIdle = "服务器当前未在扫描。",
        OrynivoServerScanDiscovering = "正在查找文件：{0}",
        OrynivoServerScanProgress = "{0}/{1} · {2}",
        OrynivoServerScanCompleted = "扫描完成：{0} 个文件，{1} 个新增，{2} 个更新，{3} 个移除，{4} 个失败。",
        OrynivoServerScanFailed = "服务器扫描失败：{0}",
        OrynivoServerBackup = "备份服务器媒体库",
        OrynivoDownloadBackup = "下载备份",
        OrynivoRestoreBackup = "恢复备份",
        OrynivoBackupDownloading = "正在下载服务器备份…",
        OrynivoBackupDownloaded = "服务器备份已保存：{0}",
        OrynivoBackupRestoring = "正在验证并恢复服务器备份…",
        OrynivoBackupRestored = "服务器备份恢复成功。",
        OrynivoBackupFailed = "服务器备份操作失败：{0}",
        OrynivoRestoreBackupConfirm = "导入将替换服务器数据库、播放列表、历史记录、封面、艺术家图片和文件夹列表。音频文件保持不变。是否继续？",
        LoadMore = "加载更多",
        FfmpegDownloading = "正在下载 FFmpeg…",
        FfmpegDownloadFailed = "无法下载 FFmpeg。请手动安装：ffmpeg.org",
        SmartPlaylistDialogTitle = "编辑智能播放列表",
        SmartPlaylistName = "名称",
        SmartPlaylistBasicFilters = "基本筛选条件",
        SmartPlaylistGenres = "流派（以逗号分隔）",
        SmartPlaylistFormats = "格式（例如 FLAC、MP3，以逗号分隔）",
        SmartPlaylistBitrates = "比特率，单位 kbps（以逗号分隔）",
        SmartPlaylistSources = "来源（local 或 server:<id>，以逗号分隔）",
        SmartPlaylistMetadata = "元数据",
        SmartPlaylistMinimumYear = "起始年份",
        SmartPlaylistMaximumYear = "结束年份",
        SmartPlaylistSearchText = "搜索文本包含",
        SmartPlaylistArtistContains = "艺术家包含",
        SmartPlaylistAlbumContains = "专辑包含",
        SmartPlaylistMinimumDuration = "最短时长（分钟）",
        SmartPlaylistMaximumDuration = "最长时长（分钟）",
        SmartPlaylistHistory = "媒体库与播放历史",
        SmartPlaylistAddedWithinDays = "最近 X 天内添加",
        SmartPlaylistPlayedWithinDays = "最近 X 天内播放",
        SmartPlaylistNeverPlayed = "从未播放",
        SmartPlaylistMinimumPlayCount = "最少播放次数",
        SmartPlaylistMaximumPlayCount = "最多播放次数",
        SmartPlaylistResult = "结果",
        SmartPlaylistSortOrder = "排序",
        SmartPlaylistSortTitle = "按曲名排序",
        SmartPlaylistSortRandom = "随机",
        SmartPlaylistSortLastPlayed = "最近播放的优先",
        SmartPlaylistSortLeastRecentlyPlayed = "最久未播放的优先",
        SmartPlaylistResultLimit = "曲目数量上限（留空表示不限）",
        CreateSmartPlaylist = "创建智能播放列表",
        InvalidSmartPlaylistCriteria = "请输入有效数字，并确保最小值和最大值一致。“从未播放”不能与最近播放或大于零的最少播放次数组合。",
        EditSmartPlaylist = "编辑智能播放列表",
        LibraryEmptyHint = "尚未配置媒体来源。请打开“设置 → 媒体库”，添加本地音乐文件夹或连接 Orynivo 服务器。",
        SmartPlaylistUpdated = "智能播放列表“{0}”已更新。",
        ImportM3u8Playlist = "导入 M3U8 播放列表",
        ExportM3u8Playlist = "导出为 M3U8",
        SaveAlbumAsPlaylist = "保存为播放列表",
        AlbumPath = "专辑路径",
        UpNext = "待播队列",
        GenreExplorer = "流派云",
        GenreCloudHint = "探索本地媒体库和已连接的 Orynivo 服务器中的流派。选择流派以继续浏览。",
        AllGenres = "所有流派",
        GenreRecommendations = "推荐曲目",
        GenreCloudEmpty = "尚未找到流派。请检查曲目是否包含流派标签。",
        MoreGenres = "更多流派",
        PlayNext = "接下来播放",
        AppendToQueue = "添加到队列末尾",
        RemoveFromQueue = "从队列移除",
        MoveUp = "上移",
        MoveDown = "下移",
        SaveQueueAsPlaylist = "将队列保存为播放列表",
        TracksQueuedNext = "接下来将播放 {0:N0} 首曲目。",
        TracksAppendedToQueue = "已向队列末尾添加 {0:N0} 首曲目。",
        M3u8ImportCompleted = "播放列表“{0}”已导入：{1} 个条目 · {2} 个本地文件缺失 · {3} 个 HTTP 条目 · {4} 个跳过。",
        M3u8ImportNoEntries = "M3U8 文件不包含可导入的条目。",
        M3u8ImportFailed = "M3U8 导入失败：{0}",
        M3u8ExportCompleted = "播放列表“{0}”已导出为 M3U8：{1} 个条目 · {2} 个跳过。",
        M3u8ExportFailed = "M3U8 导出失败：{0}",
        McpServer = "MCP 服务器",
        McpServerHint = "提供用于控制播放器和媒体库的 MCP 接口。",
        McpServerEnabled = "启用 MCP 服务器",
        McpServerPort = "端口",
        McpNetworkAccess = "允许本地网络访问",
        McpNetworkAccessHint = "将 MCP 绑定到所有网络接口。网络请求需要 bearer 令牌。未使用 HTTPS 或 VPN 时，令牌可能被网络中的其他人截获。",
        McpAccessToken = "访问令牌",
        McpAccessTokenWatermark = "启用时自动生成",
        McpGenerateToken = "生成新的",
        MobileRemote = "移动网页遥控器",
        MobileRemoteHint = "提供局域网遥控功能。请使用专用访问令牌；在家庭网络之外访问时，请使用 HTTPS 或 VPN 保护连接。",
        MobileRemoteEnabled = "启用移动网页遥控器",
        MobileRemotePort = "端口",
        MobileRemoteToken = "远程访问令牌",
        MobileRemoteNetworkAddress = "家庭网络 IP 地址",
        MobileRemoteQrHint = "请先保存设置。在同一 Wi-Fi 中扫描二维码可直接登录。二维码包含访问令牌，请勿分享！手动打开 URL 时需要输入令牌。如果显示多个地址，请选择手机可访问的地址。",
        Integration = "集成",
        McpToolsHeader = "工具",
        McpToolsHint = "启用或禁用单个工具。",
        WebBrowsing = "网页访问",
        WebBrowsingHint = "为 AI 提供受控的网页工具：SearXNG 搜索和安全页面抓取。",
        WebBrowsingEnabled = "启用网页工具",
        SearxngUrl = "SearXNG URL",
        WebBlockPrivate = "阻止私有/本地地址（SSRF 防护）",
        WebMaxResults = "最大搜索结果数",
        WebTimeoutSeconds = "超时（秒）",
        WebMaxResponseKb = "最大响应大小（KB）",
        AiChat = "AI 聊天",
        AiChatSettings = "AI 聊天设置",
        AiChatHint = "通过兼容 OpenAI 的 API 连接本地或云端 AI 模型。",
        AiChatEnabled = "启用 AI 聊天",
        AiChatEndpointUrl = "端点 URL",
        AiChatApiKey = "API 密钥（可选）",
        AiChatLocalNote = "LM Studio 和 Ollama 不需要 API 密钥。",
        AiChatModel = "模型",
        AiChatLoadModels = "加载模型",
        AiChatAvailableModels = "选择可用模型",
        AiChatTestConnection = "测试连接",
        AiChatConnectionTesting = "正在测试连接 …",
        AiChatModelsLoaded = "已加载 {0} 个模型。",
        AiChatConnectionSucceeded = "连接成功；{0} 个模型可用。",
        AiChatNoModels = "未找到模型",
        AiChatConnectionFailed = "连接失败",
        AiChatMaxTokens = "最大令牌数",
        AiChatInputPlaceholder = "输入问题…",
        AiChatSend = "发送",
        AiChatClear = "清空聊天",
        AiChatCopy = "复制",
        AiChatNotEnabled = "AI 聊天未启用。请在设置中启用。",
        AiChatEmptyResponse = "模型返回了空响应。",
        AiChatToolResultFallback = "调用工具后，模型未返回最终答复。工具结果：",
        AutoAcceptFanartTvImages = "自动接受 Fanart.tv 的结果"
    };

    /// <summary>Gets the currently active <see cref="LocalizedStrings"/> instance.</summary>
    public static LocalizedStrings Current { get; private set; } = German;
}
