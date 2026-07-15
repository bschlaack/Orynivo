# Native Bridge Instructions

This file applies to `Native/` and supplements `../AGENTS.md`.

## Completion

- Follow the root mandatory completion checklist.
- Validate the affected native project through `build.ps1`; use `-SkipAsio`
  only when the proprietary Steinberg SDK is unavailable.
- Build and documentation changes must preserve the CI path that always builds
  the vendored MIT-licensed cwASIO bridge without Steinberg SDK files.

## Native Invariants

- Keep Steinberg SDK material out of the repository and release artifacts.
- Preserve the shared export API used by `SteinbergAsioStream` for both
  `AsioBridge.dll` and `CwAsioBridge.dll`.
- In DSD mode, `preferredBufferSize` counts samples; `ASIOSTDSDInt8*` writes
  `preferredBufferSize / 8` bytes per channel.
- Native DSD remains bit-perfect and must not receive PCM volume, ReplayGain,
  boost, or equalizer processing.
- Capability queries may fail while another application owns the driver; such
  failures must remain recoverable.
- Do not change ring-buffer or callback ownership without checking both PCM and
  DSD paths in both bridge variants.

Consult the detailed ASIO/cwASIO build and playback rules in the root
`AGENTS.md` before modifying bridge code.
