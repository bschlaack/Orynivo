# Changelog

All notable changes to Orynivo are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Added "Most listened albums" and "Most listened artists" analytics cards to the
  dashboard alongside Top Genres, merging local, remote Orynivo Server, and Plex
  playback. A shared period selector (All time / This year / This month / Last 30
  days / Last 7 days) governs all three cards. Album and artist entries link into
  their own library (local, remote, or Plex).
- Added a source filter (Tracks / Radio / Podcasts / Remote / Plex) to the daily
  playback-history dialog; only the categories present that day are shown.
- Playback history now stores a stable Plex context (server plus track, album, and
  artist rating keys) so Plex history entries stay identifiable and their albums
  and artists are clickable throughout the history and dashboard statistics.

### Changed

- Background library scans no longer auto-reload the visible view. A subtle
  sidebar status ("Updating library… N / M files") shows scan/index activity,
  and reloadable views offer a controlled "New library data available" refresh
  button instead of navigating on their own.
- Remote server rows in Settings now show a clear "Unreachable" status with the
  last successful connection time (persisted per server in `server-status.json`)
  when a server cannot be reached.
- Split several `MainWindow` domains into dedicated partial files for dashboard,
  history, playlists, internet radio, and Orynivo Server navigation code.

### Fixed

## [0.23.3] - 2026-07-05

### Added

- Added an artist-info button beside the artist name in album/track detail
  headers, opening the same biography, image, and rename/merge view used by the
  artist list.
- Restyled the favorite heart with a warmer Orynivo-specific color and adjusted
  glyph across tables, artwork cards, album headers, and the transport bar.

### Fixed

- Reduced local and Orynivo Server artist rename work by updating only the
  affected Lucene search-index documents instead of rebuilding the whole index;
  remote renames now also advance the server library-change timestamp so client
  caches do not keep stale artist metadata.
- Fixed saved internet-radio sidebar entries leaving the main content loading
  skeleton visible while the stream was already playing.
- Fixed the daily listening-history dialog so album titles are captured and
  shown for playback-history rows even when they cannot be recovered through the
  local track join, including remote and Plex tracks.
- Made daily listening-history album cells open the corresponding album track
  view for local and Orynivo Server history entries.
- Prevented background library watcher refreshes from replacing playlist,
  dashboard, drill-down, search, or detail views; sidebar rebuilds now preserve
  the selected navigation item without firing an unintended navigation change.

## [0.23.2] - 2026-07-05

### Fixed

- Fixed remote Orynivo Server artist information from the shared Artists view so
  the info, refresh, rename, and image actions use the clicked row's server
  instead of the previously active server context.
- Fixed manually selected remote Orynivo Server artist images disappearing when
  reopening artist information by assigning stable artist-artwork URLs to the
  updated row before caching the uploaded image bytes.
- Restored the small thumbnail column in the Artists and Albums table views,
  including for existing saved column masks unless the thumbnail is hidden again
  through the column chooser.
- Invalidated the client-side remote artist-list cache after remote artist
  profile or image updates, and hydrated remote artist information from the
  server's cached profile before falling back to a fresh external lookup.
- Made playback-history artists clickable for both local and Orynivo Server
  entries, including the calendar day dialog and the Dashboard's recently
  played cards/full view.

## [0.23.1] - 2026-07-05

### Fixed

- Reduced the Artist artwork-card height after adding the source badge so the
  grid no longer leaves oversized vertical gaps between rows.
- Prevented the content loading overlay from intercepting mouse wheel and card
  clicks after artwork views finish loading, and synchronized the Artist artwork
  scroll calculations with the updated card height.
- Fixed full-library search so artist-name queries also populate the Albums and
  Artists result sections when the match comes from album-artist metadata; the
  Orynivo Server full-search endpoint now correctly maps Lucene track hits to
  album and artist rows instead of treating track IDs as entity IDs.

