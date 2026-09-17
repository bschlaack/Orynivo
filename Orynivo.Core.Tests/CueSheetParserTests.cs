using System.Text;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies CUE sheet parsing into virtual track definitions and virtual-path
/// detection.
/// </summary>
public sealed class CueSheetParserTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "orynivo-cue-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temporary working directory.</summary>
    public CueSheetParserTests() => Directory.CreateDirectory(_directory);

    /// <summary>Removes the temporary working directory.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    /// <summary>Virtual CUE and MKA paths are detected, ordinary paths are not.</summary>
    /// <param name="path">Candidate path.</param>
    /// <param name="expected">Expected result.</param>
    [Theory]
    [InlineData("cue://track/001?sheet=album.cue", true)]
    [InlineData("CUE://TRACK/001", true)]
    [InlineData("mka://chapter/001?source=album.mka", true)]
    [InlineData("MKA://CHAPTER/001", true)]
    [InlineData("/music/album.cue", false)]
    [InlineData("/music/track.flac", false)]
    public void IsVirtualPath_DetectsVirtualSchemes(string path, bool expected)
        => Assert.Equal(expected, CueSheetParser.IsVirtualPath(path));

    /// <summary>A two-track CUE sheet is parsed with boundaries and inherited album metadata.</summary>
    [Fact]
    public void Parse_ReadsTracksAndMetadata()
    {
        const string cue = """
            REM GENRE "Rock"
            REM DATE 1999
            PERFORMER "Album Artist"
            TITLE "Album Title"
            FILE "album.flac" WAVE
              TRACK 01 AUDIO
                TITLE "First"
                PERFORMER "Singer One"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                TITLE "Second"
                INDEX 01 03:30:00
            """;
        var cuePath = Path.Combine(_directory, "album.cue");
        File.WriteAllText(cuePath, cue, new UTF8Encoding(false));

        var tracks = CueSheetParser.Parse(cuePath);

        Assert.Equal(2, tracks.Count);

        var first = tracks[0];
        Assert.Equal(1, first.Number);
        Assert.Equal("First", first.Title);
        Assert.Equal("Singer One", first.Artist);
        Assert.Equal("Album Title", first.Album);
        Assert.Equal("Album Artist", first.AlbumArtist);
        Assert.Equal("Rock", first.Genre);
        Assert.Equal(1999, first.Year);
        Assert.Equal(0d, first.StartSeconds);
        Assert.Equal(210d, first.EndSeconds);
        Assert.StartsWith("cue://track/001", first.VirtualPath);
        Assert.EndsWith("album.flac", first.SourcePath);

        var second = tracks[1];
        Assert.Equal(2, second.Number);
        Assert.Equal("Second", second.Title);
        // A track without its own performer inherits the album artist.
        Assert.Equal("Album Artist", second.Artist);
        Assert.Equal(210d, second.StartSeconds);
        Assert.Null(second.EndSeconds);
        Assert.StartsWith("cue://track/002", second.VirtualPath);
    }
}
