using System.Runtime.InteropServices;

namespace Orynivo.Audio;

/// <summary>Minimal OpenAL interop used by the Linux PCM output implementation.</summary>
internal static class OpenAlNative
{
    private const string Library = "libopenal.so.1";
    private const int AlcDeviceSpecifier = 0x1005;
    private const int AlcDefaultDeviceSpecifier = 0x1004;
    private const int AlcAllDevicesSpecifier = 0x1013;
    private const int AlcDefaultAllDevicesSpecifier = 0x1012;
    private const int AlcFrequency = 0x1007;

    /// <summary>OpenAL stereo signed 16-bit PCM format identifier.</summary>
    internal const int AlFormatStereo16 = 0x1103;
    /// <summary>OpenAL source-state property identifier.</summary>
    internal const int AlSourceState = 0x1010;
    /// <summary>OpenAL playing source-state value.</summary>
    internal const int AlPlaying = 0x1012;
    /// <summary>OpenAL queued-buffer count property identifier.</summary>
    internal const int AlBuffersQueued = 0x1015;
    /// <summary>OpenAL processed-buffer count property identifier.</summary>
    internal const int AlBuffersProcessed = 0x1016;
    /// <summary>OpenAL source-gain property identifier.</summary>
    internal const int AlGain = 0x100A;
    /// <summary>OpenAL sample-offset property identifier.</summary>
    internal const int AlSampleOffset = 0x1025;

