# Orynivo Roadmap

Items 1-30 are complete and listed for reference only.

Each item is one commit and must follow the completion checklist in
`AGENTS.md`: build every affected project, run the three test projects, update
`CHANGELOG.md` (and `README.md`/nested `AGENTS.md` when behaviour changes), add
English XML docs, add every new visible string to all seven languages, and run
`scripts/verify-localization-parity.ps1` (plus
`scripts/verify-mcp-tool-parity.ps1` for MCP/AI changes).

Status values: `Todo`, `In progress`, `Blocked`, `Done`.

## Completed (1–14)

| # | Item | Notes |
|---|------|-------|
| 1 | Last.fm scrobbling | Core signing/rules/client plus the desktop queue; Settings under **Artist information** |
| 2 | Headphone crossfeed | `CrossfeedProcessor`, off by default, ASIO/WASAPI PCM only |
| 3 | Linux MPRIS and media keys | `MprisMediaTransport`, verified on CachyOS/KDE Plasma; remote covers come from the local `remote-artworks` cache |
| 4 | Streaming loudness normalization | Radio and podcast streams only |
| 5 | Remote transcoding with bitrate selection | `?format=opus\|aac&bitrate=` plus per-server streaming quality |
| 6 | Library Doctor duplicate resolution | `FindDuplicateGroups`, explicit confirmation before removal |
| 7 | Bulk editing in tables | Favourite/rating bulk API and the shared table action bar |
| 8 | Smart playlist "similar to track" | `SmartPlaylistCriteria` similarity reference |
| 9 | Mood/activity presets | `SimilarityFeatureService.RankPreset` and the activity mix menu |
| 10 | Harmonic mixing (Camelot) | `CamelotKey`, key estimation, `HarmonicOrdering` |
| 11 | Year-in-review export | `GetYearInReview`, PNG export |
| 12 | Karaoke fullscreen lyrics | `KaraokeWindow`, `LyricLineSelector` |
| 13 | Scheduled auto-backup with retention | `BackupRetention`, Settings > Library |
| 14 | Migrate drag-and-drop to `IDataTransfer` | Unblocked the Avalonia 11.3 line |

## 15. ★ Isolate the Core test data root — `Done`

- `Orynivo.Core.Tests/CoreTestDatabase` owns a unique temporary directory per
  test, opens the library database, builds paths inside the directory, and clears
  only that database's SQLite pool on disposal.
- `ArtistAttributionTests` no longer shares one library file or deletes it between
  tests; it was the only class still using `AudioDatabase.OpenDefault()` and
  `AppPaths.DataRoot`.
- The remaining database tests keep their own directories but no longer call the
  process-wide `SqliteConnection.ClearAllPools()`, which could close pooled
  connections of tests running in parallel.
- `TestEnvironment`'s module initializer remains the safety net for the whole run.
- Verified with ten consecutive green suite runs (337 tests).

## 16. ★ Add `scripts/verify-all.ps1` — `Done`

- Runs the managed builds for `Orynivo.Core`, `Orynivo.Server`, and `Orynivo`
  with `--warnaserror`, all three test projects, and both parity scripts.
- Stops at the first failure, prints the failing output tail, then a compact
  summary naming the steps that were skipped, and exits non-zero.
- Supports `-Configuration Debug|Release`, `-SkipBuild`, and `-SkipTests`; it
  deliberately does not force the native-bridge properties the Windows workflow
  sets, so a local build keeps its own defaults.
- Not wired into the workflows; CI runs these steps itself. Both the success and
  the failure path were verified.

## 17. ★ Expose the new features to MCP and AI Chat — `Done`

None of the 32 tools knew the year-in-review summary, the estimated musical key,
bulk favourite/rating updates, or similarity smart playlists. Keep `McpTools`,
`AiToolDefinitions`, `AiToolExecutor`, and the Settings tool checklist in exact
parity (the parity script derives its expectation from `McpTools`, so it covers
new tools automatically), keep the existing redaction rules, and update the tool
count wherever it is stated (`AGENTS.md`, `README.md`, Settings `UniformGrid`
rows, wiki `MCP-Tool-Reference.md`).

