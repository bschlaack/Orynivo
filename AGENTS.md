# AGENTS.md

## Project Overview

Windows audio player with:

- Cross-platform core library in `Orynivo.Core/`
  (library scan, database, search, streaming models)
- Avalonia UI 11 frontend in `Orynivo/` (Windows-only; references `Orynivo.Core`)
- Cross-platform headless music server in `Orynivo.Server/`
  (ASP.NET Core; references `Orynivo.Core`; exposes REST + streaming over the
  local network)
- Native Steinberg ASIO bridge in `Native/AsioBridge/`
- MIT-licensed cwASIO bridge in `Native/CwAsioBridge/`
- PCM playback through `ffmpeg`
- Native DSF/DFF DSD playback through ASIO
- Real-time DSF/DFF-to-PCM conversion through `ffmpeg` for WASAPI playback

## Orynivo.Core

`Orynivo.Core` is a `net8.0` class library with no platform-specific dependencies.
It holds everything needed to scan, store, index, and serve the music library:

**Orynivo.Core/Library/**: `AudioDatabase`, `LibraryScanner`, `LibraryWatcherService`,
`LibraryBackupService`, `TrackSearchIndex`, `CueSheetParser`, `M3u8PlaylistService`,
`LyricsService`, `RadioBrowserService`, `RadioStreamMetadataService`, `PodcastService`,
`ArtistProfileService`, `ArtistImageSearchService`, `ArtistNameNormalizer`,
`ArtworkCache`, `MusicBrainzCoverSearch`, model records, `ArtistInfoSource` enum.

**Orynivo.Core/Audio/**: `ReplayGain`, `ReplayGainMode`, `EqualizerApoParser`,
`ParametricEqualizer`, `FfmpegPcmDecoder`, `FfmpegLocator` (cross-platform:
auto-downloads FFmpeg on Windows; expects system-installed FFmpeg on Linux/macOS),
`WaveformCache` (cached compact FFmpeg-generated peak data for the transport
waveform), `SeekDiagnostics` (sanitized transport seek, FFmpeg decoder, and
server-side transcode diagnostics under `logs/seek.log`), `EqualizerProfile`,
`EqualizerFilter`, `EqualizerFilterType`.

**Orynivo.Core/Web/**: `WebBrowsingService` (SSRF-guarded page fetch + SearXNG
search), `WebBrowsingOptions` (persisted config), `HtmlContentExtractor`
(HTML→text/Markdown). Backs the `search_web`, `fetch_page`, and
`fetch_page_as_markdown` tools for MCP and the AI chat.

**Orynivo.Core/Streaming/**: `IStreamingCatalog`, `IStreamingPlaybackProvider`,
`IStreamingCredentialStore`, `StreamingModels` (all streaming model records,
enums, `PlexServerSettings`, and `OrynivoServerSettings`), `PlexServerClient`,
`OrynivoServerClient` (HTTP client for browsing and streaming a remote Orynivo Server
library — provides `GetArtistsAsync`, `GetAlbumsByArtistAsync`, `GetAlbumsAsync`,
`GetTracksByAlbumAsync`, `GetTracksAsync`, `TestConnectionAsync`, and static URL
helpers `GetStreamUrl`/`GetAlbumArtworkUrl`/`GetArtistArtworkUrl`; also uploads
client-selected album and artist artwork through `UploadAlbumArtworkAsync` and
`UploadArtistImageAsync`; remote track DTOs mirror the local list metadata used
by shared table masks, including genre, track/disc totals, composer, BPM, file
size, added date, primary `ArtistId`, and ReplayGain fields when the server
supports them. It also exposes `GetArtistAsync` for a single cached artist
profile and per-track lyrics caching via `GetTrackLyricsAsync` /
`UploadTrackLyricsAsync`).

**Access control:** `InternalsVisibleTo("Orynivo")` is set in `AssemblyInfo.cs`
so the Orynivo Windows app can access internal Core members (audio-processing
internals). `Orynivo.Server` does **not** have this grant and uses only the
public Core surface.

`SmartPlaylistCriteria.Resolve(candidates)` evaluates stored filter criteria
against the compact track set from `AudioDatabase.GetSmartPlaylistTracks()` and
returns ordered, optionally limited results. Both the app and the server use
this method so the filtering logic lives only in Core.

`LibraryWatcherService(Action)` and `UpdatePaths(paths)` are public so
`Orynivo.Server` can start watching configured library roots.
`AudioDatabase.GetTrackById(long)` is public for track-by-ID lookup in the
stream endpoint.

Windows-only items that remain exclusively in `Orynivo/`:

- `Audio/SteinbergAsioStream`, `FfmpegAudioPlayer`, `WasapiAudioPlayer`, `WasapiDeviceProvider`,
  `DsfAudioPlayer`, `DffAudioPlayer`, `WindowsEndpointVolumeSynchronizer`
- `WindowsMediaTransportService`, `Streaming/WindowsStreamingCredentialStore`,
  `Streaming/WindowsPlexCredentialStore`, all Avalonia UI files

## Build and Run

```powershell
.\build.ps1
.\Orynivo\bin\Debug\net8.0-windows10.0.19041.0\Orynivo.exe
```

`build.ps1` always builds the vendored MIT-licensed `CwAsioBridge.dll`, then
builds `AsioBridge.dll` when the Steinberg SDK is available, and finally builds
the .NET application. It
locates Visual Studio MSBuild through `vswhere.exe` or `PATH`. The ASIO SDK can
be supplied with `-AsioSdkDir`, `ASIO_SDK_DIR`, or a local
`third_party/asiosdk` / `external/asiosdk` directory. `-MSBuildPath` and
`MSBUILD_EXE_PATH` override MSBuild discovery. If no SDK is found, the script
builds without the Steinberg bridge; `-SkipAsio` forces that mode and
`-RequireAsio` makes a missing SDK fatal. `-SkipCwAsio` additionally disables
the cwASIO bridge. Managed build properties remove disabled native DLLs from
build and publish output.

`.github/workflows/dotnet-desktop.yml` builds the cwASIO bridge and managed
Avalonia project in Debug and Release. It intentionally excludes only the
Steinberg bridge because that SDK is not stored in the repository. The Release
artifact therefore contains cwASIO support without Steinberg SDK files.

## Orynivo.Server

`Orynivo.Server` is a `net8.0` ASP.NET Core Minimal API server that exposes
the local music library over the network. It references `Orynivo.Core` and has
no Windows-specific dependencies; it runs on Windows, Linux, and macOS.
The server project references `SkiaSharp.NativeAssets.Linux.NoDependencies` so
Linux packages include the native SkiaSharp library required for artwork
thumbnail generation; do not replace this with an external ImageMagick/convert
runtime dependency.

**Configuration** (`appsettings.json`, section `Orynivo`):

- `ApiKey` — pre-shared key required in every request (`X-Api-Key` header or
  `?key=` query param; query param enables direct use in FFmpeg URLs)
- `LibraryPaths` — list of root directories to scan
- `ScanOnStartup` — run a full scan when the server starts (default `true`)
- `ServerName` — display name returned by `/api/info`
- Default bind: `http://0.0.0.0:5280`

**Key files:**

- `Orynivo.Server/Program.cs`: builds and starts the server; calls
  `FfmpegLocator.EnsureAvailableAsync()`, registers `LibraryWatcherService`
  and the `LibraryService` hosted service, maps all endpoints including
  remote configuration and directory browsing
- `Orynivo.Server/ServerSettings.cs`: configuration POCO bound from the
  `Orynivo` config section
- `Orynivo.Server/Middleware/ApiKeyMiddleware.cs`: skips `/api/health`,
  validates `X-Api-Key` header or `?key=` query param, returns 401 JSON
- `Orynivo.Server/Services/LibraryService.cs`: `IHostedService` that calls
  `LibraryWatcherService.UpdatePaths` on start and runs full scans via the
  static `LibraryScanner.ScanAsync`; exposes `TriggerScan()` for manual
  trigger and `ScanStatus` with current root, processed/total counts, current
  file, last result, errors, and a persisted `LibraryChangedAt` Unix timestamp
  that updates when scans or watcher runs add, update, or remove indexed tracks
- `Orynivo.Server/Endpoints/LibraryEndpoints.cs`: artists, albums, tracks,
  playlists (smart playlists resolved via `SmartPlaylistCriteria.Resolve`),
  and Lucene search endpoints; `GET /api/albums/recent` returns the most
  recently added albums (id, title, artist, `ArtistId`, `AddedAt`, `HasArtwork`)
  for the client dashboard's cross-library Recently Added widget;
  `GET /api/artists/{id}` returns complete
  cached artist profile fields, `POST /api/artists/{id}/profile` stores
  client-refreshed biography/source fields plus optional image bytes, and
  `POST /api/artists/{id}/rename` renames or merges artists then rebuilds the
  server Lucene index; Last.fm,
  Wikipedia, and Wikimedia image-search requests run on the client, not on the
  server; `GET`
  `/api/tracks/{id}/lyrics` returns cached plain/synced lyrics with the last
  fetch timestamp and `PUT /api/tracks/{id}/lyrics` stores client-downloaded
  LRCLIB lyrics (the LRCLIB request runs on the client); `GET`
  `/api/tracks/{id}/waveform` returns cached compact waveform peaks generated
  server-side through `WaveformCache` and stored under the server data
  directory; the track DTO includes the primary `ArtistId` and `AlbumId`, and
  the album DTO includes `ArtistId`, for in-library navigation; `GET`
  `/api/tracks/facets` returns lightweight
  facet rows (`TrackFacetInfo`) and `POST` `/api/tracks/by-ids` returns track
  rows for a posted ID list, together powering the remote Tracks facet filters;
  `GET /api/playlists` (including `FilterCriteria` for smart playlists),
  `GET /api/playlists/{id}/tracks` (smart playlists are resolved server-side
  through `SmartPlaylistCriteria.Resolve` using the server's own favourite
  flags), `POST /api/playlists/{id}/resolve` (resolves a smart playlist while
  overriding favourite state with the client-supplied favourite track IDs,
  because remote favourites live client-side; this is what the Windows client
  uses to open remote smart playlists so a `FavoritesOnly` criterion matches the
  client's favourites instead of the server's unset flags),
  `POST /api/playlists`,
  `POST /api/playlists/smart` and `PUT /api/playlists/{id}/smart` (create and
  update a smart playlist from client-supplied criteria, which the server
  re-validates through `SmartPlaylistCriteria`),
  `POST /api/playlists/{id}/tracks`, `DELETE /api/playlists/{id}`, and
  `DELETE /api/playlist-tracks/{id}` expose server-side playlist browsing and
  regular- and smart-playlist editing for remote clients; remote playlist writes
  must use server-side track IDs, never credential-bearing stream URLs;
  `GET` `/api/folders/tracks` returns lightweight track rows plus playback
  metadata (artist, album, duration, format, primary `ArtistId`, `AlbumId`) for
  building the remote server library folder tree and must materialize the
  response before disposing the SQLite connection because JSON serialization
  runs after the route handler returns
- `Orynivo.Server/Endpoints/StreamEndpoints.cs`: byte-range streaming for
  regular audio files; on-the-fly FLAC transcode via FFmpeg pipe for CUE
  virtual tracks; `GET /api/stream/{trackId}?ss=<seconds>` performs a fast
  server-side seek by transcoding the local file from that offset to FLAC (used
  by remote clients so in-track seeking does not binary-search a seektable-less
  file over HTTP); the transcode/FFmpeg process is stopped when the client
  disconnects; album, artist, and track artwork endpoints (track artwork is
  served both by file path via `/api/artwork/track?p=` and by database ID via
  `/api/artwork/track/{id}` with an optional `?size=`); album artwork
  requests fall back to an on-demand embedded-artwork repair for the requested
  album before returning 404; `PUT` `/api/artwork/album/{id}` and
  `/api/artwork/artist/{id}` accept raw image bytes from authenticated clients
  and store them in the server-side artwork caches
- `Orynivo.Server/Endpoints/ConfigurationEndpoints.cs`: authenticated
  `/api/settings/library-paths` GET/PUT and `/api/files/directories?path=`
  endpoints; PUT persists `Orynivo:LibraryPaths`, refreshes
  `LibraryWatcherService`, and starts a scan. Settings are written to the
  editable, service-writable config (`/etc/orynivo-server/appsettings.json` via
  `ConfigurationEndpoints.LinuxConfigFilePath` when that directory exists,
  otherwise the content-root `appsettings.json`); the content-root copy under
  `/usr/lib/orynivo-server` is root-owned/read-only and overwritten on package
  upgrades. `Program.cs` layers the same `/etc` file on top of the bundled
  defaults at startup (optional, so it is a no-op on Windows/dev)

**Build:**

```bash
dotnet build Orynivo.Server/Orynivo.Server.csproj
dotnet run --project Orynivo.Server/Orynivo.Server.csproj
```

**Linux packages** — `.github/workflows/server-release.yml` (triggered on the
same `v*` tags as the Windows release) builds self-contained binaries for
`linux-x64` and `linux-arm64` and publishes four packages to the draft
GitHub Release: `amd64`/`arm64` DEB and `x86_64`/`aarch64` RPM.
Support files live in `.github/server-release/` (systemd unit, postinst/prerm
scripts). The packages install to `/usr/lib/orynivo-server/`, expose a
`/usr/bin/orynivo-server` symlink, ship a default config at
`/etc/orynivo-server/appsettings.json`, and register
`orynivo-server.service` running as the `orynivo-server` system user.

The `orynivo-server` service user is created with `--no-create-home` and has no
writable `$HOME`, so the default data directory (`$HOME/.local/share/Orynivo`)
cannot be created. `AppPaths.DataRoot` therefore honours the
`ORYNIVO_DATA_DIR` environment variable, and the systemd unit sets
`ORYNIVO_DATA_DIR=/var/lib/orynivo-server` plus `StateDirectory=orynivo-server`
(the post-install also creates `/var/lib/orynivo-server` owned by the service
user). The server's SQLite database, caches, and downloaded artwork live there.
Do not remove the data-directory override or the systemd unit will abort on
startup with `UnauthorizedAccessException`/`SIGABRT`.

## Important Architecture

- `Orynivo/OutputProfile.cs`: named audio output configuration (backend, device
  IDs, display name); multiple profiles allow switching between output devices
  without reconfiguring each time
- `Orynivo/OutputProfileDialog.axaml/.cs`: dialog for creating or editing an
  output profile; loads available devices asynchronously, validates unique
  names, and exposes the confirmed result via `Result`
- `Orynivo/Audio/SteinbergAsioStream.cs`: runtime-selecting C# wrapper for
  `AsioBridge.dll` and `CwAsioBridge.dll`
- `Orynivo/Audio/FfmpegAudioPlayer.cs`: PCM path
- `Orynivo/Audio/FfmpegPcmDecoder.cs`: FFmpeg PCM decoder process with an
  initial prefetched block used for gapless transitions. For `http(s)` inputs it
  caps FFmpeg's stream analysis (`-analyzeduration`/`-probesize`) and adds
  reconnect options so the first decode of a remote/HTTP track does not block on
  FFmpeg's default 5 s probe window. The PCM probe paths
  (`FfmpegAudioPlayer.ProbeAsync`, `WasapiAudioPlayer.ProbeAsync`) apply the same
  capped `ffprobe` settings for HTTP inputs. Seeking a remote Orynivo Server
  stream (URL contains `/api/stream/`) uses **server-side seek**: the decoder
  appends `?ss=<seconds>` and decodes the offset stream from position 0 instead
  of seeking the HTTP stream itself (which binary-searches seektable-less files
  over many range round-trips). Plex and other HTTP inputs keep client-side
  `-ss` seeking.
- `Orynivo/Audio/FfmpegLocator.cs`: checks `AppContext.BaseDirectory`,
  `%LOCALAPPDATA%\Orynivo\ffmpeg`, and PATH for `ffmpeg.exe`/`ffprobe.exe` at
  startup; when absent on Windows, resolves the current BtbN Windows LGPL ZIP
  asset through the GitHub release API, extracts `ffmpeg.exe` and `ffprobe.exe`
  into the per-user cache, and prepends that directory to the current-process
  PATH. Do not write downloaded FFmpeg binaries into the install directory
  because setup installs may live under `Program Files` without user write
  access. FFmpeg/FFprobe child processes must use
  `FfmpegLocator.GetSafeWorkingDirectory()` as their
  `ProcessStartInfo.WorkingDirectory`; stale installer shortcuts can otherwise
  leave the process current directory pointing to a deleted install path.
- `Orynivo/Audio/DsfAudioPlayer.cs`: native DSF-to-DSD path
- `Orynivo/Audio/RemoteDsfAudioPlayer.cs`: native DSF-to-DSD path for
  authenticated Orynivo Server streams. It reads DSF headers and audio blocks
  through HTTP byte ranges and feeds the existing ASIO/cwASIO DSD stream without
  downloading the complete file first. Transport text may claim native DSD only
  when this player or the local native DSD players are actually active.
- `Orynivo/Audio/DffAudioPlayer.cs`: native DFF/DSDIFF-to-DSD path
- `Orynivo/Audio/RemoteDffAudioPlayer.cs`: native uncompressed DFF/DSDIFF-to-DSD
  path for authenticated Orynivo Server streams. It parses the remote `FRM8`,
  `PROP`, and `DSD ` chunks through HTTP byte ranges, rejects DST-compressed DFF,
  bit-reverses the DSD payload like the local DFF player, and feeds ASIO/cwASIO
  without downloading the complete file first.
- `Orynivo/Audio/WasapiAudioPlayer.cs`: exclusive-mode WASAPI PCM path; converts
  DSD sources to PCM in real time and selects the highest supported output
  sample rate up to the source rate plus the highest supported output precision
- `Orynivo/Audio/WasapiDeviceProvider.cs`: WASAPI devices and capability queries
- `Orynivo/WindowsMediaTransportService.cs`: optional Windows System Media
  Transport Controls host for global media buttons, lock-screen/system-overlay
  metadata, artwork, playback status, and timeline updates; its `MediaPlayer`
  instance is control-only and never outputs Orynivo audio
- `Orynivo/LibraryCatalogProviders.cs`: shared catalog abstraction for local
  and remote Orynivo libraries. `ILibraryCatalogProvider` exposes common
  artist, album, track, search, and album-artwork operations; `LocalLibraryCatalogProvider`
  maps `AudioDatabase` rows, and `OrynivoServerLibraryCatalogProvider` maps
  `OrynivoServerClient` responses into the same UI-facing models so library
  masks can be reused instead of branching on local vs. server rows.
- `Orynivo/NowPlayingMetadataProviders.cs`: reusable abstraction for the
  transport lyrics, artist-info, and cover views. `INowPlayingMetadataProvider`
  exposes `GetCachedLyricsAsync`/`DownloadLyricsAsync`, `GetArtistProfileAsync`,
  and `GetArtworkAsync` (the transport cover + lyrics background, returned as
  local file paths; the remote implementation downloads the server track
  artwork into the local remote-artwork cache);
  `LocalNowPlayingMetadataProvider` reads/writes the local SQLite library, and
  `OrynivoServerNowPlayingMetadataProvider` reads/writes a remote server's
  lyrics and artist-profile caches. The client always performs the external
  LRCLIB/Wikipedia/Last.fm fetch and uploads the result; the server only caches
  it. `MainWindow` selects the provider for the currently playing track
  (`_currentNowPlayingProvider`) so lyrics and artist info work identically for
  local and remote tracks. For a remote track the now-playing artist button
  navigates within that track's server library (`OpenOrynivoArtistAlbumsAsync`
  using the row's `OrynivoServer` and `ArtistId`), not the local album view.
- `Orynivo/Audio/WindowsEndpointVolumeSynchronizer.cs`: bidirectional
  synchronization between the transport volume slider and the selected
  Windows render endpoint's master volume
- `Orynivo/Audio/ReplayGain.cs` and `ReplayGainMode.cs`: parse persisted
  track/album gain values, select the configured fallback mode, and calculate
  the linear PCM gain factor
- `Orynivo/Audio/EqualizerApoParser.cs`: imports Equalizer APO and AutoEQ
  preamp, parametric filter, and `GraphicEQ` text profiles
- `Orynivo/Audio/ParametricEqualizer.cs`: stereo biquad PCM equalizer with a
  short crossfade when the active profile changes and filter-state reset after
  seeks
- `Orynivo/Controls/EqualizerResponseControl.cs`: logarithmic frequency-response
  graph for the editable parametric equalizer profile in Settings, including
  a 20 Hz–20 kHz scale and numbered dashed markers that map filter frequencies
  to dynamic editor rows. Marker bubbles sit below the scale; colliding numbers
  remain bottom-aligned and shift horizontally with a short leader instead of
  moving upward.
- `Orynivo/Controls/InitialsAvatar.cs`: reusable `TemplatedControl` placeholder
  for missing album/artist artwork. It derives one or two initials plus a
  deterministic diagonal gradient (seeded from the display name) so empty cover
  slots look intentional instead of flat grey. Its default template lives in
  `App.axaml` (`{x:Type controls:InitialsAvatar}`). Used in the album detail
  header, album/artist artwork cards, and the artist-info image; because remote
  entities reuse the same `ContentRow`-bound masks, local and remote share it.
  Set `DisplayName` (bound to the row `Title` or an element reference) and
  `FontSize` to scale the initials to the slot size.
- `Orynivo/Controls/StatusBadge.cs`: small pill indicator (`TemplatedControl`,
  default template in `App.axaml`) with a coloured dot and short label, driven
  by `Text` and a `State` (`StatusBadgeState.Ok`/`Warning`/`Off`). Settings uses
  it to surface FFmpeg, Steinberg ASIO, cwASIO, and MCP server availability;
  `SettingsView` populates the badges in `UpdateSubsystemStatusBadges()` /
  `UpdateMcpStatusBadge()`. Each Orynivo Server and Plex server row also carries
  a live connection badge probed asynchronously (`CheckServerStatusAsync` +
  `ProbeOrynivoServerAsync`/`ProbePlexServerAsync`), so opening Settings never
  blocks on network calls; pending probes are cancelled per-list on rebuild and
  in `Deactivate()`.
- `Orynivo/EqualizerProfileNameDialog.*`: themed unique-name dialog used when
  creating a new persisted equalizer profile
- `Native/AsioBridge/bridge.cpp`: shared Steinberg/cwASIO initialization,
  PCM/DSD ring buffers, and callback
- `Native/CwAsioBridge/CwAsioBridge.vcxproj`: builds the shared bridge against
  vendored cwASIO
- `third_party/cwasio/`: pinned MIT-licensed cwASIO host and compatibility sources
- `Orynivo/SettingsView.*`: two-column settings view embedded in the main
  content area, with navigation on the left and the selected section on the
  right; the output-profile dropdown shows a compact `Backend  ·  Device`
  summary line (`OutputProfileSummaryTextBlock`) beneath it when a profile is
  selected; `NavigateToSection(tag)` and `ScrollToEqualizerSection()` allow
  the transport quick-pick buttons to jump directly into a settings section;
  the **Integration** navigation group contains the **MCP SERVER** section
  (`Tag="Mcp"`) with an enable checkbox, configurable port field, and per-tool
  enable/disable checkboxes for all 23 tools (stored in
  `AppSettings.DisabledMcpTools`); `NavigateToSection("Mcp")` jumps there;
  the tool `UniformGrid` has `Rows="12"` for 23 tools (2 columns). The MCP
  section also holds the **Web browsing** configuration (enable toggle, SearXNG
  URL, block-private-networks toggle, and timeout/response-size/result limits)
  edited via `WebBrowsingValue`
  Remote Orynivo Server connection management belongs under its own
  **Orynivo Server** navigation item in the **BIBLIOTHEK** settings group, not
  under local directories or Streaming.
- `Orynivo/Mcp/McpPlayerBridge.cs`: thread-safe bridge between the MCP layer
  and Avalonia's UI thread; `MainWindow` populates all delegate properties at
  startup; `OnUiAsync` dispatches to `Dispatcher.UIThread` at
  `DispatcherPriority.Normal`; records `PlayerState` and `QueueEntry` are
  returned by the state and queue delegates; `DisabledTools` (`HashSet<string>?`)
  and `IsToolEnabled(name)` control per-tool gating; `RefreshPlaylistsFunc`
  triggers `LoadNavPlaylists` after MCP creates a playlist; `WebBrowsing`
  (`Orynivo.Web.WebBrowsingService`) backs the web tools
- `Orynivo.Core/Web/WebBrowsingService.cs` (+ `WebBrowsingOptions`,
  `HtmlContentExtractor`): the controlled internet layer used by both MCP and the
  AI chat. `SearchAsync` queries the configured SearXNG JSON API (trusted
  endpoint, not SSRF-guarded, so a LAN/Docker instance works after the user
  configures its URL); `FetchTextAsync`/`FetchMarkdownAsync` fetch arbitrary pages behind a
  strong SSRF guard: http/https only, a `SocketsHttpHandler.ConnectCallback`
  refuses private/loopback/link-local/reserved IPs at connect time (closing the
  DNS-rebinding window), manual redirect following with a limit, response
  size-cap, non-text content refused (no arbitrary downloads), per-request
  timeout, optional domain allowlist, and per-request audit logging to
  `%LOCALAPPDATA%\Orynivo\logs\web-browsing.log`. Configured through
  `AppSettings.WebBrowsing` (`WebBrowsingOptions`); `MainWindow` creates the
  service, wires the logger, and updates `Options` on settings save.
- `Orynivo/Mcp/McpTools.cs`: 23 MCP tools annotated with `[McpServerToolType]`
  and `[McpServerTool]`; read-only tools are marked `ReadOnly = true,
  Idempotent = true`; every tool guards with `bridge.IsToolEnabled(name)` and
  returns `"Tool is disabled."` when off; `get_current_time` returns the current
  local/UTC date, time, day of week, and time-zone name; the web tools
  `search_web`, `fetch_page`, and `fetch_page_as_markdown` delegate to
  `bridge.WebBrowsing` (a `Orynivo.Web.WebBrowsingService`); `search_library`
  calls
  `TrackSearchIndex.SearchByCategory` then `AudioDatabase.OpenDefault()` for id
  resolution; property access uses `AlbumInfo.Album`, `AlbumInfo.DisplayArtist`,
  and `ArtistInfo.Artist`; playlist tools use `GetAllPlaylists`,
  `GetPlaylistById`, `GetPlaylistTracks`, `CreatePlaylist`, and
  `CreateSmartPlaylist`; `get_play_history` uses `GetHistoryForDay` for a
  specific date and `GetRecentHistory` for the N most recent entries;
  `create_smart_playlist` accepts individual filter parameters and serialises
  them to `SmartPlaylistCriteria` JSON; `clear_queue` empties the queue
  without stopping the current track; `replace_queue` atomically replaces the
  queue and starts playback of the first new track
- `Orynivo/Mcp/McpServerService.cs`: starts/stops an embedded Kestrel HTTP/SSE
  server using `WebApplication`; binds to `http://localhost:{port}` via
  `builder.WebHost.UseSetting("urls", …)`; maps MCP endpoint at `/mcp`;
  logging is cleared so Kestrel output does not appear in the player console;
  `IAsyncDisposable` — `DisposeAsync` delegates to `StopAsync`
- `Orynivo/AI/AiChatSettings.cs`: persisted configuration for the embedded AI
  chat (endpoint URL, optional API key, model name, max tokens); stored as
  `AppSettings.AiChat`
- `Orynivo/AI/AiChatService.cs`: HTTP client for OpenAI-compatible
  `/v1/chat/completions` endpoints using streaming SSE; internal tool-call
  loop accumulates streamed function arguments, executes tools via
  `AiToolExecutor`, and continues until the model emits `stop`; returns
  `IAsyncEnumerable<AiStreamEvent>` (token, tool-call, error, done events);
  conversation history is maintained across turns; `AiChatSettings` is read on
  every send so settings changes take effect without restarting the view
- `Orynivo/AI/AiToolDefinitions.cs`: builds the OpenAI function-calling schema
  (`JsonObject` list) for all 23 Orynivo tools; definitions match the method
  signatures in `McpTools.cs`
- `Orynivo/AI/AiToolExecutor.cs`: dispatches tool calls received from the LLM
  to `McpTools` methods by name; parses JSON arguments from the model; no MCP
  transport involved — tools are invoked directly against the bridge and database
- `Orynivo/AI/AiChatView.axaml/.cs`: embedded chat UI; user bubbles right-
  aligned (accent color), assistant bubbles left-aligned (surface brush),
  tool-call status centered and muted; streams tokens into the last assistant
  message; Enter sends, Shift+Enter inserts a newline; Clear button wipes
  history and resets `AiChatService`; `SetBridge(McpPlayerBridge)` wires the
  executor; `GetSettings` delegate is read on each send
- `Orynivo/ThemeManager.cs`: sets global Avalonia resources for light and dark themes
- `Orynivo/Controls/DataGridColumnWidthStore.cs`: validates, captures, and
  restores per-table pixel widths
- `Orynivo/Controls/DataGridColumnOrderStore.cs`: captures and restores
  identified data-column display order while retaining fixed-column slots
- `Orynivo/Controls/DataGridColumnChooser.cs`: opens the themed `MenuFlyout`
  for column visibility at the clicked header; entries remain open while
  toggling multiple columns
- `Orynivo/Localization/*`: language model and localized German, English,
  French, and Spanish strings
- `Orynivo/StartupWindow.*`: lightweight splash screen shown during initial
  database preparation and migration
- `Orynivo/Assets/Orynivo_Logo.png`: embedded full logo used by the splash
  screen and main sidebar
- `Orynivo/Assets/Orynivo.ico`: multi-resolution application and window icon
  generated from `Logo/only_logo_300.png`
- The process uses the explicit Windows AppUserModelID `Orynivo.AudioPlayer`;
  the startup window is excluded from the taskbar so the main window owns the
  taskbar identity
- `Orynivo/CrashLogger.cs`: writes unhandled UI, AppDomain, and task exception
  reports to `%LOCALAPPDATA%\Orynivo\logs\`
- `Orynivo/DailyHistoryDialog.*`: themed modal dashboard dialog that lists
  playback-history entries for a selected calendar day and returns track, album,
  or artist navigation actions to the main window
- `Orynivo/SettingsStore.cs`: persists `%LOCALAPPDATA%\Orynivo\settings.json`
- `Orynivo/Streaming/IStreamingCatalog.cs` and `IStreamingPlaybackProvider.cs`:
  provider-neutral contracts for future streaming catalog and playback integrations
- `Orynivo/Streaming/QobuzStreamingProvider.cs`: inactive Qobuz scaffold; do not
  add unofficial endpoints, enable it only with approved partner API documentation
- `Orynivo/Streaming/WindowsStreamingCredentialStore.cs`: stores future provider
  secrets and tokens in `%LOCALAPPDATA%\Orynivo\streaming-credentials.dat` using
  Windows DPAPI for the current user
- `Orynivo/PlaylistProviders.cs`: provider-neutral playlist persistence for
  local SQLite playlists and remote Orynivo Server playlists. Track, album, and
  folder context menu actions should build a `PlaylistSelection` and route
  add/create operations through `ILibraryPlaylistProvider` instead of branching
  directly on local vs. server storage in UI handlers.
- `Orynivo/Streaming/PlexServerClient.cs`: queries configured Plex Media Servers,
  exposes only music library sections (`type=artist`), pages
  artists/albums/tracks, resolves drill-down children, browses folders lazily,
  and builds authenticated direct-part URLs
- `Orynivo.Core/Streaming/OrynivoServerClient.cs`: HTTP client for a remote
  Orynivo Server; methods load artists, a single artist profile (`GetArtistAsync`),
  albums by artist, albums, tracks by
  album, all tracks, tracks by ID list (`GetTracksByIdsAsync`), track facet rows
  (`GetTrackFacetsAsync`), lightweight folder-tree tracks, per-track cached lyrics
  (`GetTrackLyricsAsync`/`UploadTrackLyricsAsync`), remote artist rename/merge
  (`RenameArtistAsync`), server playlists (`GetPlaylistsAsync`, which now also
  carries each smart playlist's `FilterCriteria`), playlist tracks, regular
  playlist create/append/delete, smart-playlist create/update
  (`CreateSmartPlaylistAsync`/`UpdateSmartPlaylistAsync`), and smart-playlist
  resolution with client-side favourites
  (`ResolveSmartPlaylistTracksAsync`), server library paths,
  server directory listings, and scan status including `LibraryChangedAt` for
  remote client cache invalidation; `SetLibraryPathsAsync` replaces
  the remote server's configured library
  roots; `TriggerScanAsync` starts a remote scan; `UploadAlbumArtworkAsync` and
  `UploadArtistImageAsync` send client-selected image bytes to the server so the
  server does not perform external artwork searches itself;
  `GetTrackWaveformAsync` downloads cached compact waveform peak data from the
  server for the transport progress view;
  `UpdateArtistProfileAsync` sends client-refreshed biography/source fields and
  optional image bytes for server-side caching, without sending the Last.fm API
  key; `GetStreamUrl`, `GetAlbumArtworkUrl`, `GetArtistArtworkUrl`, and
  `GetTrackArtworkUrl` are pure URL builders that include `?key=` so the URLs can
  be passed directly to FFmpeg or Avalonia's image loader
- The main sidebar library accordion is labelled **Library**/**Bibliothek**.
  Local media live under a collapsible **Local** child node followed by local
  Artists, Albums, Tracks, Folder structure, and a nested local Playlists group.
  The complete **Local** child node (and its rows/playlists) is hidden whenever
  no local library directory is configured (`AppSettings.LibraryPaths` empty).
  When neither a local directory nor any Orynivo Server is configured, a hint
  row (`LibraryEmptyHintItem`, `L_LibraryEmptyHint`) is shown directly under the
  Library header telling the user to add local directories or one or more Orynivo
  Servers in Settings. The hint disappears immediately when a directory or server
  is added because `ApplyLibrarySectionVisibility` re-runs on every settings save,
  navigation rebuild, and the live directory-change callback.
  Configured remote Orynivo
  Server entries are rendered in that same Library accordion, after the local
  media rows, as one collapsible server-name row followed by Artists, Albums,
  Tracks, Folder structure, and a collapsible Playlists child group; server
  playlists are downloaded when the sidebar navigation is built and are
  displayed below that server's Playlists group. The local Playlists group is a
  child of the Local node, indented to align with the local Artists/Albums/
  Tracks/Folder rows, and its entries are indented one level deeper. Both the
  local and each server's Playlists group are independently collapsible while
  keeping their playlist entries nested. There is no separate Orynivo Server
  sidebar accordion or separate Orynivo Server sidebar visibility setting;
  `ShowLocalLibrarySection` and `IsLocalLibrarySectionExpanded` control the
  complete Library accordion, including remote Orynivo Server rows.
  `IsLocalMediaLibraryGroupExpanded` persists the Local child group state,
  `IsPlaylistsSectionExpanded` persists the local Playlists child group state,
  `CollapsedOrynivoServerLibraryGroups` persists collapsed server child groups,
  and `CollapsedOrynivoServerPlaylistGroups` persists collapsed per-server
  Playlists child groups.
  Remote
  Artists, Albums, and Tracks reuse the **same** local table/artwork column masks
  (`ApplyColumns("Artists"/"Albums"/"Tracks")`), intro card
  (`UpdateLibraryIntroCard`), per-entity Favorites-only toggle, and A-Z index as
  the local library. There are no separate `Orynivo*` column masks. Server
  artwork hydrates lazily as rows or artwork cards become visible; downloaded
  remote artwork is cached client-side under
  `%LOCALAPPDATA%\Orynivo\remote-artworks\`.
  Artist/album entity links and double-click navigate **within** the remote
  library via `OpenOrynivoArtistAlbumsAsync`/`OpenOrynivoAlbumTracksAsync`
  (the link handlers `ArtistLinkButton_OnClick`/`AlbumLinkButton_OnClick`
  dispatch by `EntityType` prefix `"Orynivo"`). This requires remote rows to
  carry IDs: remote track rows have `ArtistId`/`AlbumId`, remote album rows have
  `ArtistId`.
  Remote album drill-downs use the shared provider-backed album-track detail
  surface with the same album header/card, cover actions, favorite toggle, and
  grouped track tables as local albums; do not route server albums to a separate
  plain track-list view. When a remote album is opened from a remote artist's
  album list, the detail view must initially scope tracks to that artist and
  expose the same **Show all album tracks** checkbox used by local album
  details. Persisting remote album tracks as local playlists must
  remain disabled because the paths contain credential-bearing server URLs.
  Remote playlist actions must target the track's own server and persist
  playlist entries on that server through `OrynivoServerClient`, using remote
  track IDs. Local playlist actions remain SQLite-based and are displayed under
  the Local Playlists group. Smart playlists work identically for local and
  remote libraries: the remote Tracks **Save smart playlist** action and the
  sidebar **Edit smart playlist** context entry persist criteria on the server
  through `CreateSmartPlaylistAsync`/`UpdateSmartPlaylistAsync`, while the server
  resolves them live through `SmartPlaylistCriteria.Resolve`. Because remote
  favourites are client-side, the client opens a remote smart playlist through
  `ResolveSmartPlaylistTracksAsync` (`POST /api/playlists/{id}/resolve`), sending
  its favourite track IDs so a `FavoritesOnly` criterion matches the client's
  favourites; regular remote playlists still use `GetPlaylistTracksAsync`.
  Back navigation must capture remote album details as remote states carrying
  the Orynivo Server navigation tag; never restore a remote album detail through
  the local `ShowAlbumTracksAsync` path because server and local album IDs can
  collide.
  Remote artist-info views must use the bound remote row/server profile data,
  not local `AudioDatabase.GetArtistById`, because server IDs can collide with
  local artist IDs. The remote artist-info rename/merge and Wikimedia image
  actions must call the Orynivo Server API (`RenameArtistAsync`,
  `UploadArtistImageAsync`) and update the remote row/detail view rather than
  writing to the local database.
  Remote Tracks offers the same Genre/Audio-Type/Bitrate facet filters as local,
  reusing the client's `MatchesTrackFilters`/facet-popup logic fed by the server
  aggregation endpoint `/api/tracks/facets` (favorite state overridden with the
  client-side favorites); filtered results load via `/api/tracks/by-ids`
  (`ResolveOrynivoTrackRowsAsync`). Typing in the header search box while a remote
  Tracks view is active shows the shared three-section search result (Tracks,
  Albums, Artists) via `ShowOrynivoSearchResultsAsync`, backed by the server
  `/api/search/full` endpoint (`OrynivoServerClient.SearchFullAsync` /
  `OrynivoServerLibraryCatalogProvider.SearchFullAsync`), mirroring the local
  `ShowSearchResultsAsync`. Result rows carry `Orynivo*` entity types so the
  shared link columns and the `SearchAlbumsDataGrid`/`SearchArtistsDataGrid`
  double-click handlers navigate within the remote library, and remote tracks
  play directly from the result list. Clearing the search box restores the remote
  Tracks list. The three search-result sections use the same accent-bordered card
  style as the library headline/intro card for local and remote results. The
  facet rows live in `_orynivoTrackFacets` while a remote Tracks view is active
  and are cleared otherwise.
  Remote folder tree file nodes must register the same `ContentRow` metadata as
  remote Tracks rows before queuing playback. This is required so the transport
  shows title/artist/artwork instead of authenticated stream URLs and enables
  lyrics, artist-info, favorite, and server-playlist actions. Remote folder
  playlist persistence must use server-side track IDs through
  `OrynivoServerPlaylistProvider`, never credential-bearing stream URLs. When
  `/api/folders/tracks` lacks the newer playback metadata because an older
  server is connected, the client may batch-hydrate folder track metadata through
  `/api/tracks/by-ids` before registering the folder rows. Before loading remote
  folder data, clear any existing local folder nodes and show the localized
  server-loading placeholder; do not display stale local tree data while the
  remote request is in flight. Cache remote folder-track lists under
  `%LOCALAPPDATA%\Orynivo\remote-folder-cache\` and reuse them only while the
  server's `LibraryChangedAt` scan status timestamp matches the cached value.
  The unfiltered remote Tracks list is loaded with a large page size (one or two
  requests instead of one per 500 rows) and is likewise cached under
  `%LOCALAPPDATA%\Orynivo\remote-track-cache\`, reused while the server's
  `LibraryChangedAt` matches the cached value. That cache stores mapped
  `LibraryCatalogTrack` rows, so its file key includes the server API key
  (cached playback URLs embed the key) and client-side favourites are re-applied
  after loading instead of trusting the cached favourite flags.
  While a remote track plays, the transport lyrics and artist-info buttons work
  through `OrynivoServerNowPlayingMetadataProvider` (see
  `Orynivo/NowPlayingMetadataProviders.cs`): lyrics and the artist
  biography/image are fetched on the client and cached on that track's server.
  The remote track's server and primary artist ID are carried on its
  `ContentRow` (`OrynivoServer`, `ArtistId`); the now-playing artist button is
  enabled for a remote track and navigates within that track's server library
  (`OpenOrynivoArtistAlbumsAsync`) rather than the local album view.
  Remote transport waveform loading first asks the server for cached peaks and
  falls back to client-side FFmpeg analysis of the authenticated stream URL when
  the server cannot analyse the format; this keeps DFF waveforms available
  without requiring the server's FFmpeg build to support every source format.
  The transport favourite (heart) button is enabled for a playing remote track
  and toggles the client-side favourite (`OrynivoServerFavorites`) for that
  track via `CurrentOrynivoFavoriteTarget`; the change is written to
  `settings.json` and reflected in any visible remote track rows. For local
  tracks it still writes `tracks.is_favorite`.
- `Orynivo/OrynivoServerDialog.axaml/.cs`: themed dialog for adding or editing a
  remote Orynivo Server (name, URL, API key, Test Connection); it can load, add,
  remove, and save the remote server's music directories through the server API,
  start a remote scan, and show live scan progress; returned server record is
  stored in `AppSettings.OrynivoServers`
- `Orynivo/RemoteDirectoryBrowserDialog.axaml/.cs`: browses the remote server
  filesystem through `/api/files/directories?path=` and returns a server-side
  directory path for `OrynivoServerDialog`
- `AppSettings.OrynivoServers` stores configured remote server connections; API
  keys are stored in `settings.json` (same policy as AI chat key);
  `AppSettings.OrynivoServerFavorites` stores client-side favorite keys for
  remote artists, albums, and tracks. Legacy `ShowOrynivoServerSection` and
  `IsOrynivoServerSectionExpanded` settings may still exist in persisted JSON
  for compatibility, but the current sidebar renders remote servers under the
  main Library accordion controlled by `ShowLocalLibrarySection` and
  `IsLocalLibrarySectionExpanded`.
- `Orynivo/Streaming/WindowsPlexCredentialStore.cs`: stores per-server Plex
  access tokens in `%LOCALAPPDATA%\Orynivo\plex-credentials.dat` using Windows
  DPAPI for the current user
- `AppSettings.PlexServers` stores Plex server IDs, display names, and base
  URLs; Plex tokens must not be added to `settings.json`
- `AppSettings.QobuzApplicationId` stores only the non-secret Qobuz application
  identifier; client secrets and tokens must not be added to `settings.json`
- `AppSettings.LastMainView`, `AppSettings.AlbumArtworkView`, and
  `AppSettings.ArtistArtworkView` preserve the selected main view and entity
  artwork/table modes
- `AppSettings.Volume` and `AppSettings.LastTrackPath` preserve volume and the
  last selected or played track; restoration requires both the file and database
  entry to exist
- The editable **Up next** queue is persisted in the SQLite library database
  (`playback_queue` table), not in `settings.json`. Legacy
  `AppSettings.PlaybackQueuePaths` and `PlaybackQueueIndex` values are imported
  once and then cleared. Local paths and non-credential HTTP/HTTPS URLs may be
  stored; Plex-token, Orynivo Server `?key=`, and user-info URLs must never be
  persisted.
- `AppSettings.OutputProfiles` persists all named output profiles;
  `SelectedOutputProfileName` identifies the active one.
  `SettingsStore.NormalizeOutputProfiles` migrates a previously configured
  single device to a profile named "Standard" and derives the flat
  `OutputBackend`, `SelectedDriverName`, `SelectedWasapiDeviceId`, and
  `SelectedWasapiDeviceName` fields from the active profile on every load and
  save. When no profile or legacy output device exists, it creates and selects a
  `Default` WASAPI profile from the Windows default multimedia render endpoint
  so a first-run installation can play audio without manual device setup. These
  flat fields remain in `AppSettings` for playback code that reads them directly
  but must not be edited independently of their profile.
- `AppSettings.ReplayGainMode` selects disabled, track, or album ReplayGain for
  PCM playback; native ASIO DSD remains bit-perfect
- `AppSettings.AlwaysConvertDsdToPcm` forces DSF/DFF sources through FFmpeg and
  the PCM output path even when ASIO/cwASIO native DSD is available, allowing
  volume, ReplayGain, and equalizer processing
- `AppSettings.PcmOutputBoostEnabled` applies an additional +6 dB linear gain to
  every PCM playback path (local, remote, Plex, radio, podcasts, and converted
  DSD). Native ASIO/cwASIO DSD remains bit-perfect and ignores the boost.
- `AppSettings.EqualizerProfiles` persists all named imported or manually
  edited Equalizer APO/AutoEQ profiles, while
  `SelectedEqualizerProfileName`, `EqualizerProfile`, and `EqualizerEnabled`
  identify the only selected/active profile and retain compatibility with the
  previous single-profile settings format. The source file path is not required
  after import.
- `AppSettings.DataGridColumnWidths` persists user-adjusted pixel widths per
  stable table/view key; dynamic main-content views capture their current widths
  before replacing columns
- `AppSettings.VisibleDataGridColumns` persists selectable column IDs per
  table/view key; right-clicking any table header opens the context-appropriate
  column chooser flyout
- `AppSettings.DataGridColumnOrders` persists drag-and-drop display order per
  stable table/view key; fixed artwork and action columns keep their structural
  positions
- `AppSettings.McpServerEnabled` enables the embedded MCP server on startup
  and when settings are saved; `AppSettings.McpServerPort` sets the TCP port
  (default 49200); the server binds exclusively to `localhost`;
  `AppSettings.DisabledMcpTools` persists the set of tool names whose
  checkboxes are unchecked in Settings — an empty set means all tools are
  active; the bridge reads this set on startup and after every settings save
- `AppSettings.AiChat` stores the embedded AI chat configuration
  (`AiChatSettings`): `Enabled`, `EndpointUrl` (default
  `http://localhost:1234/v1`), `ApiKey`, `ModelName`, and `MaxTokens` (default
  2048); any OpenAI-compatible provider works (LM Studio, Ollama, OpenAI,
  etc.); API key is stored in `settings.json`; the `AiChatView` re-reads
  `GetSettings` on every send so settings changes take effect immediately