    [DllImport(Library, EntryPoint = "alcOpenDevice", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint AlcOpenDeviceNative(byte[]? deviceName);

    /// <summary>Opens the requested OpenAL device.</summary>
    /// <param name="deviceName">UTF-8 device name, or <see langword="null"/> for the default.</param>
    /// <returns>Native device handle.</returns>
    internal static nint OpenDevice(string? deviceName) =>
        AlcOpenDeviceNative(
            deviceName is null ? null : System.Text.Encoding.UTF8.GetBytes(deviceName + '\0'));

    [DllImport(Library, EntryPoint = "alcIsExtensionPresent", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AlcIsExtensionPresentNative(nint device, byte[] extensionName);

    [DllImport(Library, EntryPoint = "alcGetString", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint AlcGetStringNative(nint device, int parameter);

    /// <summary>Enumerates the output devices exposed by the active OpenAL implementation.</summary>
    /// <returns>Stable OpenAL device names accepted by <see cref="OpenDevice"/>.</returns>
    internal static IReadOnlyList<string> GetOutputDeviceNames()
    {
        var enumerateAll = IsExtensionPresent("ALC_ENUMERATE_ALL_EXT");
        if (!enumerateAll && !IsExtensionPresent("ALC_ENUMERATION_EXT"))
            return [];

        var pointer = AlcGetStringNative(
            nint.Zero,
            enumerateAll ? AlcAllDevicesSpecifier : AlcDeviceSpecifier);
        return ReadNullSeparatedStrings(pointer);
    }

    /// <summary>Returns the OpenAL default output device name when exposed.</summary>
    /// <returns>The default device name, or <see langword="null"/>.</returns>
    internal static string? GetDefaultOutputDeviceName()
    {
        var enumerateAll = IsExtensionPresent("ALC_ENUMERATE_ALL_EXT");
        var pointer = AlcGetStringNative(
            nint.Zero,
            enumerateAll ? AlcDefaultAllDevicesSpecifier : AlcDefaultDeviceSpecifier);
        return pointer == nint.Zero ? null : Marshal.PtrToStringUTF8(pointer);
    }

    [DllImport(Library, EntryPoint = "alcGetIntegerv", CallingConvention = CallingConvention.Cdecl)]
    private static extern void AlcGetIntegervNative(
        nint device,
        int parameter,
        int size,
        out int value);

    /// <summary>Returns the mixer sample rate of an initialized OpenAL device context.</summary>
    /// <param name="device">Native device handle.</param>
    /// <returns>The mixer rate in hertz, or zero if unavailable.</returns>
    internal static int GetMixerFrequency(nint device)
    {
        AlcGetIntegervNative(device, AlcFrequency, 1, out var value);
        return value;
    }

    [DllImport(Library, EntryPoint = "alcCloseDevice", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    /// <summary>Closes an OpenAL output device.</summary>
    /// <param name="device">Native device handle.</param>
    /// <returns>Whether the device was closed.</returns>
    internal static extern bool AlcCloseDevice(nint device);

    [DllImport(Library, EntryPoint = "alcCreateContext", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint AlcCreateContextNative(nint device, int[]? attributes);

    /// <summary>Creates an OpenAL context and requests a mixer sample rate.</summary>
    /// <param name="device">Native device handle.</param>
    /// <param name="requestedFrequency">Preferred mixer rate in hertz.</param>
    /// <returns>Native context handle.</returns>
    internal static nint AlcCreateContext(nint device, int requestedFrequency) =>
        AlcCreateContextNative(
            device,
            requestedFrequency > 0 ? [AlcFrequency, requestedFrequency, 0] : null);

    [DllImport(Library, EntryPoint = "alcMakeContextCurrent", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    /// <summary>Makes an OpenAL context current on the calling thread.</summary>
    /// <param name="context">Native context handle, or zero to clear it.</param>
    /// <returns>Whether the context change succeeded.</returns>
    internal static extern bool AlcMakeContextCurrent(nint context);

    [DllImport(Library, EntryPoint = "alcDestroyContext", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Destroys an OpenAL context.</summary>
    /// <param name="context">Native context handle.</param>
    internal static extern void AlcDestroyContext(nint context);

    [DllImport(Library, EntryPoint = "alGenSources", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Allocates OpenAL sources.</summary>
    /// <param name="count">Number of sources.</param>
    /// <param name="source">Allocated source identifier.</param>
    internal static extern void AlGenSources(int count, out uint source);

    [DllImport(Library, EntryPoint = "alDeleteSources", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Deletes OpenAL sources.</summary>
    /// <param name="count">Number of sources.</param>
    /// <param name="source">Source identifier.</param>
    internal static extern void AlDeleteSources(int count, ref uint source);

    [DllImport(Library, EntryPoint = "alGenBuffers", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Allocates OpenAL buffers.</summary>
    /// <param name="count">Number of buffers.</param>
    /// <param name="buffers">Destination buffer identifiers.</param>
    internal static extern void AlGenBuffers(int count, [Out] uint[] buffers);

    [DllImport(Library, EntryPoint = "alDeleteBuffers", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Deletes OpenAL buffers.</summary>
    /// <param name="count">Number of buffers.</param>
    /// <param name="buffers">Buffer identifiers.</param>
    internal static extern void AlDeleteBuffers(int count, uint[] buffers);

    [DllImport(Library, EntryPoint = "alBufferData", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Uploads PCM data to an OpenAL buffer.</summary>
    /// <param name="buffer">Buffer identifier.</param>
    /// <param name="format">PCM format identifier.</param>
    /// <param name="data">PCM bytes.</param>
    /// <param name="size">Valid byte count.</param>
    /// <param name="frequency">Sample rate.</param>
    internal static extern void AlBufferData(
        uint buffer,
        int format,
        byte[] data,
        int size,
        int frequency);

    [DllImport(Library, EntryPoint = "alSourceQueueBuffers", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Queues a buffer on a source.</summary>
    /// <param name="source">Source identifier.</param>
    /// <param name="count">Number of buffers.</param>
    /// <param name="buffer">Buffer identifier.</param>
    internal static extern void AlSourceQueueBuffers(uint source, int count, ref uint buffer);

    [DllImport(Library, EntryPoint = "alSourceUnqueueBuffers", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Removes a processed buffer from a source.</summary>
    /// <param name="source">Source identifier.</param>
    /// <param name="count">Number of buffers.</param>
    /// <param name="buffer">Removed buffer identifier.</param>
    internal static extern void AlSourceUnqueueBuffers(uint source, int count, out uint buffer);

    [DllImport(Library, EntryPoint = "alSourcePlay", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Starts or resumes an OpenAL source.</summary>
    /// <param name="source">Source identifier.</param>
    internal static extern void AlSourcePlay(uint source);

    [DllImport(Library, EntryPoint = "alSourcePause", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Pauses an OpenAL source.</summary>
    /// <param name="source">Source identifier.</param>
    internal static extern void AlSourcePause(uint source);

    [DllImport(Library, EntryPoint = "alSourceStop", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Stops an OpenAL source.</summary>
    /// <param name="source">Source identifier.</param>
    internal static extern void AlSourceStop(uint source);

    [DllImport(Library, EntryPoint = "alSourcef", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Sets a floating-point source property.</summary>
    /// <param name="source">Source identifier.</param>
    /// <param name="parameter">Property identifier.</param>
    /// <param name="value">Property value.</param>
    internal static extern void AlSourcef(uint source, int parameter, float value);

    [DllImport(Library, EntryPoint = "alGetSourcei", CallingConvention = CallingConvention.Cdecl)]
    /// <summary>Reads an integer source property.</summary>
    /// <param name="source">Source identifier.</param>
    /// <param name="parameter">Property identifier.</param>
    /// <param name="value">Returned property value.</param>
    internal static extern void AlGetSourcei(uint source, int parameter, out int value);

    private static bool IsExtensionPresent(string extensionName) =>
        AlcIsExtensionPresentNative(
            nint.Zero,
            System.Text.Encoding.ASCII.GetBytes(extensionName + '\0'));

    private static IReadOnlyList<string> ReadNullSeparatedStrings(nint pointer)
    {
        if (pointer == nint.Zero)
            return [];

        var result = new List<string>();
        var offset = 0;
        while (Marshal.ReadByte(pointer, offset) != 0)
        {
            var value = Marshal.PtrToStringUTF8(pointer + offset);
            if (string.IsNullOrEmpty(value))
                break;
            result.Add(value);
            offset += System.Text.Encoding.UTF8.GetByteCount(value) + 1;
        }

        return result;
    }
}