## [0.23.0] - 2026-07-05

### Added

- The Tracks search now honours the active facet filters. The **source** facet
  restricts which sources are searched at all (e.g. with only an Orynivo Server
  selected, typing in the search box searches just that server and hides local
  results), while the favourite, genre, format, and bitrate facets additionally
  filter the track results. Saving the search as a smart playlist now also stores
  the search text (new `SearchText` criterion, matched case-insensitively against
  title, artist, and album), and the smart-playlist editor exposes it as a
  **Search text contains** field. The **Save smart playlist** action is available
  whenever a search query or a facet filter is active.
- The Folder structure view now groups its content by source: a top-level
  **Local** node (shown only when a local library directory is configured) and
  one node per configured Orynivo Server (shown only when the server reports
  folder tracks), each expanding into its own folder tree. With a single source
  the group expands automatically; with several sources the groups stay
  collapsed so every source stays visible at the top. Remote folder file nodes
  play through the authenticated stream URL and carry the same transport
  metadata as remote track rows.
- Prepared shared local/Orynivo Server row metadata for common Artists, Albums,
  Tracks, and search-result library views, including an optional `OS` source
  column with the server name as tooltip when remote rows are shown.
- Local playlists can now contain mixed local and Orynivo Server tracks. Server
  tracks are persisted as `orynivo://` references so authenticated `?key=`
  stream URLs are still not stored in SQLite.
- Search result rows for tracks, albums, and artists now expose the same leading
  favorite heart action as the main library tables.
- Smart playlists opened from the local playlist group now resolve against the
  combined local and configured Orynivo Server track set.
- Shared Artists, Albums, and Tracks views now load configured Orynivo Server
  rows into the combined row set before the first table bind, avoiding a
  post-startup table replacement while preserving mixed local/server results.
- Added a source facet to the Tracks filter with **Local** and configured
  Orynivo Server entries. Smart playlists can persist the same source
  restriction through stable source keys.
- Playlist tables now expose the favorite heart as the first column and the
  source column directly beside it. Source tooltips use theme-aware foreground
  and background colors.

### Fixed

- The content loading skeleton now fully covers the content area (it spans the
  intro-card and table rows edge to edge) instead of leaving a margin band where
  part of the table stayed visible during loading, most noticeable when opening
  playlists.
- Opening the Folder structure view is dramatically faster on large merged
  libraries. The folder tree now materializes lazily — only the expanded nodes
  are built — instead of eagerly creating a tree node for every one of ~150k
  tracks, which froze the UI thread for ~9 seconds. Children are populated before
  the node expands (the proven Plex lazy-folder pattern) so they render reliably.
  The already-cached server folder-track list (keyed by the server's
  `LibraryChangedAt`, re-downloaded only when it changes) is now read and
  deserialized off the UI thread, and a remote directory's descendant paths are
  collected from the in-memory folder tree so context-menu actions still see
  not-yet-expanded folders.
- The source-badge tooltip in the shared tables now renders its text in the
  theme foreground instead of black on the dark surface. The tooltip content is
  an explicit `TextBlock` with a local theme foreground, because a string tooltip
  does not inherit `ToolTip.Foreground` under the default tooltip theme.
- The Albums table no longer freezes on startup once a remote Orynivo Server is
  merged into the library. Building a remote album row's right-click playlist
  menu resolved the album's track list through a **synchronous** server request
  on the UI thread while the row was being realized inside the `DataGrid` layout
  pass, blocking the whole table (thread dump: `GetPathsForRow` →
  `TaskAwaiter.GetResult` under `DataGrid.MeasureOverride`). The remote album
  playlist targets are now resolved off the UI thread and only when the flyout is
  actually opened. Local-only libraries were unaffected because local album rows
  never triggered that server round-trip.
