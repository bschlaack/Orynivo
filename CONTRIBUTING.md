# Contributing to Orynivo

Thanks for your interest in improving Orynivo!

## Before you start

- Report bugs and propose features through
  [GitHub Issues](https://github.com/bschlaack/Orynivo/issues). For audio
  output problems, include your OS, output profile (WASAPI/cwASIO/ASIO/ALSA/
  OpenAL), DAC model, and the file format/sample rate — that is usually
  everything needed to reproduce.
- For larger features, open an issue first so the direction can be discussed
  before you invest time.
- Pull requests target the **`development`** branch.

## Development setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Clone and build:

   ```bash
   dotnet restore Orynivo/Orynivo.csproj
   dotnet build Orynivo/Orynivo.csproj --configuration Release
   dotnet run --project Orynivo/Orynivo.csproj
   ```

3. Platform prerequisites (FFmpeg, ALSA/OpenAL runtimes, optional Steinberg
   ASIO SDK) are described in the
   [full reference](docs/FULL-FEATURE-REFERENCE.md#requirements).

## Running the tests

The managed test suites run on any platform and never touch your real music
library:

```bash
dotnet test Orynivo.Core.Tests/Orynivo.Core.Tests.csproj
dotnet test Orynivo.Tests/Orynivo.Tests.csproj
dotnet test Orynivo.Server.Tests/Orynivo.Server.Tests.csproj
```

On Windows, `pwsh -NoProfile -File scripts/verify-all.ps1` runs the same checks
as CI (builds with `--warnaserror`, all test projects, the headless UI smoke
suite, and the parity scripts). Please run it before requesting review.

## Pull request checklist

- All three test projects pass; new behavior gets a test where it is testable
  without real audio hardware.
- No warnings (`--warnaserror` is enforced in CI).
- User-visible strings go through the localization resources so all seven
  shipped languages can follow later — untranslated strings are fine in the PR.
- Never write to users' media files; metadata corrections stay library-only.
- Keep UI changes keyboard-accessible and readable in both themes.

## Good first issues

Look for issues labeled
[`good first issue`](https://github.com/bschlaack/Orynivo/labels/good%20first%20issue)
— help with documentation, localization quality, and UI polish is always
appreciated.

## License

By contributing, you agree that your contributions are licensed under the
[Apache License 2.0](LICENSE) like the rest of the project.
