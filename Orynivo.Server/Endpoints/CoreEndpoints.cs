using System.Runtime.InteropServices;

namespace Orynivo.Server.Endpoints;

/// <summary>
/// Maps the unauthenticated health probe and the authenticated server-info
/// endpoint.
/// </summary>
public static class CoreEndpoints
{
    /// <summary>
    /// Maps <c>GET /api/health</c> (no authentication required) and
    /// <c>GET /api/info</c> (authenticated).
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="settings">The bound server settings.</param>
    /// <param name="serverVersion">The informational server version.</param>
    /// <param name="installType">The detected install type (<c>portable</c>, <c>deb</c>, or <c>rpm</c>).</param>
    /// <param name="updateSupported">Whether signed remote package updates are supported.</param>
    public static void MapCoreEndpoints(
        this WebApplication app,
        ServerSettings settings,
        string serverVersion,
        string installType,
        bool updateSupported)
    {
        app.MapGet("/api/health", () => Results.Ok(new
        {
            Status  = "ok",
            Server  = settings.ServerName,
            Version = serverVersion,
            Time    = DateTimeOffset.UtcNow
        }));

        app.MapGet("/api/info", () => Results.Ok(new
        {
            Name       = settings.ServerName,
            Version    = serverVersion,
            ApiVersion = 1,
            Paths      = settings.LibraryPaths,
            OperatingSystem = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "unknown",
            Architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            InstallType = installType,
            UpdateSupported = updateSupported
        }));
    }
}
