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
- Playlist context actions for local and Orynivo Server rows use the shared local
  mixed-playlist list. Remote selections retain playable URLs only for queue
  actions and persist stable `orynivo://serverId/track/trackId` references;
  hidden legacy server playlists must not be offered by these menus.
- Shared local/remote Artists, Albums, and Tracks views use the common column
  masks and catalog abstractions. Do not create parallel remote-only UI surfaces.
- Matching local and Orynivo Server artists use
  `ArtistNameNormalizer.CreateComparisonKey` and one `UnifiedArtist` row. Its
  album drill-down combines every matching library while retaining each album's
  source context. Every non-Plex artist navigation entry point must use that
  unified drill-down even when the clicked track or row came from only one
  source. The unified row selects available biography and artwork from any
  matching identity. Profile downloads and manual image selections propagate to
  every matching local and reachable Orynivo Server identity; automatic profile
  images must never overwrite a manually selected image. Plex identities remain
  separate.
- Navigation state must distinguish local, remote, Plex, and unified drill-downs;
  numeric IDs from different sources can collide.
- Keep long mixed-library row composition off the visible `DataGrid` until the
  result is complete, unless a proven virtualized/paged strategy is used.
- Use shared typography, brushes, vector icons, control themes, loading helpers,
  and context-menu patterns from the existing application resources.
- Programmatically created confirmation dialogs use a restrained accent-soft
  primary action with an accent border/text and explicitly centered content;
  localized button labels must size through padding and minimum width rather
  than fixed widths.
- Main-window placement is persisted only from the normal state. When maximized
  startup is disabled, validate the saved rectangle against current screens and
  center the window if its previous monitor is no longer attached.
- Interactive cards use the shared cyan-violet gradient hover border. Main
  sidebar entries carry a source-appropriate shared vector icon; smart playlists
  use the shared 13-px icon footprint and spacing but retain a dedicated orange
  vector lightning icon for emphasis.
- Non-Dashboard hero and intro surfaces use the normal card background and the
  shared cyan-violet highlight gradient as their persistent border. They use a
  consistent 14-px radius on all four corners. The artwork-backed Dashboard
  greeting hero remains visually distinct. Compact intro heroes reuse their
  matching sidebar vector icon inside a shared circular, accent-tinted badge.
- About reads the build-time informational version and performs desktop updates
  only from a correctly signed GitHub Release manifest. Settings may relay the
  matching verified DEB/RPM package to an update-enabled Orynivo Server; both
  client and server must verify it, and unsigned fallback is forbidden.
  `AppSettings.CheckForUpdatesOnStartup` controls the optional background check
  after the main window opens; it may notify about a newer signed desktop
  release but must not download or install it without an explicit user action.
  The startup update notification offers that explicit action and then reuses
  the About window's verified download, server-relay, and installer flow.
  Server-update HTTP rejections must expose their status code in Settings rather
  than being collapsed into the "no update" state.
  Starting a desktop update from About first relays the matching signed release
  to every reachable update-enabled configured server. Failed servers are named
  and require an explicit choice before the Windows installer continues.
- Dashboard Recently Played and Recently Added use 20-item horizontal
  carousels with smoothly animated vector previous/next controls placed in the
  header immediately before Show all; controls must never overlay the cards or
  change visibility. At either end they remain reserved, disabled, and visually
  muted so the header layout cannot shift. Keep a clear gap before Show all.
  Their Show all views contain up to 100 items.
- Dashboard favorite counters must use the same currently resolvable local and
  Orynivo Server track set as the unified Favorites view; never count raw remote
  favorite keys from settings without validating current facets and track rows.
- Dashboard album and track counters represent the same local-plus-Orynivo
  Server row sets as the shared Albums and Tracks views. Use the remote
  `/api/library/summary` aggregate endpoint, with lightweight-list fallbacks for
  older servers. Artist totals must merge local and remote names through
  `ArtistNameNormalizer.CreateComparisonKey`, matching the shared Artists view.
- Dashboard hero summary badges reuse the shared album, track, artist, and
  favorite navigation vectors while retaining their individual tinted circles.
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
- Local `cue://` tracks and `mka://chapter/` tracks both resolve through the
  shared segment-aware PCM playback, waveform, queue, and history paths.
- Windows-specific code stays in this project; cross-platform behavior belongs
  in `Orynivo.Core`.

Consult the detailed matching sections in the root `AGENTS.md` before changing
audio, queue, Dashboard, playlists, remote libraries, settings, or table/tree UI.
