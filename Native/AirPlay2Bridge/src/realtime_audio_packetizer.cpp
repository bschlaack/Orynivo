// SPDX-License-Identifier: Apache-2.0
#include "realtime_audio_packetizer.h"

#include "airplay_crypto.h"

#include <algorithm>
#include <array>
#include <stdexcept>

namespace orynivo::airplay2 {
namespace {
std::uint32_t readRandom32() {
    const auto bytes = fxchain::airplay::randomBytes(4);
    return static_cast<std::uint32_t>(bytes[0]) << 24 |
           static_cast<std::uint32_t>(bytes[1]) << 16 |
           static_cast<std::uint32_t>(bytes[2]) << 8 | bytes[3];
}

void writeBigEndian32(std::byte* destination, std::uint32_t value) {
    destination[0] = static_cast<std::byte>(value >> 24);
    destination[1] = static_cast<std::byte>(value >> 16);
    destination[2] = static_cast<std::byte>(value >> 8);
    destination[3] = static_cast<std::byte>(value);
}
} // namespace

RealtimeAudioPacketizer::RealtimeAudioPacketizer(
    std::uint32_t sampleRate,
    std::span<const std::uint8_t> audioKey,
    std::uint32_t streamConnectionId,
    std::uint32_t initialTimestamp)
    : encoder_(sampleRate), key_(audioKey.begin(), audioKey.end()),
      sequence_(static_cast<std::uint16_t>(readRandom32())), timestamp_(initialTimestamp),
      ssrc_(streamConnectionId) {
    if (sampleRate == 0) throw std::invalid_argument("AirPlay sample rate must not be zero.");
    if (key_.size() != 32) throw std::invalid_argument("AirPlay audio key must contain 32 bytes.");
    if (timestamp_ == 0) throw std::invalid_argument("AirPlay initial RTP timestamp must not be zero.");
}

std::vector<std::byte> RealtimeAudioPacketizer::packetize(std::span<const std::byte> pcm) {
    const auto encoded = encoder_.encodeUncompressed(pcm);
    std::array<std::byte, 12> header{};
    header[0] = std::byte{0x80};
    header[1] = first_ ? std::byte{0xe0} : std::byte{0x60};
    header[2] = static_cast<std::byte>(sequence_ >> 8);
    header[3] = static_cast<std::byte>(sequence_);
    writeBigEndian32(header.data() + 4, timestamp_);
    writeBigEndian32(header.data() + 8, ssrc_);

    fxchain::airplay::Bytes plain(encoded.size());
    std::transform(encoded.begin(), encoded.end(), plain.begin(),
                   [](std::byte value) { return std::to_integer<std::uint8_t>(value); });
    fxchain::airplay::Bytes aad(8);
    std::transform(header.begin() + 4, header.end(), aad.begin(),
                   [](std::byte value) { return std::to_integer<std::uint8_t>(value); });
    const auto nonce = fxchain::airplay::counterNonce8(packetCount_);
    const auto encrypted = fxchain::airplay::chacha20Poly1305Encrypt(key_, nonce, plain, aad);

    std::vector<std::byte> packet(header.size() + encrypted.size() + nonce.size());
    std::copy(header.begin(), header.end(), packet.begin());
    std::transform(encrypted.begin(), encrypted.end(), packet.begin() + header.size(),
                   [](std::uint8_t value) { return static_cast<std::byte>(value); });
    std::transform(nonce.begin(), nonce.end(), packet.end() - nonce.size(),
                   [](std::uint8_t value) { return static_cast<std::byte>(value); });
    ++sequence_;
    timestamp_ += AlacEncoder::FramesPerPacket;
    ++packetCount_;
    first_ = false;
    return packet;
}

} // namespace orynivo::airplay2
