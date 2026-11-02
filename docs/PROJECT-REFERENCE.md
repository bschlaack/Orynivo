# Project reference

This reference supplements the root and nested `AGENTS.md` instructions. Read
the sections relevant to the area being changed. Paths written in code spans are
repository-relative. Current subtree contracts take precedence over general
descriptions here; the root completion checklist always applies.

Implementation milestones and old measurements are historical context, not proof
of current behavior. The code, focused regression tests, and the current
[visualizer status](VISUALIZER-STATUS.md) identify the active implementation.

## Project Overview

Avalonia desktop music library with:

- Cross-platform core library in `Orynivo.Core/` (library scan, database,
  search, streaming models)
- Avalonia 12.1.2 frontend in `Orynivo/` (Windows, Linux, and macOS; references
  `Orynivo.Core`)
- Cross-platform headless music server in `Orynivo.Server/` (ASP.NET Core;
  references `Orynivo.Core`; exposes REST + streaming over the local network)
- Native Steinberg ASIO bridge in `Native/AsioBridge/`
- MIT-licensed cwASIO bridge in `Native/CwAsioBridge/`
- Static localized product website in `html/`; edit the German source page and
  `i18n.js`, then run `node html/generate-localized-pages.js` so English,
  German, French, Spanish, Russian, Simplified Chinese, and Hindi pages remain
  synchronized. Its five-minute quick-start styles live in
  `html/quickstart.css`.
- Structured public contribution forms live in `.github/ISSUE_TEMPLATE/`. Bug
  reports must warn contributors not to disclose credentials, authenticated
  URLs, private paths, device addresses, or personal library data; security
  reports must route to private GitHub security advisories.
- PCM playback through `ffmpeg`
- Native DSF/DFF DSD playback through ASIO on Windows or direct ALSA on Linux
- Real-time DSF/DFF-to-PCM conversion through `ffmpeg` for WASAPI playback

The desktop project builds on Windows, Linux, and macOS. Windows retains the
complete WASAPI/ASIO playback implementation. The current Linux `net10.0` build
supports PCM playback through selectable direct ALSA hardware and OpenAL output
devices in addition to the library and network/UI feature set; it excludes
Windows audio, endpoint-volume, and SMTC implementations and uses compatibility
types under `Orynivo/Compatibility/Linux`. System-media control on Linux is
provided by MPRIS 2 (`org.mpris.MediaPlayer2.orynivo`) instead of SMTC. Linux
credentials use the desktop client's AES-GCM current-user credential container
and must never persist secrets in plaintext.

Linux native DSD does not use either Windows ASIO bridge. Local and
HTTP-range-streamed stereo DSF and uncompressed stereo DFF/DSDIFF use the
compatibility players under `Orynivo/Compatibility/Linux`, open ALSA
`DSD_U32_BE` directly, and fall back to DoP when native DSD is unavailable.

Linux output-profile editing presents OpenAL and direct exclusive ALSA as
separate output types even though both retain the persisted
`OutputBackend.Wasapi` compatibility value. The selected device ID remains the
routing discriminator: `alsa:` identifies direct ALSA; every other Linux device
ID uses OpenAL. Existing profiles must be classified from that ID.

macOS desktop builds target `net10.0` for `osx-arm64` and `osx-x64`. PCM
playback uses Apple's system OpenAL framework. FFmpeg/FFprobe discovery checks
the application and per-user cache, inherited `PATH`, and conventional Homebrew,
MacPorts, pkgsrc, Fink, and per-user binary directories because Finder-launched
apps receive a minimal `PATH`; missing tools are downloaded for the current
architecture into the per-user cache. ALSA and native DSD output remain
Linux-only. Tagged releases package self-contained `Orynivo.app` bundles for
both Mac architectures as installable PKGs, portable ZIPs, and tar archives.
Each bundle generates `Orynivo.icns` from `Logo/icon.png`, stores it under
`Contents/Resources`, and declares it through `CFBundleIconFile`; every package
is included in the signed release manifest. The desktop updater selects the
current process architecture's signed `pkg`, verifies its digest, and opens it
through `/usr/bin/open`; Orynivo must never invoke a privileged installer
command directly.

## Orynivo.Core

`Orynivo.Core` is a `net10.0` class library with no platform-specific
dependencies. It holds everything needed to scan, store, index, and serve the
music library:

