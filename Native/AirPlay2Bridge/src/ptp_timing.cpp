// SPDX-License-Identifier: Apache-2.0
#include "ptp_timing.h"

#include <algorithm>
#include <array>
#include <cstring>
#include <span>
#include <vector>

namespace orynivo::airplay2 {
namespace {
constexpr std::uint16_t EventPort = 319;
constexpr std::uint16_t GeneralPort = 320;

void put16(std::byte* p, std::uint16_t v) {
    p[0] = static_cast<std::byte>(v >> 8); p[1] = static_cast<std::byte>(v);
}
void put32(std::byte* p, std::uint32_t v) {
    p[0] = static_cast<std::byte>(v >> 24); p[1] = static_cast<std::byte>(v >> 16);
    p[2] = static_cast<std::byte>(v >> 8); p[3] = static_cast<std::byte>(v);
}
void putTimestamp(std::byte* p, std::uint64_t ns) {
    const auto seconds = ns / 1'000'000'000ULL;
    const auto nanos = static_cast<std::uint32_t>(ns % 1'000'000'000ULL);
    p[0] = static_cast<std::byte>(seconds >> 40); p[1] = static_cast<std::byte>(seconds >> 32);
    p[2] = static_cast<std::byte>(seconds >> 24); p[3] = static_cast<std::byte>(seconds >> 16);
    p[4] = static_cast<std::byte>(seconds >> 8); p[5] = static_cast<std::byte>(seconds);
    put32(p + 6, nanos);
}

std::vector<std::byte> header(std::uint8_t type, std::uint16_t length,
                              const std::array<std::uint8_t, 8>& clock,
                              std::uint16_t sequence, std::uint16_t flags,
                              std::uint8_t control, std::int8_t interval) {
    std::vector<std::byte> packet(length);
    packet[0] = static_cast<std::byte>(0x10U | type); // gPTP majorSdoId=1.
    packet[1] = std::byte{0x02};
    put16(packet.data() + 2, length);
    put16(packet.data() + 6, flags);
    std::transform(clock.begin(), clock.end(), packet.begin() + 20,
                   [](std::uint8_t v) { return static_cast<std::byte>(v); });
    put16(packet.data() + 28, 0x8001);
    put16(packet.data() + 30, sequence);
    packet[32] = static_cast<std::byte>(control);
    packet[33] = static_cast<std::byte>(static_cast<std::uint8_t>(interval));
    return packet;
}
} // namespace

PtpTimingGrandmaster::PtpTimingGrandmaster(
    std::string receiverHost, std::string localAddress,
    std::array<std::uint8_t, 8> clockIdentity)
    : receiverHost_(std::move(receiverHost)), localAddress_(std::move(localAddress)),
      clockIdentity_(clockIdentity), event_(Socket::bindUdp(EventPort)),
      general_(Socket::bindUdp(GeneralPort)), worker_([this] { run(); }) {}

PtpTimingGrandmaster::~PtpTimingGrandmaster() {
    stopping_.store(true);
    event_.close();
    general_.close();
    if (worker_.joinable()) worker_.join();
}

std::uint64_t PtpTimingGrandmaster::clockId() const noexcept {
    std::uint64_t value = 0;
    for (const auto byte : clockIdentity_) value = (value << 8) | byte;
    return value;
}

std::uint64_t PtpTimingGrandmaster::nowNanoseconds() noexcept {
    return static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(
        std::chrono::system_clock::now().time_since_epoch()).count());
}

void PtpTimingGrandmaster::run() {
    auto nextSync = std::chrono::steady_clock::now();
    auto nextAnnounce = nextSync;
    while (!stopping_.load()) {
        try {
            const auto now = std::chrono::steady_clock::now();
            if (now >= nextSync) {
                sendSync();
                nextSync = now + std::chrono::milliseconds(125);
            }
            if (now >= nextAnnounce) {
                sendAnnounce();
                nextAnnounce = now + std::chrono::seconds(1);
            }
            pollRequests();
        } catch (...) {
            // Timing runs independently from the ABI caller. A transient UDP
            // failure must not escape the worker thread and terminate Orynivo.
            std::this_thread::sleep_for(std::chrono::milliseconds(25));
        }
    }
}

void PtpTimingGrandmaster::sendSync() {
    const auto sequence = syncSequence_++;
    auto sync = header(0, 44, clockIdentity_, sequence, 0x0608, 0, -3);
    event_.sendTo(receiverHost_, EventPort, sync);

    auto follow = header(8, 76, clockIdentity_, sequence, 0x0408, 2, -3);
    putTimestamp(follow.data() + 34, nowNanoseconds());
    // IEEE 802.1AS Follow_Up information TLV.
    put16(follow.data() + 44, 3); put16(follow.data() + 46, 28);
    follow[48] = std::byte{0x00}; follow[49] = std::byte{0x80}; follow[50] = std::byte{0xc2};
    follow[53] = std::byte{0x01};
    general_.sendTo(receiverHost_, GeneralPort, follow);
}

void PtpTimingGrandmaster::sendAnnounce() {
    auto packet = header(11, 76, clockIdentity_, announceSequence_++, 0x0408, 5, 0);
    putTimestamp(packet.data() + 34, nowNanoseconds());
    put16(packet.data() + 44, 37);
    packet[47] = std::byte{128}; packet[48] = std::byte{6}; packet[49] = std::byte{0x21};
    put16(packet.data() + 50, 0x436a); packet[52] = std::byte{128};
    std::transform(clockIdentity_.begin(), clockIdentity_.end(), packet.begin() + 53,
                   [](std::uint8_t v) { return static_cast<std::byte>(v); });
    packet[63] = std::byte{0x20};
    put16(packet.data() + 64, 8); put16(packet.data() + 66, 8);
    std::transform(clockIdentity_.begin(), clockIdentity_.end(), packet.begin() + 68,
                   [](std::uint8_t v) { return static_cast<std::byte>(v); });
    general_.sendTo(receiverHost_, GeneralPort, packet);
}

void PtpTimingGrandmaster::pollRequests() {
    std::array<std::byte, 256> buffer{};
    std::string host;
    std::uint16_t port = 0;
    try {
        const auto size = event_.receiveFrom(buffer, std::chrono::milliseconds(10), host, port);
        if (size < 44 || (std::to_integer<std::uint8_t>(buffer[0]) & 0x0fU) != 1U) return;
        const auto sequence = static_cast<std::uint16_t>(
            std::to_integer<std::uint8_t>(buffer[30]) << 8 |
            std::to_integer<std::uint8_t>(buffer[31]));
        auto response = header(9, 54, clockIdentity_, sequence, 0x0408, 3, 0x7f);
        putTimestamp(response.data() + 34, nowNanoseconds());
        std::copy_n(buffer.begin() + 20, 10, response.begin() + 44);
        general_.sendTo(host, GeneralPort, response);
        ++delayResponses_;
    } catch (...) {
        // Socket closure during shutdown and transient receive failures are
        // retried by the owning timing loop.
    }
}

} // namespace orynivo::airplay2
