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

    /** Returns the timestamp that will identify the next PCM packet. */
    [[nodiscard]] std::uint32_t nextTimestamp() const noexcept { return timestamp_; }

    /** Returns how many RTP audio packets this instance has produced. */
    [[nodiscard]] std::uint64_t packetCount() const noexcept { return packetCount_; }

private:
    AlacEncoder encoder_;
    std::vector<std::uint8_t> key_;
    std::uint16_t sequence_;
    std::uint32_t timestamp_;
    std::uint32_t ssrc_;
    std::uint64_t packetCount_ = 0;
    bool first_ = true;
};

} // namespace orynivo::airplay2
