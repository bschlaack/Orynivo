using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies artist display-name normalization and the diacritic-free,
/// punctuation-stripped identity comparison key.
/// </summary>
public sealed class ArtistNameNormalizerTests
{
    /// <summary>Whitespace is trimmed and collapsed, and null/blank input yields an empty name.</summary>
    /// <param name="input">Raw artist name.</param>
    /// <param name="expected">Expected display name.</param>
    [Theory]
    [InlineData("  Daft Punk  ", "Daft Punk")]
    [InlineData("Daft   Punk", "Daft Punk")]
    [InlineData("A;B", "A")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeDisplayName_TrimsAndCollapses(string input, string expected)
        => Assert.Equal(expected, ArtistNameNormalizer.NormalizeDisplayName(input));

    /// <summary>Null input yields an empty display name.</summary>
    [Fact]
    public void NormalizeDisplayName_NullYieldsEmpty()
        => Assert.Equal(string.Empty, ArtistNameNormalizer.NormalizeDisplayName(null));

    /// <summary>Featured-artist annotations and trailing separators are removed.</summary>
    /// <param name="input">Raw artist name.</param>
    /// <param name="expected">Expected primary artist name.</param>
    [Theory]
    [InlineData("Artist feat. Someone", "Artist")]
    [InlineData("Artist featuring Someone", "Artist")]
    [InlineData("Artist ft. Someone", "Artist")]
    [InlineData("Artist (feat. Someone)", "Artist")]
    [InlineData("Artist -", "Artist")]
    [InlineData("Artist &", "Artist")]
    public void NormalizeDisplayName_StripsFeaturedAndTrailingSeparators(string input, string expected)
        => Assert.Equal(expected, ArtistNameNormalizer.NormalizeDisplayName(input));

    /// <summary>Diacritics and case do not affect the comparison key.</summary>
    /// <param name="left">First artist name.</param>
    /// <param name="right">Second artist name.</param>
    [Theory]
    [InlineData("Björk", "Bjork")]
    [InlineData("Müller", "Muller")]
    [InlineData("The Beatles", "the beatles")]
    [InlineData("Sigur Rós", "Sigur Ros")]
    public void CreateComparisonKey_IsDiacriticAndCaseInsensitive(string left, string right)
        => Assert.Equal(
            ArtistNameNormalizer.CreateComparisonKey(left),
            ArtistNameNormalizer.CreateComparisonKey(right));

    /// <summary>Punctuation and separators are stripped from the comparison key.</summary>
    [Fact]
    public void CreateComparisonKey_StripsPunctuation()
        => Assert.Equal(
            ArtistNameNormalizer.CreateComparisonKey("ACDC"),
            ArtistNameNormalizer.CreateComparisonKey("AC/DC"));

    /// <summary>Featured-artist annotations are removed before key construction.</summary>
    [Fact]
    public void CreateComparisonKey_StripsFeaturedArtist()
        => Assert.Equal(
            ArtistNameNormalizer.CreateComparisonKey("Artist"),
            ArtistNameNormalizer.CreateComparisonKey("Artist feat. Someone"));

    /// <summary>Values with no letters or digits fall back to a raw-prefixed key.</summary>
    [Fact]
    public void CreateComparisonKey_FallsBackForSymbolsOnly()
        => Assert.StartsWith("raw:", ArtistNameNormalizer.CreateComparisonKey("!@#"));

    /// <summary>Null or blank input yields an empty key.</summary>
    /// <param name="input">Raw artist name.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateComparisonKey_EmptyInputYieldsEmptyKey(string? input)
        => Assert.Equal(string.Empty, ArtistNameNormalizer.CreateComparisonKey(input));
}
