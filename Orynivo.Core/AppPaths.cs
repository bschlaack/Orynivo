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

    /// <summary>
    /// Environment variable that overrides the data directory. Used by the Linux
    /// server package so the systemd service writes to a dedicated, service-owned
    /// directory (e.g. <c>/var/lib/orynivo-server</c>) instead of relying on a
    /// writable <c>$HOME</c>, which a <c>--no-create-home</c> service user lacks.
    /// </summary>
    public const string DataDirEnvironmentVariable = "ORYNIVO_DATA_DIR";

    /// <summary>
    /// Root data directory. Defaults to <c>%LOCALAPPDATA%\Orynivo\</c> (or
    /// <c>$HOME/.local/share/Orynivo</c> on Linux/macOS) but can be overridden
    /// with the <see cref="DataDirEnvironmentVariable"/> environment variable.
    /// </summary>
    public static string DataRoot { get; } = ResolveDataRoot();

    private static string ResolveDataRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create),
            ProductName);
    }

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
