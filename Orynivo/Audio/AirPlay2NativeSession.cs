using System.Runtime.InteropServices;

namespace Orynivo.Audio;

/// <summary>Owns one native AirPlay 2 sender session exposed by AirPlay2Bridge.</summary>
internal sealed class AirPlay2NativeSession : IDisposable
{
    private const string LibraryName = "AirPlay2Bridge";
    private const uint ExpectedAbiVersion = 102;
    private readonly object _nativeGate = new();
    private readonly StateCallback _callback;
    private readonly RemoteCommandCallback _remoteCommandCallback;
    private IntPtr _session;
    private bool _disposed;

    private AirPlay2NativeSession(StateCallback callback, RemoteCommandCallback remoteCommandCallback)
    {
        _callback = callback;
        _remoteCommandCallback = remoteCommandCallback;
    }

    /// <summary>Gets whether the compatible native bridge can be loaded.</summary>
    internal static bool IsAvailable
    {
        get
        {
            if (!NativeLibrary.TryLoad(LibraryName, typeof(AirPlay2NativeSession).Assembly, null, out var handle))
                return false;
            NativeLibrary.Free(handle);
            try { return GetAbiVersion() == ExpectedAbiVersion; }
            catch { return false; }
        }
    }

    /// <summary>Creates and starts an authenticated AirPlay 2 session.</summary>
    /// <param name="host">Receiver address.</param>
    /// <param name="port">Receiver AirPlay port.</param>
    /// <param name="deviceName">Receiver display name.</param>
    /// <param name="deviceId">Stable DNS-SD service identifier.</param>
    /// <param name="metadata">Track metadata and optional bounded artwork.</param>
    /// <param name="remoteCommand">Receives authenticated transport commands from the receiver.</param>
    /// <param name="cancellationToken">Cancels session startup.</param>
    /// <returns>The active native session.</returns>
    internal static async Task<AirPlay2NativeSession> CreateAsync(
        string host,
        int port,
        string? deviceName,
        string? deviceId,
        AirPlayTrackMetadata metadata,
        Action<AirPlayRemoteCommand> remoteCommand,
        CancellationToken cancellationToken)
    {
        StateCallback callback = static (_, _, _) => { };
        RemoteCommandCallback remoteCallback = (_, command) => remoteCommand(command);
        var instance = new AirPlay2NativeSession(callback, remoteCallback);
        var hostPointer = Marshal.StringToCoTaskMemUTF8(host);
        var namePointer = Marshal.StringToCoTaskMemUTF8(deviceName ?? "Orynivo AirPlay 2");
        var idPointer = Marshal.StringToCoTaskMemUTF8(deviceId ?? string.Empty);
        var titlePointer = Marshal.StringToCoTaskMemUTF8(metadata.Title ?? string.Empty);
        var artistPointer = Marshal.StringToCoTaskMemUTF8(metadata.Artist ?? string.Empty);
        var albumPointer = Marshal.StringToCoTaskMemUTF8(metadata.Album ?? string.Empty);
        var artworkMimePointer = Marshal.StringToCoTaskMemUTF8(metadata.ArtworkMimeType ?? string.Empty);
        var artworkPointer = metadata.Artwork is { Length: > 0 }
            ? Marshal.AllocHGlobal(metadata.Artwork.Length)
            : IntPtr.Zero;
        if (artworkPointer != IntPtr.Zero)
            Marshal.Copy(metadata.Artwork!, 0, artworkPointer, metadata.Artwork!.Length);
        try
        {
            var config = new SessionConfig
            {
                StructSize = (uint)Marshal.SizeOf<SessionConfig>(),
                HostUtf8 = hostPointer,
                Port = checked((ushort)port),
                DeviceNameUtf8 = namePointer,
                DeviceIdUtf8 = idPointer,
                SampleRate = 44_100,
                Channels = 2,
                BitsPerSample = 16,
                StateCallback = instance._callback,
                UserData = IntPtr.Zero,
                TitleUtf8 = titlePointer,
                ArtistUtf8 = artistPointer,
                AlbumUtf8 = albumPointer,
                ArtworkData = artworkPointer,
                ArtworkSize = (nuint)(metadata.Artwork?.Length ?? 0),
                ArtworkMimeUtf8 = artworkMimePointer,
                RemoteCommandCallback = instance._remoteCommandCallback
            };
            ThrowIfFailed(SessionCreate(in config, out instance._session));
            ThrowIfFailed(SessionSetInitialVolume(instance._session, 0.0f));
            await Task.Run(() => ThrowIfFailed(SessionStart(instance._session)), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return instance;
        }
        catch
        {
            instance.Dispose();
            throw;
        }
        finally
        {
            Marshal.FreeCoTaskMem(hostPointer);
            Marshal.FreeCoTaskMem(namePointer);
            Marshal.FreeCoTaskMem(idPointer);
            Marshal.FreeCoTaskMem(titlePointer);
            Marshal.FreeCoTaskMem(artistPointer);
            Marshal.FreeCoTaskMem(albumPointer);
            Marshal.FreeCoTaskMem(artworkMimePointer);
            if (artworkPointer != IntPtr.Zero)
                Marshal.FreeHGlobal(artworkPointer);
        }
    }

    /// <summary>Writes interleaved signed little-endian stereo PCM.</summary>
    /// <param name="pcm">PCM bytes to submit.</param>
    internal unsafe void Write(ReadOnlySpan<byte> pcm)
    {
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            fixed (byte* pointer = pcm)
            {
                ThrowIfFailed(SessionWritePcm(_session, pointer, (nuint)pcm.Length, out var consumed));
                if (consumed != (nuint)pcm.Length)
                    throw new IOException("AirPlay2Bridge accepted only part of the PCM buffer.");
            }
        }
    }

