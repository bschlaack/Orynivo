namespace Orynivo.Audio;

/// <summary>
/// Device capabilities reported by an ASIO driver, including buffer sizes,
/// supported sample rates, and DSD compatibility.
/// </summary>
/// <param name="DriverName">Name of the ASIO driver.</param>
/// <param name="InputChannels">Number of available input channels.</param>
/// <param name="OutputChannels">Number of available output channels.</param>
/// <param name="MinBufferSize">Smallest supported buffer size in samples.</param>
/// <param name="MaxBufferSize">Largest supported buffer size in samples.</param>
/// <param name="PreferredBufferSize">Buffer size preferred by the driver in samples.</param>
/// <param name="BufferGranularity">Step size for buffer sizes between min and max.</param>
/// <param name="SupportedPcmSampleRates">PCM sample rates supported by the driver in Hz.</param>
/// <param name="SupportedDsdSampleRates">DSD sample rates supported by the driver in Hz (e.g. 2822400 for DSD64).</param>
/// <param name="PcmOutputFormats">PCM output format labels supported by the driver.</param>
/// <param name="SupportsDsd"><see langword="true"/> when the driver supports native DSD.</param>
/// <param name="DsdProbeWasConclusive"><see langword="true"/> when DSD detection returned a definitive result.</param>
/// <param name="DsdOutputFormats">DSD output format labels supported by the driver.</param>
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
