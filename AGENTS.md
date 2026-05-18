# AGENTS.md

## Projektüberblick

Windows-Audioplayer mit:

- WPF-Frontend in `Player/`
- nativer ASIO-Bridge in `Native/AsioBridge/`
- PCM-Wiedergabe über `ffmpeg`
- nativer DSF/DFF-DSD-Wiedergabe über ASIO

## Build und Start

```powershell
.\build.ps1
.\Player\bin\Debug\net8.0-windows\Player.exe
```

`build.ps1` baut zuerst `AsioBridge.dll`, danach die .NET-App.

## Wichtige Architektur

- `Player/Audio/SteinbergAsioStream.cs`: C#-Wrapper für die native Bridge
- `Player/Audio/FfmpegAudioPlayer.cs`: PCM-Pfad
- `Player/Audio/DsfAudioPlayer.cs`: nativer DSF→DSD-Pfad
- `Player/Audio/DffAudioPlayer.cs`: nativer DFF/DSDIFF→DSD-Pfad
- `Player/Audio/WasapiAudioPlayer.cs`: WASAPI-PCM-Pfad
- `Player/Audio/WasapiDeviceProvider.cs`: WASAPI-Geräte und Capability-Abfragen
- `Native/AsioBridge/bridge.cpp`: ASIO-Initialisierung, PCM-/DSD-Ringpuffer, Callback
- `Player/SettingsWindow.*`: Zweispaltiges Einstellungsfenster – links Navigationsleiste mit Oberpunkten (WIEDERGABE, BIBLIOTHEK), rechts der Inhaltsbereich für den gewählten Punkt
- `Player/ThemeManager.cs`: setzt globale WPF-Ressourcen für helles/dunkles Farbschema
- `Player/Localization/*`: Sprachmodell und lokalisierte Texte für Deutsch, Englisch und Französisch
- `Player/StartupWindow.*`: schlanker Splashscreen während der initialen Datenbankvorbereitung/Migration
- `Player/SettingsStore.cs`: persistiert `%LOCALAPPDATA%\Player\settings.json`
- `AppSettings.LastMainView` und `AppSettings.AlbumArtworkView` speichern die zuletzt gewählte Hauptansicht sowie den Album-Modus (Tabelle/Artwork) und werden beim nächsten Start wiederhergestellt
- `AppSettings.Volume` und `AppSettings.LastTrackPath` speichern Lautstärke und zuletzt ausgewählten/abgespielten Track; beim Start werden sie defensiv wiederhergestellt, nur wenn Datei und DB-Eintrag noch existieren
- `AppSettings.Theme` speichert das Farbschema (`Light`/`Dark`)
- `AppSettings.Language` speichert die UI-Sprache (`German`/`English`/`French`)
- `Player/Library/TrackRecord.cs`: C#-Modell für einen DB-Track-Eintrag (ID3 + technische Metadaten)
- `Player/Library/PlaylistRecord.cs`: C#-Modell für eine Playlist (inkl. denormalisiertem `TrackCount`)
- `Player/Library/PlaylistTrackRecord.cs`: C#-Modell für einen Playlist-Eintrag (Position, optionale TrackId-Referenz, immer Pfad)
- `Player/Library/AudioDatabase.cs`: SQLite-Datenbankschicht (via `Microsoft.Data.Sqlite`); DB-Datei unter `%LOCALAPPDATA%\Player\library.db`
- `Player/Library/LibraryScanner.cs`: Verzeichnis-Scanner; liest Metadaten via TagLibSharp und schreibt sie per `AudioDatabase.Upsert()` in die DB; unterstützt Fortschritts-Reporting (`IProgress<ScanProgress>`) und Abbruch per `CancellationToken`

## Audiodatenbank

