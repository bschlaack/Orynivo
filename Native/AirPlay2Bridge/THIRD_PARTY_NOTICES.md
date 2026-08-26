# Third-party notices

AirPlay2Bridge configures the pinned Qt-free `airplay_crypto` target from
[`airplay2-sender-cpp`](https://github.com/akustikrausch/airplay2-sender-cpp),
commit `8c4034263f1c265d25b3cfb88a090624760ad22a`, under the Apache License 2.0.
It does not link the upstream Qt sender.

That target fetches [Mbed TLS](https://github.com/Mbed-TLS/mbedtls), licensed
under Apache-2.0, and includes an ed25519 implementation derived from
[orlp/ed25519](https://github.com/orlp/ed25519), licensed under the zlib license.
The fetched source trees retain their complete license and copyright files.
