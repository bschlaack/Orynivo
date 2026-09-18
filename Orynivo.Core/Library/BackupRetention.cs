namespace Orynivo.Library;

/// <summary>
/// Pure scheduling and retention decisions for automatic library backups. The
/// decisions are deterministic so they can be verified without touching disk.
/// </summary>
public static class BackupRetention
{
    /// <summary>
    /// Selects the existing backups that exceed the retention count. The newest
    /// backups are kept and the remaining ones are returned oldest first so the
    /// caller can delete them deterministically.
    /// </summary>
    /// <param name="backups">Existing backups with their creation timestamps.</param>
    /// <param name="retentionCount">Number of newest backups to keep; values below one keep a single backup.</param>
    /// <returns>The paths to remove, oldest first.</returns>
    public static IReadOnlyList<string> SelectObsolete(
        IReadOnlyList<(string Path, DateTimeOffset CreatedAt)> backups,
        int retentionCount)
    {
        ArgumentNullException.ThrowIfNull(backups);
        retentionCount = Math.Max(1, retentionCount);
        if (backups.Count <= retentionCount)
            return [];

        return backups
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Path, StringComparer.Ordinal)
            .Skip(retentionCount)
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(entry => entry.Path)
            .ToList();
    }

    /// <summary>
    /// Indicates whether a scheduled backup is due.
    /// </summary>
    /// <param name="lastRun">Timestamp of the last successful backup, or <see langword="null"/> when none ran yet.</param>
    /// <param name="intervalDays">Minimum number of days between backups; values below one run daily.</param>
    /// <param name="now">Current timestamp.</param>
    /// <returns><see langword="true"/> when a backup should run.</returns>
    public static bool IsDue(DateTimeOffset? lastRun, int intervalDays, DateTimeOffset now)
    {
        if (lastRun is null)
            return true;
        var interval = TimeSpan.FromDays(Math.Max(1, intervalDays));
        return now - lastRun.Value >= interval;
    }
}
