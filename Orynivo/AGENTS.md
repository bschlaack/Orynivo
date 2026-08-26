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
- `ApplicationCredentialStore` is the only persistent client credential
  container. It uses current-user DPAPI on Windows and AES-GCM plus a
  current-user-only random key file on Linux/macOS. Last.fm, Fanart.tv,
  AI Chat, Orynivo Server, Plex, and generic streaming secrets must remain
  `[JsonIgnore]` in JSON settings models and must not appear in caches, logs,
  server payloads, diagnostics, documentation examples, or model context.
- Fanart.tv artist artwork uses the encrypted Settings value or the
  `FANART_TV_API_KEY` environment variable. The environment variable remains
  an optional runtime override and is never copied into persistent settings.
- The Artist information Settings action for missing artist images combines the
  local library and every configured Orynivo Server, runs strictly sequentially,
  skips every manually selected image, tries Fanart.tv first only when a key is
  available, then falls back to Wikimedia Commons, and remains cancellable.
  When an API key is configured, the preview may enable automatic acceptance
  for Fanart.tv results for the current run only. This must never automatically
  accept Wikimedia results. Other candidates stay in memory until the user
  accepts or rejects their preview, and cancelling the preview cancels the
  complete run; accepted remote images are uploaded only to their owning server.
  Progress in Settings and the preview includes a remaining-time estimate based
  only on completed provider-search durations. Manual artist-image searches use
  an editable query, try Fanart.tv first when a key exists, and use Wikimedia
  Commons only as the fallback.
- Add or update English XML documentation for affected public/internal members.

## Client Invariants

- `MainWindow` remains one partial Avalonia class; use the existing domain-sized
  partials instead of creating competing window state or navigation models.
- Do not block the UI thread with database access, network requests, FFmpeg,
  device enumeration, player disposal, large cache I/O, or large row composition.
- The explicit ReplayGain maintenance action processes the local library and
  each configured Orynivo Server sequentially. It polls the shared remote scan
  status for progress, stops client-side polling when Settings closes,
  preserves all existing track/album values, and reports servers that do not
  support or cannot complete the authenticated maintenance request. An already
  accepted server calculation continues independently on that server.
- The desktop scan-time ReplayGain checkbox is local-only and says so. Each
  Orynivo Server dialog loads its server-owned scan-time preference through the
  authenticated settings API, disables the control for older servers, and
  persists a changed value before saving paths so an automatically triggered
  server scan observes the new setting.
- Every configured Orynivo Server row exposes distinct **Scan library** and
  **Calculate ReplayGain** actions. Each targets only that row's server,
  disables itself while polling `/api/scan`, renders operation-appropriate
  progress in a dedicated detail line without replacing connection/capability
  details, and cancels client polling when the list is rebuilt or Settings closes.
- The embedded Settings host must span the complete bounded main-content grid.
  Its active section scrolls inside that bound while the bottom Save and Cancel
  action row remains visible; do not place the host only in auto-sized rows.
- The transport output-lock button reflects actual ownership: a closed lock
  means an active player holds the configured device, and an open lock means no
  player owns it. Explicit release snapshots local, remote, radio, or podcast
  context plus position and paused state, disposes the player off the UI thread,
  and permits one-click reacquisition/resume. Ordinary playback clears that
  snapshot. Pausing alone must never be presented as releasing an exclusive
  WASAPI, ASIO, cwASIO, or direct ALSA device.
- Classic AirPlay output is a cross-platform `OutputBackend.AirPlay` profile.
  Discover `_raop._tcp.local.` receivers away from the UI thread, persist only
  their stable service ID/display name/last endpoint, refresh the endpoint
  before playback, and stream 44.1 kHz stereo PCM to a separately installed
  `raop_play` helper. Do not claim AirPlay 2, protected receiver, multiroom, or
  gapless support and never persist passwords or pairing material.
- The AirPlay 2 sender under development lives in the independent Qt-free
  `Native/AirPlay2Bridge` CMake project. Keep its public boundary as a stable C
  ABI with opaque session handles; it must not depend on Avalonia or Orynivo.
  Fail-closed transient pairing, encrypted control, and session SETUP are
  NTP timing, RECORD, and audio-stream SETUP are implemented and Sonos-verified.
  Do not load or advertise it from the desktop until event processing and
  ALAC/RTP audio pass native tests and complete real-receiver verification.
