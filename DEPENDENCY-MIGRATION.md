# Dependency migration plan

This is the decision record for the dependency lines that are intentionally held
back by `.github/dependabot.yml`. Dependabot ignores those **major** upgrades so
they cannot land as an unreviewed automatic bump; this file records what each
pin is waiting for and how to migrate it deliberately.

Nothing here is a permanent constraint. Each section names the concrete trigger
that unblocks the upgrade, the migration steps, and the checks that must pass
before the pin is raised.

## Toolchain support window

The solution targets `net10.0`. Per the Microsoft .NET support policy, the
relevant lines are:

| Version | Type | Phase                             | End of support   |
| ------- | ---- | --------------------------------- | ---------------- |
| .NET 10 | LTS  | Active                            | 14 November 2028 |
| .NET 9  | STS  | Maintenance (security fixes only) | 10 November 2026 |
| .NET 8  | LTS  | Maintenance (security fixes only) | 10 November 2026 |

**Target `net10.0`, not `net9.0`.** .NET 9 was already in its security-only
maintenance phase and reaches end of support on the same day as .NET 8, so
moving to it would have been a dead end. The .NET 10 migration is complete (see
below); the previous `net8.0` line stops receiving fixes on 10 November 2026, so
staying there was not an option.

Re-check these dates against the .NET support policy before planning the work;
the table is a snapshot, not a permanent fact.

## Current state

| Dependency                      | Current                                                                                            | Pinned because                                                        | Blocked by                                       |
| ------------------------------- | -------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------- | ------------------------------------------------ |
| .NET runtime and SDK            | `net10.0` / `net10.0-windows10.0.19041.0` (SDK 10.0.401)                                           | LTS with support until 14 November 2028                               | None                                             |
| Avalonia                        | `12.1.2` (incl. `Avalonia.Controls.DataGrid`)                                                      | Matches the .NET 10 toolchain                                         | None (a future major needs review)               |
| `AvaloniaUI.DiagnosticsSupport` | not referenced                                                                                     | The Debug-only DevTools bridge; the app never called `AttachDevTools` | Add it deliberately if DevTools are wanted again |
| SkiaSharp                       | `3.119.4` (`Orynivo.Core`, plus `SkiaSharp.NativeAssets.Linux.NoDependencies` in `Orynivo.Server`) | `Avalonia.Skia` 12.1.2 depends on SkiaSharp `3.119.4`                 | None (major upgrades still need review)          |
| `Microsoft.Data.Sqlite`         | `10.0.12`                                                                                          | Matches the .NET 10 toolchain                                         | None (major upgrades still need review)          |
| `Microsoft.NET.Test.Sdk`        | `18.10.1`                                                                                          | Matches the .NET 10 toolchain                                         | None (major upgrades still need review)          |
| `Tmds.DBus.Protocol`            | `0.95.1`                                                                                           | Linux-only; `0.92.0` is the documented floor                          | None (minor/patch updates are welcome)           |

Target frameworks today: `Orynivo` and `Orynivo.Tests` use
`net10.0-windows10.0.19041.0` on Windows and `net10.0` elsewhere;
`Orynivo.Core`, `Orynivo.Server`, `Orynivo.Core.Tests`, and
`Orynivo.Server.Tests` use `net10.0`. `global.json` pins SDK `10.0.100` with
`rollForward: latestFeature`, so any installed `10.0.x` SDK satisfies it.

## Completed: the .NET 10 LTS migration

The projects now target `net10.0` / `net10.0-windows10.0.19041.0` on the .NET 10
LTS line. What changed:

1. `global.json` pins SDK `10.0.100` with `rollForward: latestFeature`, so any
   installed `10.0.x` SDK satisfies it.
2. Every `TargetFramework` moved in one commit. A mixed set does not build,
   because the desktop project would reference a `net10.0` `Orynivo.Core` from a
   `net8.0` target.
3. `Microsoft.Data.Sqlite` moved to `10.0.12`, `Microsoft.AspNetCore.TestHost`
   to `10.0.12`, and `Microsoft.NET.Test.Sdk` to `18.10.1`.
