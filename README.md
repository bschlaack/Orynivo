# Orynivo

A native Windows audio player with a WPF user interface, local music library,
and ASIO and WASAPI playback.

The application uses the Orynivo wordmark in the startup screen and sidebar,
plus a multi-resolution Windows application icon based on the standalone logo.

> This project is under active development. The database schema, user
> interface, and available features may still change.

## Features

- Playback through ASIO or exclusive-mode WASAPI
- Native stereo DSD playback for DSF and uncompressed DFF files through ASIO
- PCM playback through `ffmpeg`
- Seeking, volume control, pause, an automatic playback queue, and shuffle
  without repeating a track within the currently loaded queue
- SQLite music library with multiple monitored directories
- Metadata and embedded artwork extraction through TagLibSharp
- Artist, album, track, and folder views
- Linked artist and album names for direct navigation to artist albums and album tracks
- Conservative artist-name normalization for `feat.` credits and unambiguous case, accent, spacing, and punctuation variants, with a repair action for existing libraries
- Live A-Z/# quick navigation beside alphabetically sorted artist, album, and track lists
- Album view with table and virtualized artwork modes
- Dashboard with recently added albums, playback calendar, and top genres
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
- Radio and podcast filter catalogs are shown before a search; after entering a
  search term, filter options and counts are recalculated from that result set
- Podcast category and language filters can be used without entering a title
- Lucene.NET full-text search with partial-word and German umlaut variants
- Favorites for tracks, albums, and artists
- Regular and filter-based smart playlists
- Playback history used for statistics
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
- Provider-neutral streaming interfaces with a prepared Qobuz configuration
  page for future approved partner API access

## Supported Formats

The user interface recognizes, among others:

`DSF`, `DFF`, `FLAC`, `MP3`, `WAV`, `AIFF`, `M4A`, `AAC`, `OGG`, `Opus`, and
`WMA`.

PCM formats are decoded by the locally installed version of `ffmpeg`. Actual
codec support therefore also depends on that build.

## Requirements

- Windows 10 or Windows 11, x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [FFmpeg](https://ffmpeg.org/) with `ffmpeg.exe` and `ffprobe.exe` in `PATH`
- Optional for ASIO: Visual Studio 2022 with the **Desktop development with
  C++** workload, Steinberg ASIO SDK 2.3, and an installed ASIO driver

The ASIO SDK is not included in the repository. The build script accepts its
location through `-AsioSdkDir` or the `ASIO_SDK_DIR` environment variable. It
also checks `third_party\asiosdk`, `external\asiosdk`, and, for compatibility
with older development environments, `C:\Dev\asiosdk_2.3`. When no SDK is
found, the application is built without the native bridge and ASIO is omitted
from the output-device selection.

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

The script builds the native x64 ASIO bridge first and then the WPF
application. It discovers a suitable Visual Studio installation through
`vswhere.exe` and falls back to `MSBuild.exe` from `PATH`.

Paths can be supplied without modifying project files:

```powershell
.\build.ps1 -AsioSdkDir 'D:\SDKs\asiosdk_2.3'
.\build.ps1 -Configuration Release
.\build.ps1 -Configuration Release -SkipAsio
```

For a persistent local setup, set `ASIO_SDK_DIR`. MSBuild discovery can
similarly be overridden with `-MSBuildPath` or `MSBUILD_EXE_PATH`.
`-RequireAsio` makes a missing SDK fail the build instead of using the
WASAPI-only fallback.

GitHub Actions verifies Debug and Release builds of the managed WPF project.
The native ASIO bridge is excluded from hosted CI because the separately
licensed ASIO SDK is not stored in the repository. Every successful Release
run uploads a framework-dependent `Orynivo-win-x64` Windows artifact.

## Run

```powershell
.\Orynivo\bin\Debug\net8.0-windows\Orynivo.exe
```

Library directories and the desired output device can then be selected in the
settings window.

## Project Structure

```text
Orynivo/
├── Native/AsioBridge/       Native C++ bridge for the Steinberg ASIO SDK
├── Orynivo/
│   ├── Audio/               ASIO, WASAPI, PCM, and DSD playback
│   ├── Controls/            Custom WPF controls
│   ├── Library/             SQLite database, scanner, search, and artwork cache
│   ├── Localization/        German, English, French, and Spanish resources
│   ├── Streaming/           Provider-neutral catalog, playback, and credential contracts
│   └── MainWindow.*         Main user interface and navigation
├── build.ps1                Builds the bridge and .NET application
└── Orynivo.sln              Visual Studio solution
```

## Local Data

Orynivo stores its local data under `%LOCALAPPDATA%\Orynivo\`:

- `settings.json`: application settings
- `streaming-credentials.dat`: Windows user-bound encrypted streaming secrets
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

- Native DSD playback is available only through ASIO.
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

- Lucene.NET 4.8.0-beta00017, Apache License 2.0
- Microsoft.Data.Sqlite 9.0.5, MIT License
- NAudio 2.3.0, MIT License
- TagLibSharp 2.3.0, LGPL-2.1
- SQLitePCLRaw / e_sqlite3, Apache License 2.0

ASIO is a trademark and software of Steinberg Media Technologies GmbH.

## License

No license file currently covers the original source code in this repository.
Until a license is added, no additional rights to use, modify, or distribute
that source code are granted. The dependencies listed above remain subject to
their respective licenses.
