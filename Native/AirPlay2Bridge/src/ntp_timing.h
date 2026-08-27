// SPDX-License-Identifier: Apache-2.0
#pragma once

#include "socket_transport.h"

#include <atomic>
#include <thread>

namespace orynivo::airplay2 {

/** Answers the NTP-style UDP timing requests used by realtime AirPlay streams. */
class NtpTimingResponder final {
public:
    /** Starts a responder on an ephemeral local UDP port. */
    NtpTimingResponder();
    ~NtpTimingResponder();
    NtpTimingResponder(const NtpTimingResponder&) = delete;
    NtpTimingResponder& operator=(const NtpTimingResponder&) = delete;

    /** Returns the advertised local timing port. */
    [[nodiscard]] std::uint16_t localPort() const;

    /** Returns the number of valid receiver timing requests observed. */
    [[nodiscard]] std::uint64_t requestCount() const noexcept;

    /** Returns the number of timing replies sent to the receiver. */
    [[nodiscard]] std::uint64_t responseCount() const noexcept;

private:
    void run(std::stop_token stopToken);
    Socket socket_;
    std::jthread thread_;
    std::atomic<std::uint64_t> requests_{0};
    std::atomic<std::uint64_t> responses_{0};
};

} // namespace orynivo::airplay2
