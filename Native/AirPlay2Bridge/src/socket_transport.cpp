// SPDX-License-Identifier: Apache-2.0
#include "socket_transport.h"

#include <array>
#include <algorithm>
#include <cstring>
#include <limits>
#include <memory>
#include <stdexcept>
#include <system_error>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  define NOMINMAX
#  include <WinSock2.h>
#  include <WS2tcpip.h>
#else
#  include <arpa/inet.h>
#  include <fcntl.h>
#  include <netdb.h>
#  include <poll.h>
#  include <sys/socket.h>
#  include <unistd.h>
#endif

namespace orynivo::airplay2 {
namespace {

#if defined(_WIN32)
using NativeSocket = SOCKET;
constexpr NativeSocket InvalidSocket = INVALID_SOCKET;
int lastSocketError() { return WSAGetLastError(); }
void closeSocket(NativeSocket value) { closesocket(value); }
class SocketRuntime final {
public:
    SocketRuntime() {
        WSADATA data{};
        if (WSAStartup(MAKEWORD(2, 2), &data) != 0)
            throw std::system_error(lastSocketError(), std::system_category(), "WSAStartup");
    }
    ~SocketRuntime() { WSACleanup(); }
};
void ensureRuntime() { static SocketRuntime runtime; }
#else
using NativeSocket = int;
constexpr NativeSocket InvalidSocket = -1;
int lastSocketError() { return errno; }
void closeSocket(NativeSocket value) { ::close(value); }
void ensureRuntime() {}
#endif

NativeSocket native(std::intptr_t value) { return static_cast<NativeSocket>(value); }

void setNonBlocking(NativeSocket socket, bool enabled) {
#if defined(_WIN32)
    u_long mode = enabled ? 1UL : 0UL;
    if (ioctlsocket(socket, FIONBIO, &mode) != 0)
        throw std::system_error(lastSocketError(), std::system_category(), "ioctlsocket");
#else
    const int flags = fcntl(socket, F_GETFL, 0);
    if (flags < 0 || fcntl(socket, F_SETFL, enabled ? flags | O_NONBLOCK : flags & ~O_NONBLOCK) < 0)
        throw std::system_error(lastSocketError(), std::generic_category(), "fcntl");
#endif
}

addrinfo* resolve(const std::string& host, std::uint16_t port, int socktype) {
    addrinfo hints{};
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = socktype;
    hints.ai_protocol = socktype == SOCK_STREAM ? IPPROTO_TCP : IPPROTO_UDP;
    addrinfo* result = nullptr;
    const auto service = std::to_string(port);
    const int error = getaddrinfo(host.c_str(), service.c_str(), &hints, &result);
    if (error != 0)
        throw std::runtime_error("Unable to resolve AirPlay receiver host: " + host);
    return result;
}

bool waitWritable(NativeSocket socket, std::chrono::milliseconds timeout) {
#if defined(_WIN32)
    WSAPOLLFD descriptor{socket, POLLWRNORM, 0};
    return WSAPoll(&descriptor, 1, static_cast<int>(timeout.count())) > 0;
#else
    pollfd descriptor{socket, POLLOUT, 0};
    return poll(&descriptor, 1, static_cast<int>(timeout.count())) > 0;
#endif
}

bool waitReadable(NativeSocket socket, std::chrono::milliseconds timeout) {
#if defined(_WIN32)
    WSAPOLLFD descriptor{socket, POLLRDNORM, 0};
    return WSAPoll(&descriptor, 1, static_cast<int>(timeout.count())) > 0;
#else
    pollfd descriptor{socket, POLLIN, 0};
    return poll(&descriptor, 1, static_cast<int>(timeout.count())) > 0;
#endif
}

} // namespace

Socket::~Socket() { close(); }

Socket::Socket(Socket&& other) noexcept : handle_(other.handle_) { other.handle_ = -1; }

Socket& Socket::operator=(Socket&& other) noexcept {
    if (this != &other) {
        close();
        handle_ = other.handle_;
        other.handle_ = -1;
    }
    return *this;
}

Socket Socket::connectTcp(
    const std::string& host,
    std::uint16_t port,
    std::chrono::milliseconds timeout) {
    ensureRuntime();
    addrinfo* addresses = resolve(host, port, SOCK_STREAM);
    std::unique_ptr<addrinfo, decltype(&freeaddrinfo)> guard(addresses, freeaddrinfo);
    int lastError = 0;
    for (auto* address = addresses; address != nullptr; address = address->ai_next) {
        NativeSocket candidate = socket(address->ai_family, address->ai_socktype, address->ai_protocol);
        if (candidate == InvalidSocket) {
            lastError = lastSocketError();
            continue;
        }
        try {
            setNonBlocking(candidate, true);
            const int result = connect(candidate, address->ai_addr, static_cast<int>(address->ai_addrlen));
#if defined(_WIN32)
            const bool pending = result != 0 && WSAGetLastError() == WSAEWOULDBLOCK;
#else
            const bool pending = result != 0 && errno == EINPROGRESS;
#endif
            if (result != 0 && !pending)
                throw std::system_error(lastSocketError(), std::system_category(), "connect");
            if (pending && !waitWritable(candidate, timeout))
                throw std::system_error(std::make_error_code(std::errc::timed_out), "connect");
            int socketError = 0;
#if defined(_WIN32)
            int length = sizeof(socketError);
#else
            socklen_t length = sizeof(socketError);
#endif
            if (getsockopt(candidate, SOL_SOCKET, SO_ERROR, reinterpret_cast<char*>(&socketError), &length) != 0 || socketError != 0)
                throw std::system_error(socketError == 0 ? lastSocketError() : socketError, std::system_category(), "connect");
            setNonBlocking(candidate, false);
            return Socket(static_cast<std::intptr_t>(candidate));
        } catch (...) {
            lastError = lastSocketError();
            closeSocket(candidate);
        }
    }
    throw std::system_error(lastError, std::system_category(), "Unable to connect to AirPlay receiver");
}

Socket Socket::bindUdp() {
    ensureRuntime();
    NativeSocket value = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (value == InvalidSocket)
        throw std::system_error(lastSocketError(), std::system_category(), "socket");
    sockaddr_in address{};
    address.sin_family = AF_INET;
    address.sin_addr.s_addr = htonl(INADDR_ANY);
    address.sin_port = 0;
    if (bind(value, reinterpret_cast<sockaddr*>(&address), sizeof(address)) != 0) {
        const int error = lastSocketError();
        closeSocket(value);
        throw std::system_error(error, std::system_category(), "bind");
    }
    return Socket(static_cast<std::intptr_t>(value));
}

void Socket::sendAll(std::span<const std::byte> bytes) {
    std::size_t offset = 0;
    while (offset < bytes.size()) {
        const auto remaining = std::min<std::size_t>(bytes.size() - offset, std::numeric_limits<int>::max());
        const int written = send(native(handle_), reinterpret_cast<const char*>(bytes.data() + offset), static_cast<int>(remaining), 0);
        if (written <= 0)
            throw std::system_error(lastSocketError(), std::system_category(), "send");
        offset += static_cast<std::size_t>(written);
    }
}

void Socket::sendTo(const std::string& host, std::uint16_t port, std::span<const std::byte> bytes) {
    addrinfo* addresses = resolve(host, port, SOCK_DGRAM);
    std::unique_ptr<addrinfo, decltype(&freeaddrinfo)> guard(addresses, freeaddrinfo);
    if (addresses == nullptr)
        throw std::runtime_error("No address for AirPlay receiver");
    const int written = sendto(native(handle_), reinterpret_cast<const char*>(bytes.data()), static_cast<int>(bytes.size()), 0,
        addresses->ai_addr, static_cast<int>(addresses->ai_addrlen));
    if (written < 0 || static_cast<std::size_t>(written) != bytes.size())
        throw std::system_error(lastSocketError(), std::system_category(), "sendto");
}

std::size_t Socket::receive(std::span<std::byte> destination, std::chrono::milliseconds timeout) {
    if (!waitReadable(native(handle_), timeout))
        return 0;
    const int received = recv(native(handle_), reinterpret_cast<char*>(destination.data()), static_cast<int>(destination.size()), 0);
    if (received < 0)
        throw std::system_error(lastSocketError(), std::system_category(), "recv");
    return static_cast<std::size_t>(received);
}

void Socket::close() noexcept {
    if (valid()) {
        closeSocket(native(handle_));
        handle_ = -1;
    }
}

bool Socket::valid() const noexcept { return handle_ != -1; }

std::uint16_t Socket::localPort() const {
    if (!valid()) return 0;
    sockaddr_storage address{};
#if defined(_WIN32)
    int length = sizeof(address);
#else
    socklen_t length = sizeof(address);
#endif
    if (getsockname(native(handle_), reinterpret_cast<sockaddr*>(&address), &length) != 0)
        throw std::system_error(lastSocketError(), std::system_category(), "getsockname");
    if (address.ss_family == AF_INET)
        return ntohs(reinterpret_cast<const sockaddr_in*>(&address)->sin_port);
    return ntohs(reinterpret_cast<const sockaddr_in6*>(&address)->sin6_port);
}

std::string Socket::localAddress() const {
    if (!valid()) return {};
    sockaddr_storage address{};
#if defined(_WIN32)
    int length = sizeof(address);
#else
    socklen_t length = sizeof(address);
#endif
    if (getsockname(native(handle_), reinterpret_cast<sockaddr*>(&address), &length) != 0)
        throw std::system_error(lastSocketError(), std::system_category(), "getsockname");
    std::array<char, INET6_ADDRSTRLEN> text{};
    const void* source = address.ss_family == AF_INET
        ? static_cast<const void*>(&reinterpret_cast<const sockaddr_in*>(&address)->sin_addr)
        : static_cast<const void*>(&reinterpret_cast<const sockaddr_in6*>(&address)->sin6_addr);
    if (inet_ntop(address.ss_family, source, text.data(), static_cast<socklen_t>(text.size())) == nullptr)
        throw std::system_error(lastSocketError(), std::system_category(), "inet_ntop");
    return text.data();
}

} // namespace orynivo::airplay2
