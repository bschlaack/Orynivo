using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the genre taxonomy traversal: ancestry queries, tag normalization,
/// and display-name resolution.
/// </summary>
public sealed class GenreCloudTaxonomyTests
{
    /// <summary>Ancestry queries treat a key as its own ancestor and follow every parent path.</summary>
    /// <param name="candidate">Candidate key.</param>
    /// <param name="ancestor">Expected ancestor key.</param>
    /// <param name="expected">Expected result.</param>
    [Theory]
    [InlineData("rock", "rock", true)]
    [InlineData("progressive-rock", "rock", true)]
    [InlineData("rock", "progressive-rock", false)]
    [InlineData("deep-house", "dance", true)]
    [InlineData("dance", "deep-house", false)]
    [InlineData("dance-pop", "dance", true)]
    [InlineData("dance-pop", "pop", true)]
    public void IsDescendantOrSelf_FollowsEveryParentPath(string candidate, string ancestor, bool expected)
        => Assert.Equal(expected, GenreCloudService.IsDescendantOrSelf(candidate, ancestor));

    /// <summary>Unknown keys match only themselves and never reach a taxonomy node.</summary>
    [Fact]
    public void IsDescendantOrSelf_HandlesUnknownKeys()
    {
        Assert.True(GenreCloudService.IsDescendantOrSelf("unknown-key", "unknown-key"));
        Assert.False(GenreCloudService.IsDescendantOrSelf("unknown-key", "rock"));
    }

    /// <summary>Dynamic unmapped keys belong to the virtual more-genres node.</summary>
    [Fact]
    public void IsDescendantOrSelf_ClassifiesUnmappedKeysUnderMoreGenres()
    {
        Assert.True(GenreCloudService.IsDescendantOrSelf("unmapped:zeuhl", "more-genres"));
        Assert.False(GenreCloudService.IsDescendantOrSelf("rock", "more-genres"));
    }

    /// <summary>Embedded genre values normalize through names, aliases, and delimiters.</summary>
    /// <param name="value">Raw genre value.</param>
    /// <param name="expectedKey">Expected normalized key.</param>
    [Theory]
    [InlineData("Progressive Rock", "progressive-rock")]
    [InlineData("prog rock", "progressive-rock")]
    [InlineData("D'n'B", "drum-and-bass")]
    [InlineData("Electronica", "electronic")]
    public void ResolveGenreKeys_NormalizesNamesAndAliases(string value, string expectedKey)
        => Assert.Contains(expectedKey, GenreCloudService.ResolveGenreKeys(value));

    /// <summary>Several delimited genres resolve independently.</summary>
    [Fact]
    public void ResolveGenreKeys_SplitsDelimitedValues()
    {
        var keys = GenreCloudService.ResolveGenreKeys("Rock; Pop|Jazz/Blues");

        Assert.Contains("rock", keys);
        Assert.Contains("pop", keys);
        Assert.Contains("blues", keys);
    }

    /// <summary>Unknown tags are preserved as dynamic unmapped keys.</summary>
    [Fact]
    public void ResolveGenreKeys_PreservesUnknownTags()
        => Assert.Equal(["unmapped:zeuhl"], GenreCloudService.ResolveGenreKeys("Zeuhl"));

    /// <summary>Blank input yields no keys.</summary>
    /// <param name="value">Blank value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveGenreKeys_BlankInputIsEmpty(string? value)
        => Assert.Empty(GenreCloudService.ResolveGenreKeys(value));

    /// <summary>Display names resolve for taxonomy keys, the virtual node, and dynamic keys.</summary>
    /// <param name="key">Taxonomy key.</param>
    /// <param name="expected">Expected display name.</param>
    [Theory]
    [InlineData("rock", "Rock")]
    [InlineData("more-genres", "More Genres")]
    [InlineData("unmapped:zeuhl", "Zeuhl")]
    [InlineData("unmapped:neo-prog", "Neo Prog")]
    [InlineData("unknown-key", "unknown-key")]
    public void GetDisplayName_ResolvesKnownVirtualAndDynamicKeys(string key, string expected)
        => Assert.Equal(expected, GenreCloudService.GetDisplayName(key));
}