**Orynivo.Core/Library/**: `AudioDatabase`, `LibraryScanner`,
`LibraryWatcherService`, `LibraryBackupService`, `TrackSearchIndex`,
`GenreCloudService` plus its embedded `GenreTaxonomy.json` multi-parent graph,
`CueSheetParser`, `M3u8PlaylistService`, `LyricsService`, `RadioBrowserService`,
`RadioStreamMetadataService`, `PodcastService`, `ArtistProfileService`,
`ArtistImageSearchService`, `ArtistNameNormalizer`, `ArtworkCache`,
`MusicBrainzCoverSearch`, model records, `ArtistInfoSource` enum.

**Orynivo.Core/Audio/**: `ReplayGain`, `ReplayGainMode`, `EqualizerApoParser`,
`ParametricEqualizer`, `FfmpegPcmDecoder`, `FfmpegLocator` (cross-platform:
downloads missing FFmpeg/FFprobe on Windows and macOS; Linux uses
system-installed tools), `WaveformCache` (cached compact FFmpeg-generated peak
data for the transport waveform), `SeekDiagnostics` (sanitized transport seek,
FFmpeg decoder, and server-side transcode diagnostics under `logs/seek.log`),
`EqualizerProfile`, `EqualizerFilter`, `EqualizerFilterType`,
`CrossfeedProcessor` (optional headphone crossfeed) and `CrossfeedStrength`, and
`StreamingLoudnessNormalizer` (optional loudness matching for radio and podcast
streams).

**Orynivo.Core/Web/**: `WebBrowsingService` (SSRF-guarded page fetch + SearXNG
search), `WebBrowsingOptions` (persisted config), `HtmlContentExtractor`
(HTML→text/Markdown). Backs the `search_web`, `fetch_page`, and
`fetch_page_as_markdown` tools for MCP and the AI chat.

**Orynivo.Core/Streaming/**: `IStreamingCatalog`, `IStreamingPlaybackProvider`,
`IStreamingCredentialStore`, `StreamingModels` (all streaming model records,
enums, `PlexServerSettings`, and `OrynivoServerSettings`), `PlexServerClient`,
`OrynivoServerClient` (HTTP client for browsing and streaming a remote Orynivo
Server library — provides `GetArtistsAsync`, `GetAlbumsByArtistAsync`,
`GetAlbumsAsync`, `GetTracksByAlbumAsync`, `GetTracksAsync`,
`GetLibrarySummaryAsync`, `TestConnectionAsync`, and static URL helpers
`GetStreamUrl`/`GetAlbumArtworkUrl`/`GetArtistArtworkUrl`; also uploads
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

- `Audio/SteinbergAsioStream`, `FfmpegAudioPlayer`, `WasapiAudioPlayer`,
  `WasapiDeviceProvider`, `DsfAudioPlayer`, `DffAudioPlayer`,
  `WindowsEndpointVolumeSynchronizer`
- `WindowsMediaTransportService`, `Streaming/WindowsStreamingCredentialStore`,
  and `Streaming/WindowsPlexCredentialStore` Windows implementations

The Avalonia UI itself is now also compiled for Linux. The named Windows
services above remain Windows-only implementations and are replaced by the
limited compatibility types described above for the Linux target.

## Build and Run

Skia shader fidelity tests must raise both `ShaderTimeBudgetMilliseconds` and
`ShaderPassBudgetMilliseconds` so cold compilation and CI contention cannot
abort the reference frame or force a fallback. Assert that rendering reports no
shader error; retain production budgets in application code.

```powershell
.\build.ps1
.\Orynivo\bin\Debug\net10.0-windows10.0.19041.0\Orynivo.exe
```

Run the managed unit tests with
`dotnet test Orynivo.Core.Tests/Orynivo.Core.Tests.csproj`,
`dotnet test Orynivo.Tests/Orynivo.Tests.csproj`, and
`dotnet test Orynivo.Server.Tests/Orynivo.Server.Tests.csproj`.
`Orynivo.Core.Tests` covers the cross-platform core library; `Orynivo.Tests`
covers pure desktop helpers that do not require a running Avalonia UI;
`Orynivo.Server.Tests` covers middleware and isolated in-memory endpoint hosts.
Do not move shared behavior into a UI-only class when a `Orynivo.Core` type can
own it and stay cross-platform testable. The Windows build workflow runs all
three test projects plus the isolated Windows headless workflow smoke suite.
`scripts/verify-all.ps1` runs the same checks locally in one command (managed
builds with `--warnaserror`, the non-Windows desktop compile via `-p:OS=Unix`,
all three test projects, Windows UI smoke on Windows, and the three parity
scripts) and stops at the first failure; CI still runs these steps itself. The
non-Windows compile matters because the desktop swaps in `Compatibility/Linux`
implementations for several Windows types, so a Windows-only local build cannot
see a broken Linux or macOS call site. Use it before declaring work complete.
Every test that changes the process-wide playback profile through
`AudioDatabase.SetActiveProfile` must save `AudioDatabase.ActiveProfileId` first
and restore it in a `finally`. `ActiveProfileId` is process-wide `AsyncLocal`
state, so a leaked profile makes unrelated profile-scoped queries (playback
history, the year-in-review summary, cross-device positions) silently return
nothing, and the resulting failure looks like a query bug instead of a
test-isolation bug. Every test that opens a library database must use
`Orynivo.Core.Tests.CoreTestDatabase`, which owns a unique temporary directory
per test and clears only that database's SQLite pool on disposal. Never call
`AudioDatabase.OpenDefault()`, reference `AppPaths.DataRoot`, delete a shared
database file, or call `SqliteConnection.ClearAllPools()` from a test: xUnit
runs test classes in parallel, so shared paths and process-wide pool clearing
leak rows between tests and produce intermittent failures. `TestEnvironment`'s
module initializer remains the safety net that keeps the whole run away from the
real per-user data directory.

`build.ps1` always builds the vendored MIT-licensed `CwAsioBridge.dll`, then
builds `AsioBridge.dll` when the Steinberg SDK is available, and finally builds
the .NET application. It locates Visual Studio MSBuild through `vswhere.exe` or
`PATH`. The ASIO SDK can be supplied with `-AsioSdkDir`, `ASIO_SDK_DIR`, or a
local `third_party/asiosdk` / `external/asiosdk` directory. `-MSBuildPath` and
`MSBUILD_EXE_PATH` override MSBuild discovery. If no SDK is found, the script
builds without the Steinberg bridge; `-SkipAsio` forces that mode and
`-RequireAsio` makes a missing SDK fatal. `-SkipCwAsio` additionally disables
the cwASIO bridge. Managed build properties remove disabled native DLLs from
build and publish output.

`.github/workflows/dotnet-desktop.yml` builds the Windows cwASIO bridge and
managed Avalonia project in Debug and Release, and separately builds and
publishes the self-contained `linux-x64` desktop artifact. The Windows job
intentionally excludes only the Steinberg bridge because that SDK is not stored
in the repository. Its Release artifact therefore contains cwASIO support
without Steinberg SDK files. The same workflow has a dedicated `verify` job that
runs `scripts/verify-mcp-tool-parity.ps1` and
`scripts/verify-localization-parity.ps1` on every push and pull request, so MCP
tool parity and the seven-language desktop/website/mobile localization coverage
cannot silently drift. Keep these scripts passing; do not remove the job.
`.github/dependabot.yml` tracks NuGet and GitHub Actions updates weekly, but
ignores NuGet upgrades that cannot build on the pinned toolchain: Avalonia
**major** updates plus major upgrades of Microsoft.Data.Sqlite and
Microsoft.NET.Test.Sdk. Migrate those deliberately instead of merging an
automatic bump. The projects target **.NET 10 LTS** and Avalonia **12.1.2**; do
not move to .NET 9, which is already in security-only maintenance and reaches
end of support on 10 November 2026, the same day as .NET 8.
`Avalonia.Controls.DataGrid` is part of the Avalonia 12 line again, so it moves
with the other Avalonia packages; the earlier "no release beyond 11.3.13" note
was wrong and is corrected here. SkiaSharp moves with Avalonia too:
`Avalonia.Skia` 12.1.2 depends on SkiaSharp 3.119.4, so `Orynivo.Core` and
`Orynivo.Server` use that line and must not be raised independently. The
deliberate migration triggers, steps, and required checks for every held-back
line are recorded in [`DEPENDENCY-MIGRATION.md`](../DEPENDENCY-MIGRATION.md);
keep that record and the Dependabot `ignore` list in agreement, and never merge
an ignored major upgrade without following the recorded plan. All GitHub-hosted
CI and release workflows use Node.js 24-compatible action generations
(`actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/upload-artifact@v7`,
and `softprops/action-gh-release@v3` where applicable); do not reintroduce their
Node.js 20 predecessors. Every action must be referenced with **one** version
across every workflow: Dependabot's github_actions updater fails with
`Error processing <action> (RuntimeError)` / `No files changed!` when the same
action is pinned to different majors in different files, and mixed majors still
run fine in CI, so nothing else catches the drift.
`scripts/verify-github-actions-pins.ps1` enforces this and runs in both
`scripts/verify-all.ps1` and the CI verify job. The Windows
`.github/workflows/release.yml` workflow is the sole creator of the canonical
`Orynivo <version>` draft release. Linux, macOS, and server matrix jobs must
locate that exact draft through its numeric release ID and upload their assets
to it; they must never independently create a draft by tag because concurrent
draft lookup can create duplicate releases. The shared uploader validates the
live tag against the build checkout before release admission and each upload
attempt. Moved tags reject obsolete builds. Every retry reconciles an existing
asset before POST, including uploads that arrived despite an HTTP error. Offline
command-substitution checks cover normal upload, replacement, interrupted
success, tag changes and bounded failure in the CI verify job.

`.github/workflows/player-linux-release.yml` publishes tagged self-contained
desktop builds for `linux-x64` and `linux-arm64` as portable tarballs, DEB, and
RPM packages, plus an Arch Linux `x86_64` package. These packages install the
player under `/usr/lib/orynivo`, add `/usr/bin/orynivo`, and register its
desktop launcher. Every Linux player artifact is required by and included in the
signed release manifest. The Arch `.pkg.tar.zst` must store `.PKGINFO` as an
exact root-level archive entry (not `./.PKGINFO`), and the workflow must
validate that entry before uploading the package because `pacman -U` otherwise
rejects the archive as missing package metadata.

The unpublished `packaging/aur/PKGBUILD` repackages only the release `usr` tree,
pins the version and SHA-256, and declares native runtime dependencies. It
preserves upstream binaries with `!strip` and `!debug`; CoreCLR tracing is
optional. Keep its generated `.SRCINFO` synchronized with every recipe change.
See `packaging/README.md` for validation evidence and remaining acceptance.

## Orynivo.Server

`Orynivo.Server` is a `net10.0` ASP.NET Core Minimal API server that exposes the
local music library over the network. It references `Orynivo.Core` and has no
Windows-specific dependencies; it runs on Windows, Linux, and macOS. The server
project references `SkiaSharp.NativeAssets.Linux.NoDependencies` so Linux
packages include the native SkiaSharp library required for artwork thumbnail
generation; do not replace this with an external ImageMagick/convert runtime
dependency.

**Configuration** (`appsettings.json`, section `Orynivo`):

- `ApiKey` — pre-shared key required in every request (`X-Api-Key` header or
  `?key=` query param; query param enables direct use in FFmpeg URLs)
- `LibraryPaths` — list of root directories to scan
- `ScanOnStartup` — run a full scan when the server starts (default `true`)
- `CalculateMissingReplayGainDuringScan` — optionally run expensive FFmpeg
  analysis for missing ReplayGain values during normal server scans (default
  `false`; embedded values are always imported)
- `ServerName` — display name returned by `/api/info`
- Default bind: `http://0.0.0.0:5280`

**Key files:**

- `Orynivo.Server/Program.cs`: builds and starts the server; calls
  `FfmpegLocator.EnsureAvailableAsync()`, registers `LibraryWatcherService` and
  the `LibraryService` hosted service, maps all endpoints including remote
  configuration and directory browsing
- `Orynivo.Server/ServerSettings.cs`: configuration POCO bound from the
  `Orynivo` config section
- `Orynivo.Server/Middleware/ApiKeyMiddleware.cs`: skips `/api/health`,
  validates `X-Api-Key` header or `?key=` query param, returns 401 JSON
- `Orynivo.Server/Services/LibraryService.cs`: `IHostedService` that calls
  `LibraryWatcherService.UpdatePaths` on start and runs full scans via the
  static `LibraryScanner.ScanAsync`; exposes `TriggerScan()` for manual trigger
  and `ScanStatus` with current root, processed/total counts, current file, last
  result, errors, and a persisted `LibraryChangedAt` Unix timestamp that updates
  when scans or watcher runs add, update, or remove indexed tracks
- `Orynivo.Server/Endpoints/LibraryEndpoints.cs`: artists, albums, tracks,
  playlists (smart playlists resolved via `SmartPlaylistCriteria.Resolve`), and
  Lucene search endpoints; `GET /api/albums/recent` returns the most recently
  added albums (id, title, artist, `ArtistId`, `AddedAt`, `HasArtwork`) for the
  client dashboard's cross-library Recently Added widget;
  `GET /api/library/summary` returns aggregate album, track, artist, and
  favourite counts for cross-library Dashboard totals without materializing
  complete library rows; `GET /api/albums/recommendation-candidates` returns
  compact album genre and average-BPM metadata for client-side history-based
  Dashboard recommendations; `GET /api/artists/{id}` returns complete cached
  artist profile fields, `POST /api/artists/{id}/profile` stores
  client-refreshed biography/source fields plus optional image bytes, and
  `POST /api/artists/{id}/rename` renames or merges artists then rebuilds the
  server Lucene index; Last.fm, Wikipedia, and Wikimedia image-search requests
  run on the client, not on the server; `GET` `/api/tracks/{id}/lyrics` returns
  cached plain/synced lyrics with the last fetch timestamp and
  `PUT /api/tracks/{id}/lyrics` stores client-downloaded LRCLIB lyrics (the
  LRCLIB request runs on the client); `GET` `/api/tracks/{id}/waveform` returns
  cached compact waveform peaks generated server-side through `WaveformCache`
  and stored under the server data directory; the track DTO includes the primary
  `ArtistId` and `AlbumId`, and the album DTO includes `ArtistId`, for
  in-library navigation; `GET` `/api/tracks/facets` returns lightweight facet
  rows (`TrackFacetInfo`) and `POST` `/api/tracks/by-ids` returns track rows for
  a posted ID list, together powering the remote Tracks facet filters;
  `GET /api/playlists` (including `FilterCriteria` for smart playlists),
  `GET /api/playlists/{id}/tracks` (smart playlists are resolved server-side
  through `SmartPlaylistCriteria.Resolve` using the server's own favourite
  flags), `POST /api/playlists/{id}/resolve` (resolves a smart playlist while
  overriding favourite state with the client-supplied favourite track IDs,
  because remote favourites live client-side; this is what the Windows client
  uses to open remote smart playlists so a `FavoritesOnly` criterion matches the
  client's favourites instead of the server's unset flags),
  `POST /api/playlists/resolve-count` (resolves ad-hoc client-supplied criteria
  and returns only the match count, backing the smart-playlist editor's live
  preview for remote playlists), `POST /api/playlists`,
  `POST /api/playlists/smart` and `PUT /api/playlists/{id}/smart` (create and
  update a smart playlist from client-supplied criteria, which the server
  re-validates through `SmartPlaylistCriteria`),
  `POST /api/playlists/{id}/tracks`, `DELETE /api/playlists/{id}`, and
  `DELETE /api/playlist-tracks/{id}` expose server-side playlist browsing and
  regular- and smart-playlist editing for remote clients; remote playlist writes
  must use server-side track IDs, never credential-bearing stream URLs; `GET`
  `/api/folders/tracks` returns lightweight track rows plus playback metadata
  (artist, album, duration, format, primary `ArtistId`, `AlbumId`) for building
  the remote server library folder tree and must materialize the response before
  disposing the SQLite connection because JSON serialization runs after the
  route handler returns
- `Orynivo.Server/Endpoints/StreamEndpoints.cs`: byte-range streaming for
  regular audio files; on-the-fly FLAC transcode via FFmpeg pipe for CUE virtual
  tracks; `GET /api/stream/{trackId}?ss=<seconds>` performs a fast server-side
  seek by transcoding the local file from that offset to FLAC (used by remote
  clients so in-track seeking does not binary-search a seektable-less file over
  HTTP); `?format=opus|aac&bitrate=<64-320>` requests a validated lossy
  transcode (`StreamTranscodeOptions`) for regular files and virtual segments
  alike, returning 400 for unsupported requests; the transcode/FFmpeg process is
  stopped when the client disconnects; album, artist, and track artwork
  endpoints (track artwork is served both by file path via
  `/api/artwork/track?p=` and by database ID via `/api/artwork/track/{id}` with
  an optional `?size=`); album artwork requests fall back to an on-demand
  embedded-artwork repair for the requested album before returning 404; `PUT`
  `/api/artwork/album/{id}` and `/api/artwork/artist/{id}` accept raw image
  bytes from authenticated clients and store them in the server-side artwork
  caches
- `Orynivo.Server/Endpoints/ConfigurationEndpoints.cs`: authenticated
  `/api/settings/library-paths` GET/PUT and `/api/files/directories?path=`
  endpoints plus authenticated `/api/settings/replaygain` GET/PUT for the
  server-owned scan-time ReplayGain preference; the ReplayGain PUT updates the
  live watcher and persists the value immediately. The library-path PUT persists
  `Orynivo:LibraryPaths`, refreshes `LibraryWatcherService`, and starts a scan.
  Settings are written to the editable, service-writable config
  (`/etc/orynivo-server/appsettings.json` via
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
`linux-x64` and `linux-arm64` and publishes four packages to the draft GitHub
Release: `amd64`/`arm64` DEB and `x86_64`/`aarch64` RPM. Support files live in
`.github/server-release/` (systemd unit, postinst/prerm scripts). The packages
install to `/usr/lib/orynivo-server/`, expose a `/usr/bin/orynivo-server`
symlink, ship a default config at `/etc/orynivo-server/appsettings.json`, and
register `orynivo-server.service` running as the `orynivo-server` system user.
The signed update-manifest workflow waits for the Windows installer, every Linux
and macOS desktop artifact, and all four server packages before signing, refuses
partial manifests, and supports a manual tag input for rebuilding an already
published release.

The `orynivo-server` service user is created with `--no-create-home` and has no
writable `$HOME`, so the default data directory (`$HOME/.local/share/Orynivo`)
cannot be created. `AppPaths.DataRoot` therefore honours the `ORYNIVO_DATA_DIR`
environment variable, and the systemd unit sets
`ORYNIVO_DATA_DIR=/var/lib/orynivo-server` plus `StateDirectory=orynivo-server`
(the post-install also creates `/var/lib/orynivo-server` owned by the service
user). The server's SQLite database, caches, and downloaded artwork live there.
Explicit data roots never import legacy Player settings or caches. Default roots
copy missing legacy files once, guarded by `.legacy-data-migrated`. A removed
marker allows migration again; renaming the default root alone is not a clean
reset when Player still exists. Concurrent destination creation is tolerated
only for file-exists errors, without overwriting; other I/O failures propagate
and leave migration unmarked for retry. Regression tests use temporary roots.

Do not remove the data-directory override or the systemd unit will abort on
startup with `UnauthorizedAccessException`/`SIGABRT`.

**Signed updates:** Desktop and server release versions are injected only by
GitHub release builds from semantic `v*` tags contained in `main`. Published
releases receive an ECDSA P-256 signed manifest of SHA-256 asset digests. The
desktop verifies it before self-update or server relay. Remote server updates
are opt-in; `UpdateEndpoints` stages only matching bounded DEB/RPM bundles
beneath the data root, and the fixed-command root systemd helper independently
verifies the manifest/package before invoking the package manager. Never add an
unsigned fallback or allow client-provided commands/paths to reach the helper.

## Important Architecture

High-resolution WavPack technical properties can be absent or throw through
TagLib while tags are valid. LibraryScanner preserves those tags and uses
WavPackTechnicalMetadata to fill missing duration, sample rate, bit depth,
channels and encoded bitrate. FFprobe reads selected technical fields only, with
scan cancellation, a 15-second timeout and no source mutation. Failed tag opens
retain the existing failure/retry contract. Each CUE source is probed once per
sheet; source duration is available before segment-end projection. Existing
unchanged rows require explicit Refresh all metadata on their owning library.

WASAPI output rate selection excludes every rate above a positive
MaxOutputSampleRateHz, including fallback candidates. Zero remains unrestricted.
FFmpeg resamples PCM to the selected accepted rate. If the driver supports no
format within the limit, selection fails before output starts; advertised higher
rates must never bypass an explicit cap. Pure desktop tests cover 384-kHz
WavPack metadata, PCM/DSD caps and an empty eligible candidate set.

- `Orynivo/OutputProfile.cs`: named audio output configuration (backend, device
  IDs, display name); multiple profiles allow switching between output devices
  without reconfiguring each time
- `Orynivo/Audio/AudioPlayerRetirement.cs`: UI-free ownership of detached-player
  background disposal. Each synchronous backend disposal uses an independent
  LongRunning worker on TaskScheduler.Default with DenyChildAttach, so blocked
  drivers do not occupy shared thread-pool workers needed by timeout callbacks,
  preparation or drain continuations. `MainWindow.StopPlaybackAsync` clears
  window playback state, removes the gapless event subscription, and transfers
  its detached player to this helper. Ordinary transitions wait at most two
  seconds for the current disposal; an empty ordinary transition returns
  immediately. Timed-out workers remain tracked until completion. A complete
  output-release wait covers every retirement already registered, even when no
  active player remains. Disposal and existing error-sink failures are isolated;
  completed work and unused budget timers are removed. The injected TimeProvider
  makes deadline tests deterministic. PlaybackSessionCoordinator owns active
  output and its cancellation. Queue, history, source context and UI cleanup
  remain window-owned; pending-start tracking is separate from this retirement
  helper. No backend selection, PCM/DSD processing or native output changes are
  introduced here.
- `Orynivo/Audio/PlaybackSessionCoordinator.cs`: single dispatcher-affine active
  output and linked cancellation owner. TryBegin admits current uncancelled
  intents only with empty output. TryAttach rejects stale or occupied handoffs;
  rejected players remain caller-owned. DetachForRetry checks intent and player
  identity and preserves cancellation for the next gapless preparation. Detach
  clears ownership before cancellation callbacks and returns output once for
  retirement. Callback/reporting failures cannot strand output. Window
  source/history/UI adapters and the authoritative queue remain separate. Tests
  exercise handoff with fake players without a window or audio hardware. Owns
  combines intent, live cancellation and identity. SeekAsync checks before and
  after best-effort initial/resume seek; PrepareReadiness and TrySetPaused guard
  backend pause/resume callbacks before transport publication.
  PlaybackSessionLifecycleTests compose held factory/seek, readiness and full
  release with real helpers, including cancelled late output and unique
  retirement. Window stop adapters retain their captured intent across every
  cancellation boundary and abandon obsolete cleanup. Advisory/mix/waveform
  state and artist background ownership are cleared/replaced before callbacks.
  System, slider and MCP/mobile seek adapters hold preparation leases, capture
  output identity and guard completion. Full release drains pending seeks;
  superseded remote acknowledgement cancels while current errors propagate.
- `Orynivo/Audio/PlaybackOutputDrain.cs`: composes preparation completion and
  detached-player retirement after the window's synchronous stop cleanup.
  Register retirement before awaiting preparation, then await initial disposal
  and a second retirement pass to include late factory results. Ordinary waits
  retain the retirement budget and do not await preparation. The helper owns no
  active player, cancellation, admission or UI; release publication remains
  guarded by the window. Tests use real trackers and controlled fake disposal
  without an Avalonia window or audio hardware.
- `Orynivo/Audio/PlaybackShutdownCoordinator.cs`: dispatcher-owned permanent
  admission closure and shared shutdown outcome. Publish the result task before
  invoking synchronous hooks or output drain; reentrant and later requests must
  return that task without repeating hooks. Success, faults and cancellation
  retain closed admission. MainWindow's shutdown guards delegate to this state;
  its adapter closes queue edits and disables the window before full release.
  Actual Close remains window-owned. Unit tests cover these boundaries without a
  window; isolated window checks include reentrant queue cancellation.
- `Orynivo/Audio/PlaybackStartOwnership.cs`: dispatcher-affine intent generation
  and late-factory cleanup. Start captures ownership before remote lookup; stop
  invalidates it. Backend creation receives a fixed token, including native DSD
  fallbacks. Check ownership after retirement, creation and resume seeks before
  publishing UI state. Late-created players transfer to the shared retirement
  helper; obsolete lifetime errors do not reach caller stop handlers. Guard
  remote lookup registration on the dispatcher. Active output is session-owned;
  the queue remains window-owned.
- `Orynivo/Audio/PlaybackReadiness.cs`: dispatcher-owned prepared-player result
  separate from track completion. Initial seek and paused state precede
  readiness; abandoned starts return null, preparation errors reach the awaiting
  caller, and later failures reach an active-player-guarded observer. Output
  switching and lock reacquisition use readiness instead of awaiting the full
  track. Selection generation guards retirement completion; fades bind to their
  own ready player and guard every volume write, including after the final
  delay. Failed reacquisition drains output before publishing release and
  preserves retry context. `Orynivo/Audio/PlaybackPlayerFactory.cs` is the
  creation adapter used by production routing and isolated fake-factory window
  checks; it does not introduce another active player or queue.
- Single-track MCP Play forwards the original opaque reference to the owning
  playback callback; do not resolve it through the independent queue resolver
  first. Mobile single-track Play allocates start ownership and a preparation
  lease before local/server identity lookup. Its captured token cancels pending
  work; dispatcher registration and returned paths check the owning intent. End
  that lease before handing off to the existing readiness start. Stop,
  replacement and normal full output release include this pending lookup. Queue
  append/next remain independent queue actions.
- `MainWindow.ReplacePlaybackQueueAsync` owns an intent/preparation before
  ordered local/remote resolution, then updates the existing queue and awaits
  readiness. `McpPlayerBridge.ReplaceQueueFunc` accepts original references and
  returns the prepared playable count; MCP must not resolve them before
  dispatch. Preserve duplicate occurrences and retain the old queue if no
  reference resolves. Mobile playlist Play captures ownership and catalog
  generation before loading, guards registration/mutation, and ends its lease
  before replacement handoff. Superseded results cannot replace the queue; full
  release drains pending lookup. Playlist append keeps its existing independent
  semantics.
- Infinite Mix refill captures its mix version and playback intent, with a
  preparation lease and linked cancellation. Stop, mix pause and replacement
  reject late queue and metadata publication. Genre and similarity resolvers
  guard registration on the dispatcher; stale similarity results cannot advance
  a reset cursor. Only the owning version clears loading/progress state. Full
  release drains admitted refills. Threshold checks require an active player;
  idle initial mix start awaits readiness rather than track completion.

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

- `Orynivo/Audio/GaplessQueueRefreshCoordinator.cs` owns dispatcher-affine
  refresh joining, revision changes and retry decisions without owning a player,
  queue, or UI. Inject active-output eligibility, existing preparation, guarded
  readiness, intent ownership and snapshot checks. Window adapters retain
  position/pause capture and prepared-player identity validation. Each run keeps
  independent revision and completion state; old completion/failure cannot clear
  newer pending work. Window-free tests control readiness gates and cover
  joining, retries, supersession, errors and recovery.
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

- Queue replacement captures queue generation alongside playback intent and
  links both cancellation tokens during lookup. Clear must reject late metadata
  and queue publication even when ordinary output remains active. Mobile
  playlist Play links its queue cancellation token before loading. Append/Next
  and playlist append recheck queue generation after gapless readiness;
  destructive supersession cancels MCP acknowledgement or returns false to
  Mobile. Ordinary starts preserve already accepted independent edits.

- QueueEditOwnership owns playback-independent queue generation/cancellation for
  mobile move/remove acknowledgements as well as asynchronous insertions.
  Capture before mutation, reject shutdown, and recheck after gapless readiness.
  Suppress obsolete preparation errors after Clear/replacement/shutdown while
  preserving current failures and accepted edits across ordinary start/Stop.
  Wholesale clears invalidate it; replacement owns intent before lookup and
  shutdown closes it. MCP append/next forward original references to
  InsertPlaybackQueueAsync, returning resolved paths or null. Mobile
  track/playlist append guards registration and handoff. Old edits cannot
  populate a replaced/cleared queue; ordinary start/Stop preserves them.
  Independent lookup owns no device and does not delay full output release.

- Mobile/MCP play and queue replacement use readiness; mobile track and
  queue-index callbacks return false without their current prepared player, and
  MCP play/replace commands cancel abandoned or unavailable starts. Skip and
  initial play-toggle complete after preparation; automatic queue advancement
  still awaits its normal lifecycle. Later errors remain observed by readiness.
  `McpPlayerBridge.OnUiAsync(Func<Task>, CancellationToken)` unwraps
  asynchronous actions and propagates their failures, checking cancellation
  before dispatch. This does not add request-token cancellation of an accepted
  playback session.
- Radio and podcast actions use the same readiness boundary and reject obsolete
  preparation error handlers. Later lifetime failures stay observed. Saved
  podcast resume applies only when initial position is zero; nonzero explicit
  positions retain precedence. Source/history publication follows guarded seeks.
  Stop saves episode progress only after resume preparation, preserving stored
  positions when a pending seek is abandoned. Gapless notifications check
  active-player identity on the dispatcher before advancing queue occurrences
  and closing/opening their history records.

- Radio metadata monitoring captures start intent, player, station and token;
  dispatcher publication checks all of them for successful and failed probes.
  Cross-device resume lookup starts after current source metadata assignment,
  captures player/row/start ownership and a request version, and passes linked
  playback cancellation to the server. Old same-track responses cannot restore a
  prompt. Stop and gapless transitions clear ownership and cancel lookup.
  Accepted resume seeks retain a preparation lease until completion, observe
  failures without stopping playback and guard subsequent UI messages. Full
  release waits for admitted seeks; advisory lookup is cancelled but does not
  delay device release.

- `MainWindow.RefreshActiveGaplessQueueAsync` recreates the current source at
  the captured audible position and passes paused state into readiness. It
  returns after preparation; never pause whichever player is active after a
  whole-track await. Pending independent append/next lookups remain a separate
  queue-edit ownership concern.

- `Orynivo/Audio/PlaybackPreparationTracker.cs`: dispatcher-owned cancellation
  and completion leases for lookup, creation and initial/resume seek. They end
  before waiting for track completion. Cancellation never removes an unresolved
  factory; late player cleanup must be registered before ending preparation.
  Complete output release cancels pending preparation, waits for its snapshot,
  then drains retirement again. Transport stop detaches immediately and
  registers background disposal. Normal window close keeps the dispatcher alive
  and blocks new starts until preparation and disposal finish, then closes once.
  Release completion checks its generation before changing lock/status state.
  Update installation and library import await the shared
  `MainWindow.PreparePlaybackShutdownAsync` drain before direct lifetime
  shutdown.
- `Orynivo/Audio/AirPlayDeviceDiscovery.cs` and `AirPlayAudioPlayer.cs`:
  cross-platform classic AirPlay/RAOP output. Profiles retain the stable DNS-SD
  service name and last resolved endpoint, discovery refreshes it before
  playback, and FFmpeg-decoded 44.1 kHz stereo PCM is piped to a separately
  installed `raop_play` helper. Do not persist receiver passwords or pairing
  material. This helper is the single-receiver AirPlay 1 fallback. The bundled
  Windows native bridge provides a separate experimental AirPlay 2 route; do not
  claim general protected-receiver or multiroom support.
- `Native/AirPlay2Bridge/`: independent Qt-free AirPlay 2 sender with a
  versioned C ABI and opaque sessions. Windows bundles it when available;
  `AirPlayAudioPlayer` prefers it over the classic RAOP helper. Transient HAP
  mode-4 pairing must verify the receiver proof and never log key material.
  Authenticated control framing uses a two-byte little-endian length, at most
  1024 plaintext bytes, and independent directional counters. The verified Sonos
  path uses unicast gPTP, encrypted ALAC realtime RTP, bounded receiver-
  requested retransmission, RTP-anchored metadata, authenticated receiver
  controls, and best-effort teardown. PCM delivery follows the negotiated sample
  clock without an additional fast sender prefill. Read `Native/AGENTS.md` for
  the detailed protocol invariants. Other receivers remain experimental; see
  `README.md` for supported behavior and limitations.
- `Orynivo/OutputProfileDialog.axaml/.cs`: dialog for creating or editing an
  output profile; loads available devices asynchronously, validates unique
  names, and exposes the confirmed result via `Result`
- `Orynivo/Audio/SteinbergAsioStream.cs`: runtime-selecting C# wrapper for
  `AsioBridge.dll` and `CwAsioBridge.dll`
- `Orynivo/Audio/FfmpegAudioPlayer.cs`: PCM path
- `Orynivo.Core/Audio/FfmpegPcmDecoder.cs`: FFmpeg PCM decoder process with an
  initial prefetched block used for gapless transitions. For `http(s)` inputs it
  caps FFmpeg's stream analysis (`-analyzeduration`/`-probesize`) and adds
  reconnect options so the first decode of a remote/HTTP track does not block on
  FFmpeg's default 5 s probe window. The PCM probe paths
  (`FfmpegAudioPlayer.ProbeAsync`, `WasapiAudioPlayer.ProbeAsync`) apply the
  same capped `ffprobe` settings for HTTP inputs. When a caller already knows
  the stream characteristics it can skip the probe entirely: a
  `GaplessPlaybackItem.KnownInfo` (`KnownAudioInfo`) lets both players build the
  `AudioFileInfo` from cached metadata instead of running `ffprobe`.
  `MainWindow` supplies it for remote Orynivo Server PCM tracks from the
  server-reported sample rate/channels/format (via `BuildRemotePcmKnownInfo`),
  removing one HTTP round-trip on the initial start and on gapless prefetch; DSD
  sources and tracks without a reported sample rate leave `KnownInfo` unset and
  probe as before. Seeking a remote Orynivo Server stream (URL contains
  `/api/stream/`) uses **server-side seek**: the decoder appends `?ss=<seconds>`
  and decodes the offset stream from position 0 instead of seeking the HTTP
  stream itself (which binary-searches seektable-less files over many range
  round-trips). Plex and other HTTP inputs keep client-side `-ss` seeking.
- `Orynivo.Core/Audio/FfmpegLocator.cs`: checks `AppContext.BaseDirectory`,
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
  `FfmpegLocator.IsAvailable()` is the shared platform-aware availability check
  used by Settings; do not duplicate Windows executable names in UI code.
- `Orynivo/Audio/DsfAudioPlayer.cs`: native DSF-to-DSD path
- `Orynivo/Compatibility/Linux/DsfDopAudioPlayer.cs`: local stereo DSF-to-DoP
  path for direct ALSA. It preserves the DSF payload, alternates `0x05`/`0xFA`
  marker bytes, derives the PCM carrier as DSD rate divided by 16, and bypasses
  volume, ReplayGain, boost, and equalizer processing. Its ALSA `S32_LE` frame
  order is low padding byte, two DSD bytes, then the marker in the most-
  significant byte. Direct ALSA DSF startup first tries the hardware-advertised
  `DSD_U32_BE` format at DSD-rate/32 and falls back to `S32_LE` DoP at
  DSD-rate/16.
- `Orynivo/Compatibility/Linux/RemoteDsfDopAudioPlayer.cs`: matching Linux DoP
  path for authenticated HTTP-range DSF streams. It incrementally reads the
  remote planar DSF blocks and applies the same direct-ALSA marker framing.
- `Orynivo/Audio/RemoteDsfAudioPlayer.cs`: native DSF-to-DSD path for
  authenticated Orynivo Server streams. It reads DSF headers and audio blocks
  through HTTP byte ranges and feeds the existing ASIO/cwASIO DSD stream without
  downloading the complete file first. Transport text may claim native DSD only
  when this player or the local native DSD players are actually active.
- `Orynivo/Audio/DffAudioPlayer.cs`: native DFF/DSDIFF-to-DSD path
- `Orynivo/Audio/RemoteDffAudioPlayer.cs`: native uncompressed DFF/DSDIFF-to-DSD
  path for authenticated Orynivo Server streams. It parses the remote `FRM8`,
  `PROP`, and `DSD` chunks through HTTP byte ranges, rejects DST-compressed DFF,
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
- `Orynivo/LibraryCatalogProviders.cs`: shared catalog abstraction for local and
  remote Orynivo libraries. `ILibraryCatalogProvider` exposes common artist,
  album, track, search, and album-artwork operations;
  `LocalLibraryCatalogProvider` maps `AudioDatabase` rows, and
  `OrynivoServerLibraryCatalogProvider` maps `OrynivoServerClient` responses
  into the same UI-facing models so library masks can be reused instead of
  branching on local vs. server rows.
- `Orynivo/NowPlayingMetadataProviders.cs`: reusable abstraction for the
  transport lyrics, artist-info, and cover views. `INowPlayingMetadataProvider`
  exposes `GetCachedLyricsAsync`/`DownloadLyricsAsync`, `GetArtistProfileAsync`,
  and `GetArtworkAsync` (the transport cover + lyrics background, returned as
  local file paths; the remote implementation downloads the server track artwork
  into the local remote-artwork cache); `LocalNowPlayingMetadataProvider`
  reads/writes the local SQLite library, and
  `OrynivoServerNowPlayingMetadataProvider` reads/writes a remote server's
  lyrics and artist-profile caches. The client always performs the external
  LRCLIB/Wikipedia/Last.fm fetch and uploads the result; the server only caches
  it. `MainWindow` selects the provider for the currently playing track
  (`_currentNowPlayingProvider`) so lyrics and artist info work identically for
  local and remote tracks. For a remote track the now-playing artist button
  navigates within that track's server library (`OpenOrynivoArtistAlbumsAsync`
  using the row's `OrynivoServer` and `ArtistId`), not the local album view.
- `Orynivo/MainWindow.*.cs`: `MainWindow` remains one Avalonia partial class,
  split into domain-sized partials without changing ownership or runtime
  behavior. `MainWindow.xaml.cs` is intentionally reduced to shared state:
  fields, nested types/records, and the constructor. Domain partials (all under
  `Orynivo/`): `MainWindow.Dashboard.cs`, `MainWindow.History.cs`,
  `MainWindow.Radio.cs`, `MainWindow.Podcasts.cs`, `MainWindow.Playlists.cs`,
  `MainWindow.GenreCloud.cs`, `MainWindow.InfiniteMix.cs`,
  `MainWindow.Similarity.cs`, `MainWindow.AirPlay.cs`,
  `MainWindow.MobileRemote.cs`, `MainWindow.MusicBrainz.cs`,
  `MainWindow.MetadataRepair.cs`, `MainWindow.ArtworkSynchronization.cs`,
  `MainWindow.LibraryViewCache.cs`, `MainWindow.Queue.cs`,
  `MainWindow.AlphabetIndex.cs`, `MainWindow.Artwork.cs`,
  `MainWindow.Search.cs`, `MainWindow.TrackFilters.cs`,
  `MainWindow.Settings.cs`, `MainWindow.FolderTree.cs`,
  `MainWindow.AlbumDetail.cs`, `MainWindow.ArtistInfo.cs`,
  `MainWindow.Sidebar.cs`, `MainWindow.Navigation.cs`, `MainWindow.Playback.cs`,
  `MainWindow.CoverSearch.cs`, `MainWindow.Favorites.cs`, `MainWindow.Plex.cs`,
  `MainWindow.RemoteOrynivo.cs`, `MainWindow.LibraryViews.cs`,
  `MainWindow.TableRendering.cs`, `MainWindow.Startup.cs`,
  `MainWindow.ContentLoading.cs`, `MainWindow.EntityFavorites.cs`,
  `MainWindow.NavigationLinks.cs`, `MainWindow.OrynivoNavigation.cs`,
  `MainWindow.ContextMenus.cs`, `MainWindow.Helpers.cs`,
  `MainWindow.RatingColumns.cs`, `MainWindow.ArtistAlbums.cs`, and
  `MainWindow.AppShell.cs`. The largest domains are themselves split into
  sub-partials: `MainWindow.Dashboard.{Recommendations,Media,Stats}.cs`,
  `MainWindow.PlaybackState.cs` and `MainWindow.Transport.cs`,
  `MainWindow.ArtistInfo.{Rename,Albums,Profile}.cs`, and
  `MainWindow.Playlists.DragDrop.cs`. Keep generic visual helpers
  (`FindResource`, `ResolveFontSize`, `FindAncestor`, `FindVisualChild`,
  `FindVisualChildren`) and the shared table-column factories
  (`CreateFavoriteColumn`, `CreateSourceBadgeColumn`, `CreateEntityLinkColumn`,
  `GetContentRowSortMemberPath`) in dedicated helper/rendering partials rather
  than a single domain. Pure, UI-free logic must live in a standalone testable
  type instead (for example `Orynivo.Controls.ArtworkAccentColor` or
  `Orynivo.Controls.ListeningTrendGeometry`), covered by `Orynivo.Tests`.
- `Orynivo/Audio/WindowsEndpointVolumeSynchronizer.cs`: bidirectional
  synchronization between the transport volume slider and the selected Windows
  render endpoint's master volume
- `Orynivo.Core/Audio/ReplayGain.cs` and `ReplayGainMode.cs`: parse persisted
  track/album gain values, select the configured fallback mode, and calculate
  the linear PCM gain factor
- `Orynivo.Core/Audio/EqualizerApoParser.cs`: imports Equalizer APO and AutoEQ
  preamp, parametric filter, and `GraphicEQ` text profiles. Recognized malformed
  directives, unsupported filter parameters and non-finite values reject the
  complete import. Explicit zero-gain profiles are valid. Files are limited to
  four MiB and profiles to 512 active filters. Parametric imports accept `Fc`,
  `Gain` and `Q`; `BW Oct` is rejected rather than silently using a default Q.
- `Orynivo.Core/Audio/ParametricEqualizer.cs`: stereo biquad PCM equalizer with
  a short crossfade when the active profile changes and filter-state reset after
  seeks
- `Orynivo.Core/Audio/CrossfeedProcessor.cs`: optional headphone crossfeed
  applied after ReplayGain and the equalizer in the ASIO and WASAPI PCM paths,
  wired through `ICrossfeedAudioPlayer`. It is off by default, must keep
  correlated (mono) content centered, must not change native DSD output, and
  must reset its filter history after a seek
- `Orynivo.Core/Audio/StreamingLoudnessNormalizer.cs`: optional slow, bounded
  loudness matching applied last in the ASIO and WASAPI PCM paths, wired through
  `ILoudnessNormalizerAudioPlayer`. It is off by default, runs **only** for
  radio and podcast streams (never library tracks, which use ReplayGain, or
  native DSD), must not pump on short passages, and must reset after a seek
- `Orynivo/Controls/EqualizerResponseControl.cs`: logarithmic frequency-response
  graph for the editable parametric equalizer profile in Settings, including a
  20 Hz–20 kHz scale and numbered dashed markers that map filter frequencies to
  dynamic editor rows. Marker bubbles sit below the scale; colliding numbers
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
- Successfully loaded empty Artists/Albums/Tracks views use a separate
  `LibraryEmptyNotice` in the existing header notice row. Publication requires
  complete source outcomes, zero displayed rows, configured sources and the
  original reload/detail exclusion guard. Its localized text refers to this view
  and suggests filters/source setup, not a definitive empty catalog. Settings
  routes server-only profiles to OrynivoServers and others to Library.
  Navigation clearing hides both empty and failed-load notices. Empty guidance
  also retains the coordinator Request that published its result. Version,
  generation and cancellation must remain current; new load admission and
  catalog invalidation refresh visibility. Watcher changes do this in their
  existing coalesced dispatcher pass, never on the worker.

- The empty-library sidebar hint offers localized Directories, Orynivo servers
  and Output device actions through `MainWindow.Settings.cs`. They open the
  existing Library, OrynivoServers and AudioDevice sections of embedded
  Settings. The output entry shares the transport Settings action. The row has
  no height cap or transition; both captions wrap and provide explicit
  accessible names. Existing source visibility rules hide the hint when a local
  folder or Orynivo Server is configured. The navigation ListBox uses Continue
  tab traversal so all nested setup buttons are reachable with Tab/Shift+Tab and
  activate through normal Enter handling rather than being skipped by the
  default single-entry tab policy. The setupAction class adds focus-visible
  accent border/fill in the existing header/Settings button templates. It
  changes no geometry and adds no motion; the empty-view Settings and
  output-profile actions share that marker. setupAction suppresses the duplicate
  default focus adorner. Small sidebar and folder-scan hints use secondary text.
  Headless checks simulate 100/200% scaling and assert opaque text/focus
  contrast; physical display evidence is separate. Fresh transport disables Play
  until it knows a source, and empty Stop/failed restoration preserve that state
  without changing output lifetime.
- Both live Settings root changes and accepted Settings use
  `SetConfiguredLibraryPaths` in `MainWindow.Settings.cs`. It copies the roots,
  expands Library when the first local root is added, and retains collapsed
  state on later changes. Watcher and cleanup receive that copied root set.
- Settings folder-picker admission adds a new row, publishes the copied root
  list, then awaits that row's ordinary scan action. Automatic and manual scans
  share progress, cancellation, error handling, ReplayGain preference and
  catalog invalidation. Existing folders are not scanned just by opening
  Settings; duplicate selections are ignored. Adding/removing another folder
  preserves active rows. Removed scans retain ownership until cancellation
  completes. DirectoryScanner defaults to LibraryScanner.ScanAsync and permits
  synthetic transport substitution in the isolated workflow fixture.
- Settings output-profile setup uses a full-width ComboBox above a WrapPanel of
  the existing create/configure/delete actions. Their captions wrap with bounded
  widths and flexible heights; selection and callbacks are unchanged.

- `Orynivo/Controls/StatusBadge.cs`: small pill indicator (`TemplatedControl`,
  default template in `App.axaml`) with a coloured dot and short label, driven
  by `Text` and a `State` (`StatusBadgeState.Ok`/`Warning`/`Off`). Settings uses
  it to surface FFmpeg, Steinberg ASIO, cwASIO, and MCP server availability;
  `SettingsView` populates the badges in `UpdateSubsystemStatusBadges()` /
  `UpdateMcpStatusBadge()`. Each Orynivo Server and Plex server row also carries
  a live connection badge probed asynchronously (`CheckServerStatusAsync` +
  `ProbeOrynivoServerAsync`/`ProbePlexServerAsync`), so opening Settings never
  blocks on network calls; pending probes are cancelled per-list on rebuild and
  in `Deactivate()`. When a probe succeeds the timestamp is persisted through
  `ServerConnectionStore`; when it fails the row badge shows "Unreachable" and a
  muted detail line reports the last successful connection (or "Never
  connected").
- `Orynivo/ServerConnectionStore.cs`: standalone JSON store
  (`%LOCALAPPDATA%\Orynivo\server-status.json`) mapping a server ID to the Unix
  timestamp of its last successful connection. Kept separate from
  `settings.json` so recording a connection never contends with the
  settings-save flow.
- `Orynivo/RemoteServerCache.cs`: single source of truth for the remote-server
  caches (`remote-artworks`, `remote-track-cache`, `remote-folder-cache`).
  Exposes the per-server track-list/folder-track cache file paths (used by
  `MainWindow`), `GetTotalSizeBytes()`, `ClearAll()`, and `ClearServer(server)`
  (clears the server's hashed track/folder caches plus its `track-art-<id>-*` /
  `artist-info-<id>-*` artwork). Settings shows the total size and offers a
  per-server "Clear cache" button and a clear-all button.
- Remote server compatibility: `OrynivoServerClient.GetCapabilitiesAsync` probes
  whether the newer endpoints exist (`/api/tracks/facets`, `/api/albums/recent`,
  `/api/tracks/{id}/waveform`) by sending an unsupported method and reading
  `405` (route exists) vs `404` (missing) — every server reports the same
  `ApiVersion`, so routes are probed directly. `SettingsView`
  (`CheckServerCapabilitiesAsync`) shows a per-row "Server does not support: …"
  line for missing features.
- Remote scan progress: `MainWindow` polls each configured server's `/api/scan`
  (`_remoteScanPollTimer`, `PollRemoteServerScansAsync`) and shows an
  in-progress scan in the shared sidebar activity line
  (`UpdateLibraryActivityIndicator`, which prefers the local `_localScanText`
  over the remote `_remoteScanText`), never reloading or blocking the current
  view. Settings additionally exposes separate per-server **Scan library** and
  **Calculate ReplayGain** row actions. Both trigger only their owning server
  and display operation-specific progress in the row until completion.
- `Orynivo/EqualizerProfileNameDialog.*`: themed unique-name dialog used when
  creating a new persisted equalizer profile
- `Native/AsioBridge/bridge.cpp`: shared Steinberg/cwASIO initialization,
  PCM/DSD ring buffers, and callback
- `Native/CwAsioBridge/CwAsioBridge.vcxproj`: builds the shared bridge against
  vendored cwASIO
- `third_party/cwasio/`: pinned MIT-licensed cwASIO host and compatibility
  sources
- `Orynivo/SettingsView.*`: two-column settings view embedded in the main
  content area, with navigation on the left and the selected section on the
  right; the output-profile dropdown shows a compact `Backend  ·  Device`
  summary line (`OutputProfileSummaryTextBlock`) beneath it when a profile is
  selected; `NavigateToSection(tag)` and `ScrollToEqualizerSection()` allow the
  transport quick-pick buttons to jump directly into a settings section; the
  **Integration** navigation group contains the **MCP SERVER** section
  (`Tag="Mcp"`) with an enable checkbox, configurable port field, and per-tool
  enable/disable checkboxes for all 37 tools (stored in
  `AppSettings.DisabledMcpTools`); `NavigateToSection("Mcp")` jumps there; the
  tool `UniformGrid` has `Rows="19"` for 37 tools (2 columns). The MCP section
  also holds the **Web browsing** configuration (enable toggle, SearXNG URL,
  block-private-networks toggle, and timeout/response-size/result limits) edited
  via `WebBrowsingValue` Remote Orynivo Server connection management belongs
  under its own **Orynivo Server** navigation item in the **BIBLIOTHEK**
  settings group, not under local directories or Streaming.
- `Orynivo/Mcp/McpPlayerBridge.cs`: thread-safe bridge between the MCP layer and
  Avalonia's UI thread; `MainWindow` populates all delegate properties at
  startup; `OnUiAsync` dispatches to `Dispatcher.UIThread` at
  `DispatcherPriority.Normal`; records `PlayerState` and `QueueEntry` are
  returned by the state and queue delegates; `DisabledTools`
  (`HashSet<string>?`) and `IsToolEnabled(name)` control per-tool gating;
  `RefreshPlaylistsFunc` triggers `LoadNavPlaylists` after MCP creates a
  playlist; `WebBrowsing` (`Orynivo.Web.WebBrowsingService`) backs the web
  tools; `GetOrynivoServersFunc` returns the configured remote Orynivo Servers
  and `ResolveRemoteTrackFunc` turns an `orynivo://serverId/track/trackId`
  reference into the real playable stream URL (registering the track's metadata
  in `_orynivoTracksByUrl`), so MCP and AI-chat playback of remote tracks work
  without exposing the server API key
- `Orynivo.Core/Web/WebBrowsingService.cs` (+ `WebBrowsingOptions`,
  `HtmlContentExtractor`): the controlled internet layer used by both MCP and
  the AI chat. `SearchAsync` queries the configured SearXNG JSON API (trusted
  endpoint, not SSRF-guarded, so a LAN/Docker instance works after the user
  configures its URL); `FetchTextAsync`/`FetchMarkdownAsync` fetch arbitrary
  pages behind a strong SSRF guard: http/https only, a
  `SocketsHttpHandler.ConnectCallback` refuses
  private/loopback/link-local/reserved IPs at connect time (closing the
  DNS-rebinding window), manual redirect following with a limit, response
  size-cap, non-text content refused (no arbitrary downloads), per-request
  timeout, optional domain allowlist, and per-request audit logging to
  `%LOCALAPPDATA%\Orynivo\logs\web-browsing.log`. Configured through
  `AppSettings.WebBrowsing` (`WebBrowsingOptions`); `MainWindow` creates the
  service, wires the logger, and updates `Options` on settings save.
- `Orynivo/Mcp/McpTools.cs`: 37 MCP tools annotated with `[McpServerToolType]`
  and `[McpServerTool]`; read-only tools are marked
  `ReadOnly = true, Idempotent = true`; every tool guards with
  `bridge.IsToolEnabled(name)` and returns `"Tool is disabled."` when off;
  `get_current_time` returns the current local/UTC date, time, day of week, and
  time-zone name; the web tools `search_web`, `fetch_page`, and
  `fetch_page_as_markdown` delegate to `bridge.WebBrowsing` (a
  `Orynivo.Web.WebBrowsingService`); `search_library` calls
  `TrackSearchIndex.SearchByCategory` then `AudioDatabase.OpenDefault()` for id
  resolution, and additionally queries every configured remote Orynivo Server
  through `OrynivoServerClient.SearchFullAsync`
  (`AppendRemoteSearchResultsAsync`), emitting remote tracks as opaque
  `orynivo://serverId/track/trackId` references (`BuildOrynivoTrackReference`)
  rather than credential-bearing stream URLs;
  `play`/`queue_append`/`queue_play_next`/`replace_queue` first pass each path
  through `ResolvePlayablePathAsync` (→ `bridge.ResolveRemoteTrackFunc`) so a
  remote reference resolves to a real registered stream URL while local paths
  and real URLs pass through unchanged; `RedactKey` masks any `key=` query value
  in tool output (`get_queue`, `get_now_playing`) so API keys never reach the
  model; property access uses `AlbumInfo.Album`, `AlbumInfo.DisplayArtist`, and
  `ArtistInfo.Artist`; playlist tools use `GetAllPlaylists`, `GetPlaylistById`,
  `GetPlaylistTracks`, `CreatePlaylist`, and `CreateSmartPlaylist`;
  `get_play_history` uses `GetHistoryForDay` for a specific date and
  `GetRecentHistory` for the N most recent entries; `create_smart_playlist`
  accepts individual filter parameters and serialises them to
  `SmartPlaylistCriteria` JSON; `clear_queue` empties the queue without stopping
  the current track; `replace_queue` atomically replaces the queue and starts
  playback of the first new track
- `Orynivo/Mcp/McpServerService.cs`: starts/stops an embedded Kestrel HTTP/SSE
  server using `WebApplication`; binds to `http://localhost:{port}` by default
  or `http://0.0.0.0:{port}` only when `McpNetworkAccessEnabled` is explicitly
  enabled; network mode requires `Authorization: Bearer <McpAccessToken>` and
  compares the generated 256-bit token in constant time; maps MCP at `/mcp`;
  logging is cleared so Kestrel output does not appear in the player console;
  `IAsyncDisposable` — `DisposeAsync` delegates to `StopAsync`
- `Orynivo/AI/AiChatSettings.cs`: persisted configuration for the embedded AI
  chat (endpoint URL, optional API key, model name, max tokens); stored as
  `AppSettings.AiChat`
- `Orynivo/AI/AiEndpointService.cs`: validates credential-free absolute HTTP/S
  base URLs and queries the OpenAI-compatible `/models` endpoint with the
  optional bearer token. It accepts standard `data[].id` and Ollama-compatible
  `models[].name`/`model` response shapes and never exposes response bodies,
  endpoint URLs, or credentials through Settings errors.
- `Orynivo/AI/AiChatService.cs`: HTTP client for OpenAI-compatible
  `/v1/chat/completions` endpoints using streaming SSE; internal tool-call loop
  accumulates streamed function arguments, executes tools via `AiToolExecutor`,
  and continues until the model emits `stop`; returns
  `IAsyncEnumerable<AiStreamEvent>` (token, tool-call, error, done events);
  conversation history is maintained across turns; `AiChatSettings` is read on
  every send so settings changes take effect without restarting the view
- The AI Chat settings section automatically refreshes advertised models when
  opened or after URL/key edits lose focus. It also provides explicit model
  refresh and connection-test actions. The persisted free-form model name stays
  independent of the transient list so compatibility endpoints without a model
  catalog remain usable.
- `Orynivo/AI/AiToolDefinitions.cs`: builds the OpenAI function-calling schema
  (`JsonObject` list) for all 37 Orynivo tools; definitions match the method
  signatures in `McpTools.cs`
- `Orynivo/AI/AiToolExecutor.cs`: dispatches tool calls received from the LLM to
  `McpTools` methods by name; parses JSON arguments from the model; no MCP
  transport involved — tools are invoked directly against the bridge and
  database
- The shared `search_library` tool accepts an empty free-text query when a
  structured category, release-year range, library-added date range, or sort
  order is supplied. MCP and AI schema arguments must remain identical. Date
  filters use an inclusive local-calendar `addedFrom`/`addedTo` contract and are
  converted to a half-open Unix timestamp range. Returned tracks and albums
  include release year and library-addition date; remote playback identities
  remain opaque `orynivo://` references.
- `Orynivo/AI/AiChatView.axaml/.cs`: embedded chat UI; user bubbles right-
  aligned (accent color), assistant bubbles left-aligned (surface brush),
  tool-call status centered and muted; streams tokens into the last assistant
  message; Enter sends, Shift+Enter inserts a newline; Clear button wipes
  history and resets `AiChatService`; `SetBridge(McpPlayerBridge)` wires the
  executor; `GetSettings` delegate is read on each send. Messages are copyable:
  user bubbles use `SelectableTextBlock`, assistant bubbles render selectable
  Markdown, and each bubble has a copy button (`CopyMessage_OnClick` → the
  `TopLevel` clipboard). Auto-scroll is robust: while `_stickToBottom`, the
  message `ScrollViewer` is re-pinned to the true bottom from its
  `ScrollChanged` handler (which — unlike `LayoutUpdated` on the `ScrollViewer`,
  that does not fire when only the content's extent grows — reliably fires as
  the streamed reply grows; `LayoutUpdated` is kept as a backup).
  `_stickToBottom` is released ONLY by a real mouse-wheel gesture
  (`PointerWheelChanged` tunnel handler), never from `ScrollChanged`/offset
  changes, so the constant re-layout of the streamed Markdown cannot be misread
  as a manual scroll-up and disable auto-scroll. The view is hosted in the
  bounded content row (`Grid.Row="2"`), never spanning the Auto intro-card row,
  so its `ScrollViewer` is measured with a bounded height.
- `Orynivo/AI/MarkdownTextBlock.cs`: lightweight Markdown renderer for assistant
  messages. Block level: headings, bullet/numbered lists, block quotes,
  dividers, and fenced code blocks (rendered verbatim). Inline level
  (`AppendInlines`): `**bold**`/`__bold__`, `*italic*`/`_italic_`, `` `code` ``,
  and `[label](url)` become styled `Run`s — applied to paragraphs, list items,
  and quotes alike, so no raw `**markers**` leak through. Code blocks use
  `CreateLiteralText` so inline Markdown is not interpreted inside them.
- `Orynivo/ThemeManager.cs`: sets global Avalonia resources for light and dark
  themes
- `Orynivo/Controls/DataGridColumnWidthStore.cs`: validates, captures, and
  restores per-table pixel widths
- `Orynivo/Controls/DataGridColumnOrderStore.cs`: captures and restores
  identified data-column display order while retaining fixed-column slots
- `Orynivo/Controls/DataGridColumnChooser.cs`: opens the themed `MenuFlyout` for
  column visibility at the clicked header; entries remain open while toggling
  multiple columns
- `Orynivo/Localization/*`: language model and localized German, English,
  French, Spanish, Russian, Simplified Chinese, and Hindi strings
- `Orynivo/StartupWindow.*`: lightweight splash screen shown during initial
  database preparation and migration
- `Orynivo/Assets/Orynivo_Logo.png`: embedded full logo used by the splash
  screen and About dialog in the dark theme
- `Orynivo/Assets/logo_about_light.png`: embedded full logo used by the splash
  screen and About dialog in the light theme
- `Orynivo/Assets/logo_prog.png`: embedded program wordmark used at the top of
  the main sidebar in the dark theme
- `Orynivo/Assets/logo_prog_light.png`: embedded program wordmark used at the
  top of the main sidebar in the light theme
- `Orynivo/Assets/Orynivo.ico`: multi-resolution application and window icon
  generated from `Logo/icon.png`
- The process uses the explicit Windows AppUserModelID `Orynivo.AudioPlayer`;
  the startup window is excluded from the taskbar so the main window owns the
  taskbar identity
- `Orynivo/CrashLogger.cs`: writes unhandled UI, AppDomain, and task exception
  reports to `%LOCALAPPDATA%\Orynivo\logs\`
- `Orynivo/DailyHistoryDialog.*`: themed modal dashboard dialog that lists
  playback-history entries for a selected calendar day and returns track, album,
  or artist navigation actions to the main window. A source-filter chip row
  (`HistorySource`: Track/Radio/Podcast/Remote/Plex, classified via
  `ClassifySource`) toggles which categories are shown; only categories present
  that day get a chip. Album/artist links are available for local, remote
  Orynivo Server, and Plex entries; Plex clickability requires the stored Plex
  context (album/artist rating keys) and a still-configured Plex server
- Plex page, child and folder requests retain their own cancellation identity,
  copied connection, token, library section, sidebar identity and enclosing
  root/Back ownership. Guard rows, status, lazy expansion and metadata after
  awaits. History entry rows retain their owning server without borrowing a
  library section; base details use global Back and never clear newer Plex
  history after awaiting. Sectionless Back keeps library controls hidden. Paging
  retains admitted offset/rows; child history retains pre-load rows/title, exact
  selected row and vertical offset. Plex Back uses NavigationRestoreOwnership
  before cancellation, checks between adapters and guards deferred position with
  both restore and binding identity. Replacement navigation cannot lose its
  history entry or receive an obsolete layout callback. Child state retains
  copied source/connection, transient credential, section and navigation tag.
  Reject changed/removed sources before binding and during deferred layout;
  return through the global origin, or Tracks when no origin exists or its Plex
  source has been removed. Keep snapshots in memory and never log them. Global
  Back payloads retain published Plex detail rows, selection/position, paging
  and the existing parent states when leaving for another view. A visibility
  marker excludes stale ambient Plex fields. Restore revalidates configured
  source and encrypted credentials off the UI thread, guards the continuation
  and repopulates the existing Plex stack in original order. Invalid global
  snapshots fall back to Tracks. Only admitted restored tracks register playback
  metadata; no new stack/cache is introduced. The shared Plex table adapter
  hides competing surfaces and unrelated view/queue controls. Independent Plex
  actions release old root loading and reject old fades. Back clears loading
  text and restores root paging. Top-level cancellation detaches top/cloud/Plex
  sources before callbacks so newly admitted replacements cannot be cancelled by
  older cleanup.
- `Orynivo/SettingsStore.cs`: persists non-secret settings in
  `%LOCALAPPDATA%\Orynivo\settings.json`, overlays secrets from
  `ApplicationCredentialStore`, and migrates legacy plaintext JSON credentials
- `Orynivo/ApplicationCredentialStore.cs`: the single encrypted current-user
  credential container for Last.fm (artist info, API secret, and scrobbling
  session key), Fanart.tv, AI Chat, Orynivo Server, Plex, and generic streaming
  credentials. Windows uses current-user DPAPI; Linux and macOS use AES-GCM with
  a separate random key restricted to user read/write. Last.fm scrobbling lives
  in `Orynivo/Scrobbling/` (`LastFmScrobblingService`, `PendingScrobbleStore`);
  it must never block or fail playback, and its settings are configured under
  **Artist information**.
- `Orynivo.Core/Streaming/IStreamingCatalog.cs` and
  `IStreamingPlaybackProvider.cs`: provider-neutral contracts for future
  streaming catalog and playback integrations
- `Orynivo/Streaming/QobuzStreamingProvider.cs`: inactive Qobuz scaffold; do not
  add unofficial endpoints, enable it only with approved partner API
  documentation
- `Orynivo/Streaming/WindowsStreamingCredentialStore.cs`: compatibility facade
  that stores future provider secrets through `ApplicationCredentialStore`
- `Orynivo/PlaylistProviders.cs`: provider-neutral playlist persistence for
  local SQLite mixed playlists plus retained legacy remote-server operations.
  Current track, album, and folder context menus always target the local mixed
  playlist list; remote selections persist stable `orynivo://` references while
  their queue paths remain immediately playable authenticated URLs.
- `Orynivo.Core/Streaming/PlexServerClient.cs`: queries configured Plex Media
  Servers, exposes only music library sections (`type=artist`), pages
  artists/albums/tracks, resolves drill-down children, browses folders lazily,
  and builds authenticated direct-part URLs. `PlexMediaItem` also carries the
  optional `ParentRatingKey` (album) and `GrandparentRatingKey` (artist) so a
  played Plex track can record a stable album/artist context in playback history
- `Orynivo.Core/Streaming/OrynivoServerClient.cs`: HTTP client for a remote
  Orynivo Server; methods load artists, a single artist profile
  (`GetArtistAsync`), aggregate library totals (`GetLibrarySummaryAsync`),
  albums by artist, albums, tracks by album, all tracks, tracks by ID list
  (`GetTracksByIdsAsync`), track facet rows (`GetTrackFacetsAsync`), lightweight
  folder-tree tracks, per-track cached lyrics
  (`GetTrackLyricsAsync`/`UploadTrackLyricsAsync`), remote artist rename/merge
  (`RenameArtistAsync`), server playlists (`GetPlaylistsAsync`, which now also
  carries each smart playlist's `FilterCriteria`), playlist tracks, regular
  playlist create/append/delete, smart-playlist create/update
  (`CreateSmartPlaylistAsync`/`UpdateSmartPlaylistAsync`), and smart-playlist
  resolution with client-side favourites (`ResolveSmartPlaylistTracksAsync`),
  server library paths, server directory listings, and scan status including
  `LibraryChangedAt` for remote client cache invalidation;
  `SetLibraryPathsAsync` replaces the remote server's configured library roots;
  `TriggerScanAsync` starts a remote scan; `UploadAlbumArtworkAsync` and
  `UploadArtistImageAsync` send client-selected image bytes to the server so the
  server does not perform external artwork searches itself;
  `GetTrackWaveformAsync` downloads cached compact waveform peak data from the
  server for the transport progress view; `UpdateArtistProfileAsync` sends
  client-refreshed biography/source fields and optional image bytes for
  server-side caching, without sending the Last.fm API key; `GetStreamUrl`,
  `GetAlbumArtworkUrl`, `GetArtistArtworkUrl`, and `GetTrackArtworkUrl` are pure
  URL builders that include `?key=` so the URLs can be passed directly to FFmpeg
  or Avalonia's image loader
- The main sidebar library accordion is labelled **Library**/**Bibliothek**. Its
  shared Genre Cloud view merges compact snapshots from the local library and
  every configured Orynivo Server; remote recommendations retain their server
  context and client-side favorite state. Older servers fall back to
  `/api/tracks/facets` when `/api/genres/cloud` is unavailable. Foreground cloud
  loads own their admission token, taxonomy, catalog/source identity, enclosing
  root load and outer Back context. They reject obsolete rows and playback
  metadata after awaits; shared snapshot tasks continue independently, copy
  connections and map rows without playback registration. Only the admitted view
  registers metadata. Branch and mode actions replace root loading/fades while
  reusing shared work. Background resolution/decoding retains foreground and
  configured-mode ownership, disposing rejected images. Cloud callbacks retain
  binding identity, foreground cancellation, catalog/source identity, taxonomy
  key, navigation tag and outer Back ownership for deferred Loaded layout and
  staggered animations. Removed buttons do not animate after a mode rebuild, and
  abandoned callbacks do not arrange a replacement. Genre nodes carry both
  matching-track and distinct matching-album counts; the desktop chooses the
  count matching its current recommendation mode. Local media live under a
  collapsible **Local** child node followed by local Artists, Albums, Tracks,
  Folder structure, and a nested local Playlists group. The complete **Local**
  child node (and its rows/playlists) is hidden whenever no local library
  directory is configured (`AppSettings.LibraryPaths` empty). When neither a
  local directory nor any Orynivo Server is configured, a hint row
  (`LibraryEmptyHintItem`, `L_LibraryEmptyHint`) is shown directly under the
  Library header telling the user to add local directories or one or more
  Orynivo Servers in Settings. The hint disappears immediately when a directory
  or server is added because `ApplyLibrarySectionVisibility` re-runs on every
  settings save, navigation rebuild, and the live directory-change callback.
  Configured remote Orynivo Server entries are rendered in that same Library
  accordion, after the local media rows, as one collapsible server-name row
  followed by Artists, Albums, Tracks, and Folder structure. Legacy server
  playlists are not displayed in the current sidebar; all sources use the shared
  mixed playlist list. The local Playlists group is a child of the Local node,
  indented to align with the local Artists/Albums/Tracks/Folder rows, and its
  entries are indented one level deeper. There is no separate Orynivo Server
  sidebar accordion or separate Orynivo Server sidebar visibility setting;
  `ShowLocalLibrarySection` and `IsLocalLibrarySectionExpanded` control the
  complete Library accordion, including remote Orynivo Server rows.
  `IsLocalMediaLibraryGroupExpanded` persists the Local child group state,
  `IsPlaylistsSectionExpanded` persists the local Playlists child group state,
  `CollapsedOrynivoServerLibraryGroups` persists collapsed server child groups;
  `CollapsedOrynivoServerPlaylistGroups` is retained only for persisted settings
  compatibility. Remote Artists, Albums, and Tracks reuse the **same** local
  table/artwork column masks (`ApplyColumns("Artists"/"Albums"/"Tracks")`),
  intro card (`UpdateLibraryIntroCard`), per-entity Favorites-only toggle, and
  A-Z index as the local library. There are no separate `Orynivo*` column masks.
  Server artwork hydrates lazily as rows or artwork cards become visible;
  downloaded remote artwork is cached client-side under
  `%LOCALAPPDATA%\Orynivo\remote-artworks\`. In the shared Artists view and
  global search, local and Orynivo Server artists with the same
  `ArtistNameNormalizer.CreateComparisonKey` are represented by one
  `UnifiedArtist` row with an `L+OS` source badge. Opening that row loads and
  combines the matching artist's albums from the local library and every
  configured Orynivo Server; each album row must retain its own source IDs and
  `OrynivoServer` context so album navigation, artwork, and favorites continue
  to route correctly. The unified row uses the best available cached biography
  and image from any matching identity. New profile downloads and manual artist
  image selections are synchronized to every matching local and reachable
  Orynivo Server identity; automatic image synchronization must preserve manual
  images. Manual artist-image uploads and deletions use the same synchronized
  identity set. Opening a unified artist or logical album performs a bounded,
  best-effort artwork reconciliation: copy an existing local or remote image
  only into equivalent identities without artwork, preserve manual artist
  images, invalidate unified/provider caches after writes, and never let an
  unreachable server block navigation. Album and artist detail headers expose
  localized upload actions plus contrast-safe vector trash buttons with explicit
  delete tooltips; local and remote mutations use their owning artwork
  cache/API. Dashboard artist analytics use the same unified drill-down, Back
  navigation restores it as `UnifiedArtistAlbums`, and changing the unified
  artist favorite applies to every matching local and remote artist identity.
  Plex artists remain separate and must not be folded into this identity. Mixed
  local/server rows expose stable source keys (`local` and `server:<id>`) for
  the source column, the Tracks source facet, and source-aware smart playlists.
  The visible source column belongs directly after the favorite column in shared
  tables and playlist tables. Source tooltips must use theme-aware foreground
  and surface brushes. Large mixed local/server row sets must be composed before
  the first visible `DataGrid` bind, or through a proven virtualized/paged
  binding strategy: diagnostics showed that replacing an already-visible large
  mixed table's `ItemsSource` can stall Avalonia's UI thread during scrolling.
  Artist/album entity links and double-click navigate **within** the remote
  library via `OpenOrynivoArtistAlbumsAsync`/`OpenOrynivoAlbumTracksAsync` (the
  link handlers `ArtistLinkButton_OnClick`/`AlbumLinkButton_OnClick` dispatch by
  `EntityType` prefix `"Orynivo"`). This requires remote rows to carry IDs:
  remote track rows have `ArtistId`/`AlbumId`, remote album rows have
  `ArtistId`. Remote album drill-downs use the shared provider-backed
  album-track detail surface with the same album header/card, cover actions,
  favorite toggle, and grouped track tables as local albums; do not route server
  albums to a separate plain track-list view. When a remote album is opened from
  a remote artist's album list, the detail view must initially scope tracks to
  that artist and expose the same **Show all album tracks** checkbox used by
  local album details. Persisting remote album tracks in mixed local playlists
  uses stable `orynivo://serverId/track/trackId` references and must never store
  the credential-bearing playback URL. Smart playlists work identically for
  local and remote libraries: the remote Tracks **Save smart playlist** action
  and the sidebar **Edit smart playlist** context entry persist criteria on the
  server through `CreateSmartPlaylistAsync`/`UpdateSmartPlaylistAsync`, while
  the server resolves them live through `SmartPlaylistCriteria.Resolve`. Because
  remote favourites are client-side, the client opens a remote smart playlist
  through `ResolveSmartPlaylistTracksAsync`
  (`POST /api/playlists/{id}/resolve`), sending its favourite track IDs so a
  `FavoritesOnly` criterion matches the client's favourites; regular remote
  playlists still use `GetPlaylistTracksAsync`. Back navigation must capture
  remote album details as remote states carrying the Orynivo Server navigation
  tag. `NavigationCaptureDecision` selects typed capture routes from a scalar
  `NavigationCaptureContext`, preserving visible search/cloud precedence,
  logical album parts and provider-bound album/artist routes. The decision owns
  no rows, provider or history; the window adapts its target into the existing
  Back state and retains selection, scroll and restore work. Window-free tests
  cover source collisions and capture precedence. `NavigationRestoreDecision`
  selects eligible source-specific restore adapters from scalar saved state.
  Required detail identifiers, logical parts and unified artist names preserve
  the existing top-level fallback for incomplete states. The window retains
  payloads and loader execution; server adapters retain source validation. Tests
  cover colliding IDs and incomplete states. `NavigationRestoreOwnership` reuses
  request identity with an asynchronous call-chain context. Only current nested
  restore work suppresses history capture; independent navigation abandons the
  restore and resumes capture immediately. Obsolete contexts cannot admit
  loaders or invalidate a newer restore. New Back ownership precedes
  cancellation of prior loaders, with checks between cancellation adapters.
  Completion clears only its own executing flag and retains publication identity
  for deferred layout. Outer Back visibility and immediate/deferred selection
  require ownership. Top-level restoration additionally captures the exact
  admission token, catalog generation and tag. Remote artist restoration retains
  both overview and filtered-list tokens. Configuration, Settings and close
  abandon restore work. Window-free tests cover overlap and context isolation;
  synthetic window fixtures cover source collisions, same-tag replacement,
  independent capture, rejected new targets, nested search resets/text changes
  and abandoned Loaded callbacks without rebinding. Existing stacks, payloads
  and loader responsibilities remain. `ServerNavigationTarget` parses the
  existing server tag grammar (including Root defaults) and binds album origins
  to their owning server. Malformed or removed sources are rejected before
  remote restore changes the view or requests data. Restore adapters capture the
  configured server and check active identity, current tag and configuration
  membership after awaits and before deferred scroll/selection work; ambient
  server state cannot substitute for a rejected target.
  `NavigationLoadOwnership` supplies dispatcher-owned remote view request
  versions without another history or row model. New request/token state is
  installed before cancelling old work; reentrant callbacks cannot reclaim it.
  Top-level navigation, detail reset/push and configuration changes abandon
  remote view loads. Rows, facets, playback registration, errors, status and
  finally cleanup require current request, tag, server and catalog generation.
  Each remote request releases its counted loading contribution exactly once,
  immediately on UI cancellation or via a dispatcher post for worker callbacks.
  Completion balances the same contribution without an obsolete fade/status.
  Held fake HTTP tests cover navigation to local Tracks and same-tag overlap.
  Outer top-level calls also reuse `NavigationLoadOwnership` for counted loading
  and fade publication. Navigation/detail/configuration abandonment cancels the
  outer scope immediately. `ContentLoadingLease` releases each acquired loading
  contribution once on cancellation or completion; radio views that acquire no
  contribution must not decrement another scope. Fade guards run before changing
  opacity and in deferred render work. Overlay generations change at zero-depth
  transitions, preserving pending rendering across nested scopes. Tests exercise
  complete held top-level calls, same-tag overlap, deferred fade rejection and
  nested overlay visibility. Provider-backed album track loads now capture their
  server, copied album IDs and artist scope before awaiting. The same ownership
  helper guards request, catalog generation, active provider/album, tag and
  server. Cancellation reaches each provider call; results are checked before
  mapping or playback registration. Counted release and deferred fades use the
  shared lease and ownership guard. Held tests cover navigation/configuration
  abandonment, ambient server changes and same-album replacement. Remote album
  metadata opens also retain separate request/token ownership, cancelled with
  provider detail abandonment. Both entry routes capture server, scope, fallback
  metadata and copied IDs before awaiting, and reject conflicting row sources
  before HTTP. Copied catalog connection objects resolve by exact source ID to
  the current configured server; legitimate snapshots need not share settings
  object identity. Detail admission checks request, generation, tag, active
  server and configuration membership. Back restore captures the exact overview
  token before cancellation callbacks via `LoadTopLevelViewAsync`; the existing
  `ShowTopLevelViewAsync` wrapper stays unchanged for callers. Metadata request
  ownership also guards deferred restoration. Held tests cover same-tag overview
  and metadata replacement, scope/fallback capture and source rejection. Local
  album workers also reuse request ownership and the counted lease. Copy IDs and
  artist scope before `Task.Run`, which must not reread window fields. The token
  prevents cancelled work starting; running SQLite reads may complete but
  publication requires current request, generation, tag, album ID and local
  detail kind. Check before rows/header/rating refresh admission and gate fades
  separately. Return ownership to guard the opening caller's Back-button update.
  Held dispatcher tests cover reset, top-level navigation, configuration,
  same-album overlap and input/window ID mutation. Logical detail uses the same
  request ownership with copied source parts resolved to configured server IDs.
  Capture artist scope before awaits and stage tracks from every source before
  mapping or playback registration. Guard request, generation, tag, logical row/
  parts and source membership; the shared lease balances loading and ownership
  guards header/rating admission, fade and Back. Artwork uses linked navigation/
  timeout cancellation and ownership guards before metadata, write admission and
  row hydration. Successful writes invalidate caches in finally even if
  subsequent refresh is abandoned. A still-current owner accounts for exactly
  one own cache generation advance; external invalidation remains obsolete.
  Optional artwork adapters preserve other callers. Held tests cover
  track/metadata/write abandonment, same-album overlap and own versus external
  invalidation. Never restore a remote album detail through the local
  `ShowAlbumTracksAsync` path because server and local album IDs can collide.
  Remote artist-info views must use the bound remote row/server profile data,
  not local `AudioDatabase.GetArtistById`, because server IDs can collide with
  local artist IDs. The remote artist-info rename/merge and Wikimedia image
  actions must call the Orynivo Server API (`RenameArtistAsync`,
  `UploadArtistImageAsync`) and update the remote row/detail view rather than
  writing to the local database. Remote Tracks offers the same
  Genre/Audio-Type/Bitrate facet filters as local, reusing the client's
  `MatchesTrackFilters`/facet-popup logic fed by the server aggregation endpoint
  `/api/tracks/facets` (favorite state overridden with the client-side
  favorites); filtered results load via `/api/tracks/by-ids`
  (`ResolveOrynivoTrackRowsAsync`). Typing in the header search box while a
  remote Tracks view is active shows the shared three-section search result
  (Tracks, Albums, Artists) via `ShowOrynivoSearchResultsAsync`, backed by the
  server `/api/search/full` endpoint (`OrynivoServerClient.SearchFullAsync` /
  `OrynivoServerLibraryCatalogProvider.SearchFullAsync`), mirroring the local
  `ShowSearchResultsAsync`. Result rows carry `Orynivo*` entity types so the
  shared link columns and the `SearchAlbumsDataGrid`/`SearchArtistsDataGrid`
  double-click handlers navigate within the remote library, and remote tracks
  play directly from the result list. Clearing the search box restores the
  remote Tracks list. The three search-result sections use the same
  accent-bordered card style as the library headline/intro card for local and
  remote results. The facet rows live in `_orynivoTrackFacets` while a remote
  Tracks view is active and are cleared otherwise. Remote folder tree file nodes
  must register the same `ContentRow` metadata as remote Tracks rows before
  queuing playback. This is required so the transport shows title/artist/artwork
  instead of authenticated stream URLs and enables lyrics, artist-info,
  favorite, and mixed-playlist actions. Remote folder playlist persistence must
  convert registered server track IDs to stable `orynivo://` references in local
  SQLite, never credential-bearing stream URLs. When `/api/folders/tracks` lacks
  the newer playback metadata because an older server is connected, the client
  may batch-hydrate folder track metadata through `/api/tracks/by-ids` before
  registering the folder rows. Before loading remote folder data, clear any
  existing local folder nodes and show the localized server-loading placeholder;
  do not display stale local tree data while the remote request is in flight.
  Cache remote folder-track lists under
  `%LOCALAPPDATA%\Orynivo\remote-folder-cache\` and reuse them only while the
  server's `LibraryChangedAt` scan status timestamp matches the cached value.
  The top-level **Folder structure** view (`ShowUnifiedFolderTreeAsync`) merges
  sources into one tree: a top-level **Local** group (only when a local library
  directory is configured and has tracks) plus one group per configured Orynivo
  Server (only when it reports folder tracks), each built through
  `AddFolderRootsInto`/`BuildUnifiedFolderTree`. The sidebar item remains
  visible whenever either local media or at least one Orynivo Server is
  configured; server-only setups must not require a local library path to open
  this view. `FolderTag` carries an optional `OrynivoServerSettings? Server` so
  remote directory nodes are resolved through the in-memory `FolderTree`
  (`_folderTreesBySource[sourceKey].AllFilePathsUnder`) instead of the local
  database — this is required because folder child nodes are built lazily and
  may not be materialized. `_folderViewCts` cancels an in-flight load when the
  user navigates away. Folder nodes are materialized lazily: each directory node
  gets a placeholder child, and its real children are populated the first time
  it is expanded. Because Avalonia does not reliably re-render children swapped
  in during a node's own `Expanded` pass, the expand gesture is intercepted
  (tunnel-phase `PointerPressed` on the chevron/header + a `DoubleTapped` guard)
  so the children are inserted _before_ `IsExpanded` is set — the same pattern
  the Plex lazy folders use. Eagerly building the whole tree froze the UI for ~9
  s on a ~150k-track merged library. The server folder-track cache file is
  read/deserialized and written off the UI thread
  (`LoadOrynivoFolderTracksAsync` wraps the cache I/O in `Task.Run`). The
  per-server sidebar Folders view still uses `BuildOrynivoFolderTree`, which
  shares the `MapOrynivoFolderTrackLites` mapping so remote folder file nodes
  register the same transport metadata. The unfiltered remote Tracks list is
  loaded in pages of at most 5,000 rows, matching the server cap and is likewise
  cached under `%LOCALAPPDATA%\Orynivo\remote-track-cache\`, reused while the
  server's `LibraryChangedAt` matches the cached value. That cache stores mapped
  `LibraryCatalogTrack` rows with stable `orynivo://` playback references. Its
  filename hashes only the server ID and base URL, never the API key.
  Authenticated playback URLs are rebuilt in memory after loading; client-side
  favourites are re-applied instead of trusting cached flags. Legacy
  credential-bearing track caches are cleared during migration. While a remote
  track plays, the transport lyrics and artist-info buttons work through
  `OrynivoServerNowPlayingMetadataProvider` (see
  `Orynivo/NowPlayingMetadataProviders.cs`): lyrics and the artist
  biography/image are fetched on the client and cached on that track's server.
  The remote track's server and primary artist ID are carried on its
  `ContentRow` (`OrynivoServer`, `ArtistId`); the now-playing artist button is
  enabled for a remote track and navigates within that track's server library
  (`OpenOrynivoArtistAlbumsAsync`) rather than the local album view. Remote
  transport waveform loading first asks the server for cached peaks and falls
  back to client-side FFmpeg analysis of the authenticated stream URL when the
  server cannot analyse the format; this keeps DFF waveforms available without
  requiring the server's FFmpeg build to support every source format. The
  transport favourite (heart) button is enabled for a playing remote track and
  toggles the client-side favourite (`OrynivoServerFavorites`) for that track
  via `CurrentOrynivoFavoriteTarget`; the change is written to `settings.json`
  and reflected in any visible remote track rows. For local tracks it still
  writes `tracks.is_favorite`.
