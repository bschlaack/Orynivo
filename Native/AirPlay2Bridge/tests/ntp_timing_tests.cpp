// SPDX-License-Identifier: Apache-2.0
#include "ntp_timing.h"

#include <array>
#include <cassert>
#include <chrono>
#include <cstring>

int main() {
    orynivo::airplay2::NtpTimingResponder responder;
    auto client = orynivo::airplay2::Socket::bindUdp();
    std::array<std::byte, 32> request{};
    request[1] = std::byte{0xD2};
    request[2] = std::byte{0x12};
    request[3] = std::byte{0x34};
    for (std::size_t i = 24; i < 32; ++i) request[i] = static_cast<std::byte>(i);
    client.sendTo("127.0.0.1", responder.localPort(), request);

    std::array<std::byte, 64> response{};
    std::string host;
    std::uint16_t port = 0;
    const auto count = client.receiveFrom(response, std::chrono::seconds(2), host, port);
    assert(count == 32);
    assert(response[0] == std::byte{0x80} && response[1] == std::byte{0xD3});
    assert(response[2] == request[2] && response[3] == request[3]);
    assert(std::memcmp(response.data() + 8, request.data() + 24, 8) == 0);
}
