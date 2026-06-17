# AGENTS.md

## Project Overview

Windows audio player with:

- WPF frontend in `Orynivo/`
- Native Steinberg ASIO bridge in `Native/AsioBridge/`
- MIT-licensed cwASIO bridge in `Native/CwAsioBridge/`
- PCM playback through `ffmpeg`
- Native DSF/DFF DSD playback through ASIO

## Build and Run

```powershell
.\build.ps1
.\Orynivo\bin\Debug\net8.0-windows\Orynivo.exe
```

`build.ps1` always builds the vendored MIT-licensed `CwAsioBridge.dll`, then
builds `AsioBridge.dll` when the Steinberg SDK is available, and finally builds
the .NET application. It
locates Visual Studio MSBuild through `vswhere.exe` or `PATH`. The ASIO SDK can
be supplied with `-AsioSdkDir`, `ASIO_SDK_DIR`, or a local
`third_party/asiosdk` / `external/asiosdk` directory. `-MSBuildPath` and
`MSBUILD_EXE_PATH` override MSBuild discovery. If no SDK is found, the script
builds without the Steinberg bridge; `-SkipAsio` forces that mode and
`-RequireAsio` makes a missing SDK fatal. `-SkipCwAsio` additionally disables
the cwASIO bridge. Managed build properties remove disabled native DLLs from
build and publish output.

`.github/workflows/dotnet-desktop.yml` builds the cwASIO bridge and managed WPF
project in Debug and Release. It intentionally excludes only the Steinberg
bridge because that SDK is not stored in the repository. The Release artifact
therefore contains cwASIO support without Steinberg SDK files.

## Important Architecture

