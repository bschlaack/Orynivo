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
- `AirPlay2Bridge` is an independently buildable Qt-free CMake project. Its
  exported surface is the C ABI in `include/airplay2_bridge.h`; keep C++ and
  platform socket types private so the component can be published separately.
  Every opaque session owns its sockets, copies caller configuration, reports
  stable result/state enums, and must fail closed on pairing proof errors.
  Transient pairing uses HAP mode 4 and never logs derived keys. Control framing
  authenticates the two-byte little-endian length, caps plaintext frames at
  1024 bytes, and maintains separate counters for each direction. Orynivo
  integration remains disabled until audible receiver timing, teardown, and
  failure handling pass real-device tests. The session sequence is encrypted
  `GET /info` followed by binary-plist session SETUP with a real bound timing
  port; the NTP responder must remain active through the audio session because
  receivers gate RECORD/stream SETUP on timing replies. Realtime stream SETUP
  advertises the bound UDP data/control ports and the first 32 bytes of the
  transient shared secret as `shk`. Response bodies and declared sizes remain
  bounded. The PCM ABI accepts stereo signed 16-bit data, buffers partial calls
  into 352-frame units, encodes with the pinned official Apple ALAC source, and
  emits authenticated realtime RTP as header, ciphertext/tag, and the explicit
  eight-byte nonce suffix. Emit the 20-byte NTP sync anchor before the first
  packet and every 100 packets. The bound RTP control socket owns a bounded
  sequence-indexed retransmit ring and answers type `0x55` requests with type
  `0x56` plus the original encrypted packet. Stop that responder before closing
  the socket and attempt encrypted `TEARDOWN` without making destruction fail.
  Keep codec and packetization behind private C++ types so the public C ABI
  stays independently reusable.

Consult the detailed ASIO/cwASIO build and playback rules in the root
`AGENTS.md` before modifying bridge code.
