using System.Runtime.InteropServices;

namespace Orynivo.Audio.CoreAudio;

/// <summary>
/// Native stereo S32 AUHAL renderer. The callback only consumes preallocated PCM
/// or writes silence. Lifecycle/property work stays on its owning worker.
/// </summary>
internal sealed class CoreAudioHalOutput : ICoreAudioPcmOutput
{
    private const string Library = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private readonly CoreAudioPcmRing _ring = new();
    private readonly RenderCallback _callback;
    private readonly object _nativeLock = new();
    private GCHandle _root;
    private nint _unit;
    private int _paused;
    private int _failed;
    private int _callbacks;
    private bool _started;

    private CoreAudioHalOutput(TimeSpan drainTail)
    {
        DrainTail = drainTail;
        _callback = Render;
    }

    /// <inheritdoc/>
    public long RenderedFrames => _ring.RenderedFrames;
    /// <inheritdoc/>
    public long QueuedFrames => _ring.QueuedFrames;
    /// <inheritdoc/>
    public TimeSpan DrainTail { get; }
    /// <inheritdoc/>
    public bool IsQuiescent { get; private set; } = true;
    /// <inheritdoc/>
    public bool Paused { get => Volatile.Read(ref _paused) != 0; set => Volatile.Write(ref _paused, value ? 1 : 0); }

