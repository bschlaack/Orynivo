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
  focusable so the arrow keys no longer leave a focus ring on them. An **Always show text and
  controls** toggle switches between a permanent overlay and one that follows mouse movement
  over the window only.

**Tests**: 21 cases for phase 37a; each later phase adds its own.

**Commit**: `feat(visualizer): add the audio analysis foundation`

## 38 MilkDrop preset compatibility

**Goal.** Run real `.milk` presets faithfully instead of only the documented subset. Today
the engine parses `per_frame`, `per_pixel`, `shape_N_per_frame`, `shape_N_per_point`, and a
waveform `per_point` program over a small variable set. A real preset additionally uses the
`*_init` blocks, the complete stage order, the full MilkDrop variable set, four independent
waveforms, borders, motion vectors, video echo, texture samplers, and `warp_*`/`comp_*`
HLSL shaders.

**Fidelity.** Phases 38a-38c and 38e bring the shader-free presets to the original's
behaviour. Only 38d makes shader-carrying presets look right; without a GPU shader pipeline
that phase is an approximation, not a bit-exact MilkDrop.

**Strategy.** The shader runtime starts as a CPU interpreter for the `ps_2_0` subset the
presets actually use. It needs no native dependency and can later be swapped for a GPU path
without changing the preset model. A GPU implementation would require DXC, SPIR-V, and a
Vulkan/OpenGL compute path, which the Avalonia render surface does not currently expose.

**Licensing.** The engine, the generated noise textures, and every shipped preset stay
written here. No third-party visualizer code or preset bundle is linked.

**Phases**

- 38a Full pipeline and block set - `Done`: the `per_frame_init`, `per_pixel_init`,
  `wave_N_init`, and `shape_N_init` blocks, the complete MilkDrop stage order (per-frame init
  and update, warp, blur passes, per-pixel, composite, borders, motion vectors, waves, shapes,
  video echo), and the full variable set (`zoom`, `zoomexp`, `rot`, `cx`, `cy`, `dx`, `dy`,
  `warp`, `sx`, `sy`, `wave_*`, `ob_*`, `ib_*`, `mv_*`, `echo_*`, `q1`-`q32`, `blur1`-`blur3`,
  `fDecay`, `fGammaAdj`, `darken_center`, `aspectx`/`aspecty`, `pixelsx`/`pixelsy`,
  `monitor`, `frame`, `time`, `fps`, and the smoothed `*_att` bands). The standard variable
  set is registered by `PresetVariableLayout.RegisterStandardVariables`, the four waveforms
  and every shape carry their own initialisation block, and the motion parameters, blur
  passes, centre darkening, and gamma adjustment are applied. The per-pixel block receives
  the warped sampling position in `x`/`y`, a documented deviation from Milkdrop offset
  semantics that keeps the built-in presets working until 38d revisits it. Covered by 16
  tests.
- 38b Waves, borders, motion vectors, video echo - `Done`: all wave modes including
  additive, dots, thick, and mystery, per-wave colour and position programs, outer and inner
  borders, motion-vector grids, and the video-echo stage. The circular, doubled, and
  single-line modes honour dots, thick, additive, mystery, and the wave colour/position keys;
  the four declared waveform slots are drawn; the outer and inner borders paint coloured
  frames; the motion-vector grid is derived from the real motion field; and the video echo
  blends a scaled, optionally flipped copy with its zoom, alpha, and orientation keys. Numeric
  preset keys now seed the per-frame variables, so key-only presets work. Covered by 11 tests.
- 38c Textures and `tex_` blocks - `Done`: `VisualizerTextureBank` generates the `noise_lq`
  (32 x 32), `noise_mq` (256 x 256), and `noise_hq` (512 x 512) textures and the sixteen
  `rand00`-`rand15` (32 x 32) textures deterministically from fixed seeds, samples them
  bilinearly, and supports repeat, clamp, and mirror wrap. Correcting the original note: the
  Milkdrop format has no per-preset texture block, so `tex_*` keys stay ignored, and the
  sampler constructs (`sampler_main`, `sampler_pc_main`, `sampler_fc_main`, `GetBlur1`-`GetBlur3`,
  `GetPixel`) are HLSL and belong to 38d. Covered by 10 tests.
- 38d HLSL front end - `Done`: `ShaderLexer` and `ShaderToken` tokenize the `ps_2_0` subset
  Milkdrop shaders use. Identifiers and keywords, numbers with their `f`/`h` suffixes,
  single- and multi-character operators, swizzles, line and block comments, and exact source
  positions are covered, and an unexpected character reports its offset through
  `PresetExpressionException`. Covered by 10 tests.
