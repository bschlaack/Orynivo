# Changelog

All notable changes to Orynivo are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.20.0] - 2026-06-29

### Added

- Remote Orynivo Server Tracks now caches the downloaded full track list under
  `%LOCALAPPDATA%\Orynivo\remote-track-cache\` and reuses it while the server's
  `LibraryChangedAt` scan timestamp is unchanged, so revisiting the Tracks view
  no longer re-downloads the whole library. The cache key includes the API key
  (cached playback URLs embed it) and client-side favourites are re-applied after
  loading so toggling a favourite is never masked by stale cached flags.

### Fixed

- Remote Orynivo Server folder view loading placeholder now uses the themed muted
  text brush, so the "loading" message is readable on the dark background instead
  of rendering as black text.
- Remote Orynivo Server Tracks now loads the unfiltered track list with a large
  page size instead of 500 rows per request. On a large library (~75k tracks)
  the old paging issued ~150 sequential HTTP requests and took over a minute, so
  the Tracks view appeared empty unless the favourites filter (a single request)
  was active; it now loads the whole library in one or two requests within a
  couple of seconds.
- Remote Orynivo Server Tracks now applies its table columns immediately before
  binding the loaded rows (matching the local Tracks view) instead of before the
  async server load, so the DataGrid reliably realizes the rows.
- Remote Orynivo Server folder playback now registers full track metadata before
  queuing streams, so the transport shows title/artist/artwork and enables
  lyrics, artist info, favourites, and playlist actions like the local folder
  view.
- Remote Orynivo Server folder context menus now route credential-bearing stream
  URLs through the server playlist provider instead of falling back to queue-only
  actions, so files and folders can be saved to playlists on that server.
- Remote Orynivo Server Tracks now explicitly refreshes the table layout after
  async row binding so the view appears immediately without requiring an
  unrelated sidebar interaction.
- Remote Orynivo Server Tracks now binds rows immediately instead of leaving the
  table source empty until a deferred layout pass, fixing an initially blank
  Tracks view.
- Remote Orynivo Server folder views now clear stale local folder nodes before
  loading, show a server-loading placeholder, and cache the server folder-track
  list under `%LOCALAPPDATA%\Orynivo\remote-folder-cache\`. The cache is reused
  until the server reports a newer library-change timestamp.
- Library accordion headers are no longer treated as navigable views, preventing
  raw `LibraryGroup:...` tags from replacing the content title.
- Orynivo Server scan status now exposes `LibraryChangedAt`, a persisted Unix
  timestamp updated when a scan or file watcher run adds, updates, or removes
  indexed tracks, so clients can invalidate remote caches without downloading
  the full list first.

## [0.19.0] - 2026-06-28

### Fixed

- Windows FFmpeg auto-download now resolves the current BtbN release asset via
  the GitHub API instead of relying on the removed `essentials_build` ZIP name.

## [0.18.0] - 2026-06-28

### Added

- Added a shared playlist provider layer for local and remote Orynivo Server
  libraries so track/album/folder context menus use the same playlist actions
  while persisting entries to the correct local database or remote server.

### Fixed

- FFmpeg and FFprobe child processes now always receive a valid working
  directory, and startup repairs a missing current directory left by stale
  shortcuts or installer paths, avoiding launch failures that referenced an old
  `%LOCALAPPDATA%\Programs\Orynivo` path.

## [0.17.0] - 2026-06-28

### Fixed

- Automatic FFmpeg download on Windows now stores downloaded binaries in
  `%LOCALAPPDATA%\Orynivo\ffmpeg` instead of the application install directory,
  so setup installations under `Program Files` work without elevation.
- On first start or after a missing/corrupt output configuration, Orynivo now
  creates and selects a `Default` WASAPI output profile using the Windows
  default multimedia output device so playback can work without manual device
  setup.

## [0.16.0] - 2026-06-28

### Added

- The transport favourite (heart) button now works while playing a remote
  Orynivo Server track and toggles the client-side favourite for that track
  (stored in `settings.json`), mirroring the change in any visible remote track
  rows. Previously the button was only active for local tracks.
- When no local library directory and no Orynivo Server are configured, the
  Library sidebar now shows a hint pointing to Settings to add local directories
  or one or more Orynivo Servers. The complete Local node is hidden until a
  library directory exists, and the hint disappears immediately once a directory
  or server is added.
- The transport lyrics and artist-info buttons now work while playing a track
  from a remote Orynivo Server. Lyrics are fetched from LRCLIB and the artist
  biography/image from Wikipedia or Last.fm on the client, then cached on the
  server (consistent with the existing artwork/biography upload pattern). A new
  `INowPlayingMetadataProvider` abstraction with local and Orynivo Server
  implementations (`Orynivo/NowPlayingMetadataProviders.cs`) drives both flows.
- New server endpoints `GET`/`PUT /api/tracks/{id}/lyrics` cache per-track
  lyrics; the remote track DTO now also carries the primary `ArtistId` so the
  client can resolve and cache the playing track's artist profile.
- The transport cover (bottom-left) and the lyrics background now display for
  remote Orynivo Server tracks. Cover loading is unified for local and remote
  tracks through `INowPlayingMetadataProvider.GetArtworkAsync`, backed by a new
  `GET /api/artwork/track/{id}` server endpoint.
- The Windows System Media Transport Controls (lock screen / media overlay) now
  show album artwork for remote Orynivo Server tracks via the authenticated
  track-artwork URL.
- The remote Orynivo Server Artists, Albums, and Tracks views now match the local
  library views: the same column masks with clickable artist/album links, the
  artist-info button, thumbnails, the full set of optional track columns, the
  intro card, and the per-entity Favorites-only toggle. Clicking an artist/album
  link or double-clicking navigates within the remote library.
- The remote Tracks view gained the same Genre/Audio-Type/Bitrate facet filters
  as the local Tracks view, backed by new server aggregation endpoints
  `GET /api/tracks/facets` and `POST /api/tracks/by-ids`. The remote track DTO now
  also carries `AlbumId`, and the album DTO carries `ArtistId`, to drive
  in-library navigation.

- Added a shared local/remote library catalog provider layer in the Windows
  client. Local `AudioDatabase` rows and remote Orynivo Server responses now map
  into common artist, album, and track models before reaching the reusable UI
  masks.
- Remote Orynivo Server album drill-downs now use the shared album-track detail
  surface with the local-style album header, cover actions, favorite toggle,
  and grouped track tables.
- Remote Orynivo Server album drill-downs opened from a selected artist now
  initially show only that artist's tracks and expose the same "show all album
  tracks" checkbox as local albums.
- Back navigation from remote Orynivo Server album details now preserves the
  remote server context instead of restoring the same numeric album ID against
  the local library.
- Remote Orynivo Server albums and artists now support artwork management from
  the Windows client. The client runs the existing cover/artist-image searches,
  uploads the selected image bytes to the server, and the server stores them in
  its local artwork caches without performing external artwork lookups itself.
- Remote Orynivo Server artist biographies can now be refreshed from the
  Windows client. Last.fm or Wikipedia requests run on the client, then the
  server stores only the resulting cached biography, source URL, language, and
  optional image bytes.
- Remote Orynivo Server artist-info views now expose the same rename/merge and
  Wikimedia image-search actions as local artists. Renames and merges are
  committed through the server and rebuild the server Lucene index.
- Local playlists now appear under the Local node in the Library sidebar.
  Each configured Orynivo Server now exposes server-side playlists under its
  own Playlists node; regular remote playlists can be created, deleted, filled
  with server tracks, opened, played, and edited by removing entries.
- Smart playlists now work for remote Orynivo Servers exactly like for local
  media. The remote Tracks view gained the **Save smart playlist** action (when
  facet filters are active), and remote smart playlists expose **Edit smart
  playlist** in the sidebar context menu. Criteria are persisted on the server
  through new `POST /api/playlists/smart` and `PUT /api/playlists/{id}/smart`
  endpoints and resolved live server-side. The local and remote save paths share
  the same `SmartPlaylistCriteria` model, `SmartPlaylistDialog`, and a shared
  criteria-builder helper. Because remote favourites are stored client-side,
  opening a remote smart playlist resolves it through a new
  `POST /api/playlists/{id}/resolve` endpoint that receives the client's
  favourite track IDs, so a Favourites-only remote smart playlist correctly
  returns the client's favourited tracks instead of an empty list.
- Remote Orynivo Server entries now expand into Artists, Albums, Tracks, and
  Folder structure in the sidebar. Remote Artists and Albums reuse the local
  table/artwork masks with lazy authenticated artwork loading and a local
  client artwork cache, Remote Tracks uses the normal header search box through
  the server's Lucene index, and remote artists, albums, and tracks support
  client-side favorites stored in `settings.json`.

### Changed

- Renamed the main sidebar's local-library section to Library. Local media now
  appear under a "Local" node, and configured Orynivo Servers with their
  Artists, Albums, Tracks, and Folder structure entries are listed in the same
  Library section instead of in a separate Orynivo Server section.
- The Local media node and each configured Orynivo Server node inside the
  Library sidebar section are now individually collapsible.
- Settings no longer exposes a separate Orynivo Server sidebar visibility
  toggle; the Library sidebar toggle controls both local media and configured
  Orynivo Server entries.
- Removed the now-defunct Playlists toggle from Settings > Appearance > sidebar
  sections. Playlists are a child group of the Local node and follow the Library
  section visibility; the legacy `ShowPlaylistsSection` setting is retained for
  compatibility but has no UI control.
- Orynivo Server track DTOs now include the extended metadata needed by the
  shared track tables, including genre, totals, composer, BPM, file size, added
  date, and ReplayGain values when available.
- Settings navigation group headings are now consistently uppercase in every
  supported language.
- Settings navigation child items now keep normal title casing even when their
  parent group heading is uppercase.
- Orynivo Server connection settings now live under their own Settings >
  Library > Orynivo Server entry instead of Settings > Streaming services or
  the local directories page.

### Fixed

- Remote Orynivo Server (and other HTTP-streamed) tracks now start much faster.
  FFmpeg/ffprobe were blocking on their default 5-second / 5 MB stream-analysis
  window on every decoder start over HTTP, which made the first play of a remote
  track stall for ~5 seconds. The probe window is now capped (with HTTP reconnect
  resilience) for `http(s)` inputs in the decoder and both PCM probe paths.
- Seeking within a remote Orynivo Server track is now near-instant. Previously
  the client seeked the HTTP stream itself, which for seektable-less files means
  FFmpeg binary-searches via many range round-trips (~5 seconds). The client now
  requests the stream with `?ss=<seconds>` and the server seeks the local file
  and transcodes from that offset (fast local seek), so the client decodes the
  offset stream from position 0. Plex and other HTTP sources keep client-side
  seeking. The server stream/transcode is also now stopped promptly when the
  client disconnects (e.g. on a rapid re-seek).
- The Library/sidebar sections again collapse and expand reliably. The sidebar
  navigation list is now backed by a non-virtualizing panel; the previous
  virtualizing panel recycled item containers and lost the per-item visibility
  set in code (made worse by the variable-height empty-library hint row), which
  made section toggles need two clicks and could re-expand collapsed groups.
  This also fixes the empty-library hint occasionally showing even when local
  directories or a server were configured.
- The local and per-server Playlists groups in the Library sidebar are now
  correctly indented as children of their Local/server node (aligned with the
  Artists/Albums/Tracks/Folder rows) instead of protruding to the section edge;
  their playlist entries are indented one level deeper. Local and remote server
  library rows now share the same margin-based indentation. Each Orynivo
  Server's Playlists node is now an independently collapsible group like the
  local one, persisted in `CollapsedOrynivoServerPlaylistGroups`.
- The Playlists sidebar group label is now title-cased ("Playlists"/"Listas")
  instead of the all-caps section-header form, matching the other library child
  rows.
- The sidebar accordion collapse chevrons now line up vertically across section
  headers and collapsible library groups; the section-header right padding was
  matched to the navigation-item padding so the arrows are flush.
- Double-clicking a track in a remote Orynivo Server album now plays the track
  instead of navigating to an unrelated local album. The remote track list no
  longer leaves the album view-mode toggle visible, and the album drill-down
  no longer mistakes a remote track row's ID for a local album ID.
- Remote Orynivo Server artist-info buttons now open the selected server artist
  instead of treating the server artist ID as a local library artist ID.
- Remote Orynivo Server album and artist artwork grids now load existing
  artwork on initial navigation instead of showing placeholders until a later
  artwork assignment refreshes the rows.
- The remote Orynivo Server album artwork button and cover context menu now use
  the remote server artwork upload path instead of accidentally switching the
  view to the local album library.
- Adding the first Orynivo Server now expands and rebuilds the sidebar section
  so the configured server appears under the Orynivo Server header after saving.
- Orynivo Server scans now run a missing-album-artwork repair pass after full
  scans and use physical source paths for CUE albums, allowing existing embedded
  covers to populate server-side album artwork even when tracks were otherwise
  unchanged.
- Orynivo Server album artwork requests now fall back to an on-demand embedded
  artwork repair for the requested album, so existing file covers can appear
  without a manual client-side cover assignment.
- Artwork file caches are now verified against the current application data
  root. If a server database contains artwork rows whose cached image files are
  missing or point to another environment, the original and thumbnail files are
  recreated from the SQLite artwork payload and the stored paths are updated.
- Album artwork endpoints now repair missing cached image files for the
  requested album on demand and fall back to the original artwork file when a
  requested thumbnail file is unavailable.
- The Linux Orynivo Server project now references the SkiaSharp native Linux
  assets so album-artwork thumbnail generation works in headless deployments
  without requiring an external image conversion tool.
- Orynivo Server folder browsing now materializes `/api/folders/tracks` before
  disposing SQLite, fixing the closed-connection exception that prevented the
  remote folder structure from loading.

## [0.15.0] - 2026-06-27

### Added

- **Orynivo.Core** — extracted the cross-platform library layer from the
  Windows player into a standalone `net8.0` class library.  `Orynivo.Core`
  contains `AudioDatabase`, `LibraryScanner`, `LibraryWatcherService`,
  `TrackSearchIndex`, `FfmpegLocator` (cross-platform; auto-downloads FFmpeg
  on Windows, expects system FFmpeg on Linux/macOS), `FfmpegPcmDecoder`,
  `ParametricEqualizer`, `EqualizerProfile`, `SmartPlaylistCriteria` (now
  includes a `Resolve()` method that applies filtering and ordering without
  UI code), and all library model records. The existing Orynivo Windows
  player references `Orynivo.Core` and retains `InternalsVisibleTo` access
  for audio-processing internals.
- **Orynivo.Server** — new cross-platform headless music server
  (`net8.0`, ASP.NET Core Minimal API) that exposes the local library over
  the network via a REST API secured with a pre-shared API key
  (`X-Api-Key` header or `?key=` query parameter):
  - `GET /api/health` — unauthenticated status check
  - `GET /api/info` — server name, version, and library paths
  - `GET`/`PUT /api/settings/library-paths` — read and replace server
    library roots, persist them to `appsettings.json`, refresh watchers, and
    start a scan
  - `GET /api/files/directories?path=` — browse the server filesystem for
    remote directory selection
  - `POST /api/scan` / `GET /api/scan` — trigger or monitor a library scan
  - `GET /api/artists`, `/api/artists/{id}/albums`
  - `GET /api/albums`, `/api/albums/{id}/tracks`
  - `GET /api/tracks`, `/api/tracks/{id}`
  - `GET /api/playlists`, `/api/playlists/{id}/tracks`
    (smart playlists are resolved live against stored criteria)
  - `GET /api/search?q=`, `/api/search/full?q=`
    (full-text Lucene search across tracks, albums, and artists)
  - `GET /api/stream/{trackId}` — byte-range HTTP streaming for regular
    audio files; on-the-fly FLAC transcode via FFmpeg for CUE virtual tracks
  - `GET /api/stream/path?p=` — stream by absolute file path
  - `GET /api/artwork/album/{albumId}?size=` — serve album artwork
  - `GET /api/artwork/track?p=` — serve track artwork by file path
  - Configured via `appsettings.json` (`Orynivo:ApiKey`,
    `Orynivo:LibraryPaths`, `Orynivo:ScanOnStartup`, `Orynivo:ServerName`)
  - Binds to `http://0.0.0.0:5280` by default
