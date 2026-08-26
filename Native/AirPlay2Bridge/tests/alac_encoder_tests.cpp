// SPDX-License-Identifier: Apache-2.0
#include "alac_encoder.h"

#include <cassert>
#include <cstddef>
#include <vector>

int main() {
    orynivo::airplay2::AlacEncoder encoder(44100);
    const std::vector<std::byte> silence(orynivo::airplay2::AlacEncoder::PcmBytesPerPacket);
    const auto packet = encoder.encode(silence);
    assert(!packet.empty());
    assert(packet.size() < silence.size());
    const auto uncompressed = encoder.encodeUncompressed(silence);
    assert(uncompressed.size() == 1412);
    assert(uncompressed.front() == std::byte{0x20});
    return 0;
}
