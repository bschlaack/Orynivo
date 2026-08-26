// SPDX-License-Identifier: Apache-2.0
#pragma once

#include "socket_transport.h"

#include <array>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <string>
#include <thread>

namespace orynivo::airplay2 {

/** Minimal unicast gPTP grandmaster used by native buffered AirPlay 2 sessions. */
class PtpTimingGrandmaster final {
public:
    /** Starts the event/general PTP sockets and publishes timing to one receiver. */
    PtpTimingGrandmaster(std::string receiverHost, std::string localAddress,
                         std::array<std::uint8_t, 8> clockIdentity);
    ~PtpTimingGrandmaster();
    PtpTimingGrandmaster(const PtpTimingGrandmaster&) = delete;
    PtpTimingGrandmaster& operator=(const PtpTimingGrandmaster&) = delete;

    /** Returns the unsigned 64-bit ClockID advertised in the AirPlay session. */
    [[nodiscard]] std::uint64_t clockId() const noexcept;

    /** Returns the sender address advertised in timingPeerInfo. */
    [[nodiscard]] const std::string& localAddress() const noexcept { return localAddress_; }

    /** Returns current PTP time as Unix-epoch nanoseconds. */
    [[nodiscard]] static std::uint64_t nowNanoseconds() noexcept;

    /** Returns how many receiver delay requests have been answered. */
    [[nodiscard]] std::uint64_t delayResponseCount() const noexcept {
        return delayResponses_.load();
    }

private:
    void run();
    void sendAnnounce();
    void sendSync();
    void pollRequests();

    std::string receiverHost_;
    std::string localAddress_;
    std::array<std::uint8_t, 8> clockIdentity_{};
    Socket event_;
    Socket general_;
    std::atomic_bool stopping_{false};
    std::atomic<std::uint64_t> delayResponses_{0};
    std::thread worker_;
    std::uint16_t syncSequence_ = 1;
    std::uint16_t announceSequence_ = 1;
};

} // namespace orynivo::airplay2
