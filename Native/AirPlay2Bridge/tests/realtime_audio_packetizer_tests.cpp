// SPDX-License-Identifier: Apache-2.0
#include "realtime_audio_packetizer.h"
#include "airplay_crypto.h"

#include <array>
#include <cassert>
#include <cstddef>
#include <vector>

int main() {
    std::array<std::uint8_t, 32> key{};
    constexpr std::uint32_t streamConnectionId = 0x12345678;
    constexpr std::uint32_t initialTimestamp = 66150;
    orynivo::airplay2::RealtimeAudioPacketizer packetizer(
        44100, key, streamConnectionId, initialTimestamp);
    std::vector<std::byte> pcm(orynivo::airplay2::AlacEncoder::PcmBytesPerPacket);
    pcm[0] = std::byte{0x34};
    pcm[1] = std::byte{0x12};
    const auto first = packetizer.packetize(pcm);
    const auto second = packetizer.packetize(pcm);
    assert(first.size() > 12 + 16 + 8);
    assert(first[0] == std::byte{0x80});
    assert(first[1] == std::byte{0xe0});
    assert(second[1] == std::byte{0x60});
    assert(first[8] == std::byte{0x12});
    assert(first[9] == std::byte{0x34});
    assert(first[10] == std::byte{0x56});
    assert(first[11] == std::byte{0x78});
    assert(first[4] == std::byte{0x00});
    assert(first[5] == std::byte{0x01});
    assert(first[6] == std::byte{0x02});
    assert(first[7] == std::byte{0x66});
    const auto firstSequence = static_cast<std::uint16_t>(std::to_integer<std::uint8_t>(first[2]) << 8 |
                                                         std::to_integer<std::uint8_t>(first[3]));
    const auto secondSequence = static_cast<std::uint16_t>(std::to_integer<std::uint8_t>(second[2]) << 8 |
                                                          std::to_integer<std::uint8_t>(second[3]));
    assert(static_cast<std::uint16_t>(firstSequence + 1) == secondSequence);
    for (std::size_t index = first.size() - 8; index < first.size(); ++index)
        assert(first[index] == std::byte{0});
    assert(second[second.size() - 8] == std::byte{1});
    for (std::size_t index = second.size() - 7; index < second.size(); ++index)
        assert(second[index] == std::byte{0});

    fxchain::airplay::Bytes aad(8);
    for (std::size_t index = 0; index < aad.size(); ++index)
        aad[index] = std::to_integer<std::uint8_t>(first[index + 4]);
    fxchain::airplay::Bytes encrypted(first.size() - 12 - 8);
    for (std::size_t index = 0; index < encrypted.size(); ++index)
        encrypted[index] = std::to_integer<std::uint8_t>(first[index + 12]);
    fxchain::airplay::Bytes nonce(8, 0);
    const fxchain::airplay::Bytes decryptionKey(key.begin(), key.end());
    const auto decrypted =
        fxchain::airplay::chacha20Poly1305Decrypt(decryptionKey, nonce, encrypted, aad);
    assert(decrypted.has_value());
    assert(!decrypted->empty());
    return 0;
}