- Preserve source identity on mixed rows. Remote rows must carry their
  `OrynivoServer`, server-side IDs, and authenticated playback metadata; never
  persist credential-bearing URLs.
- Playlist context actions for local and Orynivo Server rows use the shared local
  mixed-playlist list. Remote selections retain playable URLs only for queue
  actions and persist stable `orynivo://serverId/track/trackId` references;
  hidden legacy server playlists must not be offered by these menus.
- Shared local/remote Artists, Albums, and Tracks views use the common column
  masks and catalog abstractions. Do not create parallel remote-only UI surfaces.
- Album overview rows are logically coalesced across the local library and all
  Orynivo Server providers when normalized artist and trimmed album title match.
  Mixed rows show `L+OS` and retain every source-aware provider-local album ID;
  opening one loads all of those records and uses
  the shared album folder-group surface to keep physical releases/directories
  separate. Never discard duplicate track titles because they may be different
  masterings. Conventional CD/disc directory labels include their parent folder
  for context, while other groups display the leaf directory rather than a full
  private path. Dashboard recommendations and Recently Added album displays use
  the same cross-provider logical identity and carry the complete album-ID set
  into the shared card navigation.
- Album catalog/card surfaces, Dashboard album sections, Genre Cloud album
  recommendations, and album search results omit blank or localized explicit
  unknown album titles. This is presentation filtering only: never remove or
  hide the underlying tracks from track lists, folders, playlists, or track
  search.
- Album list caches are versioned when their visibility semantics change.
  Catalog and detail surfaces must not expose provider-local album records that
  have no remaining indexed tracks; local and updated Orynivo Server providers
  enforce the same rule through the shared Core queries.
- Local directory settings expose both the normal incremental scan and an
  explicit complete metadata refresh. The latter calls
  `LibraryScanner.RefreshMetadataAsync`, remains cancellable, shows per-file
  progress, and warns that unchanged files are re-read and the operation is
  slower. It must never modify source media.
- The Orynivo Server connection dialog downloads and restores the authenticated
  versioned server-library ZIP with transfer progress and explicit destructive
  confirmation. Restored server paths refresh the dialog; audio files and API
  credentials are never part of the archive.
- The shared album artwork card binds its title tooltip to the complete album
  title so truncated labels remain readable across local, unified, Dashboard,
  Genre Cloud, and Orynivo Server usages.
- Settings places the `Metadata` section as **Review metadata** in the
  **LIBRARY** navigation group. It analyzes physical folders rather than
  existing album rows, because incorrect tags may already have fragmented one
  release. Double-click
  opens `MetadataRepairDialog`; local directory nodes additionally expose
  **Identify folder as album**. The dialog pre-fills but permits editing its album
  and artist search terms before each MusicBrainz query, while track count and
  durations remain match evidence. Only a user-confirmed match may be applied,
  and media files remain unchanged.
- The shared Folder structure sidebar item is visible when either local media
  or at least one Orynivo Server is configured. Server-only setups must be able
  to open `ShowUnifiedFolderTreeAsync` without configuring a local directory.
