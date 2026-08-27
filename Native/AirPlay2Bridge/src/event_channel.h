// SPDX-License-Identifier: Apache-2.0
#pragma once

#include "encrypted_control.h"
#include "socket_transport.h"

#include <atomic>
#include <cstdint>
#include <functional>
#include <string>
#include <thread>
#include <vector>

namespace orynivo::airplay2 {

/** Transport command received from an AirPlay receiver. */
enum class MediaRemoteCommand { Play, Pause, Next, Previous };

/** Callback invoked for an authenticated receiver transport command. */
using MediaRemoteCommandCallback = std::function<void(MediaRemoteCommand)>;

/** Decrypts receiver event requests and returns authenticated RTSP success responses. */
class EventChannel final {
public:
    /** Starts the reverse event channel over an already connected socket. */
    EventChannel(Socket& socket, std::vector<std::uint8_t> incomingKey,
                 std::vector<std::uint8_t> outgoingKey,
                 MediaRemoteCommandCallback commandCallback = {});
    ~EventChannel();
    EventChannel(const EventChannel&) = delete;
    EventChannel& operator=(const EventChannel&) = delete;

    /** Returns the number of complete receiver requests answered. */
    [[nodiscard]] std::uint64_t answeredRequestCount() const noexcept;

    /** Returns whether authenticated event decoding has failed. */
    [[nodiscard]] bool failed() const noexcept;

private:
    void run(std::stop_token stopToken);
    void processPlaintext();

    Socket& socket_;
    EncryptedControl incoming_;
    EncryptedControl outgoing_;
    std::string plaintext_;
    std::jthread thread_;
    std::atomic<std::uint64_t> answered_{0};
    std::atomic<bool> failed_{false};
    MediaRemoteCommandCallback commandCallback_;
};

} // namespace orynivo::airplay2
