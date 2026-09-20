# Orynivo Roadmap

Items 1-37 are complete and listed for reference only.

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

## 31. Migrate the desktop to Avalonia 12 - `Done`

**Design**

- Avalonia 11.3.22 to 12.1.2 in one commit, including
  `Avalonia.Controls.DataGrid` (which does ship 12.1.2; the earlier "no release
  beyond 11.3.13" note was wrong) and SkiaSharp 2.88.9 to 3.119.4.
- The unused Debug-only `Avalonia.Diagnostics` reference was dropped; it has no
  12.x release and the app never called `AttachDevTools`.
- SkiaSharp 3 text and sampling APIs moved to `SKFont`/`SKSamplingOptions`; the
  Avalonia 12 deprecations that fail `--warnaserror` were fixed
  (`Watermark`/`SystemDecorations`/clipboard/drag-drop).
- Bindings stay on reflection mode for now; converting each view to compiled
  bindings with an explicit `x:DataType` is recorded in
  `DEPENDENCY-MIGRATION.md` with its measured scope.

**Tests**: 570 tests green; clean Debug and Release builds with `--warnaserror`
report 0 errors and 0 warnings. A runtime pass is still outstanding and listed in
`DEPENDENCY-MIGRATION.md`.

**Commit**: `chore(avalonia): migrate the desktop to Avalonia 12`

## 32. Adopt Avalonia 12 compiled bindings - `Done`

**Design**

- Every template and item-binding scope carries an explicit `x:DataType`;
  `AvaloniaUseCompiledBindingsByDefault=false` is gone.
- The item type goes on the column or template, never on the `DataGrid` itself.
- Ten view models moved to top-level types because XAML cannot name a nested type;
  the scopes that bind the still-nested `ContentRow` keep `{ReflectionBinding}`
  until that type is extracted (recorded as a follow-up in
  `DEPENDENCY-MIGRATION.md`).

**Tests**: 570 tests green; clean Debug and Release builds with `--warnaserror`
report 0 errors and 0 warnings.

**Commit**: `refactor(xaml): adopt Avalonia 12 compiled bindings`

## 33. Extract `ContentRow` and finish compiled bindings - `Done`

**Design**

- `ContentRow` (287 lines, 77 members) and `LogicalAlbumPart` moved out of
  `MainWindow.xaml.cs` into top-level `internal` types with full XML docs.
- `LocalSourceKey` and `GetServerSourceKey` became `internal static`.
- The three row scopes carry `x:DataType="local:ContentRow"`; no
  `{ReflectionBinding}` remains in the views.

**Tests**: 570 tests green; clean Debug and Release builds with `--warnaserror`
report 0 errors and 0 warnings.

**Commit**: `refactor(ui): extract the ContentRow row model`

## 34. Fix DSD-to-PCM playback over exclusive WASAPI - `Done`

**Design**

- The WASAPI format chooser ordered candidates from `Math.Max(SourceSampleRate,
  OutputSampleRate)`, so a DSD source preferred the device's highest supported rate
  (384 kHz, a fractional division of the DSD rate). DSD now prefers an exact division
  of its rate that does not exceed the conversion hint, matching the ASIO path, and the
  WASAPI probe reports the same 176400 Hz hint as the FFmpeg player.
- The probe order is extracted into the pure `WasapiAudioPlayer.OrderCandidateSampleRates`.

**Tests**: seven new cases in `Orynivo.Tests` cover the DSD division order, the
fractional-rate ordering, DSD128, the PCM ordering, and uniqueness.

**Commit**: `fix(playback): keep DSD-to-PCM on an exact rate division`

## 35. Add a maximum output sample rate setting - `Done`

**Design**

- `AppSettings.MaxOutputSampleRateHz` (zero = automatic) caps the PCM output rate for
  exclusive WASAPI and ASIO/cwASIO, so a driver that advertises an unusable maximum rate
  can be kept out of reach.
- The WASAPI cap only reorders the candidate rates; playback still falls back to a rate
  above the cap when the device supports nothing at or below it.
- Settings > Playback offers Automatic plus the standard rates.

**Tests**: three more cases in `WasapiSampleRateSelectionTests` cover the cap, the
fallback when the cap excludes every rate, and the DSD preference under a cap.

**Commit**: `feat(playback): add a maximum output sample rate setting`

## 36. Fix the Linux and macOS desktop builds - `Done`

**Design**

- The new maximum-output-rate parameter reached the Windows `WasapiAudioPlayer` but not
  the `Compatibility/Linux` replacement that the non-Windows targets compile, so Linux and
  macOS failed with `CS1501: No overload for CreateAsync takes 6 arguments` while Windows
  stayed green. The compatibility player now accepts and honours the cap.
- `scripts/verify-all.ps1` gained a non-Windows desktop compile (`-p:OS=Unix`), so this
  class of platform-specific call-site mismatch is caught locally instead of only by CI.

**Tests**: the new verify step fails on the reverted change and passes again once the
parameter is restored.

**Commit**: `fix(build): compile the non-Windows desktop in verify-all`

## 37. Music visualizer with a Milkdrop-style preset engine - `Done`

**Goal**

A fullscreen visualizer window that reacts to the playing music, driven by text presets in
the spirit of Winamp's Milkdrop and AVS: per-frame and per-pixel expressions over the audio
spectrum, a feedback warp of the previous frame, blur passes, waveform and custom-shape
overlays, and a composite stage.

**Design**

- **Audio tap.** Players publish their processed PCM (after volume, ReplayGain, EQ, and
  crossfeed, so the picture matches what is audible) into the lock-free
  `PcmVisualizationTap`. The audio thread only copies and never waits: when the visualizer
  falls behind, the oldest samples are dropped. Native ASIO/cwASIO DSD produces no PCM, so
  those sources need a separate lightweight analysis or simply leave the picture still.
- **Analysis.** `AudioSpectrumAnalyzer` applies a Hann window, the real-input `Fft`, a
  logarithmic 64-band grouping, and a fast-attack/slow-decay smoothing, and derives
  `bass`, `mid`, `treble`, and `volume` in the range zero to one. This is the entire
  preset-visible audio input.
- **Preset language.** A tokenizer, Pratt parser, and compiler for the Milkdrop expression
  subset: arithmetic, comparisons, `? :`, `if`, the usual `sin/cos/sqrt/pow/abs/floor/...`
  functions, per-frame and per-pixel built-ins, and user variables. Statements are
  assignments; loops, arrays, and shaders are deliberately out of scope. Presets are INI
  text with `per_frame_init`, `per_frame`, `per_pixel`, `wave`, `shape`, and `comp` keys;
  unknown keys are ignored so third-party Milkdrop presets degrade gracefully.
- **Render pipeline.** The stages run into a low-resolution framebuffer (roughly 480 x 270)
  that is scaled up with bilinear filtering, which is what gives the classic soft feedback
  look and keeps CPU rendering viable: the per-pixel displacement map warps the previous
  frame, blur passes soften it, the waveform and custom shapes are drawn on top, and the
  composite stage blends them with the preset's alpha. Scanline evaluation runs in parallel.
- **Presentation.** `VisualizerWindow` follows the karaoke window: fullscreen, closes on
  Escape or a click, preset switching with the keyboard and a click, a bounded frame-rate
  cap, and `AppSettings.ReduceMotion` honoured by falling back to a static spectrum.
- **Licensing.** `projectM` and the original Milkdrop are GPL/other-licensed, so no third
  party visualizer code or preset bundle is linked; the engine and every shipped preset are
  written here. Presets are user files, like equalizer profiles.

**Phases**

- 37a Audio analysis foundation - `Done`: `Fft`, `AudioSpectrumAnalyzer`, and
  `PcmVisualizationTap` in `Orynivo.Core/Audio`, covered by 21 tests (sine frequency
  detection, DC concentration, silence, band separation, decay and reset, ring-buffer
  overflow, oversized blocks, and clearing).
- 37b Preset expression language - `Done`: `PresetLexer`, `PresetCompiler` (a precedence
  parser that emits `System.Linq.Expressions` trees and JIT-compiles them into an
  `Action<float[]>`), and the public `PresetProgram`/`PresetExpressionException` surface.
  Supported: assignments, arithmetic, C-like remainder, comparisons, logical operators,
  the ternary operator, `if(...)`, the usual math functions, `pi`, `rand(n)`, `//` comments,
  and semicolon-separated statements. Unknown functions and malformed input report a
  position. Covered by 22 tests.
- 37c Render pipeline - `Done`: `PixelBuffer` (float RGBA, bilinear sampling, box blur,
  BGRA export), `VisualizerPreset` (INI parsing with a shared variable layout across the
  per-frame and per-pixel stages), and `PresetRenderer` (per-frame init and update, the
  per-pixel feedback warp, blur passes, decay, the waveform and spectrum overlay, and the
  composite). Covered by 18 pixel-buffer and renderer tests plus 7 preset-parsing tests; the
  warp and the per-frame decay override are asserted through deterministic frame statistics.
  Custom shapes and a per-point waveform program remain for 37e.
- 37d `VisualizerWindow` and wiring - `Done`: the fullscreen window (Escape closes, click
  and Space/arrow keys switch presets, R resets) rendering into a `WriteableBitmap` at
  480 x 270 that the image control scales up, the sidebar **Visualisierung** entry, the
  reduce-motion path that draws a static spectrum at one frame per second, the audio taps in
  the Windows WASAPI, ASIO, and Linux/macOS compatibility players through
  `VisualizerAudioHub` (inactive and therefore free while no window is open), five built-in
  presets, and three new strings in all seven languages.
- 37e Presets and documentation - `Done`: five shipped presets, `VisualizerPresetLibrary`
  loading `.oryvis` and `.milk` files from a configurable folder (broken files are skipped and
  counted in the on-screen label), the **Preset folder** setting with a folder picker, and the
  README, AGENTS, wiki, and CHANGELOG coverage.

- 37f Custom shapes and the per-point waveform - `Done`: `shape_N_*` keys (`sides`, `x`, `y`,
  `rad`, `ang`, `r`/`g`/`b`/`a`, `border_*`, `additive`) with per-shape `per_frame` and
  `per_point` programs, a scanline fill and a border pass, and a `per_point` program for the
  waveform that may move every point by writing `x` and `y`. A sixth built-in preset
  (`Orbit`) shows the feature. Covered by 7 tests.

- 37g Visualizer follow-up - `Done`: the visualizer moved from the sidebar to a fourth
  transport button with its own spectrum-bar icon; the window renders with a silent audio
  source when nothing is playing so it never stays black; and the feedback warp no longer
  clamps out-of-frame samples, which removes the coloured gradient streaks some presets
  produced. `Present()` also invalidates the image after writing the frame, which is what
  finally made the picture appear while audio played. The window overlays the current title
  and artist at the top and previous, play/pause, and next buttons at the bottom left, wired
  to the normal transport methods. A **Visualisierung** settings section now exposes the
  render resolution, the frame rate, and the user preset folder; the overlay buttons are not
  focusable so the arrow keys no longer leave a focus ring on them.

**Tests**: 21 cases for phase 37a; each later phase adds its own.

**Commit**: `feat(visualizer): add the audio analysis foundation`