- The Artists table no longer risks a UI-thread stall with a large merged
  library. Lazy per-row artist profile lookups now open the SQLite database,
  write the cached profile, and decode the artist image on a background thread
  instead of on the UI thread, and each visible row is marked as fetched for the
  session so a failed download (offline, or a concurrent library scan holding the
  write lock) no longer re-triggers a network request and database open on every
  scroll pass.
- Artist and album table thumbnail columns are now optional and hidden by
  default in large mixed local/server tables to avoid the album-table UI stall
  seen when many remote artwork-capable rows were bound at once.
- Removed the obsolete Local child node from the Library sidebar now that local
  and Orynivo Server sources share one library.
- Existing saved table column masks now show the new `OS` source column by
  default while still allowing it to be hidden from the column chooser.
- Added per-server background timeouts while loading the shared Artists, Albums,
  Tracks, and smart-playlist views so an unavailable server cannot leave the UI
  stuck on startup.
- Removed the post-bind Orynivo Server row append from the active Artists,
  Albums, and Tracks tables after diagnostics showed that replacing a large
  already-visible DataGrid could freeze the UI shortly after startup.
- Reduced scroll-time work in the shared Artists, Albums, and Tracks tables so
  the A-Z index follows the visible row without walking the complete visual
  tree on every scroll event.

## [0.22.1] - 2026-07-04

### Changed

- Updated the Windows application icon with a transparent background and updated
  the logo shown in the splash screen and About dialog.
- Updated the program wordmark shown at the top of the main sidebar.
- Removed the gradient logo containers and let the sidebar, splash screen, and
  About logos use the available width directly.
- Widened the main sidebar and reserved scrollbar space in the navigation list
  so selected-item highlights remain fully visible when the menu scrolls.
- Added light-theme variants for the splash/About logo and the main-sidebar
  program wordmark.

## [0.22.0] - 2026-07-04

### Added

- Reworked the Dashboard into a more personal "music hub": a time-of-day
  greeting with a short tagline now opens the page, followed by a new **Recently
  played** strip of compact cards (local album artwork or an initials
  placeholder, with a hover play affordance) that play the track in place
  without leaving the view. **Recently added** albums now use the same artwork
  cards as the normal Albums view, so their covers can be changed (search /
  reassign / delete) and albums can be toggled as favorites directly from the
  dashboard — for local and remote Orynivo Server albums alike. The **Calendar**
  and **Top genres** blocks are now visually separated and shown side by side on
  wide windows (stacking on narrow ones), and Top genres is a modern analytics
  card with numbered colour-coded rank chips and thin proportional bars.
- Added a **Show all** link to the Dashboard's **Recently played** and **Recently
  added** sections. It opens a full-page view of up to 200 entries with the same
  cards and actions as the dashboard strips, and integrates with the Back button:
  returning from an opened album lands back on the full-page view, and Back again
  returns to the dashboard.
- Added subtle UI motion for main navigation: album/artist artwork cards now
  show a lightweight hover overlay, Dashboard album cards react on hover,
  sidebar accordion rows fade/collapse instead of disappearing abruptly, and
  Dashboard, library, remote-library, and album-detail view changes use short
  fade-ins with a compact skeleton/progress loading overlay.
- Added an optional +6 dB PCM output boost in Settings > Playback. It applies
  to every PCM playback path, including local files, remote/Plex streams,
  radio, podcasts, and DSD sources when they are converted to PCM; native DSD
  output remains bit-perfect and unchanged.

### Fixed

- Fixed manual MusicBrainz cover search failing on stylized album titles with
  punctuation such as `M!ssundaztood`. The primary release/artist query now
  preserves punctuation and URL-encodes the complete query, with compacted and
  spaced punctuation fallbacks when the exact query finds no cover results.
- Fixed the Dashboard being rendered twice below itself when Orynivo started
  with Dashboard as the restored last view. Dashboard rebuilds are now versioned
  and applied atomically, so a startup layout reflow cannot append stale content
  from an earlier asynchronous build.
