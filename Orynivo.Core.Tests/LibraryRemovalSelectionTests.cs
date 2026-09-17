using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the row selection used when a confirmed duplicate removal deletes
/// physical source paths from the library.
/// </summary>
public sealed class LibraryRemovalSelectionTests
{
    private static readonly IReadOnlySet<string> Targets =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/music/album.flac" };

    private static List<(string Path, string? SourcePath)> Records() =>
    [
        ("/music/album.flac", null),
        ("cue://track/001?sheet=album.cue", "/music/album.flac"),
        ("cue://track/002?sheet=album.cue", "/music/album.flac"),
        ("/music/other.flac", null)
    ];

    private static List<(string Path, string? SourcePath)> Select(
        IReadOnlyList<(string Path, string? SourcePath)> records)
        => LibraryScanner.SelectRemovalTargets(
            records,
            static record => record.Path,
            static record => record.SourcePath,
            Targets);

    /// <summary>A whole-file row and every virtual track sharing the source are selected.</summary>
    [Fact]
    public void SelectRemovalTargets_IncludesWholeFileAndVirtualTracks()
    {
        var selected = Select(Records());

        Assert.Equal(3, selected.Count);
        Assert.Contains(selected, record => record.Path == "/music/album.flac");
        Assert.Contains(selected, record => record.Path == "cue://track/001?sheet=album.cue");
        Assert.Contains(selected, record => record.Path == "cue://track/002?sheet=album.cue");
    }

    /// <summary>Unrelated files are never selected.</summary>
    [Fact]
    public void SelectRemovalTargets_ExcludesUnrelatedFiles()
        => Assert.DoesNotContain(Select(Records()), record => record.Path == "/music/other.flac");

    /// <summary>An empty target set selects nothing.</summary>
    [Fact]
    public void SelectRemovalTargets_EmptyTargetsSelectNothing()
    {
        var selected = LibraryScanner.SelectRemovalTargets(
            Records(),
            static record => record.Path,
            static record => record.SourcePath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Empty(selected);
    }
}
