// SPDX-License-Identifier: Apache-2.0
#pragma once

#include "socket_transport.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <span>
#include <thread>
#include <vector>

namespace orynivo::airplay2 {

/** Builds the NTP-backed realtime RTP synchronization datagram. */
[[nodiscard]] std::array<std::byte, 20> buildRtpSyncPacket(
    std::uint32_t nextTimestamp, std::uint32_t sampleRate, bool first);

/** Retains a bounded audio packet history and answers receiver resend requests. */
class RtpRetransmitResponder final {
public:
    /** Starts listening for resend requests on the supplied bound control socket. */
    explicit RtpRetransmitResponder(Socket& socket);
    ~RtpRetransmitResponder();
    RtpRetransmitResponder(const RtpRetransmitResponder&) = delete;
    RtpRetransmitResponder& operator=(const RtpRetransmitResponder&) = delete;

    /** Adds one complete RTP audio datagram to the bounded history. */
    void store(std::span<const std::byte> packet);

private:
    struct Slot {
        std::uint16_t sequence = 0;
        std::vector<std::byte> packet;
    };
    void run(std::stop_token stopToken);

    static constexpr std::size_t SlotCount = 512;
    Socket& socket_;
    std::array<Slot, SlotCount> slots_{};
    std::mutex mutex_;
    std::jthread thread_;
};

} // namespace orynivo::airplay2
