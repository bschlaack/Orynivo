namespace Player.Audio;

public sealed record AsioDeviceInfo(
    string DriverName,
    int InputChannels,
    int OutputChannels,
    int MinBufferSize,
    int MaxBufferSize,
    int PreferredBufferSize,
    int BufferGranularity,
    IReadOnlyList<int> SupportedPcmSampleRates,
    IReadOnlyList<int> SupportedDsdSampleRates,
    IReadOnlyList<string> PcmOutputFormats,
    bool SupportsDsd,
    bool DsdProbeWasConclusive,
    IReadOnlyList<string> DsdOutputFormats);
