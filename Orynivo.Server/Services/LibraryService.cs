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
    private readonly object _progressLock = new();

    private bool _scanning;
    private ServerScanStatus _scanStatus = new(false, null, 0, 0, null, null, null);

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

    /// <summary>Gets the latest scan status snapshot.</summary>
    public ServerScanStatus ScanStatus
    {
        get
        {
            lock (_progressLock)
                return _scanStatus;
        }
    }

    /// <summary>
    /// Replaces the configured library paths and immediately refreshes the file-system watchers.
    /// </summary>
    /// <param name="paths">The new library root directories.</param>
    public void UpdateLibraryPaths(IReadOnlyList<string> paths)
    {
        _settings.LibraryPaths = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _watcher.UpdatePaths(_settings.LibraryPaths);
    }

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
        _scanning = true;
        SetScanStatus(new ServerScanStatus(true, null, 0, 0, null, null, null));
        _ = Task.Run(() => RunScanAsync(cancellationToken), cancellationToken);
        return true;
    }

    private async Task RunScanAsync(CancellationToken cancellationToken)
    {
        if (!await _scanGate.WaitAsync(0))
        {
            _scanning = false;
            SetScanStatus(ScanStatus with { IsRunning = false });
            return;
        }
        _scanning = true;
        SetScanStatus(new ServerScanStatus(true, null, 0, 0, null, null, null));
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
                SetScanStatus(new ServerScanStatus(true, path, 0, 0, null, null, null));
                var progress = new Progress<ScanProgress>(value =>
                    SetScanStatus(new ServerScanStatus(
                        true,
                        path,
                        value.Current,
                        value.Total,
                        value.CurrentFile,
                        null,
                        null)));
                var result = await LibraryScanner.ScanAsync(
                    path,
                    progress: progress,
                    cancellationToken: cancellationToken);
                SetScanStatus(new ServerScanStatus(
                    true,
                    path,
                    result.Total,
                    result.Total,
                    null,
                    result,
                    null));

                _logger.LogInformation(
                    "Scan complete for {Path}: {Total} total, {Added} added, {Updated} updated, {Removed} removed, {Failed} failed",
                    path, result.Total, result.Added, result.Updated, result.Removed, result.Failed);
            }

            _logger.LogInformation("Library scan complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Library scan cancelled");
            SetScanStatus(ScanStatus with { IsRunning = false, Error = "cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library scan failed");
            SetScanStatus(ScanStatus with { IsRunning = false, Error = ex.Message });
        }
        finally
        {
            _scanning = false;
            if (ScanStatus.Error is null)
                SetScanStatus(ScanStatus with { IsRunning = false });
            _scanGate.Release();
        }
    }

    private void SetScanStatus(ServerScanStatus status)
    {
        lock (_progressLock)
            _scanStatus = status;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _scanGate.Dispose();
        _watcher.Dispose();
    }
}

/// <summary>Snapshot of the current or most recent server library scan.</summary>
/// <param name="IsRunning">Whether a scan is currently running.</param>
/// <param name="Path">Library root currently being scanned.</param>
/// <param name="Current">Number of processed files in the current root.</param>
/// <param name="Total">Total files discovered in the current root, or zero while discovery is running.</param>
/// <param name="CurrentFile">File currently being processed, or the root path while discovery is running.</param>
/// <param name="LastResult">Summary of the last completed root scan.</param>
/// <param name="Error">Last scan error, if any.</param>
public sealed record ServerScanStatus(
    bool IsRunning,
    string? Path,
    int Current,
    int Total,
    string? CurrentFile,
    ScanResult? LastResult,
    string? Error);