- `AppSettings.ShowInternetRadioItem`, `ShowPodcastsItem`, and `ShowQueueItem`
  control the individual sidebar items for Internet Radio, Podcasts, and
  **Up Next** from Settings > Appearance; they default to visible and are
  independent of the accordion-section toggles
- `AppSettings.Theme` stores the `Light` or `Dark` theme
- `AppSettings.Language` stores `German`, `English`, `French`, or `Spanish`
- `Orynivo/Library/TrackRecord.cs`: database track model containing tags and
  technical metadata
- `Orynivo/Library/PlaylistRecord.cs`: playlist model including denormalized
  `TrackCount`, `IsSmartPlaylist`, and `FilterCriteria`
- `Orynivo/Library/SmartPlaylistCriteria.cs`: backward-compatible serialized
  smart-playlist criteria covering favourites, genres, formats, bitrates,
  metadata ranges, library/play-history rules, ordering, and result limits
- `Orynivo/SmartPlaylistDialog.*`: localized editor for the name and advanced
  criteria of an existing smart playlist
- `Orynivo/Library/PlaylistTrackRecord.cs`: playlist entry model with position,
  optional TrackId reference, and required path
- `Orynivo/Library/M3u8PlaylistService.cs`: UTF-8 M3U8 import/export with
  relative local-path resolution, relative export paths, missing-file
  preservation, HTTP/HTTPS entries, and rejection of credential-bearing URLs
