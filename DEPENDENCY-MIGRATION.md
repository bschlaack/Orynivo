# Dependency migration plan

This is the decision record for the dependency lines that are intentionally held
back by `.github/dependabot.yml`. Dependabot ignores those **major** upgrades so
they cannot land as an unreviewed automatic bump; this file records what each pin
is waiting for and how to migrate it deliberately.

Nothing here is a permanent constraint. Each section names the concrete trigger
that unblocks the upgrade, the migration steps, and the checks that must pass
before the pin is raised.

## Toolchain support window

The desktop targets `net8.0`. Per the Microsoft .NET support policy, the relevant
lines are:

| Version | Type | Phase | End of support |
| --- | --- | --- | --- |
| .NET 10 | LTS | Active | 14 November 2028 |
| .NET 9 | STS | Maintenance (security fixes only) | 10 November 2026 |
| .NET 8 | LTS | Maintenance (security fixes only) | 10 November 2026 |

**Target `net10.0`, not `net9.0`.** .NET 9 is already in its security-only
maintenance phase and reaches end of support on the same day as .NET 8, so moving
to it would be a dead end. The next LTS release is the only sensible destination.
The `net8.0` line this repository currently uses stops receiving fixes on
10 November 2026, so the migration below is time-boxed rather than optional.

Re-check these dates against the .NET support policy before planning the work; the
table is a snapshot, not a permanent fact.

## Current state

| Dependency | Current | Pinned because | Blocked by |
| --- | --- | --- | --- |
| .NET runtime and SDK | `net8.0` / `net8.0-windows10.0.19041.0` | The Avalonia line in use needs the .NET 8 toolchain | The .NET 10 LTS migration (see below) |
| Avalonia | `11.3.22` (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Fonts.Inter`) | Avalonia 12 needs a newer SDK than `net8.0` and has breaking API changes | The .NET 10 LTS migration (see below) |
| `Avalonia.Themes.Fluent`, `Avalonia.Controls.DataGrid`, `Avalonia.Diagnostics` | `11.3.13` | `Avalonia.Controls.DataGrid` is in upstream maintenance mode and has no release beyond 11.3.13 | No upstream release is planned; evaluate the successor controls |
| SkiaSharp | `2.88.9` (`Orynivo.Core`, plus `SkiaSharp.NativeAssets.Linux.NoDependencies` in `Orynivo.Server`) | `Avalonia.Skia` 11.3 depends on SkiaSharp `2.88.9` | An Avalonia release on a SkiaSharp 3/4 line |
| `Microsoft.Data.Sqlite` | `9.0.20` | The `10.x` line targets a newer runtime than `net8.0` | The .NET 10 LTS migration |
| `Microsoft.NET.Test.Sdk` | `17.14.1` | The `18.x` line targets a newer toolchain | A deliberate evaluation with all three test projects |
| `Tmds.DBus.Protocol` | `0.95.1` | Linux-only; `0.92.0` is the documented floor | None (minor/patch updates are welcome) |

Target frameworks today: `Orynivo` and `Orynivo.Tests` use
`net8.0-windows10.0.19041.0` on Windows and `net8.0` elsewhere;
`Orynivo.Core`, `Orynivo.Server`, `Orynivo.Core.Tests`, and
`Orynivo.Server.Tests` use `net8.0`.

## Moving to .NET 10 LTS and Avalonia 12

**Trigger:** the .NET 10 SDK (LTS) available on every build and release runner,
plus an Avalonia 12 release whose breaking changes are documented. Confirm the
exact minimum .NET version Avalonia 12 requires in its release notes before
choosing the SDK; the repository's Dependabot rules only record that it is newer
than .NET 8.

**Steps:**

1. Install and pin the .NET 10 SDK in `.github/workflows/dotnet-desktop.yml`,
   `.github/workflows/release.yml`, `.github/workflows/server-release.yml`, and
   `.github/workflows/player-linux-release.yml`; drop the .NET 8 SDK once no
   project targets `net8.0` any more.
2. Change every `TargetFramework` together. A mixed `net8.0`/`net10.0` set makes
   the `Orynivo` desktop project reference a `net10.0` `Orynivo.Core` from a
   `net8.0` target, which does not build.
3. Bump every Avalonia package in one commit, including
   `Avalonia.Controls.DataGrid` (see below), and keep them on one version.
4. Check which SkiaSharp line Avalonia 12 pulls in. If it moved to 3.x/4.x, fold
   the SkiaSharp migration into the same commit (see below).
5. Work through the Avalonia 12 breaking changes. The areas that historically
   needed work here are drag and drop (`IDataTransfer`/`DataTransfer`), the
   `DataGrid` theming and `ControlTheme` templates, `Popup`/`Flyout` placement,
   `Transitions` and `RenderTransform` animation APIs, and the Skia render
   interface.
6. Re-check the `App.axaml` control themes, the transport and table styles, the
   `VirtualizingWrapPanel`, and the macOS `AvaloniaNativePlatformOptions`
   rendering mode. Avalonia 12 changes the EGL/OpenGL handling on Linux and macOS,
   so the documented OpenGL-first/software-second macOS workaround must be
   re-evaluated rather than carried over blindly.
7. Fold in the `Microsoft.Data.Sqlite` and `Microsoft.NET.Test.Sdk` lines that are
   currently pinned only because they need a newer runtime.
8. Update `AGENTS.md`, `README.md`, `CHANGELOG.md`, and this file.

**Checks before merging:** `scripts/verify-all.ps1` green; a manual pass over
playback, the library tables, drag and drop into Up Next, the Dashboard, the
Genre Cloud, and the artwork grids on Windows and Linux.

## `Avalonia.Controls.DataGrid` on 11.3.13 and the successor question

`Avalonia.Controls.DataGrid` is in upstream **maintenance mode** and has no
release beyond 11.3.13, so waiting for a newer DataGrid is not a plan. This
repository uses DataGrid heavily (shared Tracks/Albums/Artists tables, playlist
and queue tables, nested album-detail grids) with custom `ControlTheme`
templates, the column chooser, column reordering, and direct
`PART_VerticalScrollbar` handling, so a replacement is a real project rather than
a package bump.

**Trigger:** either a DataGrid release on the same line as the other Avalonia
packages, or a decision to evaluate a successor control.

**Steps:**

1. If a matching DataGrid release appears, bump it together with
   `Avalonia.Themes.Fluent` and the Debug-only `Avalonia.Diagnostics`, then verify
   the mixed set still builds with `--warnaserror`.
2. Otherwise evaluate the successors against the shared table requirements:
   `TableView` (free, read-only columns, row and cell recycling, resizable
   columns) and `TreeDataGrid` (Avalonia Pro). Decide whether one of them can own
   the shared table surfaces before writing a migration.
3. Confirm the `DataGridSortIconMinWidth` override, the per-view column width and
   order stores, the column chooser flyout, and the pixel-based scroll handling
   have an equivalent in the chosen control.
4. Migrate one surface first (the shared Tracks table is the best candidate) and
   keep the DataGrid path until every table has moved.

**Checks before merging:** `scripts/verify-all.ps1` green plus a manual pass over
the shared Tracks/Albums/Artists tables, the column chooser, column reordering,
row selection and double-click playback, and the A-Z index.

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
together with the .NET 10 LTS migration.

**Steps:**

1. Evaluate the newer line against `net8.0` first; if it requires a newer
   runtime, fold it into the .NET 10 migration.
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