- Datenbank: SQLite via `Microsoft.Data.Sqlite` (cross-platform, keine Serverinstanz)
- Schema: `tracks` enthält Dateipfad, ID3-Tags, technische Metadaten sowie Referenzen auf normalisierte `artists` und `albums`
- `artists`: stabile Künstler-IDs (`id`, `name`) – vorbereitet für spätere Künstler-Favoriten
- `artists`, `albums` und `tracks` besitzen jeweils `is_favorite` als direktes Bool-Flag für UI-nahe Favoriten
- `albums`: stabile Album-IDs (`id`, `title`, `artist_id`, `year`, `artwork_id`, `is_favorite`)
- `artworks`: deduplizierte Cover-Metadaten, eindeutig über SHA-256-Hash; Originale und vorberechnete Thumbnails liegen dateibasiert unter `%LOCALAPPDATA%\Player\artworks\` (`original`, `thumb_96`, `thumb_320`)
- `favorites`: ältere generische Vorbereitung für spätere Erweiterungen; die sichtbare Favoritenfunktion nutzt aktuell die direkten `is_favorite`-Spalten an Tracks, Künstlern und Alben
- `play_history`: speichert Wiedergabestarts und -enden (`track_id`, Pfad, Start-/Endzeit, Dauer, Endposition, abgeschlossen ja/nein) als Basis für spätere Vorlieben, Vorschläge und „zuletzt gehört“
- `AudioDatabase.OpenDefault()` legt die DB unter `%LOCALAPPDATA%\Player\library.db` an
- `Upsert()` ist idempotent (INSERT … ON CONFLICT DO UPDATE) — geeignet für Re-Scans
- `GetPathTimestamps()` liefert Pfad+ModifiedAt für effiziente Änderungserkennung beim Scan
- WAL-Journal-Mode aktiv für bessere Concurrency
- Mehrere Bibliotheksverzeichnisse möglich (`AppSettings.LibraryPaths: List<string>`), persistent in `settings.json`
- Jedes Verzeichnis hat im Settings-Fenster einen eigenen „Scannen"-Button (wird zu „Abbrechen" während der Scan läuft); Fortschrittstext direkt unter dem jeweiligen Eintrag
- Verzeichnisse können über „+ Verzeichnis hinzufügen" ergänzt und per × entfernt werden; laufende Scans werden beim Entfernen oder Schließen des Fensters abgebrochen
- Scan überspringt unveränderte Dateien (ModifiedAt-Vergleich); `added_at` wird beim Update nicht überschrieben
- Metadaten-Extraktion via TagLibSharp (ID3v1/v2, Vorbis Comments, APE Tags, Cover Art)
- Beim Öffnen der DB wird eine Bestandsmigration durchgeführt: Künstler, Alben und Artworks werden normalisiert; alte pro-Track-Cover-BLOBs werden nach `artworks` verschoben und in `tracks` geleert
- Eine zweite Einmalmigration (`album_artist_rebuild_v1`) baut Albumzuordnungen strikt aus `album_artist` neu auf, damit Compilations in der Albumansicht nicht über alle Track-Interpreten aufgefächert werden
- Eine weitere Einmalmigration (`album_title_uniqueness_v1`) verdichtet Alben auf eindeutige Titel; bei mehreren Album-Interpreten wird für Anzeige und Albumzuordnung der erste Interpret verwendet
- `RebuildAlbumsFromAlbumArtists()` muss vorhandene `artwork_id`-Zuordnungen bewahren; falls historische Album-Cover-Zuordnungen fehlen, gibt es im Settings-Fenster unter Bibliothek den Wartungspunkt „Album-Cover reparieren“, der pro Album eine Beispieldatei erneut via TagLib ausliest und das Cover wieder anhängt
- Unter Bibliothek gibt es zusätzlich den Wartungspunkt „Fehlende Cover-Artworks herunterladen“; er lädt fehlende Albumcover für Alben mit vorhandenem `musicbrainz_release_id` aus dem öffentlichen Cover Art Archive nach
- Fehlende Cover in der Album-Artworkansicht zeigen einen Platzhalter mit manueller Download-Schaltfläche; diese öffnet eine freie MusicBrainz-Suche nach Albumtitel, zeigt gefundene Cover zur Auswahl und übernimmt das gewählte Cover ins lokale Artwork-System
- Der manuelle Cover-Suchdialog übernimmt die thematisierte native Titelleiste und ist kompakt auf die Ergebnisliste zugeschnitten
- Der manuelle Cover-Suchdialog zeigt während der Suche eine Aktivitätsanimation und meldet explizit, wenn keine Cover gefunden wurden
- Der Suchbegriff im manuellen Cover-Suchdialog ist editierbar; die Suche kann beliebig erneut ausgeführt werden
- Albumcover besitzen in der Artworkansicht ein Kontextmenü zum Löschen oder zur Neu-Zuordnung über die manuelle MusicBrainz-Suche
- Das Hauptfenster startet maximiert
- Einmalmigration `artwork_files_v1` exportiert bestehende BLOB-Artworks in den dateibasierten Cache. Aus Kompatibilitätsgründen bleibt `artworks.data` in Bestandsdatenbanken vorerst erhalten, da ältere Schemas die Spalte als `NOT NULL` angelegt haben; ein späterer expliziter Schema-Rebuild kann sie entfernen.
- Thumbnail-Erzeugung ist absichtlich fehlertolerant: exotische oder defekte eingebettete Cover dürfen keinen Startabbruch verursachen; in diesem Fall bleibt nur das Original erhalten und die UI zeigt für dieses Bild ggf. keinen Thumbnail-Cache.
- Ein `app_meta`-Eintrag (`normalized_library_v1`) verhindert, dass die teure Bestandsmigration bei jedem DB-Open erneut geprüft wird
- `AudioDatabase.Optimize()` führt `wal_checkpoint(TRUNCATE)`, `VACUUM` und `ANALYZE` aus; im Settings-Fenster gibt es unter Bibliothek einen Button „Datenbank optimieren“

## Playlisten-Datenbankstruktur

- Tabelle `playlists`: id, name, description, created_at, modified_at
- Tabelle `playlist_tracks`: id, playlist_id (FK→playlists, CASCADE), track_id (FK→tracks, SET NULL), path, position (1-basiert, lückenlos), added_at
- `track_id` ist nullable: Playlist-Einträge bleiben erhalten, wenn ein Track aus der Bibliothek entfernt wird; `path` ist immer gesetzt
- Verfügbare Methoden: `CreatePlaylist`, `UpdatePlaylist`, `DeletePlaylist`, `GetAllPlaylists` (inkl. TrackCount via JOIN), `GetPlaylistById`, `GetPlaylistTracks`, `AddTrackToPlaylist`, `RemoveTrackFromPlaylist`, `MovePlaylistTrack` (transaktional, nummeriert Positionen neu)

## Performance-Maßnahmen

- `AudioDatabase.GetTracksLite()`: schlanke Abfrage (nur path, file_name, title, disc_number, track_number) – kein Cover-Art-BLOB, keine Lyrics; wird für die Ordnerstruktur-Ansicht verwendet
- `AudioDatabase.GetArtistsLite()`: lädt für die Künstleransicht ausschließlich distinct-Künstlernamen direkt per SQL – keine vollständigen Track-Datensätze
- `AudioDatabase.GetAlbumsLite(includeArtwork)`: lädt standardmäßig nur Album, Anzeige-Künstler und Jahr; Artwork-BLOBs werden nur für die Kachelansicht mitgeladen
- `AudioDatabase.GetTrackList()`: lädt für die Trackliste nur die tatsächlich sichtbaren Spalten – kein Cover-Art-BLOB, keine Lyrics, kein Voll-`TrackRecord`
- `AudioDatabase.GetTracksByDirectory(dirPath)`: SQL-LIKE-Abfrage auf Verzeichnis-Prefix + C#-Filter für direkte Kinder (kein `GetAll()` + LINQ)
- Ordnerstruktur-Ansicht: lazy loading – `FolderTree`-Klasse baut eine in-memory Parent→Children-Map (O(1)-Lookup); WPF-`TreeViewItem`-Objekte werden erst beim Aufklappen eines Knotens erzeugt (`Expanded`-Event + Platzhalter-Technik)
- WPF-TreeView: `VirtualizingStackPanel.IsVirtualizing="True"` + `VirtualizationMode=Recycling` – nur sichtbare Zeilen werden gerendert
- `TrackLite`, `TrackListInfo`, `ArtistInfo` und `AlbumInfo` in `Player/Library/AudioDatabase.cs` halten Listenansichten bewusst schlank; vollständiger `TrackRecord` bleibt für Wiedergabe, Playlist- und Metadaten-Abfragen
- Artwork wird nicht mehr pro Track gespeichert, sondern dedupliziert über `artworks`; dadurch bleiben Albumlisten trotz Cover-Unterstützung beherrschbar
- `Player/Library/TrackSearchIndex.cs`: Lucene.NET-Dateiindex unter `%LOCALAPPDATA%\Player\search-index`; indiziert Track-Metadaten und technische Felder für performante Volltextsuche, wird beim ersten Start oder bei leer erkanntem Index aus der DB aufgebaut, nach Scans inkrementell aktualisiert und entfernt bei Re-Scans verschwundene Dateien unter dem jeweiligen Bibliotheks-Root

## Bekannte technische Details

- Zielplattform: `net8.0-windows`, x64
- Native DSD-Wiedergabe ist aktuell für `.dsf` und unkomprimierte Stereo-`.dff` implementiert
- `.dff` mit DST-Kompression wird derzeit nicht nativ abgespielt
- Ausgabearten sind im Settings-Modell vorbereitet: `ASIO`, `WASAPI`, `KernelStreaming`
- `ASIO` und `WASAPI` sind echte Wiedergabe-Backend-Pfade
- `KernelStreaming` ist vorbereitet, aber noch nicht implementiert
- WASAPI wird derzeit für PCM-Wiedergabe genutzt; natives DSD bleibt ASIO vorbehalten
- WASAPI läuft exklusiv und wählt für PCM das erste passende Stereoformat aus 32-Bit Float, 24-Bit PCM und 16-Bit PCM
- Transport-UI nutzt zentral drei selbst gezeichnete Vektor-Icons für Skip Back, Play/Pause und Skip Forward; der mittlere Button wechselt je nach Wiedergabestatus zwischen Play und Pause, an Queue-Anfang/-Ende werden die jeweiligen Navigationsbuttons deaktiviert
- Seeking ist aktuell für ASIO-PCM, WASAPI-PCM sowie native DSF/DFF-Pfade implementiert
- Playlist-Grundfunktion ist vorhanden: Einzeldatei oder Ordner laden, Doppelklick startet einen Eintrag, nach Titelende folgt automatisch der nächste
- Playlist-Tabelle ist höhenbegrenzt und scrollbar, damit Transportelemente sichtbar bleiben
- Lautstärkeregler wirkt auf PCM-Pfade; natives DSD bleibt bitgenau und wird nicht digital abgesenkt
- Im ASIO-DSD-Modus zählt `preferredBufferSize` Samples, nicht Bytes; bei `ASIOSTDSDInt8*` werden daher `preferredBufferSize / 8` Bytes pro Kanal geschrieben
- ASIO-Capability-Abfragen können fehlschlagen, wenn andere Programme das Gerät belegen

## UI-Leitlinie

- **Hauptfenster** – dreispaltiges modernes Layout:
  - Linke Sidebar (220 px, dunkel `#13142A`): Navigation mit Oberpunkten LOKALE BIBLIOTHEK (Künstler, Alben, Tracks, Ordnerstruktur) und PLAYLISTS (dynamisch aus DB); Geräte-Info oben; Einstellungen-Button unten