- `Orynivo/Library/AudioDatabase.cs`: SQLite database layer through
  `Microsoft.Data.Sqlite`; database at `%LOCALAPPDATA%\Orynivo\library.db`
- `Orynivo/Library/LibraryScanner.cs`: directory scanner using TagLibSharp;
  writes through `AudioDatabase.Upsert()`, reports progress, and supports cancellation
- `Orynivo/Library/CueSheetParser.cs`: parses UTF-8 or legacy-encoded CUE sheets
  and creates stable virtual track paths with physical source paths and segment
  boundaries
- `Orynivo/Library/LibraryWatcherService.cs`: owns one recursive
  `FileSystemWatcher` per available configured library root, debounces paths for
  900 ms, applies incremental create/change/rename/delete updates, and runs a
  full reconciliation after 10 minutes and every 30 minutes thereafter
- `Orynivo/Library/LibraryBackupService.cs`: versioned ZIP export/import for the
  SQLite library, artwork cache, and configured library directories; audio files
  are not included
- `Orynivo/Library/LyricsService.cs`: LRCLIB client and LRC parser for
  downloaded plain or synchronized lyrics
- `Orynivo/Library/RadioBrowserService.cs`: Radio Browser client with mirror
  discovery, station search, and click registration
- `Orynivo/Library/RadioStationRecord.cs`: persisted personal internet-radio
  station model
