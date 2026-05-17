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
- `Player/StartupWindow.*`: schlanker Splashscreen während der initialen Datenbankvorbereitung/Migration
- `Player/SettingsStore.cs`: persistiert `%LOCALAPPDATA%\Player\settings.json`
- `Player/Library/TrackRecord.cs`: C#-Modell für einen DB-Track-Eintrag (ID3 + technische Metadaten)
- `Player/Library/PlaylistRecord.cs`: C#-Modell für eine Playlist (inkl. denormalisiertem `TrackCount`)
- `Player/Library/PlaylistTrackRecord.cs`: C#-Modell für einen Playlist-Eintrag (Position, optionale TrackId-Referenz, immer Pfad)
- `Player/Library/AudioDatabase.cs`: SQLite-Datenbankschicht (via `Microsoft.Data.Sqlite`); DB-Datei unter `%LOCALAPPDATA%\Player\library.db`
- `Player/Library/LibraryScanner.cs`: Verzeichnis-Scanner; liest Metadaten via TagLibSharp und schreibt sie per `AudioDatabase.Upsert()` in die DB; unterstützt Fortschritts-Reporting (`IProgress<ScanProgress>`) und Abbruch per `CancellationToken`

## Audiodatenbank

- Datenbank: SQLite via `Microsoft.Data.Sqlite` (cross-platform, keine Serverinstanz)
- Schema: `tracks` enthält Dateipfad, ID3-Tags, technische Metadaten sowie Referenzen auf normalisierte `artists` und `albums`
- `artists`: stabile Künstler-IDs (`id`, `name`) – vorbereitet für spätere Künstler-Favoriten
- `albums`: stabile Album-IDs (`id`, `title`, `artist_id`, `year`, `artwork_id`) – vorbereitet für spätere Album-Favoriten
- `artworks`: deduplizierte Cover-BLOBs, eindeutig über SHA-256-Hash; dieselbe eingebettete Grafik wird nur einmal gespeichert
- `favorites`: vorbereitet für spätere Favoriten auf Track-, Künstler- oder Albumebene (`target_type`, `target_id`)
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
- `RebuildAlbumsFromAlbumArtists()` muss vorhandene `artwork_id`-Zuordnungen bewahren; falls historische Album-Cover-Zuordnungen fehlen, gibt es im Settings-Fenster unter Bibliothek den Wartungspunkt „Album-Cover reparieren“, der pro Album eine Beispieldatei erneut via TagLib ausliest und das Cover wieder anhängt
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

## Bekannte technische Details

- Zielplattform: `net8.0-windows`, x64
- Native DSD-Wiedergabe ist aktuell für `.dsf` und unkomprimierte Stereo-`.dff` implementiert
- `.dff` mit DST-Kompression wird derzeit nicht nativ abgespielt
- Ausgabearten sind im Settings-Modell vorbereitet: `ASIO`, `WASAPI`, `KernelStreaming`
- `ASIO` und `WASAPI` sind echte Wiedergabe-Backend-Pfade
- `KernelStreaming` ist vorbereitet, aber noch nicht implementiert
- WASAPI wird derzeit für PCM-Wiedergabe genutzt; natives DSD bleibt ASIO vorbehalten
- WASAPI läuft exklusiv und wählt für PCM das erste passende Stereoformat aus 32-Bit Float, 24-Bit PCM und 16-Bit PCM
- Transport-UI unterstützt Pause/Fortsetzen, Positionsanzeige und Seeking
- Seeking ist aktuell für ASIO-PCM, WASAPI-PCM sowie native DSF/DFF-Pfade implementiert
- Playlist-Grundfunktion ist vorhanden: Einzeldatei oder Ordner laden, Doppelklick startet einen Eintrag, nach Titelende folgt automatisch der nächste
- Playlist-Tabelle ist höhenbegrenzt und scrollbar, damit Transportelemente sichtbar bleiben
- Lautstärkeregler wirkt auf PCM-Pfade; natives DSD bleibt bitgenau und wird nicht digital abgesenkt
- Im ASIO-DSD-Modus zählt `preferredBufferSize` Samples, nicht Bytes; bei `ASIOSTDSDInt8*` werden daher `preferredBufferSize / 8` Bytes pro Kanal geschrieben
- ASIO-Capability-Abfragen können fehlschlagen, wenn andere Programme das Gerät belegen

