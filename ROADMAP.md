# Orynivo Roadmap

Items 1-37 are complete and listed for reference only.

Each item is one commit and must follow the completion checklist in
`AGENTS.md`: build every affected project, run the three test projects, update
`CHANGELOG.md` (and `README.md`/nested `AGENTS.md` when behaviour changes), add
English XML docs, add every new visible string to all seven languages, and run
`scripts/verify-localization-parity.ps1` (plus
`scripts/verify-mcp-tool-parity.ps1` for MCP/AI changes).

Status values: `Todo`, `In progress`, `Blocked`, `Done`.

Visualizer fidelity review (2026-09-23): **In progress**. The follow-up fixes now
cover missing GL uniforms, feedback/comp reflection, duplicate custom-wave
execution, isolated element state, real shape keys and compact equation spellings,
PCM waveform scaling, independent GL sampler modes, warp speed/scale, blur ranges,
overlay alpha and display-only gamma/echo. Managed and GPU contract tests cover
these changes. The oracle now uses explicit time and matching PCM input.
The direct Winamp harness now starts MilkDrop at the target aspect ratio; a controlled Royal Mashup probe exposed and corrected textured-shape colour/alpha modulation. Dynamic preset playback is still not frame-synchronized, so its single-frame pixel error is diagnostic only.

See [the current verification report](VISUALIZER-FIDELITY-RECHECK.md) for evidence
and remaining work: the reference's logarithmic frequency equalization, fallback-path
differences and matched Winamp/ANGLE captures. The analyzer now uses the reference's
one-sample pre-emphasis, raised-sine window period and multi-octave waveform
alignment; `scripts/visualizer-compare/render-compare.ps1` renders both
engines under identical resolution, frame time and audio and writes a matching test
tone, so a reference-player capture can be compared with the renders. The default
waveform now uses the reference's per-mode geometry (`MilkdropWaveform`), textured
custom shapes sample the frame through the reference's fan texture coordinates, and
the legacy final composite applies the reference's animated hue shade, all replacing
earlier approximations. Historical completed items below describe implementation
milestones, not proof of full reference fidelity.

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
- 39f Sharper defaults - `Done`: the default render size is now 640 x 360 at 60 frames per second,
  up from 480 x 270 at 30, and the Settings choices keep their existing ranges. After the parallel
  frame passes the built-in presets cost 12 ms per frame on average at 480 x 270, 21 ms at 640 x 360,
  and 43 ms at 960 x 540, so 640 x 360 fits the budget while 960 x 540 is still too slow for a
  default.
- 39g Parallel remaining passes - `Done`: `PixelBuffer.Blur` reuses one scratch array instead of
  allocating a copy per pass, and the blur, decay, gamma, centre darkening, video echo, and composite
  passes now split into row ranges through `ParallelRows.For`, gated by
  `PresetRenderer.ParallelismEnabled` like the warp. `ParallelWarpTests` renders both paths and
  asserts identical frames, including the blur, echo, border, darken, and gamma passes. The
  parallel split cut the built-in presets from 39 ms per frame to 12 ms at 480 x 270 and from 70 ms
  to 21 ms at 640 x 360.

## 40 Visualizer GPU pipeline

**Goal.** Run the preset engine on the GPU so fullscreen resolution and high frame rates become
affordable. This is a project of its own and must keep the CPU path as the fallback.

**Phases**

- 40a Render-surface decision - `Done`: Avalonia's own Skia surface through `SKRuntimeEffect`
  (SkSL) was chosen over an own OpenGL/Vulkan surface, because SkiaSharp is already referenced by
  `Orynivo.Core` and the path stays testable headlessly. The decision, its three risks, and the
  fallback rule are recorded in `DEPENDENCY-MIGRATION.md`.
- 40b Shader translation - `Done`: `ShaderTranspiler` emits SkSL from the parsed tree and
  `SkiaShaderRunner` executes it. Skia accepts the emitted program for 748 of 764 shaders in a
  500-file sample of the preset collection (98 percent, up from 25 percent at the phase start),
  and the CPU/GPU comparison tests agree within one byte. A shader that cannot be translated keeps
  the CPU path for that preset.
