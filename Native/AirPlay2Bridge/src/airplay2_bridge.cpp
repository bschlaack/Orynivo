// SPDX-License-Identifier: Apache-2.0
#include "airplay2_bridge.h"
#include "airplay_crypto.h"
#include "buffered_audio_packetizer.h"
#include "encrypted_rtsp.h"
#include "event_channel.h"
#include "ntp_timing.h"
#include "packet_pacer.h"
#include "ptp_timing.h"
#include "realtime_audio_packetizer.h"
#include "rtp_control.h"
#include "socket_transport.h"
#include "transient_pairing.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

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
    Socket audio;
    Socket audioControl;
    Socket events;
    std::unique_ptr<orynivo::airplay2::EventChannel> eventChannel;
    orynivo::airplay2::TransientPairingKeys pairingKeys;
    std::unique_ptr<orynivo::airplay2::EncryptedRtsp> rtsp;
    std::unique_ptr<orynivo::airplay2::NtpTimingResponder> timing;
    std::unique_ptr<orynivo::airplay2::PtpTimingGrandmaster> ptp;
    std::unique_ptr<orynivo::airplay2::RealtimeAudioPacketizer> packetizer;
    std::unique_ptr<orynivo::airplay2::BufferedAudioPacketizer> bufferedPacketizer;
    std::unique_ptr<orynivo::airplay2::PacketPacer> pacer;
    std::unique_ptr<orynivo::airplay2::RtpRetransmitResponder> retransmit;
    std::vector<std::byte> pendingPcm;
    std::uint16_t eventPort = 0;
    std::uint16_t dataPort = 0;
    std::uint16_t controlPort = 0;
    std::uint32_t sessionId = 0;
    std::string rtspUri;
    bool buffered = false;
};

