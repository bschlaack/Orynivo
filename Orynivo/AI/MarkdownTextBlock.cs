using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Orynivo.AI;

/// <summary>
/// Lightweight Markdown renderer used by the embedded AI chat.
/// </summary>
internal sealed partial class MarkdownTextBlock : UserControl
{
    /// <summary>Defines the Markdown text rendered by the control.</summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, string>(nameof(Text), string.Empty);

    private readonly StackPanel _panel = new()
    {
        Spacing = 7,
    };

    /// <summary>Initializes a new instance of the renderer.</summary>
    public MarkdownTextBlock()
    {
        Content = _panel;
        ResourcesChanged += (_, _) => RenderMarkdown();
        RenderMarkdown();
    }

    /// <summary>Gets or sets the Markdown text rendered by the control.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == ForegroundProperty)
            RenderMarkdown();
    }

    /// <summary>Renders the current Markdown text into Avalonia controls.</summary>
    private void RenderMarkdown()
    {
        _panel.Children.Clear();

        var markdown = Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(markdown))
            return;

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        var code = new List<string>();
        var inCode = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                if (inCode)
                {
                    AddCodeBlock(string.Join(Environment.NewLine, code));
                    code.Clear();
                    inCode = false;
                }
                else
                {
                    inCode = true;
                }
                continue;
            }

            if (inCode)
            {
                code.Add(raw);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(paragraph);
                continue;
            }

            var trimmed = line.TrimStart();
            if (TryAddHeading(trimmed) ||
                TryAddListItem(trimmed) ||
                TryAddQuote(trimmed) ||
                TryAddDivider(trimmed))
            {
                FlushParagraph(paragraph);
                continue;
            }

            paragraph.Add(trimmed);
        }

        FlushParagraph(paragraph);
        if (inCode && code.Count > 0)
            AddCodeBlock(string.Join(Environment.NewLine, code));
    }

    /// <summary>Flushes pending paragraph lines into one wrapped text block.</summary>
    /// <param name="paragraph">The pending paragraph lines.</param>
    private void FlushParagraph(List<string> paragraph)
    {
        if (paragraph.Count == 0)
            return;

        AddText(string.Join(" ", paragraph), FontSizeResource("FontSizeBody"), FontWeight.Normal);
        paragraph.Clear();
    }

    /// <summary>Attempts to render a Markdown heading.</summary>
    /// <param name="line">The trimmed input line.</param>
    /// <returns><see langword="true"/> when the line was rendered as a heading.</returns>
    private bool TryAddHeading(string line)
    {
        var level = 0;
        while (level < line.Length && level < 6 && line[level] == '#')
            level++;
        if (level == 0 || level >= line.Length || line[level] != ' ')
            return false;

        var text = line[(level + 1)..].Trim();
        var resource = level switch
        {
            1 => "FontSizeTitle",
            2 => "FontSizeSubtitle",
            _ => "FontSizeBodyStrong",
        };
        AddText(text, FontSizeResource(resource), FontWeight.SemiBold);
        return true;
    }

    /// <summary>Attempts to render a Markdown bullet or numbered-list item.</summary>
    /// <param name="line">The trimmed input line.</param>
    /// <returns><see langword="true"/> when the line was rendered as a list item.</returns>
    private bool TryAddListItem(string line)
    {
        string? text = null;
        string marker;
        if (line.StartsWith("- ", StringComparison.Ordinal) ||
            line.StartsWith("* ", StringComparison.Ordinal))
        {
            marker = "•";
            text = line[2..].Trim();
        }
        else
        {
            var match = NumberedListRegex().Match(line);
            if (!match.Success)
                return false;
            marker = match.Groups[1].Value + ".";
            text = match.Groups[2].Value.Trim();
        }

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        row.Children.Add(CreateText(marker, FontSizeResource("FontSizeBody"), FontWeight.Normal));
        var body = CreateText(text, FontSizeResource("FontSizeBody"), FontWeight.Normal);
        Grid.SetColumn(body, 1);
        body.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(body);
        _panel.Children.Add(row);
        return true;
    }

    /// <summary>Attempts to render a Markdown block quote.</summary>
    /// <param name="line">The trimmed input line.</param>
    /// <returns><see langword="true"/> when the line was rendered as a quote.</returns>
    private bool TryAddQuote(string line)
    {
        if (!line.StartsWith("> ", StringComparison.Ordinal))
            return false;

        var border = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 2),
        };
        border.BorderBrush = ResourceBrush("AppAccentBrush");
        border.Child = CreateText(line[2..].Trim(), FontSizeResource("FontSizeBody"), FontWeight.Normal);
        _panel.Children.Add(border);
        return true;
    }

    /// <summary>Attempts to render a Markdown horizontal divider.</summary>
    /// <param name="line">The trimmed input line.</param>
    /// <returns><see langword="true"/> when the line was rendered as a divider.</returns>
    private bool TryAddDivider(string line)
    {
        if (line is not ("---" or "***" or "___"))
            return false;

        var border = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4),
        };
        border.Background = ResourceBrush("AppInputBorderBrush");
        _panel.Children.Add(border);
        return true;
    }

    /// <summary>Adds a formatted text line to the rendered output.</summary>
    /// <param name="text">The text to render.</param>
    /// <param name="fontSize">The font size to use.</param>
    /// <param name="fontWeight">The font weight to use.</param>
    private void AddText(string text, double fontSize, FontWeight fontWeight) =>
        _panel.Children.Add(CreateText(text, fontSize, fontWeight));

    /// <summary>Adds a monospace code block to the rendered output.</summary>
    /// <param name="text">The code text to render.</param>
    private void AddCodeBlock(string text)
    {
        // Code is rendered verbatim: inline Markdown must not be interpreted inside it.
        var block = CreateLiteralText(text.TrimEnd(), FontSizeResource("FontSizeCaption"));
        block.FontFamily = FontFamily.Parse("Consolas, Cascadia Mono, monospace");
        block.LineHeight = 19;

        var border = new Border
        {
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(6),
            Child = block,
        };
        border.Background = ResourceBrush("AppSurfaceHoverBrush");
        border.BorderBrush = ResourceBrush("AppInputBorderBrush");
        border.BorderThickness = new Thickness(1);
        _panel.Children.Add(border);
    }

    /// <summary>Creates a themed wrapped text block with inline Markdown (bold, italic, code, links) rendered as styled runs.</summary>
    /// <param name="text">The text to display, possibly containing inline Markdown.</param>
    /// <param name="fontSize">The font size to use.</param>
    /// <param name="fontWeight">The base font weight to use.</param>
    /// <returns>The configured text block.</returns>
    private TextBlock CreateText(string text, double fontSize, FontWeight fontWeight)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontWeight = fontWeight,
            LineHeight = Math.Max(18, fontSize + 7),
        };
        block.Foreground = Foreground ?? Brushes.White;
        var inlines = new InlineCollection();
        AppendInlines(inlines, text);
        block.Inlines = inlines;
        return block;
    }

    /// <summary>Creates a themed wrapped text block that renders its text verbatim (no inline Markdown).</summary>
    /// <param name="text">The literal text to display.</param>
    /// <param name="fontSize">The font size to use.</param>
    /// <returns>The configured text block.</returns>
    private TextBlock CreateLiteralText(string text, double fontSize)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            LineHeight = Math.Max(18, fontSize + 7),
        };
        block.Foreground = Foreground ?? Brushes.White;
        return block;
    }

    /// <summary>
    /// Parses inline Markdown (<c>**bold**</c>/<c>__bold__</c>, <c>*italic*</c>/<c>_italic_</c>,
    /// <c>`code`</c>, and <c>[label](url)</c>) into styled runs. Unmatched markers are kept as
    /// literal text so ordinary asterisks and underscores are not swallowed.
    /// </summary>
    /// <param name="inlines">The inline collection to append runs to.</param>
    /// <param name="text">The source text.</param>
    private static void AppendInlines(InlineCollection inlines, string text)
    {
        var buffer = new StringBuilder();
        void Flush()
        {
            if (buffer.Length == 0)
                return;
            inlines.Add(new Run(buffer.ToString()));
            buffer.Clear();
        }

        int i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            // Inline code: `...`
            if (c == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    inlines.Add(new Run(text[(i + 1)..end])
                    {
                        FontFamily = FontFamily.Parse("Consolas, Cascadia Mono, monospace")
                    });
                    i = end + 1;
                    continue;
                }
            }

            // Bold: **...** or __...__
            if ((c == '*' || c == '_') && i + 1 < text.Length && text[i + 1] == c)
            {
                var marker = new string(c, 2);
                var end = text.IndexOf(marker, i + 2, StringComparison.Ordinal);
                if (end > i + 1)
                {
                    Flush();
                    inlines.Add(new Run(text[(i + 2)..end]) { FontWeight = FontWeight.SemiBold });
                    i = end + 2;
                    continue;
                }
            }

            // Italic: *...* or _..._
            if (c == '*' || c == '_')
            {
                var end = text.IndexOf(c, i + 1);
                if (end > i + 1)
                {
                    Flush();
                    inlines.Add(new Run(text[(i + 1)..end]) { FontStyle = FontStyle.Italic });
                    i = end + 1;
                    continue;
                }
            }

            // Link: [label](url) -> "label (url)"
            if (c == '[')
            {
                var match = LinkRegex().Match(text, i);
                if (match.Success && match.Index == i)
                {
                    Flush();
                    inlines.Add(new Run(match.Groups[1].Value));
                    inlines.Add(new Run($" ({match.Groups[2].Value})"));
                    i = match.Index + match.Length;
                    continue;
                }
            }

            buffer.Append(c);
            i++;
        }

        Flush();
    }

    /// <summary>Resolves a typography resource to a numeric font size.</summary>
    /// <param name="resourceKey">The resource key to resolve.</param>
    /// <returns>The resource value, or the body size when unavailable.</returns>
    private double FontSizeResource(string resourceKey)
    {
        if (TryGetResource(resourceKey, ActualThemeVariant, out var value) && value is double size)
            return size;
        return 13;
    }

    /// <summary>Resolves a themed brush resource.</summary>
    /// <param name="resourceKey">The resource key to resolve.</param>
    /// <returns>The resolved brush, or a transparent fallback.</returns>
    private IBrush ResourceBrush(string resourceKey)
    {
        if (TryGetResource(resourceKey, ActualThemeVariant, out var value) && value is IBrush brush)
            return brush;
        return resourceKey switch
        {
            "AppAccentBrush" => Brushes.DeepSkyBlue,
            "AppInputBorderBrush" => Brushes.DimGray,
            "AppSurfaceHoverBrush" => Brushes.Black,
            _ => Brushes.White,
        };
    }

    [GeneratedRegex(@"^(\d+)\.\s+(.+)$")]
    private static partial Regex NumberedListRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex LinkRegex();
}
