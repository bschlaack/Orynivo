# Orynivo Desktop Client Instructions

This file applies to the Windows, Linux, and macOS Avalonia desktop client under
`Orynivo/` and supplements the repository-wide `../AGENTS.md`.

## Completion

- Follow the root mandatory completion checklist. In particular, feature and UI
  changes require `CHANGELOG.md` and usually `README.md`; architectural or
  behavioral changes require this file or the root `AGENTS.md` to be updated.
- Build with `dotnet build Orynivo/Orynivo.csproj` after client changes. Linux
  and macOS compile the `net10.0` compatibility build; Windows continues to
  target `net10.0-windows10.0.19041.0`.
- New visible text must use `LocalizationManager` and exist in German, English,
  French, Spanish, Russian, Simplified Chinese, and Hindi.
- `ApplicationCredentialStore` is the only persistent client credential
  container. It uses current-user DPAPI on Windows and AES-GCM plus a
  current-user-only random key file on Linux/macOS. Last.fm, Fanart.tv, AI Chat,
  Orynivo Server, Plex, and generic streaming secrets must remain `[JsonIgnore]`
  in JSON settings models and must not appear in caches, logs, server payloads,
  diagnostics, documentation examples, or model context.
- Fanart.tv artist artwork uses the encrypted Settings value or the
  `FANART_TV_API_KEY` environment variable. The environment variable remains an
  optional runtime override and is never copied into persistent settings.
- The Artist information Settings action for missing artist images combines the
  local library and every configured Orynivo Server, runs strictly sequentially,
  skips every manually selected image, tries Fanart.tv first only when a key is
  available, then falls back to Wikimedia Commons, and remains cancellable. When
  an API key is configured, the preview may enable automatic acceptance for
  Fanart.tv results for the current run only. This must never automatically
  accept Wikimedia results. Other candidates stay in memory until the user
  accepts or rejects their preview, and cancelling the preview cancels the
  complete run; accepted remote images are uploaded only to their owning server.
  Progress in Settings and the preview includes a remaining-time estimate based
  only on completed provider-search durations. Manual artist-image searches use
  an editable query, try Fanart.tv first when a key exists, and use Wikimedia
  Commons only as the fallback.
- Add or update English XML documentation for affected public/internal members.

## Client Invariants

- Shuffle changes must refresh prepared gapless output through the existing
  coordinator, preserving position, pause and start ownership. Automatic queue
  advancement must include forward shuffle history after Previous.

- Genre Cloud shares local facet input only until catalog/profile invalidation.
  Resolve selected albums by ID and load independent sources concurrently; never
  fetch a complete album catalog for cloud recommendations. Preserve strict
  failures, source identity and foreground publication ownership.

- WASAPI MaxOutputSampleRateHz is a strict positive ceiling, not a preference.
  Never retain higher-rate fallback candidates. Zero retains unrestricted
  selection; no supported format within a positive cap must fail before output
  starts. Preserve exact DSD division ordering among eligible candidates.

- Fresh transport keeps Play disabled until a source is known. Idle Stop must
  preserve this condition while retaining remembered-source retry/resume;
  restoration failure disables Play. Do not duplicate playback ownership.
- Setup hints use secondary text with sufficient theme contrast. setupAction
  owns its focus marker and suppresses the duplicate default adorner. Keep
  text/focus contrast checks and simulated scaling distinct from physical DPI.

- Setup-action keyboard focus uses the shared accent border and soft fill in
  existing button templates, scoped by setupAction. Keep it immediate and
  geometry-preserving; do not add focus animations or shift button bounds.

- Sidebar TabNavigation must Continue through nested setup buttons. Preserve
  forward/reverse traversal and normal Enter activation; never trap focus or
  treat the non-leaf setup hint as a persisted view.

- Empty-view guidance requires a complete, source-backed zero-row result in a
  normal Artists/Albums/Tracks view with configured media sources. Reuse the
  existing detail/search/playlist exclusion guard; never present unavailable or
  partial catalogs as empty. Clear it with navigation and route server-only
  Settings to OrynivoServers, otherwise Library. Retain its original coordinator
  Request at normal/cache publication and require IsCurrent before showing
  guidance. Refresh notice visibility after load admission and invalidation,
  including the watcher's dispatcher pass.

- Local folder scan guidance stays beside Directories and uses all seven
  languages. A newly selected folder starts the ordinary row scan after source
  publication; existing folders and duplicate picker results do not auto-scan.
  Preserve active rows when adding/removing folders and retain cancellation
  ownership until completion. Share progress, errors and retry with manual Scan.
  Guidance must not imply that a configured folder already contains tracks.

- Both Settings root-change paths use SetConfiguredLibraryPaths to copy roots
  and expand Library on the first local source. Later changes preserve its
  collapsed state. Pass the returned roots to watchers and cleanup.

- Output Settings keeps the profile selector full-width above wrapping actions.
  Captions must wrap vertically and retain localized automation names; do not
  restore the fixed-width, single-row action grid.

- Empty-library setup uses the shared embedded Settings Library and
  OrynivoServers sections, plus AudioDevice through the shared transport
  Settings action. Keep its buttons keyboard accessible with explicit localized
  automation names and the wrapped hint free of row-height caps. Do not animate
  its unbounded maximum height or persist it as a sidebar leaf.

- Tracked headless workflow checks live in Orynivo.Ui.Smoke, separately from
  pure desktop unit tests. Its friend access is for synthetic production-adapter
  testing only. Follow its nested instructions; never show MainWindow or invoke
  startup/playback to test navigation. Windows full verification and CI run the
  smoke suite; broader platform/runtime coverage must be evidenced separately.

- Artist detail owns ArtistInfoScrollViewer for Back position capture and
  restoration. Never borrow selected rows or offsets from an overlaid table.
  Reset scroll on new detail admission; restore only after layout while the
  original Back guard, detail version, canonical name and visibility remain
  current. Refreshing metadata inside the same detail must not reset its scroll.

- ArtistImageIntentOwnership retains per-identity rejection only for live
  disposable observations. A later unrelated admission must never revive an old
  matching image. Observe identities before cancellation callbacks and release
  on every completion/early return/failure. Local profiles observe their known
  ID and bind canonical metadata after readback; never borrow a previous
  artist's name. Manual synchronization notifies exact local IDs on the caller
  context before off-thread mutations. Own donor propagation renews only its own
  observation. Keep source IDs/names in memory and retain no mutation history
  for completed requests.

- Missing artist artwork synchronization captures original copied sources and
  enclosing detail ownership before artist write admission. Hold admission
  through authoritative donor/target reads, image capture and mutations. Skip
  renamed, unavailable, populated or manual targets; preserve fresh biography.
  Recheck source/image/navigation ownership between awaits and before writes.
  Manual donor propagation notifies image intent and renews only its own
  observation. Invalidate catalogs in finally after admitted mutations.

- Settings missing-artist-image acceptance uses MissingArtistImageWriter with
  the window artist write queue and image-intent notification. Standalone
  Settings owns a separate queue. Recheck authoritative missing/manual state,
  normalized artist identity and original run/source ownership after waiting and
  before mutation. Copy complete source/profile preferences before lookup;
  remote acceptance targets only that source. Notify catalog invalidation after
  admitted writes even when later cancellation prevents metadata completion.

- Remote artist context-menu image selections retain original binding, row
  identity, navigation and copied server ownership. Share manual action
  ownership and artist write admission with detail actions; reserve the whole
  primary upload and synchronization batch. Recheck ownership after selection,
  queue wait and primary upload before fan-out or UI publication.

- Background artist-row profile refreshes retain shared artist write ownership
  through download, SQLite persistence and artwork decoding. Recheck original
  row/background ownership after waiting and refresh authoritative manual-image
  state off the UI thread before selecting image policy. Preserve the upfront
  session freshness stamp and owner-specific loading cleanup.

- The fixed GL post pass applies MilkDrop's centre-darkening sprite only within
  a small diamond around the centre (3/32 peak alpha, radius 0.025 of the
  smaller dimension). The legacy display pass mixes its animated hue shade with
  white by `VisualizerFrameParameters.ShaderAmount`; `fShader=0` must not tint
  the image.

- Every uniform passed to the fixed GL `Set` helper must first have its location
  resolved. Resolve the mesh vertex uniforms for custom warp programs when
  linking them as well as the fixed warp's decay/time/scale inputs. The fixed
  warp samples bottom-up feedback without an extra Y flip. Decay is applied by
  the fixed warp only; custom warp shaders own their fade. Re-check these
  contracts with a constant-output shader after changing them. Remaining
  compatibility defects are recorded in `../docs/VISUALIZER-STATUS.md`; previous
  "corrected" notes are not fidelity proof.

- Blur generation changes the active GL framebuffer and viewport. The custom
  warp pass must rebind its full-resolution ping target after
  `BuildShaderBlurLevels` and before drawing. A constant-output shader at two
  sizes checks that the real GPU pipeline fills its target. See
  `../docs/VISUALIZER-STATUS.md` for outstanding Milkdrop compatibility issues.
- The fixed GL warp transforms the texture coordinate in its vertex shader and
  interpolates the resulting coordinate, so `WarpVertexSource` computes the
  reference warp arithmetic (including the time-dependent displacement, whose
  `uWarpTime` comes from `VisualizerFrameParameters.WarpTime`) and the fragment
  shader only samples it. The mesh vertex layout is position plus the ten
  `PresetRenderer.MeshValues` motion values, mapped across four attributes; keep
  the attribute pointers and `PackVertices` in step when the value set changes.
  When a per-pixel block writes both position (`x`/`y`) and motion (`dx`/`dy`,
  rotation, zoom, etc.), the GL path must draw the CPU-evaluated motion mesh. A
  fullscreen per-pixel fragment warp discards that motion and freezes presets
  such as Royal Mashup (13). The custom warp draws the same mesh with the same
  vertex shader: its fragment stage comes from
  `ShaderTranspiler.TranspileGlslWarpMesh`, which reads the interpolated
  coordinate from the vertex stage and does not re-emit the per-pixel block,
  because the mesh already ran it. Keep the two warp programs on the mesh vertex
  shader, and never draw the custom warp as a full-screen quad again.
- GL sampler qualifiers select independent native sampler objects resolved
  through `GlInterface.GetProcAddress`; bind each active sampler to its own unit
  and clear sampler bindings before fixed passes and before returning to
  Avalonia. Fixed warp uses `bTexWrap`. Custom comp frame/blur reads convert
  top-down UVs to bottom-up GL texture coordinates; generated noise coordinates
  are not flipped. When a shader sampler is bound, make its unit active
  **before** resolving the texture. A preset texture (`sampler_<name>`) is
  uploaded lazily during binding, and an upload binds its new texture to the
  active unit, so resolving before selecting the unit let a user texture
  overwrite the previous sampler's binding — the unused samplers are skipped,
  which made the previous unit `sampler_main` and displayed the texture as the
  previous frame. `verify-fidelity.ps1` check 6 pins this.
- Legacy gamma/echo use a separate display target and must never enter feedback.
  Gamma is a linear brightness gain. The overlay's alpha is coverage, not forced
  opacity; non-additive elements cover feedback. The shader blur chain is built
  after the warp from the feedback the warp just sampled and **retained** for
  the next warp; because the reference swaps its frame buffers at the end of the
  frame, the next warp's `sampler_main` is one generation newer than its
  `sampler_blur1`-`GetBlur3` chain. This is intentional in `milkdropfs.cpp`
  ("when sampling the blurred textures in the warp shader, they are one frame
  old"), so a shader that compares the two (for example
  `GetBlur1(uv_orig) - GetPixel(uv_orig)`) oscillates on every path; do not
  "fix" it by building the chain before the warp. The GL pipeline, the CPU
  interpreter, and the Skia warp pass must all read that retained chain, so the
  Skia pass binds the renderer's retained blur levels instead of rebuilding them
  from the frame it samples. Comp reads the same freshly built chain. A freshly
  allocated retained chain is black and marked ready, so the first warp reads
  retained black rather than a same-frame blur. The comp main samplers also bind
  VS[0], not the current warp-and-overlay target VS[1]. Apply progressive range
  compression, GetBlur decoding and first-level edge darkening. Composite GLSL
  `rad` and `ang` follow MilkDrop's aspect-corrected `UvToMathSpace`
  coordinates; hue corner indices follow the reference's bottom-right,
  bottom-left, top-right, top-left vertex order. Textured custom shapes sample
  the previous feedback texture (`VS[0]`) with linear repeat, while their output
  is drawn into the current frame (`VS[1]`).