- Added roomier row and column spacing to the Dashboard's full-page **Recently
  played** view so the 200-card grid no longer feels vertically cramped.
- Restyled Dashboard **Recently played** cards to match the shared artwork-card
  language: they now use the app surface background, a separating gridline
  border, asymmetric card/cover corners, and the same accent border on hover as
  the Recently added album cards.
- Fixed old remote **Recently played** entries showing the selected output
  device (for example "Topping USB Audio") as the artist. Remote history cards
  now refresh their title/artist from the configured Orynivo Server metadata,
  and their hover border explicitly uses the current theme accent brush.
- Added manual artist-image search to the artist-info view opened from a
  currently playing remote Orynivo Server track. The shared artist-image search
  dialog now focuses and selects its editable query on open and starts a new
  search with Enter, making it easier to change the search text and retry from
  both track and artist artist-info views.
- Added an editable artist field to manual album-cover search. Album cover
  lookups now prefill the known album/display artist and send both
  `release:"album"` and `artist:"artist"` to MusicBrainz when the field is set,
  improving broad titles such as "Greatest Hits"; clearing the artist field
  keeps the previous album-only search behaviour.
- Aligned the manual cover-search and artist-image-search dialogs by placing
  the **Search again** action beside the active query field and adding a search
  icon to the button in both dialogs.
- Fixed Dashboard **Recently played** cards for Orynivo Server tracks not showing
  cover art and replaying with only a bare stream URL. Remote history entries
  now resolve back to their configured server track, load the server-side track
  artwork, and register the full remote row before playback so the transport
  shows cover/title/artist and keeps remote artist info, lyrics, favourites, and
  waveform behaviour available. New remote history entries also store a stable
  server/track identifier while older entries still resolve from their stream
  URL.
- Fixed the Dashboard **Recently played** hover play button not appearing on
  many cards (typically the cover-less ones): those were remote server / Plex
  tracks that were treated as not playable. Music-track history entries are now
  playable whenever they are a locally available file **or** a playable stream
  URL, so their cards show the hover play button and play the track in place
  (replacing the queue with just that track) instead of switching to the Tracks
  view. Radio and podcast history entries keep their dedicated views. The play
  overlay also now has an explicit size so it fills avatar-only cards.
- Fixed the Dashboard **Recently added** album cards having no hover effect: the
  card hover/cover-reveal styles were scoped to `ListBoxItem` and now also react
  to the card border being hovered, so the shared artwork card animates in the
  dashboard strip and full view too.
- Fixed the **Up next** queue order when starting a remote Orynivo Server album
  whose tracks span two directories (for example a multi-disc release without
  disc-number tags). Double-clicking a remote track now builds the queue from
  the actually clicked directory group in its displayed order, matching local
  albums, instead of rebuilding it from the raw, ungrouped album track list.
- Improved remote Orynivo Server playback start latency on ASIO/cwASIO. A remote
  PCM track (FLAC, MP3, etc.) is no longer treated as a possible native DSD
  stream when the server metadata already identifies its format, so it skips the
  two native DSF/DFF header probes (each a wasted HTTP round-trip) and starts
  through the FFmpeg PCM path directly. This also restores gapless playback for
  consecutive remote PCM tracks on ASIO/cwASIO, which previously played
  track-by-track. Tracks with no usable format metadata still fall back to the
  conservative native-DSD probe.
- Further reduced remote Orynivo Server playback start latency on all backends
  by skipping the separate FFmpeg probe when the server already reports the
  track's sample rate. The player now builds its technical info from the cached
  server metadata and only starts the decoder, removing one HTTP round-trip per
  track (initial start and gapless prefetch); DSD sources and tracks without a
  reported sample rate still probe as before.
