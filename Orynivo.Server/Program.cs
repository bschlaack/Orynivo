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

var settings = builder.Configuration
    .GetSection("Orynivo")
    .Get<ServerSettings>() ?? new ServerSettings();

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
    new LibraryWatcherService(services.GetRequiredService<ServerLibraryChangeTracker>().Touch));

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

// Scan status
app.MapGet("/api/scan", (LibraryService svc) =>
    Results.Ok(svc.ScanStatus));

app.MapLibraryEndpoints();
app.MapStreamEndpoints();
app.MapConfigurationEndpoints();
app.MapUpdateEndpoints(settings);

// ---- Start ----------------------------------------------------------------
var addr = builder.Configuration["Kestrel:Endpoints:Http:Url"] ?? "http://0.0.0.0:5280";
app.Logger.LogInformation("Orynivo Server starting on {Address}", addr);
app.Logger.LogInformation("Server name: {Name}", settings.ServerName);
if (string.IsNullOrEmpty(settings.ApiKey) || settings.ApiKey == "change-this-to-a-long-random-string")
    app.Logger.LogWarning("API key is not configured. Set Orynivo:ApiKey in appsettings.json before exposing this server on the network.");

await app.RunAsync();
