using Orynivo.Web;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the dependency-free HTML to text/Markdown conversion used by the web
/// browsing tools, including script removal, entity decoding, and plain-text
/// passthrough.
/// </summary>
public sealed class HtmlContentExtractorTests
{
    /// <summary>The document title is decoded and whitespace-collapsed.</summary>
    /// <param name="html">Raw HTML.</param>
    /// <param name="expected">Expected title.</param>
    [Theory]
    [InlineData("<html><head><title>  Hello   World  </title></head></html>", "Hello World")]
    [InlineData("<title>Hello &amp; World</title>", "Hello & World")]
    [InlineData("<title><b>Bold</b> Title</title>", "Bold Title")]
    [InlineData("<html><body>No title here</body></html>", "")]
    public void ExtractTitle_DecodesAndCollapses(string html, string expected)
        => Assert.Equal(expected, HtmlContentExtractor.ExtractTitle(html));

    /// <summary>HTML is converted to plain text with block and list structure preserved.</summary>
    [Fact]
    public void ToText_ConvertsHtmlToPlainText()
    {
        const string html = """
            <html><head><title>Hidden</title><style>.a{color:red}</style></head>
            <body>
              <p>Hello <b>World</b></p>
              <ul><li>One</li><li>Two</li></ul>
              <!-- comment --><br>End
            </body></html>
            """;

        var text = HtmlContentExtractor.ToText(html, "text/html");

        Assert.Contains("Hello World", text);
        Assert.Contains("- One", text);
        Assert.Contains("- Two", text);
        Assert.Contains("End", text);
        Assert.DoesNotContain("Hidden", text);
        Assert.DoesNotContain(".a{color:red}", text);
        Assert.DoesNotContain("comment", text);
        Assert.DoesNotContain("<", text);
    }

    /// <summary>Markdown preserves headings, links, emphasis, and lists.</summary>
    [Fact]
    public void ToMarkdown_PreservesStructure()
    {
        const string html = """
            <h1>Title</h1>
            <p>See <a href="https://example.com">Example</a>, <strong>bold</strong>, and <em>italic</em>.</p>
            <ul><li>Item</li></ul>
            """;

        var markdown = HtmlContentExtractor.ToMarkdown(html, "text/html");

        Assert.Contains("# Title", markdown);
        Assert.Contains("[Example](https://example.com)", markdown);
        Assert.Contains("**bold**", markdown);
        Assert.Contains("*italic*", markdown);
        Assert.Contains("- Item", markdown);
        Assert.DoesNotContain("<", markdown);
    }

    /// <summary>A link without inner text falls back to the raw URL.</summary>
    [Fact]
    public void ToMarkdown_EmptyLinkTextUsesHref()
        => Assert.Contains(
            "https://example.com",
            HtmlContentExtractor.ToMarkdown("<a href=\"https://example.com\"></a>", "text/html"));

    /// <summary>Plain-text media types bypass HTML conversion and only normalize line breaks.</summary>
    [Fact]
    public void ToText_PlainTextPassthroughNormalizesLineBreaks()
    {
        var text = HtmlContentExtractor.ToText("a\r\n\r\n\r\n\r\nb", "text/plain");

        Assert.DoesNotContain("\r", text);
        Assert.Equal("a\n\nb", text);
    }

    /// <summary>Markdown conversion leaves plain text untouched apart from line breaks.</summary>
    [Fact]
    public void ToMarkdown_PlainTextPassthroughKeepsTags()
        => Assert.Equal("<b>x</b>", HtmlContentExtractor.ToMarkdown("<b>x</b>", "text/plain"));
}