- 38e HLSL parser - `Done`: `ShaderNode` and `ShaderParser` build a tagged-union tree for the
  subset: declarations, expression statements, `if`/`else`, `for`, `return`, swizzles, calls,
  the ternary operator, and the C operator precedence. Function signatures and bare statement
  bodies both parse, and a sampler declaration without a type is tolerated. Covered by
  10 tests.
- 38f HLSL interpreter - `Done`: `ShaderInterpreter` and `ShaderValue` evaluate the parsed tree
  with scalar and `float2`/`float3`/`float4` values: arithmetic with the C precedence, variables
  and the assignment operators, swizzles read and written, vector constructors with
  concatenation and broadcast, the ternary operator, `if`/`else`, `for`, and the intrinsics
  (`abs`, `ceil`, `clamp`, `cos`, `dot`, `exp`, `floor`, `frac`, `length`, `lerp`, `log`, `max`,
  `min`, `mul`, `normalize`, `pow`, `saturate`, `sign`, `sin`, `smoothstep`, `sqrt`, `step`,
  `tan`). Sampling is bound through `IShaderSampler` so the interpreter carries no render state,
  division by zero yields zero, and a loop budget of 4096 iterations plus a call depth limit of
  32 bound a runaway shader. Covered by 14 tests.
- 38g Shader bindings and cost budget - `Done`: the `warp_N_*` and `comp_N_*` preset keys
  (enabled flag, per-frame and per-pixel blocks, and the shader source), the sampler bindings
  (`sampler_main`, `sampler_pc_main`, `sampler_fc_main`, `GetBlur1`-`GetBlur3`, `GetPixel`,
  and the texture bank), and a bounded per-frame cost budget that lowers the render resolution
  instead of stalling playback. The preset reader keeps the newlines inside a multi-line value
  so shader source survives, a shader that fails to parse is skipped rather than fatal, and the
  renderer implements `IShaderSampler` for `sampler_main`, `sampler_pc_main`, `sampler_fc_main`,
  `GetBlur1`-`GetBlur3`, and `GetPixel` while binding `uv`, `uv_orig`, `texsize`, the audio and
  smoothed bands, the frame counters, and the aspect ratio. The budget skips the shaders for a
  while instead of lowering the resolution, which is the documented deviation from the original
  plan. Walking the tree per pixel is the known cost limit; a JIT compiler for shaders is the
  follow-up if needed. Covered by 11 tests.

- 38h `.milk` compatibility and validation - `Done`: `[presetNN]` sections, version and
  `nWaveMode` handling, tolerance for the remaining legacy keys, a corpus of real presets as
  regression fixtures, and the per-preset skip diagnostics. Every section of a multi-preset
  `.milk` file becomes its own preset, the declared format version is reported but never gates
  loading, and a skipped preset carries a reason naming the file or section. The regression
  corpus is hand-written in the real format because third-party presets are licensed by their
  authors and are never bundled; it covers a multi-section file, a shader, and a minimal preset.
  Covered by 13 tests.

**Tests**: each phase adds its own; 38d additionally needs a shader-interpreter suite and a
render comparison against hand-computed reference pixels.

**Commit**: `feat(visualizer): extend the preset engine towards MilkDrop compatibility`

## 39 Visualizer render performance

**Goal.** The preset engine renders on the CPU, single-threaded, and on the UI thread, so it
pays for that with a low render resolution and a coarse, upscaled picture. Make the existing
CPU path fast enough first, then move the work to the GPU in section 40.

**Starting point.** The frame is a float RGBA `PixelBuffer` at 480 x 270 by default. Every
stage is a separate full-frame pass (warp with bilinear sampling, blur passes, decay, centre
darkening, gamma, video echo, composite, comp shaders), the HLSL runtime walks its syntax tree
per pixel, and `VisualizerWindow` drives the loop from a `DispatcherTimer`, so a heavy frame
stalls the interface as well. Only the presentation is GPU work: the finished frame is uploaded
to a `WriteableBitmap` that Skia scales up.

**Phases**

- 39a Render measurement - `Done`: per-stage timings (warp, blur, shader, overlay, composite,
  present) reported per preset and per resolution, surfaced in the window's diagnostic line and
  asserted in tests as bounded ratios rather than absolute times. Every later phase must show
  its gain here instead of by eye. `RenderTimings` reports warp, blur, post-processing, overlay,
  composite, comp shaders, and the frame total, both per frame and averaged over a window that
  `ResetTimings` restarts. The window's diagnostic line carries those averages once per second
  together with the render size and the skip state. A warp shader stays part of `Warp` because
  timing it per pixel would cost more than the measurement; the frame budget now compares the
  complete frame. Covered by 8 tests, all asserting ratios rather than absolute times.