- `Orynivo/OrynivoServerDialog.axaml/.cs`: themed dialog for adding or editing a
  remote Orynivo Server (name, URL, API key, per-server streaming quality, Test
  Connection); it can load, add, remove, and save the remote server's music
  directories through the server API, start a remote scan, and show live scan
  progress; returned server record is stored in `AppSettings.OrynivoServers`
- `Orynivo/RemoteDirectoryBrowserDialog.axaml/.cs`: browses the remote server
  filesystem through `/api/files/directories?path=` and returns a server-side
  directory path for `OrynivoServerDialog`
- `AppSettings.OrynivoServers` stores configured remote server connections; API
  keys are overlaid from `ApplicationCredentialStore` and excluded from
  `settings.json`; `AppSettings.OrynivoServerFavorites` stores client-side
  favorite keys for remote artists, albums, and tracks. Legacy
  `ShowOrynivoServerSection` and `IsOrynivoServerSectionExpanded` settings may
  still exist in persisted JSON for compatibility, but the current sidebar
  renders remote servers under the main Library accordion controlled by
  `ShowLocalLibrarySection` and `IsLocalLibrarySectionExpanded`.
- `Orynivo/Streaming/WindowsPlexCredentialStore.cs`: compatibility facade that
  stores per-server Plex access tokens through `ApplicationCredentialStore`
