using Microsoft.Extensions.Logging;
using Orynivo.Library;

namespace Orynivo.Server.Services;

/// <summary>
/// Hosted service that owns the file-system watcher and runs library scans.
/// Triggers an initial full scan on startup when configured and keeps
/// both the SQLite database and Lucene index in sync via <see cref="LibraryWatcherService"/>.
/// </summary>
public sealed class LibraryService : IHostedService, IDisposable
{
    private readonly ILogger<LibraryService> _logger;
    private readonly ServerSettings _settings;
    private readonly LibraryWatcherService _watcher;
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    private bool _scanning;

    /// <summary>Initialises the library service with injected dependencies.</summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="settings">Server configuration containing library paths and startup options.</param>
    /// <param name="watcher">File-system watcher that keeps the library in sync.</param>
    public LibraryService(
        ILogger<LibraryService> logger,
        ServerSettings settings,
        LibraryWatcherService watcher)
    {
        _logger = logger;
        _settings = settings;
        _watcher = watcher;
    }

    /// <summary>Gets a value indicating whether a library scan is currently running.</summary>
    public bool IsScanning => _scanning;

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _watcher.UpdatePaths(_settings.LibraryPaths);

        if (_settings.ScanOnStartup && _settings.LibraryPaths.Count > 0)
            _ = Task.Run(() => RunScanAsync(cancellationToken), cancellationToken);

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Triggers a full library scan of all configured paths in the background,
    /// unless a scan is already in progress.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when a new scan was started;
    /// <see langword="false"/> when a scan was already running.
    /// </returns>
    public bool TriggerScan(CancellationToken cancellationToken = default)
    {
        if (_scanning) return false;
        _ = Task.Run(() => RunScanAsync(cancellationToken), cancellationToken);
        return true;
    }

    private async Task RunScanAsync(CancellationToken cancellationToken)
    {
        if (!await _scanGate.WaitAsync(0, cancellationToken)) return;
        _scanning = true;
        try
        {
            _logger.LogInformation("Library scan started ({Count} paths)", _settings.LibraryPaths.Count);

            foreach (var path in _settings.LibraryPaths)
            {
                if (!Directory.Exists(path))
                {
                    _logger.LogWarning("Library path not found, skipping: {Path}", path);
                    continue;
                }

                _logger.LogInformation("Scanning: {Path}", path);
                var result = await LibraryScanner.ScanAsync(
                    path,
                    progress: null,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Scan complete for {Path}: {Total} total, {Added} added, {Updated} updated, {Removed} removed, {Failed} failed",
                    path, result.Total, result.Added, result.Updated, result.Removed, result.Failed);
            }

            _logger.LogInformation("Library scan complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Library scan cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library scan failed");
        }
        finally
        {
            _scanning = false;
            _scanGate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _scanGate.Dispose();
        _watcher.Dispose();
    }
}
