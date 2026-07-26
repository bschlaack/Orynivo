# Orynivo Desktop Client Instructions

This file applies to the Windows, Linux, and macOS Avalonia desktop client under
`Orynivo/` and supplements the repository-wide `../AGENTS.md`.

## Completion

- Follow the root mandatory completion checklist. In particular, feature and UI
  changes require `CHANGELOG.md` and usually `README.md`; architectural or
  behavioral changes require this file or the root `AGENTS.md` to be updated.
- Build with `dotnet build Orynivo/Orynivo.csproj` after client changes. Linux
  and macOS compile the `net8.0` compatibility build; Windows continues to
  target `net8.0-windows10.0.19041.0`.
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
- The shared Folder structure sidebar item is visible when either local media
  or at least one Orynivo Server is configured. Server-only setups must be able
  to open `ShowUnifiedFolderTreeAsync` without configuring a local directory.
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
  and require an explicit choice before the platform installer continues.
- Tagged macOS releases publish architecture-specific `osx-arm64` and
  `osx-x64` application bundles as installable PKGs, portable ZIPs, and tar
  archives. The PKG installs `Orynivo.app` beneath `/Applications`; all package
  variants must remain required inputs to the signed release manifest. Each
  bundle must contain `Contents/Resources/Orynivo.icns` generated from
  `Logo/icon.png` and referenced by `CFBundleIconFile` in `Info.plist`. macOS
  automatic updates select the current architecture's signed PKG, verify its
  digest through `ReleaseUpdateService`, and open the verified file through
  `/usr/bin/open`; do not invoke `installer` or request privileges directly.
- Settings must hide Steinberg ASIO and cwASIO subsystem badges on non-Windows
  platforms. FFmpeg and other genuinely cross-platform subsystem badges remain
  visible.
- `AppSettings.ShowAiChatItem` controls the AI Chat sidebar item's visibility
  from Settings > Appearance, defaults to visible, and is applied with the
  existing Internet Radio, Podcasts, and Up Next item toggles.
- macOS must configure `AvaloniaNativePlatformOptions.RenderingMode` with
  OpenGL first and software second. Do not re-enable Metal without verifying
  Orynivo's gradient and rounded-surface shaders on both Intel and Apple
  Silicon Macs; Skia's Metal compiler can reject them after its 300-ms timeout.
- FFmpeg discovery on macOS must not rely only on the inherited `PATH`, because
  Finder-launched bundles omit common package-manager prefixes. Probe the
  application and per-user cache plus conventional Homebrew, MacPorts, pkgsrc,
  Fink, and per-user binary directories. When absent, download current-architecture
  FFmpeg and FFprobe release assets into the per-user cache and restore executable
  Unix permissions before prepending that cache to the process `PATH`.
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
- The persisted DoP preference and forced DSD-to-PCM conversion are mutually
  exclusive. DoP is bit-perfect encapsulation and must bypass PCM gain and
  equalizer processing. Linux local and HTTP-range-streamed stereo DSF DoP uses
  `Compatibility/Linux/DsfDopAudioPlayer` and
  `Compatibility/Linux/RemoteDsfDopAudioPlayer` with direct ALSA only; preserve
  their alternating marker bytes and DSD-rate/16 carrier-rate calculation.
  Seeking must serialize ALSA `drop`/`prepare` against writes, discard blocks
  read before the seek generation changed, and restart markers with `0x05`.
  ALSA `S32_LE` DoP frames place low padding first, then two DSD bytes, and the
  marker in the most-significant byte. Prefer ALSA `DSD_U32_BE` at DSD-rate/32
  when the selected hardware endpoint supports it; use DoP as the fallback.
  DSF files whose format chunk declares `bitsPerSample == 1` store each DSD byte
  LSB-first; reverse every payload byte before either native ALSA or DoP output.
  Files declaring `bitsPerSample == 8` are already MSB-first.
  `Compatibility/Linux/DffDopAudioPlayer` handles local and HTTP-range-streamed
  uncompressed stereo DFF. DFF payload is already interleaved and MSB-first:
  regroup successive per-channel bytes into ALSA U32 words without bit reversal;
  retain the same serialized seek/write and native-first/DoP-fallback rules.
- Local `cue://` tracks and `mka://chapter/` tracks both resolve through the
  shared segment-aware PCM playback, waveform, queue, and history paths.
- Windows-specific code stays in this project and must be excluded from the
  Linux and macOS targets in `Orynivo.csproj`. The compatibility types currently
  live under `Compatibility/Linux`; shared non-Windows PCM playback uses OpenAL,
  loading `libopenal.so.1` on Linux and the system OpenAL framework on macOS.
  Linux additionally enumerates direct ALSA `hw:` devices
  through `libasound.so.2` and selectable OpenAL devices through
  `libopenal.so.1`. The output-profile dialog presents these as separate output
  types and classifies persisted profiles by their device ID (`alsa:` means
  direct ALSA); never mix direct ALSA hardware into the OpenAL device list.
  Direct ALSA uses stereo signed 32-bit PCM, the source rate,
  and `soft_resample=0`; failure to open that exact format/rate must be reported
  rather than silently falling back to resampling. An `EBUSY` open failure must
  identify PipeWire/other-process ownership and explain that changing the
  desktop default does not release the card. The shared device-information
  window must use ALSA/OpenAL terminology on Linux and must not expose its
  historical WASAPI labels there. OpenAL requests the source rate when
  creating the context, then queries the negotiated OpenAL mixer rate and uses
  that value for FFmpeg decoding and transport output-rate reporting. Compatibility
  types must not persist
  credentials in plaintext or claim unavailable WASAPI, ASIO, endpoint-volume,
  or SMTC capabilities.
  Cross-platform behavior shared with the server belongs in `Orynivo.Core`.
- Keep the Linux-only direct `Tmds.DBus.Protocol` dependency at 0.92.0 or newer
  within the compatible package line: its non-blocking observer dispatch avoids
  a shutdown race with Avalonia's stopped UI dispatcher.

Consult the detailed matching sections in the root `AGENTS.md` before changing
audio, queue, Dashboard, playlists, remote libraries, settings, or table/tree UI.
