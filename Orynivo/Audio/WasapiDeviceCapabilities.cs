namespace Orynivo.Audio;

/// <summary>
/// Summarises the capabilities of a WASAPI render device as reported by the operating system.
/// </summary>
/// <param name="Name">Friendly name of the device.</param>
/// <param name="Id">Unique Windows device identifier (MMDevice ID).</param>
/// <param name="MixFormatSampleRate">Sample rate of the shared mix format in Hz.</param>
/// <param name="MixFormatChannels">Channel count of the shared mix format.</param>
/// <param name="MixFormatBitsPerSample">Bit depth of the shared mix format.</param>
/// <param name="ExclusivePcmSampleRates">PCM sample rates supported in exclusive mode, sorted ascending in Hz.</param>
/// <param name="ExclusivePcmFormats">PCM format labels supported in exclusive mode, sorted alphabetically.</param>
public sealed record WasapiDeviceCapabilities(
    string Name,
    string Id,
    int MixFormatSampleRate,
    int MixFormatChannels,
    int MixFormatBitsPerSample,
    IReadOnlyList<int> ExclusivePcmSampleRates,
    IReadOnlyList<string> ExclusivePcmFormats);
