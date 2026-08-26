// SPDX-License-Identifier: Apache-2.0
#include "alac_encoder.h"

#include <ALACAudioTypes.h>
#include <ALACBitUtilities.h>
#include <ALACEncoder.h>

#include <stdexcept>

namespace orynivo::airplay2 {

AlacEncoder::AlacEncoder(std::uint32_t sampleRate, std::size_t framesPerPacket)
    : encoder_(std::make_unique<ALACEncoder>()),
      input_(std::make_unique<AudioFormatDescription>()),
      output_(std::make_unique<AudioFormatDescription>()),
      framesPerPacket_(framesPerPacket) {
    if (framesPerPacket_ == 0 || framesPerPacket_ > 4096)
        throw std::invalid_argument("ALAC packet size must contain between 1 and 4096 frames.");
    input_->mFormatID = kALACFormatLinearPCM;
    input_->mSampleRate = sampleRate;
    input_->mFormatFlags = kALACFormatFlagsNativeEndian | kALACFormatFlagIsSignedInteger;
    input_->mBytesPerPacket = Channels * sizeof(std::int16_t);
    input_->mFramesPerPacket = 1;
    input_->mBytesPerFrame = Channels * sizeof(std::int16_t);
    input_->mChannelsPerFrame = Channels;
    input_->mBitsPerChannel = sizeof(std::int16_t) * 8;

    output_->mFormatID = kALACFormatAppleLossless;
    output_->mSampleRate = sampleRate;
    output_->mFormatFlags = 1;
    output_->mFramesPerPacket = static_cast<std::uint32_t>(framesPerPacket_);
    output_->mChannelsPerFrame = Channels;

    encoder_->SetFrameSize(static_cast<std::uint32_t>(framesPerPacket_));
    encoder_->SetFastMode(true);
    if (encoder_->InitializeEncoder(*output_) != 0)
        throw std::runtime_error("Apple ALAC encoder initialization failed.");
}

AlacEncoder::~AlacEncoder() = default;
AlacEncoder::AlacEncoder(AlacEncoder&&) noexcept = default;
AlacEncoder& AlacEncoder::operator=(AlacEncoder&&) noexcept = default;

std::vector<std::byte> AlacEncoder::encode(std::span<const std::byte> pcm) {
    if (pcm.size() != pcmBytesPerPacket())
        throw std::invalid_argument("ALAC input does not match the configured packet size.");
    std::vector<std::byte> encoded(pcmBytesPerPacket() * 2 + kALACMaxEscapeHeaderBytes);
    auto size = static_cast<std::int32_t>(pcmBytesPerPacket());
    const auto result = encoder_->Encode(*input_, *output_,
        reinterpret_cast<unsigned char*>(const_cast<std::byte*>(pcm.data())),
        reinterpret_cast<unsigned char*>(encoded.data()), &size);
    if (result != 0 || size <= 0 || static_cast<std::size_t>(size) > encoded.size())
        throw std::runtime_error("Apple ALAC packet encoding failed.");
    encoded.resize(static_cast<std::size_t>(size));
    return encoded;
}

std::vector<std::byte> AlacEncoder::encodeUncompressed(std::span<const std::byte> pcm) const {
    if (pcm.size() != pcmBytesPerPacket())
        throw std::invalid_argument("ALAC input does not match the configured packet size.");

    std::vector<std::byte> encoded;
    encoded.reserve(pcmBytesPerPacket() + 4);
    std::uint8_t pending = 0;
    unsigned pendingBits = 0;
    auto writeBits = [&](std::uint32_t value, unsigned count) {
        for (unsigned bit = count; bit > 0; --bit) {
            pending = static_cast<std::uint8_t>((pending << 1) | ((value >> (bit - 1)) & 1U));
            if (++pendingBits == 8) {
                encoded.push_back(static_cast<std::byte>(pending));
                pending = 0;
                pendingBits = 0;
            }
        }
    };

    writeBits(1, 3);  // Stereo channel-pair element.
    writeBits(0, 4);  // Reserved.
    writeBits(0, 12); // Reserved.
    writeBits(0, 1);  // Packet uses the configured frame count.
    writeBits(0, 2);  // No wasted bytes.
    writeBits(1, 1);  // Uncompressed escape frame.
    for (std::size_t offset = 0; offset < pcm.size(); offset += sizeof(std::int16_t)) {
        const auto sample = static_cast<std::uint16_t>(std::to_integer<std::uint8_t>(pcm[offset])) |
                            static_cast<std::uint16_t>(
                                std::to_integer<std::uint8_t>(pcm[offset + 1]) << 8);
        writeBits(sample, 16);
    }
    writeBits(7, 3); // End element.
    if (pendingBits != 0)
        encoded.push_back(static_cast<std::byte>(pending << (8 - pendingBits)));
    return encoded;
}

} // namespace orynivo::airplay2