- `VisualizerAudioHub` keeps a 1024-frame rolling PCM window for each analysis;
  render ticks between audio writes reuse the most recent spectrum for at most
  100 ms rather than injecting silence. Clear this window on reset or
  sample-rate changes.

- Dashboard and its Show all pages share DashboardScrollViewer. Album artwork
  assignment must update the bound ContentRow in place, never call
  BuildDashboardAsync merely because that viewer is visible; this would replace
  the current Show all page and discard its scroll position.

- CoverSearchWindow publishes previews incrementally, decodes off the UI thread,
  cancels superseded/closed searches, rejects stale callbacks and duplicate
  starts, and disposes preview bitmaps. Original download failure keeps the
  dialog open for retry. Its bounded background timing log must contain only
  fixed phase names, elapsed times and byte counts, never queries or URLs.

- Local user-profile identity is persisted separately from the shared library
  catalog. `UserProfileManager` normalizes the legacy single-user installation
  to a `standard` profile and carries per-server profile mappings plus personal
  remote-favourite/recommendation containers. Profile-aware local state and
  server profile authentication must use this context; never duplicate or
  partition shared audio metadata/artwork.

- `MainWindow` remains one partial Avalonia class; use the existing domain-sized
  partials instead of creating competing window state or navigation models.
  `MainWindow.xaml.cs` is intentionally reduced to shared state (fields, nested
  types/records, and the constructor). Domain partials cover dashboard, history,
  radio, podcasts, playlists, genre cloud, Infinite Mix, similarity, AirPlay,
  mobile remote, MusicBrainz, metadata repair, artwork synchronization, library
  view cache, queue/Up Next (`MainWindow.Queue.cs`), the A-Z index
  (`MainWindow.AlphabetIndex.cs`), artwork (`MainWindow.Artwork.cs`), search
  (`MainWindow.Search.cs`), track filters and smart playlists
  (`MainWindow.TrackFilters.cs`), settings/output/EQ and PCM volume/ReplayGain
  (`MainWindow.Settings.cs`), folder trees (`MainWindow.FolderTree.cs`), album
  detail (`MainWindow.AlbumDetail.cs`), artist detail
  (`MainWindow.ArtistInfo.cs`), sidebar (`MainWindow.Sidebar.cs`), navigation
  (`MainWindow.Navigation.cs`), playback/transport (`MainWindow.Playback.cs`),
  cover search (`MainWindow.CoverSearch.cs`), favorites, Plex, remote Orynivo
  Server view/caches, unified library views (`MainWindow.LibraryViews.cs`),
  table rendering (`MainWindow.TableRendering.cs`), startup, content loading,
  entity favorites, navigation links, Orynivo navigation, context menus,
  helpers, rating columns, artist albums, and the app shell. The largest domains
  are split into sub-partials
  (`MainWindow.Dashboard.{Recommendations,Media,Stats}.cs`,
  `MainWindow.PlaybackState.cs`/`MainWindow.Transport.cs`,
  `MainWindow.ArtistInfo.{Rename,Albums,Profile}.cs`, and
  `MainWindow.Playlists.DragDrop.cs`). Generic visual helpers (`FindResource`,
  `ResolveFontSize`, `FindAncestor`, `FindVisualChild`, `FindVisualChildren`)
  and the shared table-column factories live in their dedicated helper/rendering
  partials. Credential-free queue path persistence is decided by the shared,
  tested `Orynivo.Library.QueuePathPolicy` in Core; never duplicate that URL
  policy in the desktop. Pure, UI-free desktop helpers must be extracted into
  standalone testable types (for example `Orynivo.Controls.ArtworkAccentColor`
  or `Orynivo.Controls.ListeningTrendGeometry`, covered by `Orynivo.Tests`)
  instead of remaining private `MainWindow` members. Extract further domains as
  new partials rather than letting `MainWindow.xaml.cs` grow again. Build with
  `dotnet build Orynivo/Orynivo.csproj` and run
  `dotnet test Orynivo.Tests/Orynivo.Tests.csproj` after client changes.
- Do not block the UI thread with database access, network requests, FFmpeg,
  device enumeration, player disposal, large cache I/O, or large row
  composition.
- AI chat and the embedded MCP server expose one permission-gated tool surface.
  Keep `McpTools`, `AiToolDefinitions`, `AiToolExecutor`, and the Settings tool
  checklist in exact parity through `scripts/verify-mcp-tool-parity.ps1`.
- MCP remains loopback-only unless `McpNetworkAccessEnabled` is explicitly
  selected. Network mode binds to all interfaces and every `/mcp` request must
  authenticate with the persisted 256-bit bearer token; never log that token.
  Library search includes every configured Orynivo Server and returns only
  opaque `orynivo://` references; remote failures must be reported distinctly
  from genuine empty search results without exposing URLs or API keys.
- The mobile web remote is a separate opt-in LAN endpoint with its own port and
  generated bearer token; never reuse MCP enablement, permissions, or tokens.
  Its public page may be loaded without authentication so a user can enter the
  token, but every `/remote/api` request must authenticate in constant time. Do
  not browser-cache the embedded HTML across upgrades, reject oversized API
  request bodies before endpoint binding, and stop automatic reconnect after a
  401 until the user supplies a new token. Remote state and queue DTOs must omit
  physical paths, authenticated stream URLs, credentials, and private provider
  settings. Live updates send compact state changes only and reconnect without
  reloading the complete library. Current artwork must be decoded off the UI
  thread, bounded to a 640-pixel JPEG, served only through the authenticated
  API, and carry private cache validators. Queue selection accepts only a
  validated zero-based index. Mobile search combines local and configured
  Orynivo Server tracks away from the UI thread. Results expose only display
  metadata and opaque local/server identities; resolve those identities inside
  the desktop immediately before a validated play-now, play-next, or append
  action. Artist/album browsing must remain provider-bound: expose an opaque
  provider identity, resolve it internally for the next drill-down, and never
  return a server address, API key, stream URL, or physical path to the browser.
  Favourite changes and output selection must reuse the existing MainWindow
  callbacks. Queue edits must validate indices and pass through the established
  move/remove/clear persistence, gapless-refresh, and Infinite Mix lifecycle.
  Settings enumerates active non-loopback IPv4 addresses off the UI thread and
  generates QR codes locally through QRCoder's portable PNG renderer. QR
  payloads carry the token only in the URL fragment, never in a query string or
  persisted image. The browser consumes/removes that fragment on initial load or
  hash navigation; credentials live only in page memory. Plain-URL visits and
  reloads require token entry. Settings changes must be saved before using their
  QR code. The desktop token is JsonIgnored and overlaid from
  ApplicationCredentialStore; loading a legacy plaintext mobile token migrates
  it and removes it from settings. The remote document, script, and localized
  JSON are embedded resources. Preserve blob artwork in the CSP, native
  keyboard-accessible controls, bounded artist search, stale-response guards,
  and separate playback/library/playlist/ queue navigation. Shared regular and
  smart playlists reuse desktop resolution, but background browsing must
  suppress registration into UI-owned remote-track dictionaries; only playback
  registers resolved rows on the UI thread. Mobile playlist DTOs omit paths and
  filter internals. Unresolved entries and standalone imported streams are
  intentionally omitted; do not expose raw playlist paths or authenticated URLs
  as an alternative.
- Mobile radio/podcast browsing runs through dispatcher-owned adapters in
  MainWindow.MobileRemote.Media.cs. Empty queries select saved stations/pinned
  podcasts; searches use existing directory clients. MobileRemoteMediaCatalog
  retains at most 2,048 process-local opaque source identities. Never accept
  browser-provided stream/feed URLs or serialize the registry. Episode
  identities retain their original podcast; search results matching a pinned
  feed reuse its database identity for progress/downloads. Play must use
  StartReadyPlaybackAsync and acknowledge only the still-current prepared
  player. Limit lists to 100 stations/shows and 500 newest episodes, sanitize
  provider errors, and reject stale browser responses after search, navigation
  or sign-out. Keep all six navigation destinations visible on phones and all
  seven remote locales complete.
- AI endpoint discovery and testing use `AiEndpointService` and the unsaved
  values currently visible in Settings. Query only the credential-free
  OpenAI-compatible `/models` URL, accept standard and Ollama model-list shapes,
  preserve manual model-name entry, cancel superseded requests, and never show
  response bodies, endpoint URLs, or API keys in status messages or logs.
- The explicit ReplayGain maintenance action processes the local library and
  each configured Orynivo Server sequentially. It polls the shared remote scan
  status for progress, stops client-side polling when Settings closes, preserves
  all existing track/album values, and reports servers that do not support or
  cannot complete the authenticated maintenance request. An already accepted
  server calculation continues independently on that server.
- The desktop scan-time ReplayGain checkbox is local-only and says so. Each
  Orynivo Server dialog loads its server-owned scan-time preference through the
  authenticated settings API, disables the control for older servers, and
  persists a changed value before saving paths so an automatically triggered
  server scan observes the new setting.
- Every configured Orynivo Server row exposes distinct **Scan library** and
  **Calculate ReplayGain** actions. Each targets only that row's server,
  disables itself while polling `/api/scan`, renders operation-appropriate
  progress in a dedicated detail line without replacing connection/capability
  details, and cancels client polling when the list is rebuilt or Settings
  closes.
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
- `Audio.AudioPlayerRetirement` owns asynchronous disposal of uniquely detached
  players, not active playback or queue state. Ordinary transitions keep the
  two-second wait budget; full device release drains all registered retirements,
  including older timed-out disposals and when no active player remains. A null
  ordinary transition must not add another wait. Synchronous disposal runs on
  independent LongRunning workers with TaskScheduler.Default and
  DenyChildAttach, never shared thread-pool workers; blocking backends must not
  occupy the pool needed by budget, preparation or drain continuations. Dispose
  off the dispatcher, isolate disposal/reporting failures, remove completed
  work, and cancel unused budget timers. Detach gapless event subscriptions with
  active ownership. Regression tests use fake players and a controllable
  TimeProvider, never real devices or single-sample wall-clock speed
  comparisons. Transport stop registers disposal in the background. Normal
  window closure is deferred until pending preparation and all retirements
  finish; reject new starts while closing. Do not describe retirement alone as a
  complete session coordinator.
- MCP single-track Play forwards the opaque reference to PlayFileFunc so the
  existing playback preparation owns remote lookup. Mobile single-track Play
  claims a start intent and preparation lease before local/server identity
  lookup; pass captured cancellation and a dispatcher-owned registration guard.
  Reject stale results and errors, dispose the lookup lease before readiness
  handoff, and include pending lookup in full output-release drain. Queue
  append/next retains its independent resolution semantics. Playlist Play and
  queue replacement own an intent/preparation before lookup; reject obsolete
  results before registration and queue mutation. Dispose the lookup lease
  before readiness handoff. ReplacePlaybackQueueAsync owns ordered resolution
  and the one authoritative queue; ReplaceQueueFunc returns the prepared
  playable count. Preserve duplicates and keep the existing queue for entirely
  unresolved input.
- Queue replacement captures queue generation alongside playback intent and
  links both cancellation tokens during lookup. Clear must reject late metadata
  and queue publication even when ordinary output remains active. Mobile
  playlist Play links its queue cancellation token before loading. Append/Next
  and playlist append recheck queue generation after gapless readiness;
  destructive supersession cancels MCP acknowledgement or returns false to
  Mobile. Ordinary starts preserve already accepted independent edits.