4. Every workflow pins `dotnet-version: 10.0.x`.
5. `Orynivo/Orynivo.csproj` now copies the Lucene `NOTICE.txt` from the
   `4.8.0-beta00018` package directory it actually references; the path still
   named `beta00017`, so the license file was silently skipped.

Verification: `scripts/verify-all.ps1` green in Debug and Release with
`--warnaserror`, and 570 tests passing on `net10.0` (`Orynivo.Core.Tests` 427,
`Orynivo.Tests` 105, `Orynivo.Server.Tests` 38).

## Completed: the Avalonia 12 migration

Avalonia moved from 11.3.22 to **12.1.2** in one commit, together with SkiaSharp
2.88.9 to **3.119.4** (Avalonia.Skia 12.1.2 depends on that line). What changed:

1. Every Avalonia package moved together, including
   `Avalonia.Controls.DataGrid`. The earlier note that DataGrid "has no release
   beyond 11.3.13" was wrong: DataGrid ships 12.1.2 again and is not abandoned.
2. `Avalonia.Diagnostics` was dropped. It has no 12.x release, the app never
   called `AttachDevTools`, and the Debug-only reference was therefore unused.
   The official successor for a future DevTools bridge is
   `AvaloniaUI.DiagnosticsSupport`.
3. SkiaSharp 3 removed the 2.88 text and sampling APIs. `SKPaint` no longer
   carries `TextSize`, `Typeface`, `FakeBoldText`, `FilterQuality`, or
   `MeasureText`; text uses `SKFont` (`Embolden`, `MeasureText`) and drawing
   takes `SKSamplingOptions`. `SKCanvas.DrawText` now needs the font, and a
   scaled bitmap is drawn with `DrawImage(..., SKSamplingOptions, paint)`.
   `SKFilterQuality.High` became
   `new SKSamplingOptions(SKCubicResampler.Mitchell)`.
4. Avalonia 12 deprecations that fail the `--warnaserror` build were fixed:
   `TextBox.Watermark` became `PlaceholderText` (8 sites) and
   `Window.SystemDecorations` became `WindowDecorations`.
5. `IClipboard.SetTextAsync` was replaced by the data-transfer model
   (`clipboard.SetDataAsync(DataTransfer)`), and `DragDrop.DoDragDropAsync` now
   requires the originating `PointerPressedEventArgs` rather than the move
   event.
6. **Bindings stay on the reflection mode for now.**
   `AvaloniaUseCompiledBindingsByDefault=false` keeps `{Binding}` working;
   Avalonia 12 otherwise compiles bindings and requires an explicit `x:DataType`
   on every template and root, which the existing views do not carry (232
   compiler diagnostics across 8 files). Adopting compiled bindings is the
   recorded follow-up below.

Verification: clean Debug and Release builds with `--warnaserror` (0 errors, 0
warnings) and `scripts/verify-all.ps1` green, with 570 tests passing.

**Runtime verification.** A Windows smoke test of the Release build in
`Orynivo/bin/Release/net10.0-windows10.0.19041.0` found no obvious defects. That
build was confirmed to carry `Avalonia*` 12.1.2, `Avalonia.Controls.DataGrid`
12.1.2, `SkiaSharp` 3.119.4, and `Microsoft.Data.Sqlite` 10.0.12, so it
exercises both this migration and the .NET 10 one.

The pass covered the library tables, playback and the transport, the Dashboard
(including the cover stage), the Genre Cloud (including the background mosaic),
the search and detail views, Settings, and the AI chat, on Windows.

Still outstanding:

- The Linux and macOS builds, including direct ALSA and OpenAL PCM output,
  native DSD/DoP, and MPRIS.
- Native ASIO and cwASIO playback on Windows (the Steinberg bridge is not part
  of the CI artifact).
- macOS rendering. Avalonia 12 changes the EGL/OpenGL handling, so the
  documented OpenGL-first/software-second workaround must be re-checked rather
  than assumed to still apply.
