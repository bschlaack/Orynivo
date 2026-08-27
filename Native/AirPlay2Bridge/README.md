# AirPlay2Bridge

`AirPlay2Bridge` is a standalone, Qt-free native sender component intended for
reuse by Orynivo and other applications. Its public boundary is the stable C ABI
in `include/airplay2_bridge.h`; application-specific UI, discovery, persistence,
and audio decoding remain outside the project.

## Current status

> **Experimental:** This sender is suitable for interoperability testing and
> Orynivo's experimental AirPlay 2 output, but it is not yet a general
> compatibility guarantee for every AirPlay 2 receiver. Continuous playback,
> seeking, and clean stereo output have been verified on the tested Sonos stereo
> pair. Other device models, firmware revisions, grouped topologies, and network
> conditions still require real-device validation.

The current milestone contains the ABI, portable Windows/POSIX sockets,
fail-closed HAP transient pairing, authenticated encrypted control, binary-plist
session negotiation, event-channel handling, a unicast gPTP grandmaster, and a
native realtime type-96 ALAC path. PCM is collected into 352-frame blocks,
wrapped in the uncompressed ALAC escape-frame representation used by the pinned
sender reference, authenticated with the pairing-derived
audio key, and sent over the receiver-negotiated UDP channel. Session negotiation
registers timing peers; 28-byte PTP/RTP synchronization anchors place the media
timeline on the same clock. The captured type-103 TCP record framing remains an
isolated, tested transport component for later buffered-AAC work. Best-effort
encrypted teardown and the native transports are
covered by tests. The reverse event socket uses its
independent HAP keys to answer receiver requests with encrypted RTSP success
responses. Negotiation through the receiver's
media ports has been verified against a Sonos AirPlay 2 receiver. Realtime RTP now
uses the negotiated stream connection ID as its NTP-session SSRC so the receiver
can associate the encrypted packets with the prepared stream. It also supplies
initial volume and RTP-anchored DMAP track metadata before PCM delivery because
Sonos may withhold an otherwise valid stream until metadata arrives. Receiver
volume defaults to a probe-safe -20 dB and can be selected before session start
through the C ABI; Orynivo uses 0 dB and applies its volume to PCM. Caller-supplied
title, artist, and album replace the diagnostic probe labels. Optional JPEG or
PNG cover bytes are copied during session creation, bounded to 8 MiB, and sent
best-effort so artwork rejection cannot prevent playback. The initial
RTP/NTP mapping uses a deterministic 66,150-frame receiver-latency line, and the
probe reports the receiver's active-stream count returned by `/feedback`.
Authenticated reverse-event requests are acknowledged and
`sendMediaRemoteCommand` values for Play, Pause, Next, and Previous are exposed
through the optional C-ABI callback. Receiver-originated absolute seeking is a
separate DACP concern and is not implemented by this bridge yet.
Long-running sessions continue sending authenticated `/feedback` keepalives
once per second; their worker is owned and stopped by the native session.
Realtime encryption uses a dedicated little-endian audio nonce counter starting
at zero, independent of the randomized RTP sequence number. Native realtime
SETUP is followed by a timeline-anchored `RECORD` carrying the first packet's
sequence and RTP timestamp. SETUP does not advertise a sender data port; it uses
the destination returned by the receiver.
The captured PTP/RTP media anchor is emitted before the first packet and then
approximately once per sample-rate second. Realtime packets follow the media
sample clock from the beginning; receiver buffering is represented once in the
anchor timeline rather than duplicated by a fast sender prefill.
The active realtime media format is stereo 16-bit ALAC
(`audioFormat=0x40000`, `ct=2`, `spf=352`). It requires UDP ports 319 and 320
for gPTP in addition to the encrypted RTSP and receiver-selected UDP audio ports.
Session SETUP advertises multi-select AirPlay capability for grouped receivers,
including Sonos stereo pairs.
The complete control, timing, and encrypted media path has produced continuous,
clean output on a Sonos stereo pair, including playback after seeking. Broader
receiver interoperability, event processing, and metadata behavior remain
experimental and require additional real-device testing.

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
Passing the explicit final `--tone` option sends a three-second 440 Hz
stereo test signal through the complete ALAC/type-96 PTP path. It is never enabled by
default, so ordinary handshake probes remain silent.
The tone probe uses a safe -20 dB receiver volume and a bounded signal. On
shutdown it reports RTP, retransmission, and PTP delay-request totals;
a successful UDP write alone does not prove that the receiver decrypted or
rendered the audio.