Steps:

- 17a Read-only tools — `Done`: `get_year_in_review` (listened hours, active
  days, monthly breakdown, leading genres/albums/artists) and `get_track_key`
  (Camelot label for a local path or an `orynivo://` reference). The tool count is
  34. This also uncovered and fixed a real defect: `CamelotKey` had landed on the
  rating-mutation DTO instead of `OrynivoTrackInfo`, so remote rows never showed
  the key. Two Core tests guard the DTO.
- 17b Mutating tools — `Done`: `set_tracks_favorite`, `set_tracks_rating`, and
  `create_similar_playlist`, backed by three new `McpPlayerBridge` delegates and
  path-based implementations. `SetTracksFavoriteByPathsAsync`/
  `SetTracksRatingByPathsAsync` split local paths from `orynivo://` references and
  reuse the transactional bulk update and the per-track server API;
  `CreateSimilarPlaylistByPathAsync` is now shared with the track context menu.
  The tool count is 37, and two `Orynivo.Tests` cases validate every AI tool
  schema (types, descriptions, and required names).

## 18. Pick the similarity reference in the smart-playlist editor — `Done`

The reference can currently only be set from the track context menu.

Steps:

- 18a Server-side similarity resolve — `Done`: `/api/playlists/{id}/resolve` and
  `/api/playlists/resolve-count` now go through the new public
  `Orynivo.Server.Services.SmartPlaylistResolver`, which supplies the cached
  similarity feature vectors whenever the criteria carries a reference. Before
  this a similarity playlist stored on a server resolved to an empty list. A
  reference pointing at another library (`server:<id>`) intentionally still
  resolves to nothing, because vectors are provider-local. Three
  `Orynivo.Server.Tests` cases cover the helper, the empty plain-resolve result,
  and criteria without a reference.
- 18b Reference picker in the editor — `Done`: the similarity panel gained
  **Choose reference track**, which opens `ReferenceTrackPickerDialog` (search
  over the local index plus every configured Orynivo Server) and applies the
  selection together with the minimum score. The dialog never touches the database
  or the network itself; `MainWindow` supplies the search. The pure
  `SmartPlaylistCriteriaEditing.ResolveSimilarityReference` gained a
  picked-reference override (explicit removal still wins), covered by three more
  `Orynivo.Tests` cases.

## 19. Offer activity presets in the Infinite Mix profile — `Done`

- `InfiniteMixDialog` gained **Focus**, **Workout**, and **Wind down** presets next
  to the mood selector. They pre-fill the mood, discovery level, history period,
  and weighting through the pure `Orynivo.InfiniteMixPresets.Apply`, which
  preserves the server selection, genre filters, feedback, and exclusions and
  never modifies the profile it is based on. Every field stays editable
  afterwards. 6 `Orynivo.Tests` cases.
- Deliberate boundary: the presets do **not** add descriptor scoring to Infinite
  Mix. `SimilarityFeatureService.RankPreset` (item 9) remains the
  descriptor-based activity mix, because the genre-cloud candidate payload carries
  no acoustic descriptors; adding them would change the Core/Server payload
  contract. Revisit only together with that change.

## 20. Report "now playing" and love tracks on Last.fm — `Done`

- The "now playing" notification already existed; it now builds its metadata
  through a single `BuildLastFmTrack` helper and skips untagged items instead of
  sending a request Last.fm would reject.
- Added `LastFmClient.SetTrackLovedAsync` (`track.love`/`track.unlove`, artist and
  track only) and `LastFmScrobblingService.SetTrackLoved`, wired to the transport
  favourite button for both local and Orynivo Server tracks. The call is best
  effort, never blocks playback, and is not queued while offline.
- The session key and API secret stay in `ApplicationCredentialStore` only.
- 6 `Orynivo.Core.Tests` cases verify the signed love/unlove request, the error
  response, and that an untagged item sends nothing.

## 21. Export the year in review as PDF — `Done`

