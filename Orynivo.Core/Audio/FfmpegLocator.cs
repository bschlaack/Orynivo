using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Orynivo.Audio;

/// <summary>
/// Locates <c>ffmpeg</c> and <c>ffprobe</c> binaries, downloading pre-built packages on Windows
/// and macOS when the binaries are absent from the application directory, the user cache, and
/// known system installation directories. Linux binaries must be installed separately through
/// the system package manager.
/// After a successful locate or download the directory that contains the binaries is prepended
/// to the current process PATH so all <see cref="System.Diagnostics.ProcessStartInfo"/> callers
/// can reference them by bare name without modification.
/// </summary>
public static class FfmpegLocator
{
    private const string WindowsReleaseApiUrl =
        "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest";
    private const string MacReleaseApiUrl =
        "https://api.github.com/repos/eugeneware/ffmpeg-static/releases/latest";

    private static string FfmpegBinary =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

    private static string FfprobeBinary =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffprobe.exe" : "ffprobe";

    /// <summary>
    /// Ensures that <c>ffmpeg</c> and <c>ffprobe</c> are reachable as child processes.
    /// On Windows and macOS, downloads a matching pre-built package when neither the application
    /// directory, the per-user FFmpeg cache, known platform installation directories, nor the
    /// system PATH contains the binaries. On Linux, returns <see langword="false"/> when the
    /// binaries are not already installed.
    /// </summary>
    /// <param name="progress">
    /// Receives status strings during a download. Pass a <see cref="Progress{T}"/>
    /// constructed on the UI thread so callbacks are marshalled back automatically.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the binaries are available; <see langword="false"/> when the
    /// download failed or (on Linux) the binaries are not installed.
    /// </returns>
    public static async Task<bool> EnsureAvailableAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureValidCurrentDirectory();
        var userDir = AppPaths.GetDataPath("ffmpeg");
        var userFfmpeg = Path.Combine(userDir, FfmpegBinary);
        var userFfprobe = Path.Combine(userDir, FfprobeBinary);

        var installedDirectory = FindToolsDirectory();
        if (installedDirectory is not null)
        {
            PrependToPath(installedDirectory);
            return true;
        }

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            return false;