- `Orynivo/Library/RadioStreamMetadataService.cs`: probes live ICY metadata
  through `ffprobe`; radio playback refreshes title/artist every 15 seconds
- `Orynivo/Library/PodcastService.cs`: searches the public Apple Podcasts
  catalog and resolves all playable episodes from podcast RSS/Atom feeds, newest
  first
- `Orynivo/Library/CatalogFilterCache.cs`: persists radio-genre and podcast
  category/language catalogs in
  `%LOCALAPPDATA%\Orynivo\catalog-filter-cache.json`; catalog data is refreshed
  after seven days while stale data remains usable
- `Orynivo/Library/PodcastRecord.cs`: podcast catalog, persisted subscription,
  and episode models
- `Orynivo/LyricsSearchWindow.*`: manual LRCLIB search with editable track and
  artist fields, candidate preview, and explicit replacement of the current
  track's downloaded lyrics cache
- `Orynivo/Library/ArtistProfileService.cs`: configurable artist biography and
  image lookup (Wikipedia or Last.fm); static `Source` and `LastFmApiKey`
  properties set from `AppSettings`; images cached under `%LOCALAPPDATA%\Orynivo\artist-images\`
- `Orynivo/Library/ArtistImageSearchService.cs` and
  `Orynivo/ArtistImageSearchWindow.*`: manual Wikimedia Commons artist-image
  search with editable query; selecting an image updates `artists.image_path`,
  sets `image_is_manual`, and preserves the biography source; automatic profile
  refreshes must not download over manually selected image files
- `Orynivo/EditArtistNameDialog.*` and `Orynivo/ArtistMergeDialog.*`:
  artist-info rename flow; collisions require an explicit merge-profile
  priority choice

## Audio Database

- SQLite through `Microsoft.Data.Sqlite`; no server process is required
- `tracks` stores file paths, tags, technical metadata, and references to
  normalized `artists` and `albums`
- CUE-defined tracks use a stable `cue://` path while `source_path`, `cue_path`,
  `segment_start`, and `segment_end` identify the shared FLAC/WAV source and
  playback range. The referenced source file is not also exposed as a duplicate
  whole-file track.
- `artists` contains stable artist IDs plus cached profile biography, image
  path, source URL, language, and fetch timestamp
- `artists`, `albums`, and `tracks` each have a direct `is_favorite` flag
- `albums` contains stable album IDs (`id`, `title`, `artist_id`, `year`,
  `artwork_id`, `is_favorite`)
