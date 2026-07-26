using Orynivo.Localization;

namespace Orynivo.Audio;

/// <summary>
/// Provides platform OpenAL devices and, on Linux, direct ALSA devices through the shared output-profile UI.
/// </summary>
public static class WasapiDeviceProvider
{
    /// <summary>Returns OpenAL devices, the system-default route, and Linux direct ALSA hardware.</summary>
    /// <returns>Available non-Windows audio devices.</returns>
    public static IReadOnlyList<WasapiDeviceInfo> GetRenderDevices()
    {
        var devices = new List<WasapiDeviceInfo>
        {
            new("default", LocalizationManager.Current.LinuxDefaultAudioDevice)
        };
        if (OperatingSystem.IsLinux())
            devices.AddRange(GetDirectAlsaDevices());
        devices.AddRange(
            OpenAlNative.GetOutputDeviceNames()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new WasapiDeviceInfo(name, name)));
        return devices;
    }

    /// <summary>Returns the system-default OpenAL output route.</summary>
    /// <returns>The default non-Windows audio device.</returns>
    public static WasapiDeviceInfo? GetDefaultRenderDevice() =>
        new("default", LocalizationManager.Current.LinuxDefaultAudioDevice);

    /// <summary>Returns the PCM formats supported by Orynivo's non-Windows output paths.</summary>
    /// <param name="id">Direct ALSA or OpenAL device identifier.</param>
    /// <returns>PCM output capabilities for the current platform.</returns>
    public static WasapiDeviceCapabilities GetCapabilities(string id)
    {
        var isAlsa = id.StartsWith("alsa:", StringComparison.Ordinal);
        return new(
            ResolveDisplayName(id),
            id,
            isAlsa ? 96_000 : 48_000,
            2,
            isAlsa ? 32 : 16,
            [44_100, 48_000, 88_200, 96_000, 176_400, 192_000],
            [isAlsa ? "S32_LE · 2 ch" : "S16_LE · 2 ch"]);
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