- `AppSettings.PlexServers` stores Plex server IDs, display names, and base
  URLs; Plex tokens must not be added to `settings.json`
- `AppSettings.QobuzApplicationId` stores only the non-secret Qobuz application
  identifier; client secrets and tokens must not be added to `settings.json`
- `AppSettings.LastMainView`, `AppSettings.AlbumArtworkView`, and
  `AppSettings.ArtistArtworkView` preserve the selected main view and entity
  artwork/table modes
- `AppSettings.DashboardRecommendationStageView` defaults album recommendations
  to the tall animated cover stage and persists the Dashboard List/Stage choice
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
- `AppSettings.CalculateMissingReplayGainDuringScan` controls the optional
  FFmpeg analysis of missing track and album ReplayGain values during desktop
  manual, watcher, and reconciliation scans. It defaults to disabled for fast
  library discovery; embedded ReplayGain tags are always imported, and the
  explicit **Calculate missing ReplayGain** action remains available.
- `AppSettings.NonGaplessCrossfadeSeconds` controls the optional fade transition
  used for queue advances that are not already covered by the continuous gapless
  PCM session. A value of `0` disables it; the UI caps it to 10 seconds. The
  transition fades the current player out shortly before track end, starts the
  next queue item, and fades the new player in without trying to open a second
  exclusive ASIO/WASAPI session concurrently.
- `AppSettings.AlwaysConvertDsdToPcm` forces DSF/DFF sources through FFmpeg and
  the PCM output path even when ASIO/cwASIO native DSD is available, allowing
  volume, ReplayGain, and equalizer processing
- `AppSettings.DsdOverPcmEnabled` persists the mutually exclusive DoP routing
  preference. DoP is bit-perfect DSD encapsulation, not DSD-to-PCM conversion;
  volume, PCM boost, ReplayGain, and equalizer processing must not alter it.
- `AppSettings.PcmOutputBoostEnabled` applies an additional +6 dB linear gain to
  every PCM playback path (local, remote, Plex, radio, podcasts, and converted
  DSD). Native ASIO/cwASIO DSD remains bit-perfect and ignores the boost.
- `AppSettings.EqualizerProfiles` persists all named imported or manually edited
  Equalizer APO/AutoEQ profiles, while `SelectedEqualizerProfileName`,
  `EqualizerProfile`, and `EqualizerEnabled` identify the only selected/active
  profile and retain compatibility with the previous single-profile settings
  format. The source file path is not required after import.
