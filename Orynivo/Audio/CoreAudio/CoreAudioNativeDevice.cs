using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Orynivo.Localization;

namespace Orynivo.Audio.CoreAudio;

/// <summary>A stable Core Audio output identity; runtime object IDs are never persisted.</summary>
/// <param name="Uid">HAL device UID used to resolve the device again at startup.</param>
/// <param name="Name">Device-supplied display name.</param>
internal sealed record CoreAudioDeviceInfo(string Uid, string Name);

/// <summary>
/// Minimal macOS HAL control plane. All calls belong on preparation/retirement
/// workers, never the UI or real-time render callback. No native library loads on
/// another platform, and this adapter does not create a render stream.
/// </summary>
internal sealed class CoreAudioNativeDevice : ICoreAudioDeviceControl
{
    private const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint GlobalScope = 0x676c6f62; // glob
    private const uint OutputScope = 0x6f757470; // outp
    private const uint Devices = 0x64657623; // dev#
    private const uint Streams = 0x73746d23; // stm#
    private const uint DeviceUid = 0x75696420; // uid_
    private const uint ObjectName = 0x6c6e616d; // lnam
    private const uint Alive = 0x6c69766e; // livn
    private const uint NominalRate = 0x6e737274; // nsrt
    private const uint AvailableRates = 0x6e737223; // nsr#
    private const uint HogMode = 0x6f696e6b; // oink
    private readonly uint _objectId;

    private CoreAudioNativeDevice(uint objectId, string uid)
    {
        _objectId = objectId;
        Uid = uid;
    }

    /// <inheritdoc/>
    public string Uid { get; }

    /// <inheritdoc/>
    public bool IsAvailable
    {
        get
        {
            try
            {
                return Read<uint>(_objectId, Alive) != 0 &&
                    string.Equals(ReadString(_objectId, DeviceUid), Uid, StringComparison.Ordinal);
            }
            catch (ExternalException) { return false; }
        }
    }

    /// <inheritdoc/>
    public double SampleRate => Read<double>(_objectId, NominalRate);

    /// <summary>Gets this live HAL object for renderer creation only; never persist it.</summary>
    internal uint ObjectId => _objectId;

    /// <summary>Gets a conservative output latency plus hardware-buffer tail.</summary>
    internal TimeSpan DrainTail => TimeSpan.FromSeconds((
        (double)Read<uint>(_objectId, 0x6673697a) + // fsiz, buffer frame size
        ReadOutputUInt(0x6c746e63) + // ltnc, output latency
        ReadOutputUInt(0x73616674) + // saft, safety offset
        ReadArray<uint>(_objectId, Streams, OutputScope)
            .Select(stream => (double)Read<uint>(stream, 0x6c746e63)).DefaultIfEmpty(0).Max()) / SampleRate);

    /// <summary>Returns current nominal-rate capabilities without claiming native stream precision.</summary>
    /// <returns>Display information for the CoreAudio output path.</returns>
    internal WasapiDeviceCapabilities GetCapabilities() => new(
        ReadString(_objectId, ObjectName), "coreaudio:" + Uid, checked((int)SampleRate), 2, 32,
        new[] { 8000, 11025, 22050, 44100, 48000, 88200, 96000, 176400, 192000, 352800, 384000, 705600, 768000 }
            .Where(rate => SupportsRate(rate)).ToArray(),
        [string.Format(LocalizationManager.Current.PcmIntegerFormat, 32, "S32_LE")]);

    private unsafe uint ReadOutputUInt(uint selector)
    {
        var address = new PropertyAddress(selector, OutputScope);
        uint value = 0;
        uint size = sizeof(uint);
        Check(AudioObjectGetPropertyData(_objectId, ref address, 0, 0, ref size, (nint)(&value)));
        if (size != sizeof(uint)) throw new IOException();
        return value;
    }

    /// <inheritdoc/>
    public int HogOwner => Read<int>(_objectId, HogMode);

    /// <summary>Enumerates live output-stream devices using their stable UIDs.</summary>
    /// <returns>Current macOS output devices without activating them.</returns>
    internal static IReadOnlyList<CoreAudioDeviceInfo> GetOutputDevices()
    {
        EnsureMacOS();
        var result = new List<CoreAudioDeviceInfo>();
        foreach (var objectId in ReadArray<uint>(1, Devices))
        {
            try
            {
                if (Read<uint>(objectId, Alive) == 0 ||
                    ReadArray<uint>(objectId, Streams, OutputScope).Length == 0)
                    continue;
                var uid = ReadString(objectId, DeviceUid);
                if (!string.IsNullOrEmpty(uid))
                    result.Add(new CoreAudioDeviceInfo(uid, ReadString(objectId, ObjectName)));
            }
            // A device can disappear between enumeration and property reads.
            catch (ExternalException) { }
        }
        return result;
    }

    /// <summary>Resolves a persisted UID to its current live HAL object.</summary>
    /// <param name="uid">Stable output device UID; not a numeric runtime ID.</param>
    /// <returns>The live device control, or a failure if the UID is absent.</returns>
    internal static CoreAudioNativeDevice Resolve(string uid)
    {
        EnsureMacOS();
        ArgumentException.ThrowIfNullOrEmpty(uid);
        foreach (var objectId in ReadArray<uint>(1, Devices))
        {
            try
            {
                if (ReadString(objectId, DeviceUid) == uid &&
                    Read<uint>(objectId, Alive) != 0 &&
                    ReadArray<uint>(objectId, Streams, OutputScope).Length > 0)
                    return new CoreAudioNativeDevice(objectId, uid);
            }
            catch (ExternalException) { }
        }
        throw new IOException();
    }

