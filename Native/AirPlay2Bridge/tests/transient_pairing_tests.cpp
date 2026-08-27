// SPDX-License-Identifier: Apache-2.0
#include "airplay_crypto.h"

#include <cassert>
#include <cstdint>
#include <iostream>

int main() {
    namespace airplay = fxchain::airplay;
    namespace tlv = fxchain::airplay::tlv;

    tlv::Map request = {
        {tlv::Method, {0x00}},
        {tlv::State, {0x01}},
        {tlv::Flags, {0x10}},
        {tlv::PublicKey, airplay::Bytes(384, 0x5a)},
    };
    const auto encoded = tlv::encode(request);
    const auto decoded = tlv::decode(encoded);
    assert(tlv::get(decoded, tlv::Method) == airplay::Bytes{0x00});
    assert(tlv::get(decoded, tlv::State) == airplay::Bytes{0x01});
    assert(tlv::get(decoded, tlv::Flags) == airplay::Bytes{0x10});
    assert(tlv::get(decoded, tlv::PublicKey)->size() == 384);

    const auto secret = airplay::randomBytes(64);
    const auto write = airplay::hkdfSha512(
        "Control-Salt", "Control-Write-Encryption-Key", secret, 32);
    const auto read = airplay::hkdfSha512(
        "Control-Salt", "Control-Read-Encryption-Key", secret, 32);
    assert(write.size() == 32);
    assert(read.size() == 32);
    assert(write != read);

    std::cout << "transient pairing TLV and key derivation primitives passed\n";
    return 0;
}