- The Genre Cloud item follows the same unified-library visibility. It loads
  the local snapshot and every configured Orynivo Server concurrently, merges
  counts by stable taxonomy key, applies client-side remote favorites and
  cross-source listening-history affinity, and resolves recommendations to
  ordinary source-aware `ContentRow` track rows. Its explanatory hero remains
  separate from the elliptical cloud surface below it; drill-down transitions
  cross-fade the old level and stagger the new count-scaled nodes. Cloud nodes
  use measured, centered rows with explicit horizontal/vertical gaps and must
  never overlap; excess rows scroll vertically. The recommendation mode uses
  the same segmented `ViewModeRadioTheme` controls as the Artists/Albums view
  selector and switches between the shared track table and source-aware album
  artwork cards. The cloud surface owns a dedicated Auto-sized main-content row
  between the intro hero and the shared star-sized result row; never overlay it
  on results or compensate with large result margins. Mode-button clicks must
  explicitly update both radio states and the visible result host. Opening an
  album recommendation must collapse both Genre Cloud rows and use the normal
  star-sized album detail surface. Its navigation state retains the selected
  taxonomy key and Tracks/Albums mode so Back restores the same cloud context,
  selection, and scroll offset. Node labels and font scaling use track counts in
  Tracks mode and distinct provider-local album counts in Albums mode; changing
  the mode must rebuild the existing nodes without reloading the libraries.
  A remote snapshot whose `ParentKey` differs from the requested key is stale
  taxonomy data and must be rebuilt from that server's track facets. A selected
  leaf with candidates but no child nodes displays its own name centrally; it
  is not an empty-library state. The virtual `more-genres` node is localized
  while its dynamic children retain the actual unmapped library tag names.
  Each taxonomy level may render up to sixteen recommendation-ranked local and
  remote artist images as a non-interactive grayscale tile background. Keep it
  faded beneath a dark veil so node labels retain priority. Scale each image
  proportionally to fit completely inside its tile instead of center-cropping
  away substantial parts. Cache only the rendered mosaic for 24 hours under the
  data root; authenticated remote artwork URLs and API keys must never be
  written to that cache.
  The expensive merged nodes plus resolved track/album recommendations are
  retained separately in a bounded in-memory LRU cache keyed
  by taxonomy level, configured server identities, and a catalog generation.
  The key may contain a process-local hash of a server URL for configuration
  invalidation, but never the URL or API key itself; the cache is never persisted.
  Reopening a cached level must only rebuild its controls and bind its existing
  rows; it must not repeat local facet queries, remote ID resolution, or remote
  album-catalog loads. Local watcher changes and changed remote
  `LibraryChangedAt` values increment the generation and clear completed
  entries. An in-flight load is shared and may finish after its original
  navigation is cancelled so an immediate return can reuse it.
  Settings > Appearance exposes an independent clear action for these rendered
  mosaics. It must not remove source artist images or any other remote artwork
  cache. The same section persists a background mode of
  None, Albums, or Artists (default). None must skip image resolution, network
  downloads, and rendering completely. Albums use the source-aware resolved
  recommendation album rows. Derive the column count and requested image count
  from the measured cloud-surface width, capped at 32 images. Dedupe decoded
  images by perceptual fingerprint so copies reached through different local or
  server identities are not repeated. Center sparse sets as one balanced row or
  two balanced rows; never repeat them merely to fill the available slots. A
  single row uses the complete mosaic height and derives tile width from its
  actual column count; two rows divide the height evenly. Preserve proportional
  fitting so this adaptive enlargement never crops or distorts an image. Keep
  the independent cache-clear action beside the mode selector in Appearance,
  and persist a zero-to-one opacity setting exposed as a 0–100% slider with a
  50% default. Disable that slider while the None mode is selected.
  The cloud footer starts Infinite Mix from the genres represented by the
  current level. Below the root, store the selected parent branch in
  `InfiniteMix.IncludedGenres`; at the root, store every visible root branch.
  Candidate loading must expand each branch to itself plus every recursive
  descendant so directly tagged parent tracks and all subgenres participate.
  Open the normal profile dialog with these includes prefilled, then load those
  branch snapshots instead of relying on a bounded root snapshot. Reuse the normal
  initial-mix queue, progress overlay, active-playback preservation, and
  persistence path.
- Matching local and Orynivo Server artists use
  `ArtistNameNormalizer.CreateComparisonKey` and one `UnifiedArtist` row. Its
  album drill-down combines every matching library while retaining each album's
  source context. Opening one of those albums must pass the album row's
  provider-local artist ID into the shared album detail so its tracks initially
  remain scoped to the selected artist and the show-all-tracks checkbox remains
  available. Every non-Plex artist navigation entry point must use that
  unified drill-down even when the clicked track or row came from only one
  source. The unified row selects available biography and artwork from any
  matching identity. Profile downloads and manual image selections propagate to
  every matching local and reachable Orynivo Server identity; automatic profile
  images must never overwrite a manually selected image. Manual artist-image
  uploads and deletions propagate through the same identity set. Renaming a
  local or remote identity likewise renames every normalized matching artist in
  the local library and on reachable Orynivo Servers; target-name collisions
  remain unresolved until the user explicitly chooses a merge. A local merge's
  survivor choice must be mapped by identity role (current versus existing
  target) to equivalent collisions on every matching server. Album and
  artist detail upload/delete actions route through the owning local or remote
  artwork provider and use localized labels/tooltips. Plex identities remain
  separate.
