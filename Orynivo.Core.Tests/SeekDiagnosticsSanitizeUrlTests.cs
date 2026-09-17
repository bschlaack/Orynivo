using Orynivo.Audio;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies that diagnostic URL sanitization removes credentials before values
/// are written to local logs.
/// </summary>
public sealed class SeekDiagnosticsSanitizeUrlTests
{
    /// <summary>Secret query parameters are redacted case-insensitively.</summary>
    /// <param name="url">Credential-bearing URL.</param>
    [Theory]
    [InlineData("https://example.com/stream/1?key=secret")]
    [InlineData("https://example.com/stream/1?KEY=secret")]
    [InlineData("https://example.com/stream/1?api_key=secret")]
    [InlineData("https://example.com/stream/1?apikey=secret")]
    [InlineData("https://example.com/stream/1?token=secret")]
    [InlineData("https://example.com/stream/1?access_token=secret")]
    [InlineData("https://example.com/stream/1?X-Plex-Token=secret")]
    public void SanitizeUrl_RedactsSecretQueryParameters(string url)
    {
        var sanitized = SeekDiagnostics.SanitizeUrl(url);

        Assert.DoesNotContain("secret", sanitized);
        Assert.Contains("redacted", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Non-secret query parameters are preserved alongside redacted ones.</summary>
    [Fact]
    public void SanitizeUrl_PreservesNonSecretParameters()
    {
        var sanitized = SeekDiagnostics.SanitizeUrl("https://example.com/stream/1?format=flac&key=secret");

        Assert.Contains("format=flac", sanitized);
        Assert.DoesNotContain("secret", sanitized);
    }

    /// <summary>Embedded user information is removed.</summary>
    [Fact]
    public void SanitizeUrl_RemovesUserInfo()
    {
        var sanitized = SeekDiagnostics.SanitizeUrl("https://user:pass@example.com/stream/1");

        Assert.Equal("https://example.com/stream/1", sanitized);
        Assert.DoesNotContain("pass", sanitized);
    }

    /// <summary>Non-HTTP schemes and local paths are returned unchanged.</summary>
    /// <param name="value">Value to sanitize.</param>
    [Theory]
    [InlineData("orynivo://server-1/track/42")]
    [InlineData("/music/track.flac")]
    [InlineData("track.flac")]
    public void SanitizeUrl_ReturnsNonHttpValuesUnchanged(string value)
        => Assert.Equal(value, SeekDiagnostics.SanitizeUrl(value));
}