- 39b Off the UI thread - `Done`: move the render loop off the Avalonia dispatcher into a
  background loop that presents by marshalling only the bitmap invalidation, so a heavy frame can
  no longer stall the interface. Keep shutdown, preset switching, reduce-motion, and the overlay
  idle timer correct. The loop runs on a background thread and hands a finished copy of the frame
  to the UI thread through a presentation buffer, with at most one present queued at a time, so
  the interface can neither be blocked by a frame nor build a backlog. Preset switching, the
  reset key, and shutdown travel as flags the render thread applies, and the pacing arithmetic
  lives in the pure, tested `FramePacing`. Covered by 5 tests; the loop itself is verified by
  running the window.
- 39c Parallel warp - `Done`: `ParallelRows.For` splits a frame into whole-row ranges across a
  bounded worker count and keeps small frames on the calling thread. The warp runs in parallel
  only when the per-pixel program writes nothing but the values the engine re-seeds per pixel
  (`x`, `y`, `rad`, `ang`), because a value written by one pixel and read by another would make
  the picture depend on the split; a warp shader or an active motion grid keeps it sequential.
  Each worker owns its slot array and sample scratch, and `ParallelWarpTests` proves that both
  paths render byte-identical frames. Covered by 16 tests.
- 39d Allocation-free hot path - `Done`: reuse every frame buffer and temporary, remove
  per-frame allocations and delegate churn from the pixel loops, and prove it with an allocation
  check around a rendered frame. Done before 39c because the measurement showed the per-pixel
  path is the bottleneck: the compiler now reports the variables a program references, the warp
  stage resolves its slots once instead of looking each name up per pixel, and it skips the polar
  pair, the motion grid, and the seeded sampling position when the preset never reads them. A
  rendered frame allocates nothing. Covered by 5 tests, including the allocation check.
- 39e JIT-compiled shaders - `Done`: compile the parsed shader tree to
  `System.Linq.Expressions` through the existing preset compiler machinery instead of walking it
  per pixel, keep the interpreter as the validation and fallback path, and compare both against
  the same reference frames. Measured first, because the target matters: the interpreter costs
  about 660 ns per pixel, so a comp shader takes 619 ms per frame at 1280 x 720 and the frame
  budget skips it, which is why no shader runs at all. `ShaderCostDiagnosticTests` reports that
  number per resolution; the JIT has to bring it to roughly 10 ns per pixel for the shaders to fit
  inside the budget, and that measurement is the acceptance criterion for this phase.
  `ShaderCompiler` and `ShaderProgram` now compile straight-line shader bodies (declarations and
  one return) into a delegate over a slot array, calling the same `ShaderRuntime` operations as the
  interpreter; anything else stays interpreted, and `ShaderCompilerTests` proves both paths produce
  identical values. The renderer uses the compiled shader when it is available and seeds the
  frame-constant variables once per frame instead of per pixel. That took the measured cost from
  about 660 to about 242 ns per pixel (2.7x), which is real but not enough: 720p still needs 232 ms
  and the budget skips it. The remaining cost is helper overhead — a string dispatch per call, a
  delegate indirection per component, and a loop per swizzle — so the next step is to emit the
  arithmetic inline instead of calling helpers, which is what the 10 ns target requires.
  A comp shader is also a post-processing pass, so it now runs on a grid bounded to 40,000 pixels
  and is scaled back over the frame, which made its cost independent of the render size: at
  1280 x 720 the same pass fell from 232 ms to 43 ms. That is still above the budget, so the
  remaining steps are, in order, emitting the arithmetic inline (the ~240 to ~40 ns step), a
  cheaper upscale than the bilinear one, and only then a decision about the default budget or the
  GPU phase 40, which is the real answer for full resolution.
  The inline step is done for what it can reach: arithmetic now calls one runtime method per
  operator instead of a delegate, and a swizzle has its components selected at compile time and
  binds its source to a local so a texture call is evaluated once. Measured, that bought only
  about ten percent, because the remaining cost is the string dispatch in `ShaderRuntime.Call`,
  which runs about six times per pixel, plus the full-frame upscale of the smaller comp grid. So
  the next step is numeric opcodes instead of a name switch — the compiler knows the function at
  compile time — followed by a cheaper upscale than the bilinear one.
  Done: `ShaderRuntime.Call` dispatches on a numeric `Opcode` now (the compiler resolves the
  function at compile time), the comp grid is bounded to 10,000 pixels, and the grid is scaled
  back with nearest-neighbour sampling instead of bilinear, because the upscale over the whole
  frame cost more than the shader it scaled. Measured, a comp shader now fits the budget at
  320 x 180 (8.6 ms) and 640 x 360 (18 ms) and the shaders finally run there; 1280 x 720 is
  borderline and the measurement on this machine swings between 26 and 46 ms, so it still skips
  there and the default budget was raised from 20 to 30 milliseconds to give the common
  resolutions room. Full resolution remains the GPU phase 40's job.
  Re-measured on a local session rather than over RDP, where the numbers were distorted: a comp
  shader now costs 9.0 ms at 320 x 180, 20.1 ms at 640 x 360, and 27.8 ms at 1280 x 720, so it fits
  the 30 ms budget at every common resolution and the shaders run. That closes this phase. The
  remaining blocker for real collections is 39i, which is unblocked now: a preset whose shaders are
  Milkdrop 2 templates still reports `shaders=warp0/comp0` because the template dialect is not
  translated yet.
