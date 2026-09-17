using System.Text;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies M3U8 import and export, including relative local-path resolution and
/// rejection of credential-bearing URLs.
/// </summary>
public sealed class M3u8PlaylistServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "orynivo-m3u8-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temporary working directory.</summary>
    public M3u8PlaylistServiceTests() => Directory.CreateDirectory(_directory);

    /// <summary>Removes the temporary working directory.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    /// <summary>Export writes the header, relative local paths, and skips credential URLs.</summary>
    [Fact]
    public async Task Export_WritesEntriesAndSkipsCredentials()
    {
        var localFile = Path.Combine(_directory, "track.flac");
        await File.WriteAllTextAsync(localFile, "x");
        var destination = Path.Combine(_directory, "out.m3u8");

        var result = await M3u8PlaylistService.ExportAsync(destination, [
            localFile,
            "https://example.com/stream/1",
            "https://user:secret@example.com/stream/2",
            "https://example.com/stream/3?X-Plex-Token=abc"
        ]);

        Assert.Equal(2, result.ExportedEntries);
        Assert.Equal(2, result.SkippedCredentialUrls);

        var lines = await File.ReadAllLinesAsync(destination, Encoding.UTF8);
        Assert.Equal("#EXTM3U", lines[0]);
        Assert.Contains("track.flac", lines[1]);
        Assert.Equal("https://example.com/stream/1", lines[2]);
        Assert.DoesNotContain(lines, line => line.Contains("secret", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("X-Plex-Token", StringComparison.Ordinal));
    }

    /// <summary>Import resolves relative paths, keeps remote URLs, and skips credentials.</summary>
    [Fact]
    public async Task Import_ResolvesPathsAndSkipsCredentials()
    {
        var existing = Path.Combine(_directory, "present.flac");
        await File.WriteAllTextAsync(existing, "x");

        var source = Path.Combine(_directory, "in.m3u8");
        await File.WriteAllLinesAsync(source, [
            "#EXTM3U",
            "present.flac",
            "missing.flac",
            "https://example.com/stream/1",
            "https://example.com/stream/2?X-Plex-Token=abc"
        ], new UTF8Encoding(false));

        var result = await M3u8PlaylistService.ImportAsync(source);

        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(1, result.MissingLocalFiles);
        Assert.Equal(1, result.RemoteEntries);
        Assert.Equal(1, result.SkippedCredentialUrls);
        Assert.Contains(existing, result.Entries);
        Assert.Contains("https://example.com/stream/1", result.Entries);
        Assert.DoesNotContain(result.Entries, entry => entry.Contains("X-Plex-Token", StringComparison.Ordinal));
    }
}