        try
        {
            progress?.Report("Downloading FFmpeg…");
            Directory.CreateDirectory(userDir);
            if (OperatingSystem.IsMacOS())
            {
                await DownloadAndExtractMacAsync(
                    userFfmpeg,
                    userFfprobe,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await DownloadAndExtractWindowsAsync(
                    userFfmpeg,
                    userFfprobe,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            PrependToPath(userDir);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                    or IOException
                                    or InvalidDataException
                                    or UnauthorizedAccessException
                                    or OperationCanceledException)
        {
            _ = ex;
            return false;
        }
    }

    /// <summary>
    /// Determines whether both FFmpeg command-line tools are installed in a location known to Orynivo.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <c>ffmpeg</c> and <c>ffprobe</c> exist in the application
    /// directory, the per-user cache, a known platform installation directory, or a directory
    /// on the current process PATH.
    /// </returns>
    public static bool IsAvailable()
    {
        return FindToolsDirectory() is not null;
    }

    /// <summary>
    /// Returns an existing directory that can safely be used as
    /// <see cref="System.Diagnostics.ProcessStartInfo.WorkingDirectory"/> for FFmpeg child processes.
    /// </summary>
    /// <returns>The application base directory when it exists; otherwise the Orynivo data directory or temp directory.</returns>
    public static string GetSafeWorkingDirectory()
    {
        if (Directory.Exists(AppContext.BaseDirectory))
        {
            return AppContext.BaseDirectory;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            return AppPaths.DataRoot;
        }
        catch
        {
            return Path.GetTempPath();
        }
    }

    /// <summary>
    /// Resets the process current directory when a shortcut or installer left it pointing to a missing path.
    /// </summary>
    public static void EnsureValidCurrentDirectory()
    {
        try
        {
            if (Directory.Exists(Environment.CurrentDirectory))
            {
                return;
            }
        }
        catch
        {
            // Fall through to reset below.
        }

        try
        {
            Environment.CurrentDirectory = GetSafeWorkingDirectory();
        }
        catch
        {
            // Child process start calls still receive an explicit safe working directory.
        }
    }

    private static string? FindToolsDirectory()
    {
        foreach (var directory in GetSearchDirectories())
        {
            try
            {
                if (File.Exists(Path.Combine(directory, FfmpegBinary)) &&
                    File.Exists(Path.Combine(directory, FfprobeBinary)))
                {
                    return directory;
                }
            }
            catch
            {
                // Ignore inaccessible or malformed PATH entries.
            }
        }

        return null;
    }

    private static IEnumerable<string> GetSearchDirectories()
    {
        yield return AppContext.BaseDirectory;
        yield return AppPaths.GetDataPath("ffmpeg");

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return directory;
        }

        if (!OperatingSystem.IsMacOS())
            yield break;

        // Finder-launched .app bundles receive a minimal PATH that normally omits package-manager
        // prefixes, so probe the conventional Apple Silicon, Intel, MacPorts, pkgsrc, and Fink paths.
        string[] macDirectories =
        [
            "/opt/homebrew/bin",
            "/opt/homebrew/opt/ffmpeg/bin",
            "/usr/local/bin",
            "/usr/local/opt/ffmpeg/bin",
            "/opt/local/bin",
            "/opt/pkg/bin",
            "/sw/bin"
        ];
        foreach (var directory in macDirectories)
            yield return directory;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            yield return Path.Combine(userProfile, ".local", "bin");
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

        var downloadUrl = await ResolveWindowsDownloadUrlAsync(client, cancellationToken)
            .ConfigureAwait(false);
        using var response = await client.GetAsync(
            downloadUrl,
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
            ExtractBinaryEntry(zip, FfmpegBinary, targetFfmpeg);
            ExtractBinaryEntry(zip, FfprobeBinary, targetFfprobe);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
        }
    }

    [SupportedOSPlatform("macos")]
    private static async Task DownloadAndExtractMacAsync(
        string targetFfmpeg,
        string targetFfprobe,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => throw new PlatformNotSupportedException(
                $"Automatic FFmpeg download is not available for macOS {RuntimeInformation.ProcessArchitecture}.")
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0");
        var downloads = await ResolveMacDownloadUrlsAsync(
            client,
            architecture,
            cancellationToken).ConfigureAwait(false);
        await DownloadMacBinaryAsync(
            client,
            downloads.Ffmpeg,
            targetFfmpeg,
            progress,
            cancellationToken).ConfigureAwait(false);
        await DownloadMacBinaryAsync(
            client,
            downloads.Ffprobe,
            targetFfprobe,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    [SupportedOSPlatform("macos")]
    private static async Task DownloadMacBinaryAsync(
        HttpClient client,
        string downloadUrl,
        string targetPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Downloading FFmpeg…");
        using var response = await client.GetAsync(
            downloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tempFile = Path.GetTempFileName();
        try
        {
            await using (var source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                tempFile,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            File.SetUnixFileMode(
                tempFile,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherExecute);
            File.Move(tempFile, targetPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
        }
    }

    private static async Task<(string Ffmpeg, string Ffprobe)> ResolveMacDownloadUrlsAsync(
        HttpClient client,
        string architecture,
        CancellationToken cancellationToken)
    {
        using var releaseResponse = await client.GetAsync(MacReleaseApiUrl, cancellationToken)
            .ConfigureAwait(false);
        releaseResponse.EnsureSuccessStatusCode();
        await using var releaseStream = await releaseResponse.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var releaseJson = await JsonDocument.ParseAsync(
            releaseStream,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!releaseJson.RootElement.TryGetProperty("assets", out var assetsJson) ||
            assetsJson.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "ffmpeg-static release response does not contain an assets array.");
        }

        var assets = assetsJson
            .EnumerateArray()
            .Select(TryReadAsset)
            .Where(asset => asset is not null)
            .Select(asset => asset!.Value)
            .ToDictionary(asset => asset.Name, asset => asset.Url, StringComparer.OrdinalIgnoreCase);
        var ffmpegName = $"ffmpeg-darwin-{architecture}";
        var ffprobeName = $"ffprobe-darwin-{architecture}";
        if (!assets.TryGetValue(ffmpegName, out var ffmpegUrl) ||
            !assets.TryGetValue(ffprobeName, out var ffprobeUrl))
        {
            throw new InvalidDataException(
                $"The latest ffmpeg-static release does not contain {ffmpegName} and {ffprobeName}.");
        }

        return (ffmpegUrl, ffprobeUrl);
    }

    private static async Task<string> ResolveWindowsDownloadUrlAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var releaseResponse = await client.GetAsync(WindowsReleaseApiUrl, cancellationToken)
            .ConfigureAwait(false);
        releaseResponse.EnsureSuccessStatusCode();

        await using var releaseStream = await releaseResponse.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var releaseJson = await JsonDocument.ParseAsync(
            releaseStream,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var release = releaseJson.RootElement;
        if (!release.TryGetProperty("assets_url", out var assetsUrlJson))
        {
            throw new InvalidDataException("BtbN release response does not contain an assets_url.");
        }

        var assetsUrl = assetsUrlJson.GetString();
        if (string.IsNullOrWhiteSpace(assetsUrl))
        {
            throw new InvalidDataException("BtbN release response contains an empty assets_url.");
        }

        var architectureToken = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "winarm64"
            : "win64";
        for (var page = 1; page <= 5; page++)
        {
            var pageUrl = $"{assetsUrl}?per_page=100&page={page}";
            using var assetsResponse = await client.GetAsync(pageUrl, cancellationToken)
                .ConfigureAwait(false);
            assetsResponse.EnsureSuccessStatusCode();
            await using var assetsStream = await assetsResponse.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var assetsJson = await JsonDocument.ParseAsync(
                assetsStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var bestUrl = assetsJson.RootElement
                .EnumerateArray()
                .Select(TryReadAsset)
                .Where(asset => asset is not null)
                .Select(asset => asset!.Value)
                .Where(asset =>
                    asset.Name.Contains(architectureToken, StringComparison.OrdinalIgnoreCase) &&
                    asset.Name.Contains("lgpl", StringComparison.OrdinalIgnoreCase) &&
                    asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    !asset.Name.Contains("shared", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(asset => asset.Name.StartsWith("ffmpeg-master-", StringComparison.OrdinalIgnoreCase))
                .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
                .Select(asset => asset.Url)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(bestUrl))
            {
                return bestUrl;
            }
        }

        throw new InvalidDataException("No suitable BtbN Windows LGPL FFmpeg ZIP asset was found.");
    }

    private static (string Name, string Url)? TryReadAsset(JsonElement asset)
    {
        if (!asset.TryGetProperty("name", out var nameJson) ||
            !asset.TryGetProperty("browser_download_url", out var urlJson))
        {
            return null;
        }

        var name = nameJson.GetString();
        var url = urlJson.GetString();
        return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)
            ? null
            : (name, url);
    }

    private static void ExtractBinaryEntry(ZipArchive zip, string binaryName, string targetPath)
    {
        var entry = zip.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, binaryName, StringComparison.OrdinalIgnoreCase) &&
            e.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            ?? zip.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, binaryName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidDataException(
                $"Expected binary '{binaryName}' not found in the downloaded archive.");
        }

        entry.ExtractToFile(targetPath, overwrite: true);
    }
}
