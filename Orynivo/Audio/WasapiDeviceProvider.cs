using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Orynivo.Audio;

/// <summary>
/// Factory and query methods for active WASAPI render devices.
/// </summary>
public static class WasapiDeviceProvider
{
    /// <summary>Returns all currently active WASAPI render endpoints.</summary>
    public static IReadOnlyList<WasapiDeviceInfo> GetRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var result = new List<WasapiDeviceInfo>(devices.Count);
        foreach (var device in devices)
        {
            using (device)
                result.Add(new WasapiDeviceInfo(device.ID, device.FriendlyName));
        }
        return result;
    }

    /// <summary>Returns the default Windows multimedia render endpoint when one is available.</summary>
    /// <returns>The default WASAPI render endpoint, or <see langword="null"/> when Windows has no active default output.</returns>
    public static WasapiDeviceInfo? GetDefaultRenderDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return new WasapiDeviceInfo(device.ID, device.FriendlyName);
    }

    /// <summary>Opens and returns the <see cref="MMDevice"/> for the given device ID. Caller must dispose.</summary>
    /// <param name="id">MMDevice ID as returned by <see cref="GetRenderDevices"/>.</param>
    public static MMDevice GetRenderDevice(string id)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(id);
    }

    /// <summary>
    /// Queries the exclusive-mode PCM capabilities of the device with the given ID.
    /// </summary>
    /// <param name="id">MMDevice ID as returned by <see cref="GetRenderDevices"/>.</param>
    /// <returns>Capability snapshot including supported sample rates and bit depths.</returns>
    public static WasapiDeviceCapabilities GetCapabilities(string id)
    {
        using var device = GetRenderDevice(id);
        var audioClient = device.AudioClient;
        var mixFormat = audioClient.MixFormat;
        var sampleRates = new[]
        {
            8_000, 11_025, 12_000, 16_000, 22_050, 24_000, 32_000,
            44_100, 48_000, 88_200, 96_000, 176_400, 192_000,
            352_800, 384_000, 705_600, 768_000
        };
        var formats = new[]
        {
            (Name: "16-Bit PCM", Factory: (Func<int, WaveFormat>)(rate => new WaveFormat(rate, 16, 2))),
            (Name: "24-Bit PCM", Factory: rate => new WaveFormatExtensible(rate, 24, 2)),
            (Name: "32-Bit Float PCM", Factory: rate => WaveFormat.CreateIeeeFloatWaveFormat(rate, 2))
        };

        var supportedRates = new List<int>();
        var supportedFormats = new HashSet<string>();
        foreach (var rate in sampleRates)
        {
            foreach (var format in formats)
            {
                if (audioClient.IsFormatSupported(AudioClientShareMode.Exclusive, format.Factory(rate)))
                {
                    supportedRates.Add(rate);
                    supportedFormats.Add(format.Name);
                }
            }
        }

        return new WasapiDeviceCapabilities(
            device.FriendlyName,
            device.ID,
            mixFormat.SampleRate,
            mixFormat.Channels,
            mixFormat.BitsPerSample,
            supportedRates.Distinct().Order().ToArray(),
            supportedFormats.Order().ToArray());
    }
}
