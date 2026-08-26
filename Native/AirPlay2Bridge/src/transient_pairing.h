// SPDX-License-Identifier: Apache-2.0
#pragma once

#include "socket_transport.h"

#include <cstdint>
#include <vector>

namespace orynivo::airplay2 {

/** Keys established by a successful fail-closed HAP transient pairing. */
struct TransientPairingKeys final {
    std::vector<std::uint8_t> sharedSecret;
    std::vector<std::uint8_t> controlWrite;
    std::vector<std::uint8_t> controlRead;
    std::vector<std::uint8_t> eventWrite;
    std::vector<std::uint8_t> eventRead;
    std::vector<std::uint8_t> audioKey;
};

/** Performs the Sonos-compatible HAP pair-setup M1-M4 exchange. */
class TransientPairing final {
public:
    /** Runs pairing over an already connected plaintext control socket. */
    static TransientPairingKeys run(Socket& control);
};

} // namespace orynivo::airplay2
