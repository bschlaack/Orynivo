using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
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
        _panel.Children.Add(CreateText(StripInlineMarkdown(text), fontSize, fontWeight));

    /// <summary>Adds a monospace code block to the rendered output.</summary>
    /// <param name="text">The code text to render.</param>
    private void AddCodeBlock(string text)
    {
        var block = CreateText(text.TrimEnd(), FontSizeResource("FontSizeCaption"), FontWeight.Normal);
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

    /// <summary>Creates a themed wrapped text block.</summary>
    /// <param name="text">The text to display.</param>
    /// <param name="fontSize">The font size to use.</param>
    /// <param name="fontWeight">The font weight to use.</param>
    /// <returns>The configured text block.</returns>
    private TextBlock CreateText(string text, double fontSize, FontWeight fontWeight)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontWeight = fontWeight,
            LineHeight = Math.Max(18, fontSize + 7),
        };
        block.Foreground = Foreground ?? Brushes.White;
        return block;
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

    /// <summary>Removes simple inline Markdown markers that this lightweight renderer does not style separately.</summary>
    /// <param name="text">The source text.</param>
    /// <returns>The text with common inline markers removed.</returns>
    private static string StripInlineMarkdown(string text)
    {
        var result = InlineCodeRegex().Replace(text, "$1");
        result = LinkRegex().Replace(result, "$1 ($2)");
        result = BoldRegex().Replace(result, match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
        result = ItalicRegex().Replace(result, match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
        return result;
    }

    [GeneratedRegex(@"^(\d+)\.\s+(.+)$")]
    private static partial Regex NumberedListRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*|__([^_]+)__")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<!\*)\*([^*]+)\*(?!\*)|_([^_]+)_")]
    private static partial Regex ItalicRegex();
}
