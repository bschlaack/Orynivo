// SPDX-License-Identifier: Apache-2.0
#include "encrypted_rtsp.h"

#include <algorithm>
#include <array>
#include <charconv>
#include <cctype>
#include <stdexcept>
#include <string_view>

namespace orynivo::airplay2 {
namespace {
constexpr std::size_t MaxResponseBytes = 4 * 1024 * 1024;

std::string lower(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}
}

EncryptedRtsp::EncryptedRtsp(Socket& socket, std::vector<std::uint8_t> writeKey,
                             std::vector<std::uint8_t> readKey, std::string senderId)
    : socket_(socket), writer_(std::move(writeKey)), reader_(std::move(readKey)),
      senderId_(std::move(senderId)) {}

RtspResponse EncryptedRtsp::request(std::string method, std::string uri,
                                    std::string contentType, std::vector<std::uint8_t> body,
                                    std::map<std::string, std::string> extraHeaders,
                                    std::chrono::milliseconds timeout) {
    std::string request = method + " " + uri + " RTSP/1.0\r\n" +
        "CSeq: " + std::to_string(sequence_++) + "\r\n" +
        "User-Agent: AirPlay/670.6.2\r\n" +
        "DACP-ID: " + senderId_ + "\r\n" +
        "Active-Remote: 1\r\n" +
        "Client-Instance: " + senderId_ + "\r\n" +
        "X-Apple-Client-Name: Orynivo\r\n";
    if (!contentType.empty()) request += "Content-Type: " + contentType + "\r\n";
    for (const auto& [key, value] : extraHeaders)
        request += key + ": " + value + "\r\n";
    request += "Content-Length: " + std::to_string(body.size()) + "\r\n";
    request += "\r\n";
    std::vector<std::uint8_t> plain(request.begin(), request.end());
    plain.insert(plain.end(), body.begin(), body.end());
    const auto encrypted = writer_.encode(plain);
    socket_.sendAll({reinterpret_cast<const std::byte*>(encrypted.data()), encrypted.size()});

    std::vector<std::uint8_t> received;
    std::array<std::byte, 4096> chunk{};
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    std::size_t headerEnd = std::string::npos;
    std::size_t contentLength = 0;
    RtspResponse response;
    while (std::chrono::steady_clock::now() < deadline) {
        const auto count = socket_.receive(chunk, std::chrono::milliseconds(250));
        if (count == 0) continue;
        const auto decoded = reader_.decode({reinterpret_cast<const std::uint8_t*>(chunk.data()), count});
        received.insert(received.end(), decoded.begin(), decoded.end());
        if (received.size() > MaxResponseBytes)
            throw std::runtime_error("AirPlay RTSP response exceeded the safety limit.");
        const std::string_view view(reinterpret_cast<const char*>(received.data()), received.size());
        if (headerEnd == std::string::npos) {
            headerEnd = view.find("\r\n\r\n");
            if (headerEnd == std::string::npos) continue;
            const auto lineEnd = view.find("\r\n");
            if (lineEnd == std::string::npos) throw std::runtime_error("AirPlay returned an invalid RTSP status line.");
            const auto statusLine = view.substr(0, lineEnd);
            const auto space = statusLine.find(' ');
            if (space == std::string::npos || statusLine.size() < space + 4 ||
                std::from_chars(statusLine.data() + space + 1, statusLine.data() + space + 4, response.status).ec != std::errc{})
                throw std::runtime_error("AirPlay returned an invalid RTSP status code.");
            std::size_t cursor = lineEnd + 2;
            while (cursor < headerEnd) {
                const auto end = view.find("\r\n", cursor);
                const auto line = view.substr(cursor, (end == std::string::npos ? headerEnd : end) - cursor);
                const auto colon = line.find(':');
                if (colon != std::string::npos) {
                    auto key = lower(std::string(line.substr(0, colon)));
                    auto value = std::string(line.substr(colon + 1));
                    value.erase(0, value.find_first_not_of(" \t"));
                    response.headers.emplace(std::move(key), std::move(value));
                }
                if (end == std::string::npos) break;
                cursor = end + 2;
            }
            if (const auto it = response.headers.find("content-length"); it != response.headers.end()) {
                if (std::from_chars(it->second.data(), it->second.data() + it->second.size(), contentLength).ec != std::errc{})
                    throw std::runtime_error("AirPlay returned an invalid RTSP Content-Length.");
            }
        }
        if (headerEnd != std::string::npos && received.size() >= headerEnd + 4 + contentLength) {
            response.body.assign(received.begin() + static_cast<std::ptrdiff_t>(headerEnd + 4),
                                 received.begin() + static_cast<std::ptrdiff_t>(headerEnd + 4 + contentLength));
            return response;
        }
    }
    throw std::runtime_error("AirPlay encrypted RTSP request timed out.");
}

} // namespace orynivo::airplay2