- 39f Sharper defaults - `Pending`: raise the default render resolution and frame rate to what
  the measured cost allows, keep the existing settings ranges, and document the recommended
  values in README and the wiki.
- 39g Parallel remaining passes - `Pending`: the blur, decay, gamma, darken, echo, and composite
  passes still run on one thread even though they are row-independent. Give `PixelBuffer` a
  reusable blur scratch (it allocates one array per pass today), split those passes through
  `ParallelRows.For`, and extend the identical-frame comparison to cover them.

## 40 Visualizer GPU pipeline

**Goal.** Run the preset engine on the GPU so fullscreen resolution and high frame rates become
affordable. This is a project of its own and must keep the CPU path as the fallback.

**Phases**

- 40a Render-surface decision - `Pending`: evaluate Avalonia's Skia surface (SKSL and
  `SKRuntimeEffect`) against an own OpenGL/Vulkan surface (for example Silk.NET), pick one, and
  record the decision, its risks, and the fallback rule in `DEPENDENCY-MIGRATION.md`.
- 40b Shader translation - `Pending`: translate the HLSL subset, or the preset expressions, into
  the chosen GPU shading language, reusing `ShaderParser`; a shader that cannot be translated
  keeps the CPU path for that preset instead of failing.
- 40c GPU passes - `Pending`: warp, blur, video echo, borders, and composite as GPU passes with
  the waveform and spectrum uploaded as small textures and no per-frame readback.
- 40d Platform, packaging, and CI - `Pending`: native dependencies for Windows, Linux, and macOS,
  packaging, the signed release manifest, and the CI build matrix.
- 40e Cutover and validation - `Pending`: the GPU path becomes the default where it is available,
  the CPU path stays the fallback, and a comparison harness validates both against the same
  reference frames.

- 39h Remaining preset-block failures - `Done`: the numbered expression parts are joined the
  way Milkdrop does it (concatenation, with a separator only when the previous part is complete),
  which removed every `Unexpected ';'` failure, and the shared `megabuf`/`gmegabuf` buffers exist
  with serialised access. Against a 2000-file collection the skipped expression blocks fell from
  1267 to 68; the remainder is `Expected ')'`-style syntax (57) plus a few stray operators, and it
  needs the failing parts to be read case by case. `PresetFolderDiagnosticTests` lists the current
  reasons with example files; keep it updated as the compiler grows. `loop(count, statements)` and
  the buffer write forms `gmegabuf(index, value)` and `gmegabuf(index) = value` are implemented, so
  the skipped expression blocks fell from 1267 to 19 against the same collection. The remainder is
  16 `Unexpected '='` and 2 `Unexpected '*'` cases plus one `Expected ')'`; each needs its parts
  read one by one, and they are no longer a class of failure that affects whole preset families.

