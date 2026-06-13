# AGENTS.md

## Project Overview

Windows audio player with:

- WPF frontend in `Player/`
- Native ASIO bridge in `Native/AsioBridge/`
- PCM playback through `ffmpeg`
- Native DSF/DFF DSD playback through ASIO

## Build and Run

```powershell
.\build.ps1
.\Player\bin\Debug\net8.0-windows\Player.exe
```

`build.ps1` builds `AsioBridge.dll` first and then the .NET application.

## Important Architecture

- `Player/Audio/SteinbergAsioStream.cs`: C# wrapper for the native bridge
- `Player/Audio/FfmpegAudioPlayer.cs`: PCM path
- `Player/Audio/DsfAudioPlayer.cs`: native DSF-to-DSD path
- `Player/Audio/DffAudioPlayer.cs`: native DFF/DSDIFF-to-DSD path
- `Player/Audio/WasapiAudioPlayer.cs`: WASAPI PCM path
- `Player/Audio/WasapiDeviceProvider.cs`: WASAPI devices and capability queries
- `Native/AsioBridge/bridge.cpp`: ASIO initialization, PCM/DSD ring buffers, and callback
- `Player/SettingsWindow.*`: two-column settings window with navigation on the left and the selected section on the right
- `Player/ThemeManager.cs`: sets global WPF resources for light and dark themes
- `Player/Localization/*`: language model and localized German, English, French, and Spanish strings
- `Player/StartupWindow.*`: lightweight splash screen shown during initial database preparation and migration
- `Player/CrashLogger.cs`: writes unhandled UI, AppDomain, and task exception reports to `%LOCALAPPDATA%\Player\logs\`
- `Player/SettingsStore.cs`: persists `%LOCALAPPDATA%\Player\settings.json`
- `AppSettings.LastMainView` and `AppSettings.AlbumArtworkView` preserve the selected main view and album mode
- `AppSettings.Volume` and `AppSettings.LastTrackPath` preserve volume and the last selected or played track; restoration requires both the file and database entry to exist
- `AppSettings.Theme` stores the `Light` or `Dark` theme
- `AppSettings.Language` stores `German`, `English`, `French`, or `Spanish`
- `Player/Library/TrackRecord.cs`: database track model containing tags and technical metadata
- `Player/Library/PlaylistRecord.cs`: playlist model including denormalized `TrackCount`, `IsSmartPlaylist`, and `FilterCriteria`
- `Player/Library/SmartPlaylistCriteria.cs`: serialized smart-playlist criteria (`FavoritesOnly`, `Genres`, `Formats`, `Bitrates`)
- `Player/Library/PlaylistTrackRecord.cs`: playlist entry model with position, optional TrackId reference, and required path
- `Player/Library/AudioDatabase.cs`: SQLite database layer through `Microsoft.Data.Sqlite`; database at `%LOCALAPPDATA%\Player\library.db`
- `Player/Library/LibraryScanner.cs`: directory scanner using TagLibSharp; writes through `AudioDatabase.Upsert()`, reports progress, and supports cancellation
- `Player/Library/LibraryBackupService.cs`: versioned ZIP export/import for the SQLite library, artwork cache, and configured library directories; audio files are not included
- `Player/Library/LyricsService.cs`: LRCLIB client and LRC parser for downloaded plain or synchronized lyrics
- `Player/Library/ArtistProfileService.cs`: configurable artist biography and image lookup (Wikipedia or Last.fm); static `Source` and `LastFmApiKey` properties set from `AppSettings`; images cached under `%LOCALAPPDATA%\Player\artist-images\`

## Audio Database

- SQLite through `Microsoft.Data.Sqlite`; no server process is required
- `tracks` stores file paths, tags, technical metadata, and references to normalized `artists` and `albums`
- `artists` contains stable artist IDs plus cached profile biography, image path, source URL, language, and fetch timestamp
- `artists`, `albums`, and `tracks` each have a direct `is_favorite` flag
- `albums` contains stable album IDs (`id`, `title`, `artist_id`, `year`, `artwork_id`, `is_favorite`)
- `artworks` deduplicates artwork by SHA-256 hash; originals and thumbnails live under `%LOCALAPPDATA%\Player\artworks\` as `original`, `thumb_96`, and `thumb_320`
- `favorites` is an older generic extension point; visible favorites use the direct flags
- `play_history` records playback starts and endings, duration, final position, and completion state
- `AudioDatabase.GetTrackIdAndFavorite(path)` performs a lightweight `id` and `is_favorite` lookup
- `AudioDatabase.OpenDefault()` creates or opens `%LOCALAPPDATA%\Player\library.db`
- `Upsert()` is idempotent through `INSERT ... ON CONFLICT DO UPDATE`
- `GetPathTimestamps()` returns paths and modification timestamps for efficient rescans
- WAL journal mode is enabled
- Multiple library directories are stored in `AppSettings.LibraryPaths`
- Each directory in Settings has its own Scan button, which becomes Cancel while scanning, with progress shown below the entry
- Directories can be added or removed; active scans are canceled when a directory is removed or the window closes
- Scans skip unchanged files and do not overwrite `added_at`
- Metadata extraction supports ID3v1/v2, Vorbis Comments, APE tags, and embedded artwork
- Opening the database runs a legacy-data migration that normalizes artists, albums, and artwork and removes old per-track artwork BLOBs
- `album_artist_rebuild_v1` rebuilds album assignments strictly from `album_artist` so compilations are not split by track artist
- `album_title_uniqueness_v1` consolidates albums by unique title and uses the first album artist when multiple artists exist
- `RebuildAlbumsFromAlbumArtists()` must preserve existing `artwork_id` assignments
- Settings includes **Repair album artwork**, which re-reads a sample file per album through TagLib when historical assignments are missing
- Settings includes **Download missing artwork**, using Cover Art Archive for albums with a `musicbrainz_release_id`
- Missing covers show a placeholder and manual MusicBrainz search by editable album title
- The manual cover-search dialog uses the themed native title bar, shows search activity and explicit empty results, and can be run repeatedly
- Album artwork has a context menu for deletion or reassignment through manual MusicBrainz search
- The main window starts maximized
- `artwork_files_v1` exports legacy artwork BLOBs into the file cache; `artworks.data` remains for compatibility with old `NOT NULL` schemas
- Thumbnail generation is intentionally fault tolerant; invalid embedded artwork must not prevent startup
- `normalized_library_v1` prevents expensive legacy migration checks on every database open
- `AudioDatabase.Optimize()` runs `wal_checkpoint(TRUNCATE)`, `VACUUM`, and `ANALYZE`
- Settings library backup creates a consistent SQLite snapshot, includes album artwork, artist images, and library paths, reports percentage and current-file progress for both export and import, writes to `.tmp` before publishing the completed `.zip`, validates imports in staging, rebases cached image paths, rolls back partial replacements, and reports Lucene index rebuild progress
- Downloaded lyrics are cached in `tracks.downloaded_lyrics` / `tracks.synced_lyrics`; the transport note button replaces the current main content with a large lyrics view over a dimmed cover background, highlights timestamped LRC lines through the transport timer, and falls back to embedded unsynchronized lyrics
- Artist views support table and image-card modes; visible artists lazily download localized biographies and images from the configured source (Wikipedia or Last.fm). The transport info button replaces the current main content with the current artist image, biography, and source link. The source label ("Quelle: Wikipedia" / "Quelle: Last.fm") is set dynamically from the stored `SourceUrl`.
- Artist names are normalized when scanned: only the primary performer is retained, `feat.`/`ft.` suffixes are removed, and Unicode, whitespace, case, diacritic, and punctuation variants share one normalized artist identity
- Settings includes **Normalize artist names**, which transactionally merges existing variants, preserves favorites and cached profile data, updates visible track and album-artist names without modifying audio files, and rebuilds the Lucene index

## Playlist Context Menus

- Right-clicking a track, search result, album, or folder node offers existing playlists and **New playlist...**
- Selecting a playlist immediately adds the track or all album tracks and updates the status bar
- **New playlist...** opens `NewPlaylistDialog`; a name is required and Enter confirms
- `Player/NewPlaylistDialog.xaml/.cs` is themed with dynamic brushes and a DWM-colored native title bar
- `AppendPlaylistItems()` builds context-menu items dynamically
- Album artwork cards extend their existing cover menu through `ContextMenu.Opened`
- `GetPathsForRow()` returns one track path or all album tracks through `GetTrackListByAlbum`
- `PlaylistMenuTag(long PlaylistId, IReadOnlyList<string> Paths)` stores paths directly
- Folder nodes recursively collect tracks through `GetTrackPathsUnderDirectory`; empty folders have no menu
- Main-window context menus use application theme resources and a custom border template without the default white icon strip
- Dynamically created menu objects must receive their styles explicitly from `Application.Current.Resources[typeof(...)]`
- **Delete playlist** appears in the sidebar playlist context menu, removes the database record, refreshes the sidebar, and returns to Tracks if needed
- **Remove from playlist** appears only for regular playlist entries with a `PlaylistEntryId`
- `_activePlaylistId` is set by `ShowTopLevelViewAsync` only for playlist views
- `ContentRow.PlaylistEntryId` contains `playlist_tracks.id` only in regular playlist views
- Playlist localization keys must exist in German, English, French, and Spanish

## Playlist Database Structure

- `playlists`: id, name, description, created_at, modified_at, `is_smart`, `filter_criteria`
- `playlist_tracks`: id, playlist_id, nullable track_id, path, one-based contiguous position, added_at
- Nullable `track_id` keeps playlist entries after a library track is removed; `path` is always present
- `EnsureColumn` upgrades existing databases when new columns are introduced
- Available methods include `CreatePlaylist`, `CreateSmartPlaylist`, `UpdatePlaylist`, `DeletePlaylist`, `GetAllPlaylists`, `GetPlaylistById`, `GetPlaylistTracks`, `AddTrackToPlaylist`, `RemoveTrackFromPlaylist`, and transactional `MovePlaylistTrack`

## Performance Measures

- `GetTracksLite()` loads only path, file name, title, disc number, and track number for the folder tree
- `GetArtistsLite()` loads artist IDs, names, favorite state, and cached profile data without loading tracks
- `GetAlbumsLite(includeArtwork)` loads only album, display artist, and year unless artwork is requested
- `GetTrackList()` loads only visible track-list columns
- `GetTrackListByIds(ids)` batches large ID sets to stay below SQLite variable limits
- `GetTracksByDirectory(dirPath)` uses an SQL prefix query plus a direct-child filter
- `GetTrackPathsUnderDirectory(rootPath)` returns all recursive track paths below a root
- Folder-tree lazy loading uses an in-memory parent-to-children map and creates WPF items only when expanded
- TreeView virtualization uses recycling mode
- `TrackLite`, `TrackListInfo`, `ArtistInfo`, and `AlbumInfo` remain intentionally small; `TrackRecord` is reserved for complete metadata operations
- Artwork is deduplicated instead of stored per track
- `TrackSearchIndex.cs` stores a Lucene.NET index under `%LOCALAPPDATA%\Player\search-index`, supports category-specific fields, partial words, and German umlaut/eszett variants, rebuilds stale indexes, updates incrementally after scans, and removes missing files below rescanned roots

## Known Technical Details

- Target platform: `net8.0-windows`, x64
- Native DSD supports `.dsf` and uncompressed stereo `.dff`
- DST-compressed `.dff` is not played natively
- Output types represented by settings: `ASIO`, `WASAPI`, `KernelStreaming`
- ASIO and WASAPI are implemented; Kernel Streaming is not
- WASAPI handles PCM only; native DSD remains ASIO-only
- WASAPI runs exclusively and selects the first supported stereo format from 32-bit float, 24-bit PCM, and 16-bit PCM
- WASAPI pause keeps the exclusive AudioClient running and supplies silence so drivers do not loop the final endpoint buffer; buffered audio remains available for resume
- WASAPI playback position subtracts frames still queued in `BufferedWaveProvider`, so transport time, history, and synchronized lyrics follow audible output instead of producer progress
- Transport uses custom vector icons for previous, play/pause, and next; unavailable queue directions are disabled
- Seeking is implemented for ASIO PCM, WASAPI PCM, DSF, and DFF
- Loading a file or folder builds a playback queue; completion advances automatically
- The playlist table is height-limited and scrollable
- Volume affects PCM paths; native DSD remains bit-perfect
- In ASIO DSD mode, `preferredBufferSize` counts samples rather than bytes; `ASIOSTDSDInt8*` writes `preferredBufferSize / 8` bytes per channel
- ASIO capability queries may fail while another application owns the device

## UI Guidelines

- The main window uses a modern sidebar, content area, and full-width transport bar
- The 220 px sidebar contains Dashboard, local library navigation, playlists, device information, About, and Settings
- About displays the author, library licenses, and the Steinberg ASIO trademark notice
- The content header continues the native title-bar/sidebar appearance and shows title, count, search, filters, or album mode controls
- The bottom transport bar shows artwork and track information, favorite state, playback controls, position, and volume
- Settings uses a two-column layout with navigation on the left and content on the right
- Settings navigation reuses the main sidebar theme resources
- All Settings buttons use the shared themed button style, including dynamic scan and remove buttons
- Settings ComboBoxes use fully themed templates; device information follows the active theme and title-bar color
- DSD capability states use explicit supported and unsupported theme colors
- A splash screen is shown while initial database preparation runs
- Device information displays channels, buffer sizes, PCM rates, DSD levels, and readable raw formats
- Supported Windows versions use DWM-colored native title bars; older versions keep the OS default
- Light and dark themes switch global dynamic resources for tables, artwork, surfaces, text, transport, navigation, separators, and scrollbars
- Visible primary text and runtime messages use `LocalizationManager`
- Empty artwork areas use a dedicated placeholder resource
- Tables, lists, and trees must not expose white WPF default backgrounds in dark mode
- MainWindow also overrides DataGrid and ScrollViewer backgrounds locally
- DataGrid row headers remain disabled through `HeadersVisibility="Column"`

## Dashboard

- The top sidebar item has tag `"Dashboard"`
- The page contains:
  1. **Recently added albums**: horizontal artwork strip of up to 12 albums; selecting a card opens album tracks and supports Back navigation
  2. **Calendar**: Monday-first month grid with day number, `HH:mm` playback time, top three genres, today highlight, and month navigation
  3. **Top 10 genres**: descending proportional bars with `HH:mm` duration and `_genreColors`
- Data comes from `GetRecentAlbums`, `GetCalendarData`, and `GetTopGenres`
- `RecentAlbumInfo` and `CalendarDayData` are records in `AudioDatabase.cs`
- `_dashboardYear`, `_dashboardMonth`, `_calendarInner`, and `_genreColors` hold dashboard state
- Month navigation rebuilds the dashboard so the section title changes
- Dashboard code must fully qualify `System.Windows.HorizontalAlignment` and `System.Windows.Media.Brushes` where member-name resolution would otherwise conflict

## Main Content Views

- **Artists**: distinct alphabetical artist list; double-click opens albums containing that artist
- **Albums**: normalized unique-title list with favorite, 96 px thumbnail, album, album artist, and year, plus a switchable artwork grid using 320 px thumbnails
- Album views show the album artist rather than combining track artists; when multiple album artists exist, the first is used
- Album images are converted to `ImageSource` only when visible elements load
- `ContentRow` implements `INotifyPropertyChanged` so asynchronously loaded artwork appears immediately
- **Now Playing**: shows a 96 px thumbnail and track favorite button; the button is disabled when the current file has no database track
- `VirtualizingWrapPanel.cs` materializes only visible artwork cards
- **Album tracks**: `GetTrackListByAlbum(albumId)` sorts by disc, track number, and file name; playback queues all visible album tracks
- When album tracks are opened from an artist drill-down, the list initially contains only tracks by that artist; a localized **Show all album tracks** switch removes the artist filter and rebuilds the visible playback queue
- The album-track view has a centered header with a large 240 px cover, album title, album artist, and optional year; artwork can be searched, reassigned, or deleted
- **Favorites**: artist, album, and track lists and album cards can toggle their direct favorite flags
- **Back navigation**: drill-downs remember the previous selection and use a themed pill-shaped chevron button
- Visible artist and album names act as links across tables, search results, artwork cards, album headers, artist profiles, dashboard cards, and Now Playing; artist links open the artist's albums and album links open the album's tracks
- Explicit sidebar navigation clears drill-down filters, including when the already selected item is clicked again
- **Tracks**: title-sorted list with combinable Favorites, Genre, Audio Type, and Bitrate facets; counts reflect the other active filters and unavailable unselected values are hidden
- Alphabetically sorted artist, album, and track views show an A-Z/# index immediately left of the right-aligned scrollbar; unavailable letters are disabled, dragging across letters scrolls live, and the highlighted letter follows the top visible entry
- **Search**: delayed Lucene search returns separate themed Track, Album, and Artist sections, supports partial words and German normalization variants, sorts by score then display name, and preserves the original query across drill-down Back navigation
- **Folder structure**: configured library roots start expanded; child folders load lazily; double-clicking a track queues its direct folder sorted by disc, track number, and file name
- **Playlists**: display position, title, artist, album, and duration; sidebar entries open their live track list
- **Smart playlists**: store JSON criteria instead of track rows, show a gold lightning icon, resolve live when opened, and do not permit manual entry removal
- **Save smart playlist**: available in Tracks only when filters are active; opens `NewPlaylistDialog`, serializes criteria, and calls `CreateSmartPlaylist`
- Tracks, albums, search results, and folder-tree nodes support playlist context menus

## Maintenance Requirement

Keep this file updated whenever architecture, build behavior, or user-visible
behavior changes.

Update `README.md` whenever features, requirements, build steps, supported
formats, or known limitations change so the public GitHub documentation matches
the actual project.

## Localization Rule

- Do not hard-code new visible UI text or status/error messages in XAML or code-behind
- Store all such text under `Player/Localization/`
- Every new or changed string must be provided in German, English, French, and Spanish
- A text change is complete only after all supported language resources contain meaningful translations

## Settings Layout Rule

- Settings inputs use a label on its own row with the field below it using the available width
- Do not place new ComboBoxes or similar inputs beside labels in special layouts unless there is a clear functional reason
