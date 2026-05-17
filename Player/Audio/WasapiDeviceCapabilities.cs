namespace Player.Audio;

public sealed record WasapiDeviceCapabilities(
    string Name,
    string Id,
    int MixFormatSampleRate,
    int MixFormatChannels,
    int MixFormatBitsPerSample,
    IReadOnlyList<int> ExclusivePcmSampleRates,
    IReadOnlyList<string> ExclusivePcmFormats);
