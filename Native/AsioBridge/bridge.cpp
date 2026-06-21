#define NOMINMAX
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <mutex>
#include <sstream>
#include <string>
#include <vector>
#include <windows.h>
#ifdef ORYNIVO_CWASIO
extern "C"
{
#include "cwASIO.h"
#include "asio.h"
}
#else
#include "asiosys.h"
#include "asio.h"
#include "asiodrivers.h"

extern bool loadAsioDriver(char* name);
#endif

namespace
{
    constexpr int kMaxOutputs = 2;
    constexpr int kDriverNameLength = 128;

    struct BridgeState
    {
        ASIODriverInfo driverInfo{};
        ASIOBufferInfo bufferInfos[kMaxOutputs]{};
        ASIOChannelInfo channelInfos[kMaxOutputs]{};
        ASIOCallbacks callbacks{};
        long inputChannels = 0;
        long outputChannels = 0;
        long minBufferSize = 0;
        long maxBufferSize = 0;
        long preferredBufferSize = 0;
        long granularity = 0;
        long outputChannelsInUse = 0;
        ASIOSampleRate sampleRate = 0;
        std::vector<float> ring;
        std::vector<unsigned char> dsdRing;
        size_t readIndex = 0;
        size_t writeIndex = 0;
        size_t availableSamples = 0;
        std::atomic<float> volume{ 1.0f };
        std::mutex mutex;
        bool initialized = false;
        bool driverLoaded = false;
        bool driverInitialized = false;
        bool dsdMode = false;
    };

    BridgeState g_state;

#ifdef ORYNIVO_CWASIO
    struct DriverEntry
    {
        std::string name;
        std::string id;
    };

    bool collectDriver(void* context, const char* name, const char* id, const char*)
    {
        if (context && name && id && *id)
        {
            static_cast<std::vector<DriverEntry>*>(context)->push_back({ name, id });
        }
        return true;
    }

    std::vector<DriverEntry> getDrivers()
    {
        std::vector<DriverEntry> drivers;
        cwASIOenumerate(collectDriver, &drivers);
        return drivers;
    }

    bool loadDriver(const char* name)
    {
        const auto drivers = getDrivers();
        const auto driver = std::find_if(
            drivers.begin(),
            drivers.end(),
            [name](const DriverEntry& item) { return item.name == name; });
        return driver != drivers.end() &&
               ASIOLoad(driver->id.c_str(), driver->name.c_str()) == ASE_OK;
    }

    void unloadDriver()
    {
        ASIOUnload();
    }
#else
    bool loadDriver(const char* name)
    {
        char mutableName[kDriverNameLength]{};
        strncpy_s(mutableName, name, _TRUNCATE);
        return loadAsioDriver(mutableName);
    }

    void unloadDriver()
    {
    }
#endif

    long framesPerCallback()
    {
        return g_state.dsdMode ? (g_state.preferredBufferSize / 8) : g_state.preferredBufferSize;
    }

    const char* sampleTypeName(ASIOSampleType type)
    {
        switch (type)
        {
        case ASIOSTInt16LSB: return "Int16LSB";
        case ASIOSTInt24LSB: return "Int24LSB";
        case ASIOSTInt32LSB: return "Int32LSB";
        case ASIOSTInt32LSB16: return "Int32LSB16";
        case ASIOSTInt32LSB18: return "Int32LSB18";
        case ASIOSTInt32LSB20: return "Int32LSB20";
        case ASIOSTInt32LSB24: return "Int32LSB24";
        case ASIOSTFloat32LSB: return "Float32LSB";
        case ASIOSTFloat64LSB: return "Float64LSB";
        case ASIOSTDSDInt8LSB1: return "DSDInt8LSB1";
        case ASIOSTDSDInt8MSB1: return "DSDInt8MSB1";
        case ASIOSTDSDInt8NER8: return "DSDInt8NER8";
        default: return "Other";
        }
    }

    std::string channelTypesForCurrentMode(long outputChannels)
    {
        std::ostringstream output;
        for (long channel = 0; channel < outputChannels; ++channel)
        {
            ASIOChannelInfo info{};
            info.channel = channel;
            info.isInput = ASIOFalse;
            if (ASIOGetChannelInfo(&info) != ASE_OK)
            {
                continue;
            }

            if (channel > 0)
            {
                output << ",";
            }

            output << sampleTypeName(info.type);
        }

        return output.str();
    }

