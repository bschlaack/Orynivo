using Orynivo.Library;

namespace Orynivo.Server.Services;

/// <summary>
/// Runs the optional automatic server-side library backup. The due check and the
/// retention selection come from the shared, tested <see cref="BackupRetention"/>
/// helper, and the last run is derived from the newest archive in the target
/// folder, so no additional state is persisted. It holds no credentials.
/// </summary>
public sealed class BackupScheduleService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

    private readonly ServerSettings _settings;
    private readonly ILogger<BackupScheduleService> _logger;

    /// <summary>Initializes the schedule service.</summary>
    /// <param name="settings">Server configuration holding the schedule.</param>
    /// <param name="logger">Logger for backup results.</param>
    public BackupScheduleService(ServerSettings settings, ILogger<BackupScheduleService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunIfDueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Scheduled library backup failed");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Creates a library backup when the configured interval elapsed and removes
    /// archives beyond the retention count.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when an archive was written.</returns>
    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        var schedule = _settings.BackupSchedule;
        if (schedule is not { Enabled: true })
            return false;

        var directory = ResolveDirectory(schedule);
        if (!BackupRetention.IsDue(GetNewestBackup(directory), schedule.IntervalDays, DateTimeOffset.UtcNow))
            return false;

        System.IO.Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, BackupNaming.BuildFileName(DateTimeOffset.UtcNow));
        await LibraryBackupService.ExportAsync(
            target,
            _settings.LibraryPaths ?? [],
            AppPaths.DataRoot,
            progress: null,
            cancellationToken).ConfigureAwait(false);
        Prune(directory, schedule.RetentionCount);
        _logger.LogInformation("Scheduled library backup written to {File}", Path.GetFileName(target));
        return true;
    }

    /// <summary>Resolves the configured backup folder.</summary>
    /// <param name="schedule">Schedule configuration.</param>
    /// <returns>The absolute backup folder path.</returns>
    public static string ResolveDirectory(BackupScheduleSettings schedule) =>
        string.IsNullOrWhiteSpace(schedule.Directory)
            ? Path.Combine(AppPaths.DataRoot, "backups")
            : schedule.Directory.Trim();

    /// <summary>Returns the creation timestamp of the newest backup archive.</summary>
    /// <param name="directory">Backup folder.</param>
    /// <returns>The newest timestamp, or <see langword="null"/> when no archive exists.</returns>
    private static DateTimeOffset? GetNewestBackup(string directory)
    {
        DateTimeOffset? newest = null;
        foreach (var (_, createdAt) in EnumerateBackups(directory))
        {
            if (newest is null || createdAt > newest.Value)
                newest = createdAt;
        }
        return newest;
    }

    private static void Prune(string directory, int retentionCount)
    {
        var backups = EnumerateBackups(directory).ToList();
        foreach (var name in BackupRetention.SelectObsolete(backups, retentionCount))
        {
            try
            {
                File.Delete(Path.Combine(directory, name));
            }
            catch (IOException)
            {
                // A locked archive is removed by a later run.
            }
        }
    }

    private static IEnumerable<(string Name, DateTimeOffset CreatedAt)> EnumerateBackups(string directory)
    {
        if (!System.IO.Directory.Exists(directory))
            yield break;

        foreach (var path in System.IO.Directory.EnumerateFiles(directory, $"{BackupNaming.FilePrefix}*.zip"))
        {
            if (BackupNaming.TryParseTimestamp(path, out var createdAt))
                yield return (Path.GetFileName(path), createdAt);
        }
    }
}