- `AppSettings.DataGridColumnWidths` persists user-adjusted pixel widths per
  stable table/view key; dynamic main-content views capture their current widths
  before replacing columns
- `AppSettings.VisibleDataGridColumns` persists selectable column IDs per
  table/view key; right-clicking any table header opens the context-appropriate
  column chooser flyout
- `AppSettings.DataGridColumnOrders` persists drag-and-drop display order per
  stable table/view key; fixed artwork and action columns keep their structural
  positions
- `AppSettings.McpServerEnabled` enables the embedded MCP server on startup and
  when settings are saved; `AppSettings.McpServerPort` sets the TCP port
  (default 49200); the server binds exclusively to `localhost`;
  `AppSettings.DisabledMcpTools` persists the set of tool names whose checkboxes
  are unchecked in Settings — an empty set means all tools are active; the
  bridge reads this set on startup and after every settings save
- `AppSettings.AiChat` stores the embedded AI chat configuration
  (`AiChatSettings`): `Enabled`, `EndpointUrl` (default
  `http://localhost:1234/v1`), `ApiKey`, `ModelName`, and `MaxTokens` (default
  2048); any OpenAI-compatible provider works (LM Studio, Ollama, OpenAI, etc.);
  the API key is excluded from JSON and overlaid from
  `ApplicationCredentialStore`; the `AiChatView` re-reads `GetSettings` on every
  send so settings changes take effect immediately
- `AppSettings.ShowInternetRadioItem`, `ShowPodcastsItem`, `ShowQueueItem`, and
  `ShowAiChatItem` control the individual sidebar items for Internet Radio,
  Podcasts, **Up Next**, and AI Chat from Settings > Appearance; they default to
  visible and are independent of the accordion-section toggles
- `AppSettings.Theme` stores the `Light` or `Dark` theme
- `AppSettings.GenreCloudBackground` stores whether Genre Cloud background
  rendering is disabled or uses recommendation-ranked album covers or artist
  images; artist images remain the default
- `AppSettings.GenreCloudBackgroundOpacity` stores the persisted Genre Cloud
  tile visibility from zero to one; Settings presents it as a percentage slider
  and defaults new profiles to 50 percent
- `AppSettings.Language` stores `German`, `English`, `French`, `Spanish`,
  `Russian`, `ChineseSimplified`, or `Hindi`
- `Orynivo.Core/Library/TrackRecord.cs`: database track model containing tags
  and technical metadata
- `Orynivo.Core/Library/PlaylistRecord.cs`: playlist model including
  denormalized `TrackCount`, `IsSmartPlaylist`, and `FilterCriteria`
- `Orynivo.Core/Library/SmartPlaylistCriteria.cs`: backward-compatible
  serialized smart-playlist criteria covering favourites, a free-text
  `SearchText` (matched case-insensitively against title, artist, and album),
  genres, formats, bitrates, source keys, metadata ranges, library/play-history
  rules, ordering, and result limits
- `Orynivo/SmartPlaylistDialog.*`: localized editor for the name and advanced
  criteria of an existing smart playlist. An optional `CountResolver`
  (`Func<SmartPlaylistCriteria, CancellationToken, Task<int?>>`) drives a
  debounced live preview ("N tracks match") that re-evaluates on every criteria
  change. The preview resolves the criteria exactly how the playlist opens: a
  locally stored smart playlist uses `ResolveUnifiedSmartPlaylistCountAsync`
  (`BuildUnifiedSmartPlaylistCandidates` = local library + every configured
  server, the same candidate set as `ResolveUnifiedSmartPlaylistRows`), so smart
  playlists do not distinguish local vs server sources; the server-stored edit
  path uses a resolver backed by
  `OrynivoServerClient.ResolveSmartPlaylistCountAsync`
  (`POST /api/playlists/resolve-count`, server-side `criteria.Resolve` with the
  client's favourite IDs; older servers without the endpoint return no count →
  no preview). `TryBuildCriteriaCore` builds criteria without touching the
  validation UI so the preview can reuse it.
- `Orynivo.Core/Library/PlaylistTrackRecord.cs`: playlist entry model with
  position, optional TrackId reference, and required path
- `Orynivo.Core/Library/M3u8PlaylistService.cs`: UTF-8 M3U8 import/export with
  relative local-path resolution, relative export paths, missing-file
  preservation, HTTP/HTTPS entries, and rejection of credential-bearing URLs
- `Orynivo.Core/Library/AudioDatabase.cs`: SQLite database layer through
  `Microsoft.Data.Sqlite`; database at `%LOCALAPPDATA%\Orynivo\library.db`
- `Orynivo.Core/Library/LibraryScanner.cs`: directory scanner using TagLibSharp;
  writes through `AudioDatabase.Upsert()`, reports progress, and supports
  cancellation
- `Orynivo.Core/Library/CueSheetParser.cs`: parses UTF-8 or legacy-encoded CUE
  sheets and creates stable virtual track paths with physical source paths and
  segment boundaries
- `Orynivo.Core/Library/LibraryWatcherService.cs`: owns one recursive
  `FileSystemWatcher` per available configured library root, debounces paths for
  900 ms, applies incremental create/change/rename/delete updates, and runs a
  full reconciliation after 10 minutes and every 30 minutes thereafter
- `Orynivo.Core/Library/LibraryBackupService.cs`: versioned ZIP export/import
  for the SQLite library, artwork cache, and configured library directories;
  audio files are not included
- `Orynivo.Core/Library/LyricsService.cs`: LRCLIB client and LRC parser for
  downloaded plain or synchronized lyrics
- `Orynivo.Core/Library/RadioBrowserService.cs`: Radio Browser client with
  mirror discovery, station search, and click registration
- `Orynivo.Core/Library/RadioStationRecord.cs`: persisted personal
  internet-radio station model
- `Orynivo.Core/Library/RadioStreamMetadataService.cs`: probes live ICY metadata
  through `ffprobe`; radio playback refreshes title/artist every 15 seconds
- `Orynivo.Core/Library/PodcastService.cs`: searches the public Apple Podcasts
  catalog and resolves all playable episodes from podcast RSS/Atom feeds, newest
  first
- `Orynivo.Core/Library/CatalogFilterCache.cs`: persists radio-genre and podcast
  category/language catalogs in
  `%LOCALAPPDATA%\Orynivo\catalog-filter-cache.json`; catalog data is refreshed
  after seven days while stale data remains usable
- `Orynivo.Core/Library/PodcastRecord.cs`: podcast catalog, persisted
  subscription, and episode models
- `Orynivo/LyricsSearchWindow.*`: manual LRCLIB search with editable track and
  artist fields, candidate preview, and explicit replacement of the current
  track's downloaded lyrics cache
- `Orynivo.Core/Library/ArtistProfileService.cs`: configurable artist biography
  and image lookup (Wikipedia or Last.fm) with preferred curated Fanart.tv
  `artistthumb` artwork when an encrypted stored key or `FANART_TV_API_KEY` is
  available; static source/key properties are applied by the desktop, and images
  are cached under `%LOCALAPPDATA%\Orynivo\artist-images\`. Fanart.tv resolution
  uses embedded MusicBrainz IDs or an unambiguous exact MusicBrainz match. The
  Fanart.tv key may only be persisted in `ApplicationCredentialStore`; it must
  never be written to JSON or caches, logged, sent to an Orynivo Server, or
  exposed to a model. Wikipedia/Last.fm image content is saved through shared
  serialized stream staging rather than opening the final cache file directly.
  Transfer failure, cancellation and empty content preserve the previous file;
  supported MIME and URL-extension fallback behavior remains unchanged. This
  protects file publication, not the semantic order of older automatic requests
  against later manual selections or SQLite metadata.
- `Orynivo.Core/Library/ArtistImageSearchService.cs` and
  `Orynivo/ArtistImageSearchWindow.*`: manual artist-image search with an
  editable query; it tries Fanart.tv first when a key is configured and falls
  back to Wikimedia Commons. Selecting an image updates `artists.image_path`,
  sets `image_is_manual`, and preserves the biography source; automatic profile
  refreshes must not download over manually selected image files. The Artist
  information Settings action processes missing images from the local library
  and every configured Orynivo Server strictly sequentially, remains
  cancellable, tries Fanart.tv first only when a key is available, and then
  falls back to Wikimedia Commons. Every in-memory candidate must be explicitly
  accepted or rejected in a preview before it may be saved to its owning local
  or remote library; progress includes a provider-search-based remaining-time
  estimate. Cache saves and deletions share in-process exclusive admission per
  canonical data directory and artist ID. Unique staging is cleaned on failed
  publication or cancellation; old formats are removed while ownership is held.
  `DeleteImageAsync` supports cancellable admission; legacy deletion remains
  compatible. Idle gates are removed. This file contract does not provide
  cross-process ordering, user-intent ordering or a SQLite/file transaction.
- `Orynivo/EditArtistNameDialog.*` and `Orynivo/ArtistMergeDialog.*`:
  artist-info rename flow; collisions require an explicit merge-profile priority
  choice

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
- `tracks.user_rating` stores the independent personal zero-to-five-star value;
  cached MusicBrainz community rating, vote count, fetch time, and recording
  MBID remain separate. Client-side MusicBrainz lookup prefers embedded MBIDs
  and only persists a text fallback when exact artist/title plus optional
  duration matching leaves one recording. Local and server scans preserve these
  values, and the server only caches client-resolved external metadata. Album
  detail views refresh missing or older-than-30-days track ratings in the
  background and cancel the series when another album replaces it. Fallback
  resolution persists a newly identified MBID, but community ratings must use
  direct recording lookups because MusicBrainz search responses do not reliably
  include them. Never mark a search-only empty rating as freshly fetched. Rows
  sharing a recording MBID within one album refresh must reuse one direct lookup
  result and persist it independently to each owning library. Direct rating
  lookups also cache curated MusicBrainz genres with positive counts and tags
  with at least two positive votes as separate JSON arrays. `GetTrackFacets`
  combines them with embedded genre text for classification; embedded tags are
  never overwritten. After playback first starts, one client-side low-priority
  worker continues stale MusicBrainz enrichment for local and configured Orynivo
  Server tracks only while playback is active. Album-detail and explicit
  requests take priority between API calls. Known MBIDs refresh after 30 days;
  conservative text matching that resolves no unique recording stores only its
  attempt timestamp and waits 90 days before retrying. This timestamp is not a
  cached search-response rating and scans must preserve it.
- `albums` contains stable album IDs (`id`, `title`, `artist_id`, `year`,
  `artwork_id`, `is_favorite`)
- `artworks` deduplicates artwork by SHA-256 hash; originals and thumbnails live
  under `%LOCALAPPDATA%\Orynivo\artworks\` as `original`, `thumb_96`, and
  `thumb_320`
- `favorites` is an older generic extension point; visible favorites use the
  direct flags
- `play_history` records local tracks, remote Orynivo Server and Plex tracks,
  podcast episodes, and internet-radio sessions with media type, display
  title/subtitle, optional external ID, an optional `genre` captured at playback
  time (so genre statistics include tracks without a local library row),
  playback start/end, duration, final position, and completion state. The
  `external_id` stores a stable source context for non-local tracks:
  `orynivo:{serverId}:track:{trackId}` for remote Orynivo Server tracks
  (`ResolveNowPlayingExternalId` / `BuildOrynivoHistoryExternalId`), and
  `plex:{serverId}:{trackKey}:{albumKey}:{artistKey}` for Plex tracks
  (`BuildPlexHistoryExternalId`, using the Plex track/parent/grandparent rating
  keys carried on the `ContentRow` as `PlexServerId`/`PlexAlbumRatingKey`/
  `PlexArtistRatingKey`). This is what makes Plex history albums/artists
  clickable (`TryGetPlexHistoryTarget` → `OpenPlexAlbumFromHistoryAsync` /
  `OpenPlexArtistFromHistoryAsync`, which synthesize a Plex parent row and reuse
  `ShowPlexChildrenAsync`)
- `radio_stations` stores personal Radio Browser stations by stable station
  UUID, including stream URL, logo, country, codec, bitrate, and tags
- `podcasts` stores pinned podcasts by Apple collection ID, including author,
  RSS feed URL, artwork URL, and genre
- `podcast_episode_progress` stores resume position, known duration, completion
  state, and update time per pinned podcast episode; RSS GUID is the preferred
  episode key and the audio URL is the fallback
- `AudioDatabase.GetTrackIdAndFavorite(path)` performs a lightweight `id` and
  `is_favorite` lookup
- `AudioDatabase.OpenDefault()` creates or opens
  `%LOCALAPPDATA%\Orynivo\library.db`
- On first launch after the rename, missing data is copied from
  `%LOCALAPPDATA%\Player\` and cached database paths are rebased to
  `%LOCALAPPDATA%\Orynivo\`
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
- Background watcher activity is reported to the UI: `LibraryWatcherService`
  takes an optional `Action<LibraryScanActivity>` and raises it when an
  incremental batch or a full reconciliation starts, progresses (per-file), and
  ends. `MainWindow` (`OnLibraryScanActivity`) shows a subtle, throttled sidebar
  indicator (`LibraryActivityPanel` / `LibraryActivityTextBlock`, e.g. "Updating
  library… N / M files") and never reloads a view for it.
- A background library change (`OnWatchedLibraryChanged`) must NOT auto-reload
  the visible content view. It refreshes the sidebar playlist list and, for a
  view that `CanReloadCurrentViewAfterLibraryChange()` allows, shows the header
  `LibraryRefreshButton` ("New library data available" / "Refresh"). The user
  reloads on demand (`LibraryRefreshButton_OnClick`); the button is hidden again
  on any explicit navigation (`ShowTopLevelViewAsync`). No automatic navigation
  and no skeleton for background changes.
- Removing a configured library root removes tracks outside the remaining roots
  from SQLite, Lucene, and the waveform cache through
  `LibraryScanner.RemoveTracksOutsideRoots`.
- Metadata extraction supports ID3v1/v2, Vorbis Comments, APE tags, and embedded
  artwork. The scanned PCM/container formats include Matroska Audio (`.mka`),
  whose actual codec support is provided by FFmpeg.
- Chaptered MKA files are probed through FFprobe and stored as stable
  `mka://chapter/` virtual tracks sharing the physical MKA through
  `source_path`; their time bounds reuse `segment_start` / `segment_end` and the
  defining MKA path is stored in `cue_path` for lifecycle cleanup. The physical
  file is exposed as a normal track only when no usable chapters exist. MKA
  probing caps FFprobe analysis and has a 30-second per-file timeout so a
  malformed or slow network file cannot block a complete library scan.
- `track_title_overrides` stores library-only title corrections keyed by stable
  track path. `AudioDatabase.Upsert` reapplies them after every scan, so virtual
  MKA chapter titles can be corrected without modifying the media container.
- `track_metadata_overrides` stores confirmed MusicBrainz folder-repair values
  for title, track/album artists, album, numbering, and MusicBrainz IDs.
  `AudioDatabase.Upsert` reapplies them before normalization so later scans do
  not restore bad embedded tags; the media files are never rewritten.
- Library scans include `.cue` files. CUE `FILE`, `TRACK`, `INDEX 01`, `TITLE`,
  `PERFORMER`, `REM GENRE`, and `REM DATE` metadata produces independently
  searchable and queueable virtual tracks. PCM playback seeks and stops FFmpeg
  at the stored segment boundaries; queue persistence and playback history keep
  the virtual path so tracks sharing one source remain distinct.
- Metadata extraction stores track and album ReplayGain values. The first scan
  of each configured root after ReplayGain support was added refreshes unchanged
  tracks once so existing libraries receive those values. New or changed files
  that do not contain ReplayGain tags are analysed with FFmpeg during the scan:
  the scanner calculates missing track gain per file and missing album gain by
  analysing the affected album as a whole through a temporary FFmpeg concat
  list. Existing ReplayGain metadata is preserved. Settings > Playback exposes
  **Calculate missing ReplayGain** to backfill the local library and every
  configured Orynivo Server. Remote calculations use authenticated
  `POST /api/replaygain`, report progress through `/api/scan`, and preserve all
  existing track and album values. Normal server scans remain governed by the
  separate `CalculateMissingReplayGainDuringScan` option.
- The desktop's scan-time ReplayGain checkbox controls only its local watcher.
  Each Orynivo Server dialog reads and writes the server-owned equivalent via
  authenticated `GET`/`PUT /api/settings/replaygain`; the server applies it to
  the live watcher and persists it without requiring a restart.
- Opening the database runs a legacy-data migration that normalizes artists,
  albums, and artwork and removes old per-track artwork BLOBs
- `album_artist_rebuild_v1` rebuilds album assignments strictly from
  `album_artist` so compilations are not split by track artist
- `album_title_uniqueness_v1` and `album_title_artist_identity_v1` are
  historical album migrations. `album_disc_directory_identity_v1` supersedes
  them and rebuilds album identity from normalized album title plus the physical
  album root (`source_path` for CUE tracks). Conventional disc directories such
  as `CD1`, `CD 2`, `Disc 1`, and `Disk-2` resolve to their common parent
  directory.
- `RebuildAlbumsFromAlbumArtists()` retains its historical public name but now
  rebuilds by title and physical album root. It keeps compilations and
  multi-disc releases together even when their track artists or disc directories
  differ, preserves favorites, and prefers the embedded cover from each physical
  album.
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
- Manual MusicBrainz cover searches preserve punctuation inside the primary
  quoted release/artist query and URL-encode the complete query, so stylized
  titles such as `M!ssundaztood` remain searchable. If the exact query finds no
  cover results, the search falls back to punctuation-compacted and
  punctuation-spaced query variants.
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
- `AppSettings.StartMaximized` defaults to maximized startup. When disabled, the
  main window restores its last normal logical size and physical screen
  position; saved bounds must be validated against current screen working areas
  so removed-monitor positions fall back to centered startup. Never persist or
  restore a minimized state.
- `artwork_files_v1` exports legacy artwork BLOBs into the file cache;
  `artworks.data` remains for compatibility with old `NOT NULL` schemas Artwork
  file paths are also verified per current app-data artwork root; when cached
  files are missing or point to another environment, originals and thumbnails
  are recreated from `artworks.data` and the stored paths are updated.
- Thumbnail generation is intentionally fault tolerant; invalid embedded artwork
  must not prevent startup
- `normalized_library_v1` prevents expensive legacy migration checks on every
  database open
- `AudioDatabase.Optimize()` runs `wal_checkpoint(TRUNCATE)`, `VACUUM`, and
  `ANALYZE`
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
- The same artist-info controls form the normal non-Plex artist detail page. Its
  accent-bordered hero places the image beside the artist name, bounded
  scrollable biography, source, rename, image-management, and refresh actions;
  its album strip combines normalized matching local and Orynivo Server
  identities. The image occupies a fixed left column and must never overlap the
  biography or actions in the right column. Its favorite button applies to all
  normalized matching local and Orynivo Server identities. Artist links and
  double-clicks must not fall back to a bare album-only list. Back returns to
  the originating view in one step. Album cards in the artist detail open only
  on double-click. They retain the provider-local artist ID/name as the initial
  album-track scope, expose the existing show-all-tracks checkbox, and keep the
  artist detail visible until the album detail has finished loading. Back from
  that album must restore the same unified artist detail rather than a generic
  Artists or Albums view. Local profile publication retains request/token,
  sidebar tag, detail version/name, visibility and enclosing Back ownership.
  Database, synchronization and image continuations reject abandoned work; old
  errors/completion cannot change newer profile controls. Retained-row refresh
  stages image decodes before guarded metadata assignment and disposes rejected
  images. Profile admission installs new profile/background ownership before
  cancellation; top-level cancellation detaches these alongside top/cloud/Plex
  before callbacks so replacements remain admitted. Remote-library and
  now-playing profiles use the same admission ownership; retain configured
  source identity and reject changed/removed connections, credentials or server
  profiles. Now-playing publication also retains the exact track row, artist ID
  and metadata provider. Nested artist album/track loads check the enclosing
  guard before cards, playback metadata, tracks and failure cleanup. Rejected
  decoded profile images are disposed. Profile synchronization retains its
  enclosing guard between local reads, image saves, writes and remote awaits;
  stop later writes/cache cleanup after abandonment. Capture copied server
  connections and source guards before any await and skip changed sources.
  Already admitted writes are not rolled back. Automatic row profiles retain
  background CTS, sidebar tag, language, row identity/manual-image state and
  visible binding identity. Do not scan the entire bound catalog per completion.
  Guard before persistence, after decoding and before row publication; dispose
  rejected images. Per-artist loading markers retain their background owner so
  old completion cannot remove a newer marker. Keep the initial negative-result
  freshness stamp to bound retries. Remote now-playing profile resolution passes
  the enclosing guard into `GetArtistProfileForViewAsync`; the standard metadata
  interface delegates without a view guard. Copy source settings per request and
  retain original connection/profile identity. Recheck after cached profile,
  external download, image read, upload and server-image awaits. Reject
  abandoned work before the next operation or cache write and before returning a
  publishable profile. Already admitted operations are not rolled back. Keep
  source snapshots only in memory and preserve the existing server-owned cache
  paths. Refresh lookup selection captures displayed local/remote/now-playing
  identity, source, sidebar/detail/name, profile observation and outer Back
  before the dialog. NavigationLoadOwnership.CaptureCurrent observes without
  admitting or cancelling work; a later Begin/Invalidate rejects the
  observation. Recheck before selection and after its await, then refresh only
  the captured target. Do not restore an old unified name or run an
  uncancellable album reload after profile completion; profile refresh already
  preserves the unified strip.
- Artwork A-Z navigation indexes the complete lightweight artist/album result,
  but binds rows to the virtualized wrap panels in pages. A jump must append
  through the target row and defer `ScrollIntoView` until layout has processed
  the collection changes; rebinding an artwork view resets its old offset.
- The artist information view can search Wikimedia Commons using editable text
  and assign the selected image without replacing the cached biography or its
  source URL.
- Opening artist information must synchronously clear the previous artist's
  image, biography, source URL, IDs, and mutation targets before any local or
  remote lookup begins. Unified artist info buttons use the row `Title`, never
  the track-style `Artist` field. Manual profile refresh prompts for an editable
  external lookup name; that query may affect Wikipedia/Last.fm resolution but
  must not rename the artist stored in any library.
- Artist table and artwork views use double-click as their sole detail gesture;
  do not add a redundant per-row info icon. The unified album strip is populated
  incrementally before biography/image lookup and must remain visible throughout
  that lookup. Loading, failure, and not-found status belongs inside the hero
  below its action row, never as a centered overlay over the albums.
- The artist hero uses a roughly 420 px minimum height with a 320 px image and
  enlarged bounded biography viewport; keep the album strip below the hero.
- Unified artist detail loading is strictly album-first: query local catalog
  data off the UI thread, render local albums, query configured servers
  concurrently, render their albums, yield a render pass, and only then start
  the cancellable biography/image phase. Profile loading must not clear the
  unified album strip. Local artwork decoding also stays off the UI thread so
  playback and transport interaction remain responsive.
