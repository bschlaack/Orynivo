using System.Collections.Concurrent;
using System.IO;

namespace Orynivo.Library;

/// <summary>
/// Reports background library scan/index activity so the UI can show a subtle status
/// indicator instead of reloading views.
/// </summary>
/// <param name="Active">Whether a background scan or incremental update is currently running.</param>
/// <param name="Current">Number of files processed so far in the current operation.</param>
/// <param name="Total">Total number of files in the current operation, or <c>0</c> when unknown.</param>
public readonly record struct LibraryScanActivity(bool Active, int Current, int Total);

/// <summary>
/// Watches configured library roots, debounces file-system events, and runs periodic full reconciliation scans.
/// </summary>
public sealed class LibraryWatcherService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan InitialFullScanDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FullScanInterval = TimeSpan.FromMinutes(30);

    private readonly object _sync = new();
    private readonly Action _libraryChanged;
    private readonly Action<LibraryScanActivity>? _activityChanged;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, WatchRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _configuredPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _fullScanTimer;
    private int _fullScanRunning;
    private bool _calculateMissingReplayGain;
    private bool _disposed;

    /// <summary>
    /// Initializes the watcher service.
    /// </summary>
    /// <param name="libraryChanged">Callback invoked after database or index content changes.</param>
    /// <param name="activityChanged">Optional callback invoked when background scan/index activity starts, progresses, or ends.</param>
    /// <param name="calculateMissingReplayGain">Whether scans should calculate ReplayGain values missing from file metadata.</param>
    public LibraryWatcherService(
        Action libraryChanged,
        Action<LibraryScanActivity>? activityChanged = null,
        bool calculateMissingReplayGain = true)
    {
        _libraryChanged = libraryChanged;
        _activityChanged = activityChanged;
        _calculateMissingReplayGain = calculateMissingReplayGain;
        _fullScanTimer = new Timer(
            _ => _ = RunFullReconciliationAsync(),
            null,
            InitialFullScanDelay,
            FullScanInterval);
    }

    /// <summary>Updates whether future watcher and reconciliation scans calculate missing ReplayGain values.</summary>
    /// <param name="calculateMissingReplayGain">Whether FFmpeg analysis should run for missing ReplayGain values.</param>
    public void UpdateReplayGainAnalysis(bool calculateMissingReplayGain)
    {
        lock (_sync)
        {
            if (!_disposed)
                _calculateMissingReplayGain = calculateMissingReplayGain;
        }
    }

    /// <summary>
    /// Replaces the watched root set with the supplied configured library paths.
    /// </summary>
    /// <param name="paths">Configured library root paths.</param>
    public void UpdatePaths(IEnumerable<string> paths)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            try
            {
                normalized.Add(Path.GetFullPath(path));
            }
            catch
            {
                // Invalid legacy settings entries are ignored.
            }
        }

        lock (_sync)
        {
            if (_disposed)
                return;
            _configuredPaths = normalized;

            foreach (var obsolete in _registrations.Keys.Where(path => !normalized.Contains(path)).ToList())
            {
                _registrations[obsolete].Dispose();
                _registrations.Remove(obsolete);
            }

            foreach (var path in normalized)
            {
                if (_registrations.ContainsKey(path) || !Directory.Exists(path))
                    continue;
                try
                {
                    _registrations[path] = new WatchRegistration(
                        path,
                        ProcessPathsAsync,
                        ScheduleFullReconciliation);
                }
                catch
                {
                    ScheduleFullReconciliation();
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _cts.Cancel();
            _fullScanTimer.Dispose();
            foreach (var registration in _registrations.Values)
                registration.Dispose();
            _registrations.Clear();
        }
        _cts.Dispose();
    }

    private async Task ProcessPathsAsync(IReadOnlyCollection<string> paths)
    {
        try
        {
            List<string> configuredPaths;
            lock (_sync)
            {
                if (_disposed)
                    return;
                configuredPaths = paths
                    .Where(IsUnderConfiguredRoot)
                    .ToList();
            }
            if (configuredPaths.Count == 0)
                return;

            RaiseActivity(new LibraryScanActivity(true, 0, configuredPaths.Count));
            try
            {
                bool calculateMissingReplayGain;
                lock (_sync)
                    calculateMissingReplayGain = _calculateMissingReplayGain;
                if (await LibraryScanner.ApplyFileChangesAsync(
                        configuredPaths,
                        calculateMissingReplayGain,
                        _cts.Token).ConfigureAwait(false))
                    _libraryChanged();
            }
            finally
            {
                RaiseActivity(new LibraryScanActivity(false, 0, 0));
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch
        {
            ScheduleFullReconciliation();
        }
    }

    /// <summary>Reports background scan/index activity to the optional activity callback, swallowing UI errors.</summary>
    /// <param name="activity">The activity snapshot to report.</param>
    private void RaiseActivity(LibraryScanActivity activity)
    {
        try { _activityChanged?.Invoke(activity); }
        catch { /* Activity reporting must never affect scanning. */ }
    }

    private bool IsUnderConfiguredRoot(string path) =>
        _configuredPaths.Any(root =>
        {
            var relative = Path.GetRelativePath(root, path);
            return relative != ".." &&
                   !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                   !Path.IsPathRooted(relative);
        });

    private void ScheduleFullReconciliation()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _fullScanTimer.Change(TimeSpan.FromSeconds(5), FullScanInterval);
        }
    }

    private async Task RunFullReconciliationAsync()
    {
        if (Interlocked.Exchange(ref _fullScanRunning, 1) != 0)
            return;

        try
        {
            List<string> roots;
            bool calculateMissingReplayGain;
            lock (_sync)
            {
                if (_disposed)
                    return;
                roots = _configuredPaths.ToList();
                calculateMissingReplayGain = _calculateMissingReplayGain;
            }

            var changed = false;
            var reconciliationRoots = roots.Where(Directory.Exists).ToList();
            var activityStarted = reconciliationRoots.Count > 0;
            if (activityStarted)
                RaiseActivity(new LibraryScanActivity(true, 0, 0));
            var progress = new ActivityProgress(this);
            try
            {
                foreach (var root in reconciliationRoots)
                {
                    try
                    {
                        var result = await LibraryScanner.ScanAsync(
                            root,
                            progress,
                            calculateMissingReplayGain,
                            _cts.Token).ConfigureAwait(false);
                        changed |= result.Added > 0 || result.Updated > 0 || result.Removed > 0;
                    }
                    catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                    {
                        return;
                    }
                    catch
                    {
                        // The next periodic reconciliation retries unavailable roots.
                    }
                }
            }
            finally
            {
                if (activityStarted)
                    RaiseActivity(new LibraryScanActivity(false, 0, 0));
            }

            if (changed)
                _libraryChanged();
            UpdatePaths(roots);
        }
        finally
        {
            Interlocked.Exchange(ref _fullScanRunning, 0);
        }
    }

    /// <summary>Forwards scanner file progress to the watcher's activity callback.</summary>
    private sealed class ActivityProgress : IProgress<ScanProgress>
    {
        private readonly LibraryWatcherService _owner;

        /// <summary>Initializes the progress forwarder.</summary>
        /// <param name="owner">The owning watcher service.</param>
        internal ActivityProgress(LibraryWatcherService owner) => _owner = owner;

        /// <inheritdoc/>
        public void Report(ScanProgress value) =>
            _owner.RaiseActivity(new LibraryScanActivity(true, value.Current, value.Total));
    }

    private sealed class WatchRegistration : IDisposable
    {
        private readonly ConcurrentDictionary<string, byte> _pending =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<IReadOnlyCollection<string>, Task> _processPaths;
        private readonly Action _watcherFailed;
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _debounceTimer;
        private int _processing;
        private volatile bool _disposed;

        internal WatchRegistration(
            string rootPath,
            Func<IReadOnlyCollection<string>, Task> processPaths,
            Action watcherFailed)
        {
            _processPaths = processPaths;
            _watcherFailed = watcherFailed;
            _debounceTimer = new Timer(_ => _ = FlushAsync());
            _watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024
            };
            _watcher.Created += OnPathChanged;
            _watcher.Changed += OnPathChanged;
            _watcher.Deleted += OnPathChanged;
            _watcher.Renamed += OnPathRenamed;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;
        }

        public void Dispose()
        {
            _disposed = true;
            _watcher.Dispose();
            _debounceTimer.Dispose();
            _pending.Clear();
        }

        private void OnPathChanged(object sender, FileSystemEventArgs args) => Enqueue(args.FullPath);

        private void OnPathRenamed(object sender, RenamedEventArgs args)
        {
            Enqueue(args.OldFullPath);
            Enqueue(args.FullPath);
        }

        private void OnWatcherError(object sender, ErrorEventArgs args) => _watcherFailed();

        private void Enqueue(string path)
        {
            if (_disposed)
                return;
            _pending[path] = 0;
            if (string.IsNullOrEmpty(Path.GetExtension(path)))
                _watcherFailed();
            _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }

        private async Task FlushAsync()
        {
            if (_disposed || Interlocked.Exchange(ref _processing, 1) != 0)
                return;

            try
            {
                var paths = _pending.Keys.ToList();
                foreach (var path in paths)
                    _pending.TryRemove(path, out _);
                if (paths.Count > 0)
                    await _processPaths(paths).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _processing, 0);
                if (!_disposed && !_pending.IsEmpty)
                    _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }
}
