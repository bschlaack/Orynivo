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
        resources["L_Playlists"] = Current.Playlists;
        resources["L_Settings"] = Current.Settings;
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
        resources["L_Save"] = Current.Save;
        resources["L_Cancel"] = Current.Cancel;
    }

    private static readonly LocalizedStrings German = new(
        "LOKALE BIBLIOTHEK", "Künstler", "Alben", "Tracks", "Ordnerstruktur", "PLAYLISTS", "⚙   Einstellungen",
        "Kein Gerät ausgewählt.", "Darstellung", "Farbschema", "Sprache", "WIEDERGABE", "Ausgabegerät",
        "BIBLIOTHEK", "Verzeichnisse", "+ Verzeichnis hinzufügen", "Datenbankwartung",
        "Datenbank optimieren", "Album-Cover reparieren", "Speichern", "Abbrechen", "Tabelle", "Artwork",
        "(Unbekannt)", "Album-Künstler", "Jahr", "Titel", "Künstler", "Album", "Genre", "Dauer", "Format",
        "{0:N0} Einträge", "{0:N0} Titel",
        "Bitte zuerst einen Track doppelklicken.", "Wiedergabe gestoppt.", "Wiedergabe beendet.",
        "Bitte zuerst ein ASIO-Gerät in den Einstellungen auswählen.", "Bitte zuerst ein WASAPI-Gerät in den Einstellungen auswählen.",
        "{0} ist noch nicht implementiert.", "Einstellungen gespeichert.", "Geräteinfo konnte nicht gelesen werden: {0}",
        "Keine aktiven WASAPI-Ausgabegeräte gefunden.", "Keine ASIO-Treiber gefunden.", "Gerät auswählen und speichern.",
        "Scan läuft…", "Verzeichnis nicht gefunden.", "Scan abgebrochen.", "Datenbank wird optimiert …",
        "Optimierung abgeschlossen.", "Optimierung fehlgeschlagen: {0}", "Album-Cover werden repariert …",
        "{0:N0} Album-Cover repariert.", "Cover-Reparatur fehlgeschlagen: {0}");

    private static readonly LocalizedStrings English = new(
        "LOCAL LIBRARY", "Artists", "Albums", "Tracks", "Folder structure", "PLAYLISTS", "⚙   Settings",
        "No device selected.", "Appearance", "Color scheme", "Language", "PLAYBACK", "Output device",
        "LIBRARY", "Directories", "+ Add directory", "Database maintenance",
        "Optimize database", "Repair album artwork", "Save", "Cancel", "Table", "Artwork",
        "(Unknown)", "Album artist", "Year", "Title", "Artist", "Album", "Genre", "Duration", "Format",
        "{0:N0} entries", "{0:N0} tracks",
        "Please double-click a track first.", "Playback stopped.", "Playback finished.",
        "Please select an ASIO device in settings first.", "Please select a WASAPI device in settings first.",
        "{0} is not implemented yet.", "Settings saved.", "Device info could not be read: {0}",
        "No active WASAPI output devices found.", "No ASIO drivers found.", "Select a device and save.",
        "Scanning…", "Directory not found.", "Scan canceled.", "Optimizing database …",
        "Optimization completed.", "Optimization failed: {0}", "Repairing album artwork …",
        "{0:N0} album covers repaired.", "Artwork repair failed: {0}");

    private static readonly LocalizedStrings French = new(
        "BIBLIOTHÈQUE LOCALE", "Artistes", "Albums", "Titres", "Arborescence", "PLAYLISTS", "⚙   Paramètres",
        "Aucun appareil sélectionné.", "Apparence", "Thème", "Langue", "LECTURE", "Périphérique de sortie",
        "BIBLIOTHÈQUE", "Dossiers", "+ Ajouter un dossier", "Maintenance de la base",
        "Optimiser la base", "Réparer les pochettes", "Enregistrer", "Annuler", "Tableau", "Pochettes",
        "(Inconnu)", "Artiste de l’album", "Année", "Titre", "Artiste", "Album", "Genre", "Durée", "Format",
        "{0:N0} entrées", "{0:N0} titres",
        "Veuillez d’abord double-cliquer sur un titre.", "Lecture arrêtée.", "Lecture terminée.",
        "Veuillez d’abord sélectionner un périphérique ASIO dans les paramètres.", "Veuillez d’abord sélectionner un périphérique WASAPI dans les paramètres.",
        "{0} n’est pas encore implémenté.", "Paramètres enregistrés.", "Impossible de lire les informations du périphérique : {0}",
        "Aucun périphérique de sortie WASAPI actif trouvé.", "Aucun pilote ASIO trouvé.", "Sélectionnez un périphérique puis enregistrez.",
        "Analyse en cours…", "Dossier introuvable.", "Analyse annulée.", "Optimisation de la base …",
        "Optimisation terminée.", "Échec de l’optimisation : {0}", "Réparation des pochettes …",
        "{0:N0} pochettes réparées.", "Échec de la réparation des pochettes : {0}");
}
