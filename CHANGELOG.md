# Changelog

All notable changes to Orynivo are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

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
