// SPDX-License-Identifier: Apache-2.0
#pragma once

#include "alac_encoder.h"

#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace orynivo::airplay2 {

/** Converts fixed-size PCM blocks into encrypted AirPlay 2 realtime RTP datagrams. */
class RealtimeAudioPacketizer final {
public:
    /** Creates a packetizer with receiver session key and randomized RTP identity. */
    RealtimeAudioPacketizer(std::uint32_t sampleRate, std::span<const std::uint8_t> audioKey);

    /** Encodes and encrypts one complete 352-frame stereo PCM block. */
    [[nodiscard]] std::vector<std::byte> packetize(std::span<const std::byte> pcm);

private:
    AlacEncoder encoder_;
    std::vector<std::uint8_t> key_;
    std::uint16_t sequence_;
    std::uint32_t timestamp_;
    std::uint32_t ssrc_;
    bool first_ = true;
};

} // namespace orynivo::airplay2
