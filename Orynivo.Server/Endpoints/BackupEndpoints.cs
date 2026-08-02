using Microsoft.AspNetCore.Http.Features;
using Orynivo.Library;
using Orynivo.Server.Services;
using System.IO.Compression;

namespace Orynivo.Server.Endpoints;

/// <summary>Maps authenticated download and restore endpoints for server library backups.</summary>
public static class BackupEndpoints
{
    private const long MaximumBackupBytes = 2L * 1024 * 1024 * 1024;
    private const long MaximumExtractedBytes = 8L * 1024 * 1024 * 1024;
    private const int MaximumArchiveEntries = 200_000;

    /// <summary>Registers the server library backup routes.</summary>
    /// <param name="app">Endpoint route builder.</param>
    public static void MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/library/backup");

        api.MapGet("/", async (LibraryService libraryService, CancellationToken cancellationToken) =>
        {
            if (libraryService.IsScanning)
                return Results.Conflict(new { error = "A library scan is currently running." });

            var archivePath = CreateTemporaryArchivePath();
            try
            {
                await libraryService.ExportBackupAsync(archivePath, cancellationToken);
                var downloadName = $"orynivo-server-library-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
                return Results.Stream(
                    async output =>
                    {
                        try
                        {
                            await using var input = new FileStream(
                                archivePath,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read,
                                128 * 1024,
                                FileOptions.Asynchronous | FileOptions.SequentialScan);
                            await input.CopyToAsync(output, cancellationToken);
                        }
                        finally
                        {
                            TryDelete(archivePath);
                        }
                    },
                    "application/zip",
                    downloadName);
            }
            catch
            {
                TryDelete(archivePath);
                throw;
            }
        });

        api.MapPut("/", async (
            HttpContext context,
            LibraryService libraryService,
            ServerSettings settings,
            IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            if (libraryService.IsScanning)
                return Results.Conflict(new { error = "A library scan is currently running." });

            var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false })
                sizeFeature.MaxRequestBodySize = MaximumBackupBytes;
            if (context.Request.ContentLength is > MaximumBackupBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var archivePath = CreateTemporaryArchivePath();
            try
            {
                await CopyBoundedAsync(context.Request.Body, archivePath, cancellationToken);
                ValidateArchiveLimits(archivePath);
                var paths = await libraryService.ImportBackupAsync(archivePath, cancellationToken);
                ConfigurationEndpoints.PersistLibraryPaths(
                    environment.ContentRootPath,
                    settings,
                    paths);
                return Results.Ok(new LibraryPathsRequest(paths));
            }
            catch (InvalidDataException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            finally
            {
                TryDelete(archivePath);
            }
        });
    }

    private static string CreateTemporaryArchivePath()
    {
        var directory = AppPaths.GetDataPath("backup-transfer");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.zip");
    }

    private static async Task CopyBoundedAsync(
        Stream input,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[128 * 1024];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += count;
            if (total > MaximumBackupBytes)
                throw new InvalidDataException("The uploaded backup exceeds the supported size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    private static void ValidateArchiveLimits(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumArchiveEntries)
            throw new InvalidDataException("The backup contains too many files.");

        long extractedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaximumExtractedBytes - extractedBytes)
                throw new InvalidDataException("The extracted backup exceeds the supported size limit.");
            extractedBytes += entry.Length;

            var name = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (name is "manifest.json" or "library.db" ||
                name.StartsWith("artworks/", StringComparison.Ordinal) ||
                name.StartsWith("artist-images/", StringComparison.Ordinal))
            {
                continue;
            }

            throw new InvalidDataException("The backup contains an unsupported entry.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temporary transfer cleanup is best effort.
        }
    }
}
