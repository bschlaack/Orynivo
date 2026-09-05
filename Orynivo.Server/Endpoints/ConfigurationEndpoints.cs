using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orynivo.Server.Services;

namespace Orynivo.Server.Endpoints;

/// <summary>Maps endpoints for remote server configuration and filesystem browsing.</summary>
public static class ConfigurationEndpoints
{
    private static readonly object AppSettingsWriteLock = new();

    /// <summary>
    /// Editable configuration file shipped by the Linux DEB/RPM packages. It is
    /// owned by the service user and preserved across package upgrades, unlike the
    /// copy under the read-only content root (<c>/usr/lib/orynivo-server</c>).
    /// </summary>
    public const string LinuxConfigFilePath = "/etc/orynivo-server/appsettings.json";

    /// <summary>Registers server configuration and directory browsing routes on <paramref name="app"/>.</summary>
    /// <param name="app">The endpoint route builder to register routes on.</param>
    public static void MapConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/settings/library-paths", (ServerSettings settings) =>
            Results.Ok(new LibraryPathsRequest(settings.LibraryPaths)));

        api.MapPut(
            "/settings/library-paths",
            (LibraryPathsRequest request, ServerSettings settings, LibraryService libraryService, IWebHostEnvironment env) =>
            {
                var paths = NormalizePaths(request.Paths);
                libraryService.UpdateLibraryPaths(paths);
                PersistLibraryPaths(env.ContentRootPath, settings, paths);
                libraryService.TriggerScan();
                return Results.Ok(new LibraryPathsRequest(settings.LibraryPaths));
            });

        api.MapGet("/settings/replaygain", (ServerSettings settings) =>
            Results.Ok(new ReplayGainSettingsRequest(settings.CalculateMissingReplayGainDuringScan)));

        api.MapGet("/profiles", (ServerSettings settings) =>
            Results.Ok(settings.Profiles.Select(static p => new ServerProfileDto(p.Id, p.Name))));

        api.MapPost("/profiles", (ServerProfileRequest request, ServerSettings settings, IWebHostEnvironment env) =>
        {
            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
                return Results.BadRequest(new { error = "A profile name is required." });
            if (settings.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                return Results.Conflict(new { error = "A profile with this name already exists." });

            var profile = new ServerProfile { Id = Guid.NewGuid().ToString("N"), Name = name };
            settings.Profiles.Add(profile);
            PersistProfiles(env.ContentRootPath, settings);
            return Results.Created($"/api/profiles/{profile.Id}", new ServerProfileDto(profile.Id, profile.Name));
        });

        api.MapPut("/profiles/{profileId}", (string profileId, ServerProfileRequest request, ServerSettings settings, IWebHostEnvironment env) =>
        {
            var profile = settings.Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
            var name = request.Name?.Trim() ?? string.Empty;
            if (profile is null)
                return Results.NotFound();
            if (name.Length == 0)
                return Results.BadRequest(new { error = "A profile name is required." });
            if (settings.Profiles.Any(p => !ReferenceEquals(p, profile) && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                return Results.Conflict(new { error = "A profile with this name already exists." });
            profile.Name = name;
            PersistProfiles(env.ContentRootPath, settings);
            return Results.Ok(new ServerProfileDto(profile.Id, profile.Name));
        });

        api.MapDelete("/profiles/{profileId}", (string profileId, ServerSettings settings, IWebHostEnvironment env) =>
        {
            var profile = settings.Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
                return Results.NotFound();
            if (string.Equals(profile.Id, "standard", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "The Standard profile cannot be deleted." });
            settings.Profiles.Remove(profile);
            PersistProfiles(env.ContentRootPath, settings);
            return Results.NoContent();
        });

        api.MapPut(
            "/settings/replaygain",
            (ReplayGainSettingsRequest request, ServerSettings settings, LibraryService libraryService, IWebHostEnvironment env) =>
            {
                libraryService.UpdateReplayGainAnalysis(request.CalculateMissingReplayGainDuringScan);
                PersistReplayGainSettings(env.ContentRootPath, settings);
                return Results.Ok(new ReplayGainSettingsRequest(settings.CalculateMissingReplayGainDuringScan));
            });

        api.MapGet("/files/directories", (string? path) =>
        {
            try
            {
                return Results.Ok(new DirectoryListingDto(
                    path ?? string.Empty,
                    string.IsNullOrWhiteSpace(path),
                    EnumerateDirectories(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static List<string> NormalizePaths(IEnumerable<string>? paths) =>
        (paths ?? [])
        .Where(static path => !string.IsNullOrWhiteSpace(path))
        .Select(static path => path.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static List<DirectoryEntryDto> EnumerateDirectories(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return GetRootDirectories();

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);

        return Directory.EnumerateDirectories(path)
            .Select(CreateDirectoryEntry)
            .OrderBy(static entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<DirectoryEntryDto> GetRootDirectories()
    {
        if (OperatingSystem.IsWindows())
            return Directory.GetLogicalDrives()
                .Select(static drive => new DirectoryEntryDto(drive, drive, HasChildDirectories(drive)))
                .ToList();

        return [new DirectoryEntryDto("/", "/", HasChildDirectories("/"))];
    }

    private static DirectoryEntryDto CreateDirectoryEntry(string path) =>
        new(path, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : path, HasChildDirectories(path));

    private static bool HasChildDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path).Any(); }
        catch { return false; }
    }

    /// <summary>
    /// Resolves the configuration file to persist runtime settings changes to.
    /// Prefers the editable, service-writable <see cref="LinuxConfigFilePath"/>
    /// when its directory exists (Linux packages); otherwise falls back to the
    /// content-root <c>appsettings.json</c> (development and Windows).
    /// </summary>
    /// <param name="contentRootPath">The application content root path.</param>
    /// <returns>The absolute path of the settings file to write.</returns>
    private static string ResolveWritableSettingsPath(string contentRootPath)
    {
        if (!OperatingSystem.IsWindows() &&
            Path.GetDirectoryName(LinuxConfigFilePath) is { } etcDir &&
            Directory.Exists(etcDir))
        {
            return LinuxConfigFilePath;
        }

        return Path.Combine(contentRootPath, "appsettings.json");
    }

    /// <summary>Persists normalized library roots to the server's editable configuration file.</summary>
    /// <param name="contentRootPath">Application content root used outside packaged Linux installations.</param>
    /// <param name="settings">Live server settings whose non-secret values are retained.</param>
    /// <param name="paths">Library root paths to persist.</param>
    internal static void PersistLibraryPaths(string contentRootPath, ServerSettings settings, IReadOnlyList<string> paths)
    {
        var appSettingsPath = ResolveWritableSettingsPath(contentRootPath);
        lock (AppSettingsWriteLock)
        {
            JsonObject root;
            if (File.Exists(appSettingsPath))
            {
                var parsed = JsonNode.Parse(File.ReadAllText(appSettingsPath));
                root = parsed as JsonObject ?? [];
            }
            else
            {
                root = [];
            }

            if (root["Orynivo"] is not JsonObject orynivo)
            {
                orynivo = [];
                root["Orynivo"] = orynivo;
            }

            orynivo["ApiKey"] = settings.ApiKey;
            orynivo["LibraryPaths"] = new JsonArray(paths.Select(static path => JsonValue.Create(path)).ToArray<JsonNode?>());
            orynivo["ScanOnStartup"] = settings.ScanOnStartup;
            orynivo["CalculateMissingReplayGainDuringScan"] = settings.CalculateMissingReplayGainDuringScan;
            orynivo["ServerName"] = settings.ServerName;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(appSettingsPath, root.ToJsonString(options));
        }
    }


    /// <summary>Persists the live ReplayGain scan preference to the editable server configuration.</summary>
    /// <param name="contentRootPath">Application content root used outside packaged Linux installations.</param>
    /// <param name="settings">Live server settings to persist.</param>
    internal static void PersistReplayGainSettings(string contentRootPath, ServerSettings settings)
    {
        var appSettingsPath = ResolveWritableSettingsPath(contentRootPath);
        lock (AppSettingsWriteLock)
        {
            JsonObject root = File.Exists(appSettingsPath)
                ? JsonNode.Parse(File.ReadAllText(appSettingsPath)) as JsonObject ?? []
                : [];
            if (root["Orynivo"] is not JsonObject orynivo)
            {
                orynivo = [];
                root["Orynivo"] = orynivo;
            }

            orynivo["CalculateMissingReplayGainDuringScan"] = settings.CalculateMissingReplayGainDuringScan;
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(appSettingsPath, root.ToJsonString(options));
        }
    }

    /// <summary>Persists the server profile registry without writing secrets to logs.</summary>
    internal static void PersistProfiles(string contentRootPath, ServerSettings settings)
    {
        var appSettingsPath = ResolveWritableSettingsPath(contentRootPath);
        lock (AppSettingsWriteLock)
        {
            JsonObject root = File.Exists(appSettingsPath)
                ? JsonNode.Parse(File.ReadAllText(appSettingsPath)) as JsonObject ?? []
                : [];
            if (root["Orynivo"] is not JsonObject orynivo)
            {
                orynivo = [];
                root["Orynivo"] = orynivo;
            }
            orynivo["Profiles"] = new JsonArray(settings.Profiles
                .Select(p => (JsonNode?)new JsonObject { ["Id"] = p.Id, ["Name"] = p.Name })
                .ToArray());
            File.WriteAllText(appSettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

/// <summary>Request or response payload containing server library root paths.</summary>
/// <param name="Paths">Configured library root directories.</param>
public sealed record LibraryPathsRequest(IReadOnlyList<string> Paths);

/// <summary>Request or response payload containing the server scan-time ReplayGain preference.</summary>
/// <param name="CalculateMissingReplayGainDuringScan">Whether missing values are calculated during server scans.</param>
public sealed record ReplayGainSettingsRequest(bool CalculateMissingReplayGainDuringScan);

/// <summary>Request payload for creating or renaming a server profile.</summary>
public sealed record ServerProfileRequest(string? Name);

/// <summary>Public server profile identity returned to authenticated clients.</summary>
public sealed record ServerProfileDto(string Id, string Name);

/// <summary>Response payload for a browsed server directory.</summary>
/// <param name="Path">Directory path that was listed.</param>
/// <param name="IsRoot">Whether the listing represents filesystem roots.</param>
/// <param name="Directories">Child directories visible to the server process.</param>
public sealed record DirectoryListingDto(string Path, bool IsRoot, IReadOnlyList<DirectoryEntryDto> Directories);

/// <summary>Single directory entry visible on the server filesystem.</summary>
/// <param name="Path">Full server-side directory path.</param>
/// <param name="Name">Display name for the directory.</param>
/// <param name="HasChildren">Whether the directory has at least one visible subdirectory.</param>
public sealed record DirectoryEntryDto(string Path, string Name, bool HasChildren);
