using System.Globalization;
using WpfApplication = System.Windows.Application;

namespace Orynivo.Localization;

public static class LocalizationManager
{
    public static string FormatEntryCount(int count) =>
        count == 1 ? string.Format(Current.CountEntrySingular, count) : string.Format(Current.CountEntries, count);

    public static string FormatTrackCount(int count) =>
        count == 1 ? string.Format(Current.CountTrackSingular, count) : string.Format(Current.CountTracks, count);

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

        var resources = WpfApplication.Current.Resources;
        resources["L_LocalLibrary"] = Current.LocalLibrary;
        resources["L_Artists"] = Current.Artists;
        resources["L_Albums"] = Current.Albums;
        resources["L_Tracks"] = Current.Tracks;
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
        resources["L_NewPlaylist"] = Current.NewPlaylist;
        resources["L_NewPlaylistDialogTitle"] = Current.NewPlaylistDialogTitle;
        resources["L_NewPlaylistNameLabel"] = Current.NewPlaylistNameLabel;
        resources["L_CreatePlaylist"] = Current.CreatePlaylist;
        resources["L_SaveSmartPlaylistDisabledTooltip"] = Current.SaveSmartPlaylistDisabledTooltip;
        resources["L_SaveSmartPlaylist"] = Current.SaveSmartPlaylist;
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
        ScanCompleted = "Fertig: {0} Dateien · {1} neu · {2} aktualisiert{3}",
        ScanFailed = "Fehler: {0}",
        StartupPreparingLibrary = "Bibliothek wird vorbereitet …",
        Back = "Zurück",
        MarkAsFavorite = "Als Favorit markieren",
        PlaybackThrough = "Wiedergabe über {0}",
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
        ScanCompleted = "Finished: {0} files · {1} new · {2} updated{3}", ScanFailed = "Error: {0}",
        StartupPreparingLibrary = "Preparing library …", Back = "Back", MarkAsFavorite = "Mark as favorite",
        PlaybackThrough = "Playback through {0}", SearchResultSummary = "{0:N0} tracks · {1:N0} albums · {2:N0} artists",
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
        ScanCompleted = "Terminé : {0} fichiers · {1} nouveaux · {2} actualisés{3}", ScanFailed = "Erreur : {0}",
        StartupPreparingLibrary = "Préparation de la bibliothèque …", Back = "Retour", MarkAsFavorite = "Ajouter aux favoris",
        PlaybackThrough = "Lecture via {0}", SearchResultSummary = "{0:N0} titres · {1:N0} albums · {2:N0} artistes",
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
        ScanCompleted = "Finalizado: {0} archivos · {1} nuevos · {2} actualizados{3}", ScanFailed = "Error: {0}",
        StartupPreparingLibrary = "Preparando biblioteca …", Back = "Atrás", MarkAsFavorite = "Marcar como favorita",
        PlaybackThrough = "Reproducción mediante {0}", SearchResultSummary = "{0:N0} pistas · {1:N0} álbumes · {2:N0} artistas",
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
    };

    public static LocalizedStrings Current { get; private set; } = German;
}
