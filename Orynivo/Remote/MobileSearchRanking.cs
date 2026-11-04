using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Orynivo.Remote;

/// <summary>Ranks cross-provider music matches consistently and rejects unrelated substring hits.</summary>
internal static class MobileSearchRanking
{
    /// <summary>Ranks exact titles, then phrase and prefix matches across title, artist and album.</summary>
    /// <param name="query">Literal user query, never Lucene syntax.</param>
    /// <param name="track">Safe candidate metadata.</param>
    /// <returns>Positive relevance, or zero when a query word is missing.</returns>
    internal static int Score(string query, MobileRemoteTrack track)
    {
        var terms = Words(query);
        var title = Words(track.Title); var artist = Words(track.Artist); var album = Words(track.Album);
        var all = title.Concat(artist).Concat(album).ToArray();
        if (terms.Length == 0 || terms.Any(t => !all.Any(w => w.StartsWith(t, StringComparison.Ordinal)))) return 0;
        var phrase = string.Join(' ', terms);
        return 1 + (string.Join(' ', title) == phrase ? 1000 : 0)
            + (string.Join(' ', title).Contains(phrase, StringComparison.Ordinal) ? 200 : 0)
            + (string.Join(' ', artist) == phrase ? 150 : 0)
            + (string.Join(' ', album) == phrase ? 100 : 0)
            + terms.Sum(t => title.Contains(t) ? 20 : artist.Contains(t) ? 10 : 0);
    }

    private static string[] Words(string? value)
    {
        var text = new string((value ?? "").ToLowerInvariant().Replace("ß", "ss", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Matches(text, @"[\p{L}\p{N}]+").Select(m => m.Value).ToArray();
    }
}