- Unified artist and logical-album details reconcile artwork for equivalent
  local/server identities with a bounded best-effort request. Copy only into
  missing destinations, preserve manual artist images, invalidate unified and
  remote list caches after writes, and never let an unavailable server prevent
  the detail view from opening.
- Every non-Plex artist-name link, artist-row double-click, unified search
  result, and artist navigation from the transport opens the shared artist
  detail surface instead of a bare Albums list. Its accent-bordered hero places
  the artist image at the left and the title, bounded scrollable biography,
  source link, rename, image search/upload/delete, and profile-refresh actions
  at the right without overlap. Its favorite button synchronizes all normalized
  matching local and Orynivo Server identities. The album strip below combines matching local and Orynivo Server
  identities and de-duplicates equivalent title/year cards while retaining the
  chosen card's source-aware navigation. Album cards open on double-click and
  pass their provider-local artist ID/name into the album detail so its initial
  track list remains artist-scoped and its show-all checkbox is available. The
  artist detail stays visible until loading finishes, and Back restores that
  same detail. Refreshing the profile must restore
  that unified album strip. The normal Back action returns to the originating
  view in one step; the transport info overlay remains independently closable.
- Artist detail reuse must reset all profile-bound controls and owning IDs
  before resolving the next artist, so a missing or slow profile can never show
  the previous artist's image/biography or mutate the previous identity. The
  unified artwork-card info button resolves its name from `ContentRow.Title`.
  Forced profile refresh uses `ArtistProfileSearchDialog` to accept a temporary
  lookup name without changing the canonical library artist name.
- Artist table/artwork rows open details exclusively via double-click; their
  former info icon is intentionally absent. Populate unified albums before the
  profile request and append reachable server results incrementally. Profile
  status text stays in the hero beneath the action row and changes from loading
  to not-found when no biography is available; it must never obscure the album
  strip.
- The artist detail hero intentionally has a roughly 420 px minimum height and
  320 px image; its biography remains independently scrollable and the album
  strip starts below the hero.
- Artist-detail album cards expose the same missing-artwork cover-search,
  favorite, and source-badge controls as the shared Albums artwork cards.
  Equivalent local/server albums show `L+OS`, apply favorite changes to every
  represented identity, and open their combined logical album detail. Below the album strip, the detail page
  shows one source-aware local/Orynivo Server track table ordered by album,
  disc, and track number. Its rows reuse the normal favorite, source, album-link,
  context-menu, and double-click playback behavior; track loading must not block
  profile rendering or discard already loaded album cards when one source fails.
  That embedded table uses the complete shared Tracks column set and a dedicated
  `ArtistInfoTracks` column-settings key, so header right-click selection,
  display order, and widths persist independently from the main Tracks table.
- `ShowUnifiedArtistAlbumsAsync` renders local albums before remote/profile
  work, loads server album sets concurrently, yields the dispatcher so cards
  paint, and starts profile/image resolution last. Its load version discards
  superseded results. Synchronous local provider calls and bitmap decoding must
  run through `Task.Run`; `ShowArtistInfoAsync` must never reset albums while a
  unified artist context is active.
- Navigation state must distinguish local, remote, Plex, and unified drill-downs;
  numeric IDs from different sources can collide. Back restoration of the
  top-level Artists and Albums views must use the normal unified loader rather
  than binding `QueryRows` directly, and saved row selection must include its
  stable source key. Search navigation also saves the selected result's source
  and the outer result-page offset.
- Keep long mixed-library row composition off the visible `DataGrid` until the
  result is complete, unless a proven virtualized/paged strategy is used.
- Use shared typography, brushes, vector icons, control themes, loading helpers,
  and context-menu patterns from the existing application resources.
- Keep the application-level `DataGridSortIconMinWidth` override at zero. The
  Fluent DataGrid theme otherwise reserves 32 px for an absent sort glyph in
  every column header, obscuring labels in compact columns; a visible sort glyph
  still contributes its natural width, and resize grippers remain unchanged.
