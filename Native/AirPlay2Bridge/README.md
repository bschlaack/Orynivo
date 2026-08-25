# AirPlay2Bridge

`AirPlay2Bridge` is a standalone, Qt-free native sender component intended for
reuse by Orynivo and other applications. Its public boundary is the stable C ABI
in `include/airplay2_bridge.h`; application-specific UI, discovery, persistence,
and audio decoding remain outside the project.

## Current status

The initial milestone contains the ABI and a portable Windows/POSIX socket
transport with tests. It deliberately returns `AP2_NOT_IMPLEMENTED` after the
receiver transport connects: transient HAP pairing, encrypted RTSP/event
channels, ALAC/RTP audio, and metadata are not complete yet. Orynivo must not
load or advertise this bridge until a complete session passes real-receiver
tests.

The protocol and cryptographic port is based on the interoperability research
published by
[`airplay2-sender-cpp`](https://github.com/akustikrausch/airplay2-sender-cpp)
at commit `8c4034263f1c265d25b3cfb88a090624760ad22a`. Upstream source is not copied in
this milestone. When its Apache-2.0 crypto core is introduced, the pinned source,
NOTICE, and all applicable third-party notices must be shipped with the bridge.

## Build

```sh
cmake -S Native/AirPlay2Bridge -B build/airplay2bridge
cmake --build build/airplay2bridge --config Release
ctest --test-dir build/airplay2bridge -C Release --output-on-failure
```

The library has no Qt or Orynivo dependency. Windows links only `ws2_32`; POSIX
platforms use their system socket API.