- QueueEditOwnership is dispatcher-owned and independent of playback starts.
  Mobile queue move/remove captures that generation before mutation and checks
  it after gapless preparation, including obsolete failures. Reject edits during
  shutdown. Ordinary start/Stop/release retains accepted independent edits.
  Capture version/token before append/next or playlist-append lookup; guard
  registration and mutation. Wholesale clears and replacements invalidate old
  ownership; shutdown disposes it. Ordinary start/Stop preserves independent
  work. MCP forwards original references to callbacks returning resolved paths
  or null; obsolete edits cancel. Superseded mobile edits return false.

- Mobile/MCP play and queue-start commands await prepared-player readiness, not
  the audible lifetime. Abandoned/unavailable MCP starts are cancelled; mobile
  track and queue-index starts return false without a current ready player. Skip
  and initial play-toggle return after preparation while automatic queue
  advancement retains its lifetime handling. Guard UI error handlers against
  superseded starts. `McpPlayerBridge.OnUiAsync(Func<Task>, CancellationToken)`
  must unwrap asynchronous actions and propagate failures; reject cancellation
  before dispatch. Request cancellation does not implicitly stop active
  playback.
- Radio and podcast UI actions await readiness and guard preparation errors
  against the captured start intent. Observe later lifetime errors through the
  shared readiness path. A nonzero initial podcast position takes precedence
  over saved episode progress; check ownership after resume seek before source
  and history publication. Preserve progress capture before stop cancellation.
  Save episode progress only after resume preparation; abandoning a pending seek
  preserves its previously saved position.

- Radio monitor callbacks guard captured start intent, player, station identity
  and cancellation on the dispatcher, including fallback metadata. The optional
  probe adapter supports lifecycle checks without FFmpeg. Cross-device resume
  lookup starts after source metadata assignment, owns a request version and
  captured playback token/player/row, and rejects stale same-track responses.
  Stop and gapless transitions cancel lookup and clear prompts. Accepted seeks
  validate prompt ownership, observe errors, guard completion messages and hold
  a preparation lease for full release. Advisory lookup owns no audio device and
  is cancelled without delaying output release.

- StartPlaybackIntentAsync exposes an intent-owned preparation publication.
  Queue refresh joins it while a gapless player is seeking, without beginning
  another start. PrepareCurrentPlaybackPlayerAsync checks the snapshot after
  seek, detaches/retires stale output and repeats preparation at its actual
  position within the same token/lease. Publish metadata, pause, history and
  readiness only for the final player. Every early return/error completes the
  publication with null; full release still drains pending seek/late cleanup.

- CreateCurrentQueuePlaybackPlayerAsync compares ordered playable paths before
  and after creation. Retire unpublished gapless results whose snapshot changed
  and retry within the same intent/token/preparation lease. Native single-track
  output does not retry for unrelated queue edits. Store the prepared path
  snapshot in memory only; never persist or log authenticated paths. Coalesced
  refresh compares it with the latest queue to avoid a redundant rebuild after
  creation already incorporated admitted changes. Position/pause apply to the
  final player; obsolete retries remain part of full preparation drain.

- Audio.GaplessQueueRefreshCoordinator owns dispatcher-affine refresh joining
  and revision/retry decisions through injected readiness/ownership/snapshot
  adapters. It owns no player or queue. Window adapters capture position/pause
  and validate prepared-player identity. Each run retains its own revision and
  completion; obsolete completion must not clear newer pending work. Test this
  coordination without an Avalonia window or audio hardware.
- Gapless queue refresh is dispatcher-coalesced. Requests share the pending task
  only while its playback intent remains current and increment a revision. After
  readiness, rebuild again for a changed revision using current position and
  pause state. All admitted callers await the final snapshot. Stop, Clear,
  release and newer starts prevent obsolete rebuild loops; never overwrite a
  newer task. Clear with no active player cancels pending preparation without
  joining its obsolete refresh. Keep the single authoritative queue.

- ClearPlaybackQueueAsync empties the authoritative queue, then rebuilds active
  gapless output with only the current track at its position and paused state.
  Desktop, MCP and Mobile await preparation; obsolete MCP clears cancel and
  mobile clears return false. Ordinary single-track output needs no rebuild.
  Pending starts are cancelled when no player is active.

- RefreshActiveGaplessQueueAsync awaits readiness with captured position and
  initiallyPaused state; never apply Pause after awaiting the audible lifetime.
  Obsolete rebuilding must not pause another player. Independent queue edits
  retain their separate semantics and still require a queue-resolution audit.

- `Audio.PlaybackReadiness` separates prepared-player publication from track
  completion. Apply initial seek and paused state before publication. Output
  switching and lock reacquisition await readiness; retain observation of later
  lifetime failures and check active ownership before stopping. Guard selection
  generation after retirement and every fade volume write, including the final
  delay. Failed reacquisition drains output before reporting release and retains
  its retry context. `Audio.PlaybackPlayerFactory` provides the backend creation
  adapter; keep routing in the existing window implementation and use fake
  factories for lifecycle verification.
- `Audio.PlaybackStartOwnership` assigns dispatcher-owned start identifiers
  before remote lookup. Stop invalidates pending publication; a start detaches
  current playback without invalidating itself. Check ownership after disposal,
  factory and initial/resume seek awaits; retire obsolete factory results once.
  Factories and native-format fallbacks use the captured playback token. Reject
  stale remote metadata registration and suppress obsolete errors before caller
  handlers can stop current playback. Active output is session-owned; the queue
  remains window-owned. Caller actions after awaiting the whole playback
  lifetime still require ownership guards.
- `Audio.PlaybackPreparationTracker` tracks lookup, creation, resume seek and
  late-result handoff separately from track completion. Capture cancellation
  before remote lookup; a new start or stop cancels pending preparation without
  removing it. Dispose its lease on preparation success or failure, before
  awaiting the audible lifetime. Complete release waits for preparations
  admitted before stop, then drains retirements again to include late factory
  returns. Guard release follow-up with its captured generation. Closing
  disables new starts and defers the actual Close until the complete drain;
  repeated close requests reuse one task. Update installation and library import
  await `PreparePlaybackShutdownAsync` before direct application shutdown. Keep
  history/progress capture before cancellation.
- Audio.PlaybackSessionCoordinator owns the only active player and cancellation.
  Window reads delegate to it. Admit only current, uncancelled starts before
  shutdown; rejected factory results remain caller-owned for retirement. Gapless
  retry detaches only the expected player and preserves the session token. Stop
  clears ownership before cancellation callbacks; callback/reporting failures
  must not lose the detached player. Source/history/UI cleanup and the queue
  remain in the window. Owns validates intent, live cancellation and identity;
  guarded resume seek and pause/resume recheck after backend callbacks. Tests
  compose factory, preparation, readiness and drain without a window/device.
  StopPlaybackCore captures its intent and abandons further cleanup after a
  cancellation callback starts newer playback. Cancellation helpers clear or
  replace old state before invoking callbacks; never clear replacement state
  afterward. System/slider/remote seeks capture player and intent and hold a
  preparation lease until seek completion. Full release drains these leases.
  Reject obsolete slider error/UI publication and remote acknowledgement;
  preserve current remote failures. Stop invalidates pending slider preview
  before cancellation can admit another seek.
- Audio.PlaybackOutputDrain composes the existing preparation tracker and player
  retirement owner. Register current retirement before waiting for preparation;
  full release then awaits the initial retirement pass and another pass for late
  factory results. Ordinary transitions do not drain preparation. Invoke after
  StopPlaybackCore has captured history/progress, cancelled and detached output.
  The helper owns no active player, admission, cancellation or UI state. Test
  composed release without a window. PlaybackShutdownCoordinator owns permanent
  shutdown admission and one shared outcome. Install both before invoking window
  hooks or drain so reentrant cancellation/disable callbacks cannot start a
  second shutdown. Keep success/failure/cancellation shared and admission
  closed. MainWindow's existing shutdown guards delegate to this state; it
  retains queue shutdown/UI disable hooks and actual Close. Test admission
  without a window.
- AirPlay output is a cross-platform `OutputBackend.AirPlay` profile. Discover
  `_raop._tcp.local.` receivers away from the UI thread, persist only their
  stable service ID/display name/last endpoint and refresh the endpoint before
  playback. Prefer the bundled native AirPlay 2 bridge when available; retain
  the separately installed `raop_play` helper as a classic fallback. Never
  persist passwords or transient pairing material.
- The AirPlay 2 sender lives in the independent Qt-free `Native/AirPlay2Bridge`
  CMake project. Keep its public boundary as a stable C ABI with opaque session
  handles; it must not depend on Avalonia or Orynivo. Fail-closed transient
  pairing, encrypted control, session SETUP, PTP timing, RECORD, and
  audio-stream SETUP are implemented and Sonos-verified. ALAC encoding,
  encrypted realtime RTP packetization, PTP/RTP sync anchors, retransmit
  handling, rate-aware prefill/pacing, and best-effort teardown are implemented
  behind the ABI and clean playback is verified on a Sonos stereo pair.
  `AirPlay2NativeSession` is the only managed interop boundary;
  `AirPlayAudioPlayer` owns it and feeds 44.1 kHz signed 16-bit stereo PCM.
  AirPlay user volume uses a cubic normalized-to-linear PCM curve so normal
  listening levels occupy a useful portion of the transport slider; ReplayGain
  is multiplied after that mapping. Other output backends retain their existing
  volume behavior. Session creation also carries title, artist, album, and
  optional bounded JPEG/PNG artwork resolved by `MainWindow.AirPlay`; the native
  bridge copies all values before returning from creation. Receiver artwork
  rejection is best-effort and must never prevent audio playback. Authenticated
  receiver Play, Pause, Next, and Previous events cross the native callback into
  `MainWindow.AirPlay` and must dispatch through the same shared transport
  methods as the desktop controls so player state, SMTC, history, and UI remain
  synchronized. A receiver-originated Play after Pause must rebuild the AirPlay
  pipeline at the player's current position before clearing its paused state;
  Sonos does not reliably resume media consumption on the old stream. Ignore
  callbacks from a session that is no longer the active AirPlay player.
  Receiver-originated timeline seeking is not available on this reverse event
  channel and remains unsupported until a bounded DACP endpoint is implemented.
  Keep pause, seek, stop, output release, and fallback RAOP lifecycle behavior
  behind that existing player abstraction.
- Preserve source identity on mixed rows. Remote rows must carry their
  `OrynivoServer`, server-side IDs, and authenticated playback metadata; never
  persist credential-bearing URLs.
- Playlist context actions for local and Orynivo Server rows use the shared
  local mixed-playlist list. Remote selections retain playable URLs only for
  queue actions and persist stable `orynivo://serverId/track/trackId`
  references; hidden legacy server playlists must not be offered by these menus.
- Regular mixed playlists load through `MainWindow.Playlists.Loading.cs`;
  `QueryRows` must never resolve stored remote entries synchronously. Snapshot
  entries and batch local metadata, then close SQLite before remote requests.
  Deduplicate IDs per server and request at most 200 per batch, with at most
  three requests in flight and a budget after admission. Preserve order,
  duplicate occurrences, and entry IDs. Unresolved rows have no playable path or
  track ID; retain only opaque source identity and offer removal, never queue or
  drag actions. Retry preserves current selection and scroll; navigation,
  context changes, and deferred restoration guard the owning version and catalog
  generation. Register remote playback rows only on the dispatcher after that
  guard. Mobile browsing reuses resolution without registration and omits
  unresolved rows and imported streams. Shared smart playlists use
  `MainWindow.SmartPlaylists.Loading.cs`: capture source/profile/favorites, load
  selected catalogs plus any excluded similarity seed source, and fetch strict
  feature pages with at most three concurrent server loads and budgets after
  admission. Keep local SQLite work and composition off the dispatcher. Never
  reuse incomplete feature data as a complete catalog or trigger feature
  analysis from a count preview. `SmartPlaylistSimilarityProjection` translates
  both vectors and the seed onto transient IDs without changing saved criteria.
  Counts are available only for complete current-generation results. Cancel
  preview work before debounce and on dialog close; obsolete results and errors
  must not overwrite current preview text. Smart retries restore source and
  track identity because smart rows have no stored occurrence ID.