- **Linux server packages** — pushing a version tag also triggers
  `.github/workflows/server-release.yml`, which builds self-contained
  `Orynivo.Server` binaries for `linux-x64` and `linux-arm64` and adds four
  packages to the same draft GitHub Release:
  `orynivo-server_{version}_amd64.deb`,
  `orynivo-server_{version}_arm64.deb`,
  `orynivo-server-{version}-1.x86_64.rpm`,
  `orynivo-server-{version}-1.aarch64.rpm`.
  Each package installs to `/usr/lib/orynivo-server/`, places a
  `/usr/bin/orynivo-server` symlink, ships a default config at
  `/etc/orynivo-server/appsettings.json` (marked as a conffile so
  upgrades do not overwrite user edits), and registers a systemd unit
  `orynivo-server.service` running as a dedicated `orynivo-server` system
  user. No .NET runtime is required on the target machine.

- **Orynivo Server client** — the Windows player can now connect to remote
  Orynivo Server instances running on the local network.  Add one or more
  servers in Settings → Integration (name, URL, API key); each appears as an
  accordion section in the sidebar.  Selecting a server opens an
  Artists / Albums / Tracks view backed by the server's REST API; double-
  clicking an artist or album drills down, and double-clicking a track starts
  playback of that server's audio stream (authenticated HTTP URL passed
  directly to FFmpeg — no file download required).  Album thumbnails are
  fetched from the server's `/api/artwork/album/{id}?size=96` endpoint.
  The server editor can also load, add, remove, and save the remote server's
  music directories through a server-side directory browser, start a server
  scan, and show live scan progress for large directories.
  The Orynivo Server section visibility can be toggled from Settings →
  Appearance.  API keys are stored in `settings.json` (the same policy as
  the embedded AI chat key).

