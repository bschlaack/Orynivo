// SPDX-License-Identifier: Apache-2.0
#include "socket_transport.h"

#include <array>
#include <cassert>
#include <cstddef>
#include <iostream>

using orynivo::airplay2::Socket;

int main() {
    Socket udp = Socket::bindUdp();
    assert(udp.valid());
    assert(udp.localPort() != 0);

    Socket moved = std::move(udp);
    assert(!udp.valid());
    assert(moved.valid());
    moved.close();
    moved.close();
    assert(!moved.valid());

    std::cout << "portable socket ownership and UDP bind passed\n";
    return 0;
}
