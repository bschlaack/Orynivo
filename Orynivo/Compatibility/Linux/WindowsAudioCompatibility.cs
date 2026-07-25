namespace Orynivo.Audio;

/// <summary>
/// Provides an empty WASAPI device catalog on non-Windows systems.
/// </summary>
public static class WasapiDeviceProvider
{
    /// <summary>Returns no devices because WASAPI is Windows-only.</summary>
    /// <returns>An empty device list.</returns>
    public static IReadOnlyList<WasapiDeviceInfo> GetRenderDevices() => [];

    /// <summary>Returns no default device because WASAPI is Windows-only.</summary>
    /// <returns>Always <see langword="null"/>.</returns>
    public static WasapiDeviceInfo? GetDefaultRenderDevice() => null;

    /// <summary>Rejects WASAPI capability queries on non-Windows systems.</summary>
    /// <param name="id">Unused Windows device identifier.</param>
    /// <returns>This method does not return.</returns>
    /// <exception cref="PlatformNotSupportedException">Always thrown.</exception>
    public static WasapiDeviceCapabilities GetCapabilities(string id) =>
        throw new PlatformNotSupportedException();
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