- Shared local/remote Artists, Albums, and Tracks views use the common column
  masks and catalog abstractions. Do not create parallel remote-only UI
  surfaces.
- Album overview rows are logically coalesced across the local library and all
  Orynivo Server providers when normalized artist and trimmed album title match.
  Mixed rows show `L+OS` and retain every source-aware provider-local album ID;
  opening one loads all of those records and uses the shared album folder-group
  surface to keep physical releases/directories separate. Never discard
  duplicate track titles because they may be different masterings. Conventional
  CD/disc directory labels include their parent folder for context, while other
  groups display the leaf directory rather than a full private path. Dashboard
  recommendations and Recently Added album displays use the same cross-provider
  logical identity and carry the complete album-ID set into the shared card
  navigation.
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
  release. Double-click opens `MetadataRepairDialog`; local directory nodes
  additionally expose **Identify folder as album**. The dialog pre-fills but
  permits editing its album and artist search terms before each MusicBrainz
  query, while track count and durations remain match evidence. Only a
  user-confirmed match may be applied, and media files remain unchanged. Opening
  uses index-only analysis (`inspectFiles: false`); physical readability checks
  and full duplicate hashes require the explicit checkbox and refresh. Publish
  local findings before awaiting server reports, then append completed server
  results without clearing selection. Server reports remain read-only.
  `MetadataReviewActivity` coalesces progress for a UI timer, reports elapsed
  time and phase-local estimates only for measured work, and never invents a
  remote ETA. Settings deactivation cancels analysis; dialog close cancels
  MusicBrainz. Repeated searches must clear selection and preview before
  loading. Analysis, dialog and correction use Core `OrderTracks`; never
  silently zip a partial match. Successful corrections invalidate shared library
  views and refresh analysis. The same section exposes a **Duplicate files**
  action that opens `DuplicateResolutionDialog` over
  `LibraryMetadataRepairService.FindDuplicateGroups` and
  `LibraryScanner.RemoveTracksByPaths`. The first file of each group is kept by
  default, removal requires explicit confirmation, and deleting files from disk
  is a separate opt-in. Never remove anything without that confirmation, and
  invalidate the shared library view cache after a successful removal.
- The shared Folder structure sidebar item is visible when either local media or
  at least one Orynivo Server is configured. Server-only setups must be able to
  open `ShowUnifiedFolderTreeAsync` without configuring a local directory.
- The Genre Cloud item follows the same unified-library visibility. It loads the
  local snapshot and every configured Orynivo Server concurrently, merges counts
  by stable taxonomy key, applies client-side remote favorites and cross-source
  listening-history affinity, and resolves recommendations to ordinary
  source-aware `ContentRow` track rows. Recommendation scoring lives in the
  pure, tested `Orynivo.GenreRecommendationScore`; its tie-break variation must
  stay deterministic (stable hash, never `HashCode.Combine` or `Random`), and
  the perceptual background fingerprint lives in
  `Orynivo.Controls.GenreCloudImageFingerprint`. Its explanatory hero remains
  separate from the elliptical cloud surface below it; drill-down transitions
  cross-fade the old level and stagger the new count-scaled nodes. Cloud nodes
  use measured, centered rows with explicit horizontal/vertical gaps and must
  never overlap; excess rows scroll vertically. The recommendation mode uses the
  same segmented `ViewModeRadioTheme` controls as the Artists/Albums view
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
  the mode must rebuild the existing nodes without reloading the libraries. A
  remote snapshot whose `ParentKey` differs from the requested key is stale
  taxonomy data and must be rebuilt from that server's track facets. A selected
  leaf with candidates but no child nodes displays its own name centrally; it is
  not an empty-library state. The virtual `more-genres` node is localized while
  its dynamic children retain the actual unmapped library tag names. Each
  taxonomy level may render up to sixteen recommendation-ranked local and remote
  artist images as a non-interactive grayscale tile background. Keep it faded
  beneath a dark veil so node labels retain priority. Scale each image
  proportionally to fit completely inside its tile instead of center-cropping
  away substantial parts. Cache only the rendered mosaic for 24 hours under the
  data root; authenticated remote artwork URLs and API keys must never be
  written to that cache. The expensive merged nodes plus resolved track/album
  recommendations are retained separately in a bounded in-memory LRU cache keyed
  by taxonomy level and configured server identities. Each entry retains the
  generation of its completed load separately. The key may contain a
  process-local hash of a server URL for configuration invalidation, but never
  the URL or API key itself. Runtime rows are never persisted directly;
  sanitized restart DTOs use the separate preview contract below. Cached levels
  bind before revalidation; mode switches reuse resolved rows without source
  requests. Local watcher changes and changed remote `LibraryChangedAt` values
  increment the generation and reject in-flight cache ownership while retaining
  completed entries as stale previews. An in-flight load is shared and may
  finish after its original navigation is cancelled so an immediate return can
  reuse it. Settings > Appearance exposes an independent clear action for these
  rendered mosaics. It must not remove source artist images or any other remote
  artwork cache. The same section persists a background mode of None, Albums, or
  Artists (default). None must skip image resolution, network downloads, and
  rendering completely. Albums use the source-aware resolved recommendation
  album rows. Derive the column count and requested image count from the
  measured cloud-surface width, capped at 32 images. Dedupe decoded images by
  perceptual fingerprint so copies reached through different local or server
  identities are not repeated. Center sparse sets as one balanced row or two
  balanced rows; never repeat them merely to fill the available slots. A single
  row uses the complete mosaic height and derives tile width from its actual
  column count; two rows divide the height evenly. Preserve proportional fitting
  so this adaptive enlargement never crops or distorts an image. Keep the
  independent cache-clear action beside the mode selector in Appearance, and
  persist a zero-to-one opacity setting exposed as a 0–100% slider with a 50%
  default. Disable that slider while the None mode is selected. The cloud footer
  starts Infinite Mix from the genres represented by the current level. Below
  the root, store the selected parent branch in `InfiniteMix.IncludedGenres`; at
  the root, store every visible root branch. Candidate loading must expand each
  branch to itself plus every recursive descendant so directly tagged parent
  tracks and all subgenres participate. Open the normal profile dialog with
  these includes prefilled, then load those branch snapshots instead of relying
  on a bounded root snapshot. Reuse the normal initial-mix queue, progress
  overlay, active-playback preservation, and persistence path.
- Matching local and Orynivo Server artists use
  `ArtistNameNormalizer.CreateComparisonKey` and one `UnifiedArtist` row. Its
  album drill-down combines every matching library while retaining each album's
  source context. Opening one of those albums must pass the album row's
  provider-local artist ID into the shared album detail so its tracks initially
  remain scoped to the selected artist and the show-all-tracks checkbox remains
  available. Every non-Plex artist navigation entry point must use that unified
  drill-down even when the clicked track or row came from only one source. The
  unified row selects available biography and artwork from any matching
  identity. Profile downloads and manual image selections propagate to every
  matching local and reachable Orynivo Server identity; automatic profile images
  must never overwrite a manually selected image. Manual artist-image uploads
  and deletions propagate through the same identity set. Renaming a local or
  remote identity likewise renames every normalized matching artist in the local
  library and on reachable Orynivo Servers; target-name collisions remain
  unresolved until the user explicitly chooses a merge. A local merge's survivor
  choice must be mapped by identity role (current versus existing target) to
  equivalent collisions on every matching server. Artist rename synchronization
  captures the original server set and copied connections before its first
  await. Check configured source identity before lookup, after responses and
  before every rename or merge. Changed or removed sources report incomplete
  synchronization; added servers are excluded. Keep accepted data changes
  independent of view navigation. Do not roll back admitted writes or delete a
  changed source's artist-list cache. Album and artist detail upload/delete
  actions route through the owning local or remote artwork provider and use
  localized labels/tooltips. Plex identities remain separate.
- Manual artist-image upload/deletion synchronization copies its original server
  set before local work and retains configured source identity. Recheck after
  lookup, before each remote mutation and before cache cleanup; skip
  changed/removed sources and exclude newly added servers. Local workers check
  cancellation after opening SQLite and before writes; remote work checks
  between operations. Invalidate shared catalogs in finally after admission,
  including cancellation following a write. Never roll back admitted changes.
  Keep biography metadata intact. Dialog and hero publication require separate
  ownership guards; source-safe synchronization alone does not protect UI.
- Artist-detail image search/upload/delete adapters capture displayed
  local/remote/now-playing identity, source, sidebar/detail/name, profile
  observation and outer Back before selection or synchronization. Manual image
  actions have independent publication ownership; a newer action rejects older
  completion. Recheck after selection before mutation and after synchronization,
  decoding and row reads. Decode uploads off UI and dispose rejected bitmaps.
  Publish the retained hero and captured row only; pass the guard into local row
  refresh and guard errors and final status. Accepted data writes remain
  navigation-independent. Search adapts its captured query/result into the same
  publication flow and shares ownership with file upload. Retain direct search
  upload to the copied owning server and current failure feedback. Recheck after
  direct upload before further synchronization; invalidate catalogs after
  accepted direct writes and only clean the owning source cache while valid.
  `ArtistImageWriteQueue` reserves complete manual detail mutation batches by
  normalized artist name before waiting. Recheck the original action guard after
  preceding work completes and before any local or owning-server mutation. Hold
  ordering through file/SQLite work and complete remote fan-out; preserve the
  caller synchronization context. Capture copied destination sets before waiting
  and exclude newly added sources. Rejected, failed and cancelled batches
  release later work; remove idle entries. Unrelated artist names remain
  independent. UI publication remains separately guarded after queued data work.
  Unified automatic profile synchronization shares this queue across its
  complete local file/SQLite and remote batch. Capture source snapshots before
  waiting and recheck enclosing profile/image ownership before admission and
  between writes. A later admitted manual batch writes after the automatic batch
  finishes. The server artist-detail primary profile upload also joins this
  queue through UpdateArtistProfileInWriteOrderAsync. Recheck view/source and
  cancellation after waiting; form the image payload only after checking current
  image ownership. Biography-only uploads remain admitted for a current view
  after manual image supersession. Later manual writes finish last; hero/row and
  loading publication retain their separate guards. Local foreground profiles
  use DownloadLocalArtistProfileInWriteOrderAsync to hold the same queue through
  download, SQLite persistence and image-byte capture. Check view/cancellation
  before admission; determine image download policy after waiting. Keep accepted
  biography persistence after image supersession and read image bytes before
  releasing ownership so later manual cleanup cannot remove the file mid-read.
  Release before unified synchronization to avoid nested same-artist queue
  waits. Server artist-detail downloads use
  DownloadServerArtistProfileInWriteOrderAsync for download and image-byte
  capture under the same queue. Determine image policy after waiting and recheck
  view/source/image ownership after awaits; retain biography after image
  supersession. Refresh authoritative server manual-image state after waiting
  before external image lookup; unknown state must protect the image. Use
  ServerArtistProfileCacheIdentity with opaque server ID and source-local artist
  ID for negative download cache identifiers; never reuse positive local SQLite
  IDs for server profile image files. The server API still uses its real artist
  ID. Never derive this identity from URLs or credentials. Release before the
  separately ordered primary upload and unified synchronization, both of which
  recheck ownership. Now Playing passes its independent image guard and an
  ordered-write adapter into GetArtistProfileForViewAsync. Initial metadata
  reads remain outside mutation admission; superseded cached images return
  biography without image writes. Ordered data work refreshes server
  manual-image state after waiting, uses source-owned negative download cache
  IDs and retains source/playback/image guards between download, byte capture
  and upload. Forced biography refresh loads existing manual server artwork when
  available. Stage server-image cache bytes uniquely and recheck ownership
  before publication; rejected or cancelled staging must preserve previous
  cached bytes. Release before unified synchronization. Standard metadata
  callers keep the optional adapters unset. Automatic row jobs,
  Settings/context-menu and cross-window/ client ordering remain separate work.
