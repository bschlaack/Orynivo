using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the pure scheduling and retention decisions behind automatic
/// library backups.
/// </summary>
public sealed class BackupRetentionTests
{
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Fewer backups than the retention count are all kept.</summary>
    [Fact]
    public void SelectObsolete_BelowRetention_KeepsEverything()
    {
        var backups = new[]
        {
            ("a.zip", Origin),
            ("b.zip", Origin.AddDays(1))
        };

        Assert.Empty(BackupRetention.SelectObsolete(backups, 3));
        Assert.Empty(BackupRetention.SelectObsolete(backups, 2));
    }

    /// <summary>Backups beyond the retention count are removed oldest first.</summary>
    [Fact]
    public void SelectObsolete_AboveRetention_ReturnsOldestFirst()
    {
        var backups = new[]
        {
            ("newest.zip", Origin.AddDays(4)),
            ("oldest.zip", Origin),
            ("middle.zip", Origin.AddDays(2))
        };

        var obsolete = BackupRetention.SelectObsolete(backups, 1);

        Assert.Equal(["oldest.zip", "middle.zip"], obsolete);
    }

    /// <summary>A retention count below one still keeps a single backup.</summary>
    [Fact]
    public void SelectObsolete_NonPositiveRetention_KeepsOne()
    {
        var backups = new[]
        {
            ("old.zip", Origin),
            ("new.zip", Origin.AddDays(1))
        };

        Assert.Equal(["old.zip"], BackupRetention.SelectObsolete(backups, 0));
        Assert.Equal(["old.zip"], BackupRetention.SelectObsolete(backups, -5));
    }

    /// <summary>Identical timestamps fall back to a stable path order.</summary>
    [Fact]
    public void SelectObsolete_IdenticalTimestamps_AreDeterministic()
    {
        var backups = new[]
        {
            ("b.zip", Origin),
            ("a.zip", Origin),
            ("c.zip", Origin)
        };

        var first = BackupRetention.SelectObsolete(backups, 1);
        var second = BackupRetention.SelectObsolete(backups, 1);

        Assert.Equal(["a.zip", "b.zip"], first);
        Assert.Equal(first, second);
    }

    /// <summary>A missing last run is always due.</summary>
    [Fact]
    public void IsDue_WithoutLastRun_IsDue()
    {
        Assert.True(BackupRetention.IsDue(null, 7, Origin));
    }

    /// <summary>The interval is the minimum time between backups.</summary>
    [Fact]
    public void IsDue_UsesTheConfiguredInterval()
    {
        var lastRun = Origin;

        Assert.False(BackupRetention.IsDue(lastRun, 7, Origin.AddDays(6)));
        Assert.True(BackupRetention.IsDue(lastRun, 7, Origin.AddDays(7)));
        Assert.True(BackupRetention.IsDue(lastRun, 7, Origin.AddDays(30)));
    }

    /// <summary>An interval below one day still waits a day.</summary>
    [Fact]
    public void IsDue_NonPositiveInterval_WaitsADay()
    {
        var lastRun = Origin;

        Assert.False(BackupRetention.IsDue(lastRun, 0, Origin.AddHours(23)));
        Assert.True(BackupRetention.IsDue(lastRun, 0, Origin.AddDays(1)));
    }

    /// <summary>A clock that moved backwards never triggers an immediate backup.</summary>
    [Fact]
    public void IsDue_ClockMovedBackwards_IsNotDue()
    {
        Assert.False(BackupRetention.IsDue(Origin.AddDays(3), 7, Origin));
    }
}
