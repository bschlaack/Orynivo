// SPDX-License-Identifier: Apache-2.0
#include "alac_encoder.h"

#include <ALACAudioTypes.h>
#include <ALACBitUtilities.h>
#include <ALACEncoder.h>

#include <stdexcept>

namespace orynivo::airplay2 {

AlacEncoder::AlacEncoder(std::uint32_t sampleRate)
    : encoder_(std::make_unique<ALACEncoder>()),
      input_(std::make_unique<AudioFormatDescription>()),
      output_(std::make_unique<AudioFormatDescription>()) {
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
    output_->mFramesPerPacket = FramesPerPacket;
    output_->mChannelsPerFrame = Channels;

    encoder_->SetFrameSize(FramesPerPacket);
    encoder_->SetFastMode(true);
    if (encoder_->InitializeEncoder(*output_) != 0)
        throw std::runtime_error("Apple ALAC encoder initialization failed.");
}

AlacEncoder::~AlacEncoder() = default;
AlacEncoder::AlacEncoder(AlacEncoder&&) noexcept = default;
AlacEncoder& AlacEncoder::operator=(AlacEncoder&&) noexcept = default;

std::vector<std::byte> AlacEncoder::encode(std::span<const std::byte> pcm) {
    if (pcm.size() != PcmBytesPerPacket)
        throw std::invalid_argument("ALAC input must contain exactly 352 stereo PCM frames.");
    std::vector<std::byte> encoded(PcmBytesPerPacket * 2 + kALACMaxEscapeHeaderBytes);
    auto size = static_cast<std::int32_t>(PcmBytesPerPacket);
    const auto result = encoder_->Encode(*input_, *output_,
        reinterpret_cast<unsigned char*>(const_cast<std::byte*>(pcm.data())),
        reinterpret_cast<unsigned char*>(encoded.data()), &size);
    if (result != 0 || size <= 0 || static_cast<std::size_t>(size) > encoded.size())
        throw std::runtime_error("Apple ALAC packet encoding failed.");
    encoded.resize(static_cast<std::size_t>(size));
    return encoded;
}

} // namespace orynivo::airplay2
