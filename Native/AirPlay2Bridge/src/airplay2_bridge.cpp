// SPDX-License-Identifier: Apache-2.0
#include "airplay2_bridge.h"
#include "airplay_crypto.h"
#include "encrypted_rtsp.h"
#include "socket_transport.h"
#include "transient_pairing.h"

#include <chrono>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>

using orynivo::airplay2::Socket;

struct ap2_session {
    std::mutex mutex;
    std::string host;
    std::string name;
    std::string id;
    std::uint16_t port = 7000;
    std::uint32_t sampleRate = 44100;
    std::uint16_t channels = 2;
    std::uint16_t bitsPerSample = 16;
    ap2_state state = AP2_STATE_IDLE;
    ap2_state_callback callback = nullptr;
    void* userData = nullptr;
    Socket control;
    Socket timing;
    orynivo::airplay2::TransientPairingKeys pairingKeys;
    std::unique_ptr<orynivo::airplay2::EncryptedRtsp> rtsp;
    std::uint16_t eventPort = 0;
};

namespace {
thread_local std::string lastError;

ap2_result fail(ap2_result result, std::string message) {
    lastError = std::move(message);
    return result;
}

void notify(ap2_session& session, ap2_state state, const char* message) {
    session.state = state;
    if (session.callback != nullptr)
        session.callback(session.userData, state, message);
}

std::string createSenderId() {
    const auto bytes = fxchain::airplay::randomBytes(8);
    constexpr char hex[] = "0123456789ABCDEF";
    std::string result;
    result.reserve(16);
    for (const auto value : bytes) {
        result.push_back(hex[value >> 4]);
        result.push_back(hex[value & 0x0f]);
    }
    return result;
}

std::string createUuid() {
    const auto bytes = fxchain::airplay::randomBytes(16);
    constexpr char hex[] = "0123456789ABCDEF";
    std::string value;
    for (std::size_t i = 0; i < bytes.size(); ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) value.push_back('-');
        value.push_back(hex[bytes[i] >> 4]);
        value.push_back(hex[bytes[i] & 0x0f]);
    }
    return value;
}

std::uint32_t createSessionId() {
    const auto bytes = fxchain::airplay::randomBytes(4);
    return (static_cast<std::uint32_t>(bytes[0]) << 24) |
           (static_cast<std::uint32_t>(bytes[1]) << 16) |
           (static_cast<std::uint32_t>(bytes[2]) << 8) | bytes[3];
}
} // namespace

uint32_t AP2_CALL ap2_get_abi_version(void) { return 100; }

ap2_result AP2_CALL ap2_session_create(const ap2_session_config* config, ap2_session** session) {
    if (config == nullptr || session == nullptr || config->struct_size < sizeof(ap2_session_config) ||
        config->host_utf8 == nullptr || config->host_utf8[0] == '\0' || config->port == 0)
        return fail(AP2_INVALID_ARGUMENT, "Invalid AirPlay 2 session configuration.");
    if (config->channels != 2 || config->bits_per_sample != 16 || config->sample_rate == 0)
        return fail(AP2_INVALID_ARGUMENT, "The first bridge ABI accepts stereo signed 16-bit PCM only.");
    try {
        auto value = std::make_unique<ap2_session>();
        value->host = config->host_utf8;
        value->name = config->device_name_utf8 == nullptr ? "AirPlay 2" : config->device_name_utf8;
        value->id = config->device_id_utf8 == nullptr ? "" : config->device_id_utf8;
        value->port = config->port;
        value->sampleRate = config->sample_rate;
        value->channels = config->channels;
        value->bitsPerSample = config->bits_per_sample;
        value->callback = config->state_callback;
        value->userData = config->user_data;
        *session = value.release();
        lastError.clear();
        return AP2_OK;
    } catch (const std::exception& error) {
        return fail(AP2_INTERNAL_ERROR, error.what());
    }
}