- Artist images remain visible even when no biography is available.
- Manual artist-image upload/deletion synchronization copies the original server
  set before local work and retains configured source identity. Recheck after
  lookup, before each remote mutation and before list/artwork cache cleanup.
  Skip changed/removed sources; newly added servers stay outside the admitted
  operation. Local workers check cancellation after opening SQLite and before
  writes, and remote operations check between requests. Shared catalogs are
  invalidated in finally after admission, including cancellation following a
  write. Never roll back admitted changes or replace biography metadata. Dialog
  and hero publication require their own enclosing ownership guards; source-safe
  synchronization alone does not protect UI.
- Artist-detail image search/upload/delete adapters capture displayed
  local/remote/now-playing identity, source, sidebar/detail/name, profile
  observation and outer Back before selection or synchronization. Independent
  manual-image publication ownership rejects older completion after a newer
  action. Recheck after selection before mutation, then after synchronization,
  decoding and local reads. Decode uploaded bitmaps off UI and dispose rejected
  images. Publish only the retained hero/captured row; local row refresh,
  failures and final status use the same guard. Accepted data writes remain
  navigation-independent. Search retains its query and adapts the selected bytes
  into the same flow, sharing publication ownership with file upload. Preserve
  the direct search upload to a copied owning-server connection and current
  failure feedback. Recheck afterward before subsequent
  synchronization/publication. Accepted direct writes invalidate catalogs even
  after navigation; only clean the source cache while its identity is valid.
  Separate library context-menu artwork adapters and automatic-profile/manual
  data-write overlap still need their audits.
- `Orynivo/ArtistImageWriteQueue.cs` orders complete manual artist-detail image
  search/upload/deletion batches per normalized name within one window. Reserve
  before waiting, recheck original publication/source ownership before the first
  write, and retain ordering through local file/SQLite work and server fan-out.
  Owning-server search uploads share the queue. Capture destination connections
  before waiting; new servers are excluded. Idle entries are removed and failed
  or obsolete batches release successors. The caller synchronization context
  remains intact for ownership checks. Unified automatic profile synchronization
  shares this queue through its local file/SQLite and remote batch, rechecking
  enclosing profile/image ownership after waiting and between operations. A
  later manual batch writes last. Primary server artist-detail uploads also join
  via UpdateArtistProfileInWriteOrderAsync. Recheck view/source/cancellation
  after waiting and form image payloads only after image ownership validation;
  current biography-only uploads remain admitted after manual image
  supersession. Local foreground profiles use
  DownloadLocalArtistProfileInWriteOrderAsync for ordered download, SQLite
  persistence and image-byte capture. Determine download policy after waiting,
  preserve current biography after image supersession, and capture image bytes
  before releasing ownership. Unified synchronization is admitted afterwards to
  avoid nested same-artist waits. Server artist-detail downloads use
  DownloadServerArtistProfileInWriteOrderAsync for ordered download and byte
  capture, checking policy after waiting and retaining biography after image
  supersession. Check authoritative server manual-image state after waiting and
  protect images when that state is unavailable.
  ServerArtistProfileCacheIdentity derives stable negative download cache IDs
  from opaque server ID and artist ID using SHA-256; positive local SQLite
  artist cache files remain separate. Real artist IDs remain unchanged in API
  requests. Cache file stems and save/delete wildcard patterns use invariant
  numeric formatting. Release before the separately guarded/ordered primary
  upload and unified synchronization. Now Playing supplies an independent image
  guard and shared ordered-write adapter to GetArtistProfileForViewAsync.
  Initial metadata reads stay outside write admission. Ordered work refreshes
  manual-image state after waiting and uses source-owned negative cache IDs.
  Superseded cached images return biography without image writes. Forced
  biography refresh retains manual server artwork; unique server-image staging
  rechecks ownership before replacing cached bytes and cleans rejected/cancelled
  staging. Release before unified synchronization. Standard metadata callers
  leave optional adapters unset. Automatic row jobs, Settings/context-menu and
  other windows/clients still need coordination. This does not provide a
  transaction.
- Foreground profiles separately observe admitted manual-image changes. Advance
  manual-image write ownership after accepted selection or at deletion
  admission; cancelled selection keeps profile images admitted. Older profiles
  skip image/placeholder/status and retained-row image publication, preserve
  current manual row metadata and dispose rejected decodes. Current biography,
  source and loading cleanup retain the original profile owner. Profile
  synchronization uses the additional image guard to stop later work after
  manual admission. Retain source-local artist identity and normalized name with
  the manual mutation so cancellation reentry cannot suppress a different
  artist's image while equivalent local/server identities still share image
  protection. Already-admitted file/server writes and automatic row jobs remain
  separate acceptance work.
- The artist information view can rename artists. A matching normalized name
  opens a merge dialog that asks which artist record and profile data survive;
  the transaction consolidates duplicate albums, reassigns tracks, preserves
  favorites and available album artwork, updates denormalized artist names, and
  rebuilds the Lucene index. Audio-file tags are not changed.
- Artist rename/merge dialogs must not be awaited while an `AudioDatabase`
  connection remains open. Resolve a possible collision first, dispose the
  connection, show the modal choice, then run the rename transaction.
- Cross-server artist rename synchronization captures the original source set
  and copied connection settings before its first await. Retain source ID, name,
  URL, credentials and profile identity in memory only; recheck configured
  membership before lookup, after responses and before every rename/merge.
  Changed or removed sources report incomplete synchronization; newly added
  servers are excluded. Accepted data changes complete independently of view
  navigation. Do not roll back admitted writes or delete a changed source's
  artist-list cache. Equivalent configuration objects remain eligible.
- Manual artist renames persist exactly one `artist_aliases` comparison-key
  mapping from the original tag name to the surviving artist ID. The new
  canonical name must never be written as an alias because it is already the
  stored `artists.name` and is loaded directly by `EnsureArtistComparisonCache`.
  Scanner and watcher upserts must resolve this alias and keep the canonical
  database display name in denormalized track fields; unchanged audio tags must
  never recreate or restore the pre-rename artist name.
- Local rename flows capture displayed identity/title, sidebar/detail/name,
  profile observation and outer Back before background artist lookup. Recheck
  before opening a merge dialog and after its choice before admitting the
  transaction. Accepted changes still synchronize and schedule Lucene updates
  independently of view navigation. Background indexing must not delay visible
  confirmation. Only the retained detail may update title, status, filter, list
  selection or profile refresh; obsolete failures cannot replace current status.
  Empty filter/playback IDs never match an absent merge target. Local playback
  publication retains track/provider identity and rejects remote numeric ID
  collisions. Final synchronization failure retains exactly the edit's next
  profile admission, including cancellation callback replacements.
- Server edits likewise retain displayed row identity/title, copied connection,
  sidebar/detail/name, profile observation and outer Back before dialogs.
  Resolve without activating another server; guard the edit commit callback
  before HTTP and recheck before merge admission and after every awaited UI
  operation. Accepted writes invalidate shared catalogs and still synchronize
  local metadata after navigation. Changed sources cannot receive additional
  remote synchronization or cache deletion. Playback updates require the
  original track/provider and complete owning-server identity, preventing
  collisions between numeric artist IDs on different servers. Refresh the
  retained detail directly without opening the overview or hiding its hero;
  reject obsolete metadata before row mapping/profile admission. Final failure
  retains exactly the edit's next profile admission.
- On large libraries, do not open `AudioDatabase` for collision lookup on the UI
  thread after the rename dialog. Collision lookup and a collision-free rename
  share one background connection; only an actual collision returns to the UI
  for the merge-choice dialog. Verify the committed artist name before
  refreshing lists.
- `EditArtistNameDialog` owns the complete confirmation lifecycle through
  `CommitAsync`: clicking **Rename** or pressing Enter disables its inputs,
  awaits the verified SQLite rename/merge operation, and closes only after
  success. Failures keep the dialog open with a localized status message. Its
  local button themes explicitly define centered content and theme-aware
  hover/pressed surfaces.
- Artist names are normalized when scanned: only the primary performer is
  retained, `feat.`/`ft.` suffixes are removed, and Unicode, whitespace, case,
  diacritic, and punctuation variants share one normalized artist identity
- The local Artists view contains album artists rather than every track-only
  credit. The scanner records missing `ALBUMARTIST` values and compilation
  flags; album-wide reconciliation keeps a consistent explicit album artist,
  uses the sole inferred track artist for single-artist albums, and assigns
  `Various Artists` to compilations or albums with differing inferred artists.
  MusicBrainz artist IDs, when embedded, are stronger identity evidence than
  normalized spelling.
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
  `LocalLibraryPlaylistProvider` and `PlaylistSelection` against the shared
  mixed playlist list. Remote selections store stable `orynivo://` references;
  legacy server playlist providers/endpoints are not exposed in current context
  menus.
- Selecting a playlist immediately adds the track or all album tracks and
  updates the status bar
- Track-row playlist `ContextFlyout` instances are rebuilt directly before they
  open so playlists created from a context menu appear immediately in the next
  add-to-playlist menu without rebinding the complete table.
- The album-detail header includes **Save as playlist**. It reads the current
  `ContentDataGrid.ItemsSource`, so it saves exactly the album tracks currently
  displayed after the artist-scope checkbox is applied, and opens the shared
  themed playlist `MenuFlyout` for an existing or new regular playlist.
- **New playlist...** opens `NewPlaylistDialog`; a name is required and Enter
  confirms
- `Orynivo/NewPlaylistDialog.axaml/.cs` is themed with dynamic brushes and a
  DWM-colored native title bar
- `AppendPlaylistItems()` builds context-menu items dynamically
- Album artwork cards extend their existing cover menu through
  `ContextMenu.Opened`
- `GetPathsForRow()` returns one track path or all album tracks through
  `GetTrackListByAlbum`. For a remote Orynivo Server album row it resolves the
  album's track list through a server request, so it must never be called
  synchronously while a row is being realized (during `LoadingRow` /
  `DataGrid.MeasureOverride`): `BuildOrynivoArtworkContextFlyout` populates the
  remote album's playlist targets off the UI thread only when the flyout is
  opened. Resolving remote rows synchronously on the UI thread froze the whole
  Albums table once a server was merged into the unified library.
- `PlaylistMenuTag(long PlaylistId, IReadOnlyList<string> Paths)` stores paths
  directly
- Folder nodes recursively collect tracks through `GetTrackPathsUnderDirectory`;
  empty folders have no menu
- Main-window context menus use application theme resources and a custom border
  template without the default white icon strip
- The global `ControlTheme x:Key="{x:Type ContextMenu}"` in `App.axaml` provides
  a complete `Template` with a `Border` bound to `Background`, `BorderBrush`,
  `BorderThickness`, and `CornerRadius`; this is required so that
  programmatically created context menus (e.g. the built-in cut/copy/paste menu
  of `NumericUpDown`) also render with theme colors instead of the
  Fluent-default white popup background. Property-only themes without a
  `Template` are silently ignored by the Fluent renderer.
- Dynamically created menu objects receive their styles through Avalonia
  `ControlTheme` resources looked up via `TryGetResource`
- Track, search-result, album-row, and folder-tree playlist actions follow the
  same proven pattern as sidebar radio/podcast/playlist actions: assign a themed
  `MenuFlyout` to the item's `ContextFlyout` property before opening, then
  register the tunnel-phase `PointerPressed` handler directly on that
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
  repeated-selection handlers must explicitly accept only the left mouse button.
  Explicitly created `MenuItem` objects must also receive the shared item theme
  directly; `ItemContainerTheme` alone does not reliably restyle preconstructed
  controls.
- **Remove from playlist** appears only for regular playlist entries with a
  `PlaylistEntryId`
- `_activePlaylistId` is set by `ShowTopLevelViewAsync` only for playlist views
- `ContentRow.PlaylistEntryId` contains `playlist_tracks.id` only in regular
  playlist views
- Playlist localization keys must exist in German, English, French, Spanish,
  Russian, Simplified Chinese, and Hindi

## Editable Playback Queue

- The static sidebar item `Queue` opens the localized **Up next** view backed
  directly by `MainWindow._queue`; do not create a second playback-order model.
- Queue rows retain their `PlaylistItem` reference so duplicate paths can be
  removed and moved independently. Moving the active item must recalculate
  `_queueIndex` by reference, not by path equality.
- Queue rows can move up/down or be removed, and the complete queue can be saved
  as a regular playlist through `NewPlaylistDialog`.
- The Up Next header exposes a localized **Clear queue** action that clears the
  editable queue without stopping the currently playing track; MCP `clear_queue`
  uses the same `ClearPlaybackQueue` path.
- Queue mutations update navigation buttons, the visible queue table, shuffle
  history, and persisted settings together.
- The gapless PCM players receive an immutable item list at startup. Mutating an
  active gapless queue therefore restarts the current stream and seeks back to
  its audible position so the revised order takes effect immediately.
- **Restore last queue**: `CapturePlaybackQueueState` detects a wholesale queue
  replacement (the new queue shares no path with the outgoing one) and stores
  the outgoing queue via `AudioDatabase.SavePreviousPlaybackQueue` (kept in
  `app_meta` as `previous_playback_queue`). The Up Next header shows
  `RestoreQueueButton` when `GetPreviousPlaybackQueue()` is non-empty;
  `RestorePreviousQueueAsync` swaps it back in and re-captures the outgoing
  queue, so restore is reversible.
- **Drag & drop into the queue**: `SetupQueueDragAndDrop` makes the
  `ContentDataGrid`, `AlbumArtworkListBox`, and `FolderTreeView` drag sources
  (tunnel pointer handlers start a `DragDrop` operation carrying
  `orynivo/queue-paths`) and the always-visible `QueueNavItem` the drop target.
  Dropping on **Up Next** appends the resolved paths to the queue end and must
  not restart the active playback session. Track rows (local, remote, Plex) use
  their `FilePath`; local album rows and album cards resolve via
  `GetTrackListByAlbum`; local folders via `GetTrackPathsUnderDirectory`; remote
  folders via the in-memory `FolderTree` (`_folderTreesBySource`). Remote album
  rows/cards carry an `orynivo-album:server:id` reference that
  `ResolveDroppedQueuePathsAsync` expands to registered track stream URLs on
  drop (`QueueNavItem_OnDrop` is async). The queue and the source lists are
  never co-visible, so the sidebar item is the drop target.

## Playlist Database Structure

- `playlists`: id, name, description, created_at, modified_at, `is_smart`,
  `filter_criteria`
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
- `GetTrackListByIds(ids)` batches large ID sets to stay below SQLite variable
  limits
- `GetTrackListByPaths(paths)` batches queue metadata lookup and deduplicates
  query paths while the queue view restores the original order and duplicates.
- `GetTrackListPage(page, pageSize)` applies ordering and `LIMIT`/`OFFSET` in
  SQLite; server paging must not materialize the complete track catalog first.
- Schema migration is coalesced once per physical database path and process.
  Creating a missing database at a previously used path must clear that marker.
- The shared Artists, Albums, and Tracks views retain at most three versioned
  final row snapshots. Local/remote loading runs concurrently where independent;
  watcher, remote-version, favorite, and artwork mutations invalidate the cache.
  `LibrarySourceLoader` isolates source failures with credential-free outcome
  metadata. `LibraryLoadResult` distinguishes complete, partial, failed, and
  cancelled requests independently of row count. The UI keeps successful rows
  when another source fails; only complete results enter `LibraryViewCache`.
  That cache atomically rejects results from an invalidated catalog generation.
  `LibraryCatalogLoadCoordinator` owns this single cache and shared-view load
  versions. A request captures owner, credential-free key, generation, version
  and navigation token before source work. Independent typed source adapters
  start together and combine in input order. Remote shared-view fan-out uses
  `LibrarySourceBatchLoader` with all configured servers admitted concurrently
  and one independent 20-second budget per server. Window server adapters return
  rows or propagate errors; the scheduler alone creates sanitized outcomes.
  Search/playlist concurrency remains independently bounded to three. Tests
  compose this scheduler with catalog ownership for five-server admission,
  ordered partial outcomes, late invalidation and complete empty recovery.
  Obsolete/cancelled requests return no publishable result. Store checks current
  request, key and filter eligibility before the existing complete-result/LRU
  policy. Search, playlists and Mobile share this generation without sharing
  catalog request versions. The window keeps source/filter adapters,
  sorting/merging, binding and remote registration. LibraryCatalogLoadContext
  captures copied track facets, entity favorite filters, artwork/drilldown
  choices, remote favorites and cloned server/profile settings before loading.
  Remote cache readers reapply captured favorites; full cache validation and
  complete-only writes are unchanged. Local profile identity is inherited
  through AudioDatabase's AsyncLocal into Task.Run. Contexts are transient and
  never logged or persisted. LibraryCatalogConfiguration compares source order,
  identity, name, connection/authentication, profile and streaming options.
  Saving changed servers invokes InvalidateCatalogConfiguration immediately
  after replacement and before subsequent awaits; profile changes share this
  Dashboard/Genre Cloud/unified/similarity invalidation path. This also abandons
  independent search/playlist publication through the unified invalidator. Tests
  cover every compared field, held configuration publication/recovery and local
  worker profile/favorite/artist/album scope isolation using synthetic SQLite.
  Watcher callbacks may run on workers: immediately invalidate only thread-safe
  Dashboard/Genre Cloud/unified cache generations. Coalesce UI cleanup and
  refresh hints on the dispatcher. Capture search and playlist versions before
  invalidation and abandon each independently only while its version remains
  current; deferred watcher work must preserve newly admitted requests.
  Similarity cleanup and notice updates stay on the dispatcher. Worker
  invalidation and burst/newer-owner behavior have window-free and actual-window
  regression coverage. Both Settings root-change callbacks use
  CleanupLibraryRootsAsync with copied roots. Only a successful nonzero SQLite
  removal raises the safe watcher notification; no-op/failure does not announce
  a mutation. PrepareForLibraryImport invalidates shared catalog snapshots,
  cancels the pending unified-load token and clears its outcome/retry state
  before archive replacement, retaining existing search/playlist/playback
  abandonment. Synthetic SQLite/HTTP tests reject held pre-cleanup and
  pre-import publication and verify no-op generation stability. New
  cancellation/request ownership is installed before cancelling old work, so
  reentrant reloads retain the latest request. Coordinator tests run without a
  window; isolated notice/search tests use synthetic data and fake HTTP.
  Navigation cancellation and load versions also reject late publication, and
  remote playback registration happens only after those checks. The current
  source outcomes drive a localized incomplete-load notice and **Try again**
  action. Retry keeps usable rows interactive until publication, then restores
  source-aware selection and scroll position. Deferred restore callbacks reject
  navigation, load-version, and catalog-generation changes. Full remote catalog
  cache writers use the HTTP client's strict `requireComplete` mode, including
  every 5,000-row track page. Track cache schema version 2 and new artist/album
  filename identities discard older potentially incomplete snapshots. A request
  failure never saves a partial catalog; a valid server library timestamp is
  required before disk-cache reuse.
- `GetTracksByDirectory(dirPath)` uses an SQL prefix query plus a direct-child
  filter
- `GetTrackPathsUnderDirectory(rootPath)` returns all recursive track paths
  below a root
- Folder-tree lazy loading uses an in-memory parent-to-children map
  (`FolderTree`) and materializes tree nodes only when a directory is expanded,
  for both local and remote sources (`CreateDirItemLazy` adds a placeholder and
  populates on the intercepted expand gesture)
- `TrackLite`, `TrackListInfo`, `ArtistInfo`, and `AlbumInfo` remain
  intentionally small; `TrackRecord` is reserved for complete metadata
  operations
- `GetTrackFacets()` remains a lightweight interactive-filter query;
  `GetSmartPlaylistTracks()` separately aggregates playback counts and the last
  playback timestamp only while resolving a smart playlist
- Artwork is deduplicated instead of stored per track
- `TrackSearchIndex.cs` stores a Lucene.NET index under
  `%LOCALAPPDATA%\Orynivo\search-index`, supports category-specific fields,
  partial words, and German umlaut/eszett variants, rebuilds stale indexes,
  updates incrementally after scans, and removes missing files below rescanned
  roots Supplemental MusicBrainz genres/tags are included in the all-fields
  document; persisting enrichment must update that track's Lucene document.
- Search-index freshness is determined by the stored schema marker; indexed
  `Field.Store.NO` fields must not be tested through stored-document field
  access
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

- Target platform: `net10.0-windows10.0.19041.0`, x64
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
- Linux exposes the MPRIS 2 media player interface through
  `Orynivo/Compatibility/Linux/MprisMediaTransport.cs`, compiled only under the
  `ORYNIVO_LINUX` define because `Tmds.DBus.Protocol` is referenced on Linux
  only. It dispatches onto the same shared transport methods as the desktop
  controls, must never throw into playback when the session bus is unavailable,
  and must not publish credential-bearing artwork URLs through `mpris:artUrl`
  (gate them through `QueuePathPolicy.CanPersist`). Remote Orynivo Server covers
  use the locally cached `remote-artworks/track-art-<server>-<track>.img` file
  instead, and the media metadata is refreshed after the asynchronous artwork
  download so the cover appears without exposing the `?key=` URL.
- `SteinbergAsioStream.IsBackendAvailable()` validates each native bridge and
  loads its shared export API dynamically. Settings shows **Steinberg ASIO**
  only when `AsioBridge.dll` exists and **cwASIO** only when `CwAsioBridge.dll`
  exists. A missing selected backend migrates to the other ASIO implementation
  when available, otherwise to WASAPI.
- `OutputBackend` values are persisted numerically; existing values remain
  `Asio=0`, `Wasapi=1`, and `KernelStreaming=2`, while `CwAsio=3` is appended.
  Classic network AirPlay is appended as `AirPlay=4`; never renumber existing
  values.
- Native DSD supports `.dsf` and uncompressed stereo `.dff`
- DST-compressed `.dff` is not played natively
- Output types represented by settings: Steinberg `ASIO`, `CwAsio`, `WASAPI`,
  `KernelStreaming`
- Steinberg ASIO, cwASIO, and WASAPI are implemented; Kernel Streaming is not
- Windows WASAPI handles PCM only. Native DSD uses ASIO/cwASIO on Windows and
  direct ALSA `DSD_U32_BE` on Linux; Linux DSD must not depend on either bridge.
- WASAPI runs exclusively and selects the highest supported sample rate up to
  the source rate, then the first supported stereo format from 32-bit float,
  packed 24-bit PCM, and 16-bit PCM. Sources above the endpoint maximum are
  converted by FFmpeg; when only higher rates are available, the lowest one is
  used only if it remains within a configured positive output ceiling.
- WASAPI plays DSF/DFF by converting DSD to PCM through `ffmpeg` without a
  temporary file using the same endpoint-aware sample-rate and precision
  selection as other PCM playback.
- Settings can force DSF/DFF through the same FFmpeg PCM path for ASIO/cwASIO.
  Forced conversion participates in gapless PCM playback and enables volume,
  ReplayGain, and equalizer processing; disabling the option restores native
  bit-perfect ASIO/cwASIO DSD routing.
- Linux native DSF/DFF playback bypasses the ASIO/cwASIO selection entirely: it
  writes native `DSD_U32_BE` or DoP directly to the selected ALSA hardware
  profile and must not load or require either Windows bridge.
- ASIO/cwASIO PCM playback queries the driver's reported sample rates before
  opening the stream and converts sources above or between supported rates to
  the highest available rate that does not exceed the source.
- The transport file-information line and status bar explicitly identify
  DSD-to-PCM conversion and show the selected PCM output sample rate.