namespace {
thread_local std::string lastError;
constexpr std::uint32_t ReceiverLatencyFrames = 22050U + 44100U;

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

std::array<std::uint8_t, 8> createClockIdentity() {
    const auto random = fxchain::airplay::randomBytes(8);
    std::array<std::uint8_t, 8> result{};
    std::copy_n(random.begin(), result.size(), result.begin());
    result[0] = static_cast<std::uint8_t>((result[0] & 0xfcU) | 0x02U);
    return result;
}

fxchain::airplay::bplist::Value createTimingPeer(
    const std::string& id, std::uint64_t clockId, const std::string& address) {
    using namespace fxchain::airplay::bplist;
    Dict peer;
    peer.emplace_back("ID", Value::str(id));
    peer.emplace_back("DeviceType", Value::integer(0));
    peer.emplace_back("ClockID", Value::integer(static_cast<std::int64_t>(clockId)));
    peer.emplace_back("SupportsClockPortMatchingOverride", Value::boolean(false));
    Array addresses;
    addresses.push_back(Value::str(address));
    peer.emplace_back("Addresses", Value::array(std::move(addresses)));
    return Value::object(std::move(peer));
}

std::uint32_t nextMediaTimestamp(const ap2_session& session) {
    return session.bufferedPacketizer != nullptr
        ? session.bufferedPacketizer->nextTimestamp()
        : session.packetizer->nextTimestamp();
}

void appendDmapString(std::vector<std::uint8_t>& body, const char tag[4], const std::string& value) {
    body.insert(body.end(), tag, tag + 4);
    const auto length = static_cast<std::uint32_t>(value.size());
    body.push_back(static_cast<std::uint8_t>(length >> 24));
    body.push_back(static_cast<std::uint8_t>(length >> 16));
    body.push_back(static_cast<std::uint8_t>(length >> 8));
    body.push_back(static_cast<std::uint8_t>(length));
    body.insert(body.end(), value.begin(), value.end());
}

void prepareReceiverForAudio(ap2_session& session) {
    const std::string volume = "volume: -20.000000\r\n";
    const auto volumeResponse = session.rtsp->request(
        "SET_PARAMETER", session.rtspUri, "text/parameters",
        {volume.begin(), volume.end()});
    if (volumeResponse.status < 200 || volumeResponse.status >= 300)
        throw std::runtime_error("AirPlay receiver rejected initial volume with RTSP " +
                                 std::to_string(volumeResponse.status) + '.');

    std::vector<std::uint8_t> metadata(8, 0);
    metadata[0] = 'm'; metadata[1] = 'l'; metadata[2] = 'i'; metadata[3] = 't';
    metadata.insert(metadata.end(), {'m', 'i', 'k', 'd', 0, 0, 0, 1, 2});
    appendDmapString(metadata, "minm", "AirPlay 2 test signal");
    appendDmapString(metadata, "asar", "Orynivo");
    appendDmapString(metadata, "asal", "AirPlay 2 Bridge");
    metadata.insert(metadata.end(), {'a', 's', 't', 'n', 0, 0, 0, 2, 0, 1});
    const auto payloadSize = static_cast<std::uint32_t>(metadata.size() - 8);
    metadata[4] = static_cast<std::uint8_t>(payloadSize >> 24);
    metadata[5] = static_cast<std::uint8_t>(payloadSize >> 16);
    metadata[6] = static_cast<std::uint8_t>(payloadSize >> 8);
    metadata[7] = static_cast<std::uint8_t>(payloadSize);
    const auto metadataResponse = session.rtsp->request(
        "SET_PARAMETER", session.rtspUri, "application/x-dmap-tagged", metadata,
        {{"RTP-Info", "rtptime=" + std::to_string(nextMediaTimestamp(session))}});
    if (metadataResponse.status < 200 || metadataResponse.status >= 300)
        throw std::runtime_error("AirPlay receiver rejected initial metadata with RTSP " +
                                 std::to_string(metadataResponse.status) + '.');

    const auto feedbackResponse = session.rtsp->request("POST", "/feedback");
    if (feedbackResponse.status < 200 || feedbackResponse.status >= 300)
        throw std::runtime_error("AirPlay receiver rejected feedback with RTSP " +
                                 std::to_string(feedbackResponse.status) + '.');
    std::size_t activeStreams = 0;
    if (const auto feedback = fxchain::airplay::bplist::decode(feedbackResponse.body)) {
        if (const auto* streams = feedback->find("streams");
            streams != nullptr && streams->type == fxchain::airplay::bplist::Value::Type::Arr)
            activeStreams = streams->arr.size();
    }
    const auto message = "AirPlay 2 receiver feedback reports " +
        std::to_string(activeStreams) +
        (session.buffered
            ? " active stream(s); using native buffered type-103 TCP audio, ALAC, and PTP."
            : " active stream(s); using native realtime type-96 ALAC audio and PTP.");
    notify(session, AP2_STATE_NEGOTIATING, message.c_str());
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
        using namespace fxchain::airplay::bplist;
        const auto clockIdentity = createClockIdentity();
        const auto localAddress = session->control.localAddress();
        session->ptp = std::make_unique<orynivo::airplay2::PtpTimingGrandmaster>(
            session->host, localAddress, clockIdentity);
        session->buffered = false;
        const auto peerId = createUuid();
        Dict setup;
        setup.emplace_back("deviceID", Value::str("02:00:00:00:00:01"));
        setup.emplace_back("sessionUUID", Value::str(createUuid()));
        setup.emplace_back("timingProtocol", Value::str("PTP"));
        setup.emplace_back("timingPeerInfo",
                           createTimingPeer(peerId, session->ptp->clockId(), localAddress));
        Array timingPeers;
        timingPeers.push_back(createTimingPeer(peerId, session->ptp->clockId(), localAddress));
        setup.emplace_back("timingPeerList", Value::array(std::move(timingPeers)));
        setup.emplace_back("isMultiSelectAirPlay", Value::boolean(true));
        setup.emplace_back("groupContainsGroupLeader", Value::boolean(false));
        setup.emplace_back("macAddress", Value::str("02:00:00:00:00:01"));
        setup.emplace_back("model", Value::str("Orynivo1,1"));
        setup.emplace_back("name", Value::str("Orynivo"));
        setup.emplace_back("osName", Value::str("Orynivo"));
        setup.emplace_back("sourceVersion", Value::str("670.6.2"));
        setup.emplace_back("senderSupportsRelay", Value::boolean(false));
        setup.emplace_back("statsCollectionEnabled", Value::boolean(false));
        const auto body = encode(Value::object(std::move(setup)));
        session->sessionId = createSessionId();
        session->rtspUri = "rtsp://" + session->control.localAddress() + "/" +
                           std::to_string(session->sessionId);
        const auto setupResponse = session->rtsp->request(
            "SETUP", session->rtspUri, "application/x-apple-binary-plist", body,
            {{"X-Apple-StreamID", "1"}});
        if (setupResponse.status < 200 || setupResponse.status >= 300)
            throw std::runtime_error("AirPlay receiver rejected session SETUP with RTSP " +
                                     std::to_string(setupResponse.status) + '.');
        const auto decoded = decode(setupResponse.body);
        if (!decoded) throw std::runtime_error("AirPlay session SETUP returned an invalid binary property list.");
        if (const auto* eventPort = decoded->find("eventPort"))
            session->eventPort = static_cast<std::uint16_t>(eventPort->asInt());
        if (session->eventPort == 0)
            throw std::runtime_error("AirPlay session SETUP returned no event port.");
        notify(*session, AP2_STATE_NEGOTIATING, "Opening AirPlay 2 event channel.");
        session->events = Socket::connectTcp(session->host, session->eventPort, std::chrono::seconds(5));
        session->eventChannel = std::make_unique<orynivo::airplay2::EventChannel>(
            session->events, session->pairingKeys.eventWrite, session->pairingKeys.eventRead);

        session->audioControl = Socket::bindUdp();
        session->audio = Socket::bindUdp();
        notify(*session, AP2_STATE_NEGOTIATING, "Arming the AirPlay 2 recording session.");
        const auto recordResponse = session->rtsp->request(
            "RECORD", session->rtspUri, {}, {}, {{"Range", "npt=0-"}});
        if (recordResponse.status < 200 || recordResponse.status >= 300)
            throw std::runtime_error("AirPlay receiver rejected timeline RECORD with RTSP " +
                                     std::to_string(recordResponse.status) + '.');

        Dict stream;
        stream.emplace_back("audioFormat", Value::integer(0x40000));
        stream.emplace_back("audioMode", Value::str("default"));
        stream.emplace_back("controlPort", Value::integer(session->audioControl.localPort()));
        stream.emplace_back("dataPort", Value::integer(session->audio.localPort()));
        stream.emplace_back("ct", Value::integer(2));
        stream.emplace_back("isMedia", Value::boolean(true));
        stream.emplace_back("shk", Value::bytes(session->pairingKeys.audioKey));
        stream.emplace_back("spf", Value::integer(352));
        stream.emplace_back("sr", Value::integer(session->sampleRate));
        stream.emplace_back("type", Value::integer(96));
        stream.emplace_back("supportsDynamicStreamID", Value::boolean(false));
        stream.emplace_back("streamConnectionID", Value::integer(session->sessionId));
        Array streams;
        streams.push_back(Value::object(std::move(stream)));
        Dict streamSetup;
        streamSetup.emplace_back("streams", Value::array(std::move(streams)));
        const auto streamBody = encode(Value::object(std::move(streamSetup)));
        notify(*session, AP2_STATE_NEGOTIATING, "Negotiating AirPlay 2 realtime ALAC/PTP audio stream.");
        const auto streamResponse = session->rtsp->request(
            "SETUP", session->rtspUri, "application/x-apple-binary-plist", streamBody);
        if (streamResponse.status < 200 || streamResponse.status >= 300)
            throw std::runtime_error("AirPlay receiver rejected audio stream SETUP with RTSP " +
                                     std::to_string(streamResponse.status) + '.');
        const auto streamDecoded = decode(streamResponse.body);
        if (!streamDecoded) throw std::runtime_error("AirPlay stream SETUP returned an invalid binary property list.");
        if (const auto* responseStreams = streamDecoded->find("streams");
            responseStreams && responseStreams->type == Value::Type::Arr && !responseStreams->arr.empty()) {
            if (const auto* port = responseStreams->arr.front().find("dataPort"))
                session->dataPort = static_cast<std::uint16_t>(port->asInt());
            if (const auto* port = responseStreams->arr.front().find("controlPort"))
                session->controlPort = static_cast<std::uint16_t>(port->asInt());
        }
        if (session->dataPort == 0)
            throw std::runtime_error("AirPlay stream SETUP returned no audio data port.");
        if (session->controlPort == 0)
            throw std::runtime_error("AirPlay stream SETUP returned no RTP control port.");
        session->packetizer = std::make_unique<orynivo::airplay2::RealtimeAudioPacketizer>(
            session->sampleRate, session->pairingKeys.audioKey, session->sessionId,
            ReceiverLatencyFrames);
        session->pacer = std::make_unique<orynivo::airplay2::PacketPacer>(
            session->sampleRate,
            static_cast<std::uint32_t>(orynivo::airplay2::AlacEncoder::FramesPerPacket));
        session->retransmit = std::make_unique<orynivo::airplay2::RtpRetransmitResponder>(
            session->audioControl);
        session->pendingPcm.clear();

        Array peers;
        peers.push_back(Value::str(session->host));
        peers.push_back(Value::str(localAddress));
        const auto peersResponse = session->rtsp->request(
            "SETPEERS", session->rtspUri, "application/x-apple-binary-plist",
            encode(Value::array(std::move(peers))));
        if (peersResponse.status < 200 || peersResponse.status >= 300)
            throw std::runtime_error("AirPlay receiver rejected PTP peer registration with RTSP " +
                                     std::to_string(peersResponse.status) + '.');

        notify(*session, AP2_STATE_NEGOTIATING, "Anchoring the AirPlay 2 realtime stream to PTP.");
        notify(*session, AP2_STATE_NEGOTIATING, "Setting initial AirPlay 2 volume and metadata.");
        prepareReceiverForAudio(*session);
        notify(*session, AP2_STATE_STREAMING, "AirPlay 2 realtime ALAC/PTP stream is ready.");
        lastError.clear();
        return AP2_OK;
    } catch (const std::exception& error) {
        notify(*session, AP2_STATE_FAILED, error.what());
        return fail(AP2_NETWORK_ERROR, error.what());
    }
}

