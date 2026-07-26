using System.Runtime.InteropServices;
using Orynivo.Localization;

namespace Orynivo.Audio;

/// <summary>Minimal direct ALSA PCM interop used for exact-rate Linux hardware output.</summary>
internal static class AlsaNative
{
    private const string Library = "libasound.so.2";
    private const int PlaybackStream = 0;
    private const int ReadWriteInterleaved = 3;
    private const int Signed32LittleEndian = 10;
    private const int DsdUnsigned32BigEndian = 52;

    [DllImport(Library, EntryPoint = "snd_pcm_open", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndPcmOpen(out nint pcm, string name, int stream, int mode);

    [DllImport(Library, EntryPoint = "snd_pcm_set_params", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndPcmSetParams(
        nint pcm,
        int format,
        int access,
        uint channels,
        uint rate,
        int softResample,
        uint latency);

    [DllImport(Library, EntryPoint = "snd_pcm_writei", CallingConvention = CallingConvention.Cdecl)]
    private static extern long SndPcmWriteInterleaved(nint pcm, nint buffer, ulong frames);

    [DllImport(Library, EntryPoint = "snd_pcm_recover", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndPcmRecover(nint pcm, int error, int silent);

    [DllImport(Library, EntryPoint = "snd_pcm_delay", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndPcmDelay(nint pcm, out long frames);

    [DllImport(Library, EntryPoint = "snd_pcm_drop", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Drop(nint pcm);

    [DllImport(Library, EntryPoint = "snd_pcm_prepare", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Prepare(nint pcm);

    [DllImport(Library, EntryPoint = "snd_pcm_drain", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Drain(nint pcm);

    [DllImport(Library, EntryPoint = "snd_pcm_close", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Close(nint pcm);

    [DllImport(Library, EntryPoint = "snd_strerror", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint SndStrError(int error);

    /// <summary>Opens a direct hardware PCM at an exact sample rate without ALSA resampling.</summary>
    /// <param name="deviceName">ALSA hardware identifier such as <c>hw:CARD=SABRE,DEV=0</c>.</param>
    /// <param name="sampleRate">Required sample rate in hertz.</param>
    /// <returns>Configured native PCM handle.</returns>
    internal static nint OpenExact(string deviceName, int sampleRate)
        => OpenExact(deviceName, sampleRate, Signed32LittleEndian);

    /// <summary>Opens a direct hardware PCM in native 32-bit big-endian DSD mode.</summary>
    /// <param name="deviceName">ALSA hardware identifier such as <c>hw:CARD=SABRE,DEV=0</c>.</param>
    /// <param name="sampleRate">DSD container-frame rate in hertz (DSD bit rate divided by 32).</param>
    /// <returns>Configured native DSD PCM handle.</returns>
    internal static nint OpenExactNativeDsd(string deviceName, int sampleRate)
        => OpenExact(deviceName, sampleRate, DsdUnsigned32BigEndian);

    private static nint OpenExact(string deviceName, int sampleRate, int format)
    {
        var result = SndPcmOpen(out var pcm, deviceName, PlaybackStream, 0);
        if (result < 0)
            throw CreateException(deviceName, sampleRate, result);

        result = SndPcmSetParams(
            pcm,
            format,
            ReadWriteInterleaved,
            2,
            checked((uint)sampleRate),
            softResample: 0,
            latency: 100_000);
        if (result >= 0)
            return pcm;

        Close(pcm);
        throw CreateException(deviceName, sampleRate, result);
    }

    /// <summary>Writes interleaved stereo frames, recovering recoverable ALSA stream errors.</summary>
    /// <param name="pcm">Configured native PCM handle.</param>
    /// <param name="bytes">Signed 32-bit stereo PCM bytes.</param>
    /// <returns>Number of frames accepted by ALSA.</returns>
    internal static unsafe int Write(nint pcm, Span<byte> bytes)
    {
        fixed (byte* pointer = bytes)
        {
            var result = SndPcmWriteInterleaved(pcm, (nint)pointer, checked((ulong)(bytes.Length / 8)));
            if (result < 0)
            {
                var recovered = SndPcmRecover(pcm, checked((int)result), silent: 1);
                if (recovered < 0)
                    throw new IOException(Marshal.PtrToStringUTF8(SndStrError(recovered)));
                return 0;
            }
            return checked((int)result);
        }
    }

    /// <summary>Returns frames currently queued in the ALSA device.</summary>
    /// <param name="pcm">Configured native PCM handle.</param>
    /// <returns>Non-negative queued frame count.</returns>
    internal static long GetDelay(nint pcm) =>
        SndPcmDelay(pcm, out var frames) >= 0 ? Math.Max(0, frames) : 0;

    private static IOException CreateException(string deviceName, int sampleRate, int error)
    {
        var message = error == -16
            ? string.Format(LocalizationManager.Current.AlsaDeviceBusy, deviceName)
            : string.Format(
                LocalizationManager.Current.AlsaExactOpenFailed,
                deviceName,
                sampleRate,
                Marshal.PtrToStringUTF8(SndStrError(error)));
        return new IOException(message);
    }
}