- Über-Schaltfläche unten links oberhalb der Einstellungen öffnet ein thematisiertes About-Fenster mit Autor und Bibliothekslizenzen
- Das About-Fenster weist zusätzlich auf ASIO als Marke/Software von Steinberg Media Technologies GmbH hin
  - Rechter Inhaltsbereich: dunkler Header mit Titel + Anzahl als Fortsetzung der nativen Titelleiste/Sidebar; darunter je nach Ansicht ein `DataGrid` oder (für Ordnerstruktur) ein `TreeView`; Doppelklick startet Wiedergabe
  - Transport-Leiste unten (dunkel, volle Breite): Now-Playing-Info links, Steuerung (⏹ ▶ ⏸) + Positionsslider mittig, Lautstärke rechts
- **Settings-Fenster**: zweispaltiges Layout – Navigationsleiste links, Inhalt rechts (Ausgabegerät / Bibliotheksverzeichnisse / Darstellung mit Farbschema und Sprache)
- Die Navigation im Settings-Fenster verwendet dieselben thematisierten Sidebar-Ressourcen wie das Hauptfenster (Oberpunkte, Hover, Auswahl, Textfarben)
- Alle Buttons im Settings-Fenster verwenden einen gemeinsamen Theme-Button-Stil; dynamisch erzeugte Scan-/Entfernen-Schaltflächen müssen diesen Stil ebenfalls übernehmen
- ComboBoxen im Settings-Fenster verwenden thematisierte Eingabefarben; der Geräteinfo-Dialog folgt ebenfalls dem aktiven Theme inkl. nativer Titelleiste
- DSD-Stufen in der Geräteinfo nutzen explizite Theme-Farben für unterstützt/nicht unterstützt; ComboBoxen benötigen eine vollständige Template-Färbung, nicht nur äußere Brush-Setter
- **Start**: vor dem Hauptfenster erscheint ein Splashscreen, solange die initiale DB-Vorbereitung/Migration läuft
- Geräteinfo zeigt Kanäle, Buffer, PCM-Sampleraten, DSD-Stufen und lesbar übersetzte Rohformate
- Die native Windows-Titelleiste wird auf unterstützten Systemen per DWM farblich an die dunkle Sidebar angepasst; auf älteren Systemen bleibt der OS-Standard erhalten
- Es gibt ein helles und ein dunkles Farbschema; Tabellen, Artworkansicht, Flächen und Textfarben werden über globale DynamicResources umgeschaltet
- Sichtbare Haupttexte werden über `LocalizationManager`/Sprachressourcen verwaltet; aktuell vorhanden: Deutsch, Englisch, Französisch
- Auch Settings-Dialog und zentrale Status-/Laufzeitmeldungen greifen auf die Sprachressourcen zu
- Auch die Transportleiste unten folgt dem gewählten Farbschema; im hellen Theme wird sie hell gerendert
- Sidebar-Hover und aktive Auswahl besitzen eigene Theme-Ressourcen, damit sie im hellen Farbschema nicht wie Fremdkörper aus dem dunklen Theme wirken
- Die Einstellungen-Schaltfläche übernimmt dieselben Sidebar-Ressourcen; leere Now-Playing-Artworkflächen nutzen eine eigene thematisierte Placeholder-Farbe
- Native Titelleisten von Haupt- und Settings-Fenster werden passend zum aktiven Theme eingefärbt; Trenner und Scrollbars nutzen ebenfalls Theme-Ressourcen
- Tabellen, Listen und Baumansichten besitzen globale thematisierte Standardflächen, damit kein weißer WPF-Default-Hintergrund in dunklen Ansichten durchscheint
- Das Hauptfenster überschreibt zusätzlich die DataGrid-Flächen inkl. ScrollViewer-Hintergrund lokal, damit auch die leeren Restbereiche neben Tabellen dunkel bleiben
- DataGrid-Row-Header sind global deaktiviert (`HeadersVisibility="Column"`), damit links keine zusätzliche helle Auswahlspalte erscheint