- Foreground profiles observe admitted manual-image changes separately from
  dialog/action admission. Advance manual-image write ownership only after an
  accepted selection or at deletion admission; cancelled selection must not
  reject the profile image. Older profiles skip image/placeholder/status and
  retained-row image publication, preserve current manual row metadata and
  dispose rejected decodes. Biography/source/loading cleanup remain guarded by
  the original profile owner. Profile synchronization retains the additional
  image guard and skips later work after manual admission. Retain source-local
  artist identity and normalized name with the manual mutation; do not suppress
  a different artist opened during cancellation reentry, while equivalent
  local/server identities still share image protection. Already-admitted
  automatic file/server writes and row jobs remain separate acceptance work.
- Local artist edits capture displayed identity/title, sidebar/detail/name,
  profile observation and outer Back before background artist lookup. Recheck
  before opening a merge dialog and after its choice before transaction
  admission. Accepted transactions still synchronize and schedule search-index
  updates after navigation; guard UI, errors and list selection separately. Null
  filter/playback IDs are never matching merge targets. Playback updates retain
  track/provider context and reject remote ID collisions. Final failure status
  retains exactly the edit's next profile admission, never a replacement
  admitted by cancellation callbacks.
- Server artist edits retain displayed row identity/title, connection,
  sidebar/detail/name, profile observation and outer Back before dialogs.
  Resolve the row source without changing the active server, copy connection
  settings in memory and guard the edit callback before HTTP. Recheck merge
  admission and all post-await UI/metadata/profile work. Accepted writes still
  synchronize local metadata and invalidate shared caches after navigation;
  changed sources cannot admit further remote work or cache deletion. Playback
  publication retains the original row/provider and complete server identity.
  Refresh the retained detail directly; do not reload the server overview and
  hide its hero. Final failure retains exactly its next profile admission.
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
  matching local and Orynivo Server identities. The album strip below combines
  matching local and Orynivo Server identities and de-duplicates equivalent
  title/year cards while retaining the chosen card's source-aware navigation.
  Album cards open on double-click and pass their provider-local artist ID/name
  into the album detail so its initial track list remains artist-scoped and its
  show-all checkbox is available. The artist detail stays visible until loading
  finishes, and Back restores that same detail. Refreshing the profile must
  restore that unified album strip. The normal Back action returns to the
  originating view in one step; the transport info overlay remains independently
  closable.
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
  represented identity, and open their combined logical album detail. Below the
  album strip, the detail page shows one source-aware local/Orynivo Server track
  table ordered by album, disc, and track number. Its rows reuse the normal
  favorite, source, album-link, context-menu, and double-click playback
  behavior; track loading must not block profile rendering or discard already
  loaded album cards when one source fails. That embedded table uses the
  complete shared Tracks column set and a dedicated `ArtistInfoTracks`
  column-settings key, so header right-click selection, display order, and
  widths persist independently from the main Tracks table.
- `ShowUnifiedArtistAlbumsAsync` renders local albums before remote/profile
  work, loads server album sets concurrently, yields the dispatcher so cards
  paint, and starts profile/image resolution last. Its load version discards
  superseded results. Synchronous local provider calls and bitmap decoding must
  run through `Task.Run`; `ShowArtistInfoAsync` must never reset albums while a
  unified artist context is active. Local profile loads additionally retain
  request/token, sidebar tag, detail version/name and enclosing Back ownership.
  Install profile/background ownership before cancelling older work; validate
  after database, synchronization and decoding awaits and before errors/cleanup.
  Stage retained-row images before guarded metadata assignment and dispose
  rejected decodes. Top-level cancellation detaches artist-profile ownership
  alongside top/cloud/Plex before invoking callbacks, preserving replacements.
  Remote-library and now-playing profiles use the same admission ownership;
  retain configured source identity and reject changed/removed connections,
  credentials or server profiles. Now-playing publication also retains the exact
  track row, artist ID and metadata provider. Nested artist album/track loads
  check the enclosing guard before cards, playback metadata, tracks and failure
  cleanup. Rejected decoded profile images are disposed. Profile synchronization
  retains its enclosing guard between local reads, image saves, writes and
  remote awaits; stop later writes/cache cleanup after abandonment. Capture
  copied server connections and source guards before any await and skip changed
  sources. Already admitted writes are not rolled back. Automatic row profiles
  retain background CTS, sidebar tag, language, row identity/manual-image state
  and visible binding identity. Do not scan the entire bound catalog per
  completion. Guard before persistence, after decoding and before row
  publication; dispose rejected images. Per-artist loading markers retain their
  background owner so old completion cannot remove a newer marker. Keep the
  initial negative-result freshness stamp to bound retries. Remote now-playing
  profile resolution passes the enclosing guard into
  `GetArtistProfileForViewAsync`; the standard metadata interface delegates
  without a view guard. Copy source settings per request and retain original
  connection/profile identity. Recheck after cached profile, external download,
  image read, upload and server-image awaits. Reject abandoned work before the
  next operation or cache write and before returning a publishable profile.
  Already admitted operations are not rolled back. Keep source snapshots only in
  memory and preserve the existing server-owned cache paths. Refresh lookup
  selection captures displayed local/remote/now-playing identity, source,
  sidebar/detail/name, profile observation and outer Back before the dialog.
  NavigationLoadOwnership.CaptureCurrent observes without admitting or
  cancelling work; a later Begin/Invalidate rejects the observation. Recheck
  before selection and after its await, then refresh only the captured target.
  Do not restore an old unified name or run an uncancellable album reload after
  profile completion; profile refresh already preserves the unified strip.
- NavigationCaptureDecision selects typed capture routes from scalar inputs
  without owning rows, providers, credentials or history. Preserve search/cloud,
  logical-album, provider-bound album and artist precedence. Server albums
  opened from Dashboard use their catalog source and active server identity
  rather than a coincident local ID. MainWindow retains the one Back stack,
  state payloads, selection/scroll and restore adapters. Test decisions without
  a window and exercise the capture adapter with synthetic data before changing
  restore paths.
- NavigationRestoreDecision selects eligible restore adapters from scalar saved
  state. Preserve source discriminators, required detail identity and the
  existing top-level fallback. MainWindow keeps payloads, history and loaders;
  remote adapters retain source validation. Test colliding IDs and incomplete
  states without constructing a window.
- NavigationRestoreOwnership distinguishes the current asynchronous Back call
  chain from independent navigation. Only current nested work suppresses history
  capture; obsolete contexts cannot admit navigation or invalidate newer work.
  Install a new restore before cancelling prior loaders and recheck between
  cancellation adapters. Only its own completion clears the executing flag;
  ownership remains valid for layout callbacks until abandonment. Guard outer
  Back visibility and immediate/deferred selection. Top-level restore captures
  its exact admission token, generation and tag; remote artist restore also
  retains overview and filtered-load tokens. Configuration, Settings and close
  abandon restore work. Preserve the existing history stacks and source keys.
  Test overlap, independent navigation, same-tag reload, rejected new targets,
  nested search resets, new text input and deferred layout without rebinding.
- ServerNavigationTarget owns the existing server tag grammar and album-origin
  source matching without connection settings. Album capture retains an origin
  only for the owning server; otherwise use its Albums target. Remote restores
  reject malformed or unconfigured sources before UI/network work, capture the
  configured server and recheck its identity, current tag and configuration
  membership after awaits and in deferred selection/scroll callbacks. Never
  resolve a rejected source against ambient active-server state. Preserve legacy
  Root defaults and the one window-owned Back stack.
- Plex page, child and lazy-folder loads retain request/token ownership, copied
  connection settings, section, sidebar identity, enclosing root and outer Back
  context. Publish admission before cancelling older work; guard success,
  errors, tree expansion and playback registration after awaits. Capture paging
  offset/rows and child Back rows/title before source work. Independent Plex
  actions abandon root loading/fades; root cancellation also abandons Plex.
  Detach top/cloud/Plex cancellation sources and invalidate their ownership
  before invoking any cancellation callbacks, so replacements stay intact. Back
  clears obsolete Plex loading text and restores root paging availability. Plex
  history entry rows retain their owning server and explicitly omit a library
  section. Their base detail returns through global Back without a post-await
  Plex-stack clear; nested Back keeps library controls hidden there. Preserve
  the existing Plex stack and encrypted credential store. Child states retain
  the exact selected row and vertical offset before loading, avoiding Plex
  numeric ID collisions. Plex Back owns cancellation and deferred layout through
  NavigationRestoreOwnership; check between cancellation adapters and retain
  binding identity for deferred position restoration. Retain copied source,
  credential, section and tag identity in each in-memory Plex child state;
  validate it before rebinding rows and in deferred callbacks. Invalid child
  state returns through the existing global origin, or Tracks when no origin
  exists or its Plex source was removed. Never persist or log these snapshots.
  Global Back payloads may retain the existing Plex detail and parent states;
  capture only a published, visible detail and release the active Plex stack on
  independent navigation. Restore into that existing stack in original order.
  Read encrypted credentials off the UI thread and revalidate configured source
  and ownership afterward; invalid global snapshots fall back to Tracks. Retain
  root paging and mode alongside exact selection/position, and register only
  current restored track metadata. No additional stack or cache is permitted.
- NavigationLoadOwnership guards remote view request publication without owning
  history, rows or cancellation sources. Install new request/token before old
  cancellation callbacks and recheck ownership afterward. Top-level navigation,
  drill-down reset/push and catalog configuration changes abandon remote view
  work. Guard rows, facets, metadata registration, errors, status and finally
  cleanup by request, navigation tag, server identity and catalog generation.
  Remote loads release their counted loading contribution exactly once on
  cancellation or completion; marshal cancellation release to the dispatcher.
  Gate fade/status separately so balanced cleanup cannot leak loading depth.
  Never let an old completion hide a newer view's loading state. Test held local
  navigation and same-tag replacement with fake HTTP; separate detail loaders
  must retain their own publication ownership.
- Provider album track loaders reuse `NavigationLoadOwnership` and
  `ContentLoadingLease`. Capture server, copied album IDs and artist scope
  before awaits; pass cancellation and reject obsolete results before row
  mapping or playback registration. Guard request, catalog generation, active
  provider, album, tag and server identity. Navigation/reset/push/configuration
  changes cancel the scope; completion and deferred fades retain ownership. Test
  held same-album loads and source/configuration changes.
- Logical album opens and reloads reuse `NavigationLoadOwnership`; existing
  album abandonment cancels their token. Copy source parts, resolve copied
  server IDs to configured owners and capture artist scope before awaiting.
  Stage every source's tracks before mapping/playback registration. Guard
  request, generation, tag, active logical row/parts and configured source
  identity. Use the counted lease and guard header/rating admission, fade and
  opening caller's Back update. Artwork uses linked navigation/budget
  cancellation plus ownership checks before metadata work, write admission and
  row hydration. Successful writes invalidate caches in finally, even when
  refresh is abandoned. Notify a still-current owner of exactly one own
  generation advance; external invalidation must still reject publication.
  Preserve other callers' optional artwork adapters. Test held tracks,
  same-album overlap, metadata/write cancellation and own/external invalidation.
- Local album workers reuse `NavigationLoadOwnership` and `ContentLoadingLease`.
  Copy album IDs and artist scope before `Task.Run`; its SQLite worker must not
  read window state. Pass the request token for pre-start cancellation and guard
  generation, tag, album ID and local detail kind after awaiting.
  Already-running SQLite reads may finish but cannot publish abandoned rows,
  headers, rating refresh admission or fades. Return current ownership to guard
  the opening caller's Back-button update. Navigation/configuration/detail
  abandonment cancels local ownership with the existing album cancellation
  adapter. Test held dispatcher publication, same-album overlap and mutable
  input IDs.