- Fixed remote Orynivo Server DSF playback using the FFmpeg PCM path while the
  transport claimed native DSD output. Remote DSF files now stream natively over
  HTTP byte ranges to ASIO/cwASIO, and the transport shows **DSD nativ** only
  after the native DSF player has actually started; malformed or unsupported
  remote DSF streams fall back to DSD-to-PCM and are labelled accordingly. The
  remote native path now validates the actual DSF header instead of trusting
  server metadata, retries transient ASIO driver-load failures while switching
  from PCM playback, and accepts DSF headers that report 8-bit stored DSD bytes.
- Added native DFF/DSDIFF playback for remote Orynivo Server streams. The client
  now parses the remote DFF chunk structure through HTTP byte-range reads and
  streams uncompressed DSD data directly to ASIO/cwASIO without downloading the
  complete file first; DST-compressed DFF remains unsupported and falls back to
  DSD-to-PCM.
- Fixed missing transport waveforms for remote Orynivo Server tracks when the
  server cannot generate waveform peaks for a format such as DFF. The client now
  falls back to locally analysing the authenticated stream URL with FFmpeg and
  caches the resulting compact peaks.
- Fixed track context menus staying stale after creating a new playlist from a
  track. Data-grid row playlist flyouts are now rebuilt immediately before they
  open, so the new playlist is available for the next add action without leaving
  the current list.
- Fixed table double-click actions requiring the pointer to land on visible text
  in some views. Track, search, radio, and podcast tables now resolve the
  clicked data-grid row before starting playback or opening the row target.
- Fixed the new content loading skeleton occasionally remaining over already
  loaded album detail views when a fast load completed before the deferred
  fade-in callback ran.
- Fixed the Dashboard album hover effect drawing only around the text area or
  getting clipped. Dashboard cards now keep the accent outline inside the card
  bounds and use a subtle surface change instead of scaling.

## [0.21.0] - 2026-07-04

### Added

- Added a waveform-style transport progress view that keeps the existing seek
  behaviour while showing local-file peak data with the active transport accent
  as the played overlay.
- Added cached waveform peak generation for local Orynivo tracks and remote
  Orynivo Server tracks. The server now exposes per-track waveform data through
  `GET /api/tracks/{id}/waveform` and stores generated peaks in its data
  directory.
- Added sanitized seek diagnostics in `logs/seek.log` for transport clicks,
  PCM decoder replacement, FFmpeg decoder startup, and Orynivo Server
  server-side seek transcodes so intermittent remote seek failures can be
  diagnosed without writing API keys or Plex tokens to the log.
- Added an album link to the now-playing transport metadata between title and
  artist. Local and Orynivo Server tracks open their normal album track detail
  view from that link.
- Added controlled web-browsing tools for the AI chat and external MCP clients:
  `search_web` (via a configurable SearXNG instance), `fetch_page` (readable
  plain text), and `fetch_page_as_markdown`. The MCP server — not the model —
  performs the network access through a new `WebBrowsingService` with a strong
  safety model: http/https only, private/loopback/link-local/reserved addresses
  are refused at connect time (SSRF protection, closing the DNS-rebinding
  window), redirects are limited and followed manually, responses are size- and
  timeout-capped, non-text content is refused (no arbitrary downloads), an
  optional domain allowlist is supported, and every request is logged to
  `%LOCALAPPDATA%\Orynivo\logs\web-browsing.log`. The trusted SearXNG endpoint
  is exempt from the private-network guard so a LAN/Docker instance works. The
  SearXNG URL and limits are configurable under Settings → Integration → MCP
  Server → Web browsing, and each web tool has its own enable/disable toggle
  (bringing the tool count to 23).
- Added a `get_current_time` tool that returns the current local and UTC date,
  time, day of week, and time-zone name. It is available both to the embedded AI
  chat and to external MCP clients, and has its own enable/disable toggle in
  Settings → Integration → MCP Server (bringing the tool count to 20).

### Fixed

- Fixed waveform transport seeking so pointer release is captured reliably, the
  full progress-control height is clickable for remote tracks without waveform
  data, and a cancelled seek during track changes no longer crashes the UI
  thread.
