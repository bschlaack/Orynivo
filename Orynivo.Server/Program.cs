using Orynivo.Audio;
using Orynivo.Library;
using Orynivo.Server;
using Orynivo.Server.Endpoints;
using Orynivo.Server.Middleware;
using Orynivo.Server.Services;
using System.Reflection;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration --------------------------------------------------------
// On Linux the editable configuration is installed to /etc/orynivo-server by the
// DEB/RPM package, while the service runs from /usr/lib/orynivo-server. Layer the
// /etc file on top of the bundled defaults so edits there take effect. The file
// is optional and absent on Windows, so this is a no-op there.
builder.Configuration.AddJsonFile(
    ConfigurationEndpoints.LinuxConfigFilePath, optional: true, reloadOnChange: true);

// WebApplication.CreateBuilder wires Kestrel before the editable Linux file is
// added above. Reapply the configured global body-size limit explicitly so a
// value from /etc/orynivo-server is honoured as well as bundled/environment
// configuration. The update-package route still owns its narrower streaming
// safety bound independently.
var configuredMaxRequestBodySize = builder.Configuration
    .GetValue<long?>("Kestrel:Limits:MaxRequestBodySize");
if (configuredMaxRequestBodySize.HasValue)
{
    builder.WebHost.ConfigureKestrel(options =>
        options.Limits.MaxRequestBodySize = configuredMaxRequestBodySize.Value);
}

var settings = builder.Configuration
    .GetSection("Orynivo")
    .Get<ServerSettings>() ?? new ServerSettings();

// Keep older configuration files valid and guarantee a stable migration target.
settings.Profiles ??= [];
if (settings.Profiles.Count == 0)
    settings.Profiles.Add(new ServerProfile());
if (!settings.Profiles.Any(p => string.Equals(p.Id, "standard", StringComparison.OrdinalIgnoreCase)))
    settings.Profiles.Insert(0, new ServerProfile());
settings.Profiles = settings.Profiles
    .Where(p => !string.IsNullOrWhiteSpace(p.Id))
    .Select(p => { p.Id = p.Id.Trim(); p.Name = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name.Trim(); return p; })
    .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
    .Select(g => g.First())
    .ToList();

LibraryScanner.ConfigureReplayGainThrottling(
    settings.ReplayGainFfmpegThreads,
    settings.ReplayGainDelayMilliseconds);

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<ServerLibraryChangeTracker>();

// ---- Library services -----------------------------------------------------
// FfmpegLocator is cross-platform: auto-downloads FFmpeg on Windows,
// expects it to be installed on Linux/macOS.
await FfmpegLocator.EnsureAvailableAsync();

// Ensure the data directory exists and the database schema is current.
// AudioDatabase.OpenDefault() runs migrations in its constructor.
using (var db = AudioDatabase.OpenDefault())
    _ = db; // migrations run in constructor; nothing else needed here

// The file-system watcher notifies LibraryService via the hosted-service start path.
builder.Services.AddSingleton(static services =>
    new LibraryWatcherService(
        services.GetRequiredService<ServerLibraryChangeTracker>().Touch,
        calculateMissingReplayGain: services
            .GetRequiredService<ServerSettings>()
            .CalculateMissingReplayGainDuringScan));

builder.Services.AddSingleton<LibraryService>();
builder.Services.AddHostedService(static services => services.GetRequiredService<LibraryService>());

// ---- ASP.NET Core infrastructure ------------------------------------------
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

builder.Services.AddProblemDetails();

// ---- Build app ------------------------------------------------------------
var app = builder.Build();
var serverAssembly = typeof(Program).Assembly;
var serverVersion = serverAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? serverAssembly.GetName().Version?.ToString(3) ?? "0.0.0";
var installTypePath = Path.Combine(AppContext.BaseDirectory, "install-type");
var installType = File.Exists(installTypePath)
    ? File.ReadAllText(installTypePath).Trim().ToLowerInvariant()
    : "portable";
var updateSupported = OperatingSystem.IsLinux() && installType is "deb" or "rpm";

app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<ProfileContextMiddleware>();
app.UseCors();

// ---- Endpoints ------------------------------------------------------------

// Health — no authentication required
app.MapGet("/api/health", () => Results.Ok(new
{
    Status  = "ok",
    Server  = settings.ServerName,
    Version = serverVersion,
    Time    = DateTimeOffset.UtcNow
}));

// Server info — authenticated
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

// Library scan trigger
app.MapPost("/api/scan", (LibraryService svc) =>
{
    var started = svc.TriggerScan();
    return started
        ? Results.Accepted("/api/scan", new { Status = "started" })
        : Results.Ok(new { Status = "already_running" });
});

// Explicit metadata refresh; kept separate so older servers reject the unsupported operation.
app.MapPost("/api/scan/metadata", (LibraryService svc) =>
{
    var started = svc.TriggerScan(forceMetadataRefresh: true);
    return started
        ? Results.Accepted("/api/scan/metadata", new { Status = "started" })
        : Results.Ok(new { Status = "already_running" });
});

// Scan status
app.MapGet("/api/scan", (LibraryService svc) =>
    Results.Ok(svc.ScanStatus));

// Explicit ReplayGain maintenance; existing values are preserved.
app.MapPost("/api/replaygain", (LibraryService svc) =>
{
    var started = svc.TriggerReplayGainCalculation();
    return started
        ? Results.Accepted("/api/replaygain", new { Status = "started" })
        : Results.Conflict(new { Status = "already_running" });
});

app.MapLibraryEndpoints();
app.MapStreamEndpoints();
app.MapConfigurationEndpoints();
app.MapBackupEndpoints();
app.MapUpdateEndpoints(settings);

// ---- Start ----------------------------------------------------------------
var addr = builder.Configuration["Kestrel:Endpoints:Http:Url"] ?? "http://0.0.0.0:5280";
app.Logger.LogInformation("Orynivo Server starting on {Address}", addr);
app.Logger.LogInformation("Server name: {Name}", settings.ServerName);
if (string.IsNullOrEmpty(settings.ApiKey) || settings.ApiKey == "change-this-to-a-long-random-string")
    app.Logger.LogWarning("API key is not configured. Set Orynivo:ApiKey in appsettings.json before exposing this server on the network.");

await app.RunAsync();