    void writeSample(void* destination, ASIOSampleType type, float value, long frame)
    {
        value = std::clamp(value, -1.0f, 1.0f);

        switch (type)
        {
        case ASIOSTFloat32LSB:
            reinterpret_cast<float*>(destination)[frame] = value;
            break;
        case ASIOSTInt32LSB:
        case ASIOSTInt32LSB24:
            reinterpret_cast<long*>(destination)[frame] = static_cast<long>(value * 2147483647.0f);
            break;
        case ASIOSTInt24LSB:
        {
            auto scaled = static_cast<long>(value * 8388607.0f);
            auto* bytes = reinterpret_cast<unsigned char*>(destination) + (frame * 3);
            bytes[0] = static_cast<unsigned char>(scaled & 0xFF);
            bytes[1] = static_cast<unsigned char>((scaled >> 8) & 0xFF);
            bytes[2] = static_cast<unsigned char>((scaled >> 16) & 0xFF);
            break;
        }
        case ASIOSTInt16LSB:
            reinterpret_cast<short*>(destination)[frame] = static_cast<short>(value * 32767.0f);
            break;
        default:
            std::memset(destination, 0, static_cast<size_t>(g_state.preferredBufferSize) * sizeof(float));
            break;
        }
    }

    unsigned char reverseBits(unsigned char value)
    {
        value = static_cast<unsigned char>(((value & 0xF0) >> 4) | ((value & 0x0F) << 4));
        value = static_cast<unsigned char>(((value & 0xCC) >> 2) | ((value & 0x33) << 2));
        value = static_cast<unsigned char>(((value & 0xAA) >> 1) | ((value & 0x55) << 1));
        return value;
    }

    void writeDsdSample(void* destination, ASIOSampleType type, unsigned char value, long frame)
    {
        switch (type)
        {
        case ASIOSTDSDInt8LSB1:
            reinterpret_cast<unsigned char*>(destination)[frame] = value;
            break;
        case ASIOSTDSDInt8MSB1:
            reinterpret_cast<unsigned char*>(destination)[frame] = reverseBits(value);
            break;
        default:
            reinterpret_cast<unsigned char*>(destination)[frame] = 0;
            break;
        }
    }

    ASIOTime* bufferSwitchTimeInfo(ASIOTime* timeInfo, long index, ASIOBool processNow)
    {
        std::lock_guard<std::mutex> lock(g_state.mutex);

        for (long frame = 0; frame < framesPerCallback(); ++frame)
        {
            const auto activeRingSize = g_state.dsdMode ? g_state.dsdRing.size() : g_state.ring.size();
            const bool hasFrame =
                activeRingSize > 0 &&
                g_state.availableSamples >= static_cast<size_t>(g_state.outputChannelsInUse);

            for (long channel = 0; channel < g_state.outputChannelsInUse; ++channel)
            {
                auto* destination = g_state.bufferInfos[channel].buffers[index];
                auto sampleType = g_state.channelInfos[channel].type;
                if (g_state.dsdMode)
                {
                    unsigned char value = 0x69;
                    if (hasFrame)
                    {
                        auto sourceIndex = (g_state.readIndex + static_cast<size_t>(channel)) % g_state.dsdRing.size();
                        value = g_state.dsdRing[sourceIndex];
                    }

                    writeDsdSample(destination, sampleType, value, frame);
                }
                else
                {
                    float value = 0.0f;
                    if (hasFrame)
                    {
                        auto sourceIndex = (g_state.readIndex + static_cast<size_t>(channel)) % g_state.ring.size();
                        value = g_state.ring[sourceIndex];
                    }

                    writeSample(destination, sampleType, value * g_state.volume.load(), frame);
                }
            }

            if (hasFrame)
            {
                g_state.readIndex = (g_state.readIndex + static_cast<size_t>(g_state.outputChannelsInUse)) % activeRingSize;
                g_state.availableSamples -= static_cast<size_t>(g_state.outputChannelsInUse);
            }
        }

        ASIOOutputReady();
        return timeInfo;
    }

    void bufferSwitch(long index, ASIOBool processNow)
    {
        ASIOTime timeInfo{};
        bufferSwitchTimeInfo(&timeInfo, index, processNow);
    }

    void sampleRateChanged(ASIOSampleRate sampleRate)
    {
        g_state.sampleRate = sampleRate;
    }

    long asioMessages(long selector, long value, void*, double*)
    {
        switch (selector)
        {
        case kAsioSelectorSupported:
            return value == kAsioEngineVersion ||
                   value == kAsioSupportsTimeInfo ||
                   value == kAsioSupportsTimeCode ||
                   value == kAsioResetRequest ||
                   value == kAsioResyncRequest ||
                   value == kAsioLatenciesChanged;
        case kAsioEngineVersion:
            return 2;
        case kAsioSupportsTimeInfo:
        case kAsioSupportsTimeCode:
            return 1;
        default:
            return 0;
        }
    }
}