ap2_result AP2_CALL ap2_session_write_pcm(ap2_session* session, const void* samples, size_t byteCount, size_t* consumed) {
    if (consumed != nullptr) *consumed = 0;
    if (session == nullptr || (samples == nullptr && byteCount != 0))
        return fail(AP2_INVALID_ARGUMENT, "Invalid PCM buffer.");
    std::scoped_lock lock(session->mutex);
    if (session->state != AP2_STATE_STREAMING ||
        (session->packetizer == nullptr && session->bufferedPacketizer == nullptr))
        return fail(AP2_INVALID_STATE, "PCM is accepted only after the encrypted stream is active.");
    try {
        if (byteCount != 0) {
            const auto* input = static_cast<const std::byte*>(samples);
            session->pendingPcm.insert(session->pendingPcm.end(), input, input + byteCount);
        }
        const auto packetBytes = session->buffered
            ? orynivo::airplay2::BufferedAudioPacketizer::PcmBytesPerPacket
            : orynivo::airplay2::AlacEncoder::PcmBytesPerPacket;
        std::size_t offset = 0;
        while (session->pendingPcm.size() - offset >= packetBytes) {
            const auto packetCount = session->buffered
                ? session->bufferedPacketizer->packetCount()
                : session->packetizer->packetCount();
            session->pacer->waitFor(packetCount);
            if (!session->buffered && (packetCount == 0 || packetCount % 100 == 0)) {
                const auto sync = orynivo::airplay2::buildPtpRtpSyncPacket(
                    session->packetizer->nextTimestamp(), ReceiverLatencyFrames,
                    orynivo::airplay2::PtpTimingGrandmaster::nowNanoseconds(),
                    session->ptp->clockId(), packetCount == 0);
                session->audioControl.sendTo(session->host, session->controlPort, sync);
            }
            const auto pcm = std::span<const std::byte>(session->pendingPcm).subspan(offset, packetBytes);
            if (session->buffered) {
                const auto packet = session->bufferedPacketizer->packetize(pcm);
                session->audio.sendAll(packet);
            } else {
                const auto packet = session->packetizer->packetize(pcm);
                session->audio.sendTo(session->host, session->dataPort, packet);
                session->retransmit->store(packet);
            }
            offset += packetBytes;
        }
        if (offset != 0)
            session->pendingPcm.erase(session->pendingPcm.begin(),
                                      session->pendingPcm.begin() + static_cast<std::ptrdiff_t>(offset));
        if (consumed != nullptr) *consumed = byteCount;
        lastError.clear();
        return AP2_OK;
    } catch (const std::exception& error) {
        notify(*session, AP2_STATE_FAILED, error.what());
        return fail(AP2_NETWORK_ERROR, error.what());
    }
}

