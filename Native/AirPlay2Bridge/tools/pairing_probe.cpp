// SPDX-License-Identifier: Apache-2.0
#include "airplay2_bridge.h"

#include <cstdlib>
#include <iostream>

namespace {
void AP2_CALL onState(void*, ap2_state state, const char* message) {
    std::cout << "state=" << static_cast<int>(state) << " "
              << (message == nullptr ? "" : message) << '\n';
}
}

int main(int argc, char** argv) {
    if (argc < 2 || argc > 3) {
        std::cerr << "usage: AirPlay2BridgeProbe <host> [port]\n";
        return 2;
    }
    const auto port = argc == 3 ? std::atoi(argv[2]) : 7000;
    ap2_session_config config{};
    config.struct_size = sizeof(config);
    config.host_utf8 = argv[1];
    config.port = static_cast<std::uint16_t>(port);
    config.device_name_utf8 = "AirPlay 2 receiver";
    config.device_id_utf8 = "manual-probe";
    config.sample_rate = 44100;
    config.channels = 2;
    config.bits_per_sample = 16;
    config.state_callback = onState;

    ap2_session* session = nullptr;
    auto result = ap2_session_create(&config, &session);
    if (result == AP2_OK)
        result = ap2_session_start(session);
    std::cout << "result=" << static_cast<int>(result)
              << " error=" << ap2_get_last_error() << '\n';
    ap2_session_destroy(session);
    return result == AP2_NOT_IMPLEMENTED ? 0 : static_cast<int>(result);
}