extern "C"
{
    __declspec(dllexport) int asio_get_driver_count()
    {
#ifdef ORYNIVO_CWASIO
        return static_cast<int>(getDrivers().size());
#else
        AsioDrivers drivers;
        return static_cast<int>(drivers.asioGetNumDev());
#endif
    }

    __declspec(dllexport) int asio_get_driver_name(int index, char* buffer, int bufferLength)
    {
        if (!buffer || bufferLength <= 0)
        {
            return -1;
        }

#ifdef ORYNIVO_CWASIO
        const auto drivers = getDrivers();
        if (index < 0 || static_cast<size_t>(index) >= drivers.size())
        {
            return -1;
        }
        strncpy_s(buffer, bufferLength, drivers[static_cast<size_t>(index)].name.c_str(), _TRUNCATE);
        return 0;
#else
        AsioDrivers drivers;
        return drivers.asioGetDriverName(index, buffer, bufferLength);
#endif
    }

    __declspec(dllexport) int asio_open(const char* driverName, double sampleRate, int outputChannels, int dsdMode)
    {
        if (!driverName || outputChannels <= 0 || outputChannels > kMaxOutputs)
        {
            return -1;
        }

        if (!loadDriver(driverName))
        {
            return -2;
        }
        g_state.driverLoaded = true;

        const auto failOpen = [](int code, bool buffersCreated = false)
        {
            if (buffersCreated)
            {
                ASIODisposeBuffers();
            }
            if (g_state.driverInitialized)
            {
                ASIOExit();
            }
            if (g_state.driverLoaded)
            {
                unloadDriver();
            }
            g_state.driverLoaded = false;
            g_state.driverInitialized = false;
            g_state.dsdMode = false;
            return code;
        };

        g_state.driverInfo.sysRef = GetDesktopWindow();
        if (ASIOInit(&g_state.driverInfo) != ASE_OK)
        {
            return failOpen(-3);
        }
        g_state.driverInitialized = true;

        if (ASIOGetChannels(&g_state.inputChannels, &g_state.outputChannels) != ASE_OK ||
            g_state.outputChannels < outputChannels)
        {
            return failOpen(-4);
        }

        g_state.dsdMode = dsdMode != 0;
        if (g_state.dsdMode)
        {
            ASIOIoFormat requestedFormat{ kASIODSDFormat };
            if (ASIOFuture(kAsioSetIoFormat, &requestedFormat) != ASE_SUCCESS ||
                requestedFormat.FormatType != kASIODSDFormat)
            {
                return failOpen(-10);
            }
        }

        if (ASIOCanSampleRate(sampleRate) != ASE_OK ||
            ASIOSetSampleRate(sampleRate) != ASE_OK ||
            ASIOGetSampleRate(&g_state.sampleRate) != ASE_OK)
        {
            return failOpen(-5);
        }

        if (ASIOGetBufferSize(&g_state.minBufferSize, &g_state.maxBufferSize, &g_state.preferredBufferSize, &g_state.granularity) != ASE_OK)
        {
            return failOpen(-6);
        }

        g_state.outputChannelsInUse = outputChannels;
        for (long channel = 0; channel < g_state.outputChannelsInUse; ++channel)
        {
            g_state.bufferInfos[channel].isInput = ASIOFalse;
            g_state.bufferInfos[channel].channelNum = channel;
            g_state.bufferInfos[channel].buffers[0] = nullptr;
            g_state.bufferInfos[channel].buffers[1] = nullptr;
        }

        g_state.callbacks.bufferSwitch = bufferSwitch;
        g_state.callbacks.sampleRateDidChange = sampleRateChanged;
        g_state.callbacks.asioMessage = asioMessages;
        g_state.callbacks.bufferSwitchTimeInfo = bufferSwitchTimeInfo;

        if (ASIOCreateBuffers(g_state.bufferInfos, g_state.outputChannelsInUse, g_state.preferredBufferSize, &g_state.callbacks) != ASE_OK)
        {
            return failOpen(-7);
        }

        for (long channel = 0; channel < g_state.outputChannelsInUse; ++channel)
        {
            g_state.channelInfos[channel].channel = channel;
            g_state.channelInfos[channel].isInput = ASIOFalse;
            if (ASIOGetChannelInfo(&g_state.channelInfos[channel]) != ASE_OK)
            {
                return failOpen(-8, true);
            }

            if (g_state.dsdMode &&
                g_state.channelInfos[channel].type != ASIOSTDSDInt8LSB1 &&
                g_state.channelInfos[channel].type != ASIOSTDSDInt8MSB1)
            {
                return failOpen(-11, true);
            }
        }

        if (g_state.dsdMode)
        {
            g_state.dsdRing.assign(static_cast<size_t>(framesPerCallback()) *
                                   static_cast<size_t>(g_state.outputChannelsInUse) * 8, 0x69);
            g_state.ring.clear();
        }
        else
        {
            g_state.ring.assign(static_cast<size_t>(g_state.preferredBufferSize) *
                                static_cast<size_t>(g_state.outputChannelsInUse) * 8, 0.0f);
            g_state.dsdRing.clear();
        }
        g_state.readIndex = 0;
        g_state.writeIndex = 0;
        g_state.availableSamples = 0;
        g_state.initialized = true;
        return 0;
    }

    __declspec(dllexport) int asio_start()
    {
        return g_state.initialized && ASIOStart() == ASE_OK ? 0 : -1;
    }

    __declspec(dllexport) int asio_write_interleaved(const float* samples, int sampleCount)
    {
        if (!g_state.initialized || g_state.dsdMode || !samples || sampleCount <= 0 ||
            sampleCount % g_state.outputChannelsInUse != 0)
        {
            return -1;
        }

        std::lock_guard<std::mutex> lock(g_state.mutex);
        auto freeSamples = g_state.ring.size() - g_state.availableSamples;
        auto accepted = std::min(static_cast<size_t>(sampleCount), freeSamples);
        accepted -= accepted % static_cast<size_t>(g_state.outputChannelsInUse);

        for (size_t index = 0; index < accepted; ++index)
        {
            g_state.ring[g_state.writeIndex] = samples[index];
            g_state.writeIndex = (g_state.writeIndex + 1) % g_state.ring.size();
        }

        g_state.availableSamples += accepted;
        return static_cast<int>(accepted);
    }

    __declspec(dllexport) int asio_write_dsd_interleaved(const unsigned char* bytes, int byteCount)
    {
        if (!g_state.initialized || !g_state.dsdMode || !bytes || byteCount <= 0 ||
            byteCount % g_state.outputChannelsInUse != 0)
        {
            return -1;
        }

        std::lock_guard<std::mutex> lock(g_state.mutex);
        auto freeBytes = g_state.dsdRing.size() - g_state.availableSamples;
        auto accepted = std::min(static_cast<size_t>(byteCount), freeBytes);
        accepted -= accepted % static_cast<size_t>(g_state.outputChannelsInUse);

        for (size_t index = 0; index < accepted; ++index)
        {
            g_state.dsdRing[g_state.writeIndex] = bytes[index];
            g_state.writeIndex = (g_state.writeIndex + 1) % g_state.dsdRing.size();
        }

        g_state.availableSamples += accepted;
        return static_cast<int>(accepted);
    }

    __declspec(dllexport) int asio_stop()
    {
        return ASIOStop() == ASE_OK ? 0 : -1;
    }

    __declspec(dllexport) void asio_close()
    {
        if (!g_state.initialized && !g_state.driverLoaded)
        {
            return;
        }

        if (g_state.initialized)
        {
            ASIOStop();
            ASIODisposeBuffers();
        }
        if (g_state.driverInitialized)
        {
            ASIOExit();
        }
        if (g_state.driverLoaded)
        {
            unloadDriver();
        }
        std::lock_guard<std::mutex> lock(g_state.mutex);
        g_state.ring.clear();
        g_state.dsdRing.clear();
        g_state.readIndex = 0;
        g_state.writeIndex = 0;
        g_state.availableSamples = 0;
        g_state.outputChannelsInUse = 0;
        g_state.driverLoaded = false;
        g_state.driverInitialized = false;
        g_state.dsdMode = false;
        g_state.initialized = false;
    }

    __declspec(dllexport) int asio_get_preferred_buffer_size()
    {
        return static_cast<int>(g_state.preferredBufferSize);
    }

    __declspec(dllexport) int asio_get_buffered_frames()
    {
        if (!g_state.initialized || g_state.outputChannelsInUse <= 0)
        {
            return 0;
        }

        std::lock_guard<std::mutex> lock(g_state.mutex);
        return static_cast<int>(
            g_state.availableSamples / static_cast<size_t>(g_state.outputChannelsInUse));
    }

    __declspec(dllexport) void asio_clear_buffer()
    {
        std::lock_guard<std::mutex> lock(g_state.mutex);
        g_state.readIndex = 0;
        g_state.writeIndex = 0;
        g_state.availableSamples = 0;
    }

    __declspec(dllexport) void asio_set_volume(float volume)
    {
        g_state.volume.store(std::clamp(volume, 0.0f, 1.0f));
    }

    __declspec(dllexport) int asio_get_device_info(const char* driverName, char* buffer, int bufferLength)
    {
        if (!driverName || !buffer || bufferLength <= 0 || g_state.initialized)
        {
            return -1;
        }

        if (!loadDriver(driverName))
        {
            return -2;
        }

        ASIODriverInfo driverInfo{};
        driverInfo.sysRef = GetDesktopWindow();
        if (ASIOInit(&driverInfo) != ASE_OK)
        {
            unloadDriver();
            return -3;
        }

        long inputChannels = 0;
        long outputChannels = 0;
        long minBufferSize = 0;
        long maxBufferSize = 0;
        long preferredBufferSize = 0;
        long granularity = 0;

        if (ASIOGetChannels(&inputChannels, &outputChannels) != ASE_OK)
        {
            ASIOExit();
            unloadDriver();
            return -4;
        }

        bool supportsDsd = false;
        bool dsdProbeWasConclusive = false;
        std::string dsdTypes;
        std::ostringstream supportedDsdRates;
        ASIOIoFormat requestedFormat{ kASIODSDFormat };
        if (ASIOFuture(kAsioSetIoFormat, &requestedFormat) == ASE_SUCCESS &&
            requestedFormat.FormatType == kASIODSDFormat)
        {
            supportsDsd = true;
            dsdProbeWasConclusive = true;
            dsdTypes = channelTypesForCurrentMode(outputChannels);

            const double dsdSampleRates[] = {
                2822400, 5644800, 11289600, 22579200, 45158400
            };

            bool firstDsdRate = true;
            for (double rate : dsdSampleRates)
            {
                if (ASIOCanSampleRate(rate) == ASE_OK || ASIOSetSampleRate(rate) == ASE_OK)
                {
                    if (!firstDsdRate)
                    {
                        supportedDsdRates << ",";
                    }

                    supportedDsdRates << static_cast<long long>(rate);
                    firstDsdRate = false;
                }
            }
        }
        else
        {
            ASIOIoFormat probeFormat{ kASIODSDFormat };
            if (ASIOFuture(kAsioCanDoIoFormat, &probeFormat) == ASE_SUCCESS &&
                probeFormat.FormatType == kASIOFormatInvalid)
            {
                dsdProbeWasConclusive = true;
            }
        }

        ASIOIoFormat pcmFormat{ kASIOPCMFormat };
        ASIOFuture(kAsioSetIoFormat, &pcmFormat);

        if (ASIOGetBufferSize(&minBufferSize, &maxBufferSize, &preferredBufferSize, &granularity) != ASE_OK)
        {
            ASIOExit();
            unloadDriver();
            return -4;
        }

        const double pcmSampleRates[] = {
            44100, 48000, 88200, 96000, 176400, 192000, 352800, 384000,
            705600, 768000
        };

        std::ostringstream supportedPcmRates;
        bool firstRate = true;
        for (double rate : pcmSampleRates)
        {
            if (ASIOCanSampleRate(rate) == ASE_OK)
            {
                if (!firstRate)
                {
                    supportedPcmRates << ",";
                }

                supportedPcmRates << static_cast<long long>(rate);
                firstRate = false;
            }
        }

        const auto pcmTypes = channelTypesForCurrentMode(outputChannels);

        ASIOExit();
        unloadDriver();

        std::ostringstream result;
        result << "driver=" << driverInfo.name
               << "\ninputs=" << inputChannels
               << "\noutputs=" << outputChannels
               << "\nbufferMin=" << minBufferSize
               << "\nbufferMax=" << maxBufferSize
               << "\nbufferPreferred=" << preferredBufferSize
               << "\nbufferGranularity=" << granularity
               << "\npcmSampleRates=" << supportedPcmRates.str()
               << "\ndsdSampleRates=" << supportedDsdRates.str()
               << "\npcmTypes=" << pcmTypes
               << "\ndsdSupported=" << (supportsDsd ? "true" : "false")
               << "\ndsdProbeWasConclusive=" << (dsdProbeWasConclusive ? "true" : "false")
               << "\ndsdTypes=" << dsdTypes;

        const auto text = result.str();
        strncpy_s(buffer, bufferLength, text.c_str(), _TRUNCATE);
        return 0;
    }
}