    /// <summary>Initializes a native output bound to the already leased device and exact client rate.</summary>
    /// <param name="device">Live, leased device.</param>
    /// <param name="sampleRate">Confirmed nominal rate.</param>
    /// <returns>An initialized output which has not started callbacks.</returns>
    internal static CoreAudioHalOutput Create(CoreAudioNativeDevice device, int sampleRate)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var output = new CoreAudioHalOutput(device.DrainTail);
        if (output.DrainTail < TimeSpan.Zero || output.DrainTail > TimeSpan.FromSeconds(10))
            throw new NotSupportedException();
        try
        {
            var description = new ComponentDescription
            {
                Type = 0x61756f75, SubType = 0x6168616c, Manufacturer = 0x6170706c // auou, ahal, appl
            };
            var component = AudioComponentFindNext(0, ref description);
            if (component == 0) throw new NotSupportedException();
            Check(AudioComponentInstanceNew(component, out output._unit));
            output._root = GCHandle.Alloc(output);
            output.Set(2003, 2, 0, 1u); // Enable output IO on bus zero.
            output.Set(2003, 1, 1, 0u); // Disable input IO on bus one.
            output.Set(2000, 0, 0, device.ObjectId);
            var format = new StreamDescription
            {
                SampleRate = sampleRate, Format = 0x6c70636d, Flags = 12, // lpcm, signed integer + packed
                BytesPerPacket = 8, FramesPerPacket = 1, BytesPerFrame = 8, Channels = 2, Bits = 32
            };
            output.Set(8, 1, 0, format);
            var callback = new CallbackDescription { Function = Marshal.GetFunctionPointerForDelegate(output._callback) };
            output.Set(23, 1, 0, callback);
            Check(AudioUnitInitialize(output._unit));
            var accepted = output.Get<StreamDescription>(8, 1, 0);
            if (accepted.SampleRate != sampleRate || accepted.Format != format.Format ||
                accepted.Flags != format.Flags || accepted.Channels != 2 || accepted.Bits != 32 ||
                accepted.BytesPerFrame != 8 || accepted.BytesPerPacket != 8 || accepted.FramesPerPacket != 1)
                throw new NotSupportedException();
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public void Start()
    {
        lock (_nativeLock)
        {
            if (_started) return;
            IsQuiescent = false;
            Check(AudioOutputUnitStart(_unit));
            _started = true;
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (_nativeLock)
        {
            Check(AudioOutputUnitStop(_unit));
            _started = false;
            WaitForCallbacks();
            IsQuiescent = true;
            _ring.Reset();
            Check(AudioUnitReset(_unit, 0, 0));
        }
    }

    /// <inheritdoc/>
    public int Write(ReadOnlySpan<byte> pcm) => _ring.Write(pcm);

    /// <inheritdoc/>
    public void CheckHealth()
    {
        lock (_nativeLock)
        {
            if (Volatile.Read(ref _failed) != 0 || Get<int>(22, 0, 0) != 0)
                throw new IOException();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_nativeLock)
        {
            var unit = Interlocked.Exchange(ref _unit, 0);
            if (unit == 0) return;
            var stopped = AudioOutputUnitStop(unit) == 0;
            AudioUnitUninitialize(unit);
            var result = AudioComponentInstanceDispose(unit);
            if (stopped || result == 0)
            {
                WaitForCallbacks();
                IsQuiescent = true;
            }
            // A failed native disposal must not leave a collected managed callback
            // registered with an outstanding AudioUnit. Retain the root in that case.
            if (result == 0 && _root.IsAllocated) _root.Free();
            GC.KeepAlive(_callback);
            Check(result);
        }
    }

    private unsafe int Render(nint context, ref uint flags, nint timestamp, uint bus, uint frames, nint buffers)
    {
        Interlocked.Increment(ref _callbacks);
        try
        {
            if (buffers == 0 || *(uint*)buffers != 1 || frames > 131072)
            {
                Volatile.Write(ref _failed, 1);
                return -50;
            }
            // AudioBufferList has 4 bytes of ABI padding before its first
            // AudioBuffer on both supported 64-bit Mac architectures.
            var buffer = (NativeBuffer*)((byte*)buffers + 8);
            var bytes = checked((int)frames * 8);
            if (buffer->Channels != 2 || buffer->Data == 0 || buffer->Size < bytes)
            {
                Volatile.Write(ref _failed, 1);
                return -50;
            }
            var destination = new Span<byte>((void*)buffer->Data, bytes);
            flags &= ~16u;
            if (Paused)
            {
                destination.Clear();
                flags |= 16;
            }
            else if (_ring.Read(destination) == 0) flags |= 16;
            buffer->Size = (uint)bytes;
            return 0;
        }
        catch
        {
            Volatile.Write(ref _failed, 1);
            return -50;
        }
        finally { Interlocked.Decrement(ref _callbacks); }
    }

    private void WaitForCallbacks()
    {
        var spin = new SpinWait();
        while (Volatile.Read(ref _callbacks) != 0) spin.SpinOnce();
    }

    private unsafe void Set<T>(uint property, uint scope, uint bus, T value) where T : unmanaged =>
        Check(AudioUnitSetProperty(_unit, property, scope, bus, (nint)(&value), (uint)sizeof(T)));

    private unsafe T Get<T>(uint property, uint scope, uint bus) where T : unmanaged
    {
        T value = default;
        uint size = (uint)sizeof(T);
        Check(AudioUnitGetProperty(_unit, property, scope, bus, (nint)(&value), ref size));
        if (size != sizeof(T)) throw new IOException();
        return value;
    }

    private static void Check(int status)
    {
        if (status != 0) throw new ExternalException(null, status);
    }

    /// <summary>Native output callback ABI.</summary>
    /// <param name="context">Unused opaque native context.</param>
    /// <param name="flags">Native render action flags.</param>
    /// <param name="timestamp">Borrowed native timestamp, not dereferenced.</param>
    /// <param name="bus">Output bus identifier.</param>
    /// <param name="frames">Requested stereo frame count.</param>
    /// <param name="buffers">Borrowed native AudioBufferList.</param>
    /// <returns>Zero for successful PCM/silence, or a native error status.</returns>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RenderCallback(nint context, ref uint flags, nint timestamp, uint bus, uint frames, nint buffers);

    /// <summary>Native AudioComponentDescription layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ComponentDescription
    {
        /// <summary>Stores component type, subtype, vendor and flag selectors in native order.</summary>
        public uint Type, SubType, Manufacturer, Flags, FlagsMask;
    }
    /// <summary>Native AudioStreamBasicDescription layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct StreamDescription
    {
        /// <summary>Stores the client sample rate.</summary>
        public double SampleRate;
        /// <summary>Stores native PCM format, packet, channel and precision fields.</summary>
        public uint Format, Flags, BytesPerPacket, FramesPerPacket, BytesPerFrame, Channels, Bits, Reserved;
    }
    /// <summary>Native AURenderCallbackStruct layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CallbackDescription
    {
        /// <summary>Stores the native callback function and opaque context.</summary>
        public nint Function, Context;
    }
    /// <summary>Native AudioBuffer layout.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBuffer
    {
        /// <summary>Stores native channel and byte counts.</summary>
        public uint Channels, Size;
        /// <summary>Stores the borrowed output buffer address.</summary>
        public nint Data;
    }

    [DllImport(Library)] private static extern nint AudioComponentFindNext(nint previous, ref ComponentDescription description);
    [DllImport(Library)] private static extern int AudioComponentInstanceNew(nint component, out nint instance);
    [DllImport(Library)] private static extern int AudioComponentInstanceDispose(nint instance);
    [DllImport(Library)] private static extern int AudioUnitSetProperty(nint unit, uint property, uint scope, uint bus, nint data, uint size);
    [DllImport(Library)] private static extern int AudioUnitGetProperty(nint unit, uint property, uint scope, uint bus, nint data, ref uint size);
    [DllImport(Library)] private static extern int AudioUnitInitialize(nint unit);
    [DllImport(Library)] private static extern int AudioUnitUninitialize(nint unit);
    [DllImport(Library)] private static extern int AudioUnitReset(nint unit, uint scope, uint bus);
    [DllImport(Library)] private static extern int AudioOutputUnitStart(nint unit);
    [DllImport(Library)] private static extern int AudioOutputUnitStop(nint unit);
}
