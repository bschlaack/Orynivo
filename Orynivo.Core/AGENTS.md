# Orynivo.Core Instructions

This file applies to `Orynivo.Core/` and supplements `../AGENTS.md`.

## Completion

- Follow the root mandatory completion checklist.
- Build with `dotnet build Orynivo.Core/Orynivo.Core.csproj` and also build each
  affected consumer when a public contract changes.
- Public/internal C# APIs require complete English XML documentation.

## Core Invariants

- Keep the project cross-platform `net8.0`; do not introduce Avalonia, Windows,
  DPAPI, WASAPI, ASIO, or other platform-specific dependencies.
- Put shared library scanning, SQLite persistence, search, streaming models and
  clients, FFmpeg primitives, and web-fetching behavior here.
- Preserve SQLite migrations, stable IDs, WAL behavior, CUE virtual-path
  identity, user favorites, artwork caches, ReplayGain data, and `added_at`.
- `LibraryBackupService` supports both the process data root and an explicit
  server data root. Backups remain versioned, exclude audio and credentials,
  use a consistent SQLite snapshot, validate staged imports, and roll back
  partial replacements before rebuilding the search index.
- Chaptered MKA containers use FFprobe-derived stable `mka://chapter/` virtual
  paths and the existing segment columns; unchaptered MKA files remain ordinary
  tracks. Each probe uses bounded analysis and a 30-second timeout.
- Library-only title corrections live in `track_title_overrides` and must be
  applied by every `AudioDatabase.Upsert`; never write these corrections back to
  the source media.
- Scanner, watcher, and reconciliation writes must keep SQLite and Lucene in
  sync and use the shared scanner gate.
- Multi-root owner scans must hold a watcher-operation suspension for their
  complete duration. This prevents incremental updates or periodic
  reconciliations from taking the shared scanner gate between roots and making
  the reported foreground scan appear stalled on its next root.
- `LibraryScanner.RefreshMetadataAsync` is the explicit maintenance path that
  bypasses timestamp skipping and re-reads every supported file. It shares the
  normal scanner gate and reconciliation/indexing pipeline, preserves
  library-only overrides, and never changes source media.
- A failed TagLib read must never upsert the file-system-only fallback record.
  Full, forced, watcher, and reconciliation scans retry boundedly, count a final
  failure, and preserve the existing database metadata unchanged.
- When an upsert changes a track's album identity inside the same physical album
  directory, carry the previous album's artwork and favorite flag to the target.
  Existing target artwork wins; favorites are combined rather than cleared.
- Keep compact query models compact; do not add artwork BLOBs, lyrics, or full
  records to list/facet/folder queries.
- Track scans preserve personal ratings, cached MusicBrainz rating/vote data,
  and a client-resolved recording MBID when the media tag has no recording ID.
  `MusicBrainzRatingService` prefers a valid recording MBID and permits fallback
  matching only for one exact artist/title candidate compatible with duration.
  Community ratings use MusicBrainz's zero-to-five scale and remain separate
  from the personal zero-to-five integer rating. Compact track list/streaming
  DTOs carry the rating fetch timestamp so clients can enforce cache freshness.
  Metadata fallback returns a resolved MBID and follows it with a direct
  recording lookup. Do not cache community ratings from batch/search responses:
  MusicBrainz does not reliably populate their rating field even when
  `inc=ratings` is supplied.
  Direct lookups request `ratings+genres+tags`; keep genres with positive counts,
  tags with at least two positive votes, and persist both as separate bounded
  JSON arrays. `MusicBrainzGenreMetadata.Combine` is the shared effective-genre
  composition used by facets and indexing without altering embedded metadata.
  A completed conservative lookup that cannot resolve one recording is
  persisted through `SetTrackMusicBrainzLookupAttempt`; scanner upserts must
  preserve that timestamp so clients can apply a longer retry cooldown.
- Album catalog queries, recent-album queries, detail lookup, and Dashboard
  album totals expose only albums referenced by at least one indexed track.
  Artist catalog rows and artist totals likewise require a track-backed album;
  never surface orphaned normalization rows as usable library entities.
