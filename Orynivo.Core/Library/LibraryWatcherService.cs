using System.Collections.Concurrent;
using System.IO;

namespace Orynivo.Library;

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
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, WatchRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _configuredPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _fullScanTimer;
    private int _fullScanRunning;
    private bool _disposed;

    /// <summary>
    /// Initializes the watcher service.
    /// </summary>
    /// <param name="libraryChanged">Callback invoked after database or index content changes.</param>
    public LibraryWatcherService(Action libraryChanged)
    {
        _libraryChanged = libraryChanged;
        _fullScanTimer = new Timer(
            _ => _ = RunFullReconciliationAsync(),
            null,
            InitialFullScanDelay,
            FullScanInterval);
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
            if (configuredPaths.Count > 0 &&
                await LibraryScanner.ApplyFileChangesAsync(configuredPaths, _cts.Token).ConfigureAwait(false))
                _libraryChanged();
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch
        {
            ScheduleFullReconciliation();
        }
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
            lock (_sync)
            {
                if (_disposed)
                    return;
                roots = _configuredPaths.ToList();
            }

            var changed = false;
            foreach (var root in roots)
            {
                try
                {
                    if (!Directory.Exists(root))
                        continue;
                    var result = await LibraryScanner.ScanAsync(
                        root,
                        cancellationToken: _cts.Token).ConfigureAwait(false);
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

            if (changed)
                _libraryChanged();
            UpdatePaths(roots);
        }
        finally
        {
            Interlocked.Exchange(ref _fullScanRunning, 0);
        }
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
