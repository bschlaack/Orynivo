using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Orynivo;

/// <summary>
/// Writes structured crash reports for unhandled exceptions to
/// <c>%LOCALAPPDATA%\Orynivo\logs\</c>.
/// </summary>
public static class CrashLogger
{
    private static readonly object Sync = new();
    private static int _fileSequence;

    /// <summary>
    /// Serialises <paramref name="exception"/> together with environment metadata into a timestamped
    /// log file and returns the file path.
    /// </summary>
    /// <param name="exception">The unhandled exception to record.</param>
    /// <param name="source">Label identifying the handler that caught the exception (e.g. <c>"UI thread"</c>).</param>
    /// <returns>Absolute path of the written log file, or an empty string if writing failed.</returns>
    public static string Log(Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var logDirectory = AppPaths.GetDataPath("logs");
            Directory.CreateDirectory(logDirectory);

            var timestamp = DateTimeOffset.Now;
            var sequence = Interlocked.Increment(ref _fileSequence);
            var filePath = Path.Combine(
                logDirectory,
                $"crash-{timestamp:yyyyMMdd-HHmmss-fff}-{sequence:D2}.log");
            var process = Process.GetCurrentProcess();
            var assembly = Assembly.GetEntryAssembly();

            var report = new StringBuilder()
                .AppendLine("Orynivo crash report")
                .AppendLine("===================")
                .AppendLine($"Timestamp: {timestamp:O}")
                .AppendLine($"Source: {source}")
                .AppendLine($"Process ID: {Environment.ProcessId}")
                .AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}")
                .AppendLine($"Application version: {assembly?.GetName().Version?.ToString() ?? "unknown"}")
                .AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}")
                .AppendLine($"OS: {RuntimeInformation.OSDescription}")
                .AppendLine($"OS architecture: {RuntimeInformation.OSArchitecture}")
                .AppendLine($"Command line: {Environment.CommandLine}")
                .AppendLine($"Working directory: {Environment.CurrentDirectory}")
                .AppendLine($"64-bit process: {Environment.Is64BitProcess}")
                .AppendLine($"Thread ID: {Environment.CurrentManagedThreadId}")
                .AppendLine($"Working set: {process.WorkingSet64:N0} bytes")
                .AppendLine()
                .AppendLine("Exception")
                .AppendLine("---------")
                .AppendLine(exception.ToString())
                .ToString();

            lock (Sync)
            {
                File.WriteAllText(filePath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            return filePath;
        }
        catch
        {
            return string.Empty;
        }
    }
}
