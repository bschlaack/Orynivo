using System.Runtime.InteropServices;
using System.Text;

namespace Orynivo.Audio;

public sealed class SteinbergAsioStream : IDisposable
{
    private bool _disposed;

    public SteinbergAsioStream(string driverName, double sampleRate = 192_000, int channels = 2, bool dsd = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);
        ThrowIfFailed(Native.asio_open(driverName, sampleRate, channels, dsd ? 1 : 0), "open ASIO driver");
        Channels = channels;
        SampleRate = sampleRate;
        IsDsd = dsd;
    }

    public int Channels { get; }
    public double SampleRate { get; }
    public bool IsDsd { get; }
    public int PreferredBufferSize => Native.asio_get_preferred_buffer_size();

    public static IReadOnlyList<string> GetDriverNames()
    {
        var count = Native.asio_get_driver_count();
        var names = new List<string>(Math.Max(0, count));
        for (var index = 0; index < count; index++)
        {
            var buffer = new StringBuilder(128);
            if (Native.asio_get_driver_name(index, buffer, buffer.Capacity) == 0)
            {
                names.Add(buffer.ToString());
            }
        }

        return names;
    }

    public static AsioDeviceInfo GetDeviceInfo(string driverName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);

        var buffer = new StringBuilder(2048);
        ThrowIfFailed(Native.asio_get_device_info(driverName, buffer, buffer.Capacity), "read ASIO device info");

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
            bool.TryParse(values.GetValueOrDefault("dsdProbeWasConclusive"), out var dsdProbeWasConclusive) && dsdProbeWasConclusive,
            ParseCsvStrings(values.GetValueOrDefault("dsdTypes")));
    }

    public void Start()
    {
        ThrowIfDisposed();
        ThrowIfFailed(Native.asio_start(), "start ASIO stream");
    }

    public int WriteInterleaved(ReadOnlySpan<float> samples)
    {
        ThrowIfDisposed();
        if (samples.Length % Channels != 0)
        {
            throw new ArgumentException("Sample count must be divisible by channel count.", nameof(samples));
        }

        return Native.asio_write_interleaved(samples.ToArray(), samples.Length);
    }

    public int WriteDsdInterleaved(ReadOnlySpan<byte> bytes)
    {
        ThrowIfDisposed();
        if (!IsDsd)
        {
            throw new InvalidOperationException("Stream is not in DSD mode.");
        }

        if (bytes.Length % Channels != 0)
        {
            throw new ArgumentException("Byte count must be divisible by channel count.", nameof(bytes));
        }

        return Native.asio_write_dsd_interleaved(bytes.ToArray(), bytes.Length);
    }

    public void Stop()
    {
        ThrowIfDisposed();
        ThrowIfFailed(Native.asio_stop(), "stop ASIO stream");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Native.asio_close();
        _disposed = true;
    }

    private static void ThrowIfFailed(int code, string operation)
    {
        if (code != 0)
        {
            throw new InvalidOperationException($"Could not {operation}. Native ASIO error code: {code}.");
        }
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static class Native
    {
        [DllImport("AsioBridge.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int asio_get_driver_count();

        [DllImport("AsioBridge.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int asio_get_driver_name(int index, StringBuilder buffer, int bufferLength);

        [DllImport("AsioBridge.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int asio_open(string driverName, double sampleRate, int outputChannels, int dsdMode);

        [DllImport("AsioBridge.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int asio_start();

        [DllImport("AsioBridge.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int asio_write_interleaved(float[] samples, int sampleCount);

        [DllImport("AsioBridge.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int asio_write_dsd_interleaved(byte[] bytes, int byteCount);

        [DllImport("AsioBridge.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int asio_stop();

        [DllImport("AsioBridge.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void asio_close();

        [DllImport("AsioBridge.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int asio_get_preferred_buffer_size();

        [DllImport("AsioBridge.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int asio_get_device_info(string driverName, StringBuilder buffer, int bufferLength);
    }
}
