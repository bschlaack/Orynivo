# Discovery and Remote Control Implementation Plan

This checklist tracks the three agreed feature phases. Update it after every
material implementation step so work can resume safely across sessions.

## 1. Library Doctor

- [x] Audit and reuse the existing metadata-review and MusicBrainz repair flow.
- [x] Define shared diagnostic models, severity levels, and safe-fix capability.
- [x] Extend the existing compact folder analysis with missing ReplayGain and
  MusicBrainz recording-ID findings and surface their counts in Settings.
- [x] Show each folder's highest severity and aggregate error/warning/info
  counters in the existing metadata-review table.
- [x] Detect missing metadata, track numbers, artwork, artist images,
  ReplayGain, MusicBrainz IDs, unreadable files, inconsistent artist names,
  incomplete albums, and likely duplicates.
  - [x] Detect provably incomplete discs from declared track totals without
    guessing when total metadata is absent.
  - [x] Detect missing locally assigned album artwork through compact database
    joins without reopening audio files.
  - [x] Detect missing or stale album-artist image paths.
  - [x] Detect missing and unreadable physical source files while checking a
    shared CUE/chapter source only once per folder.
  - [x] Detect conservative artist-name spelling variants across the library
    without automatically merging artist records.
- [x] Distinguish exact duplicates from alternate releases, masterings, and
  formats; never delete or rewrite audio automatically.
  - [x] Classify cross-path AcoustID matches with identical file size as likely
    file duplicates and differing sizes as alternate-file/edition candidates.
  - [x] Confirm same-size candidates with a cancellable streaming SHA-256 pass;
    hash failures remain likely candidates and never trigger deletion.
- [x] Add summary counters and filters to the existing review screen.
  - [x] Add an in-memory severity filter that does not rerun database analysis.
  - [x] Add an in-memory finding-type filter for targeted review.
- [x] Add a before/after preview and explicit per-item or selected-item actions.
- [x] Support local and Orynivo Server findings without exposing credentials.
  Remote findings are intentionally read-only until a dedicated confirmed
  correction endpoint is implemented.
- [x] Run analysis outside the UI thread with cancellation and bounded memory.
- [x] Add unit tests, localization, README/wiki documentation, AGENTS notes,
  and an Unreleased changelog entry.
- [x] Verify desktop, Core, and Server builds and affected tests.

## 2. Similarity and Mood Search

- [x] Define a versioned feature-vector contract usable locally and remotely.
- [x] Combine existing genre, BPM, rating, favourite, and listening-history
  data before introducing expensive audio analysis.
- [x] Add optional progressively cached energy, brightness, and dynamics audio
  descriptors for mood and acoustic similarity.
- [x] Implement nearest-neighbour ranking with artist/album diversity controls.
- [x] Add “More like this” and mood-based playback actions.
  - [x] Add a cross-library “More like this” track action.
  - [x] Add explicit calm, balanced, and energetic mood playback actions.
- [x] Integrate results with queue and Infinite Mix without blocking playback.
  - [x] Build and start a resolved similarity queue without blocking vector loading.
  - [x] Let Infinite Mix continue from the active similarity seed/profile.
- [x] Add matching authenticated server endpoints and compatibility fallback.
- [x] Add tests, localization, documentation, and performance measurements.

## 3. Mobile Web Remote

- [x] Define a minimal authenticated remote-control API and live-state channel.
- [x] Reuse the MCP/network security model while keeping permissions separate.
- [x] Implement responsive now-playing, transport, seek, volume, queue, search,
  output selection, favourites, and library browsing.
  - [x] Implement responsive now-playing, transport, seek, volume, and direct
    queue selection.
  - [x] Add cross-library track search with opaque play-now, play-next, and
    append actions.
  - [x] Add current-track favourites, output-profile selection, and complete
    queue editing.
  - [x] Add album and artist browsing.
- [x] Add artwork delivery with bounded thumbnails and cache headers.
- [x] Add reconnecting live updates without polling the complete library.
- [x] Provide opt-in LAN binding, token rotation, and HTTPS/VPN guidance.
- [ ] Validate phone/tablet layouts and keyboard/screen-reader accessibility.
- [ ] Add tests, documentation, packaging checks, and a security review.

## Current position

- [x] Three-phase scope and order agreed.
- [x] Persistent implementation checklist created.
- [x] Phase 1 implementation started.
- [x] Phase 1 Library Doctor completed and verified.
- [x] Phase 2 similarity contract, ranking, server transport, and first
  cross-library “More like this” playback action implemented and verified.
- [x] Phase 2 Similarity and Mood Search completed and verified, including
  progressive optional acoustic descriptors and Infinite Mix continuation.
- [x] Phase 3 functional surface completed with an independently authenticated
  mobile endpoint, reconnecting live state, transport and queue controls,
  cross-library search, output/favourite actions, and artist/album browsing.