- Fixed the waveform progress indicator getting stuck at an old preview
  position when Avalonia missed the pointer-release path; capture loss, released
  buttons during move, and a short timeout now leave preview mode.
- Fixed remote waveform seeking visually jumping back to the old position while
  the server-side seek starts. The transport now keeps the clicked target
  position visible while the seek is pending, and PCM players discard the old
  decoder/output buffer immediately instead of playing stale buffered audio
  until the new decoder is ready.
- Fixed intermittent remote seeks falling back to the old playback position
  after the new FFmpeg/server-side decoder failed to start quickly. PCM players
  now close the old decoder and clear queued output immediately so the server
  releases the previous stream and playback never silently resumes from the old
  position.
- Fixed repeated remote seeks being blocked behind a hung earlier seek. New PCM
  seeks now cancel any still-starting replacement decoder, and the Orynivo
  Server seek transcode maps only the first audio stream so embedded artwork or
  other non-audio streams cannot stall the FLAC pipe startup.
- Fixed Back navigation after opening an artist from the now-playing transport
  and then drilling into an album. The history now captures local and remote
  artist-album drill-down states explicitly instead of inferring them from the
  visible title text, so Back returns to the artist's album list.
- Fixed the same Back path for remote Orynivo Server tracks by marking
  now-playing remote artist drill-downs as server album views before capturing
  history, preventing remote artist IDs from being restored through the local
  album query path.
- Removed tracks from SQLite, Lucene, and cached waveform data when a local or
  server library root is removed from configuration.
- Fixed embedded AI chat answers rendering Markdown syntax literally. Assistant
  messages now display common Markdown structure such as headings, lists, quotes,
  dividers, and fenced code blocks in the chat bubble.
- Fixed Markdown-rendered AI chat answers becoming invisible when the renderer
  could not resolve theme brushes inside the chat message template.
- Fixed the embedded AI chat showing an empty assistant bubble when a model
  streamed only whitespace before a tool call or returned no final answer after
  executing a tool. Empty leading tokens are ignored, and the chat now shows the
  tool result as a fallback when the model does not produce answer text.
- Removed the bundled default SearXNG URL from the web-browsing settings so new
  installations start with an empty endpoint and require the user to enter their
  own instance.
- Fixed Back navigation opening the wrong album after viewing a remote Orynivo
  Server album that was opened from the dashboard and then drilling into its
  artist. Such a remote album was captured as a local album-tracks state and
  restored through the local path, reopening a local album that merely shared
  the numeric id. Remote albums are now detected by their catalog source and
  restored through the remote path regardless of how they were opened.
- Fixed the manual lyrics search/download dialog not appearing while a remote
  Orynivo Server track is playing. The handler required a local database row
  (looked up by file path), which never matches a remote stream URL, so it
  returned before showing the dialog. It now seeds the search from the current
  track's metadata and stores the chosen lyrics on the remote server for remote
  tracks (and in the local database for local tracks).

## [0.20.5] - 2026-07-01

### Added

- Introduced a shared typography scale as application resources
  (`FontSizeMeta`, `FontSizeCaption`, `FontSizeBody`, `FontSizeBodyStrong`,
  `FontSizeSubtitle`, `FontSizeTitle`, `FontSizeTitleLarge`, `FontSizeHeadline`,
  `FontSizeDisplay`, `FontSizeDisplayLarge`, `FontSizeHero`). All views, the
  Settings view, and every dialog/window now reference these tokens instead of
  ad-hoc pixel sizes, giving consistent text sizing across sidebar, headers,
  section titles, tables, and meta text.

### Changed

- Album, artist, and podcast detail views now use larger, more prominent
  titles (`FontSizeDisplay`) for a more immersive header, while small meta text
  stays compact and muted.
