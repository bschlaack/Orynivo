// SPDX-License-Identifier: Apache-2.0
#pragma once

#include <cstddef>
#include <cstdint>
#include <memory>
#include <span>
#include <vector>

class ALACEncoder;
struct AudioFormatDescription;

namespace orynivo::airplay2 {

/** Encodes fixed-size interleaved signed 16-bit PCM frames as Apple Lossless. */
class AlacEncoder final {
public:
    static constexpr std::size_t FramesPerPacket = 352;
    static constexpr std::size_t Channels = 2;
    static constexpr std::size_t PcmBytesPerPacket = FramesPerPacket * Channels * sizeof(std::int16_t);

    /** Creates an ALAC encoder for the supplied PCM sample rate and packet size. */
    explicit AlacEncoder(std::uint32_t sampleRate, std::size_t framesPerPacket = FramesPerPacket);
    ~AlacEncoder();
    AlacEncoder(AlacEncoder&&) noexcept;
    AlacEncoder& operator=(AlacEncoder&&) noexcept;
    AlacEncoder(const AlacEncoder&) = delete;
    AlacEncoder& operator=(const AlacEncoder&) = delete;

    /** Encodes exactly one 352-frame stereo signed 16-bit PCM packet. */
    [[nodiscard]] std::vector<std::byte> encode(std::span<const std::byte> pcm);

    /** Encodes exactly one packet as a receiver-compatible uncompressed ALAC escape frame. */
    [[nodiscard]] std::vector<std::byte> encodeUncompressed(std::span<const std::byte> pcm) const;

    /** Returns the configured number of PCM frames in one encoded packet. */
    [[nodiscard]] std::size_t framesPerPacket() const noexcept { return framesPerPacket_; }

    /** Returns the required byte count for one interleaved stereo PCM packet. */
    [[nodiscard]] std::size_t pcmBytesPerPacket() const noexcept {
        return framesPerPacket_ * Channels * sizeof(std::int16_t);
    }

private:
    std::unique_ptr<ALACEncoder> encoder_;
    std::unique_ptr<AudioFormatDescription> input_;
    std::unique_ptr<AudioFormatDescription> output_;
    std::size_t framesPerPacket_;
};

} // namespace orynivo::airplay2
