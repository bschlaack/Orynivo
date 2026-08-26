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
  failure handling pass real-device tests. The Sonos-targeted session sequence
  uses encrypted `GET /info`, PTP session SETUP with `timingPeerInfo` and
  `timingPeerList`, `RECORD`, realtime stream type 96 SETUP, and `SETPEERS`.
  The sender is a unicast gPTP grandmaster on UDP 319/320,
  sends two-step Sync/Follow_Up plus Announce, and answers receiver Delay_Req.
  Realtime audio uses receiver-selected UDP ports, 352-frame uncompressed ALAC
  escape frames (`audioFormat=0x40000`, `ct=2`), ChaCha20-Poly1305, a dedicated
  zero-based packet nonce,
  bounded retransmission, and 28-byte PTP/RTP synchronization anchors. The
  captured type-103 TCP record framing stays isolated until buffered AAC is
  implemented; never advertise ALAC as the captured 1024-frame AAC format.
  Native AirPlay 2 realtime issues
  `RECORD` only after stream SETUP, with `Range: npt=0-` and `RTP-Info` containing
  the first packet's randomized sequence and latency-based timestamp. Realtime stream SETUP advertises
  only the bound UDP control port, never a sender data port. Audio `shk` and
  payload encryption use exactly the first 32 bytes of the pairing shared
  secret; control and event HKDF keys must never be substituted. Response bodies and declared sizes remain
  bounded. The PCM ABI accepts stereo signed 16-bit data, buffers partial calls
  into 352-frame units and wraps the samples in uncompressed ALAC escape frames
  while negotiating the AirPlay 2 ALAC profile (`audioFormat=0x40000`, `ct=2`). It
  emits authenticated realtime RTP as header, ciphertext/tag, and the explicit
  eight-byte nonce suffix. The audio nonce is a dedicated little-endian packet
  counter starting at zero and must remain independent of the randomized RTP
  sequence number. For NTP sessions the RTP SSRC equals the advertised
  `streamConnectionID`; some receivers accept SETUP but silently discard packets
  that use an unrelated random SSRC. Send explicit initial volume and RTP-anchored
  DMAP metadata before the first audio packet; Sonos may otherwise retain a valid
  stream in a silent state. Realtime NTP sessions begin on the deterministic
  66,150-frame latency timeline and report the receiver's initial `/feedback`
  stream count during manual probing. Emit the 20-byte NTP sync anchor before the first
  packet and every 100 packets. The bound RTP control socket owns a bounded
  sequence-indexed retransmit ring and answers type `0x55` requests with type
  `0x56` plus the original encrypted packet. Stop that responder before closing
  the socket and report its bounded datagram/request/resend counters in probe
  shutdown diagnostics. Attempt encrypted `TEARDOWN` without making destruction fail.
  The reverse event channel decrypts with `Events-Write-Encryption-Key`, encrypts
  responses with `Events-Read-Encryption-Key`, uses independent counters, and must
  answer every complete request with a bare encrypted RTSP 200 response.
  Session SETUP advertises `isMultiSelectAirPlay=true`; Sonos stereo-pair and
  grouped endpoints may otherwise accept timing while withholding audio.
  PCM delivery uses a 1.75-second pacing window: initial fill packets retain at
  least a 1 ms gap, then releases follow their exact sample-clock deadlines.
  The manual probe may generate its quiet test tone only when `--tone` is
  supplied explicitly; the default receiver probe must remain silent.
  Keep codec and packetization behind private C++ types so the public C ABI
  stays independently reusable.

Consult the detailed ASIO/cwASIO build and playback rules in the root
`AGENTS.md` before modifying bridge code.