    /// <inheritdoc/>
    public bool SupportsRate(double rate) =>
        double.IsFinite(rate) && rate > 0 &&
        ReadArray<RateRange>(_objectId, AvailableRates).Any(range =>
            rate >= range.Minimum && rate <= range.Maximum);

    /// <inheritdoc/>
    public void SetSampleRate(double rate)
    {
        if (!double.IsFinite(rate) || rate <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate));
        Write(_objectId, NominalRate, rate);
    }

    /// <inheritdoc/>
    public void SetHogOwner(int pid)
    {
        if (pid != -1 && pid != Environment.ProcessId)
            throw new ArgumentOutOfRangeException(nameof(pid));
        Write(_objectId, HogMode, pid);
    }

    /// <inheritdoc/>
    public async Task WaitForSampleRateAsync(double rate, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAvailable)
                throw new IOException();
            if (SampleRate == rate)
                return;
            if (timer.Elapsed >= TimeSpan.FromSeconds(2))
                throw new TimeoutException();
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException();
    }

    private static void Check(int status)
    {
        if (status != 0)
            throw new ExternalException(null, status);
    }

    private static unsafe T Read<T>(uint objectId, uint selector) where T : unmanaged
    {
        var address = new PropertyAddress(selector, GlobalScope);
        var size = (uint)sizeof(T);
        T value = default;
        Check(AudioObjectGetPropertyData(objectId, ref address, 0, 0, ref size, (nint)(&value)));
        if (size != sizeof(T))
            throw new IOException();
        return value;
    }

    private static unsafe T[] ReadArray<T>(uint objectId, uint selector, uint scope = GlobalScope)
        where T : unmanaged
    {
        var address = new PropertyAddress(selector, scope);
        Check(AudioObjectGetPropertyDataSize(objectId, ref address, 0, 0, out var size));
        if (size > 1024 * 1024 || size % sizeof(T) != 0)
            throw new IOException();
        if (size == 0)
            return [];
        var values = new T[size / sizeof(T)];
        fixed (T* data = values)
            Check(AudioObjectGetPropertyData(objectId, ref address, 0, 0, ref size, (nint)data));
        if (size > values.Length * sizeof(T) || size % sizeof(T) != 0)
            throw new IOException();
        Array.Resize(ref values, checked((int)size / sizeof(T)));
        return values;
    }

    private static unsafe void Write<T>(uint objectId, uint selector, T value) where T : unmanaged
    {
        var address = new PropertyAddress(selector, GlobalScope);
        Check(AudioObjectIsPropertySettable(objectId, ref address, out var settable));
        if (settable == 0)
            throw new NotSupportedException();
        Check(AudioObjectSetPropertyData(objectId, ref address, 0, 0, (uint)sizeof(T), (nint)(&value)));
    }

    private static string ReadString(uint objectId, uint selector)
    {
        // Core Audio transfers a retained CFString to the property caller.
        var value = Read<nint>(objectId, selector);
        if (value == 0)
            return string.Empty;
        try
        {
            var length = CFStringGetLength(value);
            if (length < 0 || length > 65_536)
                throw new IOException();
            var buffer = new byte[checked((int)length * 4 + 1)];
            if (CFStringGetCString(value, buffer, buffer.Length, 0x08000100) == 0)
                throw new IOException();
            var terminator = Array.IndexOf(buffer, (byte)0);
            if (terminator < 0)
                throw new IOException();
            return Encoding.UTF8.GetString(buffer, 0, terminator);
        }
        finally { CFRelease(value); }
    }

    /// <summary>Native HAL property address with main element zero.</summary>
    /// <param name="selector">Native property selector.</param>
    /// <param name="scope">Native property scope.</param>
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyAddress(uint selector, uint scope)
    {
        /// <summary>Identifies the native property.</summary>
        internal uint Selector = selector;
        /// <summary>Identifies global or output scope.</summary>
        internal uint Scope = scope;
        /// <summary>Identifies the main property element.</summary>
        internal uint Element = 0;
    }

    /// <summary>Native AudioValueRange representation.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RateRange
    {
        /// <summary>Gets the inclusive minimum nominal rate.</summary>
        internal double Minimum;
        /// <summary>Gets the inclusive maximum nominal rate.</summary>
        internal double Maximum;
    }

    [DllImport(CoreAudio)]
    private static extern int AudioObjectGetPropertyDataSize(uint objectId,
        ref PropertyAddress address, uint qualifierSize, nint qualifier, out uint size);

    [DllImport(CoreAudio)]
    private static extern int AudioObjectGetPropertyData(uint objectId,
        ref PropertyAddress address, uint qualifierSize, nint qualifier, ref uint size, nint data);

    [DllImport(CoreAudio)]
    private static extern int AudioObjectSetPropertyData(uint objectId,
        ref PropertyAddress address, uint qualifierSize, nint qualifier, uint size, nint data);

    [DllImport(CoreAudio)]
    private static extern int AudioObjectIsPropertySettable(uint objectId,
        ref PropertyAddress address, out byte settable);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(nint value);

    [DllImport(CoreFoundation)]
    private static extern byte CFStringGetCString(nint value, [Out] byte[] buffer, nint size, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(nint value);
}
