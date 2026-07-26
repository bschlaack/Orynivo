namespace Orynivo.Audio;

/// <summary>Converts DSF payload bytes to the MSB-first DSD byte order used by ALSA.</summary>
internal static class DsfPayload
{
    /// <summary>Converts one DSF byte to ALSA's chronological DSD bit order when required.</summary>
    /// <param name="value">Raw byte read from the DSF data chunk.</param>
    /// <param name="isLeastSignificantBitFirst">
    /// Whether the DSF format chunk declares its common least-significant-bit-first representation.
    /// </param>
    /// <returns>The byte in ALSA's most-significant-bit-first DSD representation.</returns>
    internal static byte ToAlsa(byte value, bool isLeastSignificantBitFirst)
    {
        if (!isLeastSignificantBitFirst)
            return value;

        value = (byte)((value >> 4) | (value << 4));
        value = (byte)(((value & 0xCC) >> 2) | ((value & 0x33) << 2));
        return (byte)(((value & 0xAA) >> 1) | ((value & 0x55) << 1));
    }
}
