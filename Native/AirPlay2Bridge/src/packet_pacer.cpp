// SPDX-License-Identifier: Apache-2.0
#include "packet_pacer.h"

#include <algorithm>
#include <stdexcept>
#include <thread>

namespace orynivo::airplay2 {
namespace {
constexpr auto BufferWindow = std::chrono::milliseconds(1750);
constexpr auto MinimumFillGap = std::chrono::milliseconds(1);
}

PacketPacer::PacketPacer(std::uint32_t sampleRate, std::uint32_t framesPerPacket)
    : sampleRate_(sampleRate), framesPerPacket_(framesPerPacket) {
    if (sampleRate == 0 || framesPerPacket == 0)
        throw std::invalid_argument("Packet pacing requires a sample rate and packet size.");
}

std::chrono::microseconds PacketPacer::releaseOffset(std::uint64_t packetIndex) const {
    const auto audio = std::chrono::microseconds(
        packetIndex * static_cast<std::uint64_t>(framesPerPacket_) * 1000000ULL / sampleRate_);
    const auto buffered = audio > BufferWindow
        ? audio - std::chrono::duration_cast<std::chrono::microseconds>(BufferWindow)
        : std::chrono::microseconds::zero();
    const auto fillFloor = packetIndex * MinimumFillGap;
    return std::max(buffered, std::chrono::duration_cast<std::chrono::microseconds>(fillFloor));
}

void PacketPacer::waitFor(std::uint64_t packetIndex) {
    const auto now = std::chrono::steady_clock::now();
    if (packetIndex == 0 || started_ == std::chrono::steady_clock::time_point{}) started_ = now;
    auto deadline = started_ + releaseOffset(packetIndex);
    if (lastRelease_ != std::chrono::steady_clock::time_point{})
        deadline = std::max(deadline, lastRelease_ + MinimumFillGap);
    if (deadline > now) std::this_thread::sleep_until(deadline);
    lastRelease_ = std::chrono::steady_clock::now();
}

} // namespace orynivo::airplay2
