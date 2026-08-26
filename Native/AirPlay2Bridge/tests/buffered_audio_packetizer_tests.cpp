// SPDX-License-Identifier: Apache-2.0
#include "buffered_audio_packetizer.h"

#include <array>
#include <cassert>
#include <cstddef>
#include <cstdint>
#include <vector>

int main() {
    std::array<std::uint8_t, 32> key{};
    orynivo::airplay2::BufferedAudioPacketizer packetizer(44100, key, 0xac44f1a3, 0x4eb9e585);
    std::vector<std::byte> pcm(orynivo::airplay2::BufferedAudioPacketizer::PcmBytesPerPacket);
    const auto packet = packetizer.packetize(pcm);
    const auto declared =
        std::to_integer<std::uint32_t>(packet[0]) << 24 |
        std::to_integer<std::uint32_t>(packet[1]) << 16 |
        std::to_integer<std::uint32_t>(packet[2]) << 8 |
        std::to_integer<std::uint32_t>(packet[3]);
    assert(declared == packet.size());
    assert(packet[8] == std::byte{0x4e} && packet[9] == std::byte{0xb9});
    assert(packet[14] == std::byte{0xac} && packet[17] == std::byte{0xa3});
    for (std::size_t index = packet.size() - 8; index < packet.size(); ++index)
        assert(packet[index] == std::byte{0});
    assert(packetizer.nextTimestamp() == 0x4eb9e985);
    assert(packetizer.packetCount() == 1);
}