ap2_result AP2_CALL ap2_session_start(ap2_session* session) {
    if (session == nullptr) return fail(AP2_INVALID_ARGUMENT, "Session is null.");
    std::scoped_lock lock(session->mutex);
    if (session->state != AP2_STATE_IDLE && session->state != AP2_STATE_STOPPED)
        return fail(AP2_INVALID_STATE, "Session has already been started.");
    try {
        notify(*session, AP2_STATE_CONNECTING, "Connecting to AirPlay 2 receiver.");
        session->control = Socket::connectTcp(session->host, session->port, std::chrono::seconds(5));
        notify(*session, AP2_STATE_PAIRING, "Performing fail-closed AirPlay 2 transient pairing.");
        session->pairingKeys = orynivo::airplay2::TransientPairing::run(session->control);
        notify(*session, AP2_STATE_NEGOTIATING, "Opening authenticated AirPlay 2 control channel.");
        session->rtsp = std::make_unique<orynivo::airplay2::EncryptedRtsp>(session->control,
            session->pairingKeys.controlWrite, session->pairingKeys.controlRead, createSenderId());
        const auto info = session->rtsp->request("GET", "/info");
        if (info.status < 200 || info.status >= 300)
            throw std::runtime_error("AirPlay receiver rejected encrypted GET /info with RTSP " +
                                     std::to_string(info.status) + '.');
        session->timing = Socket::bindUdp();
        using namespace fxchain::airplay::bplist;
        Dict setup;
        setup.emplace_back("deviceID", Value::str("02:00:00:00:00:01"));
        setup.emplace_back("sessionUUID", Value::str(createUuid()));
        setup.emplace_back("timingPort", Value::integer(session->timing.localPort()));
        setup.emplace_back("timingProtocol", Value::str("NTP"));
        setup.emplace_back("isMultiSelectAirPlay", Value::boolean(false));
        setup.emplace_back("groupContainsGroupLeader", Value::boolean(false));
        setup.emplace_back("macAddress", Value::str("02:00:00:00:00:01"));
        setup.emplace_back("model", Value::str("Orynivo1,1"));
        setup.emplace_back("name", Value::str("Orynivo"));
        setup.emplace_back("osName", Value::str("Orynivo"));
        setup.emplace_back("sourceVersion", Value::str("670.6.2"));
        setup.emplace_back("senderSupportsRelay", Value::boolean(false));
        setup.emplace_back("statsCollectionEnabled", Value::boolean(false));
        const auto body = encode(Value::object(std::move(setup)));
        const auto uri = "rtsp://" + session->control.localAddress() + "/" + std::to_string(createSessionId());
        const auto setupResponse = session->rtsp->request(
            "SETUP", uri, "application/x-apple-binary-plist", body);
        if (setupResponse.status < 200 || setupResponse.status >= 300)
            throw std::runtime_error("AirPlay receiver rejected session SETUP with RTSP " +
                                     std::to_string(setupResponse.status) + '.');
        const auto decoded = decode(setupResponse.body);
        if (!decoded) throw std::runtime_error("AirPlay session SETUP returned an invalid binary property list.");
        if (const auto* eventPort = decoded->find("eventPort"))
            session->eventPort = static_cast<std::uint16_t>(eventPort->asInt());
        return fail(AP2_NOT_IMPLEMENTED, "AirPlay 2 session SETUP verified; event and audio streams are not implemented yet.");
    } catch (const std::exception& error) {
        notify(*session, AP2_STATE_FAILED, error.what());
        return fail(AP2_NETWORK_ERROR, error.what());
    }
}

ap2_result AP2_CALL ap2_session_write_pcm(ap2_session* session, const void* samples, size_t byteCount, size_t* consumed) {
    if (consumed != nullptr) *consumed = 0;
    if (session == nullptr || (samples == nullptr && byteCount != 0))
        return fail(AP2_INVALID_ARGUMENT, "Invalid PCM buffer.");
    return fail(AP2_INVALID_STATE, "PCM is accepted only after the encrypted stream is active.");
}

ap2_result AP2_CALL ap2_session_stop(ap2_session* session) {
    if (session == nullptr) return fail(AP2_INVALID_ARGUMENT, "Session is null.");
    std::scoped_lock lock(session->mutex);
    session->control.close();
    session->timing.close();
    session->rtsp.reset();
    notify(*session, AP2_STATE_STOPPED, "AirPlay 2 session stopped.");
    lastError.clear();
    return AP2_OK;
}

void AP2_CALL ap2_session_destroy(ap2_session* session) {
    if (session == nullptr) return;
    ap2_session_stop(session);
    delete session;
}

const char* AP2_CALL ap2_get_last_error(void) { return lastError.c_str(); }
