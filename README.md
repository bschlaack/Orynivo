# Player

A native Windows audio player with a WPF user interface, local music library,
and ASIO and WASAPI playback.

> This project is under active development. The database schema, user
> interface, and available features may still change.

## Features

- Playback through ASIO or exclusive-mode WASAPI
- Native stereo DSD playback for DSF and uncompressed DFF files through ASIO
- PCM playback through `ffmpeg`
- Seeking, volume control, pause, and an automatic playback queue
- SQLite music library with multiple monitored directories
- Metadata and embedded artwork extraction through TagLibSharp
- Artist, album, track, and folder views
- Album view with table and virtualized artwork modes
- Dashboard with recently added albums, playback calendar, and top genres
- Lucene.NET full-text search with partial-word and German umlaut variants
- Favorites for tracks, albums, and artists
- Regular and filter-based smart playlists
- Playback history used for statistics
- Artwork downloads through the Cover Art Archive and manual MusicBrainz search
- Light and dark themes
- German, English, and French user interfaces

## Supported Formats

The user interface recognizes, among others:

`DSF`, `DFF`, `FLAC`, `MP3`, `WAV`, `AIFF`, `M4A`, `AAC`, `OGG`, `Opus`, and
`WMA`.

PCM formats are decoded by the locally installed version of `ffmpeg`. Actual
codec support therefore also depends on that build.

## Requirements

- Windows 10 or Windows 11, x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 with the **Desktop development with C++** workload
- [FFmpeg](https://ffmpeg.org/) with `ffmpeg.exe` and `ffprobe.exe` in `PATH`
- Steinberg ASIO SDK 2.3 installed at `C:\Dev\asiosdk_2.3`
- An installed ASIO driver for ASIO playback

The ASIO SDK path is currently hard-coded in
`Native/AsioBridge/AsioBridge.vcxproj`. If the SDK is installed elsewhere,
adjust the include and source paths in that project file.

## Build

Clone the repository:

```powershell
git clone https://github.com/bschlaack/Player.git
cd Player
```

Create a debug build:

```powershell
.\build.ps1
```

The script builds the native x64 ASIO bridge first and then the WPF
application.

## Run

```powershell
.\Player\bin\Debug\net8.0-windows\Player.exe
```

Library directories and the desired output device can then be selected in the
settings window.

## Project Structure

```text
Player/
├── Native/AsioBridge/       Native C++ bridge for the Steinberg ASIO SDK
├── Player/
│   ├── Audio/               ASIO, WASAPI, PCM, and DSD playback
│   ├── Controls/            Custom WPF controls
│   ├── Library/             SQLite database, scanner, search, and artwork cache
│   ├── Localization/        German, English, and French resources
│   └── MainWindow.*         Main user interface and navigation
├── build.ps1                Builds the bridge and .NET application
└── Player.sln               Visual Studio solution
```

## Local Data

Player stores its local data under `%LOCALAPPDATA%\Player\`:

- `settings.json`: application settings
- `library.db`: SQLite music library and playback history
- `artworks\`: original artwork and generated thumbnails
- `search-index\`: Lucene.NET search index

These files are not part of the repository.

## Current Limitations

- Native DSD playback is available only through ASIO.
- Native DFF playback currently supports only uncompressed stereo files.
- DST-compressed DFF files are not played natively.
- Kernel Streaming is represented in the settings model but is not yet
  implemented.
- ASIO devices may be unavailable for inspection or playback while another
  application holds them exclusively.
- The build currently relies on fixed local paths for Visual Studio and the
  ASIO SDK.

## Contributing

Bug reports and reproducible improvement proposals can be submitted through
[GitHub Issues](https://github.com/bschlaack/Player/issues). For audio issues,
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
