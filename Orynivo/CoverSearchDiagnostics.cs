using System.IO;

namespace Orynivo;

/// <summary>Writes bounded, metadata-free cover search phase timings off the UI thread.</summary>
internal static class CoverSearchDiagnostics
{
    private static readonly object Gate = new();

    /// <summary>Queues an aggregate timing record without URLs, queries, or image identities.</summary>
    /// <param name="phase">Fixed internal phase name.</param>
    /// <param name="elapsedMs">Elapsed phase or search time in milliseconds.</param>
    /// <param name="decodeMs">Preview decode duration.</param>
    /// <param name="bytes">Preview payload size.</param>
    internal static void Record(string phase, long elapsedMs, long decodeMs = 0, int bytes = 0)
    {
        _ = Task.Run(() =>
        {
            try
            {
                lock (Gate)
                {
                    var directory = Path.Combine(AppPaths.DataRoot, "logs");
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, "cover-search-performance.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                        File.Move(path, path + ".previous", overwrite: true);
                    File.AppendAllText(path, $"{DateTime.UtcNow:O} phase={phase} elapsed_ms={elapsedMs} decode_ms={decodeMs} bytes={bytes}{Environment.NewLine}");
                }
            }
            catch { /* Diagnostics must not affect the search. */ }
        });
    }
}
