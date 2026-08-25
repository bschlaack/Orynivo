// SPDX-License-Identifier: Apache-2.0
#pragma once

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <span>
#include <string>

namespace orynivo::airplay2 {

/** Cross-platform socket handle whose ownership is explicit and movable. */
class Socket final {
public:
    Socket() noexcept = default;
    ~Socket();
    Socket(Socket&& other) noexcept;
    Socket& operator=(Socket&& other) noexcept;
    Socket(const Socket&) = delete;
    Socket& operator=(const Socket&) = delete;

    /** Connects a TCP socket to a numeric address or DNS host. */
    static Socket connectTcp(
        const std::string& host,
        std::uint16_t port,
        std::chrono::milliseconds timeout);

    /** Creates a UDP socket bound to an ephemeral local port. */
    static Socket bindUdp();

    /** Sends every byte or throws std::system_error. */
    void sendAll(std::span<const std::byte> bytes);

    /** Sends one UDP datagram to a numeric address or DNS host. */
    void sendTo(
        const std::string& host,
        std::uint16_t port,
        std::span<const std::byte> bytes);

    /** Receives available TCP bytes, returning zero on orderly shutdown. */
    std::size_t receive(
        std::span<std::byte> destination,
        std::chrono::milliseconds timeout);

    /** Closes the socket and makes subsequent close calls harmless. */
    void close() noexcept;

    /** Returns whether this object currently owns a socket. */
    [[nodiscard]] bool valid() const noexcept;

    /** Returns the bound local port, or zero for an invalid socket. */
    [[nodiscard]] std::uint16_t localPort() const;

private:
    explicit Socket(std::intptr_t handle) noexcept : handle_(handle) {}
    std::intptr_t handle_ = -1;
};

} // namespace orynivo::airplay2
