using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Player.Audio;

public static class WasapiDeviceProvider
{
    public static IReadOnlyList<WasapiDeviceInfo> GetRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(device => new WasapiDeviceInfo(device.ID, device.FriendlyName))
            .ToArray();
    }

    public static MMDevice GetRenderDevice(string id)
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(id);
    }

    public static WasapiDeviceCapabilities GetCapabilities(string id)
    {
        using var device = GetRenderDevice(id);
        var audioClient = device.AudioClient;
        var mixFormat = audioClient.MixFormat;
        var sampleRates = new[] { 44_100, 48_000, 88_200, 96_000, 176_400, 192_000, 352_800, 384_000 };
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