- Added the pure `Orynivo.Controls.YearInReviewLayout` content model (title,
  headline, monthly bar ratios, leading sections, album labels), consumed by the
  PDF export so the on-screen card and the PDF cannot drift apart.
- Added `YearInReviewPdfExporter`, which draws that model into a bounded
  single-page A4 PDF through `SKDocument.CreatePdf`; SkiaSharp comes from
  Avalonia.Skia's pinned 2.88.9 reference, so the desktop project deliberately has
  no separate SkiaSharp package reference. The dialog gained **Save as PDF**.
- 6 `Orynivo.Tests` cases cover the layout model and 2 more verify that a real
  PDF document is written, including the empty-summary case.

## 22. Word-level karaoke highlighting — `Done`

- `LyricsService.ParseLrc` extracts `<mm:ss.xx>` word timestamps into
  `TimedLyricLine.Words` and strips the markers from the line text, which also
  fixes markers leaking into the displayed lyrics.
- `KaraokeWindow` renders an enhanced line as one `Run` per word, emphasizes the
  active word, keeps sung words in the accent colour, and repaints only the active
  slot when the word changes so the surrounding transitions keep animating. Plain
  synchronized lines keep the line-level highlight; `LyricLineSelector` serves as
  both the line and word selector.
- 10 `Orynivo.Core.Tests` cases cover the parser (plain, enhanced, mixed, ordering,
  markers-only, missing timestamps) and the word-level selection.

## 23. Bulk genre editing — `Done`

**Decision**: library-only overrides, not media tags. The project's established
rule is that media files are never rewritten (`track_title_overrides`,
`track_metadata_overrides`, "corrections affect only the library"), so genre
editing follows the same pattern and needs no confirmation or backup machinery.

Steps:

- 23a Local tracks — `Done`: `track_genre_overrides` (keyed by stable track path,
  so `cue://` and `mka://chapter/` tracks are covered) is reapplied by every
  `AudioDatabase.Upsert`; `SetTrackGenres` updates several tracks in one
  transaction and writes the override at the same time; an empty value removes it
  so the next scan restores the embedded genre. The bulk action bar gained a genre
  field and updates the selected rows in place. 5 `Orynivo.Core.Tests` cases cover
  the rescan, the clear path, multiple tracks, duplicates, and the empty
  selection.
- 23b Server-owned tracks — `Done`: `PUT /api/tracks/{id}/genre` stores the same
  library-only override through `AudioDatabase.SetTrackGenres` and refreshes the
  track's Lucene document; `OrynivoServerClient.UpdateTrackGenreAsync` sends it
  (an explicit empty value clears the override), and the bulk action updates
  remote rows in place and reports per-selection failures. 3
  `Orynivo.Core.Tests` cases cover the request path, the clear value, and a
  rejected response.

## 24. Download podcast episodes for offline playback — `Done`

- `PodcastDownloadCache` owns the pure decisions: a stable hashed cache file name
  per podcast and episode key, and least-recently-used eviction that always keeps
  the newest download.
- `PodcastDownloadService` downloads into the per-user `podcast-downloads` cache,
  deletes single episodes, marks a played download as used, and enforces the limit.
- Episode rows gained a **Download episode** / **Delete download** context menu and
  a download marker in the status column; playback prefers the cached file;
  Settings > Library sets `AppSettings.PodcastDownloadLimitMb`.
- 12 `Orynivo.Core.Tests` cases cover the file naming and the eviction selection.
- Not included: pinning individual episodes so eviction can never remove them, and
  background prefetch of new episodes.

## 25. Cloud backup targets and a server-side schedule — `Done`

Steps:

- 25a Server-side schedule and shared naming — `Done`:
  `Orynivo.Server.Services.BackupScheduleService` writes a versioned ZIP at most
  once per `IntervalDays` into `Orynivo:BackupSchedule:Directory` (default: a
  `backups` folder below the server data directory) and prunes through
  `BackupRetention.SelectObsolete`. The last run comes from the newest archive, so
  no extra state is persisted, and the section holds no credentials. Automatic
  archive naming moved into the shared `Orynivo.Library.BackupNaming`, which the
  desktop now uses as well. 8 `Orynivo.Core.Tests` cases cover the naming.
