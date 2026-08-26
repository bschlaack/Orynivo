// SPDX-License-Identifier: Apache-2.0
#pragma once

#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace orynivo::airplay2 {

/** Encodes and decodes the authenticated HAP framing used by AirPlay 2 control channels. */
class EncryptedControl final {
public:
    /** Creates a directional control codec from a 32-byte key. */
    explicit EncryptedControl(std::vector<std::uint8_t> key);

    /** Encrypts plaintext into one or more length-prefixed authenticated frames. */
    std::vector<std::uint8_t> encode(std::span<const std::uint8_t> plaintext);

    /** Appends wire bytes and returns all complete authenticated plaintext frames. */
    std::vector<std::uint8_t> decode(std::span<const std::uint8_t> wireBytes);

private:
    std::vector<std::uint8_t> key_;
    std::vector<std::uint8_t> pending_;
    std::uint64_t counter_ = 0;
};

} // namespace orynivo::airplay2
