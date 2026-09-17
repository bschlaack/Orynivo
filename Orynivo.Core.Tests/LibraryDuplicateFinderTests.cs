using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the Library Doctor duplicate grouping: byte-identical files form
/// exact groups, unhashed same-size matches form likely groups, and alternate
/// recordings are never reported as duplicates.
/// </summary>
public sealed class LibraryDuplicateFinderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "orynivo-duplicate-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temporary working directory.</summary>
    public LibraryDuplicateFinderTests() => Directory.CreateDirectory(_directory);

    /// <summary>Removes the temporary working directory.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private static MetadataRepairTrack Track(long id, string path, long size, string? fingerprint) =>
        new(
            id, path, path,
            Title: "Title", Artist: "Artist", Album: "Album", AlbumArtist: "Artist",
            Duration: 180, TrackNumber: 1, DiscNumber: 1,
            AcoustIdFingerprint: fingerprint, FileSize: size);

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Byte-identical files are grouped as exact duplicates.</summary>
    [Fact]
    public void FindDuplicateGroups_DetectsExactDuplicates()
    {
        var first = WriteFile("a.flac", "identical-payload");
        var second = WriteFile("b.flac", "identical-payload");
        var size = new FileInfo(first).Length;

        var groups = LibraryMetadataRepairService.FindDuplicateGroups(
        [
            Track(1, first, size, "fingerprint-1"),
            Track(2, second, size, "fingerprint-1")
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(LibraryDuplicateKind.Exact, group.Kind);
        Assert.Equal(2, group.Files.Count);
        Assert.Contains(first, group.Files.Select(file => file.Path));
        Assert.Contains(second, group.Files.Select(file => file.Path));
    }

    /// <summary>Same fingerprint and size but different content is an alternate recording.</summary>
    [Fact]
    public void FindDuplicateGroups_IgnoresAlternateRecordings()
    {
        var first = WriteFile("c.flac", "payload-one");
        var second = WriteFile("d.flac", "payload-two");
        var size = new FileInfo(first).Length;

        var groups = LibraryMetadataRepairService.FindDuplicateGroups(
        [
            Track(1, first, size, "fingerprint-2"),
            Track(2, second, size, "fingerprint-2")
        ]);

        Assert.Empty(groups);
    }

    /// <summary>Without file inspection, same-fingerprint same-size files are likely duplicates.</summary>
    [Fact]
    public void FindDuplicateGroups_ReportsLikelyDuplicatesWithoutHashing()
    {
        var groups = LibraryMetadataRepairService.FindDuplicateGroups(
        [
            Track(1, "/music/a.flac", 1000, "fingerprint-3"),
            Track(2, "/music/b.flac", 1000, "fingerprint-3")
        ], inspectFiles: false);

        var group = Assert.Single(groups);
        Assert.Equal(LibraryDuplicateKind.Likely, group.Kind);
        Assert.Equal(2, group.Files.Count);
    }

    /// <summary>Different fingerprints are never grouped.</summary>
    [Fact]
    public void FindDuplicateGroups_IgnoresDifferentFingerprints()
    {
        var groups = LibraryMetadataRepairService.FindDuplicateGroups(
        [
            Track(1, "/music/a.flac", 1000, "fingerprint-a"),
            Track(2, "/music/b.flac", 1000, "fingerprint-b")
        ], inspectFiles: false);

        Assert.Empty(groups);
    }

    /// <summary>Different sizes within one fingerprint are alternate editions, not duplicates.</summary>
    [Fact]
    public void FindDuplicateGroups_IgnoresDifferentSizes()
    {
        var groups = LibraryMetadataRepairService.FindDuplicateGroups(
        [
            Track(1, "/music/a.flac", 1000, "fingerprint-4"),
            Track(2, "/music/b.flac", 2000, "fingerprint-4")
        ], inspectFiles: false);

        Assert.Empty(groups);
    }

    /// <summary>Rows that share one physical path collapse to a single file.</summary>
    [Fact]
    public void FindDuplicateGroups_CollapsesRepeatedPaths()
    {
        var groups = LibraryMetadataRepairService.FindDuplicateGroups(
        [
            Track(1, "/music/a.flac", 1000, "fingerprint-5"),
            Track(2, "/music/a.flac", 1000, "fingerprint-5")
        ], inspectFiles: false);

        Assert.Empty(groups);
    }
}
