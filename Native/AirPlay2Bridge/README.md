# AirPlay2Bridge

`AirPlay2Bridge` is a standalone, Qt-free native sender component intended for
reuse by Orynivo and other applications. Its public boundary is the stable C ABI
in `include/airplay2_bridge.h`; application-specific UI, discovery, persistence,
and audio decoding remain outside the project.

## Current status

The current milestone contains the ABI, portable Windows/POSIX sockets,
fail-closed HAP transient pairing, the authenticated encrypted-control frame
codec, encrypted `GET /info`, binary-plist session SETUP, an NTP timing
responder, event-channel connection, `RECORD`, realtime audio-stream SETUP, the
official Apple ALAC encoder, partial-PCM buffering, and ChaCha20-Poly1305
encrypted realtime RTP packet transport. Initial and periodic NTP-backed RTP
anchors, a bounded retransmission responder, and best-effort encrypted teardown
are implemented and covered by native tests. Negotiation through the receiver's
RTP ports has been verified against a Sonos AirPlay 2 receiver. Receiver-side
audible playback still requires real-device verification of packet pacing and
the complete timing path; event processing and metadata are also incomplete.
Orynivo must not load or advertise this bridge until a complete session passes
real-receiver tests.

The protocol and cryptographic port is based on the interoperability research
published by
[`airplay2-sender-cpp`](https://github.com/akustikrausch/airplay2-sender-cpp)
at commit `8c4034263f1c265d25b3cfb88a090624760ad22a`. CMake fetches and builds only
that project's Qt-free Apache-2.0 `airplay_crypto` target, including its Mbed TLS
and ed25519 dependencies; the Qt sender is not linked. ALAC encoding comes from
Apple's official Apache-2.0 `macosforge/alac` source pinned in CMake. See
`NOTICE` and the repository `THIRD_PARTY_NOTICES.md`.

## Build

```sh
cmake -S Native/AirPlay2Bridge -B build/airplay2bridge
cmake --build build/airplay2bridge --config Release
ctest --test-dir build/airplay2bridge -C Release --output-on-failure
```

The library has no Qt or Orynivo dependency. Configuring a clean build requires
network access to fetch the pinned cryptographic and Apple ALAC sources and
their pinned dependencies. Windows additionally links `ws2_32`; POSIX
platforms use their system socket API.

For manual development against a receiver, enable the default probe target and
run `AirPlay2BridgeProbe <host> [port]`. The tool prints only lifecycle states
and sanitized errors; it never prints session keys or pairing material.
Passing the explicit final `--tone` option sends a quiet three-second 440 Hz
stereo test signal through the complete ALAC/RTP path. It is never enabled by
default, so ordinary handshake probes remain silent.