- 40c GPU passes - `In progress`: a comp shader without a per-pixel block runs as a Skia runtime
  effect over the renderer's frames when `PresetRenderer.UseSkiaPasses` is enabled, with the
  interpreter as the fallback, and its blur levels are built on the GPU as a chain of box-blur passes
  (`SkiaShaderRunner.BlurFrame` reproduces `PixelBuffer.Blur`); the video echo also runs on the Skia
  path (`SkiaShaderRunner.VideoEcho`), and so does the final additive composite
  (`SkiaShaderRunner.Composite`). The borders also run on the Skia path
  (`SkiaShaderRunner.Borders`), and so does the geometric warp (`SkiaShaderRunner.Warp`).
  The per-pixel expression block now translates too: `PresetCompiler` parses a block once into a
  `PresetSyntaxNode` tree, the LINQ back end still produces the interpreter delegate, and
  `PresetExpressionTranspiler` emits the same tree as SkSL. The emitter reports the uniforms the
  caller seeds, maps `x`/`y`/`rad`/`ang` onto engine locals, and refuses `megabuf`/`gmegabuf` and
  `rand`; against a 500-file sample every one of the 315 global per-pixel blocks translates.
  `ShaderTranspiler.TranspileWarp` composes the block with a warp shader (or a direct frame sample)
  into a warped-`uv` entry point, and `SkiaShaderRunner.WarpPass` runs it over the previous frame.
  The renderer uses it for a preset with at most one warp shader, no per-shader per-frame block, no
  motion recording, and a per-pixel program whose written values are assigned before they are read;
  against the same sample 249 of the 315 global blocks qualify and all 249 translate and are accepted
  by Skia. Anything else keeps the interpreter. The two emitters share the `SkSL` naming and type
  helpers. The comp shader's own per-pixel block is emitted into the comp effect too
  (`ShaderTranspiler.TranspileComp`), so the comp pass no longer requires it to be empty; it has no
  effect on a compiled shader, so the picture is unchanged. The shared `q1`-`q32` and `t1`-`t8`
  variables are now seeded from the preset slots on both CPU shader paths, which is what the GPU
  path already did, so the two execution paths agree on the whole shader vocabulary.
  What remains is the warp shader's `per_frame` block, which is deferred: a survey of the whole
  9795-file collection found no `warp_N_per_frame`, `comp_N_per_frame`, `warp_N_per_pixel`, or
  `comp_N_per_pixel` key at all, and its writes would carry into the next pixel's global per-pixel
  block, which needs a disjointness check before it can run on the GPU.
  The comp pass binds the composited frame, its blur levels, and the previous frame per sampler, and
  scales each sampler by its own `texsize_*`, so the noise textures are sampled at their real size.
  It stays opt-in because the Skia path carries the frame through eight-bit textures and differs
  from the interpreter by up to one level; the cutover belongs to 40e.
  The volume texture is the single source of truth for both paths, not a GPU-only addition: the
  CPU `tex3D` used to evaluate a procedural sine hash
  (`sin(x*127.1 + y*311.7 + z*74.7) * 43758.5453`, fractional part), and that hash could not be
  reproduced bit-exactly in SkSL because the GPU's `sin` differs in its low bits and the
  43758.5453 factor amplifies the difference. Milkdrop itself samples a 3D noise volume here, so
  `VisualizerTextureBank` now generates the two 32³ volumes (`sampler_noisevol_lq`/`hq`) from a
  fixed seed with 3D smoothing and eight-bit quantisation, the CPU interpreter reads them through
  `IShaderSampler.SampleVolume`, and the SkSL emitter samples the same volume from a slice atlas
  with a trilinear helper instead of rejecting the shader. The procedural substitute is replaced
  rather than ported, and the CPU/GPU comparison test covers the volume. The emitter also grew the
  rest of the Milkdrop vocabulary (inferred call return types, engine-bound and undeclared variables,
  samplers named by the shader, writable uniforms, narrowed sampling coordinates, integer vector
  indices, bounded `for` loops, and reserved names), which lifted the share of shaders that translate
  and are accepted by Skia from 395 to 748 of 764, that is from 52 to 98 percent. The remaining 16
  are a few `aspect.zw` presets, constant divisions by zero, two shaders whose helper reads a global
  variable, and a couple of vector comparisons. The `aspect.zw` group is fixed: Milkdrop's `aspect`
  is a `float4` whose `zw` are the reciprocals of `xy`, which projectM binds as its first shader
  constant `(aspectX, aspectY, 1/aspectX, 1/aspectY)`. That lifted the share to 760 of 764, and the
  vector-comparison, narrowed-intrinsic, and helper-global fixes took it to 764 of 764: every shader
  in the sample now translates and is accepted by Skia.
- 40d Platform, packaging, and CI - `Done`: the GPU path adds no native dependency. It runs on
  SkiaSharp, which `Orynivo.Core` already referenced, and the desktop already depends on Avalonia's
  Skia renderer, so the native library is part of the existing dependency graph rather than a new
  one. Verified by publishing the desktop self-contained for `linux-x64` and `osx-arm64` and
  confirming `libSkiaSharp.so` and `libSkiaSharp.dylib` land next to the managed assembly; the
  Windows build ships `libSkiaSharp.dll` the same way. Nothing in the visualizer reaches outside the
  managed SkiaSharp surface, so the existing CI matrix (Windows Debug/Release, the `linux-x64`
  artifact, and the macOS `Orynivo.app` bundles), the packaging scripts, and the signed release
  manifest already cover it and needed no change.