- Features a general pass does not reach: drag and drop into Up Next, the
  year-in-review PNG and PDF export, the fullscreen karaoke view, the output and
  equalizer profile dialogs, remote Orynivo Server and Plex playback, MCP and AI
  tool execution, cross-device resume, and the WebDAV backup upload.

## Completed: compiled bindings

Avalonia 12 compiles bindings by default, so every binding scope now carries an
explicit `x:DataType` and `AvaloniaUseCompiledBindingsByDefault=false` is gone.
The migration produced 232 compiler diagnostics across eight files and resolved
them as follows:

1. `DataTemplate`s and grid columns got the item type: `ContentRow` for the
   shared cards,
   `RadioStationViewModel`/`PodcastViewModel`/`PodcastEpisodeViewModel` for the
   catalogs, `DailyHistoryRow` for the history grid,
   `MetadataProblemRow`/`MetadataRepairTrack`/`MetadataRepairPreviewRow` for the
   metadata views, `LyricLineViewModel` for the lyrics list, `TrackInfoEntry`
   for the track information dialog, `EqualizerProfile` and `OutputProfile` for
   the transport pickers.
2. The item type goes on the **column or template**, not on the `DataGrid`
   itself: a grid-level directive also applies to the grid's own
   `ItemsSource`/`IsVisible` bindings and breaks them, and
   `DataGridTemplateColumn` cell templates do not inherit it.
3. Ten view models moved out of their window or view into top-level types,
   because XAML cannot name a nested type: `RadioStationViewModel`,
   `PodcastViewModel`, `PodcastEpisodeViewModel`, `LyricLineViewModel` (out of
   `MainWindow`), `DailyHistoryRow` (out of `DailyHistoryDialog`),
   `MetadataProblemRow` (out of `SettingsView`), `MetadataRepairHeaderViewModel`
   and `MetadataRepairPreviewRow` (out of `MetadataRepairDialog`, the former
   replacing an anonymous type), `TrackInfoEntry` (out of `TrackInfoDialog`),
   and the three search-window result view models. The helpers they call became
   `internal static`.
4. `ContentRow` is still a nested private type, so the scopes that bind it
   (`AlbumArtworkCardTemplate`, the artist artwork card, the artist-info track
   table, and the podcast and album hero cards whose `DataContext` is assigned
   to a row in code) used explicit `{ReflectionBinding}`; they were converted
   when `ContentRow` was extracted.

Verification: clean Debug and Release builds with `--warnaserror` (0 errors, 0
warnings) and `scripts/verify-all.ps1` green, with 570 tests passing.

## Completed: `ContentRow` extraction

`ContentRow` (287 lines, 77 members) and its `LogicalAlbumPart` companion moved
out of `MainWindow.xaml.cs` into `ContentRow.cs` and `LogicalAlbumPart.cs` as
top-level `internal` types, with English XML documentation for every member; 69
members gained a summary. `MainWindow.LocalSourceKey` and
`MainWindow.GetServerSourceKey` became `internal static` so the row model can
still build its source key and badge.

The scopes that bind a row (`AlbumArtworkCardTemplate`, the `AlbumDetailHeader`
whose `DataContext` is assigned in code, and the artist artwork card) now carry
`x:DataType="local:ContentRow"`, and every `{ReflectionBinding}` in the views is
back to `{Binding}`. There is no reflection binding left in the XAML.

Verification: clean Debug and Release builds with `--warnaserror` (0 errors, 0
warnings) and `scripts/verify-all.ps1` green, with 570 tests passing.

## `Avalonia.Controls.DataGrid`

`Avalonia.Controls.DataGrid` ships with the Avalonia 12 line (12.1.2) and is
therefore no longer a pin. An earlier revision of this record claimed it was in
maintenance mode with no release beyond 11.3.13 and proposed evaluating
`TableView` or `TreeDataGrid` as a replacement; that was wrong. The shared
tables keep using `DataGrid`, including the custom `ControlTheme` templates, the
column chooser, the column order and width stores, the
`DataGridSortIconMinWidth` override, and the pixel-based
`PART_VerticalScrollbar` handling. Re-evaluate the successor controls only if a
future Avalonia line changes or removes `DataGrid`.

