using System.IO;

namespace Orynivo;

public static class AppPaths
{
    public const string ProductName = "Orynivo";
    public const string LegacyProductName = "Player";

    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create),
        ProductName);

    public static string LegacyDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create),
        LegacyProductName);

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
