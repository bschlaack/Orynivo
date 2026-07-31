using Orynivo.Updates;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Orynivo;

/// <summary>Selects and launches the signed desktop installer for the current operating system and architecture.</summary>
internal static class DesktopUpdatePlatform
{
    private const string OsReleasePath = "/etc/os-release";

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
    /// <param name="cancellationToken">Token that can cancel waiting for Linux package installation.</param>
    /// <returns>A task that completes after Linux installation exits or after another platform opens its installer.</returns>
    internal static async Task LaunchInstallerAsync(
        string installerPath,
        CancellationToken cancellationToken = default)
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

        if (OperatingSystem.IsLinux())
        {
            await LaunchLinuxInstallerAsync(installerPath, cancellationToken);
            return;
        }

        throw new PlatformNotSupportedException("Automatic desktop installation is unavailable on this platform.");
    }

    /// <summary>Launches a verified Linux package through PolicyKit and the distribution package manager.</summary>
    /// <param name="installerPath">Absolute path of the verified package.</param>
    /// <param name="cancellationToken">Token that can cancel waiting for package installation.</param>
    /// <returns>A task that completes only when the privileged package manager exits successfully.</returns>
    private static async Task LaunchLinuxInstallerAsync(
        string installerPath,
        CancellationToken cancellationToken)
    {
        var packageType = GetLinuxPackageType()
            ?? throw new PlatformNotSupportedException("The Linux package manager is unsupported.");
        var packageManager = packageType switch
        {
            "arch" => "/usr/bin/pacman",
            "deb" => "/usr/bin/apt-get",
            "rpm" when File.Exists("/usr/bin/dnf") => "/usr/bin/dnf",
            "rpm" when File.Exists("/usr/bin/zypper") => "/usr/bin/zypper",
            _ => null
        };
        if (!File.Exists("/usr/bin/pkexec") || packageManager is null || !File.Exists(packageManager))
            throw new PlatformNotSupportedException("The privileged Linux package installer is unavailable.");

        var startInfo = new ProcessStartInfo("/usr/bin/pkexec")
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(packageManager);
        switch (packageType)
        {
            case "arch":
                startInfo.ArgumentList.Add("--noconfirm");
                startInfo.ArgumentList.Add("-U");
                break;
            case "deb":
                startInfo.ArgumentList.Add("install");
                startInfo.ArgumentList.Add("--yes");
                break;
            case "rpm" when packageManager.EndsWith("/dnf", StringComparison.Ordinal):
                startInfo.ArgumentList.Add("install");
                startInfo.ArgumentList.Add("--assumeyes");
                break;
            case "rpm":
                startInfo.ArgumentList.Add("--non-interactive");
                startInfo.ArgumentList.Add("install");
                startInfo.ArgumentList.Add("--allow-unsigned-rpm");
                break;
        }
        startInfo.ArgumentList.Add(installerPath);
        using var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("The Linux package installer could not be started.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The Linux package installer exited with code {process.ExitCode}.");
        }
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
        if (OperatingSystem.IsLinux() && GetLinuxPackageType() is { } packageType)
            return ("linux", architecture, packageType);
        return null;
    }

    /// <summary>Maps the current Linux distribution to its signed release package type.</summary>
    /// <returns><c>arch</c>, <c>deb</c>, or <c>rpm</c>; otherwise <see langword="null"/>.</returns>
    private static string? GetLinuxPackageType()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(OsReleasePath))
            return null;

        var values = File.ReadLines(OsReleasePath)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => parts[0],
                parts => parts[1].Trim().Trim('"'),
                StringComparer.OrdinalIgnoreCase);
        var distribution = string.Join(
            ' ',
            values.GetValueOrDefault("ID"),
            values.GetValueOrDefault("ID_LIKE"));
        var identifiers = distribution.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (identifiers.Any(id => id.Equals("arch", StringComparison.OrdinalIgnoreCase)))
            return "arch";
        if (identifiers.Any(id => id.Equals("debian", StringComparison.OrdinalIgnoreCase) ||
                                  id.Equals("ubuntu", StringComparison.OrdinalIgnoreCase)))
            return "deb";
        if (identifiers.Any(id => id.Equals("fedora", StringComparison.OrdinalIgnoreCase) ||
                                  id.Equals("rhel", StringComparison.OrdinalIgnoreCase) ||
                                  id.Equals("suse", StringComparison.OrdinalIgnoreCase) ||
                                  id.Equals("opensuse", StringComparison.OrdinalIgnoreCase)))
            return "rpm";
        return null;
    }
}
