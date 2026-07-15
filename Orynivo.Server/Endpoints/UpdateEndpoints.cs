using Microsoft.AspNetCore.Http.Features;
using Orynivo.Updates;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Orynivo.Server.Endpoints;

/// <summary>Maps staging and application endpoints for signed server package updates.</summary>
internal static class UpdateEndpoints
{
    private const long MaximumBundleBytes = 1_073_741_824;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Adds authenticated server-update endpoints to the application.</summary>
    /// <param name="app">Server application.</param>
    /// <param name="settings">Bound server settings.</param>
    internal static void MapUpdateEndpoints(this WebApplication app, ServerSettings settings)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals("/api/update/package", StringComparison.OrdinalIgnoreCase))
            {
                var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is { IsReadOnly: false })
                    sizeFeature.MaxRequestBodySize = MaximumBundleBytes;
            }
            await next(context);
        });

        app.MapGet("/api/update/status", () =>
        {
            var paths = GetPaths();
            var installType = ReadInstallType();
            return Results.Ok(new
            {
                Enabled = settings.AllowRemoteUpdates,
                Supported = OperatingSystem.IsLinux() && installType is "deb" or "rpm",
                InstallType = installType,
                Architecture = NormalizeArchitecture(RuntimeInformation.ProcessArchitecture),
                Staged = File.Exists(paths.Manifest),
                Ready = File.Exists(paths.Ready),
                Status = File.Exists(paths.Status) ? File.ReadAllText(paths.Status).Trim() : null
            });
        });

        app.MapPost("/api/update/package", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            if (!settings.AllowRemoteUpdates)
                return Results.Problem("Remote server updates are disabled.", statusCode: StatusCodes.Status403Forbidden);
            if (!OperatingSystem.IsLinux() || ReadInstallType() is not ("deb" or "rpm"))
                return Results.Problem("This server installation cannot apply managed updates.", statusCode: StatusCodes.Status409Conflict);
            if (request.ContentLength is > MaximumBundleBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var paths = GetPaths();
            Directory.CreateDirectory(paths.Root);
            var bundlePath = Path.Combine(paths.Root, $"incoming-{Guid.NewGuid():N}.zip");
            try
            {
                await using (var output = new FileStream(bundlePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await CopyBoundedAsync(request.Body, output, MaximumBundleBytes, cancellationToken);
                var stagedVersion = await ValidateAndStageAsync(bundlePath, paths, cancellationToken);
                return Results.Ok(new { Status = "staged", Version = stagedVersion });
            }
            catch (InvalidDataException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            finally
            {
                File.Delete(bundlePath);
            }
        }).DisableAntiforgery();

        app.MapPost("/api/update/apply", () =>
        {
            if (!settings.AllowRemoteUpdates)
                return Results.Problem("Remote server updates are disabled.", statusCode: StatusCodes.Status403Forbidden);
            var paths = GetPaths();
            if (!File.Exists(paths.Manifest) || !File.Exists(paths.Signature) || !File.Exists(paths.PackageName))
                return Results.Problem("No verified update is staged.", statusCode: StatusCodes.Status409Conflict);
            File.WriteAllText(paths.Ready, DateTimeOffset.UtcNow.ToString("O"));
            return Results.Accepted("/api/update/status", new { Status = "queued" });
        });
    }

    private static async Task<string> ValidateAndStageAsync(string bundlePath, UpdatePaths paths, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(bundlePath);
        var manifestEntry = RequireEntry(archive, "update-manifest.json");
        var signatureEntry = RequireEntry(archive, "update-manifest.sig");
        var packageEntries = archive.Entries.Where(entry => entry.Name != "update-manifest.json" && entry.Name != "update-manifest.sig").ToList();
        if (packageEntries.Count != 1 || packageEntries[0].FullName != packageEntries[0].Name)
            throw new InvalidDataException("The update bundle must contain exactly one package.");

        var manifestBytes = await ReadEntryAsync(manifestEntry, 1_048_576, cancellationToken);
        var signatureBytes = await ReadEntryAsync(signatureEntry, 16_384, cancellationToken);
        VerifyManifestSignature(manifestBytes, signatureBytes);
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("The update manifest is invalid.");
        var installType = ReadInstallType();
        var architecture = NormalizeArchitecture(RuntimeInformation.ProcessArchitecture);
        var packageEntry = packageEntries[0];
        var asset = manifest.Assets.SingleOrDefault(candidate =>
            candidate.Component == "server" && candidate.OperatingSystem == "linux" &&
            candidate.Architecture == architecture && candidate.Type == installType &&
            candidate.File == packageEntry.Name)
            ?? throw new InvalidDataException("The package does not match this server installation.");
        var currentVersion = typeof(UpdateEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        if (!ReleaseUpdateService.IsNewer(currentVersion, manifest.Version))
            throw new InvalidDataException("The package is not newer than the installed server.");

        var packageTemp = Path.Combine(paths.Root, $"package-{Guid.NewGuid():N}.tmp");
        await using (var source = packageEntry.Open())
        await using (var target = new FileStream(packageTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await CopyBoundedAsync(source, target, MaximumBundleBytes, cancellationToken);
        await using (var packageStream = File.OpenRead(packageTemp))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(hash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(packageTemp);
                throw new InvalidDataException("The package checksum is invalid.");
            }
        }

        File.Move(packageTemp, paths.Package, true);
        await File.WriteAllBytesAsync(paths.Manifest, manifestBytes, cancellationToken);
        await File.WriteAllBytesAsync(paths.Signature, signatureBytes, cancellationToken);
        await File.WriteAllTextAsync(paths.PackageName, asset.File, cancellationToken);
        File.Delete(paths.Ready);
        return manifest.Version;
    }

    private static void VerifyManifestSignature(byte[] manifest, byte[] signature)
    {
        var key = typeof(UpdateEndpoints).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "OrynivoUpdatePublicKey")?.Value;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidDataException("Update signature verification is not configured.");
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
        if (!ecdsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            throw new InvalidDataException("The update manifest signature is invalid.");
    }

    private static ZipArchiveEntry RequireEntry(ZipArchive archive, string name)
        => archive.Entries.SingleOrDefault(entry => entry.FullName == name)
           ?? throw new InvalidDataException($"The update bundle is missing {name}.");

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, int maximumBytes, CancellationToken cancellationToken)
    {
        if (entry.Length > maximumBytes)
            throw new InvalidDataException("An update metadata entry is too large.");
        await using var stream = entry.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private static async Task CopyBoundedAsync(Stream source, Stream target, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81_920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maximumBytes)
                throw new InvalidDataException("The update bundle is too large.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string ReadInstallType()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "install-type");
        return File.Exists(path) ? File.ReadAllText(path).Trim().ToLowerInvariant() : "portable";
    }

    private static string NormalizeArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        _ => architecture.ToString().ToLowerInvariant()
    };

    private static UpdatePaths GetPaths()
    {
        var root = AppPaths.GetDataPath("updates");
        return new UpdatePaths(root, Path.Combine(root, "update-manifest.json"), Path.Combine(root, "update-manifest.sig"),
            Path.Combine(root, "package.bin"), Path.Combine(root, "package-name"), Path.Combine(root, "ready"), Path.Combine(root, "status"));
    }

    private sealed record UpdatePaths(string Root, string Manifest, string Signature, string Package, string PackageName, string Ready, string Status);
}