- Missing album and artist artwork now shows an elegant typographic placeholder
  (one or two initials over a deterministic accent-tinted gradient) instead of a
  flat grey rectangle, via the new reusable `InitialsAvatar` control. It is used
  in the album detail header, album/artist artwork cards, and the artist-info
  image, and looks identical for local and remote (Orynivo Server) entities. The
  cover-search action now sits on a subtle scrim at the bottom of empty album
  covers so it stays readable over the gradient.
- The artist-info view now shows the artist's albums as a wrapped strip of
  clickable cover cards below the biography, so the view reads like a proper
  artist page instead of image-plus-text only. Album covers reuse the initials
  gradient placeholder when artwork is missing, and clicking a card opens that
  album's tracks. Works identically for local, remote-library, and now-playing
  remote artists.
- Modernized the Settings view: the left navigation items now carry section
  icons that recolour with selection/hover; genuine on/off options (DSD-to-PCM,
  equalizer, MCP server, AI chat, and the Appearance sidebar-visibility options)
  use pill toggle switches instead of checkboxes; interactive inputs share a
  consistent height; and the Output and MCP sections show small status badges
  for FFmpeg, Steinberg ASIO, cwASIO, and the MCP server. The MCP per-tool list
  keeps its checkboxes as a permission checklist.
- Each configured Orynivo Server and Plex server row in Settings now shows a
  live connection status badge (checking → available/unavailable). The
  reachability check runs asynchronously per server, so it never blocks opening
  Settings, and pending checks are cancelled when the list is rebuilt or Settings
  is closed.

### Fixed

- Fixed checkbox borders appearing near-black on the dark background: the app
  now aligns the built-in Fluent theme variant with the selected light/dark
  scheme (`ThemeManager` sets `Application.RequestedThemeVariant`), so any
  control still using default Fluent resources resolves theme-appropriate
  colours. The code-created radio-genre and podcast filter checkboxes were also
  given the themed checkbox style, and a duplicate `IsCheckedChanged`
  subscription on those checkboxes was removed.

## [0.20.4] - 2026-07-01

### Changed

- Refreshed the main Orynivo visual theme with a more neutral dark/light
  palette, a central accent brush, softer sidebar selection pills, larger
  transport controls, subtler table rows, and updated Dashboard album, calendar,
  and genre-stat cards.
- Refined the modernized shell with calmer dark-mode accent tones, pill-shaped
  header action buttons, bordered segmented view switches, rounded search/input
  fields, and cleaner DataGrid spacing without visible grid lines.
- Album and artist artwork cards now use theme-aware placeholder backgrounds,
  subtle borders, clipped covers, and a calmer asymmetric card shape.

### Fixed

- Library watcher rescans now honour cancellation while waiting between locked
  file retry attempts instead of blocking through fixed sleeps.
- The bottom transport bar's position-slider progress, slider thumb, and
  play/pause button are now tinted with an accent colour extracted from the
  current cover art (falling back to the app accent when no artwork is
  available), and the album artwork is slightly larger (72 px, rounded). The bar
  keeps its full-width, flush layout.
- Startup no longer keeps the splash screen open while checking or rebuilding a
  Lucene search index for large libraries. The main window opens after database
  preparation, and search-index checking/rebuild continues in the background
  with progress shown in the sidebar status line.
- Startup no longer validates every restored playback-queue path synchronously,
  so very large persisted queues on slow or sleeping drives no longer keep the
  splash screen open for minutes.
- The editable playback queue is now persisted in the SQLite library database
  instead of `settings.json`. Existing JSON queues are imported once and then
  cleared from settings, avoiding oversized settings files and expensive JSON
  saves for large queues.
- Double-clicking a track in the unfiltered top-level Tracks view now queues only
  that track instead of persisting the entire local library as the editable
  playback queue. Filtered, album, folder, playlist, and search-result playback
  still keeps the visible context as the queue.