- Every data-bearing desktop table column must be sortable from its header.
  Template columns require an explicit `SortMemberPath`; formatted numeric,
  date, duration, source, favorite, and rating values must sort through typed
  semantic properties rather than rendered strings. Pure artwork/action columns
  may map to their entity name or remain explicitly unsortable when no data
  ordering is meaningful.
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
- Every artwork card that supports double-click navigation also exposes its
  primary entity name as an `EntityLinkButtonTheme` link to the identical
  destination. Link clicks must be handled independently so they neither invoke
  the surrounding card's double-click handler nor trigger playback.
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
- Linux desktop updates map Arch-family distributions (including CachyOS) to
  the signed `arch` package, Debian-family distributions to `deb`, and
  RPM-family distributions to `rpm`. After digest verification, installation
  must run through `/usr/bin/pkexec` and the distribution package manager; a
  downloaded package must never be launched as an executable. The client must
  await the privileged package-manager exit and shut down only after exit code
  zero; authentication cancellation and package-manager failures keep Orynivo
  open and are logged.
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
- `RefreshQueueRows` must copy a registered remote track's `OrynivoServer`
  context together with its display metadata. Otherwise the shared source
  column mislabels that queue row as local. Keep this context memory-only and
  never persist its authenticated playback URL or API key.
- Infinite Mix (`MainWindow.InfiniteMix.cs`) uses the persisted
  `AppSettings.InfiniteMix` profile: calm/balanced/energetic mood,
  familiar-to-adventurous discovery, 3/7/30/90-day history, local and selected
  Orynivo Server sources, favorite/rare-track weighting, and explicit genre
  includes/excludes. It appends batches of 20 and refills at five remaining
  items, preserves source-aware `ContentRow` metadata, excludes queued paths,
  and limits immediate artist/album repeats. Up Next owns the persistent
  active/paused status, profile editor, next replacement, genre-level more/less
  feedback, and permanent credential-free track exclusions. Never persist an
  authenticated URL as feedback identity: use `local:<trackId>` or
  `server:<serverId>:<trackId>`. Normal explicit queue replacement and Clear
  Queue stop automatic refill. Initial generation must show
  `InfiniteMixLoadingOverlay` with staged progress and block duplicate
  interaction; threshold refills remain unobtrusive in the background.
  Starting a mix during active playback must retain the audible item at queue
  position zero and append recommendations behind it. Initial refill must not
  refresh the active gapless session while the blocking overlay is visible;
  after the overlay closes, refresh through `StartPlaybackAsync` with its
  `initialPosition` argument so no audio from position zero is replayed.
  The profile dialog is resizable with a bounded minimum size, and its scroll
  content reserves a right-side gutter so overlay scrollbars never cover text
  or inputs at any supported display scale. Included and excluded genres use
  removable chips. Their type-ahead suggestions load asynchronously from the
  currently checked local/server sources; unavailable servers must not block
  the dialog, and arbitrary custom genre values remain valid.
  Batch diversity is progressive: first avoid recent artists and repeated
  albums, then permit artist repetition, and finally permit further eligible
  tracks from represented albums. Narrow genres with only a few albums must
  still fill the requested batch from their remaining candidates. Repeated
  refills rotate through the stable provider candidate order instead of
  querying the same bounded prefix forever; active playback also performs a
  throttled lightweight threshold check so refill is not dependent on queue-view
  events and an empty/error refill does not create a tight request loop.
- Remote album lists are cached by `LibraryChangedAt`, but artwork mutations do
  not advance that scan timestamp. Every successful remote album artwork upload,
  reassignment, or deletion must therefore call `DeleteOrynivoAlbumListCache`
  so Genre Cloud and other later album views reload current artwork metadata.
- Local album artwork writes generate cache files and Skia thumbnails and must
  run off the UI thread through `LocalLibraryCatalogProvider`. After assignment,
  update the bound `ContentRow` in place; do not rebuild the complete unified
  Albums view merely to display the new bitmap. Album detail may reload only its
  compact header row.
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
- Dashboard builds write one sanitized phase summary to the bounded rolling
  `logs/dashboard-performance.log`. Keep this persistence off the UI thread and
  never add media names, paths, server URLs, API keys, or other credentials to
  the diagnostic payload.
- Expensive Dashboard album aggregates use one versioned in-memory catalog
  snapshot per recommendation profile and configured server set. Concurrent
  builds must await the same in-flight load. Local watcher changes and changed
  remote `LibraryChangedAt` values invalidate it; cheap listening statistics,
  calendar data, and recently played rows remain outside this cache.
