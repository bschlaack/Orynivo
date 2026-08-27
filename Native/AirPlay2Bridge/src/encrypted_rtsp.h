// SPDX-License-Identifier: Apache-2.0
#pragma once

#include "encrypted_control.h"
#include "socket_transport.h"

#include <chrono>
#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace orynivo::airplay2 {

/** A complete response received through the encrypted AirPlay 2 control channel. */
struct RtspResponse final {
    int status = 0;
    std::map<std::string, std::string> headers;
    std::vector<std::uint8_t> body;
};

/** Sends authenticated RTSP requests after HAP pairing. */
class EncryptedRtsp final {
public:
    /** Creates the directional channel over an already paired socket. */
    EncryptedRtsp(Socket& socket, std::vector<std::uint8_t> writeKey,
                  std::vector<std::uint8_t> readKey, std::string senderId);

    /** Sends one request and waits for its complete authenticated response. */
    RtspResponse request(std::string method, std::string uri,
                         std::string contentType = {},
                         std::vector<std::uint8_t> body = {},
                         std::map<std::string, std::string> extraHeaders = {},
                         std::chrono::milliseconds timeout = std::chrono::seconds(10));

private:
    Socket& socket_;
    EncryptedControl writer_;
    EncryptedControl reader_;
    std::string senderId_;
    int sequence_ = 1;
};

} // namespace orynivo::airplay2