- `LibraryMetadataRepairService` groups tracks by their immediate physical
  directory, detects inconsistent album metadata, and uses the MusicBrainz fuzzy
  CD-TOC endpoint only when every candidate track has a duration. Confirmed
  corrections are persisted in `track_metadata_overrides` and reapplied by every
  `AudioDatabase.Upsert`; never write them into media files implicitly.
  MusicBrainz matching accepts optional user-edited release and artist queries;
  text-search results must still be fetched with their recording lists and
  scored against available track durations plus title similarity before they
  are offered. Text search must remain usable when local or MusicBrainz
  durations are missing, and fuzzy TOC lookup includes all medium formats.
- Remote dashboard totals use `OrynivoServerClient.GetLibrarySummaryAsync` and
  the server's aggregate `/api/library/summary` response; do not replace this
  fast path with complete track or album payloads.
- Dashboard recommendations use compact album-level genre/BPM candidates from
  `AudioDatabase.GetRecommendationAlbums` and the matching server endpoint.
  Keep this payload free of track rows, artwork bytes, and playback credentials.
- `GenreCloudService` owns the stable hierarchical genre taxonomy, tag
  normalization, count aggregation, breadcrumbs, and bounded provider-local
  candidate selection. Its curated data lives in the embedded
  `Library/GenreTaxonomy.json`; definitions may be top-level and have multiple
  parents, so traversal must be cycle-safe and node counts must deduplicate a
  track even when several graph paths reach the same ancestor. Every node also
  carries a distinct provider-local album count derived from the compact facet
  row's optional `AlbumId`; cross-provider merging sums those counts. Controlled
  compound-name matching resolves recognized descriptive tags without guessing
  from arbitrary substrings. Unmapped tags retain dynamic `unmapped:` keys and
  appear by their actual names beneath `more-genres`; never collapse them into
  an Other bucket. The desktop merges snapshots across providers.
- `AudioDatabase.GetListeningTrend` supports up to 366 equal chronological
  buckets so the client can request daily Dashboard points without materializing
  playback-history rows.
- `ArtistNameNormalizer.CreateComparisonKey` is the shared identity key for
  comparing artist names across local and Orynivo Server catalogs.
- `ArtistProfileService` accepts `de`, `en`, `fr`, and `es` profile language
  codes; supported UI languages must not silently fall back to another language.
- Local artist browsing is album-artist-centered. The scanner records whether
  `ALBUMARTIST` was missing, imports supported compilation flags, and
  `AudioDatabase.ReconcileAlbumArtists` resolves the complete album before the
  Artists view is exposed. Explicit consistent album artists win; otherwise
  compilation or differing inferred track artists resolve to `Various Artists`.
  Primary track artists stay attached to their tracks. Featured suffixes remain
  governed by `ArtistNameNormalizer`. Embedded MusicBrainz artist IDs take
  precedence over name comparison when resolving a local artist identity.
- `FanartTvArtistImageService` uses a known MusicBrainz artist ID or an
  unambiguous exact MusicBrainz name match, accepts only HTTPS `artistthumb`
  URLs, bounds image downloads, and never includes the Fanart.tv API key in
  diagnostics. `ArtistProfileService` prefers that image only when automatic
  image refresh is allowed; manual artist images must remain untouched.
  `ArtistImageSearchService` owns writes and explicit deletion of the
  provider-local `artist-images/<id>.*` cache variants.
- Web page fetching must retain SSRF protection, connect-time address checks,
  redirect and size limits, text-only responses, timeouts, and audit logging.
- Streaming URL builders may carry credentials for immediate playback, but such
  URLs must never be persisted, logged, documented, or returned to a model.
- Shared release-update models verify the ECDSA P-256 signed manifest before an
  asset is selected and verify its SHA-256 digest after download. Consumers must
  never bypass either verification or accept an unsigned fallback.
- `FfmpegLocator` searches platform-specific installation directories in
  addition to the inherited `PATH`. Windows and macOS may download matching
  FFmpeg/FFprobe binaries into the per-user cache; Linux remains
  system-package-managed. Downloaded macOS tools must be marked executable.
- `Orynivo.Server` has no `InternalsVisibleTo` grant; server-facing Core APIs must
  be deliberately public.

Consult the detailed database, scanner, search, streaming, audio, and web rules
in the root `AGENTS.md` before modifying those areas.
