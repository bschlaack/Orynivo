# Changelog

All notable changes to Orynivo are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

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
