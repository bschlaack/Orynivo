using Orynivo.Scrobbling;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the Last.fm API request signature: sorted parameter concatenation
/// plus the API secret, hashed with MD5.
/// </summary>
public sealed class LastFmSignatureTests
{
    /// <summary>The signature matches a fixed reference vector.</summary>
    [Fact]
    public void Compute_MatchesKnownVector()
    {
        var parameters = new Dictionary<string, string>
        {
            ["api_key"] = "abc",
            ["method"] = "auth.getToken"
        };

        Assert.Equal("3334e36028583f782c8e6db457c76835", LastFmSignature.Compute(parameters, "sec"));
    }

    /// <summary>Parameter insertion order does not affect the signature.</summary>
    [Fact]
    public void Compute_IsOrderIndependent()
    {
        var first = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" };
        var second = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };

        Assert.Equal(LastFmSignature.Compute(first, "s"), LastFmSignature.Compute(second, "s"));
    }

    /// <summary>Changing a value or the secret changes the signature.</summary>
    [Fact]
    public void Compute_DependsOnValuesAndSecret()
    {
        var valueOne = new Dictionary<string, string> { ["a"] = "1" };
        var valueTwo = new Dictionary<string, string> { ["a"] = "2" };

        Assert.NotEqual(LastFmSignature.Compute(valueOne, "s"), LastFmSignature.Compute(valueTwo, "s"));
        Assert.NotEqual(LastFmSignature.Compute(valueOne, "s1"), LastFmSignature.Compute(valueOne, "s2"));
    }

    /// <summary>The signature is 32 lowercase hexadecimal characters.</summary>
    [Fact]
    public void Compute_ReturnsLowercaseHex()
    {
        var signature = LastFmSignature.Compute(new Dictionary<string, string> { ["a"] = "1" }, "s");

        Assert.Equal(32, signature.Length);
        Assert.Matches("^[0-9a-f]{32}$", signature);
    }
}
