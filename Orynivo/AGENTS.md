# Orynivo Windows Client Instructions

This file applies to the Windows Avalonia client under `Orynivo/` and supplements
the repository-wide `../AGENTS.md`.

## Completion

- Follow the root mandatory completion checklist. In particular, feature and UI
  changes require `CHANGELOG.md` and usually `README.md`; architectural or
  behavioral changes require this file or the root `AGENTS.md` to be updated.
- Build with `dotnet build Orynivo/Orynivo.csproj` after client changes.
- New visible text must use `LocalizationManager` and exist in German, English,
  French, and Spanish.
- Add or update English XML documentation for affected public/internal members.

## Client Invariants

- `MainWindow` remains one partial Avalonia class; use the existing domain-sized
  partials instead of creating competing window state or navigation models.
- Do not block the UI thread with database access, network requests, FFmpeg,
  device enumeration, player disposal, large cache I/O, or large row composition.
- Preserve source identity on mixed rows. Remote rows must carry their
  `OrynivoServer`, server-side IDs, and authenticated playback metadata; never
  persist credential-bearing URLs.
- Shared local/remote Artists, Albums, and Tracks views use the common column
  masks and catalog abstractions. Do not create parallel remote-only UI surfaces.
- Matching local and Orynivo Server artists use
  `ArtistNameNormalizer.CreateComparisonKey` and one `UnifiedArtist` row. Its
  album drill-down combines every matching library while retaining each album's
  source context. Every non-Plex artist navigation entry point must use that
  unified drill-down even when the clicked track or row came from only one
  source. Plex identities remain separate.
- Navigation state must distinguish local, remote, Plex, and unified drill-downs;
  numeric IDs from different sources can collide.
- Keep long mixed-library row composition off the visible `DataGrid` until the
  result is complete, unless a proven virtualized/paged strategy is used.
- Use shared typography, brushes, vector icons, control themes, loading helpers,
  and context-menu patterns from the existing application resources.
- Interactive cards use the shared cyan-violet gradient hover border. Main
  sidebar entries carry a source-appropriate shared vector icon; smart playlists
  use the shared 13-px icon footprint and spacing but retain a dedicated orange
  vector lightning icon for emphasis.
- Dashboard Recently Played and Recently Added use 20-item horizontal
  carousels with smoothly animated vector previous/next controls placed in the
  header immediately before Show all; controls must never overlay the cards or
  change visibility. At either end they remain reserved, disabled, and visually
  muted so the header layout cannot shift. Keep a clear gap before Show all.
  Their Show all views contain up to 100 items.
- Dashboard favorite counters must use the same currently resolvable local and
  Orynivo Server track set as the unified Favorites view; never count raw remote
  favorite keys from settings without validating current facets and track rows.
- The four Dashboard overview cards use an edge-aligned equal-width/equal-height
  grid. Listening trends use daily points for short/current-month periods,
  monthly points for the year, no more than seven X-axis labels, and a rounded
  Y-axis ceiling strictly above the smoothed peak. Each actual point retains a
  hover tooltip with its localized date and listened-minute value. Preserve the
  chart geometry's origin anchor so `Stretch.Fill` maps values against the full
  configured Y-axis range rather than the curve's own bounds. Clamp smoothed
  Bézier control points to each segment's endpoint range to prevent overshoot.
  Recently Played cards must use the centralized `motionCard` border styles and
  must not replace the gradient through pointer-event assignments.
- Audio routing invariants remain: native ASIO/cwASIO DSD is bit-perfect; volume,
  ReplayGain, PCM boost, and equalization affect PCM paths only.
- Windows-specific code stays in this project; cross-platform behavior belongs
  in `Orynivo.Core`.

Consult the detailed matching sections in the root `AGENTS.md` before changing
audio, queue, Dashboard, playlists, remote libraries, settings, or table/tree UI.
