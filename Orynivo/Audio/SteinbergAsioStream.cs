using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Orynivo.Audio;

/// <summary>
/// Managed wrapper around <c>AsioBridge.dll</c> (Steinberg ASIO) or <c>CwAsioBridge.dll</c> (cwASIO).
/// Opens a driver, starts the stream, and accepts interleaved 32-bit float PCM or raw DSD bytes.
/// One instance represents one active ASIO session; dispose to close the driver.
/// </summary>
public sealed class SteinbergAsioStream : IDisposable
{
    private readonly NativeApi _native;
    private bool _disposed;

    /// <summary>Gets a value indicating whether <c>AsioBridge.dll</c> is present and loadable.</summary>
    public static bool IsAvailable => IsBackendAvailable(OutputBackend.Asio);
    /// <summary>Gets a value indicating whether <c>CwAsioBridge.dll</c> is present and loadable.</summary>
    public static bool IsCwAsioAvailable => IsBackendAvailable(OutputBackend.CwAsio);

    /// <summary>
    /// Opens the specified ASIO driver and configures it for the given sample rate, channel count,
    /// and PCM or DSD mode.
    /// </summary>
    /// <param name="backend">Which native bridge to load (<see cref="OutputBackend.Asio"/> or <see cref="OutputBackend.CwAsio"/>).</param>
    /// <param name="driverName">ASIO driver name as returned by <see cref="GetDriverNames"/>.</param>
    /// <param name="sampleRate">Target sample rate in Hz. For DSD this is the DSD bit rate (e.g. 2 822 400).</param>
    /// <param name="channels">Number of output channels; must match a configuration the driver supports.</param>
    /// <param name="dsd">Pass <see langword="true"/> to open the driver in native DSD mode.</param>
    public SteinbergAsioStream(
        OutputBackend backend,
        string driverName,
        double sampleRate = 192_000,
        int channels = 2,
        bool dsd = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);
        _native = NativeApi.Load(backend);
        try
        {
            ThrowIfFailed(_native.Open(driverName, sampleRate, channels, dsd ? 1 : 0), "open ASIO driver");
        }
        catch
        {
            _native.Dispose();
            throw;
        }
        Channels = channels;
        SampleRate = sampleRate;
        IsDsd = dsd;
    }

    /// <summary>Gets the number of output channels the driver was opened with.</summary>
    public int Channels { get; }
    /// <summary>Gets the sample rate in Hz the driver was opened with.</summary>
    public double SampleRate { get; }
    /// <summary>Gets a value indicating whether the stream is in native DSD mode.</summary>
    public bool IsDsd { get; }
    /// <summary>Gets the driver's preferred buffer size in samples.</summary>
    public int PreferredBufferSize => _native.GetPreferredBufferSize();

    /// <summary>
    /// Returns <see langword="true"/> when the native bridge DLL for <paramref name="backend"/> is present and can be loaded.
    /// </summary>
    /// <param name="backend">The backend to probe.</param>
    public static bool IsBackendAvailable(OutputBackend backend)
    {
        try
        {
            using var native = NativeApi.Load(backend);
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the list of installed ASIO driver names for the given backend,
    /// or an empty list when the backend is unavailable.
    /// </summary>
    /// <param name="backend">The backend to query.</param>
    public static IReadOnlyList<string> GetDriverNames(OutputBackend backend)
    {
        if (!IsBackendAvailable(backend))
            return [];

        using var native = NativeApi.Load(backend);
        var count = native.GetDriverCount();
        var names = new List<string>(Math.Max(0, count));
        for (var index = 0; index < count; index++)
        {
            var buffer = new StringBuilder(128);
            if (native.GetDriverName(index, buffer, buffer.Capacity) == 0)
                names.Add(buffer.ToString());
        }
        return names;
    }

    /// <summary>
    /// Queries the driver for detailed capability information without opening it for playback.
    /// </summary>
    /// <param name="backend">The backend that owns the driver.</param>
    /// <param name="driverName">Driver name as returned by <see cref="GetDriverNames"/>.</param>
    /// <returns>An <see cref="AsioDeviceInfo"/> describing the driver's channel, buffer, and format capabilities.</returns>
    public static AsioDeviceInfo GetDeviceInfo(OutputBackend backend, string driverName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);

        using var native = NativeApi.Load(backend);
        var buffer = new StringBuilder(2048);
        ThrowIfFailed(native.GetDeviceInfo(driverName, buffer, buffer.Capacity), "read ASIO device info");

        var values = buffer
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

        return new AsioDeviceInfo(
            values.GetValueOrDefault("driver", driverName),
            ParseInt(values, "inputs"),
            ParseInt(values, "outputs"),
            ParseInt(values, "bufferMin"),
            ParseInt(values, "bufferMax"),
            ParseInt(values, "bufferPreferred"),
            ParseInt(values, "bufferGranularity"),
            ParseCsvInts(values.GetValueOrDefault("pcmSampleRates")),
            ParseCsvInts(values.GetValueOrDefault("dsdSampleRates")),
            ParseCsvStrings(values.GetValueOrDefault("pcmTypes")),
            bool.TryParse(values.GetValueOrDefault("dsdSupported"), out var supportsDsd) && supportsDsd,
            bool.TryParse(values.GetValueOrDefault("dsdProbeWasConclusive"), out var conclusive) && conclusive,
            ParseCsvStrings(values.GetValueOrDefault("dsdTypes")));
    }

    /// <summary>Starts the ASIO stream. Must be called before writing samples.</summary>
    public void Start()
    {
        ThrowIfDisposed();
        ThrowIfFailed(_native.Start(), "start ASIO stream");
    }

    /// <summary>
    /// Writes interleaved 32-bit float PCM samples to the ASIO output buffer.
    /// </summary>
    /// <param name="samples">Interleaved samples; length must be a multiple of <see cref="Channels"/>.</param>
    /// <returns>Number of samples actually accepted by the driver buffer.</returns>
    public int WriteInterleaved(ReadOnlySpan<float> samples)
    {
        ThrowIfDisposed();
        if (samples.Length % Channels != 0)
            throw new ArgumentException("Sample count must be divisible by channel count.", nameof(samples));
        return _native.WriteInterleaved(samples.ToArray(), samples.Length);
    }

    /// <summary>
    /// Writes interleaved raw DSD bytes to the ASIO output buffer. Only valid when <see cref="IsDsd"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="bytes">Interleaved DSD bytes; length must be a multiple of <see cref="Channels"/>.</param>
    /// <returns>Number of bytes actually accepted by the driver buffer.</returns>
    public int WriteDsdInterleaved(ReadOnlySpan<byte> bytes)
    {
        ThrowIfDisposed();
        if (!IsDsd)
            throw new InvalidOperationException("Stream is not in DSD mode.");
        if (bytes.Length % Channels != 0)
            throw new ArgumentException("Byte count must be divisible by channel count.", nameof(bytes));
        return _native.WriteDsdInterleaved(bytes.ToArray(), bytes.Length);
    }

    /// <summary>Stops the ASIO stream.</summary>
    public void Stop()
    {
        ThrowIfDisposed();
        ThrowIfFailed(_native.Stop(), "stop ASIO stream");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _native.Close();
        _native.Dispose();
        _disposed = true;
    }

    private static void ThrowIfFailed(int code, string operation)
    {
        if (code != 0)
            throw new InvalidOperationException($"Could not {operation}. Native ASIO error code: {code}.");
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> values, string key) =>
        int.TryParse(values.GetValueOrDefault(key), out var value) ? value : 0;

    private static IReadOnlyList<int> ParseCsvInts(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(static item => int.Parse(item))
                .ToArray();

    private static IReadOnlyList<string> ParseCsvStrings(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class NativeApi : IDisposable
    {
        private readonly nint _handle;
        private readonly GetDriverCountDelegate _getDriverCount;
        private readonly GetDriverNameDelegate _getDriverName;
        private readonly OpenDelegate _open;
        private readonly SimpleDelegate _start;
        private readonly WriteFloatDelegate _writeInterleaved;
        private readonly WriteByteDelegate _writeDsdInterleaved;
        private readonly SimpleDelegate _stop;
        private readonly CloseDelegate _close;
        private readonly SimpleDelegate _getPreferredBufferSize;
        private readonly GetDeviceInfoDelegate _getDeviceInfo;

        private NativeApi(string path)
        {
            _handle = NativeLibrary.Load(path);
            _getDriverCount = GetDelegate<GetDriverCountDelegate>("asio_get_driver_count");
            _getDriverName = GetDelegate<GetDriverNameDelegate>("asio_get_driver_name");
            _open = GetDelegate<OpenDelegate>("asio_open");
            _start = GetDelegate<SimpleDelegate>("asio_start");
            _writeInterleaved = GetDelegate<WriteFloatDelegate>("asio_write_interleaved");
            _writeDsdInterleaved = GetDelegate<WriteByteDelegate>("asio_write_dsd_interleaved");
            _stop = GetDelegate<SimpleDelegate>("asio_stop");
            _close = GetDelegate<CloseDelegate>("asio_close");
            _getPreferredBufferSize = GetDelegate<SimpleDelegate>("asio_get_preferred_buffer_size");
            _getDeviceInfo = GetDelegate<GetDeviceInfoDelegate>("asio_get_device_info");
        }

        public static NativeApi Load(OutputBackend backend)
        {
            var fileName = backend switch
            {
                OutputBackend.Asio => "AsioBridge.dll",
                OutputBackend.CwAsio => "CwAsioBridge.dll",
                _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Not an ASIO backend.")
            };
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path))
                throw new DllNotFoundException($"{fileName} was not found.");
            return new NativeApi(path);
        }

        public int GetDriverCount() => _getDriverCount();
        public int GetDriverName(int index, StringBuilder buffer, int length) =>
            _getDriverName(index, buffer, length);
        public int Open(string name, double rate, int channels, int dsd) =>
            _open(name, rate, channels, dsd);
        public int Start() => _start();
        public int WriteInterleaved(float[] samples, int count) => _writeInterleaved(samples, count);
        public int WriteDsdInterleaved(byte[] bytes, int count) => _writeDsdInterleaved(bytes, count);
        public int Stop() => _stop();
        public void Close() => _close();
        public int GetPreferredBufferSize() => _getPreferredBufferSize();
        public int GetDeviceInfo(string name, StringBuilder buffer, int length) =>
            _getDeviceInfo(name, buffer, length);
        public void Dispose() => NativeLibrary.Free(_handle);

        private T GetDelegate<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_handle, name));

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetDriverCountDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int GetDriverNameDelegate(int index, StringBuilder buffer, int bufferLength);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int OpenDelegate(string driverName, double sampleRate, int outputChannels, int dsdMode);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SimpleDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int WriteFloatDelegate(float[] samples, int sampleCount);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int WriteByteDelegate(byte[] bytes, int byteCount);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CloseDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int GetDeviceInfoDelegate(string driverName, StringBuilder buffer, int bufferLength);
    }
}
