using System.Text;

namespace Orynivo.Library;

/// <summary>
/// Imports and exports UTF-8 M3U8 playlists while preserving local and remote entries.
/// </summary>
internal static class M3u8PlaylistService
{
    /// <summary>
    /// Reads an M3U8 file, resolves relative local paths against its directory, and skips credential-bearing URLs.
    /// </summary>
    /// <param name="filePath">Absolute path of the M3U8 file.</param>
    /// <returns>The resolved entries and import diagnostics.</returns>
    internal static async Task<M3u8ImportResult> ImportAsync(string filePath)
    {
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath))
                            ?? Environment.CurrentDirectory;
        var entries = new List<string>();
        var missingLocalFiles = 0;
        var remoteEntries = 0;
        var skippedCredentialUrls = 0;
        var skippedInvalidEntries = 0;

        foreach (var rawLine in await File.ReadAllLinesAsync(filePath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (TryGetRemoteUri(line, out var remoteUri))
            {
                if (ContainsPersistedCredential(remoteUri))
                {
                    skippedCredentialUrls++;
                    continue;
                }

                entries.Add(remoteUri.AbsoluteUri);
                remoteEntries++;
                continue;
            }

            try
            {
                var localPath = TryGetFileUriPath(line)
                                ?? (Path.IsPathFullyQualified(line)
                                    ? Path.GetFullPath(line)
                                    : Path.GetFullPath(line, baseDirectory));
                entries.Add(localPath);
                if (!File.Exists(localPath))
                    missingLocalFiles++;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                skippedInvalidEntries++;
            }
        }

        return new M3u8ImportResult(
            entries,
            missingLocalFiles,
            remoteEntries,
            skippedCredentialUrls,
            skippedInvalidEntries);
    }

    /// <summary>
    /// Writes a UTF-8 M3U8 file. Local paths are made relative to the destination when possible.
    /// </summary>
    /// <param name="filePath">Absolute destination path.</param>
    /// <param name="entries">Playlist paths or HTTP/HTTPS URLs in playback order.</param>
    /// <returns>Export diagnostics.</returns>
    internal static async Task<M3u8ExportResult> ExportAsync(
        string filePath,
        IEnumerable<string> entries)
    {
        var destination = Path.GetFullPath(filePath);
        var baseDirectory = Path.GetDirectoryName(destination)
                            ?? Environment.CurrentDirectory;
        var lines = new List<string> { "#EXTM3U" };
        var exportedEntries = 0;
        var skippedCredentialUrls = 0;
        var skippedInvalidEntries = 0;

        foreach (var rawEntry in entries)
        {
            var entry = rawEntry.Trim();
            if (entry.Length == 0)
                continue;

            if (TryGetRemoteUri(entry, out var remoteUri))
            {
                if (ContainsPersistedCredential(remoteUri))
                {
                    skippedCredentialUrls++;
                    continue;
                }

                lines.Add(remoteUri.AbsoluteUri);
                exportedEntries++;
                continue;
            }

            try
            {
                var absolutePath = TryGetFileUriPath(entry) ?? Path.GetFullPath(entry);
                var relativePath = Path.GetRelativePath(baseDirectory, absolutePath);
                lines.Add(Path.IsPathFullyQualified(relativePath)
                    ? absolutePath
                    : relativePath.Replace('\\', '/'));
                exportedEntries++;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                skippedInvalidEntries++;
            }
        }

        await File.WriteAllLinesAsync(
            destination,
            lines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new M3u8ExportResult(
            exportedEntries,
            skippedCredentialUrls,
            skippedInvalidEntries);
    }

    private static bool TryGetRemoteUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme is "http" or "https")
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static string? TryGetFileUriPath(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
            return uri.LocalPath;
        return null;
    }

    private static bool ContainsPersistedCredential(Uri uri)
    {
        var decoded = Uri.UnescapeDataString(uri.Query);
        return uri.UserInfo.Length > 0 ||
               decoded.Contains("X-Plex-Token=", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Describes the result of reading an M3U8 playlist.
/// </summary>
/// <param name="Entries">Resolved local paths and permitted remote URLs.</param>
/// <param name="MissingLocalFiles">Number of local entries whose files are currently missing.</param>
/// <param name="RemoteEntries">Number of imported HTTP or HTTPS entries.</param>
/// <param name="SkippedCredentialUrls">Number of URLs skipped because they contained a Plex token.</param>
/// <param name="SkippedInvalidEntries">Number of malformed path entries that could not be resolved.</param>
internal sealed record M3u8ImportResult(
    IReadOnlyList<string> Entries,
    int MissingLocalFiles,
    int RemoteEntries,
    int SkippedCredentialUrls,
    int SkippedInvalidEntries);

/// <summary>
/// Describes the result of writing an M3U8 playlist.
/// </summary>
/// <param name="ExportedEntries">Number of entries written to the file.</param>
/// <param name="SkippedCredentialUrls">Number of URLs skipped because they contained a Plex token.</param>
/// <param name="SkippedInvalidEntries">Number of malformed path entries that could not be written.</param>
internal sealed record M3u8ExportResult(
    int ExportedEntries,
    int SkippedCredentialUrls,
    int SkippedInvalidEntries);
