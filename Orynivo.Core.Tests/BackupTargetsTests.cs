using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies WebDAV backup target validation and URL building.</summary>
public sealed class BackupTargetsTests
{
    /// <summary>Only absolute HTTP(S) URLs without user information are accepted.</summary>
    [Theory]
    [InlineData("https://cloud.example.com/dav/backups", true)]
    [InlineData("http://192.168.1.10/dav", true)]
    [InlineData("ftp://cloud.example.com/dav", false)]
    [InlineData("file:///C:/backups", false)]
    [InlineData("cloud.example.com/dav", false)]
    [InlineData("https://user:secret@cloud.example.com/dav", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedTargetUrl_ValidatesSchemeAndCredentials(string? url, bool expected)
    {
        Assert.Equal(expected, BackupTargets.IsSupportedTargetUrl(url));
    }

    /// <summary>The remote directory and file name are joined and escaped.</summary>
    [Theory]
    [InlineData("https://cloud.example.com/dav", null, "https://cloud.example.com/dav/orynivo-backup-1.zip")]
    [InlineData("https://cloud.example.com/dav/", "", "https://cloud.example.com/dav/orynivo-backup-1.zip")]
    [InlineData("https://cloud.example.com/dav", "music/2026", "https://cloud.example.com/dav/music/2026/orynivo-backup-1.zip")]
    [InlineData("https://cloud.example.com/dav", "/music/", "https://cloud.example.com/dav/music/orynivo-backup-1.zip")]
    [InlineData("https://cloud.example.com/dav", "my backups", "https://cloud.example.com/dav/my%20backups/orynivo-backup-1.zip")]
    public void BuildTargetUrl_JoinsBaseDirectoryAndFileName(string baseUrl, string? directory, string expected)
    {
        Assert.Equal(expected, BackupTargets.BuildTargetUrl(baseUrl, directory, "orynivo-backup-1.zip"));
    }

    /// <summary>An unusable base URL or empty file name is rejected.</summary>
    [Fact]
    public void BuildTargetUrl_RejectsUnusableInput()
    {
        Assert.Throws<ArgumentException>(() => BackupTargets.BuildTargetUrl("not a url", null, "a.zip"));
        Assert.Throws<ArgumentException>(() =>
            BackupTargets.BuildTargetUrl("https://cloud.example.com/dav", null, " "));
    }
}
