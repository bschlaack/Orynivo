// SPDX-License-Identifier: Apache-2.0
#include "packet_pacer.h"

#include <cassert>
#include <chrono>

int main() {
    using namespace std::chrono_literals;
    const orynivo::airplay2::PacketPacer pacer(44100, 352);
    assert(pacer.releaseOffset(0) == 0us);
    assert(pacer.releaseOffset(100) > 798ms);
    assert(pacer.releaseOffset(100) < 799ms);
    assert(pacer.releaseOffset(220) > 1755ms);
    assert(pacer.releaseOffset(1000) > 7s);
    assert(pacer.releaseOffset(1001) > pacer.releaseOffset(1000));
    return 0;
}