- The complete unfiltered shared Artists, Albums, and Tracks results use a
  three-entry LRU session cache. Configured-server identity and a generation
  form the key; library-version, watcher, favorite, and artwork changes advance
  the generation. Independent remote servers load concurrently, and local
  provider database work must not run synchronously on the Avalonia UI thread.
- A cached Genre Cloud level renders immediately and must not retain the
  first-load branch-transition delay.
- `AppSettings.LastMainView` persists every selectable sidebar leaf tag, not a
  hard-coded view subset. Section and library-group containers, empty hints,
  and disabled Plex server headings are never persisted. Synchronously built
  dynamic rows restore normally; a Plex library tag remains pending while Plex
  navigation loads asynchronously, with Tracks as the temporary/safe fallback.
- Dashboard Recently Played cards show the persisted album below the artist.
  When the history identity can resolve a local, Orynivo Server, or Plex album,
  the album name opens that source's album detail without triggering card playback.
- A title action in `DailyHistoryDialog` starts the selected history entry
  directly through `PlayHistoryEntryInPlaceAsync`. It must never navigate to,
  bind, or `ScrollIntoView` the complete unified Tracks table first; that path
  can block Avalonia's UI thread for large mixed libraries. Album and artist
  actions retain their source-aware navigation behavior.
- Shared local and Orynivo Server track rows carry a personal zero-to-five
  rating plus cached MusicBrainz recording rating metadata. The interactive
  star column persists through the owning database/API. MusicBrainz lookup runs
  on the client, prefers the recording MBID, and accepts an artist/title fallback
  only when optional duration filtering leaves one exact result. Server scans
  must preserve client-resolved recording MBIDs and all rating fields. Album
  detail rendering starts a cancellable refresh only after track rows are bound;
  values fetched within 30 days must not be queried again. Resolve missing MBIDs
  conservatively and persist every unambiguous identity, then fetch each rating
  through the direct recording lookup; MusicBrainz batch search rating fields
  are not reliable enough to cache. De-duplicate known MBIDs across mirrored
  local/server album rows before issuing direct lookups.
  The rating cell displays a localized **Load rating** action before its first
  lookup, a loading state while active, and **Try again** after a temporary
  failure. Album-detail foreground refresh retries temporary failures up to
  three times. A successful direct lookup without a community score displays
  the distinct localized not-rated state and must not be retried continuously.
  Direct lookups request supplemental MusicBrainz genres and tags. Persist them
  through the owning local database or remote rating API, then incrementally
  update the local/server Lucene document; never replace the row's embedded
  genre or contact MusicBrainz from a normal library scan.
  A single client-side background enrichment worker starts on first playback,
  covers the local library and every configured Orynivo Server, and advances
  only while playback is active and not paused. Album-detail and explicit
  rating requests increment the foreground gate so the worker yields between
  requests; all calls still share the MusicBrainz service throttle. Known
  recording lookups use the 30-day cache lifetime. An unresolved conservative
  metadata lookup persists its attempt timestamp and is retried after 90 days.
  The worker must never block the UI thread or run from a normal library scan.
- Dashboard album recommendations rank compact local and Orynivo Server album
  candidates against genre listening time from the selected history period.
  Already-heard albums are de-emphasized, and the optional mood selector applies
  a genre/BPM preference rather than excluding all non-matching candidates.
  Recommendation cards retain their source context and use the shared album-card
  navigation. Their default presentation is the taller circular cover stage:
  the centered album is full-size, neighboring albums are progressively scaled,
  angled, and faded, and navigation crossfades translated/rotated stage frames.
  `AppSettings.DashboardRecommendationStageView` persists the List/Stage switch
  immediately and defaults to Stage.
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
- The Windows installer shortcuts must carry the same
  `Orynivo.AudioPlayer` application user model ID that `App.xaml.cs` assigns to
  the process. Windows uses that identity to attribute the SMTC media session
  to Orynivo instead of showing an unknown application.
- Keep the Linux-only direct `Tmds.DBus.Protocol` dependency at 0.92.0 or newer
  within the compatible package line: its non-blocking observer dispatch avoids
  a shutdown race with Avalonia's stopped UI dispatcher.

Consult the detailed matching sections in the root `AGENTS.md` before changing
audio, queue, Dashboard, playlists, remote libraries, settings, or table/tree UI.
