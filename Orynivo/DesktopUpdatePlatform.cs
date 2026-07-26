using Orynivo.Updates;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Orynivo;

/// <summary>Selects and launches the signed desktop installer for the current operating system and architecture.</summary>
internal static class DesktopUpdatePlatform
{
    /// <summary>Selects the installer asset matching the running desktop process.</summary>
    /// <param name="assets">Signed release-manifest assets.</param>
    /// <param name="requiredFile">Optional exact asset file name that must still match.</param>
    /// <returns>The matching installer, or <see langword="null"/> when automatic installation is unsupported.</returns>
    internal static ReleaseAssetInfo? SelectInstaller(
        IEnumerable<ReleaseAssetInfo> assets,
        string? requiredFile = null)
    {
        var target = GetCurrentTarget();
        if (target is null)
            return null;

        return assets.FirstOrDefault(asset =>
            string.Equals(asset.Component, "desktop", StringComparison.Ordinal) &&
            string.Equals(asset.OperatingSystem, target.Value.OperatingSystem, StringComparison.Ordinal) &&
            string.Equals(asset.Architecture, target.Value.Architecture, StringComparison.Ordinal) &&
            string.Equals(asset.Type, target.Value.PackageType, StringComparison.Ordinal) &&
            (requiredFile is null || string.Equals(asset.File, requiredFile, StringComparison.Ordinal)));
    }

    /// <summary>Launches the verified installer through the current platform's normal installation UI.</summary>
    /// <param name="installerPath">Absolute path of the downloaded and hash-verified installer.</param>
    internal static void LaunchInstaller(string installerPath)
    {
        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo("/usr/bin/open")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(installerPath);
            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("The macOS Installer could not be opened.");
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            if (Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true }) is null)
                throw new InvalidOperationException("The Windows installer could not be opened.");
            return;
        }

        throw new PlatformNotSupportedException("Automatic desktop installation is unavailable on this platform.");
    }

    private static (string OperatingSystem, string Architecture, string PackageType)? GetCurrentTarget()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };
        if (architecture is null)
            return null;

        if (OperatingSystem.IsMacOS())
            return ("macos", architecture, "pkg");
        if (OperatingSystem.IsWindows() && architecture == "x64")
            return ("windows", architecture, "installer");
        return null;
    }
}
