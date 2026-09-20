using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the shared naming of automatic backup archives used by the desktop and
/// the server schedule.
/// </summary>
public sealed class BackupNamingTests
{
    /// <summary>A built name parses back to the same timestamp.</summary>
    [Fact]
    public void BuildFileName_RoundTripsThroughTryParseTimestamp()
    {
        var timestamp = new DateTimeOffset(2026, 9, 20, 12, 30, 45, TimeSpan.Zero);

        var name = BackupNaming.BuildFileName(timestamp);

        Assert.Equal("orynivo-backup-20260920-123045.zip", name);
        Assert.True(BackupNaming.TryParseTimestamp(name, out var parsed));
        Assert.Equal(timestamp, parsed);
    }

    /// <summary>A full path is accepted and only its file name is inspected.</summary>
    [Fact]
    public void TryParseTimestamp_AcceptsAPath()
    {
        var name = BackupNaming.BuildFileName(DateTimeOffset.UtcNow);

        Assert.True(BackupNaming.TryParseTimestamp(Path.Combine("/var/lib/orynivo", name), out _));
    }

    /// <summary>Foreign file names are rejected so user files are never pruned.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("library.zip")]
    [InlineData("orynivo-backup-manual.zip")]
    [InlineData("orynivo-backup-2026-09-20.zip")]
    [InlineData("orynivo-backup-20260920-123000.tar")]
    public void TryParseTimestamp_RejectsForeignNames(string? fileName)
    {
        Assert.False(BackupNaming.TryParseTimestamp(fileName, out _));
    }
}
