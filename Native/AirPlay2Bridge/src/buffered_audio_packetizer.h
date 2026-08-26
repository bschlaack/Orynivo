// SPDX-License-Identifier: Apache-2.0
#pragma once

#include "alac_encoder.h"

#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace orynivo::airplay2 {

/** Produces Apple-compatible type-103 ALAC frames for the reliable TCP audio channel. */
class BufferedAudioPacketizer final {
public:
    static constexpr std::size_t FramesPerPacket = 1024;
    static constexpr std::size_t PcmBytesPerPacket =
        FramesPerPacket * AlacEncoder::Channels * sizeof(std::int16_t);

    /** Creates a packetizer using a pairing-derived audio key and initial media timeline. */
    BufferedAudioPacketizer(std::uint32_t sampleRate,
                            std::span<const std::uint8_t> audioKey,
                            std::uint32_t streamConnectionId,
                            std::uint32_t initialTimestamp);

    /** Encodes and encrypts one complete PCM block including its TCP length prefix. */
    [[nodiscard]] std::vector<std::byte> packetize(std::span<const std::byte> pcm);

    /** Returns the timestamp assigned to the next buffered packet. */
    [[nodiscard]] std::uint32_t nextTimestamp() const noexcept { return timestamp_; }

    /** Returns the number of committed TCP audio frames. */
    [[nodiscard]] std::uint64_t packetCount() const noexcept { return packetCount_; }

private:
    AlacEncoder encoder_;
    std::vector<std::uint8_t> key_;
    std::uint32_t timestamp_;
    std::uint32_t streamConnectionId_;
    std::uint64_t packetCount_ = 0;
};

} // namespace orynivo::airplay2
