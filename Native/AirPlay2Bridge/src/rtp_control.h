// SPDX-License-Identifier: Apache-2.0
#pragma once

#include "socket_transport.h"

#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <span>
#include <thread>
#include <vector>

namespace orynivo::airplay2 {

/** Builds the NTP-backed realtime RTP synchronization datagram. */
[[nodiscard]] std::array<std::byte, 20> buildRtpSyncPacket(
    std::uint32_t nextTimestamp, std::uint32_t latencyFrames, bool first);

/** Builds the PTP-clocked realtime RTP synchronization datagram. */
[[nodiscard]] std::array<std::byte, 28> buildPtpRtpSyncPacket(
    std::uint32_t currentPlaybackTimestamp, std::uint32_t nextTimestamp,
    std::uint64_t clockNanoseconds, std::uint64_t clockId, bool first);

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

    /** Returns the number of receiver control datagrams observed. */
    [[nodiscard]] std::uint64_t receivedDatagramCount() const noexcept;

    /** Returns the number of valid receiver retransmission requests observed. */
    [[nodiscard]] std::uint64_t retransmitRequestCount() const noexcept;

    /** Returns the number of historical audio packets sent again. */
    [[nodiscard]] std::uint64_t resentPacketCount() const noexcept;

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
    std::atomic<std::uint64_t> receivedDatagrams_{0};
    std::atomic<std::uint64_t> retransmitRequests_{0};
    std::atomic<std::uint64_t> resentPackets_{0};
};

} // namespace orynivo::airplay2