## SkiaSharp line

SkiaSharp is on `3.119.4`, matching `Avalonia.Skia` 12.1.2. A future Avalonia
release that moves to SkiaSharp 4.x repeats the same procedure: bump `SkiaSharp`
in `Orynivo.Core` and `SkiaSharp.NativeAssets.Linux.NoDependencies` in
`Orynivo.Server` in the same commit as the Avalonia packages, replace the APIs
the new line removed, keep the desktop free of a separate SkiaSharp reference,
and verify the Linux packages still ship the native library.

**Checks before merging:** `scripts/verify-all.ps1` green; a manual pass over
album/artist artwork generation, thumbnails, and the year-in-review PNG and PDF
export on Windows and Linux.

## Visualizer GPU shading surface

**Trigger:** the visualizer's GPU phase (roadmap 40a). Fullscreen resolution and
high frame rates are not affordable on the CPU, so the preset engine needs a GPU
path.

**Decision:** Avalonia's own Skia surface through `SKRuntimeEffect` and SkSL,
not an own OpenGL or Vulkan surface.

**Why:** `SkiaSharp` is already referenced by `Orynivo.Core` (and by
`Orynivo.Server`), so the GPU path adds no dependency to any platform, needs no
second windowing integration, and stays testable headlessly because the same
code runs in the test projects. An own surface such as Silk.NET was rejected for
now: it would add a native dependency to every platform, require its own context
and swapchain handling next to Avalonia's, and could not be verified without a
GPU in CI.

**Risks:**

1. SkSL is stricter than the HLSL the presets are written in. Measured: `while`
   is rejected and must become a counted `for` with `break`, loops are unrolled
   so a translated loop is bounded (`MaxTranslatedIterations`), typing is strict
   with no implicit scalar-to-vector conversion, and there is a program-size
   limit. Only presets that translate and compile take the GPU path; the
   measured share is recorded in `CHANGELOG.md`.
2. A future Skia major line may change the dialect. The emitter is therefore
   kept emitter-agnostic: `ShaderTranspiler` builds an intermediate text from
   the parsed tree, so a GLSL emitter can replace the SkSL emitter without
   touching the parser, the interpreter, or the renderer.
3. GPU availability and driver quality vary. The CPU interpreter remains the
   reference implementation and the fallback, and every translation change is
   checked against it by the CPU/GPU comparison tests.

**Fallback rule:** a preset whose shader cannot be translated, fails to compile,
or exceeds a pass budget keeps the CPU path for that preset. The CPU path is
never removed. A coverage change is only accepted while the CPU/GPU comparison
tests stay green, so the two paths cannot silently diverge.

**Checks before merging:** the CPU/GPU comparison tests green; `verify-all.ps1`
green in Debug and Release; the translation coverage measured against the preset
collection and recorded in `CHANGELOG.md`.

## `Microsoft.Data.Sqlite` and the test SDK

**Trigger:** a deliberate decision to move the toolchain forward. Both lines
already match the .NET 10 toolchain, so only a major bump remains.

**Steps:**

1. Evaluate the newer line against `net10.0` first; a line that needs an even
   newer runtime has to wait for the next LTS migration.
2. Bump `Microsoft.Data.Sqlite` in `Orynivo.Core` and re-run every database test
   through `Orynivo.Core.Tests.CoreTestDatabase`.
3. Bump `Microsoft.NET.Test.Sdk` in all three test projects together and re-run
   all of them.

**Checks before merging:** `scripts/verify-all.ps1` green, with all three test
projects executing.

## How to keep this record current

- When a pin is raised, update the table and remove the now-obsolete section.
- When a new pin is added, add its trigger, steps, and checks here and link it
  from the Dependabot comment block in `.github/dependabot.yml`.
- Re-check the toolchain support window whenever the .NET release cadence
  publishes a new LTS line.
- Keep the Dependabot `ignore` list and this file in agreement; a pin without a
  recorded reason is a bug.