- The transport file-information line shows a compact localized `RG` badge when
  ReplayGain is enabled and the current PCM track has a usable track or album
  ReplayGain value. Native DSD, radio, podcasts, and tracks without ReplayGain
  metadata keep the badge hidden.
- WASAPI pause keeps the exclusive AudioClient running and supplies silence so
  drivers do not loop the final endpoint buffer; buffered audio remains
  available for resume
- WASAPI playback position subtracts frames still queued in
  `BufferedWaveProvider`, so transport time, history, and synchronized lyrics
  follow audible output instead of producer progress
- Transport uses custom vector icons for previous, play/pause, next, secondary
  action buttons, quick-picker settings, and volume; unavailable queue
  directions are disabled. Small UI icons should use shared `StreamGeometry`
  resources from `App.axaml` instead of text glyphs or emoji.
- Seeking is implemented for ASIO PCM, WASAPI PCM, DSF, and DFF
- Loading a file or folder builds a playback queue; completion advances
  automatically
- Sequential PCM queues use one persistent ASIO/cwASIO or exclusive WASAPI
  output session. The next FFmpeg decoder is started and prefetched while the
  current track plays, then its samples are appended without reopening the
  device. Audible track changes are derived from rendered/buffered frame counts
  so transport metadata and playback history change at the actual boundary.
- Gapless playback is disabled for shuffle queues and native ASIO DSD (DSF/DFF).
  Changing Shuffle refreshes already prepared output through the shared gapless
  refresh coordinator, preserving current position and pause state. Automatic
  advancement includes forward shuffle history after Previous. Those paths
  retain title-by-title device handling; DSD converted to PCM through WASAPI
  participates in the PCM gapless pipeline.
- Seeking remains available inside multi-track gapless PCM sessions. A seek
  clears buffered output, restarts the current FFmpeg decoder at the selected
  position, and rebuilds preparation of the following track.
- Gapless PCM position offsets are stored per queued track. Preparing or writing
  the next decoder must not reset the seek offset of the track that is still
  audible; the transport changes offsets only with the rendered track boundary.
- PCM user volume is applied at the active output stage rather than baked into
  prefetched samples: WASAPI follows the selected Windows endpoint's master
  volume bidirectionally and the native ASIO bridge applies an atomic volume
  factor in its callback. Per-track ReplayGain remains part of PCM sample
  preparation.
- `StartPlaybackAsync` accepts an optional `initialPosition` parameter. When
  provided, the new player is seeked to that position immediately after creation
  and before transport UI setup, ensuring no audio from position 0 is heard.
  Used by the output quick-pick popup to resume at the exact track position
  after a device switch.
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
  saturating sample conversion to prevent integer overflow; native DSD ignores
  it.
- The parametric equalizer runs after ReplayGain and before ASIO/cwASIO or
  WASAPI PCM output. It supports peak, low/high shelf, low/high pass, preamp,
  and imported `GraphicEQ` curves. Live changes crossfade over 50 ms, gapless
  transitions preserve filter state, seeks reset it, and native DSD ignores the
  equalizer.
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
- Equalizer profile changes and seek resets must never synchronously lock the UI
  thread against the PCM pump. Players atomically queue those requests and apply
  them from the audio pump before processing the next block. Profile file
  reading/parsing runs off the UI thread and enforces bounded input size and
  filter count.
- Settings previews equalizer enable/disable immediately against the active PCM
  player through a debounced background request; the checkbox event itself must
  never call into the player. Cancel restores the original state. Saving
  settings must only reconfigure endpoint synchronization when the backend or
  selected device actually changed. Driver enumeration, endpoint open/close, and
  player disposal during a device change must not run on the UI thread.
  Device-change settings application waits at most two seconds for old-player
  disposal before continuing; a misbehaving driver must not hold the settings
  workflow.
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
  `FontSizeDisplay`; tables use `FontSizeCaption`/`FontSizeBody`; small meta
  text uses `FontSizeMeta` with a muted foreground.
- The main window uses a modern sidebar, content area, and full-width transport
  bar
- The full Orynivo logo appears directly, without a surrounding logo surface, in
  the startup window and About dialog. The main sidebar uses the linked
  `Logo/orynivo.png` asset (`Assets/DashboardLogo.png` at runtime) as its
  wordmark.
- The 260 px sidebar contains Dashboard, library navigation with local and
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
- `AppSettings.ShowInternetRadioItem`, `ShowPodcastsItem`, `ShowQueueItem`, and
  `ShowAiChatItem` control visibility of the Internet Radio, Podcasts, Up Next,
  and AI Chat sidebar items from Settings > Appearance and default to visible
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
  filters, and mode controls. Directly below it, Artists, Albums, Tracks, Folder
  structure, and equivalent library overview views use a shared compact
  accent-bordered intro card (`#20D9E8`, `CornerRadius="14"`) with a short
  view-specific headline and explanatory text. Search and filter controls stay
  outside and above that card in the plain header layout; A-Z indexes stay
  aligned with the content/table area, not the intro card. The Dashboard
  deliberately has no shared intro card because its greeting hero is the first
  content surface.
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
  favorite state, playback controls, position, volume, two quick-pick buttons
  (EQ and Output), and an output-device lock toggle below the volume control.
  The EQ popup contains a profile ComboBox, a vector settings button, and a
  themed enable/disable checkbox (`PopupCheckBoxTheme`). The Output popup
  contains a profile ComboBox and a vector settings button. All three buttons
  use vector path icons and tooltips. The lock is closed only while an active
  player owns the configured output; selecting it snapshots playback context and
  position, disposes the player to release an exclusive device, and changes to
  an open lock that can reacquire the device and resume. A normal pause does not
  release the device. Right-clicking the now-playing cover opens a compact
  themed menu for opening the current album or artist, searching album artwork,
  and toggling the current track favorite.
- The transport uses a cover-derived accent brush (`AppTransportAccentBrush`,
  default `#20D9E8`) for the position-slider progress fill/thumb and the
  play/pause button background. `UpdateTransportAccentFromArtwork` recomputes it
  whenever the now-playing cover changes (subscribed on
  `NowPlayingArtworkImage.Source`): `ExtractAccentColor` samples a 24 × 24
  scaled copy, bins pixels by hue weighted by saturation × value, and normalises
  the dominant vibrant hue. It falls back to the default accent when there is no
  artwork or extraction fails; the brush is mutated in place so
  `DynamicResource` consumers repaint.
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
- Directories includes localized scan guidance below its folder list: use the
  existing per-folder Scan action, follow row progress, save Settings after
  completion, then open Tracks for normal double-click playback. This text
  neither starts a scan nor implies that a configured source has indexed rows.
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
  tokens remain memory-only in generated stream URLs and must never be written
  to settings, documentation, logs, or source
- Starting a Plex track from the folder tree queues only direct track siblings
  from that same tree level; subfolder tracks are excluded and the existing
  shuffle state applies to that sibling queue
- Plex tracks may contain several ordered `Media.Part` entries. The client keeps
  every part URL, and `GaplessPlaybackItem.SourcePaths` passes them to FFmpeg's
  concat demuxer as one logical track. Never reduce such an item to its first
  part, because playback would advance before the Plex metadata duration ends.
- A Plex FFmpeg decoder EOF is accepted as the track boundary only when the
  decoded position is within five seconds of Plex's authoritative duration.
  Earlier HTTP EOFs reopen the same logical item at the decoded position with at
  most three retries; they must not immediately advance the queue.
- Plex folder nodes use real `TreeViewItem` children in `Items`, matching the
  local folder tree. Do not bind an `ObservableCollection<TreeViewItem>` to
  `ItemsSource`: Avalonia can wrap those controls in additional item containers,
  producing expanded nodes with large blank child rows. The expand-toggle
  pointer event must be intercepted while a lazy node is unloaded; fetch and
  insert real child controls first, then set `IsExpanded=true`. Keep the
  `Expanded` event only as a keyboard-accessibility fallback that immediately
  collapses until loading completes. Suppress concurrent requests, classify
  nodes through `PlexMediaItem.IsFolder` rather than `PartKey`, and retain the
  placeholder after failure so loading can be retried. Double-clicking a folder
  header must intercept the second `PointerPressed` (`ClickCount >= 2`) in the
  tunnel phase and route it through the same lazy-load function before toggling
  expansion; handling only `DoubleTapped` is too late because Avalonia may
  already expose the placeholder row.
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
- Search result lists for artist images, album covers, and lyrics use the shared
  `AppSearchResultItemTheme`. Its custom presenter must forward `Content`,
  `ContentTemplate`, `DataContext`, and `TextBlock.Foreground`; selected items
  retain `AppPrimaryTextBrush` in both themes.
- Visible primary text and runtime messages use `LocalizationManager`
- TextBox normal, pointer-over, and focused Fluent theme resources must remain
  synchronized with `AppInputBrush`, `AppPrimaryTextBrush`, and the active
  input-border colors so entered text keeps sufficient contrast in both themes
- Any text placed on an accent, selected, tinted, image-derived, or otherwise
  colored background must use an explicit contrast-safe foreground resource (for
  example `AppAccentTextBrush` or a dynamically computed foreground), not a
  hard-coded assumption such as white text. Check dark and light themes before
  accepting new color combinations.
- Empty artwork areas use a dedicated placeholder resource
- Empty library, radio, and podcast states should explain the next useful setup
  or discovery action instead of only reporting that no rows exist.
- Tables, lists, and trees must not expose default-white backgrounds in dark
  mode
- DataGrid and ScrollViewer backgrounds are overridden via Avalonia styles in
  `MainWindow.axaml`
- DataGrid row headers remain disabled through `HeadersVisibility="Column"`
- Visible rows whose playback path matches the currently audible local, Plex,
  radio, or podcast item receive the `nowPlaying` class. Its background uses the
  theme-specific `AppNowPlayingRowBrush`; selected rows retain the stronger
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
  Local file nodes are additionally indexed by case-insensitive absolute path in
  `_localFolderTrackItems`; previous/next and gapless queue advances update
  those exact node references instead of relying only on TreeView traversal.
  Avalonia's Fluent local-tree template does not reliably paint the nested local
  node container background, so every local file node uses a dedicated header
  `Border` indexed in `_localFolderTrackHeaders`. The audible path sets
  `AppNowPlayingRowBrush` directly on that visible header and clears the prior
  header on playback start, previous/next, or gapless transition. Theme changes
  must refresh the complete highlight.
- DataGrid columns are user-resizable. Main library, search, radio, podcast,
  podcast-episode, Plex, playlist, and daily-history widths are restored from
  `settings.json`; invalid or structurally outdated width sets are ignored.
- Right-clicking a DataGrid column header opens a localized, themed `MenuFlyout`
  column chooser at the pointer position. It uses `StaysOpenOnClick` so several
  columns can be changed in one session. Do not replace this with a dynamically
  attached and programmatically opened Avalonia `ContextMenu`; Avalonia
  11.2/11.3 retains internal ownership in that sequence and can throw during
  placement. Track contexts additionally expose file name, album artist, year,
  track/disc numbers, genre, bitrate, sample rate, bit depth, channels,
  composer, BPM, file size, added date, and ReplayGain values. Radio and podcast
  tables expose only metadata appropriate to those catalogs. Artwork and action
  columns stay fixed, and at least one selectable data column remains visible.
- Identified data columns can be reordered by dragging their headers. The order
  is restored independently for each table/view; fixed artwork and action
  columns cannot be dragged.
- Every selectable or reorderable data column must have a stable,
  language-independent string in `DataGridColumn.Tag`. These IDs are persisted
  in `VisibleDataGridColumns` and `DataGridColumnOrders`; changing an ID is a
  settings-compatibility change. Fixed artwork/action columns intentionally have
  no persisted ID.

## Dashboard

Dashboard and Genre Cloud admit visible content before network completion and
bypass the shared blocking overlay/final whole-surface fade. Dashboard keeps
four stable section hosts in `MainWindow.Dashboard.Refresh.cs`; hero counts,
media, recommendations and listening statistics refresh independently. Display
fingerprints retain unchanged controls. Expensive in-flight phases are shared;
publication retains build/root navigation, catalog generation and outer Back
ownership. Show all pages retain their separate ownership of the shared panel.

Genre Cloud separates stable cache identity from completed-load generation.
Ordinary invalidation rejects old source work while keeping up to sixteen stale
levels available for immediate display. Root visits and stale levels revalidate
behind their interactive presentation. Failed complete-source refreshes retain
old data; changed results restore source-aware selection and guarded scroll.
Unchanged results preserve node controls. Mode switches reuse resolved data.

Cloud source reads share local facet input within one catalog generation;
invalidation drops the shared task and old work cannot repopulate it.
Independent remote track and album groups load concurrently; only selected
albums are read through `GetAlbumsByIds`, never a complete album catalog.
Authenticated `POST /api/albums/by-ids` accepts at most 100 IDs and retains
active-profile favorites, artwork paths and artist identity. The strict client
falls back only for 404/405 to detail requests with four workers; other failures
propagate. `GenreCloudService` precomputes immutable multi-parent ancestor sets
and normalized fallback phrases. Each snapshot resolves repeated raw genre
values once while keeping per-track identities, ratings and candidate ordering.
Facet SQL joins profile state and current-version audio features rather than
running three correlated subqueries per track. Lucene remains the full-text
index: it has no dedicated taxonomy facets or album-ID fields, so it is not used
as a substitute for authoritative genre/profile metadata.

Uncached foreground branch admission hides the old cloud and recommendation
hosts before the first await. `MainWindow.GenreCloud.Loading.cs` displays a
localized assembly panel and observes the shared `CompletedWorkProgress`
counter. Its fixed work units cover local facets, listening weights, each
configured remote source, composition, track resolution and album resolution.
Percentages count successful steps; step durations can differ. An indeterminate
bar covers preview lookup before source admission. Cache publication immediately
removes foreground feedback while background revalidation remains interactive.
The timer, cancellation and completion retain the original foreground owner;
obsolete work cannot hide another request's loading panel or destination view.

`MainWindow.ViewPreviews.cs` projects separate restart DTOs into
`ViewPreviewStore`: display data, normalized options and opaque source IDs only.
Never serialize runtime rows/settings, credentials, authenticated URLs or
bitmaps. Remote playback/artwork addresses are reconstructed with current
settings; unsafe history paths are removed from disk DTOs. Profile/source
identity selects hashed filenames. Configuration/authentication/profile changes
reject old writes and clear retained previews. The atomic writer uses unique
staging files and off-thread I/O, with at most 64 files, 4 MiB per file and a
seven-day read lifetime. Missing/corrupt/incomplete/oversized files are cache
misses. Cold Dashboard visits paint the greeting first; absent cloud previews
fall back to normal data loading. These guarantees concern ordering and
responsiveness, not a measured portable first-frame deadline.

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
  intentionally omit counts because normalized genre groups overlap multiple raw
  tags. Selecting genres performs Radio Browser tag queries (OR across selected
  genres) and may load up to 10,000 stations per genre.