### Fixed

- The Orynivo Server settings and remote directory browser dialogs now use
  themed button and list-item templates for normal, hover, pressed, selected,
  and disabled states, avoiding unreadable dark controls on dark backgrounds.
- Server and local library scans now skip inaccessible subdirectories such as
  Linux `lost+found` folders instead of aborting the complete scan with
  `UnauthorizedAccessException`.
- Orynivo Server stream URLs are no longer used as display titles in the
  transport, Up Next view, play history, or media metadata. Remote server queue
  entries now carry the track title, artist, album, duration, and format, and
  authenticated `?key=` stream URLs are not persisted in the playback queue.
- Settings now uses title-case labels for Plex and Orynivo Server sections
  in the server configuration area, while sidebar and visibility-toggle labels
  keep their all-caps section headings.
- The Orynivo Server directory browser now shows the current server path in a
  read-only field, uses a `..` list entry for parent navigation, and widens the
  add-directory action so its text is not clipped.
- On Unix-like Orynivo Server hosts, the remote directory browser now opens `/`
  directly instead of showing a Windows-style drive-root view.

## [0.14.0] - 2026-06-26

### Added

- Added a Windows installer and portable ZIP built via GitHub Actions.
  Pushing a version tag (e.g. `v0.14.0`) triggers the release workflow
  (`.github/workflows/release.yml`), which publishes two self-contained
  packages as a draft GitHub Release:
  `Orynivo-{version}-win-x64-Setup.exe` (installer with Start Menu entry
  and uninstaller) and `Orynivo-{version}-win-x64-Portable.zip` (extract
  anywhere and run). No .NET installation is required on the target machine.
  Library data under `%LOCALAPPDATA%\Orynivo\` is preserved on uninstall.

## [0.13.0] - 2026-06-26

### Added

- **Embedded AI Chat** — a new sidebar view (KI-Chat / AI Chat) that sends
  natural-language questions about the music library to any
  OpenAI-compatible LLM endpoint (LM Studio, Ollama, OpenAI, Anthropic
  compatibility layer, or any `/v1/chat/completions` provider). The model
  has access to all 19 Orynivo player tools via function calling and
  executes them directly against the player bridge and library database —
  no external MCP server required. Responses stream token-by-token.
  Configuration (endpoint URL, optional API key, model name, max tokens)
  lives under Settings > Integration > AI Chat.
  (`AppSettings.AiChat`, `Orynivo/AI/AiChatSettings.cs`,
  `AiChatService.cs`, `AiToolDefinitions.cs`, `AiToolExecutor.cs`,
  `AiChatView.axaml`)
- Added `clear_queue` MCP/AI tool: empties the playback queue without
  stopping the current track.
- Added `replace_queue` MCP/AI tool: atomically replaces the entire
  playback queue with a given list of tracks and starts playing from the
  first one. The AI uses this automatically when the user asks to fill
  the queue with new content, avoiding stale entries from a previous
  session. Per-tool enable/disable checkboxes are available in
  Settings > Integration > MCP Server.

## [0.12.0] - 2026-06-26

### Added

- Internet Radio, Podcasts, and **Als Nächstes** (Up Next) sidebar items can
  now be hidden individually in Settings > Appearance, consistent with the
  existing accordion-section toggles.
  (`AppSettings.ShowInternetRadioItem`, `ShowPodcastsItem`, `ShowQueueItem`)
- Added an embedded **MCP server** (Model Context Protocol) under
  Settings > Integration. When enabled, the player starts an HTTP/SSE server on
  a configurable local port (default 49200) that exposes 17 tools to any
  MCP-compatible AI assistant (e.g. Claude Desktop):
  `get_now_playing`, `get_queue`, `play`, `pause_resume`, `next_track`,
  `previous_track`, `stop`, `seek`, `set_volume`, `queue_append`,
  `queue_play_next`, `search_library`, `list_playlists`, `get_playlist_tracks`,
  `create_playlist`, `create_smart_playlist`, and `get_play_history`. The
  server is started and stopped immediately when Settings are saved; it only
  binds to `localhost`.
  (`AppSettings.McpServerEnabled`, `AppSettings.McpServerPort`,
  `Orynivo/Mcp/McpPlayerBridge.cs`, `McpTools.cs`, `McpServerService.cs`)
- MCP tools can now be individually enabled or disabled under
  Settings > Integration. Disabled tools respond with `"Tool is disabled."` so
  the AI assistant knows the capability is unavailable; the active set is
  persisted in `AppSettings.DisabledMcpTools`.

### Fixed

- Fixed numbered circle labels on the equalizer frequency-response graph being
  near-black in light theme. The circles are always filled with the accent
  color, so the label text is now always white regardless of theme.
- Fixed the right-click context menu of text inputs (e.g. the MCP server port
  field) showing a white Fluent-theme popup regardless of the active theme. The
  global `ContextMenu` `ControlTheme` in `App.axaml` now provides a complete
  `Template` that binds `Background`, `BorderBrush`, `BorderThickness`, and
  `CornerRadius` to theme resources, so all context menus render with
  `AppSurfaceBrush` and `AppInputBorderBrush` in both light and dark mode.

## [0.11.0] - 2026-06-25

### Added

- Added **EQ** and **Output** quick-pick buttons to the right side of the
  transport bar (below the volume control). The EQ button opens a popup with
  an equalizer-profile ComboBox, a ⚙ button that navigates to Settings >
  Equalizer, and a themed enable/disable checkbox. The Output button opens a
  popup with an output-profile ComboBox and a ⚙ button that navigates to
  Settings > Audio Device. Both buttons use vector path icons, tooltips, and
  respect the active light/dark theme. (`EqPickerButton`, `OutputPickerButton`,
  `EqPickerPopup`, `OutputPickerPopup`, `PopupCheckBoxTheme`)
- The output-profile dropdown in Settings now shows a compact summary line
  (e.g. `WASAPI  ·  Realtek HD Audio`) beneath it when a profile is selected.
- Increased the transport album artwork from 42 × 42 px to 58 × 58 px to
  better fill the taller transport bar.
- Added named **output profiles** to Settings. The output device section is
  replaced by a dropdown listing saved profiles and three buttons: **Ausgabe
  erstellen** opens a dialog to pick a name, backend (WASAPI, Steinberg ASIO, or
  cwASIO), and device, then saves and immediately selects the new profile;
  **Ausgabe konfigurieren** re-opens the dialog for the selected profile;
  **Ausgabe löschen** removes it after confirmation. Both action buttons are
  disabled when no profile is selected. An existing single-device configuration
  is automatically migrated to a profile named "Standard" on first launch.
  (`OutputProfile`, `OutputProfileDialog`, `AppSettings.OutputProfiles`,
  `AppSettings.SelectedOutputProfileName`, `SettingsStore.NormalizeOutputProfiles`)

### Fixed

- Switching the output profile via the transport quick-pick popup now resumes
  playback at the exact position the track was at before the device change.
  `StartPlaybackAsync` accepts an `initialPosition` parameter that seeks the
  new player immediately after creation, before any UI updates or timer ticks.
- Sanitized manual MusicBrainz cover-search queries to contain only letters,
  numbers, and separating spaces, preventing punctuation such as hyphens from
  interfering with album matches.
- Fixed artist-filtered compilation albums hiding the full album header when
  opened from an artist drill-down. The cover, album metadata, actions, and
  **Show all album tracks** option now remain visible above the filtered
  tracks. CD/directory headings are shown only when the current result contains
  multiple physical groups; enabling the option shows every assigned disc and
  track, while disabling it returns to the artist-filtered result.
- Fixed selecting a track in a multi-disc album scrolling the complete grouped
  album view upward before a double-click could complete. The outer album
  scroller and nested disc tables no longer request ancestor bring-into-view
  movement when row focus changes.
- Removed redundant horizontal and vertical scrollbars from each nested
  directory/disc track table. Every table now stretches to the album viewport
  and expands to its complete header-plus-row height, leaving scrolling solely
  to the outer album view.
- Fixed Back navigation from album-track and artist drill-down views returning
  to an unrelated position. Navigation history now preserves the selected row
  and exact vertical offset for album and artist table/artwork modes, restores
  paged artwork rows before applying the offset, and waits until layout has
  completed so the normal rebind reset cannot overwrite the restored position.
- Preserved the selected album and exact table/artwork scroll position after
  assigning, replacing, or deleting a cover. Artist-list reloads after a rename
  use the same restoration path, while manual artist-image replacement updates
  the visible row in place without rebinding the list.
- Fixed the cover-search confirmation button clipping the end of its localized
  label. The button now sizes to its complete text while retaining a consistent
  minimum size.
- Fixed duplicate album copies appearing as one interleaved track list. Each
  physical album directory now resolves to its own album entry, full cover
  header, track list, and playback queue. The detail view retains a grouped
  fallback for inconsistent legacy assignments and derives those headings from
  the actual track metadata.
- Changed normalized album identity from title-only to album title plus
  physical album root. Equal titles stored in different album folders appear
  independently, while compilations remain together. Conventional multi-disc
  subfolders such as `CD1`, `CD 2`, `Disc 1`, and `Disk-2` now resolve to their
  common parent album and are shown as separate disc groups inside one full
  album detail view. Existing libraries migrate automatically.

## [0.10.0] - 2026-06-22

### Added

- Added a persisted **Always convert DSD files to PCM** option. When enabled,
  DSF and DFF playback uses the FFmpeg PCM path with ASIO/cwASIO as well as
  WASAPI, allowing volume, ReplayGain, and the parametric equalizer to apply.
  When disabled, ASIO/cwASIO retains native bit-perfect DSD playback.
- Added a persisted parametric equalizer for ASIO/cwASIO PCM, exclusive WASAPI
  PCM, and WASAPI DSD-to-PCM playback. Settings can import Equalizer APO and
  AutoEQ profiles containing preamp, peak, low/high shelf, low/high pass, and
  `GraphicEQ` definitions. Profile changes crossfade without clicks, seeks
  reset filter history, and native ASIO DSD remains bit-perfect.
- Added a graphical parametric-EQ editor with a live combined frequency
  response, editable preamp, and a dynamic filter list. Peak, shelf, and
  low/high-pass entries can be added, removed, or adjusted while playback
  previews changes through the existing debounced DSP update path.
- Moved the complete settings experience into the main window. Output, library,
  streaming, appearance, artist-information, and equalizer settings now share
  the main content area instead of opening a separate window.
- Added multiple named equalizer profiles with one selected profile at a time.
  Settings provides an empty-capable profile dropdown, a themed name dialog,
  per-profile import and editing, and confirmed deletion. Existing single-EQ
  settings migrate automatically into the profile list.

### Fixed

- Enabled the A–Z index in the Plex folder view. Available letters now come
  from the currently displayed top-level directories, jump directly to the
  first matching root folder, and follow manual tree scrolling.
- Fixed double-clicking a Plex folder name expanding only its lazy placeholder.
  The second pointer press is now intercepted before Avalonia's internal
  TreeView gesture can expose the placeholder; it uses the same asynchronous
  child-loading path as the chevron and then toggles the populated folder.
- Fixed Plex tracks composed of multiple media parts advancing to the next
  queue item after only the first part. Orynivo now preserves all ordered Plex
  part URLs and decodes them as one logical gapless track with the authoritative
  Plex duration.
- Fixed Plex playback advancing when FFmpeg reports an unexpected end of the
  HTTP stream before the duration supplied by Plex. PCM playback now reopens
  the same logical track at its last decoded position with bounded retries.
- Improved the embedded equalizer editor layout with a wider preamp input,
  consistently full-width filter-type selectors, and equally wide adjacent
  frequency, gain, and Q fields sized to keep their numeric values readable.
- Added numbered dashed frequency markers to the graphical equalizer response.
  Each marker matches the corresponding dynamic filter-row number, with
  staggered labels when nearby frequencies would otherwise overlap.
- Added a logarithmic frequency scale below the equalizer response from 20 Hz
  through 20 kHz.
- Equalizer marker numbers now remain aligned along the bottom of the graph.
  Markers at identical or nearby frequencies move sideways with a short leader
  instead of jumping upward into the response curve.
- Moved the numbered equalizer marker bubbles below the frequency scale. Their
  dashed lines continue through the scale at the exact selected frequency, so
  the relationship between each row and its frequency remains unambiguous.
- Fixed the application becoming unresponsive after saving an imported
  Equalizer APO/AutoEQ profile during active playback. UI updates no longer
  wait for the audio thread's DSP work; profile changes and seek resets are
  handed to that thread atomically. Profile file parsing also runs off the UI
  thread with visible progress and bounded file/filter counts.
- Equalizer enable/disable now previews immediately during playback without
  closing Settings; cancel restores the previous profile state. Saving an EQ
  change no longer reopens the unchanged WASAPI endpoint. Actual backend or
  device changes stop and dispose the old player off the UI thread, while
  endpoint synchronization and ASIO/WASAPI device enumeration also run in the
  background to prevent driver calls from freezing the application.
- Fixed another live-EQ AppHang caused by competing settings activity. EQ
  preview requests are now debounced and executed entirely away from the UI
  event handler. Initial output-device enumeration no longer starts twice, and
  later backend enumeration requests are serialized so native driver discovery
  cannot overlap within the settings view.
- Fixed leaked WASAPI endpoint COM objects during device enumeration and device
  opening. Repeatedly opening Settings no longer accumulates undisposed
  `MMDevice` and `MMDeviceEnumerator` instances while playback is active.
- Saving an EQ-only settings change no longer performs unrelated synchronous
  work on the UI thread. Settings persistence and credential protection run in
  the background, while theme, language, sidebar, artist-provider, ReplayGain,
  Plex navigation, and output-device refreshes execute only when their
  corresponding values actually changed.
- High-resolution PCM and WASAPI-converted DSD now fall back to the highest
  sample rate and output precision supported by the selected device instead of
  failing when the source rate or bit depth exceeds the endpoint capability.
  The transport shows both source and converted PCM rates when they differ.

## [0.9.0] - 2026-06-21

### Added

- Added CUE-sheet support for large FLAC/WAV images. Library scans expose CUE
  entries as independently searchable virtual tracks with their own metadata,
  queue identity, playback-history records, and persisted queue positions.
  ASIO PCM and exclusive WASAPI playback seek into the shared source file and
  stop at each track's CUE boundary without creating split files.

### Fixed

## [0.8.0] - 2026-06-21

### Added

- Added a localized, editable **Up next** view backed by the active playback
  queue. Tracks, albums, folders, search results, playlist entries, and Plex
  tracks can be played next or appended through themed context flyouts. Queue
  entries can be removed or moved up/down, the full queue can be saved as a
  regular playlist, and safe queue entries plus the current index persist
  across restarts. Credential-bearing Plex URLs are never stored.
- Added Windows System Media Transport Controls integration. Global media keys
  now control Orynivo's existing transport and queue, while Windows receives
  playback state, seekable timeline data, title, artist, album, and artwork for
  the media overlay and lock screen. Metadata follows gapless transitions,
  podcasts, Plex tracks, and live internet-radio title changes.
- Added a themed **Save as playlist** action to the album-detail header. It can
  append every currently displayed album track to an existing playlist or
  create a new playlist, respecting the current album-track scope. The album
  card now spans the available content width, with the favorite action directly
  before the album title and the cover/playlist actions aligned side by side.

### Fixed

- Preserved manual artist renames across watcher updates and later library
  scans. The original tag name is stored as exactly one alias entry in
  `artist_aliases`, and rescanned tracks continue using the canonical database
  display name instead of recreating the old artist. Merges also record the
  pre-merge names of both artists as aliases without adding a redundant entry
  for the new canonical name.
- Fixed A-Z navigation in virtualized artist and album artwork views after
  reopening or rebinding the view. Alphabet jumps now load through the target,
  wait for wrap-panel layout, and then scroll reliably; stale offsets are reset.
- Fixed artist rename and merge from the artist-information view by separating
  modal dialogs from database connection lifetime and running the final
  transaction without holding a UI-thread connection open.
- Fixed the **Rename** button in the artist-name dialog not confirming pointer
  clicks. Enter and the primary button now use the same confirmation path.
  Both dialog buttons use centered text and explicit theme-aware hover and
  pressed colors instead of the default black Fluent hover surface.
- Artist renames now update the information-screen title and active artist
  state immediately after the SQLite transaction. The potentially lengthy
  full Lucene rebuild runs afterward in the background instead of making the
  rename appear to have no effect.
- Moved artist-name collision lookup off the UI thread and combined it with a
  collision-free rename in one database session. Large libraries no longer
  stall between closing the dialog and starting the transaction, and the
  committed name is verified before refreshing the artist view.
- Moved the verified SQLite rename/merge operation into the artist-name
  dialog's confirmation lifecycle. **Rename** and Enter now keep the dialog
  open while committing on a background thread, close only after success, and
  display a localized error in place when persistence fails. Merge failures
  are now caught and displayed instead of propagating as unhandled exceptions.
- Restored the theme-aware currently-playing highlight for track nodes in the
  local folder view. Tree highlighting now resolves local `FolderTag` paths as
  well as Plex folder-track paths, and remains visible when the playing node is
  also the currently selected tree item. Local track nodes are indexed by
  absolute path so previous/next and gapless transitions update the exact
  following entry independently of TreeView realization. Local file entries
  now use a dedicated visible header surface because Avalonia's Fluent tree
  template did not consistently render their container background.
- Restored the playlist context actions for track tables, search results,
  album rows, and folder-tree entries. Dynamically generated actions now open
  as themed pointer-positioned flyouts, including existing playlists, new
  playlist creation, and removal from the active regular playlist. Rows and
  folder nodes now carry and directly open their own `ContextFlyout` instances
  using the same tunnel-phase pattern as the working sidebar actions.

## [0.7.2] - 2026-06-21

### Fixed

- Fixed the application failing during startup because the lyrics
  `ControlTheme` contained a descendant selector that Avalonia rejects only at
  runtime. Active-line typography now binds to direct selected-item properties.
- Fixed synchronized-lyrics highlighting being lost again after the shared
  dialog list-theme changes. Active-line typography is now driven directly by
  the programmatically selected lyrics item instead of a nested bound class in
  the data template.
- Restored synchronized-lyrics highlighting by keeping the active LRC line
  selected in the non-interactive lyrics list in addition to its active data
  state. The manual LRCLIB result list now has a dedicated plain theme instead
  of header-card styling, with explicit readable selected text colors in light
  and dark themes.
- Fixed the custom LRCLIB result-item template bypassing its `DataTemplate` and
  displaying the complete lyrics record in the left result list. The template
  now forwards the configured content template so entries again show title,
  artist, duration, album, and synchronization status.
- Removed black selected text from the artist-image search and audited all
  Avalonia views for the same fallback-color issue. Artist-image, cover, and
  lyrics result lists now share a theme-aware item container, while every
  window receives the primary theme text color as a global fallback.

## [0.7.1] - 2026-06-21

### Fixed

- Added the theme-aware now-playing highlight to tracks in the Plex folder
  tree. It follows normal and gapless playback transitions, clears on stop,
  and remains correct for collapsed or newly lazy-loaded branches.
- Fixed Plex Artists, Albums, Tracks, and Folders mode buttons starting loads
  for both checked and unchecked radio-button events. Mode requests now use
  stable server, library, token, and view snapshots and discard stale
  asynchronous responses before they can display another mode's rows or
  columns.
- Fixed Plex folder nodes expanding into blank rows. Using `TreeViewItem`
  controls through `ItemsSource` caused Avalonia to create incorrect nested
  containers, while expanding before asynchronous loading exposed the empty
  placeholder. The expand action now waits until real children exist, folder
  classification uses Plex's folder marker, keyboard expansion follows the
  same guarded path, and failures remain retryable.

## [0.7.0] - 2026-06-21

### Added

- Expanded smart playlists with a dedicated localized editor for year, artist,
  album, duration, recently added or played windows, never-played tracks,
  minimum/maximum play counts, random or playback-recency ordering, and result
  limits. Creating a smart playlist remains the compact active-filter workflow;
  the advanced editor is available by right-clicking an existing smart
  playlist in the sidebar. Existing smart-playlist JSON stays compatible.
- Added automatic recursive library monitoring with one `FileSystemWatcher`
  per available configured root. Create, change, rename, and delete events are
  debounced before updating SQLite and Lucene, while a full reconciliation runs
  after 10 minutes and every 30 minutes as protection against missed or
  overflowed watcher events.
- Manual scans, watcher batches, and periodic reconciliation now share a single
  scanner gate. Full scans also remove missing tracks from SQLite, not only
  Lucene, and scan results report the removed-file count.
- Added drag-and-drop reordering for data columns. Column order is persisted
  independently per table and dynamic main-content view, while fixed artwork
  and action columns retain their structural positions.
- Added a localized, context-sensitive column chooser opened by right-clicking
  table headers. Local track tables can show additional tag, file, technical,
  date, and ReplayGain metadata; radio and podcast tables expose only relevant
  catalog fields. Selections persist per table/view, while artwork/action
  columns remain fixed and at least one data column stays visible. Active
  columns have an explicit check mark and selected-row background in the menu.
- Added user-resizable columns to all application tables. Widths are persisted
  per table and main-content view in `settings.json`, captured before dynamic
  column sets are replaced, and restored on the next application start.
- Added optional ReplayGain volume adjustment for PCM playback with disabled,
  track, and album modes. The preferred value falls back to the other available
  ReplayGain tag, gain is combined with the user volume using saturating sample
  conversion, and native ASIO DSD output remains bit-perfect.
- Library scans now import track and album ReplayGain metadata. Each configured
  library root receives a one-time refresh of unchanged tracks on its first
  scan after this update.
- Added UTF-8 M3U8 import and export for regular playlists. Relative paths are
  resolved against the playlist file and written relatively where possible;
  missing local files and HTTP/HTTPS entries are retained, while URLs carrying
  user-info credentials or Plex tokens are skipped. Smart playlists remain
  live and are not exported as static M3U8 files.
- Added gapless playback for sequential PCM queues through ASIO, cwASIO, and
  exclusive WASAPI. The next FFmpeg decoder is started and prefetched while
  the current track is playing, then continues through the existing device
  session. Transport metadata, ReplayGain, and playback history follow the
  audible buffered-frame boundary. Shuffle and native ASIO DSD retain
  title-by-title playback.
- Added a themed favorite button to the album detail header shown above an
  album's track list. Its heart state updates immediately and persists the
  album favorite without leaving the detail view.
- Matched the album detail header to the shared radio, podcast, and library
  card design with the accent-colored border and asymmetric rounded corners.

### Fixed

- Added a theme-aware background highlight for the currently audible item in
  track, search, radio, and podcast-episode tables. The highlight follows
  gapless transitions, clears when playback stops, survives navigation back to
  a list, and does not override the stronger selected-row background.
- Restored seek-slider dragging and track-position clicks during multi-track
  gapless PCM playback. Seeking now clears buffered output, restarts the
  current decoder at the selected position, and prepares the following track
  again. User-volume changes are applied at the live ASIO/WASAPI output stage
  instead of being delayed by already prefetched PCM samples.
- Synchronized the WASAPI transport volume bidirectionally with the selected
  Windows output device. Changes made through Windows now update the displayed
  slider and percentage. The custom position-slider thumb also uses an
  explicit two-way value binding and handled pointer-release routing, restoring
  dragging as well as direct track clicks.
- Fixed the position slider jumping backwards after seeking when the current
  track reached its buffered end during gapless playback. Seek offsets are now
  tracked independently per queued title and remain active until that title is
  no longer audible.
- Restored sidebar context menus for personal radio stations, pinned podcasts,
  regular playlists, and smart playlists. Dynamic entries now use Avalonia
  `MenuFlyout` instances opened at the pointer position. Right-button presses
  are intercepted before the `ListBox` selection logic, while accordion and
  repeated-item navigation handlers explicitly ignore non-left mouse buttons.
  Flyout surfaces, borders, text, hover, pressed, and separator colors follow
  the active Orynivo theme.
- Unified the table-header column chooser with the same themed `MenuFlyout`
  presentation. It remains open while multiple columns are toggled and still
  prevents hiding the final selectable column. Explicit menu entries now share
  the same theme-aware text colors in sidebar and table-header flyouts.

## [0.6.0] - 2026-06-19

### Added

- Licensed Orynivo's original source code and documentation under Apache
  License 2.0, with repository and release copies of `LICENSE`, `NOTICE`,
  third-party notices, and applicable license texts.
- Expanded the About dialog with the Orynivo license, copyright, trademark
  scope, FFmpeg, Steinberg ASIO, and third-party licensing information.
- Added real-time DSF/DFF-to-PCM playback through exclusive-mode WASAPI. The
  conversion uses FFmpeg without temporary files, prefers PCM rates in the
  44.1 kHz family, and falls back to common 48 kHz-family rates supported by
  the selected endpoint.
- Added localized transport and status-bar indicators in German, English,
  French, and Spanish when DSD is being converted to PCM, including the active
  PCM output sample rate.

### Fixed

- Fixed the table-header column chooser not opening on right-click and then
  crashing because Avalonia 11.2 retained internal ownership for dynamically
  attached `ContextMenu` instances. The chooser no longer calls the
  `ContextMenu` API.
- Corrected themed scrollbar behavior so arrow buttons move one row and clicks
  above or below the thumb move by one visible table page with one-row overlap.
  Track-table A-Z highlighting now follows the top visible row during manual
  scrolling, and alphabet jumps use the same trimmed sort-title ordering as the
  database.
- Trimmed leading and trailing whitespace from track titles and sort titles
  during scans, database upserts, migration of existing libraries, display,
  and Lucene indexing. The search-index schema is advanced so existing indexes
  rebuild with normalized title values.
- Fixed search and other text inputs switching to a white focused background
  while retaining white text in the dark theme. Normal, hover, focused,
  placeholder, and border colors now follow Orynivo's active light or dark
  input palette.

## [0.5.0] - 2026-06-17

### Changed

- Migrated the entire frontend from WPF to Avalonia UI 11.2 for cross-platform
  compatibility. The target framework remains `net8.0-windows` to preserve
  Windows-only features (ASIO, DPAPI). All XAML files are now AXAML; styles use
  `ControlTheme` with pseudo-class selectors instead of WPF `ControlTemplate`
  triggers; `FuncDataTemplate<T>` replaces `FrameworkElementFactory`; and
  thumbnail generation in `ArtworkCache` is rewritten with SkiaSharp.
  New NuGet packages: `Avalonia.Fonts.Inter`, `SkiaSharp`.

### Added

- Automatic FFmpeg download: when `ffmpeg.exe` and `ffprobe.exe` are not found in
  the application directory or the system PATH, Orynivo downloads the BtbN
  LGPL-essential Windows build from GitHub Releases on first start, extracts the
  binaries next to the application executable, and makes them available for the
  current process session. A localised progress indicator is shown in the startup
  screen. If the download fails, a warning dialog is displayed and the application
  starts without audio playback capability.

### Fixed

- Restored visible text in Avalonia table/list navigation and restored vector
  glyph rendering in themed buttons after the Avalonia UI 11.2 migration.
- Fixed Avalonia transport controls after the migration: the play/pause glyph is
  centered, transport sliders show their track background again, sidebar
  accordion headers toggle reliably, and album/artist artwork tiles lazy-load
  visible images without eagerly decoding every card.
- Moved album and artist artwork tile bitmap decoding off the UI thread,
  reduced tile title text sizes, and enlarged the table/artwork view switcher.
- Reduced album and artist artwork view stalls by binding artwork cards in
  incremental pages and loading only the visible page's images.
- Increased the transport play/pause glyph to 20 px and reduced default button
  and dropdown text sizes for better visual consistency.
- Fixed track-filter popup rows so facet counts stay visible in a fixed right
  column and checkbox changes apply the selected state only once.
- Rebalanced the Tracks table layout with calmer row/header sizing, proportional
  title/artist/album columns, and spacing between the table and A-Z index.
- Harmonized DataGrid cell font sizes so link/button columns match regular text
  columns.
- Restored the global header search box in regular library views after its
  Avalonia visibility condition was inverted.
- Restored the folder-structure view by eagerly populating root nodes and
  falling back to detected library roots when configured roots do not match the
  scanned track paths exactly.
- Removed the WPF-style placeholder row from folder nodes so expanding a folder
  shows its actual child folders and tracks under Avalonia.
- The folder-structure expand/collapse glyphs now use themed text and accent
  colors instead of the default dark template paths, including non-template
  ToggleButton glyph paths.
- The folder-structure chevrons now also override Avalonia Fluent's
  `TreeViewItemForeground` resources, which are used directly by the default
  chevron path.
- Increased the Tracks table column-header row height so the first visible row
  no longer looks cramped against the header.
- Fixed clipping in the Tracks favorite column by giving the favorite button
  fixed centered icon bounds.
- Fixed transport and volume slider thumbs staying centered by binding the
  custom slider template track to the slider minimum, maximum, value, and
  orientation.
- Centered the missing-cover text and search button inside album artwork cards.
- Increased album artwork card height and tightened the favorite button bounds
  so the favorite glyph stays inside the card.
- Matched the Internet Radio and Podcast discovery cards with the accented
  podcast detail border and aligned their top spacing.
- Added a shared accent-bordered intro card below the plain main header so
  Dashboard, Artists, Albums, Tracks, and Folder structure match the Radio and
  Podcast view style without moving search/filter controls into the card.
- Kept the library intro card compact and aligned the A-Z index with the content
  area instead of the intro card.
- Added a session-wide navigation history so the main header Back button returns
  through sidebar navigation, search results, album tracks, artist drill-downs,
  dashboard links, playlists, podcasts, radio, folder, and Plex library views.
- Kept the global Back button hidden on the initial view and restyled it as a
  subtle light-blue left chevron.
- Top-aligned the main header title and global Back chevron so taller controls
  on the right no longer shift headings vertically between views.
- Added Favorites-only header toggles for Artists and Albums that apply to both
  table and artwork-card modes.
- The Artists/Albums Favorites-only checkbox now uses the dark header theme with
  a visible light-blue outline and stable hover text color.
- Artwork card views now keep sparse artist and album card sets aligned from the
  top-left instead of centering them vertically.
- Transport-bar buttons now use dynamic light/dark theme resources instead of
  fixed dark colors, including hover and disabled states.
- Fixed the daily-history dialog close button typography/alignment and render
  non-actionable history title, artist, and album cells as plain text instead of
  disabled link buttons.
- Reduced Settings appearance checkbox label typography to match the rest of
  the settings dialog.
- Expanded the Settings output-device dropdowns to use the available dialog
  width.
- Fixed a crash when opening a dashboard calendar day's listening history under
  Avalonia UI 11.2 by removing an invalid zero row-header width from the dialog
  DataGrid.
- Fixed dark-theme text colors in the dashboard daily-history dialog table and
  link buttons.
- Removed nullable and Avalonia runtime-loader build warnings from the migrated
  Avalonia UI dialogs and main-window search/navigation code.
- Fixed a race condition in the DFF and DSF audio players where `SeekAsync`
  could write to `FileStream.Position` while `PumpAsync` was concurrently
  reading from the same stream, causing corrupted reads or exceptions.
  A `SemaphoreSlim` now serialises file seeks and reads, matching the guard
  already used by the WASAPI and FFmpeg ASIO players.
- Fixed a missing `volatile` modifier on the `_paused` field in the DFF, DSF,
  and FFmpeg ASIO players, which could prevent pause/resume from taking effect
  immediately due to CPU cache visibility.

## [0.4.0] - 2026-06-15

### Fixed

- Plex folder playback now queues only the tracks on the selected file's
  immediate folder level, continues with sibling entries, and respects shuffle
  without recursively adding tracks from subfolders.
- Prevented the application from freezing after saving or reopening Plex server
  settings by removing synchronous waits on asynchronous credential file I/O.
- Applied the active light or dark theme to the Plex server dialog title bar,
  input fields, and all dialog buttons.
- Standardized line spacing throughout the About dialog license list.
- Fixed dark-theme text and check-mark styling for the sidebar visibility
  options under **Settings > Appearance**.
- Fixed native DSF playback from drives that return partial asynchronous reads
  by preserving complete per-channel DSF blocks and stopping at the declared
  data chunk boundary.
- Fixed cwASIO playback with legacy Windows ASIO drivers that reject the
  optional cwASIO instance-name extension, affecting both PCM and native DSD.
- Podcast filters no longer require a podcast title before returning results.
- Radio genres no longer show misleading counts produced by summing
  overlapping normalized tags.
- Radio genre filters no longer return only matches from the initial
  100-station result page.
- Radio Browser tag queries now use compatible lowercase search values.
- Cached podcast categories without Apple genre IDs are refreshed
  automatically.

### Added

- Plex music-library browsing with switchable artist, album, track, and lazy
  folder views, artist/album drill-down, paginated large result sets, and
  playback through the existing audio path.
- Audio-format columns in track search results and playlist/favorites-based
  track lists.
- Configurable multi-server Plex integration with Windows user-bound encrypted
  access tokens and connection testing under **Settings > Streaming services**.
- A hideable Plex sidebar accordion below podcasts that lists configured
  servers and only their music libraries.
- Podcast details through the transport info button during episode playback,
  showing centered podcast artwork, episode metadata, and the RSS summary.
- Accordion sections for local library, personal radios, pinned podcasts, and
  playlists in the main sidebar, with per-section visibility options under
  **Settings > Appearance**, independent expansion, and persisted open states.
- MIT-licensed cwASIO output backend with PCM and native DSD support through
  the existing bridge and playback paths.
- Separate **Steinberg ASIO** and **cwASIO** choices in Settings; availability
  follows the corresponding native bridge DLL.
- cwASIO native builds and Release artifacts in GitHub Actions without
  requiring the separately distributed Steinberg SDK.
- Modal daily playback history opened from populated Dashboard calendar days,
  including playback time, media type, title, artist, album, listened duration,
  and total duration.
- Daily-history links for playing local tracks and opening the corresponding
  album or artist view.
- Playback-history entries for podcast episodes and internet-radio sessions,
  including media type, display metadata, external identifiers, duration, final
  position, and completion state.
- Podcast discovery through the regional Apple Podcasts catalog.
- Persistent **My Podcasts** sidebar entries with removal through the context menu.
- Complete RSS/Atom episode lists sorted from newest to oldest.
- Podcast episode playback through the existing PCM audio path.
- Per-episode resume positions and **New**, **Started**, and **Played** states.
- Automatic podcast progress persistence during playback and when playback stops.
- Podcast category and feed-language filters that work without a title query.
- Cached radio-genre and podcast category/language catalogs under
  `%LOCALAPPDATA%\Orynivo\catalog-filter-cache.json`.
- Podcast detail cards with large artwork, feed description, categories,
  language, latest publication date, and total/unheard/started statistics.
- German, English, French, and Spanish localization for the podcast interface.

### Changed

- The About dialog now displays the Orynivo logo above the author and license
  information.
- Dashboard calendar and top-genre durations now use `HH:mm:ss`.
- Dashboard calendar genres and Top 10 genre labels now open Tracks with the
  clicked genre filter preselected.
- Dashboard calendar listening time now includes local tracks, podcasts, and
  internet radio while music genre statistics remain track-only.
- Internet-radio genre selections now run server-side Radio Browser queries
  instead of filtering only the first 100 loaded stations.
- Multiple selected radio genres use OR semantics and can load up to 10,000
  stations per selected genre.
- Initial radio filters use the complete Radio Browser tag catalog.
- Podcast categories use Apple's regional genre taxonomy and genre IDs.
- Podcast language discovery uses regional top podcasts, pinned podcasts, and
  subsequent search results.
- Filter options use cached catalog data before a search and are recalculated
  from the result set after a text search.
- Podcast episode headers now use the same accent border style as the
  internet-radio now-playing card.
- The podcast episode Back button is hidden when the view was opened directly
  from **My Podcasts**.

## [0.3.0] - 2026-06-14

- Baseline release before the podcast and catalog-filter additions documented
  above.
