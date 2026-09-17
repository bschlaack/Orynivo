using Orynivo;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the stable, credential-free Orynivo Server track and album
/// references used by playlists and queue drag-and-drop.
/// </summary>
public sealed class PlaylistReferencesTests
{
    /// <summary>Track references use the documented credential-free form.</summary>
    [Fact]
    public void BuildTrack_UsesDocumentedForm()
        => Assert.Equal("orynivo://server-1/track/42", PlaylistReferences.BuildTrack("server-1", 42));

    /// <summary>Track references escape the server identifier.</summary>
    [Fact]
    public void BuildTrack_EscapesServerId()
        => Assert.Equal("orynivo://a%20b/track/5", PlaylistReferences.BuildTrack("a b", 5));

    /// <summary>Building and parsing a track reference round-trips.</summary>
    [Fact]
    public void BuildTrack_RoundTrips()
    {
        var reference = PlaylistReferences.BuildTrack("server-1", 42);

        Assert.True(PlaylistReferences.TryParseTrack(reference, out var serverId, out var trackId));
        Assert.Equal("server-1", serverId);
        Assert.Equal(42L, trackId);
    }

    /// <summary>Track reference parsing is case-insensitive for the scheme and segment.</summary>
    [Fact]
    public void TryParseTrack_IsCaseInsensitive()
        => Assert.True(PlaylistReferences.TryParseTrack("ORYNIVO://Server-1/TRACK/42", out _, out _));

    /// <summary>Malformed or unrelated values are rejected as track references.</summary>
    /// <param name="path">Candidate value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://server-1/track/42")]
    [InlineData("orynivo://server-1/album/42")]
    [InlineData("orynivo://server-1/track/abc")]
    [InlineData("orynivo://server-1")]
    [InlineData("orynivo://server-1/track/42/extra")]
    [InlineData("orynivo:///track/42")]
    public void TryParseTrack_RejectsMalformedValues(string? path)
        => Assert.False(PlaylistReferences.TryParseTrack(path, out _, out _));

    /// <summary>Album references use the documented form.</summary>
    [Fact]
    public void BuildAlbum_UsesDocumentedForm()
        => Assert.Equal("orynivo-album:server-1:7", PlaylistReferences.BuildAlbum("server-1", 7));

    /// <summary>Building and parsing an album reference round-trips.</summary>
    [Fact]
    public void BuildAlbum_RoundTrips()
    {
        var reference = PlaylistReferences.BuildAlbum("server-1", 7);

        Assert.True(PlaylistReferences.TryParseAlbum(reference, out var serverId, out var albumId));
        Assert.Equal("server-1", serverId);
        Assert.Equal(7L, albumId);
    }

    /// <summary>The album reference splits on the last colon so server ids may contain colons.</summary>
    [Fact]
    public void TryParseAlbum_SplitsOnLastColon()
    {
        Assert.True(PlaylistReferences.TryParseAlbum("orynivo-album:srv:1:7", out var serverId, out var albumId));
        Assert.Equal("srv:1", serverId);
        Assert.Equal(7L, albumId);
    }

    /// <summary>Malformed or unrelated values are rejected as album references.</summary>
    /// <param name="token">Candidate value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("orynivo-album:")]
    [InlineData("orynivo-album:server-1")]
    [InlineData("orynivo-album:server-1:abc")]
    [InlineData("orynivo-album::7")]
    [InlineData("server-1:7")]
    public void TryParseAlbum_RejectsMalformedValues(string? token)
        => Assert.False(PlaylistReferences.TryParseAlbum(token, out _, out _));
}
