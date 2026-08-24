using System.Text;

namespace Orynivo;

/// <summary>Writes sanitized Dashboard phase timings to the application log directory.</summary>
internal static class DashboardPerformanceLog
{
    private static readonly object Sync = new();
    private const long MaximumLogBytes = 2 * 1024 * 1024;

    /// <summary>Gets the path of the rolling Dashboard performance log.</summary>
    internal static string LogPath => AppPaths.GetDataPath("logs", "dashboard-performance.log");

    /// <summary>Queues one already-sanitized diagnostic line for background persistence.</summary>
    /// <param name="message">Timing data without media names, paths, URLs, or credentials.</param>
    internal static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        _ = Task.Run(() =>
        {
            try
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                var line = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";
                lock (Sync)
                {
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= MaximumLogBytes)
                        File.WriteAllText(LogPath, string.Empty, new UTF8Encoding(false));
                    File.AppendAllText(LogPath, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Diagnostics must never affect Dashboard navigation.
            }
        });
    }
}
