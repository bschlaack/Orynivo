// SPDX-License-Identifier: Apache-2.0
#include "airplay2_bridge.h"

#include <cstdlib>
#include <cmath>
#include <chrono>
#include <cstdint>
#include <iostream>
#include <thread>
#include <string_view>
#include <vector>

namespace {
void AP2_CALL onState(void*, ap2_state state, const char* message) {
    std::cout << "state=" << static_cast<int>(state) << " "
              << (message == nullptr ? "" : message) << '\n';
}
}

int main(int argc, char** argv) {
    if (argc < 2 || argc > 4) {
        std::cerr << "usage: AirPlay2BridgeProbe <host> [port] [--tone]\n";
        return 2;
    }
    auto port = 7000;
    auto playTone = false;
    for (int i = 2; i < argc; ++i) {
        if (std::string_view(argv[i]) == "--tone") playTone = true;
        else port = std::atoi(argv[i]);
    }
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
    if (result == AP2_OK && playTone) {
        constexpr std::uint32_t sampleRate = 44100;
        constexpr auto frames = sampleRate * 3U;
        std::vector<std::int16_t> pcm(frames * 2U);
        for (std::uint32_t frame = 0; frame < frames; ++frame) {
            const auto sample = static_cast<std::int16_t>(
                std::sin(2.0 * 3.14159265358979323846 * 440.0 * frame / sampleRate) * 1000.0);
            pcm[frame * 2U] = sample;
            pcm[frame * 2U + 1U] = sample;
        }
        std::size_t consumed = 0;
        result = ap2_session_write_pcm(session, pcm.data(), pcm.size() * sizeof(std::int16_t), &consumed);
        std::cout << "tone-bytes=" << consumed << '\n';
        std::this_thread::sleep_for(std::chrono::seconds(3));
    }
    std::cout << "result=" << static_cast<int>(result)
              << " error=" << ap2_get_last_error() << '\n';
    ap2_session_destroy(session);
    return result == AP2_NOT_IMPLEMENTED ? 0 : static_cast<int>(result);
}
