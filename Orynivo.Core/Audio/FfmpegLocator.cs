using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace Orynivo.Audio;

/// <summary>
/// Locates <c>ffmpeg</c> and <c>ffprobe</c> binaries, downloading a pre-built package on Windows
/// when the binaries are absent from the application directory and the system PATH.
/// On Linux and macOS the binaries must be installed separately via the system package manager
/// (e.g. <c>apt install ffmpeg</c>, <c>brew install ffmpeg</c>).
/// After a successful locate or download the directory that contains the binaries is prepended
/// to the current process PATH so all <see cref="System.Diagnostics.ProcessStartInfo"/> callers
/// can reference them by bare name without modification.
/// </summary>
public static class FfmpegLocator
{
    // BtbN LGPL-essential Windows build (GitHub Releases, latest)
    private const string WindowsDownloadUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/" +
        "ffmpeg-master-latest-win64-lgpl-essentials_build.zip";

    private const string WindowsZipFfmpegEntry =
        "ffmpeg-master-latest-win64-lgpl-essentials_build/bin/ffmpeg.exe";

    private const string WindowsZipFfprobeEntry =
        "ffmpeg-master-latest-win64-lgpl-essentials_build/bin/ffprobe.exe";

    private static string FfmpegBinary =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

    private static string FfprobeBinary =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffprobe.exe" : "ffprobe";

    /// <summary>
    /// Ensures that <c>ffmpeg</c> and <c>ffprobe</c> are reachable as child processes.
    /// On Windows, downloads the BtbN LGPL-essential build when neither the application directory
    /// nor the system PATH contains the binaries.
    /// On Linux and macOS, returns <see langword="false"/> when the binaries are not already on PATH.
    /// </summary>
    /// <param name="progress">
    /// Receives status strings during the Windows download. Pass a <see cref="Progress{T}"/>
    /// constructed on the UI thread so callbacks are marshalled back automatically.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the binaries are available; <see langword="false"/> when the
    /// download failed or (on Linux/macOS) the binaries are not installed.
    /// </returns>
    public static async Task<bool> EnsureAvailableAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var appDir = AppContext.BaseDirectory;
        var localFfmpeg  = Path.Combine(appDir, FfmpegBinary);
        var localFfprobe = Path.Combine(appDir, FfprobeBinary);

        if (File.Exists(localFfmpeg) && File.Exists(localFfprobe))
        {
            PrependToPath(appDir);
            return true;
        }

        if (IsOnPath(FfmpegBinary) && IsOnPath(FfprobeBinary))
            return true;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            progress?.Report("Downloading FFmpeg…");
            await DownloadAndExtractWindowsAsync(localFfmpeg, localFfprobe, progress, cancellationToken)
                .ConfigureAwait(false);
            PrependToPath(appDir);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                    or IOException
                                    or InvalidDataException
                                    or OperationCanceledException)
        {
            _ = ex;
            return false;
        }
    }

    private static bool IsOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        return pathEnv
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir =>
            {
                try { return File.Exists(Path.Combine(dir, fileName)); }
                catch { return false; }
            });
    }

    private static void PrependToPath(string directory)
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                   .Any(p => string.Equals(p, directory, StringComparison.OrdinalIgnoreCase)))
            return;
        Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + current);
    }

    private static async Task DownloadAndExtractWindowsAsync(
        string targetFfmpeg,
        string targetFfprobe,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0");

        using var response = await client.GetAsync(
            WindowsDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total   = response.Content.Headers.ContentLength;
        var tempFile = Path.GetTempFileName();
        try
        {
            await using (var netStream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = new FileStream(
                tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await netStream.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    downloaded += read;
                    if (total.HasValue && progress is not null)
                        progress.Report($"Downloading FFmpeg… {(int)(downloaded * 100 / total.Value)} %");
                }
            }

            using var zip = ZipFile.OpenRead(tempFile);
            ExtractEntry(zip, WindowsZipFfmpegEntry,  targetFfmpeg);
            ExtractEntry(zip, WindowsZipFfprobeEntry, targetFfprobe);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
        }
    }

    private static void ExtractEntry(ZipArchive zip, string entryPath, string targetPath)
    {
        var entry = zip.GetEntry(entryPath)
            ?? throw new InvalidDataException(
                $"Expected entry '{entryPath}' not found in the downloaded archive.");
        entry.ExtractToFile(targetPath, overwrite: true);
    }
}
