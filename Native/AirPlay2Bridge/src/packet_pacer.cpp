// SPDX-License-Identifier: Apache-2.0
#include "packet_pacer.h"

#include <stdexcept>
#include <thread>

namespace orynivo::airplay2 {

PacketPacer::PacketPacer(std::uint32_t sampleRate, std::uint32_t framesPerPacket)
    : sampleRate_(sampleRate), framesPerPacket_(framesPerPacket) {
    if (sampleRate == 0 || framesPerPacket == 0)
        throw std::invalid_argument("Packet pacing requires a sample rate and packet size.");
}

std::chrono::microseconds PacketPacer::releaseOffset(std::uint64_t packetIndex) const {
    return std::chrono::microseconds(
        packetIndex * static_cast<std::uint64_t>(framesPerPacket_) * 1000000ULL / sampleRate_);
}

void PacketPacer::waitFor(std::uint64_t packetIndex) {
    const auto now = std::chrono::steady_clock::now();
    if (packetIndex == 0 || started_ == std::chrono::steady_clock::time_point{}) started_ = now;
    const auto deadline = started_ + releaseOffset(packetIndex);
    if (deadline > now) std::this_thread::sleep_until(deadline);
}

} // namespace orynivo::airplay2