- `Orynivo/Audio/SteinbergAsioStream.cs`: runtime-selecting C# wrapper for `AsioBridge.dll` and `CwAsioBridge.dll`
- `Orynivo/Audio/FfmpegAudioPlayer.cs`: PCM path
- `Orynivo/Audio/DsfAudioPlayer.cs`: native DSF-to-DSD path
- `Orynivo/Audio/DffAudioPlayer.cs`: native DFF/DSDIFF-to-DSD path
- `Orynivo/Audio/WasapiAudioPlayer.cs`: WASAPI PCM path
- `Orynivo/Audio/WasapiDeviceProvider.cs`: WASAPI devices and capability queries
- `Native/AsioBridge/bridge.cpp`: shared Steinberg/cwASIO initialization, PCM/DSD ring buffers, and callback
- `Native/CwAsioBridge/CwAsioBridge.vcxproj`: builds the shared bridge against vendored cwASIO
- `third_party/cwasio/`: pinned MIT-licensed cwASIO host and compatibility sources
- `Orynivo/SettingsWindow.*`: two-column settings window with navigation on the left and the selected section on the right
- `Orynivo/ThemeManager.cs`: sets global WPF resources for light and dark themes
- `Orynivo/Localization/*`: language model and localized German, English, French, and Spanish strings
- `Orynivo/StartupWindow.*`: lightweight splash screen shown during initial database preparation and migration
- `Orynivo/Assets/Orynivo_Logo.png`: embedded full logo used by the splash screen and main sidebar
- `Orynivo/Assets/Orynivo.ico`: multi-resolution application and window icon generated from `Logo/only_logo_300.png`
- The process uses the explicit Windows AppUserModelID `Orynivo.AudioPlayer`; the startup window is excluded from the taskbar so the main window owns the taskbar identity
- `Orynivo/CrashLogger.cs`: writes unhandled UI, AppDomain, and task exception reports to `%LOCALAPPDATA%\Orynivo\logs\`
- `Orynivo/DailyHistoryDialog.*`: themed modal dashboard dialog that lists playback-history entries for a selected calendar day and returns track, album, or artist navigation actions to the main window
- `Orynivo/SettingsStore.cs`: persists `%LOCALAPPDATA%\Orynivo\settings.json`
- `Orynivo/Streaming/IStreamingCatalog.cs` and `IStreamingPlaybackProvider.cs`: provider-neutral contracts for future streaming catalog and playback integrations
- `Orynivo/Streaming/QobuzStreamingProvider.cs`: inactive Qobuz scaffold; do not add unofficial endpoints, enable it only with approved partner API documentation
- `Orynivo/Streaming/WindowsStreamingCredentialStore.cs`: stores future provider secrets and tokens in `%LOCALAPPDATA%\Orynivo\streaming-credentials.dat` using Windows DPAPI for the current user
- `Orynivo/Streaming/PlexServerClient.cs`: queries configured Plex Media Servers, exposes only music library sections (`type=artist`), pages artists/albums/tracks, resolves drill-down children, browses folders lazily, and builds authenticated direct-part URLs
- `Orynivo/Streaming/WindowsPlexCredentialStore.cs`: stores per-server Plex access tokens in `%LOCALAPPDATA%\Orynivo\plex-credentials.dat` using Windows DPAPI for the current user
- `AppSettings.PlexServers` stores Plex server IDs, display names, and base URLs; Plex tokens must not be added to `settings.json`
- `AppSettings.QobuzApplicationId` stores only the non-secret Qobuz application identifier; client secrets and tokens must not be added to `settings.json`
- `AppSettings.LastMainView` and `AppSettings.AlbumArtworkView` preserve the selected main view and album mode
- `AppSettings.Volume` and `AppSettings.LastTrackPath` preserve volume and the last selected or played track; restoration requires both the file and database entry to exist
- `AppSettings.Theme` stores the `Light` or `Dark` theme
- `AppSettings.Language` stores `German`, `English`, `French`, or `Spanish`
- `Orynivo/Library/TrackRecord.cs`: database track model containing tags and technical metadata
- `Orynivo/Library/PlaylistRecord.cs`: playlist model including denormalized `TrackCount`, `IsSmartPlaylist`, and `FilterCriteria`
- `Orynivo/Library/SmartPlaylistCriteria.cs`: serialized smart-playlist criteria (`FavoritesOnly`, `Genres`, `Formats`, `Bitrates`)
- `Orynivo/Library/PlaylistTrackRecord.cs`: playlist entry model with position, optional TrackId reference, and required path
- `Orynivo/Library/AudioDatabase.cs`: SQLite database layer through `Microsoft.Data.Sqlite`; database at `%LOCALAPPDATA%\Orynivo\library.db`
- `Orynivo/Library/LibraryScanner.cs`: directory scanner using TagLibSharp; writes through `AudioDatabase.Upsert()`, reports progress, and supports cancellation
- `Orynivo/Library/LibraryBackupService.cs`: versioned ZIP export/import for the SQLite library, artwork cache, and configured library directories; audio files are not included
- `Orynivo/Library/LyricsService.cs`: LRCLIB client and LRC parser for downloaded plain or synchronized lyrics
- `Orynivo/Library/RadioBrowserService.cs`: Radio Browser client with mirror discovery, station search, and click registration
- `Orynivo/Library/RadioStationRecord.cs`: persisted personal internet-radio station model
- `Orynivo/Library/RadioStreamMetadataService.cs`: probes live ICY metadata through `ffprobe`; radio playback refreshes title/artist every 15 seconds
- `Orynivo/Library/PodcastService.cs`: searches the public Apple Podcasts catalog and resolves all playable episodes from podcast RSS/Atom feeds, newest first
- `Orynivo/Library/CatalogFilterCache.cs`: persists radio-genre and podcast category/language catalogs in `%LOCALAPPDATA%\Orynivo\catalog-filter-cache.json`; catalog data is refreshed after seven days while stale data remains usable
- `Orynivo/Library/PodcastRecord.cs`: podcast catalog, persisted subscription, and episode models
- `Orynivo/LyricsSearchWindow.*`: manual LRCLIB search with editable track and artist fields, candidate preview, and explicit replacement of the current track's downloaded lyrics cache
- `Orynivo/Library/ArtistProfileService.cs`: configurable artist biography and image lookup (Wikipedia or Last.fm); static `Source` and `LastFmApiKey` properties set from `AppSettings`; images cached under `%LOCALAPPDATA%\Orynivo\artist-images\`
- `Orynivo/Library/ArtistImageSearchService.cs` and `Orynivo/ArtistImageSearchWindow.*`: manual Wikimedia Commons artist-image search with editable query; selecting an image updates `artists.image_path`, sets `image_is_manual`, and preserves the biography source; automatic profile refreshes must not download over manually selected image files
- `Orynivo/EditArtistNameDialog.*` and `Orynivo/ArtistMergeDialog.*`: artist-info rename flow; collisions require an explicit merge-profile priority choice

## Audio Database

- SQLite through `Microsoft.Data.Sqlite`; no server process is required
- `tracks` stores file paths, tags, technical metadata, and references to normalized `artists` and `albums`
- `artists` contains stable artist IDs plus cached profile biography, image path, source URL, language, and fetch timestamp
- `artists`, `albums`, and `tracks` each have a direct `is_favorite` flag
- `albums` contains stable album IDs (`id`, `title`, `artist_id`, `year`, `artwork_id`, `is_favorite`)
- `artworks` deduplicates artwork by SHA-256 hash; originals and thumbnails live under `%LOCALAPPDATA%\Orynivo\artworks\` as `original`, `thumb_96`, and `thumb_320`
- `favorites` is an older generic extension point; visible favorites use the direct flags
- `play_history` records local tracks, podcast episodes, and internet-radio sessions with media type, display title/subtitle, optional external ID, playback start/end, duration, final position, and completion state
- `radio_stations` stores personal Radio Browser stations by stable station UUID, including stream URL, logo, country, codec, bitrate, and tags
- `podcasts` stores pinned podcasts by Apple collection ID, including author, RSS feed URL, artwork URL, and genre
- `podcast_episode_progress` stores resume position, known duration, completion state, and update time per pinned podcast episode; RSS GUID is the preferred episode key and the audio URL is the fallback
- `AudioDatabase.GetTrackIdAndFavorite(path)` performs a lightweight `id` and `is_favorite` lookup
- `AudioDatabase.OpenDefault()` creates or opens `%LOCALAPPDATA%\Orynivo\library.db`
- On first launch after the rename, missing data is copied from `%LOCALAPPDATA%\Player\` and cached database paths are rebased to `%LOCALAPPDATA%\Orynivo\`
- Cache-path rebasing is guarded by the `cache_paths_orynivo_v1` database migration marker and must not run on every database open
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
- The lyrics view can manually search LRCLIB with overridden title and artist text. Selecting a result replaces only the database-cached downloaded/synchronized lyrics and leaves audio-file tags unchanged.
- Artist views support table and image-card modes; visible artists lazily download localized biographies and images from the configured source (Wikipedia or Last.fm). The transport info button replaces the current main content with the current artist image, biography, and source link. The source label ("Quelle: Wikipedia" / "Quelle: Last.fm") is set dynamically from the stored `SourceUrl`.
- The artist information view can search Wikimedia Commons using editable text and assign the selected image without replacing the cached biography or its source URL.
- Artist images remain visible even when no biography is available.
- The artist information view can rename artists. A matching normalized name opens a merge dialog that asks which artist record and profile data survive; the transaction consolidates duplicate albums, reassigns tracks, preserves favorites and available album artwork, updates denormalized artist names, and rebuilds the Lucene index. Audio-file tags are not changed.
- Artist names are normalized when scanned: only the primary performer is retained, `feat.`/`ft.` suffixes are removed, and Unicode, whitespace, case, diacritic, and punctuation variants share one normalized artist identity
- Settings includes **Normalize artist names**, which transactionally merges existing variants, preserves favorites and cached profile data, updates visible track and album-artist names without modifying audio files, and rebuilds the Lucene index

## Playlist Context Menus

- Right-clicking a track, search result, album, or folder node offers existing playlists and **New playlist...**
- Selecting a playlist immediately adds the track or all album tracks and updates the status bar
- **New playlist...** opens `NewPlaylistDialog`; a name is required and Enter confirms
- `Orynivo/NewPlaylistDialog.xaml/.cs` is themed with dynamic brushes and a DWM-colored native title bar
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
- `TrackSearchIndex.cs` stores a Lucene.NET index under `%LOCALAPPDATA%\Orynivo\search-index`, supports category-specific fields, partial words, and German umlaut/eszett variants, rebuilds stale indexes, updates incrementally after scans, and removes missing files below rescanned roots
- Search-index freshness is determined by the stored schema marker; indexed `Field.Store.NO` fields must not be tested through stored-document field access

## Known Technical Details

- Target platform: `net8.0-windows`, x64
- `SteinbergAsioStream.IsBackendAvailable()` validates each native bridge and
  loads its shared export API dynamically. Settings shows **Steinberg ASIO**
  only when `AsioBridge.dll` exists and **cwASIO** only when
  `CwAsioBridge.dll` exists. A missing selected backend migrates to the other
  ASIO implementation when available, otherwise to WASAPI.
- `OutputBackend` values are persisted numerically; existing values remain
  `Asio=0`, `Wasapi=1`, and `KernelStreaming=2`, while `CwAsio=3` is appended.
- Native DSD supports `.dsf` and uncompressed stereo `.dff`
- DST-compressed `.dff` is not played natively
- Output types represented by settings: Steinberg `ASIO`, `CwAsio`, `WASAPI`, `KernelStreaming`
- Steinberg ASIO, cwASIO, and WASAPI are implemented; Kernel Streaming is not
- WASAPI handles PCM only; native DSD remains ASIO-only
- WASAPI runs exclusively and selects the first supported stereo format from 32-bit float, 24-bit PCM, and 16-bit PCM
- WASAPI pause keeps the exclusive AudioClient running and supplies silence so drivers do not loop the final endpoint buffer; buffered audio remains available for resume
- WASAPI playback position subtracts frames still queued in `BufferedWaveProvider`, so transport time, history, and synchronized lyrics follow audible output instead of producer progress
- Transport uses custom vector icons for previous, play/pause, and next; unavailable queue directions are disabled
- Seeking is implemented for ASIO PCM, WASAPI PCM, DSF, and DFF
- Loading a file or folder builds a playback queue; completion advances automatically
- The transport action buttons for artist information, lyrics, favorite, and shuffle are left-aligned above the position slider; previous/play/next remain independently centered
- When the transport area becomes narrow, the centered previous/play/next group shifts right only enough to keep a 12 px gap from the left action buttons
- The position slider keeps the standard thumb size but exposes a 30 px transparent vertical hit area; clicking anywhere in that area updates the seek position while the visible track remains 3 px high
- Shuffle keeps a per-loaded-queue set of played file paths, so duplicate entries and already played tracks are not selected again; loading any queue again resets that set while the shuffle toggle may remain enabled
- The playlist table is height-limited and scrollable
- Volume affects PCM paths; native DSD remains bit-perfect
- In ASIO DSD mode, `preferredBufferSize` counts samples rather than bytes; `ASIOSTDSDInt8*` writes `preferredBufferSize / 8` bytes per channel
- ASIO capability queries may fail while another application owns the device

## UI Guidelines

- The main window uses a modern sidebar, content area, and full-width transport bar
- The full Orynivo logo appears on a light logo surface in the startup window and at the top of the main sidebar
- The 220 px sidebar contains Dashboard, local library navigation, playlists, device information, About, and Settings
- Local library, personal radio, pinned podcast, Plex server, and playlist sidebar groups use independently expandable accordion headers; expansion state is persisted
- `AppSettings.ShowLocalLibrarySection`, `ShowOwnRadiosSection`, `ShowMyPodcastsSection`, `ShowPlexSection`, and `ShowPlaylistsSection` control group visibility from Settings > Appearance and default to visible
- `AppSettings.IsLocalLibrarySectionExpanded`, `IsOwnRadiosSectionExpanded`, `IsMyPodcastsSectionExpanded`, `IsPlexSectionExpanded`, and `IsPlaylistsSectionExpanded` persist independent sidebar accordion states
- About displays the author, library licenses, and the Steinberg ASIO trademark notice
- The content header continues the native title-bar/sidebar appearance and shows title, count, search, filters, or album mode controls
- The bottom transport bar shows artwork and track information, favorite state, playback controls, position, and volume
- Settings uses a two-column layout with navigation on the left and content on the right
- Settings navigation reuses the main sidebar theme resources
- All Settings buttons use the shared themed button style, including dynamic scan and remove buttons
- Settings ComboBoxes use fully themed templates; device information follows the active theme and title-bar color
- The Plex server editor uses themed inputs and buttons plus a DWM-colored native title bar; Plex credential persistence must not synchronously wait on asynchronous file I/O from the UI thread
- Selecting a Plex audio library exposes Artists, Albums, Tracks, and Folders modes; lists load in pages of 500, artist/album rows drill down to children, and folder nodes query Plex only when expanded
- Plex track rows reuse the main track table and playback path; Plex access tokens remain memory-only in generated stream URLs and must never be written to settings, documentation, logs, or source
- Starting a Plex track from the folder tree queues only direct track siblings from that same tree level; subfolder tracks are excluded and the existing shuffle state applies to that sibling queue
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
- **Internet Radio** appears directly below Dashboard. It searches Radio Browser, plays streams through the existing FFmpeg PCM path, and adds stations to **Own Radios** above Playlists.
- Personal radio sidebar entries use `Radio:{id}` tags; right-clicking one offers deletion from `radio_stations`.
- **Podcasts** appears directly below Internet Radio. It searches Apple Podcasts without credentials and reads playable episodes from the selected podcast's RSS/Atom feed.
- Double-clicking a podcast search result or selecting a pinned podcast opens its complete episode list, sorted from newest to oldest. Playback starts only when an episode is double-clicked.
- Podcast search results support combinable multi-select category and language filters. Categories come from Apple's complete regional podcast genre taxonomy and are available before a search. Languages are detected from RSS/Atom `language` or `xml:lang` metadata for regional top podcasts, pinned podcasts, and subsequent search results. Values within one filter use OR semantics, while category and language filters combine with AND semantics.
- Internet-radio genres are loaded from Radio Browser's complete tag statistics instead of the first station-result page. Initial catalog options intentionally omit counts because normalized genre groups overlap multiple raw tags. Selecting genres performs Radio Browser tag queries (OR across selected genres) and may load up to 10,000 stations per genre.
- Podcast category selections work without a title query by sending the cached Apple genre IDs to the catalog search. Language-only filtering uses the regional top 100 podcasts as its starting set and then evaluates feed languages.
- The cached catalog options are used only while the radio or podcast search field is empty. After a text search, visible filter values and counts are rebuilt from that search result; clearing the search restores the cached catalog options.
- Pinned podcasts appear under **My Podcasts** with `Podcast:{id}` tags. The context menu removes them from `podcasts`.
- Podcast progress is saved about every five seconds and when playback stops. Incomplete episodes resume at the saved position; episodes reaching 95 percent or ending normally are shown as played.
- The podcast episode view uses the same violet-blue accent border as the radio now-playing card and a 150 px podcast image. Its header shows total, unheard, and started episode counts plus feed categories, language, latest publication date, and a shortened feed description.
- During podcast playback, the transport info button opens a dedicated detail view with centered podcast artwork, podcast and episode titles, publication metadata, duration, genre, and the episode RSS summary
- During radio playback, the Internet Radio page shows a large station logo and live ICY title/artist metadata when supplied by the stream; the transport summary is updated at the same time.
- Radio search results expose a multi-select genre popup derived from normalized Radio Browser tags. Selected genres use OR semantics, technical tags are excluded, and unavailable selections are removed after a new search.
- The page contains:
  1. **Recently added albums**: horizontal artwork strip of up to 12 albums; selecting a card opens album tracks and supports Back navigation
  2. **Calendar**: Monday-first month grid with day number, `HH:mm:ss` playback time, top three linked genres, today highlight, and month navigation
  3. **Top 10 genres**: descending proportional bars with `HH:mm:ss` duration, linked genre labels, and `_genreColors`
- Clicking a calendar day with playback opens a modal history table with playback time, media type, title, artist, album, listened duration, and total duration
- Local-track title links in daily history open Tracks, select the title, and start playback; album and artist links open their existing drill-down views
- Data comes from `GetRecentAlbums`, `GetCalendarData`, and `GetTopGenres`
- Dashboard calendar playback time includes tracks, podcasts, and internet radio; top-genre statistics remain limited to local tracks because they require library genre metadata
- Clicking a dashboard genre opens Tracks with only that genre facet selected; other track filters are cleared and the genre filter section is expanded
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

## XML Documentation Comments

All public and internal C# types, members, and parameters must carry XML documentation
comments (`///`). Write comments in **English**.

