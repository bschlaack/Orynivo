using System.Globalization;
using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>
/// Automatic library backups. The schedule and retention decisions live in the
/// pure <see cref="BackupRetention"/> helper; this partial owns the low-frequency
/// timer, the export call, and the pruning of older archives.
/// </summary>
public partial class MainWindow
{
    private const string ScheduledBackupFilePrefix = "orynivo-backup-";
    private DispatcherTimer? _scheduledBackupTimer;
    private int _scheduledBackupRunning;

    /// <summary>Starts the low-frequency automatic-backup check.</summary>
    private void StartScheduledBackupTimer()
    {
        _scheduledBackupTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _scheduledBackupTimer.Tick -= ScheduledBackupTimer_OnTick;
        _scheduledBackupTimer.Tick += ScheduledBackupTimer_OnTick;
        _scheduledBackupTimer.Start();
        // Give the initial library scan and dashboard work a head start.
        DispatcherTimer.RunOnce(() => _ = RunScheduledBackupIfDueAsync(), TimeSpan.FromMinutes(2));
    }

    private void ScheduledBackupTimer_OnTick(object? sender, EventArgs e) =>
        _ = RunScheduledBackupIfDueAsync();

    /// <summary>
    /// Creates an automatic library backup when the configured interval elapsed,
    /// then removes archives beyond the retention count.
    /// </summary>
    /// <param name="force">Whether to ignore the configured interval.</param>
    /// <returns><see langword="true"/> when a backup archive was written.</returns>
    private async Task<bool> RunScheduledBackupIfDueAsync(bool force = false)
    {
        var schedule = _settings.ScheduledBackup;
        if (!force && !schedule.Enabled)
            return false;
        if (Interlocked.CompareExchange(ref _scheduledBackupRunning, 1, 0) != 0)
            return false;

        try
        {
            var now = DateTimeOffset.Now;
            var lastRun = schedule.LastRunAtUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(schedule.LastRunAtUnix).ToLocalTime()
                : (DateTimeOffset?)null;
            if (!force && !BackupRetention.IsDue(lastRun, schedule.IntervalDays, now))
                return false;

            var directory = schedule.ResolveDirectory();
            Directory.CreateDirectory(directory);
            var target = Path.Combine(
                directory,
                $"{ScheduledBackupFilePrefix}{now:yyyyMMdd-HHmmss}.zip");
            var libraryPaths = (_settings.LibraryPaths ?? []).ToList();
            await LibraryBackupService.ExportAsync(target, libraryPaths).ConfigureAwait(true);
            PruneScheduledBackups(directory, schedule.RetentionCount);
            schedule.LastRunAtUnix = now.ToUnixTimeSeconds();
            await Task.Run(() => _settingsStore.Save(_settings)).ConfigureAwait(true);
            StatusTextBlock.Text = string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Current.ScheduledBackupDone,
                Path.GetFileName(target));
            return true;
        }
        catch (Exception exception)
        {
            CrashLogger.Log(exception, "Scheduled library backup");
            StatusTextBlock.Text = LocalizationManager.Current.ScheduledBackupFailed;
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _scheduledBackupRunning, 0);
        }
    }

    /// <summary>Removes backup archives beyond the configured retention count.</summary>
    /// <param name="directory">Backup folder.</param>
    /// <param name="retentionCount">Number of newest archives to keep.</param>
    private static void PruneScheduledBackups(string directory, int retentionCount)
    {
        var existing = Directory
            .EnumerateFiles(directory, $"{ScheduledBackupFilePrefix}*.zip")
            .Select(path => (
                Path: path,
                CreatedAt: new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)))
            .ToList();
        foreach (var obsolete in BackupRetention.SelectObsolete(existing, retentionCount))
        {
            try
            {
                File.Delete(obsolete);
            }
            catch (IOException)
            {
                // A locked archive is removed by a later run.
            }
        }
    }
}