- `artworks` deduplicates artwork by SHA-256 hash; originals and thumbnails live
  under `%LOCALAPPDATA%\Orynivo\artworks\` as `original`, `thumb_96`, and `thumb_320`
- `favorites` is an older generic extension point; visible favorites use the
  direct flags
- `play_history` records local tracks, remote Orynivo Server and Plex tracks,
  podcast episodes, and internet-radio sessions with media type, display
  title/subtitle, optional external ID, an optional `genre` captured at playback
  time (so genre statistics include tracks without a local library row),
    playback start/end, duration, final position, and completion state
- `radio_stations` stores personal Radio Browser stations by stable station
  UUID, including stream URL, logo, country, codec, bitrate, and tags
- `podcasts` stores pinned podcasts by Apple collection ID, including author,
  RSS feed URL, artwork URL, and genre
- `podcast_episode_progress` stores resume position, known duration, completion
  state, and update time per pinned podcast episode; RSS GUID is the preferred
    episode key and the audio URL is the fallback
- `AudioDatabase.GetTrackIdAndFavorite(path)` performs a lightweight `id` and
  `is_favorite` lookup
- `AudioDatabase.OpenDefault()` creates or opens `%LOCALAPPDATA%\Orynivo\library.db`
- On first launch after the rename, missing data is copied from
  `%LOCALAPPDATA%\Player\` and cached database paths are rebased to `%LOCALAPPDATA%\Orynivo\`
- Cache-path rebasing is guarded by the `cache_paths_orynivo_v1` database
  migration marker and must not run on every database open
- `Upsert()` is idempotent through `INSERT ... ON CONFLICT DO UPDATE`
- `GetPathTimestamps()` returns paths and modification timestamps for efficient
  rescans
- WAL journal mode is enabled
- Multiple library directories are stored in `AppSettings.LibraryPaths`
- Configured, currently available library directories are watched recursively.
  Watchers are replaced immediately when Settings adds, removes, or imports
  paths; unavailable roots are retried by periodic reconciliation.
- Each directory in Settings has its own Scan button, which becomes Cancel while
  scanning, with progress shown below the entry
- Directories can be added or removed; active scans are canceled when a
  directory is removed or the window closes
- Scans skip unchanged files and do not overwrite `added_at`
- Manual scans, watcher batches, and periodic reconciliations share one scanner
  gate so SQLite and Lucene updates cannot run concurrently.
- Watcher create/change/rename/delete events update SQLite and Lucene together.
  Renames enqueue both old and new paths; changed files are retried briefly
  while another process still holds them.
- Full scans are the authoritative fallback: they upsert new/changed files and
  remove missing paths from both SQLite and Lucene, covering lost or overflowed
  file-system events.
- Removing a configured library root removes tracks outside the remaining roots
  from SQLite, Lucene, and the waveform cache through
  `LibraryScanner.RemoveTracksOutsideRoots`.
- Metadata extraction supports ID3v1/v2, Vorbis Comments, APE tags, and embedded
  artwork
- Library scans include `.cue` files. CUE `FILE`, `TRACK`, `INDEX 01`, `TITLE`,
  `PERFORMER`, `REM GENRE`, and `REM DATE` metadata produces independently
  searchable and queueable virtual tracks. PCM playback seeks and stops FFmpeg
  at the stored segment boundaries; queue persistence and playback history keep
  the virtual path so tracks sharing one source remain distinct.
- Metadata extraction stores track and album ReplayGain values. The first scan
  of each configured root after ReplayGain support was added refreshes unchanged
    tracks once so existing libraries receive those values.
- Opening the database runs a legacy-data migration that normalizes artists,
  albums, and artwork and removes old per-track artwork BLOBs
- `album_artist_rebuild_v1` rebuilds album assignments strictly from
  `album_artist` so compilations are not split by track artist
- `album_title_uniqueness_v1` and `album_title_artist_identity_v1` are
  historical album migrations. `album_disc_directory_identity_v1` supersedes
  them and rebuilds album identity from normalized album title plus the
  physical album root (`source_path` for CUE tracks). Conventional disc
  directories such as `CD1`, `CD 2`, `Disc 1`, and `Disk-2` resolve to their
  common parent directory.
- `RebuildAlbumsFromAlbumArtists()` retains its historical public name but now
  rebuilds by title and physical album root. It keeps compilations and
  multi-disc releases together even when their track artists or disc
  directories differ, preserves favorites, and prefers the embedded cover
  from each physical album.
- Settings includes **Repair album artwork**, which re-reads a sample file per
  album through TagLib when historical assignments are missing
- Orynivo Server full scans run the same missing-album-artwork repair after
  scanning configured roots, so unchanged tracks with embedded covers can still
  populate missing server-side album artwork. The repair sample query must use
  physical `source_path` for CUE/virtual tracks instead of the `cue://` path.
- Settings includes **Download missing artwork**, using Cover Art Archive for
  albums with a `musicbrainz_release_id`
- Missing covers show a placeholder and manual MusicBrainz search by editable
  album title
- Manual MusicBrainz cover searches replace every character other than Unicode
  letters and numbers with a separating space before submitting the album title.
- The manual cover-search dialog uses the themed native title bar, shows search
  activity and explicit empty results, and can be run repeatedly
- Album artwork has a context menu for deletion or reassignment through manual
  MusicBrainz search
- Album cover assignment, reassignment, and deletion must preserve the selected
  album plus the exact table/artwork vertical offset across the required list
  reload. Artist-list reloads after rename follow the same rule; manual artist
  image changes update the existing row without rebinding.
- The album track detail header includes a themed heart button bound to the
  album's favorite state. Toggling it updates `albums.is_favorite` in place
  without leaving the detail view, including when opened from a favorites-only
  album list.
- The album track detail header uses the same accent border and asymmetric
  `CornerRadius="0,24,0,24"` card shape as the radio, podcast, and shared
  library intro cards.
- The main window starts maximized
- `artwork_files_v1` exports legacy artwork BLOBs into the file cache;
  `artworks.data` remains for compatibility with old `NOT NULL` schemas
  Artwork file paths are also verified per current app-data artwork root; when
  cached files are missing or point to another environment, originals and
  thumbnails are recreated from `artworks.data` and the stored paths are
  updated.
- Thumbnail generation is intentionally fault tolerant; invalid embedded artwork
  must not prevent startup
- `normalized_library_v1` prevents expensive legacy migration checks on every
  database open
- `AudioDatabase.Optimize()` runs `wal_checkpoint(TRUNCATE)`, `VACUUM`, and `ANALYZE`
- Settings library backup creates a consistent SQLite snapshot, includes album
  artwork, artist images, and library paths, reports percentage and current-file
  progress for both export and import, writes to `.tmp` before publishing the
  completed `.zip`, validates imports in staging, rebases cached image paths,
  rolls back partial replacements, and reports Lucene index rebuild progress
- Downloaded lyrics are cached in `tracks.downloaded_lyrics` /
  `tracks.synced_lyrics`; the transport note button replaces the current main
  content with a large lyrics view over a dimmed cover background, highlights
  timestamped LRC lines through the transport timer, and falls back to embedded
  unsynchronized lyrics
- The synchronized lyrics view keeps its active line both in
  `LyricLineViewModel.IsActive` and as the programmatically selected,
  non-interactive `LyricsListBox` item. Its isolated item theme must preserve
  active color, size, weight, and opacity through direct properties in the
  `^:selected` item selector; the lyric `TextBlock` binds those properties from
  its ancestor item. Never put child or descendant selectors such as
  `^:selected TextBlock` inside a `ControlTheme`: Avalonia accepts them at
  compile time but throws `InvalidOperationException` while loading the window.
- The lyrics view can manually search LRCLIB with overridden title and artist
  text. Selecting a result replaces only the database-cached
  downloaded/synchronized lyrics and leaves audio-file tags unchanged.
- `LyricsSearchWindow` uses its own plain `LyricsResultItemTheme`. Search
  results must not use the asymmetric intro/header card shape; selected and
  hover states use application surface brushes, while primary result text
  explicitly inherits the themed item foreground to avoid black text in dark
  mode.
- Custom `ListBoxItem` templates in `LyricsSearchWindow` must forward both
  `Content` and `ContentTemplate` to their `ContentPresenter`. Omitting
  `ContentTemplate` bypasses the result `DataTemplate` and renders the record's
  complete `ToString()` value, including full lyrics, in the result list.
- Artist views support table and image-card modes; visible artists lazily
  download localized biographies and images from the configured source
  (Wikipedia or Last.fm). The transport info button replaces the current main
  content with the current artist image, biography, and source link. The source
  label ("Quelle: Wikipedia" / "Quelle: Last.fm") is set dynamically from the
  stored `SourceUrl`.
- Below the biography, the artist-info view shows the artist's albums as a
  wrapped strip of clickable cover cards (`ArtistInfoAlbumsSection` /
  `ArtistInfoAlbumsPanel`, populated by `LoadArtistInfoAlbumsAsync` →
  `PopulateArtistInfoAlbums` → `BuildArtistInfoAlbumCard`). Albums are loaded
  through the shared `ILibraryCatalogProvider` (`GetAlbumsByArtistAsync`) so
  local, remote-library, and now-playing remote artists all populate the same
  way; missing covers use the `InitialsAvatar` placeholder. Clicking a card
  closes the artist-info overlay and opens the album's tracks
  (`OpenArtistInfoAlbumAsync`): local via `ShowAlbumTracksAsync`, remote via
  `OpenOrynivoAlbumTracksAsync` on the album's own server.
- Artwork A-Z navigation indexes the complete lightweight artist/album result,
  but binds rows to the virtualized wrap panels in pages. A jump must append
  through the target row and defer `ScrollIntoView` until layout has processed
  the collection changes; rebinding an artwork view resets its old offset.
- The artist information view can search Wikimedia Commons using editable text
  and assign the selected image without replacing the cached biography
  or its source URL.
- Artist images remain visible even when no biography is available.
- The artist information view can rename artists. A matching normalized name
  opens a merge dialog that asks which artist record and profile data survive;
  the transaction consolidates duplicate albums, reassigns tracks, preserves
  favorites and available album artwork, updates denormalized artist names,
  and rebuilds the Lucene index. Audio-file tags are not changed.
- Artist rename/merge dialogs must not be awaited while an `AudioDatabase`
  connection remains open. Resolve a possible collision first, dispose the
  connection, show the modal choice, then run the rename transaction.
- Manual artist renames persist exactly one `artist_aliases` comparison-key
  mapping from the original tag name to the surviving artist ID. The new
  canonical name must never be written as an alias because it is already the
  stored `artists.name` and is loaded directly by `EnsureArtistComparisonCache`.
  Scanner and watcher upserts must resolve this alias and keep the canonical
  database display name in denormalized track fields; unchanged audio tags must
  never recreate or restore the pre-rename artist name.
- After the rename transaction, update `ArtistInfoTitleButton` and related
  current/filter artist state immediately. The potentially expensive complete
  Lucene rebuild runs afterward in the background and must not delay visible
  confirmation of the new name.
- On large libraries, do not open `AudioDatabase` for collision lookup on the
  UI thread after the rename dialog. Collision lookup and a collision-free
  rename share one background connection; only an actual collision returns to
  the UI for the merge-choice dialog. Verify the committed artist name before
  refreshing lists.
- `EditArtistNameDialog` owns the complete confirmation lifecycle through
  `CommitAsync`: clicking **Rename** or pressing Enter disables its inputs,
  awaits the verified SQLite rename/merge operation, and closes only after
  success. Failures keep the dialog open with a localized status message.
  Its local button themes explicitly define centered content and theme-aware
  hover/pressed surfaces.
- Artist names are normalized when scanned: only the primary performer is
  retained, `feat.`/`ft.` suffixes are removed, and Unicode, whitespace, case,
  diacritic, and punctuation variants share one normalized artist identity
- Settings includes **Normalize artist names**, which transactionally merges
  existing variants, preserves favorites and cached profile data, updates
  visible track and album-artist names without modifying audio files, and
  rebuilds the Lucene index

## Playlist Context Menus

- Right-clicking a track, search result, album, or folder node first offers
  **Play next** and **Append to queue**, followed by existing playlists and
  **New playlist...**. Plex tracks expose only the in-memory queue actions so
  authenticated URLs cannot be written to playlist or settings storage.
- Local and remote Orynivo Server playlist context actions use
  `ILibraryPlaylistProvider`, `LocalLibraryPlaylistProvider`,
  `OrynivoServerPlaylistProvider`, and `PlaylistSelection`. Do not add separate
  local/server add-to-playlist handlers; provider selection belongs in the
  menu-building layer and mutation handlers should call the provider interface.
- Selecting a playlist immediately adds the track or all album tracks and
  updates the status bar
- Track-row playlist `ContextFlyout` instances are rebuilt directly before they
  open so playlists created from a context menu appear immediately in the next
  add-to-playlist menu without rebinding the complete table.
- The album-detail header includes **Save as playlist**. It reads the current
  `ContentDataGrid.ItemsSource`, so it saves exactly the album tracks currently
  displayed after the artist-scope checkbox is applied, and opens the shared
  themed playlist `MenuFlyout` for an existing or new regular playlist.
- **New playlist...** opens `NewPlaylistDialog`; a name is required and Enter confirms
- `Orynivo/NewPlaylistDialog.axaml/.cs` is themed with dynamic brushes and a
  DWM-colored native title bar
- `AppendPlaylistItems()` builds context-menu items dynamically
- Album artwork cards extend their existing cover menu through `ContextMenu.Opened`
- `GetPathsForRow()` returns one track path or all album tracks through `GetTrackListByAlbum`
- `PlaylistMenuTag(long PlaylistId, IReadOnlyList<string> Paths)` stores paths directly
- Folder nodes recursively collect tracks through `GetTrackPathsUnderDirectory`;
  empty folders have no menu
- Main-window context menus use application theme resources and a custom border
  template without the default white icon strip
- The global `ControlTheme x:Key="{x:Type ContextMenu}"` in `App.axaml`
  provides a complete `Template` with a `Border` bound to `Background`,
  `BorderBrush`, `BorderThickness`, and `CornerRadius`; this is required so
  that programmatically created context menus (e.g. the built-in cut/copy/paste
  menu of `NumericUpDown`) also render with theme colors instead of the
  Fluent-default white popup background. Property-only themes without a
  `Template` are silently ignored by the Fluent renderer.
- Dynamically created menu objects receive their styles through Avalonia
  `ControlTheme` resources looked up via `TryGetResource`
- Track, search-result, album-row, and folder-tree playlist actions follow the
  same proven pattern as sidebar radio/podcast/playlist actions: assign a
  themed `MenuFlyout` to the item's `ContextFlyout` property before opening,
  then register the tunnel-phase `PointerPressed` handler directly on that
  `DataGridRow` or `TreeViewItem` with `handledEventsToo: true`. The item marks
  the right-button event handled and calls
  `ContextFlyout.ShowAt(item, showAtPointer: true)` itself; do not resolve the
  item indirectly from a parent grid/tree event. Data-grid flyouts are assigned
  during `LoadingRow`; folder nodes receive a placeholder flyout when created
  and replace it with the path-specific playlist flyout immediately before
  opening. Do not use dynamically assigned `ContextMenu` instances or per-row
  `ContextRequested` handlers for these actions.
- **Delete playlist** appears in the sidebar playlist context menu, removes the
  database record, refreshes the sidebar, and returns to Tracks if needed
