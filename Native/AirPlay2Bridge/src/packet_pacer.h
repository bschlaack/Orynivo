// SPDX-License-Identifier: Apache-2.0
#pragma once

#include <chrono>
#include <cstdint>

namespace orynivo::airplay2 {

/** Paces RTP delivery on the negotiated realtime audio sample clock. */
class PacketPacer final {
public:
    /** Creates a clock for fixed-size audio packets at the supplied sample rate. */
    PacketPacer(std::uint32_t sampleRate, std::uint32_t framesPerPacket);

    /** Blocks until the packet with this zero-based index may be released. */
    void waitFor(std::uint64_t packetIndex);

    /** Returns the ideal release offset used for deterministic tests. */
    [[nodiscard]] std::chrono::microseconds releaseOffset(std::uint64_t packetIndex) const;

private:
    std::uint32_t sampleRate_;
    std::uint32_t framesPerPacket_;
    std::chrono::steady_clock::time_point started_{};
};

} // namespace orynivo::airplay2
