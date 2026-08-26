// SPDX-License-Identifier: Apache-2.0
#include "encrypted_control.h"

#include "airplay_crypto.h"

#include <algorithm>
#include <stdexcept>

namespace orynivo::airplay2 {
namespace {
constexpr std::size_t MaxFramePlaintext = 1024;
constexpr std::size_t TagBytes = 16;
}

EncryptedControl::EncryptedControl(std::vector<std::uint8_t> key) : key_(std::move(key)) {
    if (key_.size() != 32) throw std::invalid_argument("AirPlay control key must contain 32 bytes.");
}

std::vector<std::uint8_t> EncryptedControl::encode(std::span<const std::uint8_t> plaintext) {
    std::vector<std::uint8_t> output;
    for (std::size_t offset = 0; offset < plaintext.size();) {
        const auto count = std::min(MaxFramePlaintext, plaintext.size() - offset);
        fxchain::airplay::Bytes aad = {
            static_cast<std::uint8_t>(count & 0xff),
            static_cast<std::uint8_t>((count >> 8) & 0xff),
        };
        fxchain::airplay::Bytes block(plaintext.begin() + static_cast<std::ptrdiff_t>(offset),
                                     plaintext.begin() + static_cast<std::ptrdiff_t>(offset + count));
        const auto encrypted = fxchain::airplay::chacha20Poly1305Encrypt(
            key_, fxchain::airplay::counterNonce8(counter_++), block, aad);
        output.insert(output.end(), aad.begin(), aad.end());
        output.insert(output.end(), encrypted.begin(), encrypted.end());
        offset += count;
    }
    return output;
}

std::vector<std::uint8_t> EncryptedControl::decode(std::span<const std::uint8_t> wireBytes) {
    pending_.insert(pending_.end(), wireBytes.begin(), wireBytes.end());
    std::vector<std::uint8_t> output;
    while (pending_.size() >= 2) {
        const std::size_t count = pending_[0] | (static_cast<std::size_t>(pending_[1]) << 8);
        if (count > MaxFramePlaintext) throw std::runtime_error("AirPlay control frame exceeded 1024 bytes.");
        const auto frameBytes = 2 + count + TagBytes;
        if (pending_.size() < frameBytes) break;
        const fxchain::airplay::Bytes aad(pending_.begin(), pending_.begin() + 2);
        const fxchain::airplay::Bytes encrypted(pending_.begin() + 2,
                                               pending_.begin() + static_cast<std::ptrdiff_t>(frameBytes));
        const auto plain = fxchain::airplay::chacha20Poly1305Decrypt(
            key_, fxchain::airplay::counterNonce8(counter_++), encrypted, aad);
        if (!plain) throw std::runtime_error("AirPlay control frame authentication failed.");
        output.insert(output.end(), plain->begin(), plain->end());
        pending_.erase(pending_.begin(), pending_.begin() + static_cast<std::ptrdiff_t>(frameBytes));
    }
    return output;
}

} // namespace orynivo::airplay2
