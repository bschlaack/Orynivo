# Dependency migration plan

This is the decision record for the dependency lines that are intentionally held
back by `.github/dependabot.yml`. Dependabot ignores those **major** upgrades so
they cannot land as an unreviewed automatic bump; this file records what each pin
is waiting for and how to migrate it deliberately.

Nothing here is a permanent constraint. Each section names the concrete trigger
that unblocks the upgrade, the migration steps, and the checks that must pass
before the pin is raised.

## Current state

| Dependency | Current | Pinned because | Blocked by |
| --- | --- | --- | --- |
| Avalonia | `11.3.22` (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Fonts.Inter`) | Avalonia 12 needs the .NET 9 SDK and has breaking API changes while the desktop targets `net8.0` | .NET 9 SDK migration (see below) |
| `Avalonia.Themes.Fluent`, `Avalonia.Controls.DataGrid`, `Avalonia.Diagnostics` | `11.3.13` | `Avalonia.Controls.DataGrid` has no release beyond 11.3.13 | An upstream DataGrid release on a newer line |
| SkiaSharp | `2.88.9` (`Orynivo.Core`, plus `SkiaSharp.NativeAssets.Linux.NoDependencies` in `Orynivo.Server`) | `Avalonia.Skia` 11.3 depends on SkiaSharp `2.88.9` | A SkiaSharp 3/4-based Avalonia release |
| `Microsoft.Data.Sqlite` | `9.0.20` | The `10.x` line targets a newer runtime than `net8.0` | A deliberate `net8.0`-compatible evaluation |
| `Microsoft.NET.Test.Sdk` | `17.14.1` | The `18.x` line targets a newer toolchain | A deliberate evaluation with all three test projects |
| `Tmds.DBus.Protocol` | `0.95.1` | Linux-only; `0.92.0` is the documented floor | None (minor/patch updates are welcome) |

Target frameworks today: `Orynivo` and `Orynivo.Tests` use
`net8.0-windows10.0.19041.0` on Windows and `net8.0` elsewhere;
`Orynivo.Core`, `Orynivo.Server`, `Orynivo.Core.Tests`, and
`Orynivo.Server.Tests` use `net8.0`.

## Moving to Avalonia 12 / .NET 9

**Trigger:** a supported .NET 9 SDK (Roslyn 4.14 or newer) on every build and
release runner, and an Avalonia 12 release whose breaking changes are documented.

**Steps:**

1. Install and pin the .NET 9 SDK in `.github/workflows/dotnet-desktop.yml`,
   `.github/workflows/release.yml`, `.github/workflows/server-release.yml`, and
   `.github/workflows/player-linux-release.yml`; keep the .NET 8 SDK only if a
   project still targets `net8.0`.
2. Change every `TargetFramework` together. A mixed `net8.0`/`net9.0` set makes
   the `Orynivo` desktop project reference a `net9.0` `Orynivo.Core` from a
   `net8.0` target, which does not build.
3. Bump every Avalonia package in one commit, including
   `Avalonia.Controls.DataGrid` (see below), and keep them on one version.
4. Work through the Avalonia 12 breaking changes. The areas that historically
   needed work here are drag and drop (`IDataTransfer`/`DataTransfer`), the
   `DataGrid` theming and `ControlTheme` templates, `Popup`/`Flyout` placement,
   `Transitions` and `RenderTransform` animation APIs, and the Skia render
   interface.
5. Re-check the `App.axaml` control themes, the transport and table styles, the
   `VirtualizingWrapPanel`, and the macOS `AvaloniaNativePlatformOptions`
   rendering mode.
6. Update `AGENTS.md`, `README.md`, `CHANGELOG.md`, and this file.

**Checks before merging:** `scripts/verify-all.ps1` green; a manual pass over
playback, the library tables, drag and drop into Up Next, the Dashboard, the
Genre Cloud, and the artwork grids on Windows and Linux.

## Unblocking `Avalonia.Controls.DataGrid` beyond 11.3.13

**Trigger:** an `Avalonia.Controls.DataGrid` release on the same line as the
other Avalonia packages (currently any 11.3 release after 11.3.13, or the
Avalonia 12 line).

**Steps:**

1. Bump `Avalonia.Controls.DataGrid` (and, while on the 11.3 line,
   `Avalonia.Themes.Fluent` and the Debug-only `Avalonia.Diagnostics`) to the
   newest published 11.3 patch.
2. If the other Avalonia packages already moved ahead within 11.3, verify the
   mixed set still builds with `--warnaserror` and that the DataGrid themes,
   column chooser, and drag-and-drop reordering behave unchanged.
3. Confirm the `DataGridSortIconMinWidth` override and the direct
   `PART_VerticalScrollbar` handling still work.

**Checks before merging:** `scripts/verify-all.ps1` green plus a manual pass over
the shared Tracks/Albums/Artists tables, the column chooser, column reordering,
and the A-Z index.

## Revisiting the SkiaSharp 2.88.9 pin

**Trigger:** an Avalonia release that depends on a SkiaSharp 3.x or 4.x line.

**Steps:**

1. Bump `SkiaSharp` in `Orynivo.Core` and
   `SkiaSharp.NativeAssets.Linux.NoDependencies` in `Orynivo.Server` in the same
   commit as the Avalonia packages, so the desktop never renders through an
   incompatible managed/native Skia pair.
2. Replace the removed 2.88-era APIs. `SKFilterQuality` was dropped in 3.x; the
   remaining uses are in the artwork thumbnail generation and the year-in-review
   PDF export.
3. Keep the desktop free of a separate `SkiaSharp` package reference: it must
   consume Skia only through `Avalonia.Skia`, as `YearInReviewPdfExporter` does.
4. Verify the Linux packages still ship the native Skia library through
   `SkiaSharp.NativeAssets.Linux.NoDependencies`; do not replace it with an
   external ImageMagick/convert runtime dependency.

**Checks before merging:** `scripts/verify-all.ps1` green; a manual pass over
album/artist artwork generation, thumbnails, and the year-in-review PNG and PDF
export on Windows and Linux.

## `Microsoft.Data.Sqlite` and the test SDK

**Trigger:** a deliberate decision to move the toolchain forward, normally
together with the .NET 9 migration.

**Steps:**

1. Evaluate the newer line against `net8.0` first; if it requires a newer
   runtime, fold it into the .NET 9 migration.
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
- Keep the Dependabot `ignore` list and this file in agreement; a pin without a
  recorded reason is a bug.
