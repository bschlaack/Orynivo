# Orynivo

[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

A native Windows audio player with an Avalonia UI interface, local music
library, and ASIO and WASAPI playback.

The application uses the Orynivo wordmark in the startup screen and sidebar,
plus a multi-resolution Windows application icon based on the standalone logo.

> This project is under active development. The database schema, user
> interface, and available features may still change.

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
- Seeking, volume control, pause, and an editable persistent **Up next** queue
  with play-next/append actions, removal, reordering, playlist saving, and
  shuffle without repeating a track within the currently loaded queue
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
  podcast, and playlist sections
- Linked artist and album names for direct navigation to artist albums and album tracks
- Session-wide Back navigation across sidebar views, search results, dashboard
  links, artist/album drill-downs, playlists, podcasts, radio, folders, and Plex
  library views
- Conservative artist-name normalization for `feat.` credits and unambiguous case, accent, spacing, and punctuation variants, with a repair action for existing libraries
- Live A-Z/# quick navigation beside alphabetically sorted artist, album, and track lists
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
- Artwork downloads through the Cover Art Archive and manual MusicBrainz search
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

- Windows 10 or Windows 11, x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [FFmpeg](https://ffmpeg.org/) — downloaded automatically on first start if not
  already present. To use a specific build instead, place `ffmpeg.exe` and
  `ffprobe.exe` in `PATH` or next to `Orynivo.exe`.
- For cwASIO: Visual Studio 2022 with the **Desktop development with C++**
  workload and an installed ASIO driver
- Optional for the separate Steinberg bridge: Steinberg ASIO SDK 2.3

The MIT-licensed cwASIO sources are included under `third_party/cwasio`, so the
normal build provides ASIO support without the Steinberg SDK. The Steinberg
ASIO SDK is not included in the repository. The build script accepts its
location through `-AsioSdkDir` or the `ASIO_SDK_DIR` environment variable. It
also checks `third_party\asiosdk`, `external\asiosdk`, and, for compatibility
with older development environments, `C:\Dev\asiosdk_2.3`. When no SDK is
found, only **cwASIO** is offered. When the SDK is available, Settings offers
both **Steinberg ASIO** and **cwASIO**.

## Build

Clone the repository:

```powershell
git clone https://github.com/bschlaack/Orynivo.git
cd Orynivo
```

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

GitHub Actions builds cwASIO and the managed Avalonia project in Debug and
Release. The Steinberg bridge remains excluded because its SDK is not stored in
the repository. Release artifacts therefore include `CwAsioBridge.dll`.

## Run

```powershell
.\Orynivo\bin\Debug\net8.0-windows10.0.19041.0\Orynivo.exe
```

Library directories and the desired output device can then be selected in the
settings view inside the main window. ReplayGain can be disabled or switched to track/album mode
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
├── Orynivo/
│   ├── Audio/               ASIO, WASAPI, PCM, and DSD playback
│   ├── Controls/            Custom Avalonia controls
│   ├── Library/             SQLite database, scanner, search, and artwork cache
│   ├── Localization/        German, English, French, and Spanish resources
│   ├── Streaming/           Provider-neutral catalog, playback, and credential contracts
│   └── MainWindow.*         Main user interface and navigation
├── build.ps1                Builds native bridges and the .NET application
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
