using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies that credential-bearing URLs are never considered persistable while
/// local and stable reference paths remain persistable.
/// </summary>
public sealed class QueuePathPolicyTests
{
    /// <summary>Local absolute paths, relative paths, and file URIs are persistable.</summary>
    /// <param name="path">The candidate local path.</param>
    [Theory]
    [InlineData("/home/user/track.flac")]
    [InlineData("Music/track.flac")]
    [InlineData("file:///home/user/track.flac")]
    public void CanPersist_AllowsLocalPaths(string path)
        => Assert.True(QueuePathPolicy.CanPersist(path));

    /// <summary>Virtual CUE paths are persistable.</summary>
    /// <param name="path">The candidate CUE virtual path.</param>
    [Theory]
    [InlineData("cue://album/track/1")]
    [InlineData("CUE://album/track/1")]
    public void CanPersist_AllowsCuePaths(string path)
        => Assert.True(QueuePathPolicy.CanPersist(path));

    /// <summary>Stable credential-free Orynivo references are persistable.</summary>
    /// <param name="path">The candidate Orynivo reference.</param>
    [Theory]
    [InlineData("orynivo://server-1/track/42")]
    [InlineData("ORYNIVO://server-1/track/42")]
    public void CanPersist_AllowsOrynivoReferences(string path)
        => Assert.True(QueuePathPolicy.CanPersist(path));

    /// <summary>Plain remote URLs without credentials are persistable.</summary>
    /// <param name="path">The candidate remote URL.</param>
    [Theory]
    [InlineData("http://example.com/stream/1")]
    [InlineData("https://example.com/stream/1?format=flac")]
    public void CanPersist_AllowsCredentialFreeHttpUrls(string path)
        => Assert.True(QueuePathPolicy.CanPersist(path));

    /// <summary>URLs carrying a known credential query parameter are rejected.</summary>
    /// <param name="path">The credential-bearing remote URL.</param>
    [Theory]
    [InlineData("https://example.com/stream/1?X-Plex-Token=abc")]
    [InlineData("https://example.com/stream/1?x-plex-token=abc")]
    [InlineData("https://example.com/stream/1?token=abc")]
    [InlineData("https://example.com/stream/1?key=abc")]
    [InlineData("https://example.com/stream/1?KEY=abc")]
    public void CanPersist_RejectsCredentialQueryParameters(string path)
        => Assert.False(QueuePathPolicy.CanPersist(path));

    /// <summary>URLs embedding user information are rejected.</summary>
    [Fact]
    public void CanPersist_RejectsUserInfo()
        => Assert.False(QueuePathPolicy.CanPersist("https://user:secret@example.com/stream/1"));

    /// <summary>Unsupported absolute schemes are rejected.</summary>
    [Fact]
    public void CanPersist_RejectsUnsupportedScheme()
        => Assert.False(QueuePathPolicy.CanPersist("ftp://example.com/stream/1"));

    /// <summary>Empty and non-absolute values keep the historical permissive behavior.</summary>
    /// <param name="path">The non-absolute or empty candidate.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a uri")]
    public void CanPersist_AllowsNonAbsoluteValues(string path)
        => Assert.True(QueuePathPolicy.CanPersist(path));
}