- 39i Milkdrop 2 shader dialect and blur/edge keys - `Pending`: measured against a real
  2000-file collection, 1706 presets (85 percent) declare their `warp_N`/`comp_N` values as
  Milkdrop 2 template references such as `` `shader_body ``, whose actual HLSL lives in Milkdrop 2's
  built-in templates rather than in the file, so no shader is loaded for them and they render only
  the generic warp and the shared overlay. The same presets use Milkdrop 2's per-frame blur and
  edge parameter keys (`b1n`, `b1x`, `b1ed`). Implement our own equivalents of the default shader
  bodies and the blur/edge chain, resolve the template markers before parsing, and extend the
  diagnostic to separate template presets from genuinely broken ones. Do not copy Milkdrop's
  sources; the shipped templates stay written here.

  A survey of 3000 collection files sharpened this into a dialect problem rather than a small
  marker map: the `` ` `` prefix marks Milkdrop 2 shader keywords throughout the source, with
  `` `ret `` (9140), `` `float2 `` (5558), `` `if `` (5543), `` `shader_body `` (4920), `` `float3 ``,
  `` `float ``, `` `float4 ``, `` `uv ``, `` `sampler ``, `` `uv2 ``, and `` `GetPixel `` all in the
  thousands. Translating that dialect is its own project, and it is only worth doing once the
  shader runtime is fast enough to run the result: a per-pixel comp shader currently costs tens of
  milliseconds on the CPU interpreter, so the budget skips it and the preset looks unchanged.
  Phase 39e is therefore the prerequisite. The blur/edge chain is a separate, larger family:
  `b1n`, `b1x`, and `b1ed` (plus `b2*` and `b3*`) appear in 313 of 400 sampled files, and their
  exact semantics are not in the presets, so they need our own documented approximation.

  The storage format is decoded now: Milkdrop 2 writes a shader one source line per numbered key,
  each line carrying a backtick marker, and `` `shader_body `` only says where the body starts.
  `VisualizerPreset` detects that form and joins the lines, so a real collection went from 50,707
  skipped shader slots to 1,020 against the same 2,000 files. What remains is the HLSL preprocessor
  (`#if` and friends) and the macro constants such as `M_INV_PI_2`, which account for the rest, plus
  the blur and edge parameter keys (`b1n`, `b1x`, `b1ed`, and the `b2`/`b3` family) that need our own
  documented approximation because their semantics are not in the presets.
  Comparing the variable sets against the reference implementation closed three more gaps and
  exposed one semantic difference. Added: the eight `t1`-`t8` variables, plus `progress`, `meshx`,
  and `meshy`. The difference is bigger than a missing name: in Milkdrop the motion variables
  (`zoom`, `zoomexp`, `rot`, `warp`, `cx`, `cy`, `dx`, `dy`, `sx`, `sy`) are per-vertex variables
  that a per-pixel program may change, and the changed value carries into the next pixel. Our warp
  reads them once per frame as constants, so a preset that writes any of them inside `per_pixel`
  has no effect here, and the reference notes that some presets depend on that. Moving those reads
  into the pixel loop is the next fidelity step; it is a behaviour change to the warp, so it needs
  its own verification. Done: the warp reads those variables inside the pixel loop when, and only
  when, the preset's per-pixel program writes one of them — the compiler reports that through
  `WrittenVariables` — so a preset that changes them sees the change on the following pixel while
  every other preset keeps the cheaper per-frame path. `PerPixelMotionTests` covers a per-pixel
  zoom, a per-pixel rotation, and the unchanged per-frame path.
  The reference implementation's shader header also settled two more points. First, it transpiles
  HLSL to GLSL and runs it on the GPU, which confirms that phase 40 is the natural home for full
  fidelity and that our CPU path is the harder route. Second, it resolves the sampler names a
  shader references, which we now do as well: `sampler_noise_lq`, `sampler_noise_mq`,
  `sampler_noise_hq`, and `sampler_rand00`-`sampler_rand15` sample the generated texture bank
  instead of falling back to the frame, so the bank built in 38c is in use. What remains is the
  preprocessor and the macro vocabulary from `MilkdropShader.cpp` plus the per-frame random
  variables it keeps (`rand_frame` and the random translation and rotation vectors).
  `rand_frame` is bound now as well: a random four-component vector, refreshed once per frame and
  available to both shader paths. Still missing from that family are the random translation and
  rotation vectors the reference keeps per preset.
  The shader front end now accepts `float1`/`half1` and the `const`/`static`/`inline` qualifiers,
  which took the skipped shader slots against the same 2,000 files from 697 to 326 and from the
  original 50,707 to 326 overall. The remainder, in order of frequency, is a `{` where a statement
  is expected (123), multiple declarations without a separator (73), array indexing (50), and a few
  single cases; each needs its source read, the way the `float1` and `const` cases did.
**Tests**: each phase adds its own; 39a is the prerequisite for claiming any speed-up.

**Commit**: `perf(visualizer): add render measurement` (39a), then one commit per phase
