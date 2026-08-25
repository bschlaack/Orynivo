// SPDX-License-Identifier: Apache-2.0
#include "airplay2_bridge.h"
#include "socket_transport.h"

#include <chrono>
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
        notify(*session, AP2_STATE_PAIRING, "Transport connected; transient pairing is not implemented in this milestone.");
        return fail(AP2_NOT_IMPLEMENTED, "AirPlay 2 transient pairing is not implemented yet.");
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