- Remote album metadata opens reuse `NavigationLoadOwnership` with a separate
  window-owned token. Cancel them with provider detail abandonment. Capture
  server, fallback metadata, artist scope and copied IDs before awaiting; reject
  conflicting row sources before HTTP. Resolve copied catalog connection objects
  by exact source ID to the current configured server before admission; never
  require reference identity between a copied row source and settings. Guard
  request, generation, tag, active server and configuration membership before
  admitting detail. Install a new request before old cancellation callbacks.
  Back restore captures its exact overview token at admission, then keeps
  metadata request ownership through deferred selection/scroll. Never infer
  ownership from a matching tag alone. Test held metadata, same-album/tag
  overlap, captured fallback/scope and source conflicts; preserve the existing
  top-level wrapper and Back stack.
- Top-level view loads reuse `NavigationLoadOwnership` for loading/fade
  publication and install replacement tokens before cancellation callbacks.
  Detail reset/push and configuration changes cancel their scope.
  `ContentLoadingLease` releases an acquired counted contribution exactly once;
  UI release stays on the dispatcher. Views without an acquired scope must not
  decrement loading depth. Deferred fades check the owning request again. Shared
  overlay generation changes only when entering/leaving zero loading depth;
  nested scopes must not invalidate the active render. Test held complete
  top-level calls, same-tag replacement, deferred fades and nested rendering.
  Nested row loaders remain responsible for their own publication guards.
- Navigation state must distinguish local, remote, Plex, and unified
  drill-downs; numeric IDs from different sources can collide. Back restoration
  of the top-level Artists and Albums views must use the normal unified loader
  rather than binding `QueryRows` directly, and saved row selection must include
  its stable source key. Search navigation also saves the selected result's
  source and the outer result-page offset.
- Keep long mixed-library row composition off the visible `DataGrid` until the
  result is complete, unless a proven virtualized/paged strategy is used.
- Use shared typography, brushes, vector icons, control themes, loading helpers,
  and context-menu patterns from the existing application resources.
- The bulk action bar also offers a genre field. Local rows go through
  `AudioDatabase.SetTrackGenres`; Orynivo Server rows go through
  `OrynivoServerClient.UpdateTrackGenreAsync` to that server's
  `PUT /api/tracks/{id}/genre`. Both are library-only overrides that never
  rewrite media files, and failures are reported per selection.
- The shared content table supports multi-selection for track rows. Selecting
  more than one local or Orynivo Server track in a Tracks view reveals the bulk
  action bar (`BulkEditBar`) with **Mark as favorite**, **Remove favorite**, and
  a personal-rating selector. Local rows use the transactional
  `AudioDatabase.SetTrackFavorites`/`SetTrackUserRatings`; remote rows update
  the profile-scoped `OrynivoServerFavorites` container and the server rating
  API. The bar stays hidden for every other entity type or view and must never
  persist an authenticated playback URL.
- Automatic library backups are driven by `AppSettings.ScheduledBackup` (enable,
  interval days, retention count, folder, last-run timestamp) and the pure,
  tested `Orynivo.Library.BackupRetention` decisions. The low-frequency timer
  only checks; the actual export reuses `LibraryBackupService` and prunes
  archives beyond the retention count. Backups must never include audio files or
  credentials, only one run may be in flight at a time, and the backup folder is
  created on demand. Settings exposes the enable toggle, interval, retention,
  folder picker, **Back up now**, and the last successful run.
- Completed backup archives can additionally be uploaded to a WebDAV collection.
  `AppSettings.BackupTarget` holds the enable flag, URL, optional sub-folder,
  and user name; the password is overlaid from `ApplicationCredentialStore` and
  stays `[JsonIgnore]`. Only plain `http`/`https` URLs without embedded
  credentials are accepted (`Orynivo.Library.BackupTargets`), the upload itself
  is best effort and never removes the local archive, and credentials must never
  appear in a URL, a log, or an error message
  (`Orynivo.Library.BackupUploader`). The scheduled and **Back up now** paths
  share `MainWindow.TryUploadBackupAsync`; the explicit **Export library**
  action still writes only the user-chosen local ZIP.
- `AppSettings.MaxOutputSampleRateHz` caps the PCM output rate for exclusive
  WASAPI and ASIO/cwASIO; zero means automatic. Both players must honour it, the
  WASAPI cap only reorders the candidates (never removes them) so a device that
  supports nothing at or below the cap still plays, and the Settings control
  lists automatic plus the standard rates.
- A DSD source reports its 1-bit rate (352800 for DSD64), so its PCM conversion
  target must stay an exact division of that rate.
  `WasapiAudioPlayer.OrderCandidateSampleRates` prefers such a division that
  does not exceed the conversion hint, and both probes report the same 176400 Hz
  hint for DSD. Never let the exclusive-format chooser fall back to the device's
  maximum rate for DSD: a fractional ratio resampled DSD into pure noise on a
  Sound BlasterX AE-5. The device's reported exclusive-format set is not stable
  between queries: the same AE-5 reported 192 kHz and 384 kHz as supported in
  one run and neither in another, under .NET 8 and .NET 10 alike. Never make the
  DSD conversion depend on that answer alone.
- Before changing visualizer presentation or lifecycle, read the complete
  desktop chapter of [Visualizer contracts](../docs/VISUALIZER-CONTRACTS.md).
  OpenGL is the default supported GPU path; the bitmap/Skia CPU fallback stays
  available and failures must never interrupt playback.
- The desktop runs on Avalonia 12.1.2 with **compiled bindings enabled by
  default**; do not add `AvaloniaUseCompiledBindingsByDefault=false` back. Every
  `DataTemplate` and every item-binding scope needs an explicit `x:DataType`:
  - Put the item type on the **column or template**, never on the `DataGrid`
    itself. A grid-level directive also applies to the grid's own bindings
    (`ItemsSource`, `IsVisible`), which then fail against the item type.
    `DataGridTemplateColumn` cell templates do not inherit a grid-level
    directive, so they need their own.
  - A view model that XAML binds must be a **top-level type**; a nested or
    private type cannot be named in `x:DataType`. That is why
    `RadioStationViewModel`, `PodcastViewModel`, `PodcastEpisodeViewModel`,
    `LyricLineViewModel`, `DailyHistoryRow`, `MetadataProblemRow`,
    `MetadataRepairHeaderViewModel`, `MetadataRepairPreviewRow`,
    `TrackInfoEntry`, and the three search-window result view models live in
    their own files, and why the metadata dialog uses a named header record
    instead of an anonymous type.
  - `ContentRow` and `LogicalAlbumPart` are top-level types too; there is no
    `{ReflectionBinding}` left in the views. Keep it that way: a row model bound
    from XAML must never move back into `MainWindow`. SkiaSharp moves with
    Avalonia: use `SKSamplingOptions`/`SKFont` instead of the removed 2.88 text
    and sampling APIs on `SKPaint`.
- Cross-device resume for remote Orynivo Server tracks lives in
  `MainWindow.CrossDeviceResume.cs`. The last audible position is published at
  most every 20 seconds and only through the authenticated, profile-scoped
  `SaveTrackPositionAsync`, and the **Resume** transport action appears only
  when the pure, tested `Orynivo.Library.CrossDeviceResume.ShouldOffer` accepts
  the stored position. Never persist a credential-bearing stream URL for this,
  never block playback on a publish or fetch failure, and clear the prompt when
  the now-playing remote row changes.
- Optional UI motion is governed by `AppSettings.ReduceMotion` through the pure,
  tested `Orynivo.Controls.MotionPreferences` helper. The Genre Cloud, the
  Dashboard cover stage, and `KaraokeWindow` must ask that helper instead of
  building transitions or frame loops directly, and `KaraokeWindow.ReduceMotion`
  is assigned from the saved setting when the window opens. The album and artist
  artwork grids open the selected card on Enter or Space through
  `ActivateArtworkCardAsync`, and transport controls keep an
  `AutomationProperties.Name` that matches their localized tooltip.
- Enhanced-LRC word timestamps (`<mm:ss.xx>`) are parsed by
  `LyricsService.ParseLrc` into `TimedLyricLine.Words`, which also strips the
  markers from the line text. `KaraokeWindow` renders such a line as one `Run`
  per word and repaints only the active slot when the word changes, so the
  surrounding opacity/font-size transitions keep animating. Plain synchronized
  lines keep the line-level highlight through `LyricLineSelector`, which is also
  the word-level selector.
- The lyrics view's **Karaoke** action opens `KaraokeWindow` fullscreen. It
  shows a fixed window of synchronized lines around the active one, emphasizes
  the active line, and animates opacity and font size through `Transitions`; the
  main window pushes positions from its existing transport timer and never adds
  a second polling loop. Karaoke requires synchronized lines, closes on Esc, a
  click, or whenever the lyrics are cleared, and the active-line lookup must
  stay in the pure, tested `Orynivo.Library.LyricLineSelector`.
- The Dashboard **Year in review** action lives in the Listening stats card
  directly below its period selector, so both share one context. It opens
  `YearInReviewDialog`, which renders the existing year aggregates from
  `AudioDatabase.GetYearInReview` and exports the visible card through
  `RenderTargetBitmap`. It must only read playback history, must load each year
  off the UI thread, and must keep the export bounded to the rendered card. A
  past year must never include later listening: the shared top-genre, album, and
  artist queries take an optional exclusive upper time bound for that reason.
- The estimated musical key travels with the compact track data:
  `TrackListInfo.CamelotKey` feeds `ContentRow.CamelotKey` (local and remote
  providers), the shared track-column set exposes it as the optional
  `camelotKey` column for Tracks, Up Next, and playlists, and **Show track
  information** lists it. Never recompute the key in the UI, and never persist a
  key that did not come from the cached analysis. Settings > Playback owns the
  explicit **Analyze audio features** action; it runs the local library through
  `AudioFeatureMaintenanceService` and requests bounded batches from each
  configured server, stays cancellable, and cancels on Settings deactivation.
- Last.fm scrobbling mirrors the transport favourite button through
  `LastFmScrobblingService.SetTrackLoved` and `LastFmClient.SetTrackLovedAsync`
  (`track.love`/`track.unlove`, artist and track only). The call is best effort:
  it never blocks playback, is skipped for items without an artist and title,
  and is not queued while offline. `BuildLastFmTrack` is the single place that
  builds the metadata for both now-playing and love, so an untagged item never
  produces a request Last.fm rejects.

- The year-in-review export shares one pure content model,
  `Orynivo.Controls.YearInReviewLayout` (title, headline, monthly bar ratios,
  and the leading sections). The Avalonia dialog renders it as controls; the PDF
  export draws it through SkiaSharp in `YearInReviewPdfExporter`, which must
  stay bounded to one A4 page, offline, and free of new data collection.
  SkiaSharp is used through Avalonia.Skia's pinned 3.119.4 dependency, so do not
  add a separate SkiaSharp package reference to the desktop project.

- The Infinite Mix profile editor offers Focus/Workout/Wind down presets through
  the pure `Orynivo.InfiniteMixPresets.Apply`, which only pre-fills the mood,
  discovery level, history period, and weighting and must preserve the server
  selection, genre filters, feedback, and exclusions. Descriptor-based preset
  scoring stays in the context-menu activity mix
  (`SimilarityFeatureService.RankPreset`); the Infinite Mix profile itself
  remains metadata-based because the genre-cloud candidate payload carries no
  acoustic descriptors.

