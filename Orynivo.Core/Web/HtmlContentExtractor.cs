using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Orynivo.Web;

/// <summary>
/// Lightweight, dependency-free helpers that turn fetched HTML into plain text or
/// a compact Markdown representation suitable for feeding to a language model.
/// The conversion is intentionally forgiving rather than a full HTML parser; the
/// input is always size-capped by <see cref="WebBrowsingService"/> first.
/// </summary>
public static class HtmlContentExtractor
{
    private static readonly Regex ScriptStyleRegex = new(
        @"<(script|style|head|noscript|svg|template)\b[^>]*>.*?</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CommentRegex = new(
        @"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TagRegex = new(
        @"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex TitleRegex = new(
        @"<title[^>]*>(.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Extracts the document title from HTML, or an empty string when absent.</summary>
    /// <param name="html">The raw HTML.</param>
    /// <returns>The decoded, trimmed title, or an empty string.</returns>
    public static string ExtractTitle(string html)
    {
        var match = TitleRegex.Match(html);
        return match.Success
            ? CollapseWhitespace(WebUtility.HtmlDecode(StripTags(match.Groups[1].Value))).Trim()
            : string.Empty;
    }

    /// <summary>Converts HTML (or plain text) into readable plain text.</summary>
    /// <param name="content">The raw response body.</param>
    /// <param name="mediaType">The response media type, used to skip conversion for plain text.</param>
    /// <returns>Decoded plain text with normalized whitespace.</returns>
    public static string ToText(string content, string mediaType)
    {
        if (mediaType.Contains("plain", StringComparison.OrdinalIgnoreCase))
            return NormalizeLines(content).Trim();

        var html = ScriptStyleRegex.Replace(content, " ");
        html = CommentRegex.Replace(html, " ");
        html = Regex.Replace(html, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<(br|hr)\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</(p|div|section|article|tr|h[1-6]|ul|ol|li|table)>", "\n", RegexOptions.IgnoreCase);
        var text = WebUtility.HtmlDecode(StripTags(html));
        return NormalizeLines(text).Trim();
    }

    /// <summary>Converts HTML into a compact Markdown representation.</summary>
    /// <param name="content">The raw response body.</param>
    /// <param name="mediaType">The response media type, used to skip conversion for plain text.</param>
    /// <returns>A Markdown string preserving headings, links, lists, and emphasis.</returns>
    public static string ToMarkdown(string content, string mediaType)
    {
        if (mediaType.Contains("plain", StringComparison.OrdinalIgnoreCase))
            return NormalizeLines(content).Trim();

        var html = ScriptStyleRegex.Replace(content, " ");
        html = CommentRegex.Replace(html, " ");

        html = Regex.Replace(
            html,
            @"<h([1-6])\b[^>]*>(.*?)</h\1>",
            m => "\n\n" + new string('#', int.Parse(m.Groups[1].Value)) + " " +
                 Clean(m.Groups[2].Value) + "\n\n",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        html = Regex.Replace(
            html,
            @"<a\b[^>]*?href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a>",
            m =>
            {
                var href = m.Groups[1].Value.Trim();
                var inner = Clean(m.Groups[2].Value);
                return string.IsNullOrEmpty(inner) ? href : $"[{inner}]({href})";
            },
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        html = Regex.Replace(
            html,
            @"<(strong|b)\b[^>]*>(.*?)</\1>",
            m => "**" + Clean(m.Groups[2].Value) + "**",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        html = Regex.Replace(
            html,
            @"<(em|i)\b[^>]*>(.*?)</\1>",
            m => "*" + Clean(m.Groups[2].Value) + "*",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        html = Regex.Replace(html, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<(br|hr)\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</(p|div|section|article|tr|ul|ol|li|table)>", "\n\n", RegexOptions.IgnoreCase);

        var markdown = WebUtility.HtmlDecode(StripTags(html));
        return NormalizeLines(markdown).Trim();
    }

    /// <summary>Strips inner tags, decodes entities, and collapses whitespace to a single line.</summary>
    /// <param name="fragment">The HTML fragment.</param>
    /// <returns>Clean single-line text.</returns>
    private static string Clean(string fragment) =>
        CollapseWhitespace(WebUtility.HtmlDecode(StripTags(fragment))).Trim();

    /// <summary>Removes all HTML tags from a fragment.</summary>
    /// <param name="html">The HTML fragment.</param>
    /// <returns>The fragment without tags.</returns>
    private static string StripTags(string html) => TagRegex.Replace(html, " ");

    /// <summary>Collapses runs of whitespace into single spaces.</summary>
    /// <param name="text">Input text.</param>
    /// <returns>Text with collapsed inline whitespace.</returns>
    private static string CollapseWhitespace(string text) =>
        Regex.Replace(text, @"\s+", " ");

    /// <summary>Normalizes line breaks: trims inline whitespace and limits blank runs.</summary>
    /// <param name="text">Input text.</param>
    /// <returns>Text with tidy line breaks.</returns>
    private static string NormalizeLines(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"[ \t\f\v]+", " ");
        text = Regex.Replace(text, @" *\n *", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text;
    }
}
