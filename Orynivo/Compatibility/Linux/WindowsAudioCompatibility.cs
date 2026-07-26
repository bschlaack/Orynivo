using Orynivo.Localization;

namespace Orynivo.Audio;

/// <summary>
/// Provides direct ALSA and OpenAL output devices through the shared output-profile UI.
/// </summary>
public static class WasapiDeviceProvider
{
    /// <summary>Returns direct ALSA hardware, OpenAL devices, and the system-default route.</summary>
    /// <returns>Available Linux audio devices.</returns>
    public static IReadOnlyList<WasapiDeviceInfo> GetRenderDevices()
    {
        var devices = new List<WasapiDeviceInfo>
        {
            new("default", LocalizationManager.Current.LinuxDefaultAudioDevice)
        };
        devices.AddRange(GetDirectAlsaDevices());
        devices.AddRange(
            OpenAlNative.GetOutputDeviceNames()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new WasapiDeviceInfo(name, name)));
        return devices;
    }

    /// <summary>Returns the system-default OpenAL output route.</summary>
    /// <returns>The default Linux audio device.</returns>
    public static WasapiDeviceInfo? GetDefaultRenderDevice() =>
        new("default", LocalizationManager.Current.LinuxDefaultAudioDevice);

    /// <summary>Returns the PCM formats supported by Orynivo's Linux output paths.</summary>
    /// <param name="id">Direct ALSA or OpenAL device identifier.</param>
    /// <returns>Linux PCM output capabilities.</returns>
    public static WasapiDeviceCapabilities GetCapabilities(string id)
    {
        var isAlsa = id.StartsWith("alsa:", StringComparison.Ordinal);
        var dsdCapabilities = isAlsa
            ? ProbeDsdCapabilities(id["alsa:".Length..])
            : (Native: Array.Empty<int>(), Dop: Array.Empty<int>(), Conclusive: true);
        return new(
            ResolveDisplayName(id),
            id,
            isAlsa ? 96_000 : 48_000,
            2,
            isAlsa ? 32 : 16,
            [44_100, 48_000, 88_200, 96_000, 176_400, 192_000],
            [isAlsa ? "S32_LE · 2 ch" : "S16_LE · 2 ch"],
            dsdCapabilities.Native,
            dsdCapabilities.Dop,
            dsdCapabilities.Conclusive);
    }

    private static (IReadOnlyList<int> Native, IReadOnlyList<int> Dop, bool Conclusive)
        ProbeDsdCapabilities(string deviceName)
    {
        int[] levels = [64, 128, 256, 512, 1024];
        var native = new List<int>();
        var dop = new List<int>();
        foreach (var level in levels)
        {
            var dsdRate = checked(level * 44_100);
            var nativeResult = AlsaNative.ProbeExactFormat(deviceName, dsdRate / 32, nativeDsd: true);
            if (nativeResult == AlsaNative.ExactFormatProbeResult.Inconclusive)
                return (native, dop, false);
            if (nativeResult == AlsaNative.ExactFormatProbeResult.Supported)
                native.Add(level);

            var dopResult = AlsaNative.ProbeExactFormat(deviceName, dsdRate / 16, nativeDsd: false);
            if (dopResult == AlsaNative.ExactFormatProbeResult.Inconclusive)
                return (native, dop, false);
            if (dopResult == AlsaNative.ExactFormatProbeResult.Supported)
                dop.Add(level);
        }
        return (native, dop, true);
    }

    private static string ResolveDisplayName(string id) =>
        string.Equals(id, "default", StringComparison.OrdinalIgnoreCase)
            ? LocalizationManager.Current.LinuxDefaultAudioDevice
            : GetRenderDevices().FirstOrDefault(device => device.Id == id)?.Name ?? id;

    private static IReadOnlyList<WasapiDeviceInfo> GetDirectAlsaDevices()
    {
        const string pcmListPath = "/proc/asound/pcm";
        if (!File.Exists(pcmListPath))
            return [];

        var devices = new List<WasapiDeviceInfo>();
        foreach (var line in File.ReadLines(pcmListPath))
        {
            if (!line.Contains("playback", StringComparison.OrdinalIgnoreCase))
                continue;
            var separator = line.IndexOf(':');
            if (separator < 5 ||
                !int.TryParse(line.AsSpan(0, 2), out var cardNumber) ||
                !int.TryParse(line.AsSpan(3, 2), out var deviceNumber))
                continue;

            var cardIdPath = $"/sys/class/sound/card{cardNumber}/id";
            if (!File.Exists(cardIdPath))
                continue;
            var cardId = File.ReadAllText(cardIdPath).Trim();
            if (cardId.Length == 0)
                continue;

            var fields = line[(separator + 1)..].Split(':', StringSplitOptions.TrimEntries);
            var displayName = fields.FirstOrDefault(static value => value.Length > 0) ?? cardId;
            var alsaName = $"hw:CARD={cardId},DEV={deviceNumber}";
            devices.Add(new($"alsa:{alsaName}", $"ALSA · {displayName} ({cardId})"));
        }
        return devices;
    }
}

/// <summary>
/// Placeholder for Windows endpoint-volume synchronization on non-Windows systems.
/// </summary>
internal sealed class WindowsEndpointVolumeSynchronizer : IDisposable
{
    /// <summary>Rejects creation because Windows endpoint volume is unavailable.</summary>
    /// <param name="deviceId">Unused Windows device identifier.</param>
    /// <exception cref="PlatformNotSupportedException">Always thrown.</exception>
    internal WindowsEndpointVolumeSynchronizer(string deviceId) =>
        throw new PlatformNotSupportedException();

    /// <summary>Would be raised when a platform endpoint changes volume.</summary>
    internal event EventHandler<float>? VolumeChanged;

    /// <summary>Gets a neutral volume value.</summary>
    internal float Volume => 1.0f;

    /// <summary>Ignores a volume update.</summary>
    /// <param name="volume">Requested linear volume.</param>
    internal void SetVolume(float volume) { }

    /// <inheritdoc/>
    public void Dispose() { }
}
