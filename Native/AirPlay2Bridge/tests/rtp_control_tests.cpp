// SPDX-License-Identifier: Apache-2.0
#include "rtp_control.h"

#include <array>
#include <cassert>
#include <chrono>
#include <cstddef>

int main() {
    const auto sync = orynivo::airplay2::buildRtpSyncPacket(100000, 44100, true);
    assert(sync[0] == std::byte{0x90} && sync[1] == std::byte{0xd4});
    assert(sync[16] == std::byte{0x00} && sync[17] == std::byte{0x01});
    assert(sync[18] == std::byte{0x86} && sync[19] == std::byte{0xa0});

    auto control = orynivo::airplay2::Socket::bindUdp();
    orynivo::airplay2::RtpRetransmitResponder responder(control);
    std::array<std::byte, 16> original{};
    original[0] = std::byte{0x80}; original[1] = std::byte{0x60};
    original[2] = std::byte{0x12}; original[3] = std::byte{0x34};
    responder.store(original);

    auto receiver = orynivo::airplay2::Socket::bindUdp();
    std::array<std::byte, 8> request{
        std::byte{0x80}, std::byte{0xd5}, std::byte{0xab}, std::byte{0xcd},
        std::byte{0x12}, std::byte{0x34}, std::byte{0x00}, std::byte{0x01}};
    receiver.sendTo("127.0.0.1", control.localPort(), request);
    std::array<std::byte, 64> response{};
    std::string host;
    std::uint16_t port = 0;
    const auto size = receiver.receiveFrom(response, std::chrono::seconds(2), host, port);
    assert(size == original.size() + 4);
    assert(response[1] == std::byte{0xd6});
    assert(response[2] == std::byte{0xab} && response[3] == std::byte{0xcd});
    for (std::size_t i = 0; i < original.size(); ++i) assert(response[i + 4] == original[i]);
    return 0;
}
