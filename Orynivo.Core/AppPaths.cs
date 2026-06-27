using System.IO;

namespace Orynivo;

/// <summary>
/// Centralises well-known file-system paths for application data and handles one-time migration
/// from the legacy <c>Player</c> data directory to the current <c>Orynivo</c> directory.
/// </summary>
public static class AppPaths
{
    /// <summary>Current product name used as the <c>%LOCALAPPDATA%</c> sub-directory.</summary>
    public const string ProductName = "Orynivo";

    /// <summary>Legacy product name whose data is migrated on first launch.</summary>
    public const string LegacyProductName = "Player";

    /// <summary>Root data directory: <c>%LOCALAPPDATA%\Orynivo\</c>.</summary>
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create),
        ProductName);

    /// <summary>Legacy data directory: <c>%LOCALAPPDATA%\Player\</c>.</summary>
    public static string LegacyDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create),
        LegacyProductName);

    /// <summary>
    /// Copies missing files from <see cref="LegacyDataRoot"/> to <see cref="DataRoot"/> exactly once,
    /// guarded by a marker file so subsequent launches skip the operation.
    /// </summary>
    public static void MigrateLegacyData()
    {
        var migrationMarker = GetDataPath(".legacy-data-migrated");
        if (File.Exists(migrationMarker))
            return;

        if (Directory.Exists(LegacyDataRoot))
        {
            Directory.CreateDirectory(DataRoot);
            CopyMissingFiles(LegacyDataRoot, DataRoot);
        }

        Directory.CreateDirectory(DataRoot);
        File.WriteAllText(migrationMarker, DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>
    /// Builds a path beneath <see cref="DataRoot"/> by joining <paramref name="parts"/> with
    /// <see cref="Path.Combine(string, string)"/>.
    /// </summary>
    /// <param name="parts">Path segments to append to <see cref="DataRoot"/>.</param>
    /// <returns>The combined absolute path.</returns>
    public static string GetDataPath(params string[] parts)
    {
        var path = DataRoot;
        foreach (var part in parts)
            path = Path.Combine(path, part);
        return path;
    }

    private static void CopyMissingFiles(string sourceDirectory, string targetDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            if (!File.Exists(targetFile))
                File.Copy(file, targetFile);
        }
    }
}
