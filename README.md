# Orynivo

A native Windows music player and cross-platform music server for local Hi-Fi libraries.

ASIO/WASAPI · DSD/DSF/DFF · Gapless Playback · ReplayGain · Parametric EQ
Plex · Radio · Podcasts · AI Chat · MCP Server · Network Streaming

## Why Orynivo?

Orynivo is for people who still own and manage a local music library
and want a modern Windows player with serious audio output support —
and the ability to reach that library from any device on the local network.

- Bit-perfect/native DSD playback via ASIO
- Exclusive WASAPI and ASIO output profiles
- Gapless playback
- CUE sheet support
- ReplayGain and parametric EQ
- Local library, playlists, smart playlists and full-text search
- AI control via local LLMs, LM Studio/Ollama/OpenAI-compatible endpoints
- MCP server for external AI assistants
- **Orynivo Server** — headless cross-platform music server (Linux, macOS, Windows)
  that exposes the same library over the local network via REST and HTTP streaming

[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

A native Windows audio player with an Avalonia UI interface, local music
library, ASIO and WASAPI playback, and built-in AI control via an embedded
chat and an MCP server.

The application uses the Orynivo wordmark in the startup screen and sidebar,
plus a multi-resolution Windows application icon based on the standalone logo.

> This project is under active development. The database schema, user
> interface, and available features may still change.

## AI Integration

Orynivo includes two complementary AI interfaces that share the same 19
player-control, queue-management, and library tools.

### Embedded AI Chat

The **KI-Chat** sidebar view connects to any OpenAI-compatible LLM endpoint —
[LM Studio](https://lmstudio.ai/), [Ollama](https://ollama.com/), OpenAI,
Anthropic (via compatibility layer), or any custom `/v1/chat/completions`
provider. No external configuration file or MCP server is required: tools are
dispatched directly inside the application.

Responses stream token by token. The model calls tools autonomously — asking
*"Spiele alle Beatles-Alben"* makes it search the library, fill the queue with
the results, and start playback, all in one turn.

Configure the endpoint URL, optional API key, model name, and max-token limit
under **Settings → Integration → KI-Chat/AI Chat**. LM Studio and Ollama work
without an API key.

**Available tools:**

| Category | Tools |
| --- | --- |
| State | `get_now_playing`, `get_queue` |
| Playback | `play`, `pause_resume`, `next_track`, `previous_track`, `stop`, `seek`, `set_volume` |
| Queue | `queue_append`, `queue_play_next`, `clear_queue`, `replace_queue` |
| Library | `search_library` |
| Playlists | `list_playlists`, `get_playlist_tracks`, `create_playlist`, `create_smart_playlist` |
| History | `get_play_history` |

The model picks the right queue tool automatically: `replace_queue` clears the
old list and starts playing immediately when the user asks for new content;
`queue_append` adds to the existing queue when the user wants to add more;
`clear_queue` empties the queue without interrupting the current track.

### MCP Server

The same 19 tools are available as an embedded **Model Context Protocol (MCP)**
HTTP/SSE server for external AI assistants such as
[Claude Desktop](https://claude.ai/download). Enable it under
**Settings → Integration → MCP Server**, choose a port (default **49200**),
and point your assistant at `http://localhost:49200/mcp`. The server binds to
`localhost` only. Each of the 19 tools has an individual enable/disable toggle
in Settings so you can limit what an external assistant is allowed to do.

## Orynivo Server

`Orynivo.Server` is a self-contained headless music server that runs on
**Windows, Linux, and macOS**. It scans the same local library directories and
exposes them over the local network so any HTTP client — another Orynivo
instance, a media player, or a custom app — can browse and stream your music.

### API

All endpoints except `/api/health` require a pre-shared API key sent either as
an `X-Api-Key` header or a `?key=` query parameter. The query-parameter form
works directly in FFmpeg and browser URLs.

| Endpoint | Description |
| --- | --- |
| `GET /api/health` | Status — no authentication required |
| `GET /api/info` | Server name, version, and configured library paths |
| `GET /api/settings/library-paths` | Configured library root paths |
| `PUT /api/settings/library-paths` | Replace configured library root paths, persist them, refresh watchers, and start a scan |
| `GET /api/files/directories?path=` | Browse server-side directories for remote path selection |
| `POST /api/scan` | Trigger a full library scan |
| `GET /api/scan` | Scan status with current root, processed/total counts, current file, last result, and errors |
| `GET /api/artists` | All artists (id, name, favorite, biography/image flags) |
| `GET /api/artists/{id}` | Complete artist metadata, including cached biography/source fields |
| `POST /api/artists/{id}/profile` | Store client-refreshed artist biography/source fields and optional image bytes |
| `POST /api/artists/{id}/rename` | Rename one artist or merge it with a matching artist |
| `GET /api/artists/{id}/albums` | Albums for one artist |
| `GET /api/albums` | All albums (id, title, display artist, year, artwork paths) |
| `GET /api/albums/{id}/tracks` | Track list for one album |
| `GET /api/tracks` | Paginated track list (`?page=0&pageSize=500`) |
| `GET /api/tracks/{id}` | Full metadata for one track |
| `GET /api/tracks/{id}/lyrics` | Cached plain/synced lyrics for one track |
| `PUT /api/tracks/{id}/lyrics` | Store client-downloaded lyrics on the server |
| `GET /api/tracks/facets` | Lightweight facet rows (genre, format, bitrate) for the Tracks filter |
| `POST /api/tracks/by-ids` | Track rows for a list of track IDs (facet-filtered results) |
| `GET /api/folders/tracks` | Lightweight track rows for building a server library folder tree |
| `GET /api/artwork/album/{id}?size=96` | Album artwork thumbnail or original image |
| `PUT /api/artwork/album/{id}` | Store raw client-selected album artwork bytes on the server |
| `GET /api/artwork/artist/{id}` | Artist image stored on the server |
| `PUT /api/artwork/artist/{id}` | Store raw client-selected artist image bytes on the server |
| `GET /api/playlists` | All playlists (regular and smart) |
| `GET /api/playlists/{id}/tracks` | Resolved track list (smart playlists are evaluated live) |
| `POST /api/playlists` | Create a regular playlist from server-side track IDs |
| `POST /api/playlists/{id}/tracks` | Append server-side track IDs to a regular playlist |
| `DELETE /api/playlists/{id}` | Delete a playlist on the server |
| `DELETE /api/playlist-tracks/{id}` | Remove one entry from a server playlist |
| `GET /api/search?q=` | Full-text search — returns matching tracks |
| `GET /api/search/full?q=` | Category search — returns tracks, albums, and artists |
| `GET /api/stream/{trackId}` | Byte-range HTTP streaming for regular files; FLAC transcode for CUE virtual tracks |
| `GET /api/stream/path?p=` | Stream by absolute file path |
| `GET /api/artwork/album/{id}?size=` | Album artwork (`size=96` or `size=320` for thumbnails) |
| `GET /api/artwork/track?p=` | Track artwork by file path |
| `GET /api/artwork/track/{id}?size=` | Track artwork by track ID (`size=96` or `size=320` for thumbnails) |

### Configuration

Edit `appsettings.json` before first use:

```json
{
  "Orynivo": {
    "ServerName": "Orynivo Server",
    "ApiKey": "change-this-to-a-long-random-string",
    "LibraryPaths": ["/music", "/mnt/nas/music"],
    "ScanOnStartup": true
  }
}
```

The server binds to `http://0.0.0.0:5280` by default. Override the port in
`appsettings.json` under `Kestrel:Endpoints:Http:Url`.

When the Windows player is connected to an Orynivo Server, the server's music
directories can also be managed from the Orynivo Server connection dialog in
Settings → Library → Orynivo Server. The directory browser shows the server
filesystem, not the local Windows filesystem: Unix-like servers open at `/`,
while Windows servers expose their drive roots. The same dialog can start a
server scan and shows live progress while large directories are being scanned.
Inaccessible subdirectories such as Linux `lost+found` folders are skipped
instead of aborting the complete scan.
Configured Orynivo Server connections appear in the main sidebar inside the
Library section, below the Local media node.

### Running the server

**dotnet:**

```bash
dotnet run --project Orynivo.Server/Orynivo.Server.csproj
```

**Linux (after package install):**

```bash
# Edit config first
sudo nano /etc/orynivo-server/appsettings.json

# Enable and start the service
sudo systemctl enable --now orynivo-server

# Check logs
journalctl -u orynivo-server -f
```

FFmpeg must be installed separately on Linux and macOS and is required only for
CUE-sheet track transcoding. Regular audio files are served directly via
byte-range streaming without FFmpeg.

## Features

- Playback through ASIO or exclusive-mode WASAPI
- Automatic PCM down-conversion through `ffmpeg` when the source sample rate
  exceeds the selected ASIO or WASAPI device's capabilities; WASAPI uses the
  highest supported 32-bit float, 24-bit PCM, or 16-bit PCM output format
- Native stereo DSD playback for DSF and uncompressed DFF files through ASIO
- Real-time DSF/DFF-to-PCM conversion for playback through WASAPI, with the
  active conversion and PCM sample rate shown in the transport and status bar
- Optional forced DSF/DFF-to-PCM conversion with ASIO/cwASIO, allowing volume,
  ReplayGain, and the parametric equalizer to affect DSD sources
- PCM playback through `ffmpeg`
- Multiple named output profiles for quickly switching between configured
  output devices; a quick-pick popup in the transport bar selects the active
  profile without opening Settings
- Seeking, volume control, pause, and an editable persistent **Up next** queue
  with play-next/append actions, removal, reordering, playlist saving, and
  shuffle without repeating a track within the currently loaded queue
- Remote Orynivo Server tracks keep their library title, artist, album, and
  duration in transport metadata, play history, and **Up next**. Authenticated
  `?key=` stream URLs are not shown as titles and are not persisted in the
  playback queue.
- Remote Orynivo Server sidebar entries now appear in the main Library section,
  below the Local media node, and expose Artists, Albums, Tracks, and Folder
  structure below each server. The Local media node and each server node are
  individually collapsible. Remote Artists and Albums reuse the local
  table/artwork masks, while remote artwork is loaded lazily from authenticated
  server artwork endpoints and cached in the Windows client's local data
  directory. Track search uses the normal header search box and runs through
  the server's Lucene index.
- Remote Orynivo Server artist-info pages support renaming/merging artists and
  assigning Wikimedia artist images. The Windows client performs the image
  search and uploads the selected image to the server.
- Opening a remote server album from a selected artist initially scopes the
  album tracks to that artist, with the same checkbox used by local albums to
  show every track on the album.
- Playlists live under the Library sidebar: local playlists are grouped below
  Local, and each Orynivo Server exposes its own Playlists node populated from
  that server. Adding/removing tracks and creating/deleting regular playlists
  for remote server tracks writes to the selected server.
- Remote Orynivo Server artists, albums, and tracks can be marked as favorites;
  those favorite flags are stored only in the Windows client's settings.
- Remote Orynivo Server album covers and artist images can be searched from the
  Windows client; the client uploads the selected image bytes to the server, and
  the server stores them in its own artwork cache.
- Remote Orynivo Server artist biographies can be refreshed from the Windows
  client. Last.fm or Wikipedia requests run on the client; the server receives
  only the resulting biography, source URL, language, and optional image bytes
  to cache.
- Windows System Media Transport Controls integration with global media keys,
  play/pause/previous/next/stop and seek requests, system-overlay and lock-screen
  metadata, album art, playback state, and timeline synchronization
- Optional ReplayGain volume adjustment for PCM playback, using track or album
  gain metadata with fallback to the other available value; native DSD output
  remains bit-perfect
- Multiple named parametric PCM equalizers with one selected profile, a live
  frequency-response graph, editable preamp, dynamic filter rows, persisted
  on/off state, and Equalizer APO/AutoEQ text-profile import. Preamp, peak,
  low/high shelf, low/high pass, and `GraphicEQ` profiles are supported;
  changes are crossfaded during playback and native DSD output remains
  bit-perfect
- SQLite music library with multiple monitored directories
- CUE-sheet support for large FLAC/WAV images: indexed CUE entries appear as
  independent virtual tracks in library, folder, search, queue, playlist, and
  playback-history workflows while retaining the shared physical audio file
- Automatic recursive library monitoring with debounced create, update, rename,
  and delete handling, plus periodic full reconciliation as a safety net
- Metadata and embedded artwork extraction through TagLibSharp
- Artist, album, track, and folder views
- Resizable table columns whose widths are preserved separately for each
  library, search, playlist, Plex, radio, podcast, and history table
- Context-sensitive column selection by right-clicking a table header, including
  optional technical and tag metadata for local tracks and appropriate catalog
  fields for radio and podcasts
- Drag-and-drop table-column ordering persisted independently for each table
  and main-content view
- Space-saving accordion sections in the main sidebar, with configurable
  visibility and persisted independent expansion for library, personal radio,
  podcast, and playlist sections; the Internet Radio, Podcasts, and
  **Up Next** sidebar items can each be hidden independently in Settings
- Linked artist and album names for direct navigation to artist albums and
  album tracks
- Session-wide Back navigation across sidebar views, search results, dashboard
  links, artist/album drill-downs, playlists, podcasts, radio, folders, and Plex
  library views
- Conservative artist-name normalization for `feat.` credits and unambiguous
  case, accent, spacing, and punctuation variants, with a repair action for
  existing libraries
- Live A-Z/# quick navigation beside alphabetically sorted artist, album, and
  track lists
- Artist and album views with table and virtualized artwork modes, including
  Favorites-only filtering in both modes
- Dashboard with recently added albums, second-precision playback calendar,
  and linked top genres that open the matching filtered track list
- Clickable populated calendar days with a modal daily listening history;
  local title, album, and artist links open the corresponding library view,
  and title links immediately start playback
- Internet radio search through the free Radio Browser directory, direct
  playback, persistent personal stations in the sidebar, station logos, and
  live ICY title/artist metadata when supplied by the stream
- Multi-select genre filtering for radio search results using normalized
  station tags, with filter options built from the complete Radio Browser tag
  statistics rather than the first result page; selecting a genre runs a new
  server-side station query
- Podcast search through the public Apple Podcasts catalog, complete RSS/Atom
  episode lists sorted newest first, persistent pinned podcasts in the sidebar,
  category and feed-language filters, played/in-progress state, and automatic
  resume from the saved position
- Podcast detail cards with large artwork, feed description and metadata, and
  total, unheard, and started episode statistics
- A transport info view for the currently playing podcast episode with centered
  podcast artwork, publication data, duration, genre, and RSS summary
- Radio and podcast filter catalogs are shown before a search; after entering a
  search term, filter options and counts are recalculated from that result set
- Podcast category and language filters can be used without entering a title
- Lucene.NET full-text search with partial-word and German umlaut variants
- Favorites for tracks, albums, and artists
- Regular playlists and live smart playlists with metadata, library-age,
  playback-history, ordering, and result-limit criteria
- Smart playlists are created directly from active track filters and can be
  refined later through their sidebar context menu
- UTF-8 M3U8 import and export for regular playlists, including relative local
  paths, retained missing-file entries, and HTTP/HTTPS streams; credentialed
  Plex URLs are excluded
- Gapless sequential PCM playback through ASIO, cwASIO, and exclusive WASAPI:
  the next FFmpeg decoder is prefetched and handed to the existing output
  session without reopening the audio device
- Theme-aware table highlighting follows the currently audible track across
  library, search, playlist, radio, podcast, and Plex views
- Back navigation restores the previous selection and scroll position in album
  and artist table or artwork views after returning from a drill-down
- Album cover changes and artist metadata/image updates retain the current
  selection and list position
- Album track details provide an in-place favorite button alongside the album
  metadata. Album identity uses album title plus physical album root, so equal
  titles stored in different album folders have independent list entries,
  covers, and favorites. Compilations remain together, and conventional
  `CD1`/`CD2` or `Disc 1`/`Disc 2` subfolders appear as separate groups inside
  one multi-disc album detail view. Disc tables expand fully without their own
  scrollbars, and row selection does not move the outer page.
- Opening a compilation from an artist keeps the full album header visible,
  initially filters its tracks to that artist, and provides a switch to show
  every track across all assigned discs. Physical directory/disc headings are
  shown only when the current result contains multiple groups.
- Playback history for local tracks, podcast episodes, and internet-radio
  sessions, including position and completion state
- Artwork downloads through the Cover Art Archive and manual MusicBrainz
  search, with punctuation removed from album-title queries
- Embedded or downloaded lyrics with synchronized LRC highlighting during playback
- Manual LRCLIB lyrics search with editable title and artist, result preview,
  and explicit replacement of the cached lyrics
- Cached artist images and localized biographies from Wikipedia/Wikimedia
- Manual Wikimedia Commons artist-image search with editable search text
- Manually selected artist images are retained across profile refreshes, renames,
  and artist merges
- Artist renaming in the artist information view, including a transactional
  merge flow with an explicit choice of which artist profile to retain
- ZIP export and import for the managed library, playlists, personal radio
  stations, pinned podcasts, history, artwork, and configured library directories
- Light and dark themes
- German, English, French, and Spanish user interfaces
- Multiple Plex Media Server configurations with protected access tokens and
  music-library discovery, artist/album/track browsing, folder navigation, and
  playback, including an A–Z root-folder index and multi-part tracks decoded as
  one logical item
- Provider-neutral streaming interfaces with a prepared Qobuz configuration
  page for future approved partner API access
- Embedded **MCP server** (Model Context Protocol) that, when enabled under
  Settings > Integration, exposes 12 player-control and library-search tools
  to any MCP-compatible AI assistant (e.g. Claude Desktop). Tools cover
  playback control (`play`, `pause_resume`, `next_track`, `previous_track`,
  `stop`, `seek`, `set_volume`), queue management (`queue_append`,
  `queue_play_next`), and library lookup (`get_now_playing`, `get_queue`,
  `search_library`). The server binds to `localhost` only; the TCP port is
  configurable (default 49200)

## Supported Formats

The user interface recognizes, among others:

`DSF`, `DFF`, `FLAC`, `MP3`, `WAV`, `AIFF`, `M4A`, `AAC`, `OGG`, `Opus`,
`WMA`, and CUE sheets referencing PCM source files such as FLAC or WAV.

PCM formats are decoded by `ffmpeg`, which Orynivo downloads automatically on
first start if not already installed. Actual codec support depends on the build.
For CUE sheets, Orynivo uses `INDEX 01` boundaries to seek and stop FFmpeg
within the referenced source file; no temporary split files are created.
When WASAPI is selected, DSD audio in DSF or DFF containers is converted to PCM
in real time without creating a temporary file. PCM and converted DSD are
output at the highest supported endpoint sample rate that does not exceed the
source rate; if the endpoint exposes only higher rates, its lowest supported
rate is used. Unsupported sample rates and bit depths are converted by
`ffmpeg`.

## Requirements

### Windows player

- Windows 10 or Windows 11, x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for building)
- [FFmpeg](https://ffmpeg.org/) — downloaded automatically on first start if not
  already present. To use a specific build, place `ffmpeg.exe` and `ffprobe.exe`
  in `PATH` or next to `Orynivo.exe`.
- For cwASIO: Visual Studio 2022 with the **Desktop development with C++**
  workload and an installed ASIO driver
- Optional Steinberg bridge: Steinberg ASIO SDK 2.3

The MIT-licensed cwASIO sources are included under `third_party/cwasio`, so the
normal build provides ASIO support without the Steinberg SDK. The Steinberg
ASIO SDK is not included in the repository. The build script accepts its
location through `-AsioSdkDir` or the `ASIO_SDK_DIR` environment variable. It
also checks `third_party\asiosdk`, `external\asiosdk`, and, for compatibility
with older development environments, `C:\Dev\asiosdk_2.3`. When no SDK is
found, only **cwASIO** is offered. When the SDK is available, Settings offers
both **Steinberg ASIO** and **cwASIO**.

### Orynivo Server

- Linux, macOS, or Windows; x64 or ARM64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for building;
  not required when using a self-contained release package)
- [FFmpeg](https://ffmpeg.org/) — recommended for CUE-sheet track transcoding
  (Debian/Ubuntu: `apt install ffmpeg`; Fedora/Rocky: install from RPM Fusion)
- Artwork thumbnail generation uses the bundled SkiaSharp native Linux assets;
  ImageMagick or other external image conversion tools are not required.
- No ASIO drivers or Windows dependencies

## Download

Download the latest builds from [Releases](https://github.com/bschlaack/Orynivo/releases).

### Windows player

| Package | Description |
| --- | --- |
| `Orynivo-{version}-win-x64-Setup.exe` | Installer — Start Menu entry and uninstaller |
| `Orynivo-{version}-win-x64-Portable.zip` | Portable — extract anywhere and run `Orynivo.exe` |

Both packages are self-contained (.NET 8 bundled, no prerequisites).

### Linux server

| Package | Architecture |
| --- | --- |
| `orynivo-server_{version}_amd64.deb` | Debian / Ubuntu (x86-64) |
| `orynivo-server_{version}_arm64.deb` | Debian / Ubuntu (ARM64 / Raspberry Pi) |
| `orynivo-server-{version}-1.x86_64.rpm` | Fedora / Rocky / RHEL (x86-64) |
| `orynivo-server-{version}-1.aarch64.rpm` | Fedora / Rocky / RHEL (ARM64) |

All packages are self-contained (.NET 8 bundled). See the
[Server section](#orynivo-server) for post-install setup.

## Build

Clone the repository:

```powershell
git clone https://github.com/bschlaack/Orynivo.git
cd Orynivo
```

### Windows player

Create a debug build:

```powershell
.\build.ps1
```

The script builds the native x64 cwASIO bridge, optionally builds the Steinberg
bridge, and then builds the .NET application. It discovers Visual Studio through
`vswhere.exe` and falls back to `MSBuild.exe` from `PATH`.

Paths can be supplied without modifying project files:

```powershell
.\build.ps1 -AsioSdkDir 'D:\SDKs\asiosdk_2.3'
.\build.ps1 -Configuration Release
.\build.ps1 -Configuration Release -SkipAsio
.\build.ps1 -Configuration Release -SkipAsio -SkipCwAsio
```

For a persistent local setup, set `ASIO_SDK_DIR`. MSBuild discovery can
similarly be overridden with `-MSBuildPath` or `MSBUILD_EXE_PATH`.
`-RequireAsio` makes a missing Steinberg SDK fail the build. `-SkipAsio`
disables only the Steinberg bridge; `-SkipCwAsio` disables cwASIO.

### Orynivo Server

The server has no native dependencies and builds on any platform:

```bash
dotnet build Orynivo.Server/Orynivo.Server.csproj
dotnet run --project Orynivo.Server/Orynivo.Server.csproj
```

To publish a self-contained binary for a specific platform:

```bash
# Linux x64
dotnet publish Orynivo.Server/Orynivo.Server.csproj \
  --runtime linux-x64 --self-contained true --output out/linux-x64

# Linux ARM64
dotnet publish Orynivo.Server/Orynivo.Server.csproj \
  --runtime linux-arm64 --self-contained true --output out/linux-arm64
```

### Release builds (GitHub Actions)

Pushing a version tag triggers two parallel release workflows:

```powershell
git tag v0.14.0
git push origin v0.14.0
```

| Workflow | Runner | Output |
| --- | --- | --- |
| `release.yml` | Windows | `Orynivo-{v}-win-x64-Setup.exe`, `Orynivo-{v}-win-x64-Portable.zip` |
| `server-release.yml` | Ubuntu | `amd64`/`arm64` `.deb` and `x86_64`/`aarch64` `.rpm` packages |

Both workflows upload to the same draft GitHub Release. To trigger a release
without a tag, use **workflow dispatch** in the Actions tab.

## Run

```powershell
.\Orynivo\bin\Debug\net8.0-windows10.0.19041.0\Orynivo.exe
```

Library directories and the desired output device can then be configured in
Settings. Named output profiles allow saving multiple backend and device
combinations; a quick-pick popup on the transport bar switches between them
without opening Settings. ReplayGain can be disabled or switched to
track/album mode
under the output-device settings. The first subsequent scan of each configured
library root refreshes unchanged files once to import existing ReplayGain tags.
Equalizer APO or AutoEQ `.txt`/`.cfg` profiles can be imported in the same
section. `GraphicEQ` curves are translated into a log-frequency shelf cascade;
the imported parameters are stored directly in `settings.json`, so the source
profile file does not need to remain available. The same settings section plots
the combined response and exposes every filter as an editable row. Rows follow
the profile dynamically, and filters can be added or removed without
reimporting a file. Several named equalizers can be created and retained, while
the dropdown selects the only profile eligible for active playback. With no
selection, the editor and import controls remain hidden. Profiles can be
deleted after confirmation. Edits are previewed during active PCM playback.
The DSD playback option can force DSF/DFF files through this PCM path even when
ASIO/cwASIO native DSD is available.
Available library roots are monitored automatically after configuration.
File-system events are debounced before updating the database and search index;
periodic full scans reconcile changes that a watcher may have missed.

## Project Structure

```text
Orynivo/
├── Native/AsioBridge/       Native C++ bridge for the Steinberg ASIO SDK
├── Native/CwAsioBridge/     Native C++ bridge built against cwASIO
├── third_party/cwasio/      Vendored cwASIO sources under the MIT License
├── Orynivo.Core/            Cross-platform library (net8.0, no Windows deps)
│   ├── Audio/               FFmpeg decoder, equalizer, ReplayGain utilities
│   ├── Library/             SQLite database, scanner, Lucene search, models
│   └── Streaming/           Provider-neutral streaming contracts and models
├── Orynivo/                 Windows player (net8.0-windows, Avalonia UI)
│   ├── Audio/               ASIO, WASAPI, PCM, and DSD playback
│   ├── Controls/            Custom Avalonia controls
│   ├── Localization/        German, English, French, and Spanish resources
│   ├── Mcp/                 Embedded MCP server, player bridge, and tools
│   ├── Streaming/           Windows credential stores and Plex client
│   └── MainWindow.*         Main user interface and navigation
├── Orynivo.Server/          Cross-platform headless server (net8.0, ASP.NET Core)
│   ├── Endpoints/           REST and streaming endpoint handlers
│   ├── Middleware/          API key authentication
│   ├── Services/            Library scan and file-system watcher service
│   ├── Program.cs           Server entry point
│   └── appsettings.json     Default configuration
├── .github/
│   ├── server-release/      systemd unit and package scripts for Linux releases
│   └── workflows/           CI (dotnet-desktop.yml), Windows release, Server release
├── build.ps1                Builds native bridges and the Windows .NET application
└── Orynivo.sln              Visual Studio solution
```

## Local Data

Orynivo stores its local data under `%LOCALAPPDATA%\Orynivo\`:

- `settings.json`: application settings
- `streaming-credentials.dat`: Windows user-bound encrypted streaming secrets
- `plex-credentials.dat`: Windows user-bound encrypted Plex access tokens
- `library.db`: SQLite music library and playback history
- `logs\`: timestamped crash reports for unhandled application errors
- `artworks\`: original artwork and generated thumbnails
- `artist-images\`: cached Wikipedia/Wikimedia artist images
- `search-index\`: Lucene.NET search index
- `catalog-filter-cache.json`: cached radio genres and podcast categories/languages

These files are not part of the repository.

The note button in the transport bar replaces the current main content with a
large lyrics view. The current cover is shown dimmed in the background. Orynivo
first uses cached synchronized lyrics, then downloaded or embedded plain lyrics
as a fallback. Missing lyrics can be requested from the public LRCLIB API and
are stored in `library.db`; synchronized LRC lines are highlighted and kept in
view using the current playback position. The refresh button performs a new
lookup, and a missing result is shown directly in the lyrics view.
For WASAPI, buffered but not yet audible frames are excluded from the playback
position so synchronized lyrics follow the actual output timing.

The Artists page supports the same table/artwork modes as Albums. Profiles for
visible artists are loaded lazily in the selected UI language and cached in the
database and `artist-images\`. The stylized information button beside the
lyrics button opens the current artist profile in the main content area, with a
large image, biography, refresh action, and a link to the Wikipedia source.
Opening an album from an artist drill-down initially shows only that artist's
tracks. The album header provides a switch to show every track on the album.

The Settings library page can export this managed library data as a ZIP archive
and import it again. Audio files are intentionally not included; their existing
paths and the configured library directories are preserved in the backup. A
successful import rebuilds the search index and restarts the application state.
Exports show file-level progress and write to a temporary `.tmp` archive first;
the file is renamed to `.zip` only after the export completes successfully.
Imports use the same progress bar while extracting, validating, restoring
artwork, rebasing paths, and rebuilding the search index.

## Current Limitations

- Native, bit-perfect DSD playback is available only through ASIO. WASAPI can
  play DSF/DFF by converting the audio to PCM in real time.
- Builds without the optional ASIO bridge use WASAPI for playback and do not
  offer ASIO in Settings.
- Native DFF playback currently supports only uncompressed stereo files.
- DST-compressed DFF files are not played natively.
- Kernel Streaming is represented in the settings model but is not yet
  implemented.
- Qobuz catalog access and playback are not yet active. The application contains
  only the provider-neutral integration layer and settings scaffold; an approved
  Qobuz partner API contract and official endpoint documentation are still
  required.
- Plex browsing is paginated to keep very large libraries responsive. Playback
  availability depends on every selected Plex media part being directly
  accessible and decodable by the installed FFmpeg build. Unexpected HTTP
  stream termination is retried from the last decoded position before Orynivo
  advances to the next queue item.
- Renaming or merging artists updates Orynivo's internal library, album
  assignments, and search index. It does not modify tags in the audio files.
- ASIO devices may be unavailable for inspection or playback while another
  application holds them exclusively.
- Internet radio availability, metadata, and stream formats depend on the
  external station and the Radio Browser directory.
- Podcast search depends on the Apple Podcasts catalog. Episode availability
  and audio compatibility depend on each publisher's RSS/Atom feed and media
  enclosure.
- The Steinberg ASIO SDK must be obtained separately and supplied to the build
  script; it cannot be distributed with this repository.

## Contributing

Bug reports and reproducible improvement proposals can be submitted through
[GitHub Issues](https://github.com/bschlaack/Orynivo/issues). For audio issues,
include the output backend, device, file format, and sample rate.

## Dependencies and Notices

Orynivo uses components under MIT, Apache-2.0, LGPL-2.1-only, and other
compatible terms. This includes Avalonia, SkiaSharp, Lucene.NET,
Microsoft.Data.Sqlite, NAudio, TagLibSharp, SQLitePCLRaw, cwASIO, and their
transitive dependencies.

The complete attribution and redistribution information is maintained in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Applicable license texts are
provided in [`licenses/`](licenses/) and are copied into build and publish
outputs.

FFmpeg is run as a separate executable. If it is not installed, Orynivo
downloads the BtbN LGPL essentials build. FFmpeg remains subject to its own
license and is not covered by the Orynivo license.

ASIO is a trademark and software of Steinberg Media Technologies GmbH. The
optional Steinberg ASIO SDK is not included in this repository and must be
obtained and licensed separately. The vendored cwASIO implementation is an
independent MIT-licensed implementation.

## License

Copyright 2026 Björn Schlaack.

Orynivo's original source code and documentation are licensed under the
[Apache License 2.0](LICENSE). The license does not grant trademark rights in
the Orynivo name, wordmark, logo, or application icon.

Third-party components and the optional Steinberg ASIO SDK are excluded from
that grant and remain subject to their respective terms. See [NOTICE](NOTICE)
and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
