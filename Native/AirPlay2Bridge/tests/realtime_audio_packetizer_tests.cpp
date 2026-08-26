// SPDX-License-Identifier: Apache-2.0
#include "realtime_audio_packetizer.h"

#include <array>
#include <cassert>
#include <cstddef>
#include <vector>

int main() {
    std::array<std::uint8_t, 32> key{};
    orynivo::airplay2::RealtimeAudioPacketizer packetizer(44100, key);
    const std::vector<std::byte> silence(orynivo::airplay2::AlacEncoder::PcmBytesPerPacket);
    const auto first = packetizer.packetize(silence);
    const auto second = packetizer.packetize(silence);
    assert(first.size() > 12 + 16 + 8);
    assert(first[0] == std::byte{0x80});
    assert(first[1] == std::byte{0xe0});
    assert(second[1] == std::byte{0x60});
    const auto firstSequence = static_cast<std::uint16_t>(std::to_integer<std::uint8_t>(first[2]) << 8 |
                                                         std::to_integer<std::uint8_t>(first[3]));
    const auto secondSequence = static_cast<std::uint16_t>(std::to_integer<std::uint8_t>(second[2]) << 8 |
                                                          std::to_integer<std::uint8_t>(second[3]));
    assert(static_cast<std::uint16_t>(firstSequence + 1) == secondSequence);
    return 0;
}
