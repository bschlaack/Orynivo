// SPDX-License-Identifier: Apache-2.0
#include "transient_pairing.h"

#include "airplay_crypto.h"

#include <algorithm>
#include <array>
#include <cctype>
#include <charconv>
#include <chrono>
#include <cstddef>
#include <stdexcept>
#include <string>
#include <string_view>

namespace orynivo::airplay2 {
namespace {

using fxchain::airplay::Bytes;
namespace tlv = fxchain::airplay::tlv;

constexpr std::size_t MaxPairingResponseBytes = 1024 * 1024;

struct HttpResponse final {
    int status = 0;
    Bytes body;
};

std::span<const std::byte> asBytes(std::string_view text) {
    return {reinterpret_cast<const std::byte*>(text.data()), text.size()};
}

std::string lower(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}

HttpResponse postPairSetup(Socket& socket, const Bytes& body, int sequence) {
    const std::string header =
        "POST /pair-setup RTSP/1.0\r\n"
        "CSeq: " + std::to_string(sequence) + "\r\n"
        "User-Agent: AirPlay/670.6.2\r\n"
        "X-Apple-HKP: 4\r\n"
        "Content-Type: application/octet-stream\r\n"
        "Content-Length: " + std::to_string(body.size()) + "\r\n\r\n";
    socket.sendAll(asBytes(header));
    socket.sendAll({reinterpret_cast<const std::byte*>(body.data()), body.size()});

    std::vector<std::byte> received;
    received.reserve(4096);
    std::array<std::byte, 4096> chunk{};
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
    std::size_t headerEnd = std::string::npos;
    std::size_t contentLength = 0;
    while (std::chrono::steady_clock::now() < deadline) {
        const auto count = socket.receive(chunk, std::chrono::milliseconds(250));
        if (count == 0) continue;
        received.insert(received.end(), chunk.begin(), chunk.begin() + static_cast<std::ptrdiff_t>(count));
        if (received.size() > MaxPairingResponseBytes)
            throw std::runtime_error("AirPlay pairing response exceeded the safety limit.");

        const std::string_view view(reinterpret_cast<const char*>(received.data()), received.size());
        if (headerEnd == std::string::npos) {
            headerEnd = view.find("\r\n\r\n");
            if (headerEnd == std::string::npos) continue;
            std::size_t cursor = 0;
            while (cursor < headerEnd) {
                const auto end = view.find("\r\n", cursor);
                const auto line = view.substr(cursor, (end == std::string::npos ? headerEnd : end) - cursor);
                const auto colon = line.find(':');
                if (colon != std::string::npos && lower(std::string(line.substr(0, colon))) == "content-length") {
                    const auto value = line.substr(colon + 1);
                    const char* first = value.data();
                    const char* last = value.data() + value.size();
                    while (first != last && *first == ' ') ++first;
                    const auto parsed = std::from_chars(first, last, contentLength);
                    if (parsed.ec != std::errc{})
                        throw std::runtime_error("AirPlay pairing returned an invalid Content-Length.");
                }
                if (end == std::string::npos) break;
                cursor = end + 2;
            }
        }
        if (headerEnd != std::string::npos && received.size() >= headerEnd + 4 + contentLength) {
            const std::string_view statusLine = view.substr(0, view.find("\r\n"));
            const auto firstSpace = statusLine.find(' ');
            if (firstSpace == std::string::npos)
                throw std::runtime_error("AirPlay pairing returned an invalid status line.");
            int status = 0;
            const auto statusText = statusLine.substr(firstSpace + 1, 3);
            if (std::from_chars(statusText.data(), statusText.data() + statusText.size(), status).ec != std::errc{})
                throw std::runtime_error("AirPlay pairing returned an invalid status code.");
            const auto* bodyStart = reinterpret_cast<const std::uint8_t*>(received.data() + headerEnd + 4);
            return {status, Bytes(bodyStart, bodyStart + contentLength)};
        }
    }
    throw std::runtime_error("AirPlay transient pairing timed out.");
}

tlv::Map requireTlvResponse(const HttpResponse& response, std::uint8_t expectedState) {
    if (response.status != 200)
        throw std::runtime_error("AirPlay receiver rejected transient pairing with HTTP " + std::to_string(response.status) + '.');
    const auto decoded = tlv::decode(response.body);
    if (const auto error = tlv::get(decoded, tlv::Error))
        throw std::runtime_error("AirPlay receiver rejected transient pairing with HAP error " +
            std::to_string(error->empty() ? 0 : error->front()) + '.');
    const auto state = tlv::get(decoded, tlv::State);
    if (!state || state->size() != 1 || state->front() != expectedState)
        throw std::runtime_error("AirPlay pairing response had an unexpected HAP state.");
    return decoded;
}

} // namespace

TransientPairingKeys TransientPairing::run(Socket& control) {
    tlv::Map m1 = {
        {tlv::Method, {0x00}},
        {tlv::State, {0x01}},
        {tlv::Flags, {0x10}},
    };
    const auto m2 = requireTlvResponse(postPairSetup(control, tlv::encode(m1), 1), 0x02);
    const auto salt = tlv::get(m2, tlv::Salt);
    const auto serverB = tlv::get(m2, tlv::PublicKey);
    if (!salt || salt->empty() || !serverB || serverB->empty())
        throw std::runtime_error("AirPlay pairing M2 omitted SRP parameters.");

    fxchain::airplay::SrpClient srp;
    srp.start("3939");
    if (!srp.process(*salt, *serverB))
        throw std::runtime_error("AirPlay pairing rejected invalid SRP parameters.");

    tlv::Map m3 = {
        {tlv::State, {0x03}},
        {tlv::PublicKey, srp.publicA()},
        {tlv::Proof, srp.proofM1()},
    };
    const auto m4 = requireTlvResponse(postPairSetup(control, tlv::encode(m3), 2), 0x04);
    const auto proof = tlv::get(m4, tlv::Proof);
    if (!proof || proof->empty() || !srp.verifyServerProof(*proof))
        throw std::runtime_error("AirPlay receiver SRP proof validation failed.");

    TransientPairingKeys keys;
    keys.sharedSecret = srp.sessionKey();
    keys.controlWrite = fxchain::airplay::hkdfSha512(
        "Control-Salt", "Control-Write-Encryption-Key", keys.sharedSecret, 32);
    keys.controlRead = fxchain::airplay::hkdfSha512(
        "Control-Salt", "Control-Read-Encryption-Key", keys.sharedSecret, 32);
    keys.eventWrite = fxchain::airplay::hkdfSha512(
        "Events-Salt", "Events-Write-Encryption-Key", keys.sharedSecret, 32);
    keys.eventRead = fxchain::airplay::hkdfSha512(
        "Events-Salt", "Events-Read-Encryption-Key", keys.sharedSecret, 32);
    if (keys.sharedSecret.size() < 32)
        throw std::runtime_error("AirPlay transient pairing produced a short audio key.");
    keys.audioKey.assign(keys.sharedSecret.begin(), keys.sharedSecret.begin() + 32);
    return keys;
}

} // namespace orynivo::airplay2