- Dynamic radio, podcast, and playlist `ListBoxItem` instances receive a
  `MenuFlyout` through `ContextFlyout` when they are created. A tunnel-phase
  right-button handler marks the initial press handled before
  `SelectingItemsControl` can change selection and opens the flyout with
  `ShowAt(item, showAtPointer: true)`. The flyouts use dedicated presenter and
  item themes based on the Fluent defaults with Orynivo's dynamic surface,
  border, text, hover, pressed, and separator resources. Sidebar accordion and
  repeated-selection handlers must explicitly accept only the left mouse
  button. Explicitly created `MenuItem` objects must also receive the shared
  item theme directly; `ItemContainerTheme` alone does not reliably restyle
  preconstructed controls.
- **Remove from playlist** appears only for regular playlist entries with a `PlaylistEntryId`
- `_activePlaylistId` is set by `ShowTopLevelViewAsync` only for playlist views
- `ContentRow.PlaylistEntryId` contains `playlist_tracks.id` only in regular
  playlist views
- Playlist localization keys must exist in German, English, French, and Spanish

## Editable Playback Queue

- The static sidebar item `Queue` opens the localized **Up next** view backed
  directly by `MainWindow._queue`; do not create a second playback-order model.
- Queue rows retain their `PlaylistItem` reference so duplicate paths can be
  removed and moved independently. Moving the active item must recalculate
  `_queueIndex` by reference, not by path equality.
- Queue rows can move up/down or be removed, and the complete queue can be
  saved as a regular playlist through `NewPlaylistDialog`.
- Queue mutations update navigation buttons, the visible queue table, shuffle
  history, and persisted settings together.
- The gapless PCM players receive an immutable item list at startup. Mutating
  an active gapless queue therefore restarts the current stream and seeks back
  to its audible position so the revised order takes effect immediately.

## Playlist Database Structure

- `playlists`: id, name, description, created_at, modified_at, `is_smart`, `filter_criteria`
- `playlist_tracks`: id, playlist_id, nullable track_id, path, one-based
  contiguous position, added_at
- Nullable `track_id` keeps playlist entries after a library track is removed;
  `path` is always present
- `EnsureColumn` upgrades existing databases when new columns are introduced
- Available methods include `CreatePlaylist`, `CreateSmartPlaylist`,
  `UpdatePlaylist`, `DeletePlaylist`, `GetAllPlaylists`, `GetPlaylistById`,
  `GetPlaylistTracks`, `AddTrackToPlaylist`, `RemoveTrackFromPlaylist`, and
  transactional `MovePlaylistTrack`
- `CreatePlaylist(name, paths)` imports playlist entries transactionally and
  links matching local paths to existing library tracks

## Performance Measures

- `GetTracksLite()` loads only path, file name, title, disc number, and track
  number for the folder tree
- `GetArtistsLite()` loads artist IDs, names, favorite state, and cached profile
  data without loading tracks
- `GetAlbumsLite(includeArtwork)` loads only album, display artist, and year
  unless artwork is requested
- `GetTrackList()` and related list queries load compact scalar metadata used by
  selectable track columns, but continue to omit artwork BLOBs and lyrics text
- `GetTrackListByIds(ids)` batches large ID sets to stay below SQLite variable limits
- `GetTrackListByPaths(paths)` batches queue metadata lookup and deduplicates
  query paths while the queue view restores the original order and duplicates.
- `GetTracksByDirectory(dirPath)` uses an SQL prefix query plus a direct-child filter
- `GetTrackPathsUnderDirectory(rootPath)` returns all recursive track paths
  below a root
- Folder-tree lazy loading uses an in-memory parent-to-children map and creates
  items only when expanded
- `TrackLite`, `TrackListInfo`, `ArtistInfo`, and `AlbumInfo` remain
  intentionally small; `TrackRecord` is reserved for complete metadata operations
- `GetTrackFacets()` remains a lightweight interactive-filter query;
  `GetSmartPlaylistTracks()` separately aggregates playback counts and the last
  playback timestamp only while resolving a smart playlist
- Artwork is deduplicated instead of stored per track
- `TrackSearchIndex.cs` stores a Lucene.NET index under
  `%LOCALAPPDATA%\Orynivo\search-index`, supports category-specific fields,
  partial words, and German umlaut/eszett variants, rebuilds stale indexes,
  updates incrementally after scans, and removes missing files below rescanned roots
- Search-index freshness is determined by the stored schema marker; indexed
  `Field.Store.NO` fields must not be tested through stored-document field access
- `TrackSearchIndex.RemovePaths(paths)` removes explicit watcher/full-scan
  deletions without rebuilding the complete index.
- Track `title` and `sort_title` values are trimmed before database persistence
  and again before Lucene indexing; future metadata/indexing changes must
  preserve this invariant so A-Z ordering is not affected by surrounding
  whitespace
- Avalonia `DataGrid` vertical scrolling is handled by its own pixel-based
  `PART_VerticalScrollbar`, not by an inner `ScrollViewer`. The main table
  listens to `DataGrid.VerticalScroll`; scrollbar track clicks use a
  `LargeChange` equal to the visible viewport minus one realized row

## Known Technical Details

- Target platform: `net8.0-windows10.0.19041.0`, x64
- Windows SMTC integration is created opportunistically at main-window startup.
  API or metadata failures must remain silent and must never prevent playback.
  Global commands dispatch onto Avalonia's UI thread and reuse Orynivo's normal
  play, pause, previous, next, stop, and seek paths.
- SMTC metadata follows local tracks, gapless transitions, podcasts, Plex
  tracks, remote Orynivo Server tracks, and changing internet-radio ICY
  metadata. Local artwork uses the managed artwork cache; podcast/radio artwork
  uses its public catalog URL; remote Orynivo Server artwork uses the
  authenticated `GetTrackArtworkUrl` (`?key=`) so Windows fetches it directly.
- The SMTC timeline is refreshed at most every five seconds during playback and
  immediately after track changes or seeks.
- `SteinbergAsioStream.IsBackendAvailable()` validates each native bridge and
  loads its shared export API dynamically. Settings shows **Steinberg ASIO**
  only when `AsioBridge.dll` exists and **cwASIO** only when
  `CwAsioBridge.dll` exists. A missing selected backend migrates to the other
  ASIO implementation when available, otherwise to WASAPI.
- `OutputBackend` values are persisted numerically; existing values remain
  `Asio=0`, `Wasapi=1`, and `KernelStreaming=2`, while `CwAsio=3` is appended.
- Native DSD supports `.dsf` and uncompressed stereo `.dff`
- DST-compressed `.dff` is not played natively
- Output types represented by settings: Steinberg `ASIO`, `CwAsio`, `WASAPI`, `KernelStreaming`
- Steinberg ASIO, cwASIO, and WASAPI are implemented; Kernel Streaming is not
- WASAPI handles PCM only; native DSD remains ASIO-only
- WASAPI runs exclusively and selects the highest supported sample rate up to
  the source rate, then the first supported stereo format from 32-bit float,
  packed 24-bit PCM, and 16-bit PCM. Sources above the endpoint maximum are
  converted by FFmpeg; when only higher rates are available, the lowest one is
  used.
- WASAPI plays DSF/DFF by converting DSD to PCM through `ffmpeg` without a
  temporary file using the same endpoint-aware sample-rate and precision
  selection as other PCM playback.
- Settings can force DSF/DFF through the same FFmpeg PCM path for
  ASIO/cwASIO. Forced conversion participates in gapless PCM playback and
  enables volume, ReplayGain, and equalizer processing; disabling the option
  restores native bit-perfect ASIO/cwASIO DSD routing.
- ASIO/cwASIO PCM playback queries the driver's reported sample rates before
  opening the stream and converts sources above or between supported rates to
  the highest available rate that does not exceed the source.
- The transport file-information line and status bar explicitly identify
  DSD-to-PCM conversion and show the selected PCM output sample rate.
- WASAPI pause keeps the exclusive AudioClient running and supplies silence so
  drivers do not loop the final endpoint buffer; buffered audio remains
  available for resume
- WASAPI playback position subtracts frames still queued in
  `BufferedWaveProvider`, so transport time, history, and synchronized lyrics
  follow audible output instead of producer progress
- Transport uses custom vector icons for previous, play/pause, and next;
  unavailable queue directions are disabled
- Seeking is implemented for ASIO PCM, WASAPI PCM, DSF, and DFF
- Loading a file or folder builds a playback queue; completion advances automatically
- Sequential PCM queues use one persistent ASIO/cwASIO or exclusive WASAPI
  output session. The next FFmpeg decoder is started and prefetched while the
  current track plays, then its samples are appended without reopening the
  device. Audible track changes are derived from rendered/buffered frame
  counts so transport metadata and playback history change at the actual
  boundary.
- Gapless playback is disabled for shuffle queues and native ASIO DSD
  (DSF/DFF). Those paths retain title-by-title device handling; DSD converted
  to PCM through WASAPI participates in the PCM gapless pipeline.
- Seeking remains available inside multi-track gapless PCM sessions. A seek
  clears buffered output, restarts the current FFmpeg decoder at the selected
  position, and rebuilds preparation of the following track.
- Gapless PCM position offsets are stored per queued track. Preparing or
  writing the next decoder must not reset the seek offset of the track that is
  still audible; the transport changes offsets only with the rendered track
  boundary.
- PCM user volume is applied at the active output stage rather than baked into
  prefetched samples: WASAPI follows the selected Windows endpoint's master
  volume bidirectionally and the native ASIO bridge applies an atomic volume
  factor in its callback. Per-track ReplayGain remains part of PCM sample
  preparation.
- `StartPlaybackAsync` accepts an optional `initialPosition` parameter. When
  provided, the new player is seeked to that position immediately after
  creation and before transport UI setup, ensuring no audio from position 0 is
  heard. Used by the output quick-pick popup to resume at the exact track
  position after a device switch.
- The transport action buttons for artist information, lyrics, favorite, and
  shuffle are left-aligned above the position slider; previous/play/next remain
  independently centered
- When the transport area becomes narrow, the centered previous/play/next group
  shifts right only enough to keep a 12 px gap from the left action buttons
- The position slider keeps the standard thumb size but exposes a 30 px
  transparent vertical hit area; clicking anywhere in that area updates the seek
  position while the visible track remains 3 px high
- The position slider's custom `Track.Value` binding must remain explicitly
  two-way. Its pointer pressed and released handlers are registered with
  `handledEventsToo` because the Avalonia `Thumb` handles those routed events
  while dragging.
- Shuffle keeps a per-loaded-queue set of played file paths, so duplicate
  entries and already played tracks are not selected again; loading any queue
  again resets that set while the shuffle toggle may remain enabled
- The playlist table is height-limited and scrollable
- Volume affects PCM paths; native DSD remains bit-perfect
- ReplayGain can be disabled or use track/album gain with fallback to the other
  available value. It is combined with the user volume for PCM output and uses
  saturating sample conversion to prevent integer overflow; native DSD ignores it.
- The parametric equalizer runs after ReplayGain and before ASIO/cwASIO or
  WASAPI PCM output. It supports peak, low/high shelf, low/high pass, preamp,
  and imported `GraphicEQ` curves. Live changes crossfade over 50 ms, gapless
  transitions preserve filter state, seeks reset it, and native DSD ignores
  the equalizer.
- Settings displays the combined equalizer response and a dynamic row for every
  filter. Preamp, type, frequency, gain, and Q can be edited; filters can be
  added or removed up to the same 512-filter bound used by profile import.
  Filter rows use fixed adjacent columns so type selectors share one width and
  frequency, gain, and Q use equally wide readable fields instead of being
  pushed to the right.
- Settings can retain multiple uniquely named equalizer profiles but selects at
  most one. The profile dropdown may be empty; in that state the enable option,
  import action, graph, and parameter editor remain hidden. Creating a profile
  selects it immediately, and deleting one requires confirmation.
- Equalizer profile changes and seek resets must never synchronously lock the
  UI thread against the PCM pump. Players atomically queue those requests and
  apply them from the audio pump before processing the next block. Profile
  file reading/parsing runs off the UI thread and enforces bounded input size
  and filter count.
- Settings previews equalizer enable/disable immediately against the active PCM
  player through a debounced background request; the checkbox event itself must
  never call into the player. Cancel restores the original state. Saving
  settings must only
  reconfigure endpoint synchronization when the backend or selected device
  actually changed. Driver enumeration, endpoint open/close, and player
  disposal during a device change must not run on the UI thread. Device-change
  settings application waits at most two seconds for old-player disposal
  before continuing; a misbehaving driver must not hold the settings workflow.
- Settings output-device enumeration is serialized and guarded by a load
  version. Initial ComboBox setup must not start enumeration through its
  selection-changed event in addition to the explicit initial load.
- `WasapiDeviceProvider` must dispose every temporary enumerated `MMDevice` and
  its `MMDeviceEnumerator`; only the explicitly returned render device remains
  owned by the caller.
- Applying Settings must be change-scoped. An EQ-only save must not rebuild
  sidebar/Plex navigation, reopen SQLite for ReplayGain, reapply theme or
  language, refresh output labels, or recreate endpoint synchronization.
  Settings JSON writes and DPAPI credential writes run off the UI thread.
- In ASIO DSD mode, `preferredBufferSize` counts samples rather than bytes;
  `ASIOSTDSDInt8*` writes `preferredBufferSize / 8` bytes per channel
- ASIO capability queries may fail while another application owns the device

## UI Guidelines

- Text sizing uses the shared typography scale defined as `x:Double` resources
  in `App.axaml` (`FontSizeMeta` 11, `FontSizeCaption` 12, `FontSizeBody` 13,
  `FontSizeBodyStrong` 15, `FontSizeSubtitle` 17, `FontSizeTitle` 20,
  `FontSizeTitleLarge` 24, `FontSizeHeadline` 28, `FontSizeDisplay` 34,
  `FontSizeDisplayLarge` 52, `FontSizeHero` 72). New visible text must reference
  these tokens via `{DynamicResource FontSize…}` instead of hard-coded pixel
  sizes. Immersive detail titles (album, artist, podcast headers) use
  `FontSizeDisplay`; tables use `FontSizeCaption`/`FontSizeBody`; small meta text
  uses `FontSizeMeta` with a muted foreground.