ap2_result AP2_CALL ap2_session_stop(ap2_session* session) {
    if (session == nullptr) return fail(AP2_INVALID_ARGUMENT, "Session is null.");
    std::scoped_lock lock(session->mutex);
    if (session->bufferedPacketizer != nullptr && session->ptp != nullptr) {
        const auto diagnostic = "AirPlay 2 media diagnostics: sent " +
            std::to_string(session->bufferedPacketizer->packetCount()) +
            " buffered TCP packet(s) to " + session->host + ':' +
            std::to_string(session->dataPort) + ", answered " +
            std::to_string(session->ptp->delayResponseCount()) + " PTP delay request(s).";
        notify(*session, AP2_STATE_NEGOTIATING, diagnostic.c_str());
    } else if (session->packetizer != nullptr && session->retransmit != nullptr &&
               (session->timing != nullptr || session->ptp != nullptr)) {
        const auto diagnostic = "AirPlay 2 media diagnostics: sent " +
            std::to_string(session->packetizer->packetCount()) + " RTP packets to " +
            session->host + ':' + std::to_string(session->dataPort) + ", received " +
            std::to_string(session->retransmit->receivedDatagramCount()) +
            " control datagram(s), " +
            std::to_string(session->retransmit->retransmitRequestCount()) +
            " retransmission request(s), resent " +
            std::to_string(session->retransmit->resentPacketCount()) + " packet(s), answered " +
            (session->ptp != nullptr
                ? std::to_string(session->ptp->delayResponseCount()) + " PTP delay request(s), answered "
                : std::to_string(session->timing->responseCount()) + " of " +
                  std::to_string(session->timing->requestCount()) + " NTP timing request(s), answered ") +
            std::to_string(session->eventChannel == nullptr ? 0 : session->eventChannel->answeredRequestCount()) +
            " encrypted event request(s)" +
            (session->eventChannel != nullptr && session->eventChannel->failed() ? " (event channel failed)." : ".");
        notify(*session, AP2_STATE_NEGOTIATING, diagnostic.c_str());
    }
    session->retransmit.reset();
    if (session->rtsp != nullptr && !session->rtspUri.empty()) {
        try {
            session->rtsp->request("TEARDOWN", session->rtspUri, {}, {}, {}, std::chrono::seconds(2));
        } catch (...) {
            // Teardown is best-effort; an unavailable receiver must not make destruction fail.
        }
    }
    session->rtsp.reset();
    session->control.close();
    session->timing.reset();
    session->ptp.reset();
    session->audio.close();
    session->audioControl.close();
    session->eventChannel.reset();
    session->events.close();
    session->bufferedPacketizer.reset();
    session->packetizer.reset();
    session->pacer.reset();
    session->pendingPcm.clear();
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
