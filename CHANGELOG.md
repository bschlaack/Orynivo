# Changelog

All notable changes to Orynivo are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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

### Fixed

- Podcast filters no longer require a podcast title before returning results.
- Radio genres no longer show misleading counts produced by summing
  overlapping normalized tags.
- Radio genre filters no longer return only matches from the initial
  100-station result page.
- Radio Browser tag queries now use compatible lowercase search values.
- Cached podcast categories without Apple genre IDs are refreshed
  automatically.

## [0.3.0] - 2026-06-14

- Baseline release before the podcast and catalog-filter additions documented
  above.
