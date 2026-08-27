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
    /**
     * Creates a packetizer for the negotiated receiver stream.
     * @param sampleRate PCM sample rate in frames per second.
     * @param audioKey Pairing-derived 32-byte realtime audio key.
     * @param streamConnectionId Negotiated stream identifier, also used as the NTP RTP SSRC.
     * @param initialTimestamp First RTP timestamp on the receiver's latency timeline.
     */
    RealtimeAudioPacketizer(
        std::uint32_t sampleRate,
        std::span<const std::uint8_t> audioKey,
        std::uint32_t streamConnectionId,
        std::uint32_t initialTimestamp);

    /** Converts one PCM block to network byte order and encrypts its realtime RTP payload. */
    [[nodiscard]] std::vector<std::byte> packetize(std::span<const std::byte> pcm);

    /** Returns the timestamp that will identify the next PCM packet. */
    [[nodiscard]] std::uint32_t nextTimestamp() const noexcept { return timestamp_; }

    /** Returns the sequence number that will identify the next RTP packet. */
    [[nodiscard]] std::uint16_t nextSequence() const noexcept { return sequence_; }

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