- The main window uses a modern sidebar, content area, and full-width transport
  bar
- The full Orynivo logo appears on a light logo surface in the startup window
  and at the top of the main sidebar
- The 220 px sidebar contains Dashboard, library navigation with local and
  Orynivo Server playlists, device information, About, and Settings
- Library, personal radio, pinned podcast, and Plex server sidebar groups use
  independently expandable accordion headers; local playlists are a nested
  Library child group and their expansion state is persisted
- `NavListBox` must keep its non-virtualizing `StackPanel` `ItemsPanel`. The
  sidebar collapse/expand logic animates `ListBoxItem` opacity and `MaxHeight`
  before changing `IsVisible`; a virtualizing panel recycles containers and
  drops those values, which breaks the accordion groups (and is worsened by
  variable-height rows such as the empty-library hint). Do not reintroduce
  virtualization here.
- `AppSettings.ShowInternetRadioItem`, `ShowPodcastsItem`, and `ShowQueueItem`
  control visibility of the Internet Radio, Podcasts, and Up Next sidebar items
  from Settings > Appearance and default to visible
- `AppSettings.ShowLocalLibrarySection`, `ShowOwnRadiosSection`,
  `ShowMyPodcastsSection`, and `ShowPlexSection` control group visibility from
  Settings > Appearance and default to visible. (`ShowPlaylistsSection` is a
  retained legacy setting with no UI control; the Playlists group is now a child
  of the Local node and follows the Library section visibility.)
- `AppSettings.IsLocalLibrarySectionExpanded`, `IsOwnRadiosSectionExpanded`,
  `IsMyPodcastsSectionExpanded`, `IsPlexSectionExpanded`, and
  `IsPlaylistsSectionExpanded` persist independent sidebar accordion states
- `AppSettings.IsLocalMediaLibraryGroupExpanded`,
  `CollapsedOrynivoServerLibraryGroups`, and
  `CollapsedOrynivoServerPlaylistGroups` persist the nested Local, Orynivo
  Server, and per-server Playlists child-group expansion states inside the
  Library sidebar accordion; `IsPlaylistsSectionExpanded` persists the local
  Playlists child group
- About displays the author, library licenses, and the Steinberg ASIO trademark
  notice
- The content header continues the native title-bar/sidebar appearance and shows
  title, count, search, filters, or album mode controls
- Main library views keep the plain content header for title, count, search,
  filters, and mode controls. Directly below it, Dashboard, Artists, Albums,
  Tracks, Folder structure, and equivalent library overview views use a shared
  compact accent-bordered intro card (`#6C63FF`, `CornerRadius="0,24,0,24"`)
  with a short view-specific headline and explanatory text. Search and filter
  controls stay outside and above that card in the plain header layout; A-Z
  indexes stay aligned with the content/table area, not the intro card.
- Main content view switches use short opacity fade-ins and longer library,
  remote-library, Dashboard, and album-detail loads show the shared
  `ContentLoadingOverlay` skeleton/progress state. Keep this motion subtle and
  centralized through the existing helpers instead of adding per-view ad-hoc
  animations.
- The album-detail card above its track table stretches across the available
  content width. Its favorite button appears immediately before the album title;
  cover search and **Save as playlist** remain adjacent themed actions.
- The bottom transport bar is a full-width, flush bar (top separator only, no
  floating card) showing 72 × 72 px rounded album artwork, track information,
  favorite state, playback controls, position, volume, and two quick-pick
  buttons (EQ and Output) below the volume control. The EQ popup contains a
  profile ComboBox, a ⚙ settings button, and a themed enable/disable checkbox
  (`PopupCheckBoxTheme`). The Output popup contains a profile ComboBox and a
  ⚙ settings button. Both buttons use vector SVG path icons and tooltips.
- The transport uses a cover-derived accent brush (`AppTransportAccentBrush`,
  default `#6C63FF`) for the position-slider progress fill/thumb and the
  play/pause button background. `UpdateTransportAccentFromArtwork` recomputes it
  whenever the now-playing cover changes (subscribed on
  `NowPlayingArtworkImage.Source`): `ExtractAccentColor` samples a 24 × 24 scaled
  copy, bins pixels by hue weighted by saturation × value, and normalises the
  dominant vibrant hue. It falls back to the default accent when there is no
  artwork or extraction fails; the brush is mutated in place so `DynamicResource`
  consumers repaint.
- **Up next** is a top-level sidebar view using the shared track table styling.
  It displays queue order, title, artist, album, duration, and themed
  move/remove actions, plus a header action to save the queue as a playlist.
- Settings opens inside the main window and uses a two-column layout with
  navigation on the left and content on the right
- Settings navigation reuses the main sidebar theme resources
- Settings navigation group headings must be uppercase in every supported
  language, including **WIEDERGABE/PLAYBACK**, **BIBLIOTHEK/LIBRARY**,
  **DARSTELLUNG/APPEARANCE**, **KÜNSTLERINFO/ARTIST INFORMATION**, and
  **INTEGRATION**. Child navigation items under those headings use normal
  language-specific title casing, not all caps.
- All Settings buttons use the shared themed button style, including dynamic
  scan and remove buttons
- Settings ComboBoxes use fully themed templates, inputs should stretch to the
  available content width, and device information follows the active theme and
  title-bar color
- The Plex server editor uses themed inputs and buttons plus a DWM-colored  
native title bar; Plex credential persistence must not synchronously wait on
asynchronous file I/O from the UI thread
- Selecting a Plex audio library exposes Artists, Albums, Tracks, and Folders
  modes; lists load in pages of 500, artist/album rows drill down to children,
  and folder nodes query Plex only when expanded
- Plex mode `RadioButton.IsCheckedChanged` handlers must react only to
  `IsChecked == true`; Avalonia also raises the event for the button being
  unchecked. Each asynchronous mode load snapshots its server, token, section,
  and view and validates a load version before applying rows so stale responses
  cannot replace another mode's content or columns.
- Plex track rows reuse the main track table and playback path; Plex access
  tokens remain memory-only in generated stream URLs and must never be written to
  settings, documentation, logs, or source
- Starting a Plex track from the folder tree queues only direct track siblings
  from that same tree level; subfolder tracks are excluded and the existing
  shuffle state applies to that sibling queue
- Plex tracks may contain several ordered `Media.Part` entries. The client keeps
  every part URL, and `GaplessPlaybackItem.SourcePaths` passes them to FFmpeg's
  concat demuxer as one logical track. Never reduce such an item to its first
  part, because playback would advance before the Plex metadata duration ends.
- A Plex FFmpeg decoder EOF is accepted as the track boundary only when the
  decoded position is within five seconds of Plex's authoritative duration.
  Earlier HTTP EOFs reopen the same logical item at the decoded position with
  at most three retries; they must not immediately advance the queue.
- Plex folder nodes use real `TreeViewItem` children in `Items`, matching the
  local folder tree. Do not bind an `ObservableCollection<TreeViewItem>` to
  `ItemsSource`: Avalonia can wrap those controls in additional item
  containers, producing expanded nodes with large blank child rows. The
  expand-toggle pointer event must be intercepted while a lazy node is
  unloaded; fetch and insert real child controls first, then set
  `IsExpanded=true`. Keep the `Expanded` event only as a keyboard-accessibility
  fallback that immediately collapses until loading completes. Suppress
  concurrent requests, classify nodes through `PlexMediaItem.IsFolder` rather
  than `PartKey`, and retain the placeholder after failure so loading can be
  retried. Double-clicking a folder header must intercept the second
  `PointerPressed` (`ClickCount >= 2`) in the tunnel phase and route it through
  the same lazy-load function before toggling expansion; handling only
  `DoubleTapped` is too late because Avalonia may already expose the
  placeholder row.
- The A–Z index in Plex Folders is built only from the currently displayed
  top-level directory nodes. A click or drag scrolls the real root
  `TreeViewItem` into view, and manual tree scrolling updates the active letter;
  nested lazy-loaded children do not contribute index letters.
- DSD capability states use explicit supported and unsupported theme colors
- A splash screen is shown while initial database preparation runs
- Device information displays channels, buffer sizes, PCM rates, DSD levels, and
  readable raw formats
- Supported Windows versions use DWM-colored native title bars; older versions
  keep the OS default
- Light and dark themes switch global dynamic resources for tables, artwork,
  surfaces, text, transport controls, navigation, separators, and scrollbars;
  transport buttons must not use fixed dark-only colors
- All Avalonia windows receive `AppPrimaryTextBrush` as a global fallback
  foreground. Do not add empty `ListBoxItem` styles or rely on Fluent's
  selected-item foreground in themed dialogs; it can become black on dark
  surfaces.
- Search result lists for artist images, album covers, and lyrics use the
  shared `AppSearchResultItemTheme`. Its custom presenter must forward
  `Content`, `ContentTemplate`, `DataContext`, and `TextBlock.Foreground`;
  selected items retain `AppPrimaryTextBrush` in both themes.
- Visible primary text and runtime messages use `LocalizationManager`
- TextBox normal, pointer-over, and focused Fluent theme resources must remain
  synchronized with `AppInputBrush`, `AppPrimaryTextBrush`, and the active
  input-border colors so entered text keeps sufficient contrast in both themes
- Any text placed on an accent, selected, tinted, image-derived, or otherwise
  colored background must use an explicit contrast-safe foreground resource
  (for example `AppAccentTextBrush` or a dynamically computed foreground), not a
  hard-coded assumption such as white text. Check dark and light themes before
  accepting new color combinations.
- Empty artwork areas use a dedicated placeholder resource
- Tables, lists, and trees must not expose default-white backgrounds in dark mode
- DataGrid and ScrollViewer backgrounds are overridden via Avalonia styles in
  `MainWindow.axaml`
- DataGrid row headers remain disabled through `HeadersVisibility="Column"`
- Visible rows whose playback path matches the currently audible local, Plex,
  radio, or podcast item receive the `nowPlaying` class. Its background uses
  the theme-specific `AppNowPlayingRowBrush`; selected rows retain the stronger
  selection background. Loading-row handlers must also clear the class on
  recycled virtualized rows.
- Local and Plex track nodes in the folder tree use the same `nowPlaying` class
  and theme brush. `ApplyNowPlayingClass(TreeViewItem)` must resolve local
  `FolderTag` file paths as well as Plex `PlexFolderTag` track paths. Playback
  transitions update the complete materialized `TreeViewItem` hierarchy
  recursively, including collapsed branches, while newly created or lazy-loaded
  nodes apply the class immediately. Unlike table rows, a selected folder-tree
  track must retain the `AppNowPlayingRowBrush`; otherwise the selection
  background hides the playing indicator immediately after double-clicking it.
  Local file nodes are additionally indexed by case-insensitive absolute path
  in `_localFolderTrackItems`; previous/next and gapless queue advances update
  those exact node references instead of relying only on TreeView traversal.
  Avalonia's Fluent local-tree template does not reliably paint the nested
  local node container background, so every local file node uses a dedicated
  header `Border` indexed in `_localFolderTrackHeaders`. The audible path sets
  `AppNowPlayingRowBrush` directly on that visible header and clears the prior
  header on playback start, previous/next, or gapless transition. Theme changes
  must refresh the complete highlight.
- DataGrid columns are user-resizable. Main library, search, radio, podcast,
  podcast-episode, Plex, playlist, and daily-history widths are restored from
  `settings.json`; invalid or structurally outdated width sets are ignored.
- Right-clicking a DataGrid column header opens a localized, themed
  `MenuFlyout` column chooser at the pointer position. It uses
  `StaysOpenOnClick` so several columns can be changed in one session. Do not
  replace this with a dynamically attached and programmatically opened
  Avalonia `ContextMenu`; Avalonia 11.2 retains internal ownership in that
  sequence and can throw during placement.
  Track contexts additionally expose file name, album artist, year, track/disc
  numbers, genre, bitrate, sample rate, bit depth, channels, composer, BPM,
  file size, added date, and ReplayGain values. Radio and podcast tables expose
  only metadata appropriate to those catalogs. Artwork and action columns stay
  fixed, and at least one selectable data column remains visible.
- Identified data columns can be reordered by dragging their headers. The order
  is restored independently for each table/view; fixed artwork and action
  columns cannot be dragged.
- Every selectable or reorderable data column must have a stable,
  language-independent string in `DataGridColumn.Tag`. These IDs are persisted
  in `VisibleDataGridColumns` and `DataGridColumnOrders`; changing an ID is a
  settings-compatibility change. Fixed artwork/action columns intentionally
  have no persisted ID.

## Dashboard

- The top sidebar item has tag `"Dashboard"`
- **Internet Radio** appears directly below Dashboard. It searches Radio
  Browser, plays streams through the existing FFmpeg PCM path, and adds stations
  to **Own Radios** above Playlists.
- Personal radio sidebar entries use `Radio:{id}` tags; right-clicking one
  offers deletion from `radio_stations`.
- **Podcasts** appears directly below Internet Radio. It searches Apple Podcasts
  without credentials and reads playable episodes from the selected podcast's
  RSS/Atom feed.
- Double-clicking a podcast search result or selecting a pinned podcast opens
  its complete episode list, sorted from newest to oldest. Playback starts only
  when an episode is double-clicked.
- Podcast search results support combinable multi-select category and language
  filters. Categories come from Apple's complete regional podcast genre taxonomy
  and are available before a search. Languages are detected from RSS/Atom
  `language` or `xml:lang` metadata for regional top podcasts, pinned podcasts,
  and subsequent search results. Values within one filter use OR semantics,
  while category and language filters combine with AND semantics.
