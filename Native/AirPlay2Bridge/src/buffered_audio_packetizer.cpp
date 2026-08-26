// SPDX-License-Identifier: Apache-2.0
#include "buffered_audio_packetizer.h"

#include "airplay_crypto.h"

#include <algorithm>
#include <array>
#include <stdexcept>

namespace orynivo::airplay2 {
namespace {
void writeBigEndian32(std::byte* destination, std::uint32_t value) {
    destination[0] = static_cast<std::byte>(value >> 24);
    destination[1] = static_cast<std::byte>(value >> 16);
    destination[2] = static_cast<std::byte>(value >> 8);
    destination[3] = static_cast<std::byte>(value);
}
} // namespace

BufferedAudioPacketizer::BufferedAudioPacketizer(
    std::uint32_t sampleRate,
    std::span<const std::uint8_t> audioKey,
    std::uint32_t streamConnectionId,
    std::uint32_t initialTimestamp)
    : encoder_(sampleRate, FramesPerPacket), key_(audioKey.begin(), audioKey.end()),
      timestamp_(initialTimestamp), streamConnectionId_(streamConnectionId) {
    if (sampleRate == 0) throw std::invalid_argument("AirPlay sample rate must not be zero.");
    if (key_.size() != 32) throw std::invalid_argument("AirPlay audio key must contain 32 bytes.");
    if (streamConnectionId_ == 0)
        throw std::invalid_argument("AirPlay stream connection ID must not be zero.");
}

std::vector<std::byte> BufferedAudioPacketizer::packetize(std::span<const std::byte> pcm) {
    if (pcm.size() != PcmBytesPerPacket)
        throw std::invalid_argument("Buffered AirPlay input must contain exactly 1024 stereo PCM frames.");

    const auto encoded = encoder_.encode(pcm);
    // A working Apple sender uses a four-byte total-length prefix and a
    // fourteen-byte buffered-media header. Unlike realtime RTP, the reliable
    // channel leaves its sequence fields zero and advances only the media time.
    std::array<std::byte, 14> header{};
    writeBigEndian32(header.data() + 4, timestamp_);
    writeBigEndian32(header.data() + 10, streamConnectionId_);

    fxchain::airplay::Bytes plain(encoded.size());
    std::transform(encoded.begin(), encoded.end(), plain.begin(),
                   [](std::byte value) { return std::to_integer<std::uint8_t>(value); });
    fxchain::airplay::Bytes aad(10);
    std::transform(header.begin() + 4, header.end(), aad.begin(),
                   [](std::byte value) { return std::to_integer<std::uint8_t>(value); });
    const auto nonce = fxchain::airplay::counterNonce8(packetCount_);
    const auto encrypted = fxchain::airplay::chacha20Poly1305Encrypt(key_, nonce, plain, aad);

    const auto total = 4U + static_cast<std::uint32_t>(header.size() + encrypted.size() + nonce.size());
    std::vector<std::byte> frame(total);
    writeBigEndian32(frame.data(), total);
    std::copy(header.begin(), header.end(), frame.begin() + 4);
    std::transform(encrypted.begin(), encrypted.end(), frame.begin() + 4 + header.size(),
                   [](std::uint8_t value) { return static_cast<std::byte>(value); });
    std::transform(nonce.begin(), nonce.end(), frame.end() - nonce.size(),
                   [](std::uint8_t value) { return static_cast<std::byte>(value); });

    timestamp_ += static_cast<std::uint32_t>(FramesPerPacket);
    ++packetCount_;
    return frame;
}

} // namespace orynivo::airplay2