- Every `class`, `interface`, `enum`, `record`, `struct`, and `delegate` needs a `<summary>`.
- Every public or internal method and property needs a `<summary>`.
- Every method parameter needs a `<param name="…">` tag.
- Non-void methods need a `<returns>` tag.
- Use `<see cref="…"/>` for cross-references and `<see langword="…"/>` for keywords like
  `true`, `false`, and `null`.
- Generated files (under `obj/`) and XAML code-behind auto-properties are exempt.

When adding or changing C# code, add or update the XML comments for all affected members
in the same edit. Never leave a newly introduced public or internal declaration without a
`<summary>`.

## Maintenance Requirement

Keep this file updated whenever architecture, build behavior, or user-visible
behavior changes.

Update `README.md` whenever features, requirements, build steps, supported
formats, or known limitations change so the public GitHub documentation matches
the actual project.

Keep `CHANGELOG.md` updated for every notable user-visible, architectural,
build, compatibility, or bug-fix change. Add ongoing work under **Unreleased**
and move those entries into a dated version section when preparing a release.

## Localization Rule

- Do not hard-code new visible UI text or status/error messages in XAML or code-behind
- Store all such text under `Orynivo/Localization/`
- Every new or changed string must be provided in German, English, French, and Spanish
- A text change is complete only after all supported language resources contain meaningful translations

## Settings Layout Rule

- Settings inputs use a label on its own row with the field below it using the available width
- Do not place new ComboBoxes or similar inputs beside labels in special layouts unless there is a clear functional reason
