# Third-Party Notices

Orynivo uses the components listed below. These components are not relicensed
under Orynivo's Apache License 2.0; their respective licenses continue to
apply. Package versions are the versions referenced by `Orynivo/Orynivo.csproj`
and its resolved dependency graph.

## MIT-licensed components

The MIT License text is provided in `licenses/MIT.txt`. Copyright notices and
upstream license files remain available from the linked projects and NuGet
packages.

- [Avalonia 11.2.0](https://github.com/AvaloniaUI/Avalonia), including
  Avalonia Desktop, Win32, X11, FreeDesktop, Skia, Native, themes, fonts,
  controls, DataGrid, diagnostics, remote protocol, and related native assets.
- [SkiaSharp 2.88.9](https://github.com/mono/SkiaSharp), including its native
  assets. SkiaSharp's package also contains notices for software used by
  SkiaSharp. The build copies those notices to
  `licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt`.
- [Microsoft.Data.Sqlite 9.0.5](https://github.com/dotnet/efcore) and
  Microsoft.Data.Sqlite.Core.
- [NAudio 2.3.0](https://github.com/naudio/NAudio), including NAudio.Core,
  NAudio.Asio, NAudio.Wasapi, NAudio.WinMM, NAudio.Midi, and NAudio.WinForms.
- [.NET runtime libraries](https://github.com/dotnet/runtime), including
  System.Security.Cryptography.ProtectedData, System.IO.Pipelines,
  System.Memory, and Microsoft.Extensions support libraries.
- [cwASIO](https://github.com/s13n/cwASIO), copyright 2024 Stefan Heinzmann.
  The vendored revision and its original license are recorded in
  `third_party/cwasio/UPSTREAM.md` and `third_party/cwasio/LICENSE`.
- Other resolved MIT dependencies include HarfBuzzSharp, MicroCom.Runtime,
  Tmds.DBus.Protocol, and their applicable native asset packages.

## Apache License 2.0 components

The complete Apache License 2.0 text is the repository's `LICENSE` file.

- [Apache Lucene.NET 4.8.0-beta00017](https://lucenenet.apache.org/),
  copyright 2006-2024 The Apache Software Foundation. This product includes
  software developed at The Apache Software Foundation. Its package NOTICE is
  copied to `licenses/Lucene.NET-NOTICE.txt`.
- [J2N 2.1.0](https://github.com/NightOwl888/J2N).
- [SQLitePCLRaw 2.1.10](https://github.com/ericsink/SQLitePCL.raw), including
  the core, provider, bundle, and native e_sqlite3 packages. SQLite itself is
  dedicated to the public domain; see https://sqlite.org/copyright.html.

## TagLibSharp

[TagLibSharp 2.3.0](https://github.com/mono/taglib-sharp) is licensed under the
GNU Lesser General Public License, version 2.1 only (LGPL-2.1-only). The
complete license text is provided in `licenses/LGPL-2.1.txt`.

Orynivo consumes TagLibSharp as a separate managed assembly. Orynivo does not
modify TagLibSharp. The exact corresponding source is available from the
upstream repository and the source link recorded by its NuGet package:

https://github.com/mono/taglib-sharp/tree/TaglibSharp-2.3.0.0

Recipients may replace the separately distributed TagLibSharp assembly with a
compatible modified build, subject to the LGPL and runtime compatibility.

## FFmpeg

[FFmpeg](https://ffmpeg.org/) is not linked into Orynivo. Orynivo launches the
separate `ffmpeg` and `ffprobe` executables as child processes. When those
executables are absent, Orynivo downloads the BtbN
`win64-lgpl-essentials` build from:

https://github.com/BtbN/FFmpeg-Builds

FFmpeg is primarily licensed under LGPL-2.1-or-later, but the exact license of
a binary depends on its build configuration and enabled external libraries.
Orynivo deliberately requests the LGPL essentials build. The downloaded
archive includes FFmpeg's applicable license information. Corresponding FFmpeg
source and build scripts are available from:

- https://ffmpeg.org/download.html
- https://github.com/BtbN/FFmpeg-Builds

FFmpeg is an independent work and is not covered by Orynivo's Apache License.

## Steinberg ASIO

ASIO is a trademark and software of Steinberg Media Technologies GmbH.

The optional `Native/AsioBridge` target can be built against a separately
obtained Steinberg ASIO SDK. The SDK is not included in this repository and is
not covered by Orynivo's Apache License. Anyone building or distributing that
optional bridge must obtain the SDK from Steinberg and comply with the chosen
Steinberg ASIO SDK license and trademark usage guidelines.

The independently implemented cwASIO backend described above does not include
the Steinberg ASIO SDK and is available under the MIT License.
