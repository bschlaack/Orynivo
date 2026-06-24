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
    /// <see cref="Current"/>, and all <c>L_*</c> resource keys in <see cref="System.Windows.Application.Current"/>'s resource dictionary.
    /// </summary>
    /// <param name="language">The language to activate.</param>
    public static void Apply(Language language)
    {
        var culture = language switch
        {
            Language.English => CultureInfo.GetCultureInfo("en-US"),
            Language.French => CultureInfo.GetCultureInfo("fr-FR"),
            Language.Spanish => CultureInfo.GetCultureInfo("es-ES"),
            _ => CultureInfo.GetCultureInfo("de-DE")
        };
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        Current = language switch
        {
            Language.English => English,
            Language.French => French,
            Language.Spanish => Spanish,
            _ => German
        };

        var resources = AvaloniaApp.Current!.Resources;
        resources["L_LocalLibrary"] = Current.LocalLibrary;
        resources["L_Artists"] = Current.Artists;
        resources["L_Albums"] = Current.Albums;
        resources["L_Tracks"] = Current.Tracks;
        resources["L_UpNext"] = Current.UpNext;
        resources["L_FolderStructure"] = Current.FolderStructure;
        resources["L_Search"] = Current.Search;
        resources["L_Playlists"] = Current.Playlists;
        resources["L_About"] = Current.About;
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
        resources["L_Library"] = Current.Library;
        resources["L_Directories"] = Current.Directories;
        resources["L_AddDirectory"] = Current.AddDirectory;
        resources["L_DatabaseMaintenance"] = Current.DatabaseMaintenance;
        resources["L_OptimizeDatabase"] = Current.OptimizeDatabase;
        resources["L_RepairAlbumArtwork"] = Current.RepairAlbumArtwork;
        resources["L_DownloadMissingArtwork"] = Current.DownloadMissingArtwork;
        resources["L_DownloadMissingArtworkHint"] = Current.DownloadMissingArtworkHint;
        resources["L_CoverNotFound"] = Current.CoverNotFound;
        resources["L_SearchCover"] = Current.SearchCover;
        resources["L_CoverSearchTitle"] = Current.CoverSearchTitle;
        resources["L_CoverSearchRunning"] = Current.CoverSearchRunning;
        resources["L_CoverSearchNoResults"] = Current.CoverSearchNoResults;
        resources["L_CoverSearchQuery"] = Current.CoverSearchQuery;
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
        resources["L_SmartPlaylistMetadata"] = Current.SmartPlaylistMetadata;
        resources["L_SmartPlaylistMinimumYear"] = Current.SmartPlaylistMinimumYear;
        resources["L_SmartPlaylistMaximumYear"] = Current.SmartPlaylistMaximumYear;
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
        resources["L_ShowAllAlbumTracks"] = Current.ShowAllAlbumTracks;
        resources["L_OutputType"] = Current.OutputType;
        resources["L_AsioOutputDevice"] = Current.AsioOutputDevice;
        resources["L_CwAsioOutputDevice"] = Current.CwAsioOutputDevice;
        resources["L_DeviceInfo"] = Current.DeviceInfo;
        resources["L_ReplayGain"] = Current.ReplayGain;
        resources["L_ReplayGainHint"] = Current.ReplayGainHint;
        resources["L_DsdPlayback"] = Current.DsdPlayback;
        resources["L_AlwaysConvertDsdToPcm"] = Current.AlwaysConvertDsdToPcm;
        resources["L_AlwaysConvertDsdToPcmHint"] = Current.AlwaysConvertDsdToPcmHint;
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
        resources["L_DevicePcmSampleRates"] = Current.DevicePcmSampleRates;
        resources["L_DeviceDsdRates"] = Current.DeviceDsdRates;
        resources["L_DevicePcmFormats"] = Current.DevicePcmFormats;
        resources["L_DeviceDsdFormats"] = Current.DeviceDsdFormats;
        resources["L_Dashboard"] = Current.Dashboard;
        resources["L_StartupPreparingLibrary"] = Current.StartupPreparingLibrary;
        resources["L_Streaming"] = Current.Streaming;
        resources["L_StreamingServices"] = Current.StreamingServices;
        resources["L_Qobuz"] = Current.Qobuz;
        resources["L_QobuzApplicationId"] = Current.QobuzApplicationId;
        resources["L_QobuzIntegrationHint"] = Current.QobuzIntegrationHint;
        resources["L_QobuzCredentialsHint"] = Current.QobuzCredentialsHint;
        resources["L_SearchArtistImage"] = Current.SearchArtistImage;
        resources["L_ArtistImageSearchTitle"] = Current.ArtistImageSearchTitle;
        resources["L_ArtistImageSearchRunning"] = Current.ArtistImageSearchRunning;
        resources["L_ArtistImageSearchNoResults"] = Current.ArtistImageSearchNoResults;
        resources["L_ArtistImageSearchQuery"] = Current.ArtistImageSearchQuery;
        resources["L_ArtistImageSearchFailed"] = Current.ArtistImageSearchFailed;
        resources["L_UseSelectedArtistImage"] = Current.UseSelectedArtistImage;
        resources["L_ArtistImageDownloadFailed"] = Current.ArtistImageDownloadFailed;
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
        resources["L_ClearFilter"] = Current.ClearFilter;
        resources["L_Podcasts"] = Current.Podcasts;
        resources["L_MyPodcasts"] = Current.MyPodcasts;
        resources["L_SidebarSections"] = Current.SidebarSections;
        resources["L_SidebarSectionsHint"] = Current.SidebarSectionsHint;
        resources["L_PlexServers"] = Current.PlexServers;
        resources["L_PlexServersHint"] = Current.PlexServersHint;
        resources["L_AddPlexServer"] = Current.AddPlexServer;
        resources["L_PlexServerDialogTitle"] = Current.PlexServerDialogTitle;
        resources["L_PlexServerName"] = Current.PlexServerName;
        resources["L_PlexServerUrl"] = Current.PlexServerUrl;
        resources["L_PlexToken"] = Current.PlexToken;
        resources["L_PlexTestConnection"] = Current.PlexTestConnection;
        resources["L_LoadMore"] = Current.LoadMore;
        resources["L_PodcastInfo"] = Current.PodcastInfo;
        resources["L_ClosePodcastInfo"] = Current.ClosePodcastInfo;
        resources["L_PodcastDirectory"] = Current.PodcastDirectory;
        resources["L_PodcastDirectoryHint"] = Current.PodcastDirectoryHint;
        resources["L_PodcastSearch"] = Current.PodcastSearch;
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
    }

    private static readonly LocalizedStrings German = new(
        "LOKALE BIBLIOTHEK", "Künstler", "Alben", "Tracks", "Ordnerstruktur", "Suche", "PLAYLISTS", "Über", "Einstellungen",
        "Filter", "Favoriten", "Audiotypen", "Bitrate",
        "Kein Gerät ausgewählt.", "Darstellung", "Farbschema", "Sprache", "WIEDERGABE", "Ausgabegerät",
        "BIBLIOTHEK", "Verzeichnisse", "+ Verzeichnis hinzufügen", "Datenbankwartung",
        "Datenbank optimieren", "Album-Cover reparieren", "Fehlende Cover-Artworks herunterladen",
        "Automatischer Download findet nur Cover, wenn eine MusicBrainz-ID gesetzt ist. Für freiere Suchen nutze die Schaltfläche direkt in der Albumansicht.",
        "Cover nicht gefunden", "Cover suchen", "Cover suchen", "Suche nach passenden Covern …",
        "Keine Cover gefunden.", "Suchbegriff", "Erneut suchen", "Ausgewähltes Cover übernehmen",
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
        "Künstlerinfo", "Künstlerinfo anzeigen", "Künstlerinfo neu laden", "Künstlerinfo schließen",
        "Künstlerinfo wird geladen …", "Künstlerinfo wird heruntergeladen …",
        "Keine Künstlerinfo gefunden.", "Künstlerinfo konnte nicht heruntergeladen werden.",
        "Kein Bild heruntergeladen", "Bilddatei fehlt", "Bild konnte nicht geladen werden",
        "Quelle: Wikipedia", "Quelle: Last.fm",
        "Quelle für Künstlerinfos", "Last.fm API-Schlüssel",
        "Kostenlosen API-Schlüssel erstellen unter: last.fm/api/account/create",
        "Alle Tracks des Albums anzeigen",
        "Orynivo ist abgestürzt",
        "Ein unerwarteter Fehler ist aufgetreten. Ein Fehlerbericht wurde hier gespeichert:\n\n{0}\n\nOrynivo wird beendet.",
        "Ein unerwarteter Fehler ist aufgetreten. Der Fehlerbericht konnte nicht gespeichert werden. Orynivo wird beendet.")
    {
        OutputType = "Ausgabeart",
        AsioOutputDevice = "ASIO-Ausgabegerät",
        CwAsioOutputDevice = "cwASIO-Ausgabegerät",
        SteinbergAsio = "Steinberg ASIO",
        CwAsio = "cwASIO",
        WasapiOutputDevice = "WASAPI-Ausgabegerät",
        DeviceInfo = "Geräteinfo",
        DatabaseOptimizeHint = "Freigegebene Seiten werden entfernt; danach ist die Datei physisch kleiner.",
        AsioBridgeMissing = "Dieser Build enthält keine ASIO-Unterstützung. Bitte WASAPI verwenden.",
        KernelStreamingUnavailable = "Kernel Streaming ist auswählbar, aber noch nicht als Wiedergabe-Backend implementiert.",
        AddMusicDirectory = "Musikverzeichnis hinzufügen",
        TrackCountTooltip = "Anzahl Titel in der Datenbank",
        Scan = "Scannen",
        RemoveDirectory = "Verzeichnis entfernen",
        ScanCompleted = "Fertig: {0} Dateien · {1} neu · {2} aktualisiert · {3} entfernt{4}",
        ScanFailed = "Fehler: {0}",
        StartupPreparingLibrary = "Bibliothek wird vorbereitet …",
        Back = "Zurück",
        MarkAsFavorite = "Als Favorit markieren",
        PlaybackThrough = "Wiedergabe über {0}",
        PlaybackThroughWithDsdConversion = "Wiedergabe über {0} · DSD wird in PCM ({1:N0} Hz) konvertiert",
        NativeDsdOutput = "DSD nativ",
        DsdToPcmOutput = "DSD → PCM",
        ReplayGain = "ReplayGain-Lautstärkeanpassung",
        ReplayGainHint = "Gilt für PCM-Wiedergabe. Im Track-Modus wird bevorzugt der Track-Wert verwendet, im Album-Modus der Album-Wert. Native DSD-Ausgabe bleibt bitgenau.",
        ReplayGainOff = "Aus",
        ReplayGainTrack = "Track",
        ReplayGainAlbum = "Album",
        DsdPlayback = "DSD-Wiedergabe",
        AlwaysConvertDsdToPcm = "DSD-Dateien immer in PCM umwandeln",
        AlwaysConvertDsdToPcmHint = "Verwendet auch mit ASIO/cwASIO den PCM-Pfad, damit Lautstärke, ReplayGain und Equalizer wirken. Bei deaktivierter Option bleibt native DSD-Ausgabe bitgenau.",
        OutputDevicesLoading = "Ausgabegeräte werden geladen …",
        Equalizer = "Parametrischer Equalizer",
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
        Homepage = "Homepage", FeedUrl = "Feed-Adresse",
        SearchResultSummary = "{0:N0} Titel · {1:N0} Alben · {2:N0} Künstler",
        RecentAlbums = "Zuletzt hinzugefügte Alben",
        Calendar = "Kalender – {0}",
        TopGenres = "Top 10 Genres nach Spielzeit",
        NoData = "Keine Daten vorhanden.",
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
        NativeDsdUsesAsio = "Native DSD-Wiedergabe läuft in Orynivo über ASIO.",
        Dashboard = "Dashboard", ThemeLight = "Hell", ThemeDark = "Dunkel",
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
        , ArtistImageSearchTitle = "Künstlerbild suchen"
        , ArtistImageSearchRunning = "Passende Künstlerbilder werden gesucht …"
        , ArtistImageSearchNoResults = "Keine Künstlerbilder gefunden."
        , ArtistImageSearchQuery = "Suchbegriff"
        , ArtistImageSearchFailed = "Die Künstlerbildsuche ist fehlgeschlagen."
        , UseSelectedArtistImage = "Ausgewähltes Bild übernehmen"
        , ArtistImageDownloadFailed = "Das ausgewählte Künstlerbild konnte nicht gespeichert werden."
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
        , Podcast = "Podcast"
        , PodcastAuthor = "Autor"
        , PlayLatestEpisode = "Neueste abspielen"
        , AddToMyPodcasts = "Zu meinen Podcasts"
        , DeletePodcast = "Podcast löschen"
        , PodcastLoading = "Podcasts werden geladen …"
        , PodcastNoResults = "Keine passenden Podcasts gefunden."
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
        , LoadMore = "Mehr laden"
        , FfmpegDownloading = "FFmpeg wird heruntergeladen …"
        , FfmpegDownloadFailed = "FFmpeg konnte nicht heruntergeladen werden. Bitte manuell installieren: ffmpeg.org"
        , SmartPlaylistDialogTitle = "Smart-Playlist bearbeiten"
        , SmartPlaylistName = "Name"
        , SmartPlaylistBasicFilters = "Grundfilter"
        , SmartPlaylistGenres = "Genres (durch Komma getrennt)"
        , SmartPlaylistFormats = "Formate (z. B. FLAC, MP3; durch Komma getrennt)"
        , SmartPlaylistBitrates = "Bitraten in kbps (durch Komma getrennt)"
        , SmartPlaylistMetadata = "Metadaten"
        , SmartPlaylistMinimumYear = "Jahr von"
        , SmartPlaylistMaximumYear = "Jahr bis"
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
        , SmartPlaylistUpdated = "Smart-Playlist »{0}« aktualisiert."
        , ImportM3u8Playlist = "M3U8-Playlist importieren"
        , ExportM3u8Playlist = "Als M3U8 exportieren"
        , SaveAlbumAsPlaylist = "Als Playlist speichern"
        , AlbumPath = "Albumpfad"
        , UpNext = "Als Nächstes"
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
    };

    private static readonly LocalizedStrings English = new(
        "LOCAL LIBRARY", "Artists", "Albums", "Tracks", "Folder structure", "Search", "PLAYLISTS", "About", "Settings",
        "Filter", "Favorites", "Audio types", "Bitrate",
        "No device selected.", "Appearance", "Color scheme", "Language", "PLAYBACK", "Output device",
        "LIBRARY", "Directories", "+ Add directory", "Database maintenance",
        "Optimize database", "Repair album artwork", "Download missing artwork",
        "Automatic download only finds covers when a MusicBrainz ID is present. For freer searches, use the button directly in the album view.",
        "Cover not found", "Search cover", "Search cover", "Searching for matching covers …",
        "No covers found.", "Search term", "Search again", "Use selected cover",
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
        "Artist information", "Show artist information", "Refresh artist information", "Close artist information",
        "Loading artist information …", "Downloading artist information …",
        "No artist information found.", "Artist information could not be downloaded.",
        "No image downloaded", "Image file missing", "Failed to load image",
        "Source: Wikipedia", "Source: Last.fm",
        "Artist info source", "Last.fm API key",
        "Create a free API key at: last.fm/api/account/create",
        "Show all album tracks",
        "Orynivo crashed",
        "An unexpected error occurred. A crash report was saved here:\n\n{0}\n\nOrynivo will now close.",
        "An unexpected error occurred. The crash report could not be saved. Orynivo will now close.")
    {
        OutputType = "Output type", AsioOutputDevice = "ASIO output device", WasapiOutputDevice = "WASAPI output device",
        CwAsioOutputDevice = "cwASIO output device", SteinbergAsio = "Steinberg ASIO", CwAsio = "cwASIO",
        DeviceInfo = "Device information", DatabaseOptimizeHint = "Released pages are removed so the file becomes physically smaller.",
        AsioBridgeMissing = "This build does not include ASIO support. Please use WASAPI.",
        KernelStreamingUnavailable = "Kernel Streaming can be selected but is not implemented as a playback backend yet.",
        AddMusicDirectory = "Add music directory", TrackCountTooltip = "Number of tracks in the database",
        Scan = "Scan", RemoveDirectory = "Remove directory",
        ScanCompleted = "Finished: {0} files · {1} new · {2} updated · {3} removed{4}", ScanFailed = "Error: {0}",
        StartupPreparingLibrary = "Preparing library …", Back = "Back", MarkAsFavorite = "Mark as favorite",
        PlaybackThrough = "Playback through {0}",
        PlaybackThroughWithDsdConversion = "Playback through {0} · DSD is converted to PCM ({1:N0} Hz)",
        NativeDsdOutput = "Native DSD", DsdToPcmOutput = "DSD → PCM",
        ReplayGain = "ReplayGain volume adjustment",
        ReplayGainHint = "Applies to PCM playback. Track mode prefers track gain; album mode prefers album gain. Native DSD output remains bit-perfect.",
        ReplayGainOff = "Off", ReplayGainTrack = "Track", ReplayGainAlbum = "Album",
        DsdPlayback = "DSD playback",
        AlwaysConvertDsdToPcm = "Always convert DSD files to PCM",
        AlwaysConvertDsdToPcmHint = "Uses the PCM path with ASIO/cwASIO as well, allowing volume, ReplayGain, and the equalizer to apply. With this option disabled, native DSD output remains bit-perfect.",
        OutputDevicesLoading = "Loading output devices …",
        Equalizer = "Parametric equalizer",
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
        Homepage = "Homepage", FeedUrl = "Feed address",
        SearchResultSummary = "{0:N0} tracks · {1:N0} albums · {2:N0} artists",
        RecentAlbums = "Recently added albums", Calendar = "Calendar – {0}", TopGenres = "Top 10 genres by play time",
        NoData = "No data available.", DevicePcmSampleRates = "Supported PCM sample rates", DeviceDsdRates = "DSD rates",
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
        NativeDsdUsesAsio = "Native DSD playback in this player uses ASIO.",
        Dashboard = "Dashboard", ThemeLight = "Light", ThemeDark = "Dark",
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
        , ArtistImageSearchTitle = "Search artist image"
        , ArtistImageSearchRunning = "Searching for matching artist images …"
        , ArtistImageSearchNoResults = "No artist images found."
        , ArtistImageSearchQuery = "Search term"
        , ArtistImageSearchFailed = "The artist image search failed."
        , UseSelectedArtistImage = "Use selected image"
        , ArtistImageDownloadFailed = "The selected artist image could not be saved."
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
        , Podcast = "Podcast"
        , PodcastAuthor = "Author"
        , PlayLatestEpisode = "Play latest"
        , AddToMyPodcasts = "Add to my podcasts"
        , DeletePodcast = "Delete podcast"
        , PodcastLoading = "Loading podcasts …"
        , PodcastNoResults = "No matching podcasts found."
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
        , LoadMore = "Load more"
        , FfmpegDownloading = "Downloading FFmpeg …"
        , FfmpegDownloadFailed = "FFmpeg could not be downloaded. Please install it manually: ffmpeg.org"
        , SmartPlaylistDialogTitle = "Edit smart playlist"
        , SmartPlaylistName = "Name"
        , SmartPlaylistBasicFilters = "Basic filters"
        , SmartPlaylistGenres = "Genres (comma-separated)"
        , SmartPlaylistFormats = "Formats (for example FLAC, MP3; comma-separated)"
        , SmartPlaylistBitrates = "Bitrates in kbps (comma-separated)"
        , SmartPlaylistMetadata = "Metadata"
        , SmartPlaylistMinimumYear = "Year from"
        , SmartPlaylistMaximumYear = "Year to"
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
        , SmartPlaylistUpdated = "Smart playlist '{0}' updated."
        , ImportM3u8Playlist = "Import M3U8 playlist"
        , ExportM3u8Playlist = "Export as M3U8"
        , SaveAlbumAsPlaylist = "Save as playlist"
        , AlbumPath = "Album path"
        , UpNext = "Up next"
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
    };

    private static readonly LocalizedStrings French = new(
        "BIBLIOTHÈQUE LOCALE", "Artistes", "Albums", "Titres", "Arborescence", "Recherche", "PLAYLISTS", "À propos", "Paramètres",
        "Filtre", "Favoris", "Types audio", "Débit",
        "Aucun appareil sélectionné.", "Apparence", "Thème", "Langue", "LECTURE", "Périphérique de sortie",
        "BIBLIOTHÈQUE", "Dossiers", "+ Ajouter un dossier", "Maintenance de la base",
        "Optimiser la base", "Réparer les pochettes", "Télécharger les pochettes manquantes",
        "Le téléchargement automatique ne trouve des pochettes que si un identifiant MusicBrainz est présent. Pour une recherche plus libre, utilisez le bouton directement dans la vue des albums.",
        "Pochette introuvable", "Rechercher une pochette", "Rechercher une pochette", "Recherche de pochettes correspondantes …",
        "Aucune pochette trouvée.", "Terme de recherche", "Relancer la recherche", "Utiliser la pochette sélectionnée",
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
        "Informations sur l’artiste", "Afficher les informations sur l’artiste",
        "Actualiser les informations sur l’artiste", "Fermer les informations sur l’artiste",
        "Chargement des informations sur l’artiste …",
        "Téléchargement des informations sur l’artiste …",
        "Aucune information trouvée sur l’artiste.",
        "Impossible de télécharger les informations sur l’artiste.",
        "Aucune image téléchargée", "Fichier image introuvable", "Échec du chargement de l’image",
        "Source : Wikipédia", "Source : Last.fm",
        "Source des informations artiste", "Clé API Last.fm",
        "Créez une clé API gratuite sur : last.fm/api/account/create",
        "Afficher tous les titres de l’album",
        "Orynivo a cessé de fonctionner",
        "Une erreur inattendue s’est produite. Un rapport a été enregistré ici :\n\n{0}\n\nOrynivo va maintenant se fermer.",
        "Une erreur inattendue s’est produite. Le rapport n’a pas pu être enregistré. Orynivo va maintenant se fermer.")
    {
        OutputType = "Type de sortie", AsioOutputDevice = "Périphérique de sortie ASIO", WasapiOutputDevice = "Périphérique de sortie WASAPI",
        CwAsioOutputDevice = "Périphérique de sortie cwASIO", SteinbergAsio = "Steinberg ASIO", CwAsio = "cwASIO",
        DeviceInfo = "Informations sur le périphérique", DatabaseOptimizeHint = "Les pages libérées sont supprimées afin de réduire physiquement le fichier.",
        AsioBridgeMissing = "Cette version ne comprend pas la prise en charge ASIO. Utilisez WASAPI.",
        KernelStreamingUnavailable = "Kernel Streaming peut être sélectionné, mais ce mode de lecture n’est pas encore implémenté.",
        AddMusicDirectory = "Ajouter un dossier musical", TrackCountTooltip = "Nombre de titres dans la base",
        Scan = "Analyser", RemoveDirectory = "Supprimer le dossier",
        ScanCompleted = "Terminé : {0} fichiers · {1} nouveaux · {2} actualisés · {3} supprimés{4}", ScanFailed = "Erreur : {0}",
        StartupPreparingLibrary = "Préparation de la bibliothèque …", Back = "Retour", MarkAsFavorite = "Ajouter aux favoris",
        PlaybackThrough = "Lecture via {0}",
        PlaybackThroughWithDsdConversion = "Lecture via {0} · Le DSD est converti en PCM ({1:N0} Hz)",
        NativeDsdOutput = "DSD natif", DsdToPcmOutput = "DSD → PCM",
        ReplayGain = "Ajustement du volume ReplayGain",
        ReplayGainHint = "S’applique à la lecture PCM. Le mode piste privilégie le gain de piste, le mode album le gain d’album. La sortie DSD native reste bit-perfect.",
        ReplayGainOff = "Désactivé", ReplayGainTrack = "Piste", ReplayGainAlbum = "Album",
        DsdPlayback = "Lecture DSD",
        AlwaysConvertDsdToPcm = "Toujours convertir les fichiers DSD en PCM",
        AlwaysConvertDsdToPcmHint = "Utilise également le chemin PCM avec ASIO/cwASIO afin d’appliquer le volume, ReplayGain et l’égaliseur. Lorsque cette option est désactivée, la sortie DSD native reste bit-perfect.",
        OutputDevicesLoading = "Chargement des périphériques de sortie …",
        Equalizer = "Égaliseur paramétrique",
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
        Homepage = "Page d’accueil", FeedUrl = "Adresse du flux",
        SearchResultSummary = "{0:N0} titres · {1:N0} albums · {2:N0} artistes",
        RecentAlbums = "Albums ajoutés récemment", Calendar = "Calendrier – {0}", TopGenres = "Top 10 des genres par durée d’écoute",
        NoData = "Aucune donnée disponible.", DevicePcmSampleRates = "Fréquences PCM prises en charge", DeviceDsdRates = "Niveaux DSD",
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
        NativeDsdUsesAsio = "La lecture DSD native de ce lecteur utilise ASIO.",
        Dashboard = "Tableau de bord", ThemeLight = "Clair", ThemeDark = "Sombre",
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
        , ArtistImageSearchTitle = "Rechercher une image d’artiste"
        , ArtistImageSearchRunning = "Recherche d’images d’artiste correspondantes …"
        , ArtistImageSearchNoResults = "Aucune image d’artiste trouvée."
        , ArtistImageSearchQuery = "Terme de recherche"
        , ArtistImageSearchFailed = "La recherche d’images d’artiste a échoué."
        , UseSelectedArtistImage = "Utiliser l’image sélectionnée"
        , ArtistImageDownloadFailed = "Impossible d’enregistrer l’image d’artiste sélectionnée."
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
        , Podcast = "Podcast"
        , PodcastAuthor = "Auteur"
        , PlayLatestEpisode = "Écouter le dernier"
        , AddToMyPodcasts = "Ajouter à mes podcasts"
        , DeletePodcast = "Supprimer le podcast"
        , PodcastLoading = "Chargement des podcasts …"
        , PodcastNoResults = "Aucun podcast correspondant trouvé."
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
        , LoadMore = "Charger plus"
        , FfmpegDownloading = "Téléchargement de FFmpeg …"
        , FfmpegDownloadFailed = "FFmpeg n'a pas pu être téléchargé. Veuillez l'installer manuellement : ffmpeg.org"
        , SmartPlaylistDialogTitle = "Modifier la playlist intelligente"
        , SmartPlaylistName = "Nom"
        , SmartPlaylistBasicFilters = "Filtres de base"
        , SmartPlaylistGenres = "Genres (séparés par des virgules)"
        , SmartPlaylistFormats = "Formats (par ex. FLAC, MP3 ; séparés par des virgules)"
        , SmartPlaylistBitrates = "Débits en kbps (séparés par des virgules)"
        , SmartPlaylistMetadata = "Métadonnées"
        , SmartPlaylistMinimumYear = "Année de début"
        , SmartPlaylistMaximumYear = "Année de fin"
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
        , SmartPlaylistUpdated = "Playlist intelligente « {0} » mise à jour."
        , ImportM3u8Playlist = "Importer une playlist M3U8"
        , ExportM3u8Playlist = "Exporter au format M3U8"
        , SaveAlbumAsPlaylist = "Enregistrer comme playlist"
        , AlbumPath = "Chemin de l’album"
        , UpNext = "À suivre"
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
    };

    private static readonly LocalizedStrings Spanish = new(
        "BIBLIOTECA LOCAL", "Artistas", "Álbumes", "Pistas", "Estructura de carpetas", "Búsqueda", "LISTAS", "Acerca de", "Ajustes",
        "Filtro", "Favoritos", "Tipos de audio", "Tasa de bits",
        "Ningún dispositivo seleccionado.", "Apariencia", "Esquema de color", "Idioma", "REPRODUCCIÓN", "Dispositivo de salida",
        "BIBLIOTECA", "Directorios", "+ Agregar directorio", "Mantenimiento de base de datos",
        "Optimizar base de datos", "Reparar portadas de álbum", "Descargar portadas faltantes",
        "La descarga automática solo encuentra portadas cuando hay un ID de MusicBrainz presente. Para búsquedas más libres, usa el botón directamente en la vista del álbum.",
        "Portada no encontrada", "Buscar portada", "Buscar portada", "Buscando portadas coincidentes …",
        "No se encontraron portadas.", "Término de búsqueda", "Buscar de nuevo", "Usar portada seleccionada",
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
        "Información del artista", "Mostrar información del artista", "Actualizar información del artista", "Cerrar información del artista",
        "Cargando información del artista …", "Descargando información del artista …",
        "No se encontró información del artista.", "No se pudo descargar la información del artista.",
        "Ninguna imagen descargada", "Archivo de imagen faltante", "Error al cargar la imagen",
        "Fuente: Wikipedia", "Fuente: Last.fm",
        "Fuente de información del artista", "Clave de API de Last.fm",
        "Crea una clave de API gratuita en: last.fm/api/account/create",
        "Mostrar todas las pistas del álbum",
        "Orynivo se ha bloqueado",
        "Se produjo un error inesperado. Se guardó un informe aquí:\n\n{0}\n\nOrynivo se cerrará ahora.",
        "Se produjo un error inesperado. No se pudo guardar el informe. Orynivo se cerrará ahora.")
    {
        OutputType = "Tipo de salida", AsioOutputDevice = "Dispositivo de salida ASIO", WasapiOutputDevice = "Dispositivo de salida WASAPI",
        CwAsioOutputDevice = "Dispositivo de salida cwASIO", SteinbergAsio = "Steinberg ASIO", CwAsio = "cwASIO",
        DeviceInfo = "Información del dispositivo", DatabaseOptimizeHint = "Las páginas liberadas se eliminan para reducir físicamente el archivo.",
        AsioBridgeMissing = "Esta compilación no incluye compatibilidad con ASIO. Utiliza WASAPI.",
        KernelStreamingUnavailable = "Kernel Streaming se puede seleccionar, pero todavía no está implementado como backend de reproducción.",
        AddMusicDirectory = "Agregar directorio de música", TrackCountTooltip = "Número de pistas en la base de datos",
        Scan = "Analizar", RemoveDirectory = "Eliminar directorio",
        ScanCompleted = "Finalizado: {0} archivos · {1} nuevos · {2} actualizados · {3} eliminados{4}", ScanFailed = "Error: {0}",
        StartupPreparingLibrary = "Preparando biblioteca …", Back = "Atrás", MarkAsFavorite = "Marcar como favorita",
        PlaybackThrough = "Reproducción mediante {0}",
        PlaybackThroughWithDsdConversion = "Reproducción mediante {0} · DSD se convierte a PCM ({1:N0} Hz)",
        NativeDsdOutput = "DSD nativo", DsdToPcmOutput = "DSD → PCM",
        ReplayGain = "Ajuste de volumen ReplayGain",
        ReplayGainHint = "Se aplica a la reproducción PCM. El modo pista prioriza la ganancia de pista y el modo álbum la ganancia de álbum. La salida DSD nativa sigue siendo bit-perfect.",
        ReplayGainOff = "Desactivado", ReplayGainTrack = "Pista", ReplayGainAlbum = "Álbum",
        DsdPlayback = "Reproducción DSD",
        AlwaysConvertDsdToPcm = "Convertir siempre los archivos DSD a PCM",
        AlwaysConvertDsdToPcmHint = "También usa la ruta PCM con ASIO/cwASIO para aplicar volumen, ReplayGain y ecualizador. Con esta opción desactivada, la salida DSD nativa sigue siendo bit-perfect.",
        OutputDevicesLoading = "Cargando dispositivos de salida …",
        Equalizer = "Ecualizador paramétrico",
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
        Homepage = "Página principal", FeedUrl = "Dirección del feed",
        SearchResultSummary = "{0:N0} pistas · {1:N0} álbumes · {2:N0} artistas",
        RecentAlbums = "Álbumes añadidos recientemente", Calendar = "Calendario – {0}", TopGenres = "10 géneros principales por tiempo de reproducción",
        NoData = "No hay datos disponibles.", DevicePcmSampleRates = "Frecuencias PCM compatibles", DeviceDsdRates = "Niveles DSD",
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
        NativeDsdUsesAsio = "La reproducción DSD nativa de este reproductor utiliza ASIO.",
        Dashboard = "Panel", ThemeLight = "Claro", ThemeDark = "Oscuro",
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
        , ArtistImageSearchTitle = "Buscar imagen del artista"
        , ArtistImageSearchRunning = "Buscando imágenes del artista …"
        , ArtistImageSearchNoResults = "No se encontraron imágenes del artista."
        , ArtistImageSearchQuery = "Término de búsqueda"
        , ArtistImageSearchFailed = "La búsqueda de imágenes del artista ha fallado."
        , UseSelectedArtistImage = "Usar imagen seleccionada"
        , ArtistImageDownloadFailed = "No se pudo guardar la imagen del artista seleccionada."
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
        , Podcast = "Podcast"
        , PodcastAuthor = "Autor"
        , PlayLatestEpisode = "Reproducir el último"
        , AddToMyPodcasts = "Añadir a mis podcasts"
        , DeletePodcast = "Eliminar podcast"
        , PodcastLoading = "Cargando podcasts …"
        , PodcastNoResults = "No se encontraron podcasts coincidentes."
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
        , LoadMore = "Cargar más"
        , FfmpegDownloading = "Descargando FFmpeg …"
        , FfmpegDownloadFailed = "No se pudo descargar FFmpeg. Instálelo manualmente: ffmpeg.org"
        , SmartPlaylistDialogTitle = "Editar lista inteligente"
        , SmartPlaylistName = "Nombre"
        , SmartPlaylistBasicFilters = "Filtros básicos"
        , SmartPlaylistGenres = "Géneros (separados por comas)"
        , SmartPlaylistFormats = "Formatos (por ejemplo FLAC, MP3; separados por comas)"
        , SmartPlaylistBitrates = "Bitrates en kbps (separados por comas)"
        , SmartPlaylistMetadata = "Metadatos"
        , SmartPlaylistMinimumYear = "Año desde"
        , SmartPlaylistMaximumYear = "Año hasta"
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
        , SmartPlaylistUpdated = "Lista inteligente '{0}' actualizada."
        , ImportM3u8Playlist = "Importar lista M3U8"
        , ExportM3u8Playlist = "Exportar como M3U8"
        , SaveAlbumAsPlaylist = "Guardar como lista"
        , AlbumPath = "Ruta del álbum"
        , UpNext = "A continuación"
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
    };

    /// <summary>Gets the currently active <see cref="LocalizedStrings"/> instance.</summary>
    public static LocalizedStrings Current { get; private set; } = German;
}
