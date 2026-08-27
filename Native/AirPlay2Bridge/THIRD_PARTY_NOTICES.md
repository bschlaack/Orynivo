# Third-party notices

AirPlay2Bridge configures the pinned Qt-free `airplay_crypto` target from
[`airplay2-sender-cpp`](https://github.com/akustikrausch/airplay2-sender-cpp),
commit `8c4034263f1c265d25b3cfb88a090624760ad22a`, under the Apache License 2.0.
It does not link the upstream Qt sender.

That target fetches [Mbed TLS](https://github.com/Mbed-TLS/mbedtls), licensed
under Apache-2.0, and includes an ed25519 implementation derived from
[orlp/ed25519](https://github.com/orlp/ed25519), licensed under the zlib license.
The fetched source trees retain their complete license and copyright files.

AirPlay2Bridge also fetches Apple's official
[`macosforge/alac`](https://github.com/macosforge/alac) codec at commit
`c38887c5c5e64a4b31108733bd79ca9b2496d987` and builds its encoder sources.
Apple publishes those sources under the Apache License 2.0; the fetched tree
retains `LICENSE` and the per-source copyright notices.