    private static void ThrowIfFailed(Result result)
    {
        if (result == Result.Ok)
            return;
        var pointer = GetLastError();
        var message = pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
        throw new IOException(string.IsNullOrWhiteSpace(message)
            ? $"AirPlay2Bridge failed with result {(int)result}."
            : message);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_nativeGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_session == IntPtr.Zero)
                return;
            _ = SessionStop(_session);
            SessionDestroy(_session);
            _session = IntPtr.Zero;
            GC.KeepAlive(_callback);
            GC.KeepAlive(_remoteCommandCallback);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StateCallback(IntPtr userData, State state, IntPtr messageUtf8);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RemoteCommandCallback(IntPtr userData, AirPlayRemoteCommand command);

    [StructLayout(LayoutKind.Sequential)]
    private struct SessionConfig
    {
        internal uint StructSize;
        internal IntPtr HostUtf8;
        internal ushort Port;
        internal IntPtr DeviceNameUtf8;
        internal IntPtr DeviceIdUtf8;
        internal uint SampleRate;
        internal ushort Channels;
        internal ushort BitsPerSample;
        internal StateCallback StateCallback;
        internal IntPtr UserData;
        internal IntPtr TitleUtf8;
        internal IntPtr ArtistUtf8;
        internal IntPtr AlbumUtf8;
        internal IntPtr ArtworkData;
        internal nuint ArtworkSize;
        internal IntPtr ArtworkMimeUtf8;
        internal RemoteCommandCallback RemoteCommandCallback;
    }

    private enum Result { Ok = 0 }
    private enum State { Idle = 0 }

    [DllImport(LibraryName, EntryPoint = "ap2_get_abi_version", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint GetAbiVersion();

    [DllImport(LibraryName, EntryPoint = "ap2_session_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern Result SessionCreate(in SessionConfig config, out IntPtr session);

    [DllImport(LibraryName, EntryPoint = "ap2_session_set_initial_volume", CallingConvention = CallingConvention.Cdecl)]
    private static extern Result SessionSetInitialVolume(IntPtr session, float volumeDb);

    [DllImport(LibraryName, EntryPoint = "ap2_session_start", CallingConvention = CallingConvention.Cdecl)]
    private static extern Result SessionStart(IntPtr session);

    [DllImport(LibraryName, EntryPoint = "ap2_session_write_pcm", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe Result SessionWritePcm(
        IntPtr session, void* samples, nuint byteCount, out nuint bytesConsumed);

    [DllImport(LibraryName, EntryPoint = "ap2_session_stop", CallingConvention = CallingConvention.Cdecl)]
    private static extern Result SessionStop(IntPtr session);

    [DllImport(LibraryName, EntryPoint = "ap2_session_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SessionDestroy(IntPtr session);

    [DllImport(LibraryName, EntryPoint = "ap2_get_last_error", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetLastError();
}
