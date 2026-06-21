# Changelog

All notable changes to Orynivo are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Added a themed **Save as playlist** action to the album-detail header. It can
  append every currently displayed album track to an existing playlist or
  create a new playlist, respecting the current album-track scope. The album
  card now spans the available content width, with the favorite action directly
  before the album title and the cover/playlist actions aligned side by side.

### Fixed

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
