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
- `Player/SettingsStore.cs`: persistiert `%LOCALAPPDATA%\Player\settings.json`
- `Player/Library/TrackRecord.cs`: C#-Modell für einen DB-Track-Eintrag (ID3 + technische Metadaten)
- `Player/Library/PlaylistRecord.cs`: C#-Modell für eine Playlist (inkl. denormalisiertem `TrackCount`)
- `Player/Library/PlaylistTrackRecord.cs`: C#-Modell für einen Playlist-Eintrag (Position, optionale TrackId-Referenz, immer Pfad)
- `Player/Library/AudioDatabase.cs`: SQLite-Datenbankschicht (via `Microsoft.Data.Sqlite`); DB-Datei unter `%LOCALAPPDATA%\Player\library.db`
- `Player/Library/LibraryScanner.cs`: Verzeichnis-Scanner; liest Metadaten via TagLibSharp und schreibt sie per `AudioDatabase.Upsert()` in die DB; unterstützt Fortschritts-Reporting (`IProgress<ScanProgress>`) und Abbruch per `CancellationToken`

## Audiodatenbank

- Datenbank: SQLite via `Microsoft.Data.Sqlite` (cross-platform, keine Serverinstanz)
- Schema: Tabelle `tracks` mit ID3-Tags (Title, Artist, AlbumArtist, Album, Genre, Year, Date, TrackNumber, DiscNumber, Composer, Conductor, Lyricist, Lyrics, Comment, Copyright, Publisher, BPM, ISRC, Language, Mood, ReplayGain, …), technischen Metadaten (Format, Duration, SampleRate, BitDepth, Channels, Bitrate, IsLossless, IsDsd, DsdRate), Cover Art (BLOB, optional) und MusicBrainz/AcoustID-Feldern
- `AudioDatabase.OpenDefault()` legt die DB unter `%LOCALAPPDATA%\Player\library.db` an
- `Upsert()` ist idempotent (INSERT … ON CONFLICT DO UPDATE) — geeignet für Re-Scans
- `GetPathTimestamps()` liefert Pfad+ModifiedAt für effiziente Änderungserkennung beim Scan
- WAL-Journal-Mode aktiv für bessere Concurrency
- Mehrere Bibliotheksverzeichnisse möglich (`AppSettings.LibraryPaths: List<string>`), persistent in `settings.json`
- Jedes Verzeichnis hat im Settings-Fenster einen eigenen „Scannen"-Button (wird zu „Abbrechen" während der Scan läuft); Fortschrittstext direkt unter dem jeweiligen Eintrag
- Verzeichnisse können über „+ Verzeichnis hinzufügen" ergänzt und per × entfernt werden; laufende Scans werden beim Entfernen oder Schließen des Fensters abgebrochen
- Scan überspringt unveränderte Dateien (ModifiedAt-Vergleich); `added_at` wird beim Update nicht überschrieben
- Metadaten-Extraktion via TagLibSharp (ID3v1/v2, Vorbis Comments, APE Tags, Cover Art)

## Playlisten-Datenbankstruktur

- Tabelle `playlists`: id, name, description, created_at, modified_at
- Tabelle `playlist_tracks`: id, playlist_id (FK→playlists, CASCADE), track_id (FK→tracks, SET NULL), path, position (1-basiert, lückenlos), added_at
- `track_id` ist nullable: Playlist-Einträge bleiben erhalten, wenn ein Track aus der Bibliothek entfernt wird; `path` ist immer gesetzt
- Verfügbare Methoden: `CreatePlaylist`, `UpdatePlaylist`, `DeletePlaylist`, `GetAllPlaylists` (inkl. TrackCount via JOIN), `GetPlaylistById`, `GetPlaylistTracks`, `AddTrackToPlaylist`, `RemoveTrackFromPlaylist`, `MovePlaylistTrack` (transaktional, nummeriert Positionen neu)

## Performance-Maßnahmen

- `AudioDatabase.GetTracksLite()`: schlanke Abfrage (nur path, file_name, title, disc_number, track_number) – kein Cover-Art-BLOB, keine Lyrics; wird für die Ordnerstruktur-Ansicht verwendet
- `AudioDatabase.GetTracksByDirectory(dirPath)`: SQL-LIKE-Abfrage auf Verzeichnis-Prefix + C#-Filter für direkte Kinder (kein `GetAll()` + LINQ)
- Ordnerstruktur-Ansicht: lazy loading – `FolderTree`-Klasse baut eine in-memory Parent→Children-Map (O(1)-Lookup); WPF-`TreeViewItem`-Objekte werden erst beim Aufklappen eines Knotens erzeugt (`Expanded`-Event + Platzhalter-Technik)
- WPF-TreeView: `VirtualizingStackPanel.IsVirtualizing="True"` + `VirtualizationMode=Recycling` – nur sichtbare Zeilen werden gerendert
- `TrackLite`-Record in `Player/Library/AudioDatabase.cs` (5 Felder); vollständiger `TrackRecord` bleibt für Wiedergabe, Playlist- und Metadaten-Abfragen

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
- Geräteinfo zeigt Kanäle, Buffer, PCM-Sampleraten, DSD-Stufen und lesbar übersetzte Rohformate

## Inhaltsansichten (Hauptfenster)

- **Künstler**: distinct-Künstler-Liste (eine Zeile pro Künstler, alphabetisch); kein Doppelklick-Abspielen (Funktionalität folgt)
- **Alben**: distinct-Album-Liste (eine Zeile pro Album; Spalten: Album, Album-Künstler, Jahr); kein Doppelklick-Abspielen (Funktionalität folgt)
- **Tracks**: alle Tracks sortiert nach Titel; Doppelklick spielt Track und baut Queue aus allen sichtbaren Einträgen
- **Ordnerstruktur**: `TreeView` (statt DataGrid); Wurzelelemente sind die in den Einstellungen konfigurierten Bibliotheksverzeichnisse (voller Pfad als Label, initial aufgeklappt); Unterordner werden mit Ordnernamen angezeigt und sind initial zugeklappt; Tracks als Blattknoten (Titel oder Dateiname); Doppelklick auf einen Track spielt alle Tracks desselben Ordners als Queue ab (sortiert nach Disc → Track-Nr. → Dateiname)
- **Playlists**: Playlist-Tracks mit Positions-Nr., Titel, Künstler, Album, Dauer

## Pflegehinweis

Diese Datei bei Architektur-, Build- oder Verhaltensänderungen mitpflegen.
