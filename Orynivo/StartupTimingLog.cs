using System.Diagnostics;
using System.IO;
using System.Text;

namespace Orynivo;

/// <summary>
/// Writes startup timing diagnostics to <c>%LOCALAPPDATA%\Orynivo\logs\</c>.
/// </summary>
internal static class StartupTimingLog
{
    private static readonly object Sync = new();
    private static readonly Stopwatch Stopwatch = new();
    private static string? _filePath;

    /// <summary>Starts a new startup timing log file.</summary>
    internal static void Start()
    {
        try
        {
            var logDirectory = AppPaths.GetDataPath("logs");
            Directory.CreateDirectory(logDirectory);
            _filePath = Path.Combine(logDirectory, "startup-timing-latest.log");
            Stopwatch.Restart();
            File.WriteAllText(
                _filePath,
                $"Orynivo startup timing{Environment.NewLine}" +
                $"======================{Environment.NewLine}" +
                $"Timestamp: {DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"Process ID: {Environment.ProcessId}{Environment.NewLine}" +
                $"Data root: {AppPaths.DataRoot}{Environment.NewLine}{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            _filePath = null;
        }
    }

    /// <summary>Writes a startup timing message.</summary>
    /// <param name="message">The message to write.</param>
    internal static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(_filePath))
            return;

        try
        {
            var line = $"[{Stopwatch.ElapsedMilliseconds,8:N0} ms] {message}{Environment.NewLine}";
            lock (Sync)
                File.AppendAllText(_filePath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch { }
    }

    /// <summary>Starts a timed startup logging scope.</summary>
    /// <param name="name">The scope name.</param>
    /// <returns>An object that writes the elapsed time when disposed.</returns>
    internal static IDisposable Time(string name)
    {
        Write($"{name} started");
        return new TimingScope(name);
    }

    private sealed class TimingScope : IDisposable
    {
        private readonly string _name;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _disposed;

        public TimingScope(string name) => _name = name;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _stopwatch.Stop();
            Write($"{_name} finished in {_stopwatch.ElapsedMilliseconds:N0} ms");
        }
    }
}
