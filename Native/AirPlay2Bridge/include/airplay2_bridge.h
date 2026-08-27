// SPDX-License-Identifier: Apache-2.0
#pragma once

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#  if defined(AIRPLAY2BRIDGE_EXPORTS)
#    define AP2_API __declspec(dllexport)
#  else
#    define AP2_API __declspec(dllimport)
#  endif
#  define AP2_CALL __cdecl
#else
#  define AP2_API __attribute__((visibility("default")))
#  define AP2_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

/** Opaque sender-session handle owned by the bridge. */
typedef struct ap2_session ap2_session;

/** Stable result codes returned by every bridge operation. */
typedef enum ap2_result {
    AP2_OK = 0,
    AP2_INVALID_ARGUMENT = 1,
    AP2_INVALID_STATE = 2,
    AP2_NETWORK_ERROR = 3,
    AP2_AUTHENTICATION_ERROR = 4,
    AP2_PROTOCOL_ERROR = 5,
    AP2_NOT_IMPLEMENTED = 6,
    AP2_INTERNAL_ERROR = 7
} ap2_result;

/** Session states emitted to the host callback. */
typedef enum ap2_state {
    AP2_STATE_IDLE = 0,
    AP2_STATE_CONNECTING = 1,
    AP2_STATE_PAIRING = 2,
    AP2_STATE_STREAMING = 3,
    AP2_STATE_STOPPED = 4,
    AP2_STATE_FAILED = 5,
    AP2_STATE_NEGOTIATING = 6
} ap2_state;

/** Host callback for lifecycle changes. The message is valid only for the call. */
typedef void(AP2_CALL *ap2_state_callback)(
    void* user_data,
    ap2_state state,
    const char* message_utf8);

/** Configuration copied by ap2_session_create; pointers need not outlive it. */
typedef struct ap2_session_config {
    uint32_t struct_size;
    const char* host_utf8;
    uint16_t port;
    const char* device_name_utf8;
    const char* device_id_utf8;
    uint32_t sample_rate;
    uint16_t channels;
    uint16_t bits_per_sample;
    ap2_state_callback state_callback;
    void* user_data;
} ap2_session_config;

/** Returns the ABI version encoded as major * 10000 + minor * 100 + patch. */
AP2_API uint32_t AP2_CALL ap2_get_abi_version(void);

/** Creates one independent sender session. */
AP2_API ap2_result AP2_CALL ap2_session_create(
    const ap2_session_config* config,
    ap2_session** session);

/** Sets the receiver volume in decibels before session start (default: -20 dB). */
AP2_API ap2_result AP2_CALL ap2_session_set_initial_volume(
    ap2_session* session,
    float volume_db);

/** Starts the asynchronous AirPlay 2 connection and transient pairing flow. */
AP2_API ap2_result AP2_CALL ap2_session_start(ap2_session* session);

/** Supplies interleaved signed little-endian PCM frames to an active session. */
AP2_API ap2_result AP2_CALL ap2_session_write_pcm(
    ap2_session* session,
    const void* samples,
    size_t byte_count,
    size_t* bytes_consumed);

/** Stops network activity. Safe to call repeatedly. */
AP2_API ap2_result AP2_CALL ap2_session_stop(ap2_session* session);

/** Releases a stopped session. A null pointer is accepted. */
AP2_API void AP2_CALL ap2_session_destroy(ap2_session* session);

/** Returns a thread-local diagnostic for the last failing bridge call. */
AP2_API const char* AP2_CALL ap2_get_last_error(void);

#ifdef __cplusplus
}
#endif