- Internet-radio genres are loaded from Radio Browser's complete tag statistics
  instead of the first station-result page. Initial catalog options
  intentionally omit counts because normalized genre groups overlap multiple
  raw tags. Selecting genres performs Radio Browser tag queries (OR across
  selected genres) and may load up to 10,000 stations per genre.
- Podcast category selections work without a title query by sending the cached
  Apple genre IDs to the catalog search. Language-only filtering uses the
  regional top 100 podcasts as its starting set and then evaluates feed languages.
- The cached catalog options are used only while the radio or podcast search
  field is empty. After a text search, visible filter values and counts are
  rebuilt from that search result; clearing the search restores the cached
  catalog options.
- Pinned podcasts appear under **My Podcasts** with `Podcast:{id}` tags. The
  context menu removes them from `podcasts`.
- Podcast progress is saved about every five seconds and when playback stops.
  Incomplete episodes resume at the saved position; episodes reaching 95 percent
  or ending normally are shown as played.
- The podcast episode view uses the same violet-blue accent border as the radio
  now-playing card and a 150 px podcast image. Its header shows total, unheard,
  and started episode counts plus feed categories, language, latest publication
  date, and a shortened feed description.
- During podcast playback, the transport info button opens a dedicated detail
  view with centered podcast artwork, podcast and episode titles, publication
  metadata, duration, genre, and the episode RSS summary
- During radio playback, the Internet Radio page shows a large station logo and
  live ICY title/artist metadata when supplied by the stream; the transport
  summary is updated at the same time.
- Radio search results expose a multi-select genre popup derived from normalized
  Radio Browser tags. Selected genres use OR semantics, technical tags are
  excluded, and unavailable selections are removed after a new search.
- The page contains:
  1. **Recently added albums**: horizontal artwork strip of up to 12 albums,
  merging the local library with every configured remote Orynivo Server
  (`LoadRecentAlbumsAsync`, sorted by each album's last-added timestamp).
  Local cards open the local album/artist; remote cards (`DashboardAlbum.Server`
  set) load artwork from the server via `LoadRemoteArtworkImageAsync` and open
  within that remote library (`OpenOrynivoAlbumTracksAsync` /
  `OpenOrynivoArtistAlbumsAsync`). Backed by the server endpoint
  `GET /api/albums/recent` (`OrynivoServerClient.GetRecentAlbumsAsync`); servers
  without it are skipped. Selecting a card supports Back navigation
  2. **Calendar**: Monday-first month grid with day number, `HH:mm:ss` playback
  time, top three linked genres, today highlight, and month navigation
  3. **Top 10 genres**: descending proportional bars with `HH:mm:ss` duration,
  linked genre labels, and `_genreColors`
- Clicking a calendar day with playback opens a modal history table with playback
  time, media type, title, artist, album, listened duration, and total duration
- Local-track title links in daily history open Tracks, select the title, and
  start playback; album and artist links open their existing drill-down views.
  Daily-history cells without an available action must render as plain text, not
  disabled link-style buttons.
- Data comes from `GetRecentAlbums` (now also exposing `ArtistId`/`AddedAt` for
  cross-library merging), `GetCalendarData`, and `GetTopGenres`
- Dashboard calendar playback time includes local, remote Orynivo Server, and
  Plex tracks, podcasts, and internet radio. Top-genre statistics (the Top genres
  section and per-day calendar genres) include local, remote Orynivo Server, and
  Plex tracks: the genre query uses `COALESCE(tracks.genre, play_history.genre)`
  over a `LEFT JOIN`, falling back to the genre captured at playback time
  (`ResolveNowPlayingGenre`) for tracks without a local library row
- Clicking a dashboard genre opens Tracks with only that genre facet selected;
  other track filters are cleared and the genre filter section is expanded
- `RecentAlbumInfo` and `CalendarDayData` are records in `AudioDatabase.cs`
- `_dashboardYear`, `_dashboardMonth`, `_calendarInner`, and `_genreColors`
  hold dashboard state
- Month navigation rebuilds the dashboard so the section title changes
- Dashboard code uses `HorizontalAlignment` and `Brushes` from `Avalonia.Layout`
  and `Avalonia.Media`; no WPF namespaces are present

## Main Content Views

- **Artists**: distinct alphabetical artist list with table and artwork-card
  modes; double-click opens albums containing that artist
- **Albums**: normalized title-plus-physical-album-root list with favorite,
  96 px thumbnail, album, album artist, and year, plus a switchable artwork
  grid using 320 px thumbnails. Equal titles in different album roots are
  separate album records and open separate full detail headers and covers.
  Compilation tracks and conventional `CD1`/`CD2` sibling directories remain
  one album.
- Album views show a common album artist when one exists. The artist is display
  metadata and is not part of album identity; differing artists inside one
  physical album directory therefore do not split compilations.
- Album images are converted to `ImageSource` only when visible elements load
- `ContentRow` implements `INotifyPropertyChanged` so asynchronously loaded
  artwork appears immediately
- **Now Playing**: shows a 96 px thumbnail and track favorite button; the button
  is disabled when the current file has no database track
- `VirtualizingWrapPanel.cs` is an Avalonia `Panel` subclass that lays out
  fixed-size artwork cards from the top-left; sparse artist/album card result
  sets must not be vertically centered. It uses `StyledProperty` for
  `ItemWidth` and `ItemHeight`
- **Album tracks**: `GetTrackListByAlbum(albumId)` sorts by disc, track number,
  and file name. Normalized album IDs represent one title and physical album
  root. Multi-disc releases keep one full album header while the detail view
  renders separate track groups for their actual `CD1`/`CD2` directories and
  queues only the selected group on double-click.
- Nested multi-disc track grids and their outer album `ScrollViewer` disable
  focus-triggered bring-into-view behavior. Selecting a row must not scroll the
  complete album page or move its directory header before a double-click.
- Each nested directory/disc `DataGrid` stretches to the available width,
  disables both internal scrollbars through the DataGrid's direct scrollbar
  properties, and uses its complete column-header plus row height. Only the
  outer album `ScrollViewer` scrolls the grouped detail page.
- When album tracks are opened from an artist drill-down, the list initially
  contains only tracks by that artist; a localized **Show all album tracks**
  switch removes the artist filter and rebuilds the visible playback queue
- Artist-filtered compilation details always retain the full album cover
  header, metadata, favorite/cover/playlist actions, and the **Show all album
  tracks** switch. CD/directory group headers appear below it only when the
  current filter produces more than one physical group. Enabling the switch
  includes every directory assigned to the album; disabling it restores the
  artist filter.
- The album-track view has a centered header with a large 240 px cover, album
  title, album artist, and optional year; artwork can be searched, reassigned,
  or deleted
- **Favorites**: artist, album, and track lists and album cards can toggle their
  direct favorite flags; Artists and Albums expose a Favorites-only header
  toggle that applies to both table and artwork-card modes
- **Back navigation**: the main header Back chevron tracks a session-wide
  navigation stack for sidebar navigation, search results, album tracks, artist
  drill-downs, dashboard links, playlists, podcasts, radio, folder, and Plex
  library views. It is hidden on the initial view until a real return target
  exists. Plex child browsing may additionally use its existing intra-Plex stack
  before returning through the global stack. Album and artist table/artwork
  states retain both the selected entity and exact vertical offset; artwork
  restoration appends virtualized pages through the saved viewport and applies
  the offset only after the normal rebind reset and layout pass have completed.
- Visible artist and album names act as links across tables, search results,
  artwork cards, album headers, artist profiles, dashboard cards, and Now
  Playing; artist links open the artist's albums and album links open the
  album's tracks
- Explicit sidebar navigation clears drill-down filters, including when the
  already selected item is clicked again
- **Tracks**: title-sorted list with combinable Favorites, Genre, Audio Type,
  and Bitrate facets; counts reflect the other active filters and unavailable
  unselected values are hidden
- DataGrid double-click handlers resolve the clicked `DataGridRow` from the
  event source before falling back to `SelectedItem`, so playback and navigation
  work across the whole row, not just the visible text.
- Alphabetically sorted artist, album, and track views show an A-Z/# index
  immediately left of the right-aligned scrollbar; unavailable letters are
  disabled, dragging across letters scrolls live, and the highlighted letter
  follows the top visible entry
- The A-Z/# index uses trimmed `sort_title` where available and otherwise the
  displayed title; programmatic jumps use `DataGrid.ScrollIntoView`, while
  manual DataGrid scrolling updates the active letter from the top visible row
- **Search**: delayed Lucene search returns separate themed Track, Album, and
  Artist sections, supports partial words and German normalization variants,
  sorts by score then display name, and preserves the original query across
  drill-down Back navigation
- **Folder structure**: configured library roots start expanded; child folders
  load lazily; double-clicking a track queues its direct folder sorted by disc,
  track number, and file name
- **Playlists**: display position, title, artist, album, and duration; sidebar
  entries open their live track list
- **Smart playlists**: store JSON criteria instead of track rows, show a gold
  lightning icon, resolve live when opened, and do not permit manual entry
  removal. Criteria can include favourites, genre, format, bitrate, year,
  artist, album, duration, recently added/played windows, never played,
  playback-count ranges, alphabetical/random/recent/least-recent ordering, and
  a result limit.
- **Save smart playlist**: available in the local and remote Orynivo Server
  Tracks views only when facet filters are active; opens the compact
  `NewPlaylistDialog` and serializes the active favourite, genre, format, and
  bitrate facets through the shared `BuildCurrentTrackFilterCriteria` helper. In
  the local Tracks view it calls `AudioDatabase.CreateSmartPlaylist`; in a remote
  Tracks view it calls `OrynivoServerClient.CreateSmartPlaylistAsync` so the
  smart playlist is persisted on that server.
- Right-clicking a smart playlist in the sidebar exposes **Edit smart
  playlist**, which opens the shared `SmartPlaylistDialog` with the stored name
  and full criteria. Local smart playlists update `filter_criteria` through
  `AudioDatabase.UpdateSmartPlaylist`; remote Orynivo Server smart playlists
  update through `OrynivoServerClient.UpdateSmartPlaylistAsync` on the track's
  own server. Saving never replaces playlist rows.
- Right-clicking the Playlists accordion header exposes **Import M3U8
  playlist**. Imports are UTF-8, resolve relative local paths against the M3U8
  directory, preserve missing local paths, and retain HTTP/HTTPS entries.
  URLs containing user-info credentials or `X-Plex-Token` are skipped and
  never persisted.
- Right-clicking a regular playlist exposes **Export as M3U8**. Export writes
  UTF-8 without BOM, uses relative forward-slash local paths where possible,
  keeps HTTP/HTTPS entries, and skips credential-bearing URLs. Smart playlists
  are intentionally not exported as static M3U8 files.
- Tracks, albums, search results, and folder-tree nodes support playlist context
  menus

## XML Documentation Comments

All public and internal C# types, members, and parameters must carry XML documentation
comments (`///`). Write comments in **English**.

- Every `class`, `interface`, `enum`, `record`, `struct`, and `delegate` needs
  a `<summary>`.
- Every public or internal method and property needs a `<summary>`.
- Every method parameter needs a `<param name="…">` tag.
- Non-void methods need a `<returns>` tag.
- Use `<see cref="…"/>` for cross-references and `<see langword="…"/>` for
  keywords like `true`, `false`, and `null`.
- Generated files (under `obj/`) and AXAML code-behind auto-properties are
  exempt.

When adding or changing C# code, add or update the XML comments for all affected
members
in the same edit. Never leave a newly introduced public or internal declaration
without a
`<summary>`.

## Maintenance Requirement

Keep this file updated whenever architecture, build behavior, or user-visible
behavior changes.

Update `README.md` whenever features, requirements, build steps, supported
formats, or known limitations change so the public GitHub documentation matches
the actual project.

Keep `CHANGELOG.md` updated for every notable user-visible, architectural,
build, compatibility, or bug-fix change. Add ongoing work under **Unreleased**
and move those entries into a dated version section when preparing a release.

## Localization Rule

- Do not hard-code new visible UI text or status/error messages in XAML or code-behind
- Store all such text under `Orynivo/Localization/`
- Every new or changed string must be provided in German, English, French, and Spanish
- A text change is complete only after all supported language resources contain
  meaningful translations

## Settings Layout Rule

- Settings inputs use a label on its own row with the field below it using the
  available width
- Do not place new ComboBoxes or similar inputs beside labels in special layouts
  unless there is a clear functional reason
- Settings checkbox labels should use the compact settings typography and must
  not inherit oversized default control text.
- The Settings left navigation items carry a small vector `Path` icon before the
  label; the icon `Fill` follows the nav item foreground (selected/hover) via a
  `ListBox#NavListBox ListBoxItem Path` style so icons recolour with the row.
- Genuine on/off options use the pill toggle `SettingsToggleTheme` (still a
  `CheckBox`, so code that reads `IsChecked` is unchanged): DSD-to-PCM, equalizer
  enable, MCP server enable, AI chat enable, and the Appearance sidebar-visibility
  options. The 19 MCP per-tool entries stay on `SettingsCheckBoxTheme` because
  they form a permission checklist, not a single on/off switch.
- Interactive settings inputs (TextBox, ComboBox, NumericUpDown, buttons) share
  a consistent 30 px height.
- Subsystem availability is surfaced with `StatusBadge` controls (FFmpeg,
  Steinberg ASIO, cwASIO in the Output section; MCP in the MCP section). Each
  configured Orynivo Server / Plex server row additionally shows a live
  connection badge probed asynchronously so opening Settings stays instant.