- Podcast downloads live in `PodcastDownloadService` beneath the per-user
  `podcast-downloads` cache. `PodcastDownloadCache.BuildCacheFileName` derives a
  stable hashed name per podcast and episode key, playback prefers the cached
  file and marks it used, and `EnforceLimit` evicts through the pure
  `PodcastDownloadCache.SelectForEviction` (least recently used first, newest
  always kept). The limit is `AppSettings.PodcastDownloadLimitMb`. Episode rows
  carry a download marker and their own context flyout, attached from
  `TrackDataGrid_OnLoadingRow` when the row data context is an episode.
- `ReferenceTrackPickerDialog` is the shared search dialog for picking a
  similarity reference track. It never touches the database or the network
  itself: the caller supplies the search through `Search` (the editor receives
  it via `ReferenceTrackPicker`), and `MainWindow.SearchReferenceTracksAsync`
  queries the local index plus every configured Orynivo Server, returning only
  credential-free `local`/`server:<id>` identities.

- The smart-playlist editor (`SmartPlaylistDialog`) must show every stored
  criterion and must never drop a criterion it cannot rebuild from its own input
  fields. The similarity reference is displayed with a readable track label
  (track title and artist for local references, server name for remote ones) and
  is carried across a save through the pure, tested
  `SmartPlaylistCriteriaEditing.ResolveSimilarityReference`; it is removed only
  through the explicit **Remove reference** action. The reference label lookup
  runs off the UI thread.
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
- Full server track catalogs must request at most 5,000 tracks per page,
  matching the server cap; requesting more falsely signals the end of
  pagination. Track cache schema version 2 and versioned artist/album cache
  identities reject legacy potentially incomplete snapshots. Every full-catalog
  disk-cache writer must use strict requests and finish every page successfully.
  Reapply current client favorites after loading cached tracks.
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
  than being collapsed into the "no update" state. Starting a desktop update
  from About first relays the matching signed release to every reachable
  update-enabled configured server. Failed servers are named and require an
  explicit choice before the platform installer continues.
- Linux desktop updates map Arch-family distributions (including CachyOS) to the
  signed `arch` package, Debian-family distributions to `deb`, and RPM-family
  distributions to `rpm`. After digest verification, installation must run
  through `/usr/bin/pkexec` and the distribution package manager; a downloaded
  package must never be launched as an executable. The client must await the
  privileged package-manager exit and shut down only after exit code zero;
  authentication cancellation and package-manager failures keep Orynivo open and
  are logged.
- Tagged macOS releases publish architecture-specific `osx-arm64` and `osx-x64`
  application bundles as installable PKGs, portable ZIPs, and tar archives. The
  PKG installs `Orynivo.app` beneath `/Applications`; all package variants must
  remain required inputs to the signed release manifest. Each bundle must
  contain `Contents/Resources/Orynivo.icns` generated from `Logo/icon.png` and
  referenced by `CFBundleIconFile` in `Info.plist`. macOS automatic updates
  select the current architecture's signed PKG, verify its digest through
  `ReleaseUpdateService`, and open the verified file through `/usr/bin/open`; do
  not invoke `installer` or request privileges directly.
- Settings must hide Steinberg ASIO and cwASIO subsystem badges on non-Windows
  platforms. FFmpeg and other genuinely cross-platform subsystem badges remain
  visible.
- `AppSettings.ShowAiChatItem` controls the AI Chat sidebar item's visibility
  from Settings > Appearance, defaults to visible, and is applied with the
  existing Internet Radio, Podcasts, and Up Next item toggles.
- `RefreshQueueRows` must copy a registered remote track's `OrynivoServer`
  context together with its display metadata. Otherwise the shared source column
  mislabels that queue row as local. Keep this context memory-only and never
  persist its authenticated playback URL or API key.
- The Up Next table uses the same selectable track-column set as the Tracks
  view. Album and artist cells remain navigation links, and queue refreshes
  during track transitions must restore the prior pixel scroll offset.
- Interface languages include German, English, French, Spanish, Russian, and
  Simplified Chinese (`zh-CN`), and Hindi (`hi-IN`). New visible strings must be
  added to every complete built-in language resource in LocalizationManager.cs.
  All seven languages use the same constructor/property schema; do not add
  overlays or inherit English text for untranslated entries. Dynamic navigation
  headers and the open Dashboard must be rebuilt after a runtime language change
  so they cannot retain labels from the previous language. Keep format
  placeholders intact. Run scripts/verify-localization-parity.ps1 from the
  repository root to check all seven resource sets and the website.
- Track context menus, including Up Next, expose **Show track information**; the
  modal lists the physical file path first and then every selectable track
  metadata field without exposing authenticated remote playback URLs. Remote
  Orynivo Server paths use the configured server name as a prefix.
- Persisted Up Next entries from Orynivo Server must use stable
  `orynivo://serverId/track/trackId` references rather than credential-bearing
  stream URLs. Restore those references asynchronously after startup and resolve
  them only when playback or metadata requires it. Build and parse these
  references (and the `orynivo-album:serverId:albumId` drag references) only
  through the tested `Orynivo.PlaylistReferences`; never inline the format.
- The single-track **Play more like this** action may combine local and Orynivo
  Server similarity vectors, but it must load them asynchronously, resolve each
  provider-local result through its owning catalog, and keep server URLs and
  credentials out of feature vectors and persisted identities. Its transient
  ranked candidates refill through the normal Infinite Mix threshold; stopping
  or starting a regular mix must clear that transient profile. Quick mood mixes
  use the same transient continuation path and must begin with the track whose
  context menu launched the action. Compact vectors use one coalesced
  five-minute memory cache; unified catalog invalidation must also invalidate
  this cache, and diagnostics may log only aggregate vector counts and elapsed
  time, never library metadata. After a similarity or mood mix is queued,
  navigation must switch to the Up Next view so the generated queue is
  immediately visible.
- Infinite Mix refills own a mix version, captured playback intent and
  preparation lease. Stop, mix pause and replacement cancel publication; guard
  queue mutation, provider metadata registration and similarity cursor changes
  after awaits. Only the owning version clears loading/progress state. Full
  release drains pending refills; idle initial start awaits readiness. Keep
  metadata conversion on the dispatcher and suppress threshold refill without an
  active player or during shutdown.

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
  interaction; threshold refills remain unobtrusive in the background. Starting
  a mix during active playback must retain the audible item at queue position
  zero and append recommendations behind it. Initial refill must not refresh the
  active gapless session while the blocking overlay is visible; after the
  overlay closes, refresh through `StartPlaybackAsync` with its
  `initialPosition` argument so no audio from position zero is replayed. The
  profile dialog is resizable with a bounded minimum size, and its scroll
  content reserves a right-side gutter so overlay scrollbars never cover text or
  inputs at any supported display scale. Included and excluded genres use
  removable chips. Their type-ahead suggestions load asynchronously from the
  currently checked local/server sources; unavailable servers must not block the
  dialog, and arbitrary custom genre values remain valid. Batch diversity is
  progressive: first avoid recent artists and repeated albums, then permit
  artist repetition, and finally permit further eligible tracks from represented
  albums. Narrow genres with only a few albums must still fill the requested
  batch from their remaining candidates. Repeated refills rotate through the
  stable provider candidate order instead of querying the same bounded prefix
  forever; active playback also performs a throttled lightweight threshold check
  so refill is not dependent on queue-view events and an empty/error refill does
  not create a tight request loop.
- Remote album lists are cached by `LibraryChangedAt`, but artwork mutations do
  not advance that scan timestamp. Every successful remote album artwork upload,
  reassignment, or deletion must therefore call `DeleteOrynivoAlbumListCache` so
  Genre Cloud and other later album views reload current artwork metadata.
- Local album artwork writes generate cache files and Skia thumbnails and must
  run off the UI thread through `LocalLibraryCatalogProvider`. After assignment,
  update the bound `ContentRow` in place; do not rebuild the complete unified
  Albums view merely to display the new bitmap. Album detail may reload only its
  compact header row.
- macOS must configure `AvaloniaNativePlatformOptions.RenderingMode` with OpenGL
  first and software second. Do not re-enable Metal without verifying Orynivo's
  gradient and rounded-surface shaders on both Intel and Apple Silicon Macs;
  Skia's Metal compiler can reject them after its 300-ms timeout.
- FFmpeg discovery on macOS must not rely only on the inherited `PATH`, because
  Finder-launched bundles omit common package-manager prefixes. Probe the
  application and per-user cache plus conventional Homebrew, MacPorts, pkgsrc,
  Fink, and per-user binary directories. When absent, download
  current-architecture FFmpeg and FFprobe release assets into the per-user cache
  and restore executable Unix permissions before prepending that cache to the
  process `PATH`.
- Dashboard Recently Played and Recently Added use 20-item horizontal carousels
  with smoothly animated vector previous/next controls placed in the header
  immediately before Show all; controls must never overlay the cards or change
  visibility. At either end they remain reserved, disabled, and visually muted
  so the header layout cannot shift. Keep a clear gap before Show all. Their
  Show all views contain up to 100 items.
- Dashboard builds write one sanitized phase summary to the bounded rolling
  `logs/dashboard-performance.log`. Keep this persistence off the UI thread and
  never add media names, paths, server URLs, API keys, or other credentials to
  the diagnostic payload.
- Dashboard retains four section hosts (hero, media, recommendations, stats) and
  refreshes their data independently. Compare display fingerprints before
  rebuilding; unchanged controls retain scroll and animation state. Rebuild
  retained data immediately on language, theme or responsive-layout changes,
  without awaiting network data. Coalesce in-flight expensive source phases.
  Guard section publication by build, root navigation, catalog generation and
  outer Back ownership. The shared Dashboard surface must never replace Show all
  pages after navigation.
- Dashboard/Genre Cloud bypass the blocking content overlay and final whole-view
  fade. Render retained content before awaiting revalidation. Genre Cloud keeps
  stale bounded session entries after ordinary catalog notifications, but clears
  them on profile/source changes. Only complete refreshes replace retained cloud
  data; keep mode switches on existing resolved rows without fresh catalog work.
- ViewPreviewStore owns atomic off-thread restart storage: 64 files, at most 4
  MiB per file, seven-day reads. Persist only sanitized display DTOs and opaque
  source IDs, never settings objects, API keys, authenticated URLs or bitmaps.
  Reconstruct remote paths from current configuration on admission. Profile,
  source and authentication changes reject older writes and clear previews;
  navigation alone must not discard completed background cache work. Corrupt,
  missing or oversized previews are cache misses. Test restart/held HTTP,
  unchanged/changed data, failed refresh, configuration and secret exclusion
  against synthetic data; do not infer a portable latency budget from these
  ordering checks.
