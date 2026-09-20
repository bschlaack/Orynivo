using System.Globalization;

namespace Orynivo.Library;

/// <summary>
/// Shared naming for automatic library backup archives. The desktop and the server
/// both write and prune the same file names, so the format lives here.
/// </summary>
public static class BackupNaming
{
    /// <summary>File-name prefix of every automatic backup archive.</summary>
    public const string FilePrefix = "orynivo-backup-";

    /// <summary>Builds the archive file name for a timestamp.</summary>
    /// <param name="timestamp">Backup creation timestamp.</param>
    /// <returns>A file name such as <c>orynivo-backup-20260920-123000.zip</c>.</returns>
    public static string BuildFileName(DateTimeOffset timestamp) =>
        $"{FilePrefix}{timestamp.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.zip";

    /// <summary>
    /// Parses the creation timestamp of an automatic backup archive name. Foreign
    /// names are rejected so a user file in the same folder is never pruned.
    /// </summary>
    /// <param name="fileName">File name or path to inspect.</param>
    /// <param name="timestamp">Parsed creation timestamp.</param>
    /// <returns><see langword="true"/> when the name matches the backup pattern.</returns>
    public static bool TryParseTimestamp(string? fileName, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var name = Path.GetFileName(fileName);
        if (!name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stamp = name[FilePrefix.Length..^".zip".Length];
        return DateTimeOffset.TryParseExact(
            stamp,
            "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);
    }
}