- Startup now writes detailed timing diagnostics to
  `%LOCALAPPDATA%\Orynivo\logs\startup-timing-latest.log`, including FFmpeg,
  the initial database open, schema/migration substeps, main-window construction,
  and background search-index work. The detailed database hook is disabled
  before the main window opens so normal UI database reads do not flood the log
  or slow the app down.
- Sidebar accordion chevrons now share one alignment and colour across static
  and dynamically generated navigation groups, and Plex library children use
  the same nav text styling as the other submenu rows.
- Accent-filled controls such as the artwork/table switch, active A-Z index
  buttons, cover-search buttons, and the transport play button now use
  contrast-safe foreground colours instead of assuming white text on every
  accent background.

## [0.20.3] - 2026-06-30

### Added

- The Dashboard's "Recently added albums" strip now also includes albums from
  every configured remote Orynivo Server, merged with the local library and
  sorted by when each album was last added. Remote cards load their artwork from
  the server (cached locally) and navigate within the remote library. A new
  server endpoint `GET /api/albums/recent` backs this; older servers without it
  are simply skipped.
- Searching within a remote Orynivo Server Tracks view now shows the same
  three-section result (Tracks, Albums, Artists) as the local library search,
  backed by the server's `/api/search/full` endpoint. Result rows navigate
  within the remote library (album/artist links and double-click) and remote
  tracks play directly from the result list.

### Changed

- The three search-result sections (Tracks, Albums, Artists) now use the same
  accent-bordered card style (`#6C63FF`, `CornerRadius="0,24,0,24"`) as the
  library headline/intro card, for both local and remote search results.

### Fixed

- The Dashboard genre statistics (Top genres and the per-day calendar genres) now
  include remote Orynivo Server and Plex tracks. Their genre is captured at
  playback time in a new `play_history.genre` column, so genre stats no longer
  require a local library row. The calendar's daily total playback time already
  counted these tracks.
- The now-playing artist button is now enabled while a remote Orynivo Server
  track plays and opens that artist within the track's server library (instead of
  staying disabled to avoid opening an unrelated local artist).
- Clicking the artist link on a dashboard recent-album card no longer also opens
  the album, and dashboard/search remote albums now clear any leftover artist
  filter so they show all of their tracks instead of appearing empty.
- Opening a remote Orynivo Server view (e.g. an artist's albums) from a dashboard
  recent-album card now hides the dashboard, internet-radio, podcast, lyrics, and
  artist-info views, so the loaded remote content is actually shown instead of
  staying hidden behind the previous view (which made the album view appear
  empty).

## [0.20.2] - 2026-06-29

### Fixed

- The Linux Orynivo Server now reads and writes its editable configuration at
  `/etc/orynivo-server/appsettings.json`. It is layered on top of the bundled
  defaults at startup, and runtime changes (such as adding a library directory
  through the client) are persisted there instead of to the read-only,
  root-owned content-root copy under `/usr/lib/orynivo-server`, which previously
  failed with `UnauthorizedAccessException` and is also overwritten on package
  upgrades.

## [0.20.1] - 2026-06-29

### Fixed

- The Linux Orynivo Server package no longer crashes on startup
  (`UnauthorizedAccessException` / `SIGABRT`) when run as the `orynivo-server`
  systemd service. The service user has no writable home, so resolving the data
  directory via `$HOME/.local/share` failed. The data directory can now be
  overridden with the `ORYNIVO_DATA_DIR` environment variable; the systemd unit
  sets it to `/var/lib/orynivo-server` (created via `StateDirectory` and the
  package post-install) so the database, caches, and artwork are stored in a
  dedicated service-owned directory.
- Plex folder browsing now shows tracks whose files have no title tag. Such
  tracks are returned by Plex with an empty title (common for audio-book
  folders containing files like `001.mp3`) and were dropped by the empty-title
  filter, making those folders appear empty when expanded. The track now falls
  back to its source file name.

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
