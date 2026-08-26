// SPDX-License-Identifier: Apache-2.0
#include "event_channel.h"

#include <array>
#include <charconv>
#include <chrono>
#include <cctype>
#include <stdexcept>

namespace orynivo::airplay2 {
namespace {
constexpr std::size_t MaxEventBody = 1024 * 1024;

std::string trim(std::string value) {
    while (!value.empty() && std::isspace(static_cast<unsigned char>(value.front())))
        value.erase(value.begin());
    while (!value.empty() && std::isspace(static_cast<unsigned char>(value.back())))
        value.pop_back();
    return value;
}
}

EventChannel::EventChannel(Socket& socket, std::vector<std::uint8_t> incomingKey,
                           std::vector<std::uint8_t> outgoingKey)
    : socket_(socket), incoming_(std::move(incomingKey)), outgoing_(std::move(outgoingKey)),
      thread_([this](std::stop_token token) { run(token); }) {}

EventChannel::~EventChannel() {
    thread_.request_stop();
    if (thread_.joinable()) thread_.join();
}

std::uint64_t EventChannel::answeredRequestCount() const noexcept {
    return answered_.load(std::memory_order_relaxed);
}

bool EventChannel::failed() const noexcept { return failed_.load(std::memory_order_relaxed); }

void EventChannel::run(std::stop_token stopToken) {
    std::array<std::byte, 4096> wire{};
    while (!stopToken.stop_requested()) {
        try {
            const auto count = socket_.receive(wire, std::chrono::milliseconds(100));
            if (count == 0) continue;
            const auto plain = incoming_.decode(std::span<const std::uint8_t>(
                reinterpret_cast<const std::uint8_t*>(wire.data()), count));
            plaintext_.append(reinterpret_cast<const char*>(plain.data()), plain.size());
            processPlaintext();
        } catch (...) {
            if (!stopToken.stop_requested()) failed_.store(true, std::memory_order_relaxed);
            return;
        }
    }
}

void EventChannel::processPlaintext() {
    for (;;) {
        const auto headerEnd = plaintext_.find("\r\n\r\n");
        if (headerEnd == std::string::npos) return;
        std::size_t contentLength = 0;
        std::string cseq;
        std::size_t lineStart = 0;
        while (lineStart < headerEnd) {
            const auto lineEnd = plaintext_.find("\r\n", lineStart);
            const auto line = plaintext_.substr(lineStart, lineEnd - lineStart);
            const auto colon = line.find(':');
            if (colon != std::string::npos) {
                auto key = trim(line.substr(0, colon));
                for (auto& character : key)
                    character = static_cast<char>(std::tolower(static_cast<unsigned char>(character)));
                const auto value = trim(line.substr(colon + 1));
                if (key == "cseq") cseq = value;
                if (key == "content-length") {
                    const auto result = std::from_chars(value.data(), value.data() + value.size(), contentLength);
                    if (result.ec != std::errc{} || contentLength > MaxEventBody)
                        throw std::runtime_error("Invalid AirPlay event content length.");
                }
            }
            if (lineEnd == std::string::npos || lineEnd >= headerEnd) break;
            lineStart = lineEnd + 2;
        }
        const auto total = headerEnd + 4 + contentLength;
        if (plaintext_.size() < total) return;
        plaintext_.erase(0, total);
        auto response = std::string("RTSP/1.0 200 OK\r\nServer: AirTunes/550.10\r\n");
        if (!cseq.empty()) response += "CSeq: " + cseq + "\r\n";
        response += "\r\n";
        const auto encrypted = outgoing_.encode(std::span<const std::uint8_t>(
            reinterpret_cast<const std::uint8_t*>(response.data()), response.size()));
        socket_.sendAll({reinterpret_cast<const std::byte*>(encrypted.data()), encrypted.size()});
        answered_.fetch_add(1, std::memory_order_relaxed);
    }
}

} // namespace orynivo::airplay2