## UI-Leitlinie

- **Hauptfenster** – dreispaltiges modernes Layout:
  - Linke Sidebar (220 px, dunkel `#13142A`): Navigation mit Oberpunkten LOKALE BIBLIOTHEK (Künstler, Alben, Tracks, Ordnerstruktur) und PLAYLISTS (dynamisch aus DB); Geräte-Info oben; Einstellungen-Button unten
  - Rechter Inhaltsbereich: Header mit Titel + Anzahl; je nach Ansicht ein `DataGrid` oder (für Ordnerstruktur) ein `TreeView`; Doppelklick startet Wiedergabe
  - Transport-Leiste unten (dunkel, volle Breite): Now-Playing-Info links, Steuerung (⏹ ▶ ⏸) + Positionsslider mittig, Lautstärke rechts
- **Settings-Fenster**: zweispaltiges Layout – Navigationsleiste links, Inhalt rechts (Ausgabegerät / Bibliotheksverzeichnisse)
- **Start**: vor dem Hauptfenster erscheint ein Splashscreen, solange die initiale DB-Vorbereitung/Migration läuft
- Geräteinfo zeigt Kanäle, Buffer, PCM-Sampleraten, DSD-Stufen und lesbar übersetzte Rohformate

## Inhaltsansichten (Hauptfenster)

- **Künstler**: distinct-Künstler-Liste (eine Zeile pro Künstler, alphabetisch); Doppelklick öffnet die Alben, auf denen dieser Künstler vorkommt
- **Alben**: normalisierte Album-Liste (eine Zeile pro Album; alphabetisch nach Albumtitel aufsteigend; Spalten: Album, Album-Künstler, Jahr) plus umschaltbare Artwork-Kachelansicht. In der Albumansicht wird der Album-Künstler gezeigt, nicht die Menge aller Track-Interpreten eines Albums. Der Wechsel erfolgt über einen segmentierten Tabellen-/Artwork-Umschalter im Header; Bilder werden erst beim Laden sichtbarer Zeilen in `ImageSource` umgewandelt. Doppelklick auf ein Album öffnet eine nach diesem Album gefilterte Trackansicht.
- `Player/Controls/VirtualizingWrapPanel.cs`: eigenes virtualisierendes WrapPanel für die Album-Artwork-Kacheln; hält das Coverraster visuell bei, materialisiert aber nur sichtbare Elemente
- **Album-Trackansicht**: nutzt `AudioDatabase.GetTrackListByAlbum(albumId)`, sortiert nach Disc → Tracknummer → Dateiname; Doppelklick startet einen Track und verwendet alle sichtbaren Albumtitel als Queue, sodass nach Titelende automatisch der nächste Albumtitel folgt.
- **Zurück-Navigation**: interne Drill-downs merken sich die vorherige Auswahl. Von Albumtracks geht es zurück zum zuvor gewählten Album; von Künstleralben zurück zum zuvor gewählten Künstler. Die Schaltfläche im Header nutzt ein appliktionskonformes, pillenförmiges Icon-Button-Design mit Chevron.
- Ein expliziter Klick auf einen Eintrag der linken Hauptnavigation setzt alle internen Drill-down-Filter zurück; Sidebar-Navigation zeigt immer die ungefilterte Top-Level-Ansicht. Auch erneutes Anklicken des bereits markierten Sidebar-Eintrags stellt die ungefilterte Hauptansicht wieder her.
- **Tracks**: alle Tracks sortiert nach Titel; Doppelklick spielt Track und baut Queue aus allen sichtbaren Einträgen
- **Ordnerstruktur**: `TreeView` (statt DataGrid); Wurzelelemente sind die in den Einstellungen konfigurierten Bibliotheksverzeichnisse (voller Pfad als Label, initial aufgeklappt); Unterordner werden mit Ordnernamen angezeigt und sind initial zugeklappt; Tracks als Blattknoten (Titel oder Dateiname); Doppelklick auf einen Track spielt alle Tracks desselben Ordners als Queue ab (sortiert nach Disc → Track-Nr. → Dateiname)
- **Playlists**: Playlist-Tracks mit Positions-Nr., Titel, Künstler, Album, Dauer

## Pflegehinweis

Diese Datei bei Architektur-, Build- oder Verhaltensänderungen mitpflegen.
