using WpfApplication = System.Windows.Application;

namespace Player.Localization;

public static class LocalizationManager
{
    public static LocalizedStrings Current { get; private set; } = German;

    public static void Apply(Language language)
    {
        Current = language switch
        {
            Language.English => English,
            Language.French => French,
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
        resources["L_LibraryBackup"] = Current.LibraryBackup;
        resources["L_LibraryBackupHint"] = Current.LibraryBackupHint;
        resources["L_ExportLibrary"] = Current.ExportLibrary;
        resources["L_ImportLibrary"] = Current.ImportLibrary;
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
        "Bibliothek wurde importiert. Der Player wird jetzt beendet und kann anschließend neu gestartet werden.",
        "Bibliothek konnte nicht importiert werden: {0}",
        "Bitte laufende Bibliotheksscans oder Wartungsarbeiten zuerst beenden.",
        "Player-Bibliothek (*.zip)|*.zip",
        "Bibliothek wird exportiert: {0}% – {1}",
        "Bibliothek wird importiert: {0}% – {1}");

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
        "Library imported. Player will now close and can then be restarted.",
        "Library import failed: {0}",
        "Please finish active library scans or maintenance operations first.",
        "Player library (*.zip)|*.zip",
        "Exporting library: {0}% – {1}",
        "Importing library: {0}% – {1}");

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
        "Bibliothèque Player (*.zip)|*.zip",
        "Exportation de la bibliothèque : {0}% – {1}",
        "Importation de la bibliothèque : {0}% – {1}");
}
