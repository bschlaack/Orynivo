// SPDX-License-Identifier: Apache-2.0
#include "ntp_timing.h"

#include <array>
#include <chrono>
#include <cstring>

namespace orynivo::airplay2 {
namespace {
void writeNtp(std::byte* destination) {
    constexpr std::uint64_t EpochDelta = 2208988800ULL;
    const auto now = std::chrono::system_clock::now().time_since_epoch();
    const auto seconds = std::chrono::duration_cast<std::chrono::seconds>(now);
    const auto fractionDuration = now - seconds;
    const auto sec = static_cast<std::uint32_t>(seconds.count() + EpochDelta);
    const auto nanos = std::chrono::duration_cast<std::chrono::nanoseconds>(fractionDuration).count();
    const auto frac = static_cast<std::uint32_t>((static_cast<std::uint64_t>(nanos) << 32) / 1000000000ULL);
    for (int i = 0; i < 4; ++i) {
        destination[i] = static_cast<std::byte>(sec >> (24 - i * 8));
        destination[4 + i] = static_cast<std::byte>(frac >> (24 - i * 8));
    }
}
}

NtpTimingResponder::NtpTimingResponder() : socket_(Socket::bindUdp()),
    thread_([this](std::stop_token token) { run(token); }) {}

NtpTimingResponder::~NtpTimingResponder() {
    thread_.request_stop();
    if (thread_.joinable()) thread_.join();
}

std::uint16_t NtpTimingResponder::localPort() const { return socket_.localPort(); }

void NtpTimingResponder::run(std::stop_token stopToken) {
    std::array<std::byte, 256> request{};
    while (!stopToken.stop_requested()) {
        std::string host;
        std::uint16_t port = 0;
        try {
            const auto count = socket_.receiveFrom(request, std::chrono::milliseconds(100), host, port);
            if (count < 32 || request[1] != std::byte{0xD2}) continue;
            std::array<std::byte, 32> response{};
            response[0] = std::byte{0x80};
            response[1] = std::byte{0xD3};
            std::memcpy(response.data() + 2, request.data() + 2, 2);
            std::memcpy(response.data() + 8, request.data() + 24, 8);
            writeNtp(response.data() + 16);
            std::memcpy(response.data() + 24, response.data() + 16, 8);
            socket_.sendTo(host, port, response);
        } catch (...) {
            if (stopToken.stop_requested()) return;
        }
    }
}

} // namespace orynivo::airplay2