- Podcast category selections work without a title query by sending the cached
  Apple genre IDs to the catalog search. Language-only filtering uses the
  regional top 100 podcasts as its starting set and then evaluates feed
  languages.
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
  summary and Windows media overlay are updated at the same time. Downloaded
  station logos are cached under `%LOCALAPPDATA%\Orynivo\radio-logos\` and the
  cached local file is passed to SMTC so Windows does not have to refetch the
  remote favicon URL itself.
- Radio search results expose a multi-select genre popup derived from normalized
  Radio Browser tags. Selected genres use OR semantics, technical tags are
  excluded, and unavailable selections are removed after a new search.
- The dashboard is laid out as a personal "music hub" and is built entirely in
  code in `BuildDashboardAsync`. Section headers are created by the reusable
  `DashboardCreateSectionHeader` (accent underline, optional month navigation).
  The page contains, top to bottom: 0. **Greeting hero**
  (`DashboardBuildGreeting`): a 210-px hero with a 3-px rounded rim that uses a
  lightened version of the same background artwork using the linked
  `Logo/bg.png` asset (`Assets/DashboardHeroBackground.png` at runtime). It
  contains the localized time-of-day greeting, welcome overline and descriptive
  text (all explicitly left-aligned within the hero content column); working
  random-play and Up Next buttons; and four live album, track, artist, and
  favorite counters. Local aggregates come from
  `AudioDatabase.GetDashboardLibrarySummary()`; remote track and album totals
  come from `/api/library/summary` (with lightweight fallbacks for older
  servers), while artist names are normalized and unified exactly like the
  shared Artists view. The favorite counter additionally includes only
  client-side track favorites that still occur in each configured Orynivo
  Server's current facets and resolve through `GetTracksByIdsAsync`
  (`ResolveDashboardRemoteLibrarySummaryAsync`). Stale IDs and unavailable
  servers must not make the hero/Quick Access count differ from the Favorites
  view. The database query returns only aggregate counts and must not
  materialize complete library lists. The four counters are compact 148-px cards
  in a right-aligned horizontal group; their circular icon badges remain
  left-aligned above the value and label.
  1. **Recently played** (`DashboardBuildRecentlyPlayed`, when history exists):
     a horizontal carousel of up to 20 cards from `GetRecentHistory` (deduped by
     path) placed beside Recently Added in a matching surface card. History
     cards use the same 180 × 276 proportions and 160-px cover size as album
     artwork cards. Each shows a local/remote album thumbnail or
     `InitialsAvatar`, hover play overlay, favorite heart, and an `L`, `OS`, or
     `P` source badge. Local and remote Orynivo hearts are toggleable; Plex
     hearts are display-only because Plex favorites are not persisted. A card is
     playable (`IsPlayableHistoryEntry`) when the entry is a music track (media
     type `track`, not radio/podcast) that is either a locally available file or
     a playable stream URL (remote server / Plex), independent of the history
     track id or artist tag. Clicking the card outside its link/favorite buttons
     plays it **in place** through `PlayHistoryEntryInPlaceAsync` (replaces the
     queue with just that track and starts playback) without leaving the
     dashboard.
  2. **Recently added albums**: horizontal artwork carousel of up to 20 albums,
     merging the local library with every configured remote Orynivo Server
     (`LoadRecentAlbumsAsync`, sorted by each album's last-added timestamp).
     Cards are the **same** artwork cards as the Albums view: each
     `DashboardAlbum` is mapped to a `ContentRow` (`BuildRecentAlbumRow`, local
     `Album` / remote `OrynivoAlbum` carrying `OrynivoServer`) rendered from the
     shared `AlbumArtworkCardTemplate` resource (`BuildRecentAlbumCard`), so
     covers can be changed and favorites toggled directly, and album/artist
     links navigate in library. Because those shared handlers are
     location-independent via `ActivateRowOrynivoServer` (activates
     `row.OrynivoServer`), they work for the dashboard's mixed local/remote
     list. Double-clicking a card opens the album
     (`RecentAlbumCard_OnDoubleTapped`). Local artwork uses the album's 320-px
     path (`RecentAlbumInfo.ArtworkPath`); remote uses the authenticated server
     URL. Backed by the server endpoint `GET /api/albums/recent`
     (`OrynivoServerClient.GetRecentAlbumsAsync`); servers without it are
     skipped. Selecting a card supports Back navigation. Cover/favorite handlers
     refresh the bound card in place on the dashboard surface
     (`UpdateRowArtworkFromBytes`) instead of rebuilding the hidden Albums list.
  3. **Stats section** (`DashboardBuildStatsSection`): a first row mirrors the
     visual hierarchy of the reference design with Listening stats, Most
     listened genres, Calendar, and Quick access cards. The period selector
     lives inside the Listening stats card, defaults to Last 30 days, and
     governs listening, genre, album, and artist data (All time / This year /
     This month / Last 30 days / Last 7 days); changing it rebuilds the
     dashboard. Listening data comes from `GetTotalListeningSeconds` and
     `GetListeningTrend`. The trend uses daily points for Last 7 days, Last 30
     days, and the elapsed current month; This year and All time use 12 buckets.
     At most seven localized date/month labels are rendered on the X-axis
     independently of the point count. The minute Y-axis ends at a rounded scale
     value strictly above the peak so the smoothed curve retains headroom. The
     cyan line uses Catmull-Rom-derived cubic Bézier segments (never straight
     point-to-point joins). Clamp each segment's two Bézier control-point Y
     values to the range of that segment's real endpoints so smoothing can never
     invent a peak or trough outside the measured values. The area under the
     line uses a vertical cyan-to-nearly-transparent gradient; do not replace it
     with a flat fill. It compares with the immediately preceding period where
     one exists. Quick access links to favorites, Up Next, full recent history,
     and random local playback and shows short secondary labels/counts. The
     calendar keeps independent month navigation. On wide dashboards the four
     overview cards sit in one row; on narrow dashboards they form a 2 × 2 grid.
     The layout switches at a 980-px threshold (`ComputeDashboardTwoColumn`) and
     re-flows on the `DashboardScrollViewer.SizeChanged` boundary crossing only.
     The wide overview uses equal star-width columns with fixed gaps and no
     outside card margins, so its outer edges align with the media cards above;
     all cards in one row stretch to a common height. Every listening-trend
     point has its own transparent hover zone and tooltip containing the
     corresponding localized date and listened-minute value; sparse X-axis
     labels must not reduce tooltip granularity. Because the chart path uses
     `Stretch.Fill`, its geometry must retain the zero-length `M 0,0 L 0,0`
     origin anchor before the curve. Without that anchor, Avalonia normalizes
     the curve's own peak to the top and falsifies the Y scale. Recently Played
     cards must not assign borders from pointer handlers; their normal and
     gradient hover borders come exclusively from `motionCard` styles.
     - **Calendar** (`DashboardBuildCalendarCard`): compact Monday-first month
       grid with day numbers, today highlight, activity dots, month navigation,
       tooltips, and clickable populated days.
     - **Top genres** (`DashboardBuildTopGenresCard`): the five leading linked
       genres in compact rows with cyan proportional bars and listened-minute
       totals. Genre bars use `DashboardBuildStatBar`, including the complete
       muted 100-percent background track; do not render them as fill-only bars.
       Their 31-px rows and spacing intentionally fill the complete
       overview-card body.
     - **Most listened albums** (`DashboardBuildTopAlbumsCard`) and **Most
       listened artists** (`DashboardBuildTopArtistsCard`): matching bordered
       analytics cards below the overview row, with wide cyan proportional bars
       (including the muted 100-percent background track) and listened-minute
       totals, backed by `AudioDatabase.GetTopAlbums`/`GetTopArtists`
       (`TopAlbumStat`/`TopArtistStat`), which merge local, remote Orynivo
       Server, and Plex playback by album title+artist / artist name in the
       selected period. Album rows show a small cover (local thumbnail, remote
       track artwork, or an `InitialsAvatar`); artist rows retain colored rank
       markers. Album/artist labels are links that open the entity in its own
       library through `OpenTopAlbumAsync`/`OpenTopArtistAsync`, which route a
       synthetic `DailyHistoryEntry` (`MakeStatHistoryEntry`) through the shared
       `OpenHistoryAlbumAsync`/`OpenHistoryArtistAsync` so local, remote, and
       Plex targets all work. Album and artist ranking rows both use a 40-px
       minimum height so the two side-by-side cards remain vertically aligned.
       Quick-access buttons must set both `HorizontalAlignment` and
       `HorizontalContentAlignment` to `Stretch`, so every action occupies the
       full inner width of its card.
- The side-by-side **Recently played** and **Recently added** surface-card
  headers carry a right-aligned **Show all** link
  (`DashboardBuildMediaSectionCard`). It opens a full-page view
  (`ShowAllRecentlyPlayedAsync` / `ShowAllRecentAlbumsAsync`) that reuses the
  dashboard scroll surface and shows up to 100 entries in a `WrapPanel` of the
  same cards (identical double-click / click behaviour). These views are
  pseudo-top-level tags `RecentlyPlayedAll` / `RecentAlbumsAll` handled in
  `ShowTopLevelViewAsync` (`BuildAllRecentlyPlayedViewAsync` /
  `BuildAllRecentAlbumsViewAsync`); opening one pushes the current state onto
  `_navigationStack`, so Back returns to the dashboard, and opening an album
  from a full-page view returns to that full-page view.
  `DashboardScrollViewer.SizeChanged` only re-flows the real dashboard
  (`_currentTopLevelTag == "Dashboard"`), never the full-page views. Dashboard
  album and recently-played thumbnails decode off the UI thread
  (`LoadDashboardLocalArtworkAsync`) so the 100-item views build without a
  hitch. Each 20-item strip has smoothly animated vector previous/next buttons
  in the header immediately before **Show all**. These buttons must not overlay
  or hide any artwork card, and their layout slots remain visible at both ends:
  unavailable directions are disabled and muted rather than hidden. Keep their
  compact size aligned with the **Show all** link and leave a clear gap before
  that link.
- Clicking a calendar day with playback opens a modal history table with
  playback time, media type, title, artist, album, listened duration, and total
  duration
- Local-track title links in daily history open Tracks, select the title, and
  start playback; album and artist links open their existing drill-down views.
  Daily-history cells without an available action must render as plain text, not
  disabled link-style buttons.
- Data comes from `GetRecentAlbums` (now also exposing `ArtistId`/`AddedAt` for
  cross-library merging), `GetDashboardLibrarySummary`,
  `GetTotalListeningSeconds`, `GetListeningTrend`, `GetCalendarData`,
  `GetTopGenres`, `GetTopAlbums`, and `GetTopArtists`. Listening and top-stat
  queries take an optional `sinceUnix` lower bound driven by the dashboard
  period selector.
- Dashboard calendar playback time includes local, remote Orynivo Server, and
  Plex tracks, podcasts, and internet radio. The Top genres / albums / artists
  statistics include local, remote Orynivo Server, and Plex tracks: the genre
  query uses `COALESCE(tracks.genre, play_history.genre)` over a `LEFT JOIN`,
  falling back to the genre captured at playback time (`ResolveNowPlayingGenre`)
  for tracks without a local library row; the album/artist aggregations merge by
  album title+artist / artist name and prefer a local match (album ID + artwork)
  as the representative identity, otherwise keep a representative external ID
  and path for remote/Plex resolution
- Clicking a dashboard genre opens Tracks with only that genre facet selected;
  other track filters are cleared and the genre filter section is expanded
- `RecentAlbumInfo` and `CalendarDayData` are records in `AudioDatabase.cs`
- `_dashboardYear`, `_dashboardMonth`, `_calendarInner`, and `_genreColors` hold
  dashboard state
- Month navigation rebuilds the dashboard so the section title changes
- Dashboard code uses `HorizontalAlignment` and `Brushes` from `Avalonia.Layout`
  and `Avalonia.Media`; no WPF namespaces are present

## Main Content Views

- **Artists**: distinct alphabetical artist list with table and artwork-card
  modes; double-click opens albums containing that artist
- **Albums**: normalized title-plus-physical-album-root list with favorite, 96
  px thumbnail, album, album artist, and year, plus a switchable artwork grid
  using 320 px thumbnails. Equal titles in different album roots are separate
  album records and open separate full detail headers and covers. Compilation
  tracks and conventional `CD1`/`CD2` sibling directories remain one album.
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
  sets must not be vertically centered. It uses `StyledProperty` for `ItemWidth`
  and `ItemHeight`
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
- Artist-filtered compilation details always retain the full album cover header,
  metadata, favorite/cover/playlist actions, and the **Show all album tracks**
  switch. CD/directory group headers appear below it only when the current
  filter produces more than one physical group. Enabling the switch includes
  every directory assigned to the album; disabling it restores the artist
  filter.
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
  Because local and remote numeric IDs can overlap, saved selection also carries
  the row's stable source key. Restoring top-level Artists or Albums must call
  the regular unified local/server view path and must never bind local
  `QueryRows` directly; search restoration likewise preserves its selected
  result source and outer result-page offset.
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
  manual DataGrid scrolling updates the active letter from the top visible row.
  For `DataGrid` views this update must use the DataGrid scrollbar's pixel
  offset plus the measured row height; do not walk the visual tree on every
  scroll event, because large unified local/server tables can otherwise stall
  input while scrolling.
- **Search**: delayed Lucene search returns separate themed Track, Album, and
  Artist sections, supports partial words and German normalization variants,
  sorts by score then display name, and preserves the original query across
  drill-down Back navigation. Global and server-scoped searches share one
  window-owned cancellation/version lifecycle. Text changes cancel immediately
  before the debounce delay; navigation, Settings, imports, window close, and
  catalog/profile changes abandon pending work. Capture source settings,
  favorites, and facet selections before loading. Local Lucene/SQLite work runs
  off the dispatcher while `LibrarySourceBatchLoader` admits at most three
  independent servers, with a separate source budget after admission. Full
  search uses strict HTTP/payload handling and retains per-source outcomes. Bind
  only the current version/generation; register remote playback rows after that
  guard on the dispatcher. Partial/failed searches show localized
  unavailable-source counts and **Try again**; empty categories never claim
  definitive no-match results when a source failed. Retry keeps usable rows and
  restores selection by category/source/ID plus outer scroll offset, with a
  deferred navigation guard. A selection requesting no sources completes empty
  without entering the source-backed catalog cache.
- **Folder structure**: configured library roots start expanded; child folders
  load lazily; double-clicking a track queues its direct folder sorted by disc,
  track number, and file name
- **Playlists**: display position, title, artist, album, and duration; sidebar
  entries open their live track list. Playlist tables include a first-column
  favorite heart and a source column immediately beside it.
  `MainWindow.Playlists.Loading.cs` snapshots regular mixed-playlist entries and
  batches local metadata off the dispatcher, closing SQLite before network work.
  `PlaylistTrackBatchLoader` deduplicates IDs per server and sends at most 200
  per request, with three concurrent requests and separate 20-second budgets
  after admission. Successful batches survive other failures. Recompose rows in
  stored order with distinct row objects and original entry IDs for duplicate
  occurrences. Missing/deleted/unavailable entries carry a localized title and
  opaque source context, without a playable path or track ID. They offer removal
  only. Failed sources offer **Try again**, preserving usable rows, selection by
  entry ID, and guarded deferred scroll restoration. Navigation and catalog
  changes cancel/reject pending publication and playback registration. Mobile
  browsing uses the same resolver without registration; mobile queue actions
  register on the dispatcher and reject changed catalog generations. Imported
  safe HTTP streams remain playable on desktop and omitted from mobile results.
  Smart criteria use the asynchronous resolver described below.
- **Smart playlists**: store JSON criteria instead of track rows, show an orange
  vector lightning icon using the same 13-px footprint and spacing as other
  sidebar icons, resolve live when opened, and do not permit manual entry
  removal. Criteria can include favourites, genre, format, bitrate, year,
  artist, album, duration, recently added/played windows, never played,
  playback-count ranges, alphabetical/random/recent/least-recent ordering, and a
  result limit. `MainWindow.SmartPlaylists.Loading.cs` captures criteria and
  source/profile/favorite context, loads local candidates off the dispatcher,
  and loads remote catalogs and strict similarity feature pages with at most
  three simultaneous source loads and 60-second budgets starting after
  admission. Sources excluded by criteria are skipped, except the similarity
  reference's source, which must still provide its seed. SQLite is closed before
  network awaits; matched local IDs are hydrated together after evaluation.
  Source failures retain available rows and the existing retry notice. A missing
  configured source is a failure, while a successful empty catalog is valid.
  `SmartPlaylistSimilarityProjection` maps both remote feature vectors and the
  persisted seed identity onto negative candidate IDs, retaining local IDs and
  every stored filter without modifying saved JSON. Provider-local ID collisions
  remain isolated. Only current version/generation results register playback
  metadata; mobile browsing shares resolution without registration. Retry
  preserves source/track selection and guarded scroll. Count previews use the
  same resolver without row hydration, report only complete current-generation
  counts, and display a seven-language unavailable message otherwise. Criteria
  changes cancel before debounce; dialog close cancels pending work. Obsolete
  completions and errors cannot replace current text. This path does not reuse
  tolerant feature caches or schedule opportunistic audio-feature analysis.
- **Save smart playlist**: available in the local and remote Orynivo Server
  Tracks views when facet filters are active **or** the search box holds a
  query; opens the compact `NewPlaylistDialog` and serializes the active
  favourite, search text, genre, format, bitrate, and source facets through the
  shared `BuildCurrentTrackFilterCriteria` helper. In the local Tracks view it
  calls `AudioDatabase.CreateSmartPlaylist`; in a remote Tracks view it calls
  `OrynivoServerClient.CreateSmartPlaylistAsync` so the smart playlist is
  persisted on that server.
- Tracks **search** honours the active facets: the source facet gates which
  sources contribute results (`ShowSearchResultsAsync` skips local when the
  source facet excludes it and the captured source snapshot skips servers not in
  the source set), while favourite/genre/format/bitrate filter the track section
  through the shared `MatchesTrackFilters` (`TrackFacetFilterSnapshot` shares
  live filter semantics and remote providers apply the captured client-side
  favourites). The same applies to the per-server
  `ShowOrynivoSearchResultsAsync`.
- Right-clicking a smart playlist in the sidebar exposes **Edit smart
  playlist**, which opens the shared `SmartPlaylistDialog` with the stored name
  and full criteria. Local smart playlists update `filter_criteria` through
  `AudioDatabase.UpdateSmartPlaylist`; remote Orynivo Server smart playlists
  update through `OrynivoServerClient.UpdateSmartPlaylistAsync` on the track's
  own server. Saving never replaces playlist rows.
- Right-clicking the Playlists accordion header exposes **Import M3U8
  playlist**. Imports are UTF-8, resolve relative local paths against the M3U8
  directory, preserve missing local paths, and retain HTTP/HTTPS entries. URLs
  containing user-info credentials or `X-Plex-Token` are skipped and never
  persisted.
- Right-clicking a regular playlist exposes **Export as M3U8**. Export writes
  UTF-8 without BOM, uses relative forward-slash local paths where possible,
  keeps HTTP/HTTPS entries, and skips credential-bearing URLs. Smart playlists
  are intentionally not exported as static M3U8 files.
- Tracks, albums, search results, and folder-tree nodes support playlist context
  menus

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
  `CheckBox`, so code that reads `IsChecked` is unchanged): DSD-to-PCM,
  equalizer enable, MCP server enable, AI chat enable, and the Appearance
  sidebar-visibility options. The toggle track is placed immediately before its
  label. The MCP per-tool entries stay on `SettingsCheckBoxTheme` because they
  form a permission checklist, not a single on/off switch.
- Interactive settings inputs (TextBox, ComboBox, NumericUpDown, buttons) share
  a consistent 30 px height.
- Subsystem availability is surfaced with `StatusBadge` controls (FFmpeg,
  Steinberg ASIO, cwASIO in the Output section; MCP in the MCP section). Each
  configured Orynivo Server / Plex server row additionally shows a live
  connection badge probed asynchronously so opening Settings stays instant.
  Reachable Orynivo Server rows also show the version returned by `/api/info`.

### Background artist profile write ownership

`EnsureArtistProfileAsync` delegates to an injectable download adapter and
reserves the window artist write queue before authoritative SQLite manual-state
readback. Download, profile persistence and off-thread artwork decoding share
that reservation. Original row binding, identity, language and background owner
remain required. Manual image observations independently reject old image
publication. Session freshness suppression and owner-specific loading cleanup
remain intact. This ordering is scoped to the window, not a SQLite/file
transaction or a cross-client coordinator.

### Remote artist context-menu image ownership

`OpenOrynivoArtistImageSearchAsync` uses an injectable selection adapter
retaining original binding, row identity, navigation and copied server
connection. Its manual action shares ownership with detail image actions. After
valid selection, it admits image intent and reserves the shared artist write
queue through primary upload and unified synchronization. Recheck ownership
after waiting and primary upload; obsolete work cannot begin fan-out or publish
row artwork/status. Completed writes invalidate catalog snapshots even when
publication is abandoned.

### Settings missing artist image acceptance

`MissingArtistImageWriter` retains shared artist admission through authoritative
state readback, file/SQLite work or source-only upload. Embedded Settings
injects the window queue and image-intent notification; standalone Settings owns
its own queue. Recheck run/source ownership and normalized artist identity after
waiting and readback. Skip manual, populated, unknown or obsolete targets
without replacing biography. Complete server connection copies retain personal
profile and streaming preferences. Eligibility is rechecked after preview
acceptance; provider search and review remain sequential and outside write
admission. Catalog invalidation follows admitted mutations, including
cancellation after local file publication. This is window-scoped ordering, not a
transaction or cross-client lock.

### Missing artist artwork synchronization ownership

`SynchronizeMissingArtistArtworkAsync` reserves the shared artist write queue
through authoritative source refresh, bounded byte capture and local/remote
mutations. Original copied source connections and enclosing artist-detail
ownership are retained before waiting. Recheck donor availability and source
identity; re-read each target before writing, skipping manual, populated,
renamed or unavailable identities. Preserve the target biography from current
metadata. Manual donor propagation notifies image intent and renews this
operation's own observation; later user changes still reject subsequent writes.
Catalog invalidation runs in finally after admitted changes, including
cancellation after file publication. This is window-scoped ordering, not a
transaction or cross-client lock.

### Artist image intent observation lifetime

`ArtistImageIntentOwnership` replaces last-target manual image epochs with
disposable, dispatcher-affine observations for live operations. Each observed
identity retains its rejection independently of later unrelated writes. Fixed
source-local IDs and normalized names match across libraries; no mutation
history survives completed observations. Profile loaders, background rows and
missing artwork copying dispose observations on every exit. Local profiles bind
resolved canonical metadata after readback without borrowing the previous
artist's name. Manual upload/deletion fan-out notifies exact local IDs on the
caller context before file/SQLite work, protecting profiles whose canonical name
is still unresolved. Manual donor copying renews only its own observation. The
pure helper is covered by tracked identity/lifetime tests and actual offline
overlap fixtures. Write ordering remains window-scoped.

Local artwork decoding opens read streams with deletion sharing. Atomic cache
replacement and variant cleanup can proceed while an existing reader finishes
decoding its original published bytes. Enclosing image guards still decide
whether that decoded result may publish.

### Artist detail Back scroll acceptance

Artist detail captures the offset of its named outer `ArtistInfoScrollViewer`
and exposes no selection from overlaid table rows. New unified artist admission
starts at zero. Restore routes schedule the saved offset after layout, retaining
the enclosing Back guard plus detail version, canonical name and visibility.
Metadata refresh within the same detail retains its position. Actual offline
Back-button fixtures cover source-aware origin selection/history, late
remote/Now Playing profiles, delayed upload/search/refresh selection, and
admitted image changes whose data may finish while UI publication is rejected. A
bounded headless scroll host checks the compiled artist scroll control and
current or obsolete deferred callbacks; MainWindow stays unshown and no playback
starts.

### Tracked Windows headless workflow suite

`Orynivo.Ui.Smoke` is a separate executable in the solution, invoked by local
full verification and Windows CI in Debug and Release. It seeds unique temporary
SQLite data before application creation, never shows MainWindow, injects
synthetic server transport and blocks external profile/image HTTP handlers.
Fixed workflow diagnostics contain no credentials or real catalog metadata.
Cached artist/Back scenarios include source-ID collision, deliberately late
metadata response, valid image selection after navigation and compiled outer
scroll layout. Reflection is confined to fixture adapters for the existing
window-owned navigation model; production gains no test navigation model. A
nonzero exit fails verification. The initial runtime contract is Windows-only.
Ten additional search groups exercise real search/retry/restore adapters with
synthetic source collisions, cancellation-insensitive late responses,
partial/full failure versus successful empty results, seven-language
presentation, retained retry rows, duplicate retry rejection and cache
invalidation. Search Back retains source selection and history; newly typed
queries reject obsolete restoration. Twelve remote album groups exercise both
open overloads and the existing album restore adapter: deliberately held
metadata must reject navigation/configuration/source changes before requesting
tracks, overlapping opens/restores retain only the latest owner, mutable row
fallbacks and logical IDs stay captured, and copied sources resolve their
configured owner. These remote metadata groups use empty overview/track replies.
Five local album groups hold dispatcher publication during reset, top-level or
configuration changes, reject obsolete rows/header/fade/Back updates, retain the
latest same-album publication and copy caller-owned logical IDs. A separate
populated synthetic journey searches for a remote album with a colliding local
ID, opens it through the existing row adapter, checks remote tracks/detail, and
uses the actual Back handler to restore search selection, source, surface and
history. It does not simulate pointer double-click dispatch or playback. Eight
album cache groups use the actual remote catalog adapter and process-owned disk
cache. Failed loads create no valid empty cache; full and empty recovery remain
distinct from failure. A valid snapshot avoids another catalog request while
applying current favorites. Changed library timestamps reject stale data;
corrupt files rebuild, missing payloads preserve validated bytes, and artwork
invalidation deletes the album list cache. Synthetic server stamps/responses
only. Five unified catalog recovery groups use actual binding and Retry
handlers. An isolated synchronization context observes the async-void Retry
lifetime through its finally block, including abandoned requests; no fixed sleep
stands in for completion. Tests retain partial rows/busy state, suppress
duplicate retries, recover after failure, reject navigation/cache/configuration
changes and retain newer admission during cancellation callbacks. All captured
gates release during teardown. Server endpoint integration and Linux/macOS UI
execution remain separate follow-ups.

### Track endpoint integration and personal state

LibraryEndpoints.MapLibraryEndpoints delegates track paging, by-ID resolution,
single detail and favorite writes to MapTrackCatalogEndpoints. Production uses
AudioDatabase.OpenDefault; an optional caller-owned connection factory permits
isolated TestServer fixtures. Every request disposes its own connection, and the
existing API-key/profile middleware remains required. Eight integration cases
exercise ordered disjoint pages, page bounds, missing IDs, rejected requests
before database access and overlapping profile reads after a favorite write.
GetTrackListPage and GetTrackListByIds overlay active-profile favorites and
ratings in SQLite, retaining existing legacy-column fallback and bounded paging.
Tests seed synthetic SQLite, use fixed synthetic keys, disable host logging and
release only their own SQLite pool. They do not start Program, hosted services,
watchers, scans, FFmpeg or a listening TCP port. Playlist and original-file
streaming integration are described below; generated-library measurements remain
open.

### Compact track profile consistency

All five compact track query methods (GetTrackList, GetTrackListByAlbum,
GetTrackListByPaths, GetTrackListByIds and GetTrackListPage) read active-profile
favorites and personal ratings directly in SQLite. Explicit false and zero
values override legacy columns; absent profile fields retain the established
legacy fallback. Four synthetic Core profile cases check standard, alpha, beta
and uninitialized profiles across all readers, including album artist filtering,
duplicate/missing path or ID input and existing page/order semantics. Other DTO
and entity query paths are outside this compact-track regression scope.

### Playlist endpoint integration

MapLibraryEndpoints delegates existing playlist routes to
MapPlaylistCatalogEndpoints. Its optional connection factory follows the same
per-request ownership and production OpenDefault contract as track mapping.
Seven TestServer cases verify regular order/duplicates and missing input IDs,
append/entry removal/deletion, shared playlists with request-profile track
favorites/ratings, client favorite overrides and matching resolve counts,
similarity vector resolution, invalid criteria/names and rejected mutations
before database access. Playlist definitions remain shared; personal track state
follows the request profile. Synthetic databases, fixed test keys and disabled
host logging only; no startup services or TCP listener. Original-file
range/cancellation and process-lifetime checks are described below; FFmpeg codec
validation remains separate.

### Original-file streaming integration

MapStreamEndpoints delegates audio routes to MapAudioStreamEndpoints; artwork
routes stay in the original mapper. The optional indexed-track connection
factory uses OpenDefault in production and temporary SQLite in TestServer.
Thirteen cases check full bytes/content type/length, closed/open/suffix ranges
with 206 and Content-Range, unsatisfiable 416, invalid/multipart full fallback,
missing tracks/files, invalid transcode options and authentication/profile
rejection before database access. A gated response stream observes the actual
file-result write token: after the write starts, client cancellation must reach
RequestAborted, cancel writing and unwind the server request. Diagnostic
timeouts are not latency budgets. Fixtures use synthetic opaque bytes without
decoding audio, listening TCP ports or FFmpeg. This proves original-file
cancellation through the in-memory pipeline, not FFmpeg process termination or a
physical network disconnect; those require separate evidence.

### Transcode process lifetime

StreamEndpoints uses Services.TranscodeProcessRunner.PipeAsync for process
start, stdout copy and natural exit. On every successfully started path, cleanup
requests entire-tree termination if the child is still alive and awaits parent
process exit without the already-cancelled request token. Already-exited races
are accepted; a live-child kill failure is not silently swallowed. The caller
retains Process disposal and response-stream ownership. Existing endpoint
diagnostics remain outside the runner.

Five tests execute a small managed child from the test-only TranscodeChild
project: natural output, cancellation during a gated write, write failure,
pre-cancellation and startup failure. They assert process exit before runner
completion and keep the borrowed output stream open. Parent teardown stops only
its own fixture process. The fixture is built/copied transitively by server
tests, not shipped with the server. This tests a real managed-child lifetime,
not FFmpeg codec behavior, descendant-process trees or a physical network
disconnect.

### Synthetic catalog measurements

scripts/CatalogBench generates production-schema metadata in an explicit owned
temporary SQLite directory, measures eight actual synchronous readers at 1,000
and 100,000 tracks, validates counts and records seven samples after one warmup.
Seeding is outside samples; no OpenDefault, UI, HTTP, scan or audio work runs.
Reports contain fixed phases, counts, runtime/OS/architecture/processor count,
elapsed samples and current-thread managed allocations, without source paths or
personal data. Allocation counts exclude native SQLite memory and process RSS.
Cleanup validates its temporary parent/prefix and clears only its own pools.
This opt-in tool is not a CI latency gate. The maintained methodology and dated
observations are in [CATALOG-MEASUREMENTS.md](CATALOG-MEASUREMENTS.md).

### Composed playback workflow verification

PlaybackSessionLifecycleTests composes production start/session ownership,
preparation/readiness, retirement/drain and GaplessQueueRefreshCoordinator with
controlled fake outputs. Four output-switch cases capture simulated current
positions 25/69 seconds in running/paused state, wait for held old disposal,
then seek and apply pause on the replacement before readiness. One queue-edit
case changes synthetic queue input during held seek; all callers join until a
second owned preparation captures the final queue and preserves position. Old
tokens cancel, each output is disposed once and audible-lifetime tasks remain
distinct from readiness. These checks do not initialize MainWindow, enumerate
hardware or produce physical audio. The maintained evidence matrix is
[WORKFLOW-TEST-COVERAGE.md](WORKFLOW-TEST-COVERAGE.md).