- The complete unfiltered shared Artists, Albums, and Tracks results use a
  three-entry LRU session cache. Configured-server identity and a generation
  form the key; library-version, watcher, favorite, and artwork changes advance
  the generation. Independent remote servers load concurrently, and local
  provider database work must not run synchronously on the Avalonia UI thread.
  `LibraryLoadResult` retains explicit per-source success, failure, timeout, and
  cancellation outcomes; a successful empty source is not a failure. Cache only
  complete results, capturing the catalog generation before loading and
  rejecting late writes atomically in `LibraryViewCache`.
  LibraryCatalogLoadCoordinator owns the one bounded cache, load versions and
  captured request generations. Its source adapters return typed outcomes;
  shared-view server fan-out uses LibrarySourceBatchLoader with concurrency
  equal to the configured source count (at least one), retaining existing
  all-server admission and independent 20-second budgets. Server adapters return
  raw rows; only the scheduler creates sanitized terminal outcomes. Do not nest
  another source budget around those adapters. Composition tests cover more than
  three servers, ordered partial outcomes, invalidation and empty recovery.
  Independent local/remote loads combine in supplied order and reject obsolete
  or cancelled publication. The window retains source/filter adapters,
  sort/merge, metadata registration and binding. LibraryCatalogLoadContext
  copies track selections, entity favorite filters, local artwork/drilldown
  choices, remote favorite identities and server settings before source work.
  Remote follow-up requests and cached catalog favorite projection must use
  those copies. Local profile identity flows through AudioDatabase's AsyncLocal
  into Task.Run; never change the caller's profile to execute an old load.
  Contexts are transient and must never be serialized or logged.
  LibraryCatalogConfiguration compares every ordered server field, including
  profile and streaming options. ApplySettingsAsync must call
  InvalidateCatalogConfiguration immediately after replacing changed server
  settings and before later awaits. This shares the profile-change invalidation
  path for Dashboard, Genre Cloud, unified views and similarity; independent
  search/playlist publication is abandoned by the unified invalidator. Test each
  configuration field and held worker profile/scope isolation. Publish new
  request/token ownership before cancelling old work and recheck after
  callbacks. Search, playlists and Mobile read the same catalog generation
  through the coordinator; they retain independent request ownership. Test
  overlap, invalidation, recovery, eligibility and LRU behavior without a
  window. Navigation cancellation and load versions reject stale publication;
  register remote playback metadata only after that guard. Partial/failed shared
  views expose a localized notice with an unavailable-source count and in-place
  retry. Keep usable rows interactive while retrying, suppress duplicate
  retries, and retain source-aware selection and scroll position at publication.
  Deferred restore callbacks must reject abandoned loads; runtime language
  changes refresh the notice. Successful empty catalogs and navigation
  cancellation show no failure notice. Global and server-scoped library searches
  share cancellation/version ownership, captured source/profile/favorite/filter
  context, strict full-search reads, and at most three concurrent server loads.
  Source time budgets start after admission. Text changes cancel before
  debounce; navigation, Settings, imports, window close, and catalog/profile
  changes reject pending publication and remote metadata registration. Keep
  available categories when a source fails; show incomplete-search text instead
  of definitive no-match text for empty categories, with an in-place retry.
  Deferred Back/retry scroll restores must guard their owning search. Empty
  source selections are successful but never eligible for source-backed catalog
  caching. Outcome metadata and diagnostics contain no exception text, media
  paths, server names, URLs, or credentials. Any local scan launched from
  Settings must raise `SettingsView.LocalLibraryChanged` after a successful
  catalog mutation so the same Dashboard, Genre Cloud, and unified-library
  caches are invalidated as for watcher-driven changes; never require an
  application restart to see new rows. Watcher callbacks run on workers: only
  Dashboard/Genre Cloud/unified snapshot invalidation may run immediately there.
  Coalesce search/playlist cancellation, similarity cleanup, notices and refresh
  hints on the dispatcher. Capture independent request versions before
  invalidation; only cancel each captured version if still current. Never let a
  delayed notification cancel a newer search or playlist. Test background bursts
  and newer admission independently. Both Settings root-change paths use
  CleanupLibraryRootsAsync: copy roots before worker admission, run removal off
  UI, and notify OnWatchedLibraryChanged only after a successful nonzero
  removal. No-op/failed cleanup must not report a catalog mutation. Import
  preparation invalidates all shared catalog configuration caches, cancels
  shared-load tokens and clears their outcome/retry notice before archive
  replacement, in addition to existing search/playlist and playback abandonment.
  Test held pre-cleanup and pre-import loads with synthetic SQLite and HTTP.
- Genre Cloud foreground requests retain their own admission token, taxonomy,
  catalog/source identity, enclosing top-level load and outer Back context.
  Publish new ownership before cancelling older work; recheck after callbacks
  and awaits. Branch/mode actions abandon enclosing root loading/fades without
  cancelling their new foreground request. Top-level cancellation also abandons
  foreground cloud work. Shared snapshot tasks remain navigation-independent,
  copy server connections and map rows without playback registration. Only the
  current view registers recommendation metadata. Background resolution and
  decoding retain foreground and configured-mode guards; dispose rejected
  decoded images. A mode click transfers pending Back work through the same
  cache instead of reloading complete libraries.
- Genre Cloud Loaded layout and staggered animation callbacks retain binding,
  cancellation, catalog/source, taxonomy, navigation and outer Back ownership.
  Removed buttons must never animate after a mode rebuild; hidden or abandoned
  bindings must not arrange a newer cloud.
- A cached Genre Cloud level renders immediately and must not retain the
  first-load branch-transition delay.
- Uncached cloud admission immediately hides the previous level/results and
  displays localized assembly feedback. Percentages count completed source and
  assembly steps with CompletedWorkProgress, never fabricated time-based
  increments. Shared work owns its counter; UI polling/cleanup retains
  foreground request identity. Stop polling on cache publication, cancellation,
  failure and navigation; obsolete cleanup must not affect newer loading or
  another view.
- `AppSettings.LastMainView` persists every selectable sidebar leaf tag, not a
  hard-coded view subset. Section and library-group containers, empty hints, and
  disabled Plex server headings are never persisted. Synchronously built dynamic
  rows restore normally; a Plex library tag remains pending while Plex
  navigation loads asynchronously, with Tracks as the temporary/safe fallback.
- Dashboard Recently Played cards show the persisted album below the artist.
  When the history identity can resolve a local, Orynivo Server, or Plex album,
  the album name opens that source's album detail without triggering card
  playback. The full Recently Played view must batch-resolve missing local
  history IDs so older entries expose the same artist and album links as newer
  entries.
- A title action in `DailyHistoryDialog` starts the selected history entry
  directly through `PlayHistoryEntryInPlaceAsync`. It must never navigate to,
  bind, or `ScrollIntoView` the complete unified Tracks table first; that path
  can block Avalonia's UI thread for large mixed libraries. Album and artist
  actions retain their source-aware navigation behavior.
- Shared local and Orynivo Server track rows carry a personal zero-to-five
  rating plus cached MusicBrainz recording rating metadata. The interactive star
  column persists through the owning database/API. MusicBrainz lookup runs on
  the client, prefers the recording MBID, and accepts an artist/title fallback
  only when optional duration filtering leaves one exact result. Server scans
  must preserve client-resolved recording MBIDs and all rating fields. Album
  detail rendering starts a cancellable refresh only after track rows are bound;
  values fetched within 30 days must not be queried again. Resolve missing MBIDs
  conservatively and persist every unambiguous identity, then fetch each rating
  through the direct recording lookup; MusicBrainz batch search rating fields
  are not reliable enough to cache. De-duplicate known MBIDs across mirrored
  local/server album rows before issuing direct lookups; use the tested
  `Orynivo.MusicBrainzRatingGrouping.Partition` for that case-insensitive
  grouping instead of ad-hoc `GroupBy`/`Guid.TryParse` filters. The rating cell
  displays a localized **Load rating** action before its first lookup, a loading
  state while active, and **Try again** after a temporary failure. Album-detail
  foreground refresh retries temporary failures up to three times. A successful
  direct lookup without a community score displays the distinct localized
  not-rated state and must not be retried continuously. Direct lookups request
  supplemental MusicBrainz genres and tags. Persist them through the owning
  local database or remote rating API, then incrementally update the
  local/server Lucene document; never replace the row's embedded genre or
  contact MusicBrainz from a normal library scan. A single client-side
  background enrichment worker starts on first playback, covers the local
  library and every configured Orynivo Server, and advances only while playback
  is active and not paused. Album-detail and explicit rating requests increment
  the foreground gate so the worker yields between requests; all calls still
  share the MusicBrainz service throttle. Known recording lookups use the 30-day
  cache lifetime. An unresolved conservative metadata lookup persists its
  attempt timestamp and is retried after 90 days. The worker must never block
  the UI thread or run from a normal library scan.
- Dashboard album recommendations rank compact local and Orynivo Server album
  candidates against genre listening time from the selected history period.
  Already-heard albums are de-emphasized, and the optional mood selector applies
  a genre/BPM preference rather than excluding all non-matching candidates.
  Recommendation cards retain their source context and use the shared album-card
  navigation. Their default presentation is the taller circular cover stage: the
  centered album is full-size, neighboring albums are progressively scaled,
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
- Audio routing invariants remain: native ASIO/cwASIO DSD is bit-perfect;
  volume, ReplayGain, PCM boost, and equalization affect PCM paths only.
- The persisted DoP preference and forced DSD-to-PCM conversion are mutually
  exclusive. DoP is bit-perfect encapsulation and must bypass PCM gain and
  equalizer processing. Linux local and HTTP-range-streamed stereo DSF DoP uses
  `Compatibility/Linux/DsfDopAudioPlayer` and
  `Compatibility/Linux/RemoteDsfDopAudioPlayer` with direct ALSA only; preserve
  their alternating marker bytes and DSD-rate/16 carrier-rate calculation.
  Seeking must serialize ALSA `drop`/`prepare` against writes, discard blocks
  read before the seek generation changed, and restart markers with `0x05`. ALSA
  `S32_LE` DoP frames place low padding first, then two DSD bytes, and the
  marker in the most-significant byte. Prefer ALSA `DSD_U32_BE` at DSD-rate/32
  when the selected hardware endpoint supports it; use DoP as the fallback. DSF
  files whose format chunk declares `bitsPerSample == 1` store each DSD byte
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
  Linux additionally enumerates direct ALSA `hw:` devices through
  `libasound.so.2` and selectable OpenAL devices through `libopenal.so.1`. The
  output-profile dialog presents these as separate output types and classifies
  persisted profiles by their device ID (`alsa:` means direct ALSA); never mix
  direct ALSA hardware into the OpenAL device list. Direct ALSA uses stereo
  signed 32-bit PCM, the source rate, and `soft_resample=0`; failure to open
  that exact format/rate must be reported rather than silently falling back to
  resampling. An `EBUSY` open failure must identify PipeWire/other-process
  ownership and explain that changing the desktop default does not release the
  card. The shared device-information window must use ALSA/OpenAL terminology on
  Linux and must not expose its historical WASAPI labels there. OpenAL requests
  the source rate when creating the context, then queries the negotiated OpenAL
  mixer rate and uses that value for FFmpeg decoding and transport output-rate
  reporting. Compatibility types must not persist credentials in plaintext or
  claim unavailable WASAPI, ASIO, endpoint-volume, or SMTC capabilities.
  Cross-platform behavior shared with the server belongs in `Orynivo.Core`.
- The Linux target exposes system-media control through MPRIS 2 instead of SMTC.
  `Orynivo/Compatibility/Linux/MprisMediaTransport.cs` registers
  `org.mpris.MediaPlayer2.orynivo` on the session bus and is compiled only under
  the `ORYNIVO_LINUX` define, because `Tmds.DBus.Protocol` is referenced on
  Linux only. It connects asynchronously and must stay inert (never throw into
  playback) when the session bus is unavailable. The non-Windows
  `WindowsMediaTransportService` forwards to it so the shared transport methods
  remain the single source of playback state. MPRIS volume changes route through
  the existing `VolumeSlider`/`ApplySystemVolume` path and are echoed back with
  `SetVolume`. Never publish a credential-bearing artwork URL (`?key=`, Plex
  token, user info) through `mpris:artUrl`; gate remote artwork URLs through the
  shared `QueuePathPolicy.CanPersist`. Remote Orynivo Server covers must instead
  use the locally cached `remote-artworks/track-art-<server>-<track>.img` file,
  and the now-playing media metadata must be refreshed after the asynchronous
  artwork download completes so the credential-free local file is published.
  Keep the `VolumeChangeRequested`/`SetVolume` members on the Windows
  implementation as no-ops so the shared `MainWindow` code compiles for every
  target.
- The Windows installer shortcuts must carry the same `Orynivo.AudioPlayer`
  application user model ID that `App.xaml.cs` assigns to the process. Windows
  uses that identity to attribute the SMTC media session to Orynivo instead of
  showing an unknown application.
- Keep the Linux-only direct `Tmds.DBus.Protocol` dependency at 0.92.0 or newer
  within the compatible package line: its non-blocking observer dispatch avoids
  a shutdown race with Avalonia's stopped UI dispatcher.

Consult the detailed matching sections in `docs/PROJECT-REFERENCE.md`
(repository-relative) before changing audio, queue, Dashboard, playlists, remote
libraries, settings, or table/tree UI.