- 25b WebDAV upload target — `Done`: `Orynivo.Library.BackupTargets` validates and
  builds the target URL (plain `http`/`https`, no embedded credentials) and
  `Orynivo.Library.BackupUploader` performs a bounded `PUT` with optional Basic
  auth that never leaks credentials into a URL, a log, or an error message.
  `ApplicationCredentialSnapshot.BackupTargetPassword` carries the password, so it
  never reaches `settings.json`; Settings exposes enable, URL, sub-folder, user
  name, and password, and the scheduled and **Back up now** paths share
  `MainWindow.TryUploadBackupAsync`. 18 `Orynivo.Core.Tests` cases cover the URL
  rules and the upload request. S3 remains a deliberate follow-up.

## 26. Reduce motion and keyboard navigation — `Done`

**Design**

- Add an `AppSettings.ReduceMotion` toggle that disables the Genre Cloud,
  Dashboard stage, and karaoke animations.
- Add keyboard navigation and accessible names for the artwork grids and
  transport controls.

**Tests**: the animation decision as a pure helper.

**Commit**: `feat(a11y): add reduce motion and keyboard navigation`

## 27. Resume a track across devices — `Done`

**Design**

- Store the last position per track on the server (profile-scoped) and offer
  **Resume on this device** when a track starts elsewhere.
- Reuse the existing profile and playback-history infrastructure; never persist
  authenticated stream URLs.

**Tests**: the resume decision (pure).

**Commit**: `feat(playback): resume a track across devices`

## 28. Record the dependency migration plan — `Done`

**Design**

- Document when and how to move to Avalonia 12/.NET 10 LTS, what would unblock
  `Avalonia.Controls.DataGrid` beyond 11.3.13, and how the SkiaSharp 2.88.9 pin
  (Avalonia.Skia) is revisited.
- Keep it as a decision record next to the Dependabot rules in `AGENTS.md`.

**Tests**: none; documentation only.

**Commit**: `docs: record the dependency migration plan`

## 29. Correct the toolchain target to .NET 10 LTS - `Done`

**Design**

- .NET 9 is in security-only maintenance and reaches end of support on
  10 November 2026, the same day as the currently used .NET 8, so the record's
  original ".NET 9" destination was a dead end. The target is **.NET 10 LTS**
  (supported until 14 November 2028).
- `Avalonia.Controls.DataGrid` is in upstream maintenance mode and has no release
  beyond 11.3.13, so the DataGrid pin cannot be resolved by waiting. The record
  now points at evaluating a successor control (`TableView`/`TreeDataGrid`).
- Added the toolchain support-window table and the SkiaSharp fold-in step to the
  .NET 10/Avalonia 12 migration.

**Tests**: none; documentation only.

**Commit**: `docs: correct the dependency migration target to .NET 10 LTS`

## 30. Migrate the solution to .NET 10 LTS - `Done`

**Design**

- All six projects target `net10.0` / `net10.0-windows10.0.19041.0`;
  `global.json` pins SDK `10.0.100` with `rollForward: latestFeature`; every
  workflow pins `dotnet-version: 10.0.x`.
- `Microsoft.Data.Sqlite` 10.0.12, `Microsoft.AspNetCore.TestHost` 10.0.12, and
  `Microsoft.NET.Test.Sdk` 18.10.1 match the new toolchain.
- Avalonia stays on 11.3 for now; the Avalonia 12 migration is a separate step
  recorded in `DEPENDENCY-MIGRATION.md`.
- Drive-by license fix: `Orynivo.csproj` copies the Lucene `NOTICE.txt` from the
  `4.8.0-beta00018` directory it actually references.

**Tests**: the existing 570 tests (Core 427, Desktop 105, Server 38) run on
`net10.0`; `scripts/verify-all.ps1` is green in Debug and Release.

**Commit**: `chore(dotnet): migrate the solution to .NET 10 LTS`
