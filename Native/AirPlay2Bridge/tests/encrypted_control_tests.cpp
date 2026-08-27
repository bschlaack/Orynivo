// SPDX-License-Identifier: Apache-2.0
#include "encrypted_control.h"

#include <cassert>
#include <cstdint>
#include <vector>

int main() {
    std::vector<std::uint8_t> key(32);
    for (std::size_t i = 0; i < key.size(); ++i) key[i] = static_cast<std::uint8_t>(i);
    std::vector<std::uint8_t> plain(2500);
    for (std::size_t i = 0; i < plain.size(); ++i) plain[i] = static_cast<std::uint8_t>(i % 251);

    orynivo::airplay2::EncryptedControl writer(key);
    orynivo::airplay2::EncryptedControl reader(key);
    auto wire = writer.encode(plain);
    std::vector<std::uint8_t> decoded;
    for (std::size_t offset = 0; offset < wire.size();) {
        const auto count = std::min<std::size_t>(37, wire.size() - offset);
        auto part = reader.decode({wire.data() + offset, count});
        decoded.insert(decoded.end(), part.begin(), part.end());
        offset += count;
    }
    assert(decoded == plain);

    orynivo::airplay2::EncryptedControl tamperedReader(key);
    wire.back() ^= 1;
    bool rejected = false;
    try { (void)tamperedReader.decode(wire); } catch (...) { rejected = true; }
    assert(rejected);
}
