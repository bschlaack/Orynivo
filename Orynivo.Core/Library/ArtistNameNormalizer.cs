using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Orynivo.Library;

/// <summary>
/// Normalises raw artist name strings for consistent display and identity comparison.
/// Strips featured-artist suffixes and trailing separators; builds diacritic-free comparison keys.
/// </summary>
public static partial class ArtistNameNormalizer
{
    /// <summary>
    /// Returns the primary artist name with featured-artist annotations and trailing separators removed.
    /// Unicode is normalised to NFC form and internal whitespace is collapsed.
    /// </summary>
    /// <param name="value">Raw artist name from a tag or database field.</param>
    public static string NormalizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var name = value.Normalize(NormalizationForm.FormKC).Trim();
        name = MultipleWhitespaceRegex().Replace(name, " ");
        name = name.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? name;
        name = FeaturedArtistRegex().Replace(name, string.Empty);
        return TrailingSeparatorRegex().Replace(name, string.Empty).Trim();
    }

    /// <summary>
    /// Builds a lowercase, diacritic-free, punctuation-stripped key suitable for identity comparison
    /// across Unicode and case variants. Falls back to a <c>raw:</c>-prefixed value when only
    /// non-letter/digit characters remain.
    /// </summary>
    /// <param name="value">Raw artist name to convert.</param>
    public static string CreateComparisonKey(string? value)
    {
        var displayName = NormalizeDisplayName(value);
        if (displayName.Length == 0)
            return string.Empty;

        var decomposed = displayName.Normalize(NormalizationForm.FormD);
        var key = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
                key.Append(char.ToLowerInvariant(character));
        }

        return key.Length == 0
            ? $"raw:{displayName.ToLowerInvariant()}"
            : key.ToString();
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultipleWhitespaceRegex();

    [GeneratedRegex(
        @"\s*[\(\[]?\s*\b(?:feat(?:uring)?|ft)\.?\s*[,.:;\-]?\s+.*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FeaturedArtistRegex();

    [GeneratedRegex(@"[\s,;/+&\-–—]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingSeparatorRegex();
}