## Inhaltsansichten (Hauptfenster)

- **Künstler**: distinct-Künstler-Liste (eine Zeile pro Künstler, alphabetisch); Doppelklick öffnet die Alben, auf denen dieser Künstler vorkommt
- **Alben**: normalisierte Album-Liste (eine Zeile pro eindeutigem Albumtitel; alphabetisch nach Albumtitel aufsteigend; Spalten: Herz, kleines 96px-Thumbnail, Album, Album-Künstler, Jahr) plus umschaltbare Artwork-Kachelansicht. In der Albumansicht wird der Album-Künstler gezeigt, bei mehreren Album-Interpreten nur der erste; Track-Interpreten werden dafür nicht zusammengeführt. Kacheln verwenden 320px-Thumbnails; sowohl Tabellen-Thumbnails als auch Kachelbilder werden erst beim Laden sichtbarer Elemente in `ImageSource` umgewandelt. Doppelklick auf ein Album öffnet eine nach diesem Album gefilterte Trackansicht.
- `ContentRow` implementiert `INotifyPropertyChanged`, damit nachträglich geladene Thumbnails/Artworks sofort sichtbar werden und nicht erst nach Scroll-Recycling der UI.
- **Now Playing**: zeigt links neben den Titelinformationen ein 96px-Thumbnail des gerade laufenden Tracks
- `Player/Controls/VirtualizingWrapPanel.cs`: eigenes virtualisierendes WrapPanel für die Album-Artwork-Kacheln; hält das Coverraster visuell bei, materialisiert aber nur sichtbare Elemente
- **Album-Trackansicht**: nutzt `AudioDatabase.GetTrackListByAlbum(albumId)`, sortiert nach Disc → Tracknummer → Dateiname; Doppelklick startet einen Track und verwendet alle sichtbaren Albumtitel als Queue, sodass nach Titelende automatisch der nächste Albumtitel folgt.
- **Favoriten**: Künstler-, Album- und Tracklisten zeigen eine Herzspalte (`♡`/`♥`) zum direkten Umschalten des jeweiligen `is_favorite`-Flags; Album-Kacheln zeigen ebenfalls ein Herz.
- **Zurück-Navigation**: interne Drill-downs merken sich die vorherige Auswahl. Von Albumtracks geht es zurück zum zuvor gewählten Album; von Künstleralben zurück zum zuvor gewählten Künstler. Die Schaltfläche im Header nutzt ein appliktionskonformes, pillenförmiges Icon-Button-Design mit Chevron.
- Ein expliziter Klick auf einen Eintrag der linken Hauptnavigation setzt alle internen Drill-down-Filter zurück; Sidebar-Navigation zeigt immer die ungefilterte Top-Level-Ansicht. Auch erneutes Anklicken des bereits markierten Sidebar-Eintrags stellt die ungefilterte Hauptansicht wieder her.
- **Tracks**: alle Tracks sortiert nach Titel; Doppelklick spielt Track und baut Queue aus allen sichtbaren Einträgen. Im Header steht ein Filter-Dropdown: Favoriten liegt direkt auf oberster Ebene, Genre, Audiotyp und Bitrate sind standardmäßig eingeklappt und einzeln aufklappbar; die Werte sind per Checkbox kombinierbar, jede Facette zeigt die unter den übrigen aktiven Filtern verfügbare Trefferzahl und blendet nicht mehr passende, nicht ausgewählte Werte aus.
- **Suche**: Eingabefeld im Header; Suchanfragen werden kurz verzögert gegen den Lucene.NET-Index ausgeführt und liefern eine dreigeteilte Ergebnisseite für Tracks, Alben und Künstler. Die drei Bereiche erscheinen als thematisierte Karten mit Mindesthöhe statt als gequetschte Standardtabellen. Doppelklick auf Tracks spielt die sichtbare Trefferliste, Doppelklick auf Alben/Künstler nutzt dieselben Drill-downs wie die Hauptnavigation.
- Drill-downs aus der Suchseite merken sich die ursprüngliche Suchanfrage; die Zurück-Schaltfläche führt wieder auf dieselbe Ergebnisansicht mit identischem Suchstand zurück
- **Ordnerstruktur**: `TreeView` (statt DataGrid); Wurzelelemente sind die in den Einstellungen konfigurierten Bibliotheksverzeichnisse (voller Pfad als Label, initial aufgeklappt); Unterordner werden mit Ordnernamen angezeigt und sind initial zugeklappt; Tracks als Blattknoten (Titel oder Dateiname); Doppelklick auf einen Track spielt alle Tracks desselben Ordners als Queue ab (sortiert nach Disc → Track-Nr. → Dateiname)
- **Playlists**: Playlist-Tracks mit Positions-Nr., Titel, Künstler, Album, Dauer

## Pflegehinweis

Diese Datei bei Architektur-, Build- oder Verhaltensänderungen mitpflegen.

## Lokalisierungsregel

- Neue sichtbare UI-Texte und neue Status-/Fehlermeldungen dürfen nicht hart im XAML oder Code-Behind verdrahtet werden.
- Alle Texte müssen über `Player/Localization/` ausgelagert werden.
- Bei neuen oder geänderten Texten sind alle unterstützten Sprachen vollständig mitzuführen: aktuell Deutsch, Englisch und Französisch.
- Änderungen gelten erst als vollständig, wenn die neuen Texte in allen Sprachressourcen abgelegt und sinnvoll übersetzt sind.

## Settings-Layout-Regel

- Eingabefelder im Einstellungen-Dialog folgen einheitlich dem Muster: Label in einer eigenen Zeile, darunter das zugehörige Feld über die verfügbare Breite.
- Neue ComboBoxen oder vergleichbare Eingaben dürfen nicht daneben in Sonderlayouts gesetzt werden, sofern es keinen klaren funktionalen Grund dafür gibt.
