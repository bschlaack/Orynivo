// SPDX-License-Identifier: Apache-2.0
#include "rtp_control.h"

#include <algorithm>
#include <chrono>

namespace orynivo::airplay2 {
namespace {
void writeBigEndian32(std::byte* destination, std::uint32_t value) {
    destination[0] = static_cast<std::byte>(value >> 24);
    destination[1] = static_cast<std::byte>(value >> 16);
    destination[2] = static_cast<std::byte>(value >> 8);
    destination[3] = static_cast<std::byte>(value);
}

void writeNtp(std::byte* destination) {
    constexpr std::uint64_t EpochDelta = 2208988800ULL;
    const auto now = std::chrono::system_clock::now().time_since_epoch();
    const auto seconds = std::chrono::duration_cast<std::chrono::seconds>(now);
    const auto nanos = std::chrono::duration_cast<std::chrono::nanoseconds>(now - seconds).count();
    writeBigEndian32(destination, static_cast<std::uint32_t>(seconds.count() + EpochDelta));
    writeBigEndian32(destination + 4,
        static_cast<std::uint32_t>((static_cast<std::uint64_t>(nanos) << 32) / 1000000000ULL));
}
} // namespace

std::array<std::byte, 20> buildRtpSyncPacket(
    std::uint32_t nextTimestamp, std::uint32_t latencyFrames, bool first) {
    std::array<std::byte, 20> packet{};
    packet[0] = first ? std::byte{0x90} : std::byte{0x80};
    packet[1] = std::byte{0xd4};
    packet[3] = std::byte{0x07};
    writeBigEndian32(packet.data() + 4,
                     nextTimestamp >= latencyFrames ? nextTimestamp - latencyFrames : 0);
    writeNtp(packet.data() + 8);
    writeBigEndian32(packet.data() + 16, nextTimestamp);
    return packet;
}

std::array<std::byte, 28> buildPtpRtpSyncPacket(
    std::uint32_t currentPlaybackTimestamp, std::uint32_t nextTimestamp,
    std::uint64_t clockNanoseconds, std::uint64_t clockId, bool first) {
    std::array<std::byte, 28> packet{};
    packet[0] = first ? std::byte{0x90} : std::byte{0x80};
    packet[1] = std::byte{0xd7};
    packet[3] = std::byte{0x06};
    writeBigEndian32(packet.data() + 4, currentPlaybackTimestamp);
    writeBigEndian32(packet.data() + 8, static_cast<std::uint32_t>(clockNanoseconds >> 32));
    writeBigEndian32(packet.data() + 12, static_cast<std::uint32_t>(clockNanoseconds));
    // AirPlay 2 receivers use this field as the earliest RTP frame that may be
    // rendered. Apple's senders and OwnTone place it 250 ms behind the next
    // packet timestamp for a 44.1-kHz stream.
    writeBigEndian32(packet.data() + 16, nextTimestamp - 11025U);
    writeBigEndian32(packet.data() + 20, static_cast<std::uint32_t>(clockId >> 32));
    writeBigEndian32(packet.data() + 24, static_cast<std::uint32_t>(clockId));
    return packet;
}

RtpRetransmitResponder::RtpRetransmitResponder(Socket& socket)
    : socket_(socket), thread_([this](std::stop_token token) { run(token); }) {}

RtpRetransmitResponder::~RtpRetransmitResponder() {
    thread_.request_stop();
    if (thread_.joinable()) thread_.join();
}

void RtpRetransmitResponder::store(std::span<const std::byte> packet) {
    if (packet.size() < 12 || packet.size() > 4096) return;
    const auto sequence = static_cast<std::uint16_t>(std::to_integer<std::uint8_t>(packet[2]) << 8 |
                                                     std::to_integer<std::uint8_t>(packet[3]));
    std::scoped_lock lock(mutex_);
    auto& slot = slots_[sequence % SlotCount];
    slot.sequence = sequence;
    slot.packet.assign(packet.begin(), packet.end());
}

std::uint64_t RtpRetransmitResponder::receivedDatagramCount() const noexcept {
    return receivedDatagrams_.load(std::memory_order_relaxed);
}

std::uint64_t RtpRetransmitResponder::retransmitRequestCount() const noexcept {
    return retransmitRequests_.load(std::memory_order_relaxed);
}

std::uint64_t RtpRetransmitResponder::resentPacketCount() const noexcept {
    return resentPackets_.load(std::memory_order_relaxed);
}

void RtpRetransmitResponder::run(std::stop_token stopToken) {
    std::array<std::byte, 512> request{};
    while (!stopToken.stop_requested()) {
        std::string host;
        std::uint16_t port = 0;
        try {
            const auto size = socket_.receiveFrom(request, std::chrono::milliseconds(100), host, port);
            if (size == 0) continue;
            receivedDatagrams_.fetch_add(1, std::memory_order_relaxed);
            if (size < 8 || (std::to_integer<std::uint8_t>(request[1]) & 0x7f) != 0x55) continue;
            retransmitRequests_.fetch_add(1, std::memory_order_relaxed);
            const auto requestSequence = static_cast<std::uint16_t>(
                std::to_integer<std::uint8_t>(request[2]) << 8 | std::to_integer<std::uint8_t>(request[3]));
            const auto first = static_cast<std::uint16_t>(
                std::to_integer<std::uint8_t>(request[4]) << 8 | std::to_integer<std::uint8_t>(request[5]));
            auto count = static_cast<std::uint16_t>(
                std::to_integer<std::uint8_t>(request[6]) << 8 | std::to_integer<std::uint8_t>(request[7]));
            count = std::clamp<std::uint16_t>(count == 0 ? 1 : count, 1, static_cast<std::uint16_t>(SlotCount));
            for (std::uint16_t offset = 0; offset < count; ++offset) {
                const auto sequence = static_cast<std::uint16_t>(first + offset);
                std::vector<std::byte> original;
                {
                    std::scoped_lock lock(mutex_);
                    const auto& slot = slots_[sequence % SlotCount];
                    if (!slot.packet.empty() && slot.sequence == sequence) original = slot.packet;
                }
                if (original.empty()) continue;
                std::vector<std::byte> response(4 + original.size());
                response[0] = std::byte{0x80};
                response[1] = std::byte{0xd6};
                response[2] = static_cast<std::byte>(requestSequence >> 8);
                response[3] = static_cast<std::byte>(requestSequence);
                std::copy(original.begin(), original.end(), response.begin() + 4);
                socket_.sendTo(host, port, response);
                resentPackets_.fetch_add(1, std::memory_order_relaxed);
            }
        } catch (...) {
            if (stopToken.stop_requested()) return;
        }
    }
}

} // namespace orynivo::airplay2