- 40f A real GPU pipeline - `Done for presets without shaders`: section 40 was named "GPU pipeline",
  but nothing in it
  reaches the GPU. `SkiaShaderRunner` creates its surfaces with
  `SKSurface.Create(target.Info, target.GetPixels(), target.RowBytes)`, which is Skia's **raster**
  constructor over CPU memory, so every "Skia" pass is Skia's CPU runtime-effect JIT; the finished
  frame is then copied into a `WriteableBitmap` and the GPU only blits that bitmap. Measured on the
  built-in presets the whole frame costs 6-16 ms at 480 x 270 and 16-59 ms at 960 x 540 with the warp
  dominating, so 4K is roughly a second per frame. The reference implementation reaches 4K at high
  frame rates because mesh, blur, and shaders run in DirectX/OpenGL.
  The context is confirmed: `Avalonia.OpenGL.Controls.OpenGlControlBase` ships in the already
  referenced `Avalonia` 12.1.2 package, and a probe in the visualizer reported
  `GL GlVersion { Type = OpenGLES, Major = 3, Minor = 0 }` over 671 frames, so Avalonia hands out an
  OpenGL ES 3.0 context through ANGLE on Windows. `Avalonia.Win32` also references `Avalonia.Vulkan`
  as a later option. The verified surface is `OnOpenGlInit(GlInterface)`,
  `OnOpenGlRender(GlInterface, int)`, `OnOpenGlDeinit(GlInterface)`, `OnOpenGlLost()`,
  `RequestNextFrameRendering()`, and `GlVersion`; `GlInterface` exposes
  `CreateShader`/`CompileShaderAndGetError`/`CreateProgram`/`LinkProgramAndGetError`/`UseProgram`,
  `GenBuffer`/`BindBuffer`/`BufferData`, `GenVertexArray`/`BindVertexArray`,
  `GenTexture`/`BindTexture`/`TexImage2D`/`TexParameteri`, `VertexAttribPointer`, `DrawArrays`,
  `Viewport`, `ClearColor`/`Clear`, and `Flush` — but **not** `TexSubImage2D`, so a texture update
  re-specifies it through `TexImage2D` or fetches the missing entry point through `GetProcAddress`.
  Step 1 is done: `Orynivo.Controls.VisualizerGlPresenter` uploads the finished frame as an RGBA8
  texture and draws it with a
  `#version 300 es` program over a two-triangle quad in a vertex buffer, and logs the GL version once.
  The presenter is now the visualizer's default presentation; `ORYNIVO_VISUALIZER_OPENGL=0` forces
  the bitmap path, and a platform whose GL context never arrives falls back to it within
  `GlPresenterGraceSeconds`. A shader or context failure is logged and leaves the window on the CPU
  path.
  The remaining steps, each behind its own flag with the CPU renderer as the fallback: (2) move the
  geometric warp to a mesh draw, using the interpolated per-vertex motion the mesh already computes,
  and keep the feedback in an FBO instead of a CPU buffer; (3) move the full-frame passes (blur,
  decay, echo, borders, composite) to ping-pong FBOs in float16, which also removes the eight-bit
  colour drift the Skia path carries; (4) move the comp and warp shaders to GLSL, retargeting
  `ShaderTranspiler` from SkSL to GLSL and compiling in the GL context. The overlay stays on the CPU
  and is uploaded as one texture per frame, because it is a vector drawing and cheap. The GLSL
  preamble must be chosen from the negotiated version rather than hard-coded, because the dialect
  differs per platform (ANGLE's GLES on Windows, desktop GL on Linux, CGL on macOS).
  Step 2's Core side is done: `PresetRenderer.MeshGridX`/`MeshGridY`/`MeshValues` are public,
  `MeshRequested` builds the per-vertex mesh without switching the CPU picture over (the transitional
  double work), and `TryCopyMeshMotion` copies the values while `MeshSource` exposes the frame the
  mesh samples, so the GL warp can upload the mesh as vertex attributes and bind the feedback as its
  source texture. `PerVertexMeshTests` proves the mesh is exposed, that the aspect-scaled zero-to-one
  vertex convention holds, and that requesting the mesh leaves the CPU frame byte-identical.
  Note that the GL context belongs to the control and therefore to the UI thread, so the GL frame
  work has to run inside `OnOpenGlRender`; the render thread cannot drive it without a second shared
  context. The chosen shape is the whole frame pipeline on the control's thread, the way Avalonia's
  GL controls and the reference implementation both work: the CPU keeps the per-frame block, the
  per-vertex mesh, and the overlay as one texture, and the GPU owns the feedback, the warp mesh, the
  full-frame passes, and the shaders.   The Core surface a GPU pipeline reads is complete: the per-vertex mesh (`MeshRequested`,
  `TryCopyMeshMotion`), the clamped per-frame pass values (`ReadFrameParameters`), and the
  overlay-only frame (`RenderOverlayFrame`/`OverlayFrame`), all covered by tests. What is left is the
  GL pipeline itself in the control, which is app-side only.
  Step 3 is done for presets without shaders. `Orynivo.Controls.VisualizerGlPipeline` runs the warp
  as a mesh draw (the fragment shader is a translation of `WarpSampling.SamplePosition`, and every
  texture is bottom-up so the engine's top-down coordinates are converted in the shader), the blur as
  the same nine-tap clamped box filter with ping-pong targets, and decay, video echo, centre
  darkening, both border bands, gamma, and the additive overlay composite in one post pass, because
  each is a function of the same input frame. `PresetRenderer.ExpressionsOnly` is the CPU half: the
  per-frame block, the mesh, and the overlay, with no pixel pass. `GlInterface` exposes only scalar
  uniforms, so vector uniforms are set component by component, and it exposes no `TexSubImage2D`, so
  the overlay texture is re-specified through `TexImage2D`. A preset with shaders keeps the CPU frame
  path, and a pipeline failure is logged once and hands the frame back to the CPU.
  Step 3's float16 feedback is done: the frame textures are `RGBA16F` when the context exposes
  `GL_EXT_color_buffer_float` or `GL_EXT_color_buffer_half_float`, and eight-bit colour otherwise; an
  incomplete framebuffer falls back to eight-bit colour instead of losing the pipeline. The headless
  harness runs a desktop context, which advertises neither extension, so it exercises the eight-bit
  path and `VisualizerGlPipeline.ForceHalfFloat` lets it force the float path for comparison. Measured
  on a blur-heavy synthetic preset (five blur passes, 40 frames) the mean channel difference from the
  CPU reference falls from 9.74 to 9.59 of 255, so the eight-bit rounding was a small part of the
  remaining difference; the rest is structural.
  **Every pass clamps its output to zero-to-one**, exactly like the eight-bit texture it replaces.
  That is not cosmetic: the clamp is what gives a preset that amplifies its own feedback a stable
  fixed point, so without it a preset with `fGammaAdj` below one against a decay near one diverges
  exponentially past the sixteen-bit range, turns into an infinity and then a NaN, and paints the
  whole frame white after a few seconds. The sixteen-bit format buys precision, not range.
  The clamp alone was not enough, because the **comp shader is a display pass, not a feedback
  stage**: the reference draws it into the previous-frame buffer while the next frame's `mainTexture`
  is the pre-comp composite, so a comp shader that amplifies its input (the common `ret *= 10`
  gamma idiom) never compounds. The CPU renderer fed the comp output back instead, so
  `LuxXx - BadBallz Beta` saturated to a white frame by frame 6; the feedback is now the pre-comp
  composite and only the display is the post-comp frame. Measured at 640 x 360 the frame settles
  around 0.85 mean brightness instead of 1.0. `PresetRenderer.Output` is the post-comp display frame
  and `MeshSource` is the pre-comp feedback, and `CompFeedbackTests` pins both down.
  The presenter draws on **every** refresh, re-presenting the texture it drew last when the render
  thread published nothing new. Avalonia's GL surface is double buffered, so an undrawn refresh swaps
  to the buffer two presentations old; because the render loop publishes at the configured frame rate
  while the control refreshes at the display rate, an undrawn refresh is the normal case and the
  artefact was a steady rubber band.
  What remains: deleting the CPU frame path for the GL mode. The GL presentation is the default now;
  the CPU frame path stays as the automatic fallback for a platform without a GL context. A per-pixel
  block that writes the sample position is emitted as a warp fragment shader by default
  (`ShaderTranspiler.TranspileGlslWarp` over a full-screen quad, with
  the frame motion seeded as `_orynivo_*` uniforms and the decay moved to the post pass);
  `ORYNIVO_VISUALIZER_PIXELWARP=0` forces the CPU warp. A block the dialect cannot express no longer
  costs the preset its shaders in the mesh path, which is what sent `$$$ Royal - Mashup (397)` to the
  CPU entirely. `scripts/gl-harness` renders it with `GLH_PIXEL_WARP=1`, and
  `scripts/gl-harness/verify-pixel-warp.ps1` verifies it. A whole-frame CPU-vs-GPU comparison is
  **not** a valid gate: the display frame is dominated by the overlay, which both renderers composite
  identically after the warp, so the frames agree to about 1/255 whether the GPU runs the per-pixel
  warp or the fixed one. The probe therefore draws the overlay only for the first frames and follows
  the brightness centroid of the remaining warped feedback: the GPU tracks the CPU within 0.08 px over
  a 23 px travel, and a control preset without the block stays 26 px away.

  **A matched-music comparison now exists.** Most presets drive their shapes, zoom, and colours from
  the audio, so a tone could not show whether they behave like the reference.
  `scripts/projectm-oracle/run-oracle.ps1 -Audio <track>` converts a track FFmpeg can read into one raw
  PCM file both engines consume, and `scripts/gl-harness` reads the same file with `GLH_ORACLE_AUDIO`.
  It found a real defect straight away: the loudness guard stood at the reference's literal `0.001`,
  which is written for projectM's unnormalized magnitudes, so `mid` and `treble` reported a constant
  one for real music and every preset that reacts to them was dead. The guard is scaled to Orynivo's
  units now. With that fixed and the same track, `$$$ Royal - Mashup (138)` runs at 96-101 mean
  brightness against projectM's 32. The reference's logarithmic frequency equalization is adopted too,
  which changes the band weighting rather than the ratios and therefore did not move that gap; the next
  step is to disable the preset's shapes and see whether the extra energy comes from the overlay or from
  the feedback accumulation.

  The old overlay step follows. The renderer's own stage timings from a real 1920 x 1080 session
  show the overlay is now the last CPU cost - `overlayMs` 120 for `$$$ Royal - Mashup (115)` and
  475-500 for `(135)`, against `warpMs=0` and `compShaderMs=0` - because `PresetRenderer.FillShapeFan`
  scans every fan triangle over its own bounding box, so the centre of a 25-sided shape is tested by
  all 25 triangles. **The shape fills are done.** `PresetRenderer.CollectShapeFills` publishes
  `ShapeFills` and `VisualizerGlPipeline.DrawShapeFills` draws them into the post's source with
  premultiplied `ONE, ONE_MINUS_SRC_ALPHA` blending - the same "over" `PaintPixel` applies, so
  overlapping shapes accumulate identically - and `ONE, ONE` with the alpha channel masked for an
  additive fill. The fan repeats its first rim vertex because `GL_TRIANGLE_FAN` does not wrap, the
  position is y-flipped into OpenGL's space, and a textured fill samples the blurred frame with a
  flipped v. `verify-fidelity.ps1`'s shape probe passes against the GPU fill, and measured at
  640 x 360 the overlay falls from 54.8 ms to 0.8 ms. The CPU still draws the polygon borders and the
  waves; moving those too is the remaining step, though they cover few pixels.
  Reading the original MilkDrop source (the `milkdrop_225c_src` archive, BSD-style Nullsoft licence,
  reference only) then showed the shape space is Direct3D's y-up space, so `shape_y` counts from the
  top: every `shapecode_*` shape had been drawn vertically mirrored. `BuildVertices` and the fan
  centre now convert, projectM confirms the corrected position (72 % versus 75 % from the top for
  `shapecode_0_y=0.75`), and the waveform needed no change because the reference measures `wave_y`
  from the bottom.
  **Step 4 is done.** The GLSL emitter is `ShaderTranspiler`'s GLSL dialect next to SkSL
  (`TranspileGlsl`, `TranspileGlslComp`, `TranspileGlslWarp`): the prelude aliases `float2/3/4` onto
  `vec2/3/4` so the shared body emission is byte-identical, the samplers become `sampler2D` sampled
  with `texture()`, and the entry point becomes a `void main()` writing `orynivoColor` with the
  engine's top-down `fragCoord` built from `gl_FragCoord`. The pipeline integration runs it:
  `VisualizerGlPipeline.SetShaders` compiles the emitted GLSL in the control's context, the warp pass
  uses the warp shader in place of the fixed mesh fragment shader, the blur chain is built into the
  textures the shader's `GetBlur1`-`GetBlur3` read (for the warp from the feedback, for the comp from
  the composited frame), and the comp pass runs after the post pass into its own display target so
  the feedback stays the pre-comp frame. The GLSL samples with normalised coordinates while Skia's
  `eval` takes pixels, so `SamplerCoordinate` and `PixelCoordinate` branch on the dialect; the vector
  uniforms are declared as scalars with a reconstructing macro because `GlInterface` only exposes
  scalar uniform setters; and the uniforms come from `PresetRenderer.WriteShaderUniforms`, which
  seeds the same values the interpreter binds. A shader the dialect cannot express leaves that stage
  on the fixed pipeline, and a preset whose shaders do not emit keeps the CPU frame path.
  The `gl-harness` emits a real preset's shaders, compiles them in the context, and compares the GPU
  frame against the CPU reference: `LuxXx - BadBallz Beta` now renders 0.49 against the CPU's 0.53
  instead of a saturated white frame, a shader-free preset matches to 0.0002, and three further
  shader presets run with `glError=0x0`. The residual difference is the GL frame-pass approximation,
  not the shaders: the fixed-warp pre-comp frame differs from the CPU by the same ratio. The dialect
  is chosen from the negotiated version (ANGLE's GLES on Windows, desktop GL on Linux, CGL on macOS),
  and the SkSL path stays the fallback. The SkSL emitter's matrix support is mirrored: `floatNxN`
  becomes GLSL `matN`, and a single vector argument is spread into scalars because `matN` has no
  four-component constructor.
  The warp's sampling arithmetic now lives in the tested

  `Orynivo.Visualization.WarpSampling`, which the GLSL fragment shader has to translate rather than
  restate, so the two paths cannot drift apart.
  A GL mesh warp was written and then deliberately not wired in, because a warp alone is the wrong
  picture: this engine applies the blur, decay, video echo, borders, gamma, and the overlay *after*
  the warp, so presenting only the warped feedback would drop every one of them and the window would
  look visibly worse. The warp therefore cannot be added on its own; the step has to move the whole
  frame pipeline onto the control's thread, with the CPU providing the per-frame block, the
  per-vertex mesh, and the overlay texture, and the GPU owning the feedback, the passes, and the
  shaders. That is the shape the remaining work has, and it is why the cutover is one step rather
  than four: `PresetRenderer`'s CPU frame path and the GL path cannot share a frame.

- 40e Cutover and validation - `Done`: the GPU path is the visualizer's default where it is
  available and the CPU path stays the fallback, and a comparison harness validates both against the
  same reference frames. `PresetSkiaComparisonDiagnosticTests` is that harness. The CPU-side causes
  it surfaced are fixed: the interpreter was missing the shader functions the SkSL emitter already
  had (`lum`, `asin`, `acos`, `atan`, `cross`, `rsqrt`, `log2`, `exp2`, and others), which silently
  disabled those shaders; `fps` was zero on the first frame, which made a preset that divides by it
  accumulate an infinity; declarations and assignments kept the initializer's component count instead
  of the declared type, so a `float z = float4(...)` differed from the GPU; and `GetPixel` read the
  warped frame while `sampler_main` read the composited one. The GPU-side causes are fixed too: the
  warp pass now builds `sampler_blur1`-`sampler_blur3` from the previous frame, and the shader's `/`
  uses `orynivoSafeDiv` so a zero divisor yields zero as it does on the CPU.
  A constant-source test proves the shader math itself agrees: the same comp shader that the harness
  reports as far apart computes the identical value on both paths and only the eight-bit clamp
  differs. The harness therefore still shows large differences for warp and comp presets, but those
  are the eight-bit Skia surface amplified through the feedback and the comp shader's squaring
  (`ret *= ret`), not a semantic divergence. `VisualizerWindow` enables `UseSkiaPasses`, so the
  compiled SkSL runs the comp shader and a warp shader, and the frame passes' runtime effects are
  cached for the process because their SkSL is constant and Skia compiles an effect when it is
  created. The full-frame passes stay on the interpreter, gated by the new
  `PresetRenderer.UseSkiaFramePasses` and off by default: measured on the raster Skia surface they
  were about 2.6 times slower than the in-place float passes, because each converts the whole frame
  to an eight-bit bitmap and back. With the shader-pass cutover the built-in presets cost 39 ms per
  frame on average at 480 x 270 against 57 ms before.

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
  Reading those parts closed the rest of the class. An assignment or a buffer write is a valid
  `if(...)` argument and a semicolon continues such an argument as a statement sequence,
  `loop(...)`/`while(...)` are accepted wherever a primary expression starts, and
  `exec2`/`exec3`/`exec4` yield their last argument. The join no longer cuts a block off at 64
  parts, strips a part's line comment before concatenating, and does not insert a separator before
  a `(` that continues a call. Against the 2,000-file sample the failed expression blocks fell from
  19 to 4, and those four are two files that use a `while` spelling with a single argument; the
  shader slots stay complete at zero failures. A block that still fails is skipped and recorded on
  `VisualizerPreset.FailedBlocks`, which is the documented design, so a remaining case costs one
  block rather than a preset.

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
  skipped shader slots to 1,020 against the same 2,000 files. The HLSL preprocessor (`#define`,
  `#if`, `#ifdef`, `#ifndef`, `#else`, `#endif`) and the built-in math constants (`M_PI`, `M_PI_2`,
  `M_2PI`, `M_INV_PI`, `M_INV_PI_2`, `M_E`) are translated before parsing, so what remains there is
  the per-frame random translation and rotation vectors. The blur and edge parameter keys are
  recognised: `b1n`/`b1x`/`b1ed` and the `b2`/`b3` family are the Milkdrop 1 `blurN_min`,
  `blurN_max`, and `blurN_edge_darken` parameters under their short names, so they resolve to those
  variables, and a preset that carries only them takes its blur amount from their `blurN_max` sum
  instead of Orynivo's own `blur_level` key. Their edge-darkening amount is applied after the blur
  passes with our own documented falloff: the frame centre is untouched and the border is multiplied
  down towards `1 - amount`, which is the approximation the phase called for because the exact shape
  is not stored in a preset.
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
  The most frequent remaining class, a brace where a statement is expected, was checked against the
  sources and is not the `if (condition) { ... }` form: `ShaderStatementFormTests` proves that form
  parses, including an inline block, an `else` glued to its block with a trailing semicolon, and a
  block on the next line. The compiler deliberately leaves those bodies to the interpreter, which is
  why only the parser is asserted there. The 123 brace failures therefore come from another shape
  and need the same source-by-source treatment that found `float1` and `const`.
  Reading the sources for the four remaining classes paid off again, and this time every cause was a
  dialect gap rather than an oddity. `while` loops were simply unknown, which is the whole
  `Expected ';' but found '{'` class: the parser read the keyword as a name and then demanded a
  semicolon before the block. The comma operator was missing at the two places C allows it, so both
  `ret = a.x, ret = b;` and `texsize.zx*(q3,q3)` failed; it is now parsed at statement level and
  inside parentheses, while call arguments keep their commas as separators. A declaration may now be
  initialized with a braced list (`float2x2 rot = { a, b, c, d };`, built as the same constructor the
  explicit spelling produces) or with a state block (`sampler s = sampler_state { AddressU = WRAP; };`),
  which is what the two `Unexpected '{'` classes were. Vector element access (`retish[0]`) and the
  integer and double vector types (`int2 k1 = ...`) are accepted, and a macro definition now ends at
  its line comment: `#define MyGet GetPixel //GetBlur1` used to expand that comment into the middle
  of a call, where it swallowed the rest of the line and turned the next line into a syntax error.
  The preset expression language had the same kind of gap on the other side of the boundary:
  `PresetLexer` split `+=` into `+` and `=`, so every block containing `n += 1` or `zoom -= 0.03` —
  which is most of them — failed as `Unexpected '='`. The lexer now emits one compound-assignment
  token and the compiler applies it, including for the `gmegabuf(i) += x` buffer form.
  Against the same 2,000 files this took the skipped shader slots from 326 to 10 and the failed
  expression blocks from 345 to 13. What was left was array declarations such as
  `const float4 samples[5] = { ... }` indexed as `samples[i]`, a `loop(...)` that does not close,
  and four single cases. Arrays are modelled now: the parser keeps the element type, the size, and
  the flattened initializer on their own node, and `ShaderInterpreter` stores the elements in array
  storage that an element access reads, so a shader that indexes an array renders what the preset
  asked for instead of dropping its block.
  The parser also stops after the top-level block that is the shader body, because Milkdrop stores a
  footer such as "written by ..." after its closing brace; two presets failed to parse on that text
  before.
  A measurement of real presets at the shipped 480 x 270 render size found the reason the
  collection looks empty rather than merely imprecise. The warp stage costs 83 to 218 ms per frame
  and the comp shader up to 87 ms, while `ShaderTimeBudgetMilliseconds` is 30 ms and the target
  frame interval at the default 30 fps is 33 ms, so `RecordTimings` marks the frame as over budget
  and `BeginShaderFrame` then leaves the shaders off for the next 119 frames. A user therefore sees
  the generic warp of the decayed feedback plus the shared overlay, which is the empty picture with
  a waveform line and a spectrum, and the shaders only come back for one frame in every 120. Two
  separate defects are behind that: the budget compares the whole frame against a threshold that
  only the optional shader work should have to meet, and the penalty for a slow frame is two to
  four seconds of no shaders instead of a smaller one. The deeper cause is that the per-pixel stage
  runs per screen pixel: Milkdrop evaluates its per-vertex program on a mesh of about 32 x 24
  vertices and only the comp pass per pixel, so our 129,600 evaluations are roughly 170 times the
  reference's work. Moving the per-pixel program and the warp shader onto a mesh grid, and letting
  the budget degrade that grid instead of disabling the shaders, is the next step; it is a
  behaviour change to the warp, so it needs its own identical-frame verification.
  Done as the first step of that plan: the shader grid is now adaptive and the warp shader runs on
  it, and the skip mechanism is gone. `ShaderTimeBudgetMilliseconds` no longer decides whether the
  shaders run at all; `AdaptShaderGrid` scales the grid's pixel target from the measured shader cost
  (`ShaderTimeBudgetMilliseconds` against the shader milliseconds) between a floor of 1,024 pixels
  and the 10,000-pixel cap, so a heavy shader settles at a resolution it can afford and keeps
  drawing while a cheap one returns to full size. The warp stage's per-pixel program is unchanged
  and still runs per pixel, but the warp shader itself now runs once per grid point through
  `WarpShaderGrid` and is scaled over the frame, which is what removed the cost. Measured on the
  same real presets at 480 x 270: warp 83-218 ms to 7-27 ms, frame 106-233 ms to 19-35 ms, with the
  shaders intact. What is left is the per-pixel program itself, which still runs per screen pixel
  rather than on Milkdrop's roughly 32 x 24 mesh; moving it needs an interpolated sampling field and
  is the next fidelity step. A second, unrelated gap showed up while measuring: several real presets
  lose their shaders at run time to an unimplemented built-in (`conway` is the known one) or to the
  interpreter's loop budget, so those blocks are disabled after the first frame. Both are recorded
  as their own work rather than being hidden by the budget.
  The `conway` half of that claim is retracted. A scan of the collection found 294 occurrences of the
  name, all of them shader-local variables (`float1 conway = tex2D(...)`), and **zero** in an
  expression block; the scan had treated shader-local identifiers as built-ins.
  The loop-budget half is retracted as well, and by measurement rather than inspection: rendering all
  2000 sampled presets for three frames at 64 x 36 reports **0 presets with a runtime error and 0
  loop-budget errors**. The claim had counted a *parse* failure as a run-time loss. So nothing in the
  collection loses a block or a frame at run time today; the only open fidelity work is the per-pixel
  mesh convention below and the GLSL shader port in 40f.
  **Remaining.** Two things are still open in this phase. The **matrix types are done**:
  `float2x2`, `float3x3`, and `float4x4` were unknown to the shader runtime, so
  `Unknown shader function 'float2x2'` disabled a shader outright. A scan of the 9795-file collection
  found 729 files that build a `float2x2`, 59 that build a `float3x3`, and none that build a
  `float4x4`. The usage is construction plus `mul` —
  `mul(uv1, float2x2(ang2.y,-ang2.x,ang2.x,ang2.y))` with four scalars,
  `mul(16*uv1, float2x2(_qb))` with one vector, and
  `mul(float3(...), RotMat)` for the static-const three-by-three — so the implemented scope is a
  matrix value of any of the three dimensions, the `floatNxN(...)` constructor from scalars or a
  vector, and HLSL's `mul` for matrix-by-vector, vector-by-matrix, and matrix-by-matrix.
  The representation is the decision that mattered: a matrix needs nine or sixteen components, and
  putting them inside `ShaderValue` would make every shader value four times larger and slow the
  per-pixel path for the 99 percent of presets that never build one. Measured on the comp-shader cost
  harness, an inline matrix value took the 640 x 360 frame from 26 ms to 47 ms. The matrix therefore
  lives in a per-pixel pool in `ShaderRuntime` (`StoreMatrix`/`ResetMatrixPool`) and the value carries
  only its index and dimension, which left the measured cost within noise of 26 ms. `ShaderRuntime`
  is the single dispatch point both execution paths call, the compiled path in `ShaderProgram` and the
  SkSL emitter agree with it (`floatNxN` becomes a SkSL `matN`, a vector argument is spread into
  scalars because `matN` has no four-component constructor, and a matrix argument is not narrowed),
  and `ShaderMatrixTests`, `ShaderCompilerTests`, and `ShaderTranspilerTests` are the check. The
  collection-wide translation harness reports every sampled shader accepted by Skia.
  The per-pixel mesh is implemented and
  opt-in, but it cannot become the default yet: the engine's per-pixel `x`/`y` are the warped
  position in minus-one-to-one space rather than Milkdrop's aspect-scaled zero-to-one vertex
  position, so a preset that derives `dx`/`dy` from `x`/`y` renders visibly differently once that
  offset is interpolated. Reconciling the two conventions is the step that turns the mesh on. The
  expression language's `while` used to require a statement list after its condition, which is not
  what a real collection writes: it writes the one-argument `while (exec2(statements, condition))`,
  so those blocks were rejected and the preset lost its motion. Both spellings are now accepted and
  the affected presets compile every block again; parsing the 2000-file sample collection reports 0
  whole-file, 0 expression-block, and 0 shader-slot failures, against 4 expression-block failures in
  2 files before. And the blur
  chain's blur amount and edge darkening (`blurN_min`/`blurN_max`/`blurN_edge_darken` and their
  `bNn`/`bNx`/`bNed` aliases) resolve to their variables but are not applied, because the reference's
  semantics are not stored in a preset and a measured comparison against projectM showed that
  applying a guess left the picture no closer than leaving it out.
  The per-pixel mesh itself is done: `PresetRenderer.MeshPerPixelEnabled` makes the warp evaluate the
  per-pixel program once per 64 x 48 mesh vertex — the reference's default grid — and interpolate the
  motion across the quad, and `PerVertexMeshTests` proves the constant-motion case is byte-identical
  to the per-pixel path, that a position-varying motion is interpolated, and that a position-writing
  program ignores the setting.
  A comparison harness now exists for the fidelity work that remains:
  `scripts/projectm-oracle/` builds projectM as the reference, renders a preset with it and with
  Orynivo, and reports the mean channel difference and the correlation per frame. It is a local
  development tool, links a projectM checkout the developer builds, and is not part of any build,
  test run, or release artifact. Against a few presets the correlation is weak but positive where
  the two renderers draw a similar structure, which is the signal the remaining shader and audio
  work has to move.
  The oracle drove the first five fidelity fixes. **Milkdrop 2's `f`-prefixed scalar keys were not
  aliased**, so `fDecay`, `fWaveAlpha`, `fWaveScale`, `fWaveSmoothing`, `fWaveParam`, and the
  `fWaveR/G/B/X/Y` colours fell back to the built-in default: `LuxXx - BadBallz Beta` asked for
  `fWaveAlpha=0.001` and got a full overlay, and its `fDecay=0.925` fed back at 0.96. With the
  aliases in place that preset went from a mean channel difference of 0.5 and a correlation of 0.03
  to 0.07-0.14 and 0.36-0.41. **A shader helper call did not coerce its argument to the declared
  parameter type**, so `lavcol(float t)` called as `lavcol(ret * 2)` ran on the whole vector and the
  SkSL emitter produced a call Skia rejected; both paths now coerce. **The shader blur levels used a
  three-by-three box at full resolution**, while the reference runs a long horizontal and a short
  vertical weighted filter on a downscaled copy of the frame (`blur1` a quarter, `blur2` an eighth,
  `blur3` a sixteenth); `GetBlur1` feeds a preset's own `tan` term, so the sharper `ist` turned a soft
  blob into a hard diamond. **The reference's roam vectors (`roam_cos`, `roam_sin`, `slow_roam_cos`,
  `slow_roam_sin`) were never bound**, so a shader that computes `1 + normalize(slow_roam_cos)`
  normalised a zero vector and the infinity spread a white shape across the frame; seeding them from
  the preset time moved `Waltra - Horizon` from 0.12 to 0.04-0.06. **The GL pipeline never uploaded
  the generated noise and volume textures**, so a `tex3D` cloud or a noise sample read the fallback
  unit, which is the frame; it now uploads them.
  What remains, in the order the oracle ranks them: projectM tints the frame with a per-preset
  **random** hue (the video echo's `shade` and `ApplyHueShaderColors`), which cannot be reproduced and
  makes an exact colour match impossible; the comp shader's `sampler_main` is the warped frame in
  projectM and the composited frame here; and the video echo and overlay modulation still differ. The
  oracle's audio input is not equivalent to projectM's own analysis unless both sides run silent
  (`ORACLE_SILENT` and `GLH_SILENT`), so a comparison must use that mode before the numbers mean
  anything, and the Orynivo side must be the GL pipeline because the CPU pass budget abandons a heavy
  comp shader the GPU runs.
**Tests**: each phase adds its own; 39a is the prerequisite for claiming any speed-up.

**Commit**: `perf(visualizer): add render measurement` (39a), then one commit per phase

