// SPDX-License-Identifier: Apache-2.0
#pragma once

#include "socket_transport.h"

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

private:
    void run(std::stop_token stopToken);
    Socket socket_;
    std::jthread thread_;
};

} // namespace orynivo::airplay2
