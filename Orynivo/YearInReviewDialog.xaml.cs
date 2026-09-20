using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>
/// Shows the aggregated listening statistics for one chosen calendar year and
/// exports the rendered summary as a shareable PNG image. It only reads existing
/// playback history and never collects additional data.
/// </summary>
public partial class YearInReviewDialog : Window
{
    private readonly IReadOnlyList<int> _years;
    private readonly Func<int, Task<YearInReviewSummary?>> _summaryLoader;
    private readonly StackPanel _content = new() { Spacing = 16 };
    private YearInReviewSummary? _summary;
    private bool _suppressYearChange;

    /// <summary>Initializes a runtime-loader instance with the current year.</summary>
    public YearInReviewDialog()
        : this([DateTime.Now.Year], DateTime.Now.Year, _ => Task.FromResult<YearInReviewSummary?>(null))
    {
    }

    /// <summary>Initializes the dialog for a set of selectable years.</summary>
    /// <param name="years">Years offered in the selector, newest first.</param>
    /// <param name="selectedYear">Year shown first.</param>
    /// <param name="summaryLoader">Loads the summary for a year, or <see langword="null"/> when unavailable.</param>
    public YearInReviewDialog(
        IReadOnlyList<int> years,
        int selectedYear,
        Func<int, Task<YearInReviewSummary?>> summaryLoader)
    {
        ArgumentNullException.ThrowIfNull(years);
        ArgumentNullException.ThrowIfNull(summaryLoader);
        _years = years.Count > 0 ? years : [selectedYear];
        _summaryLoader = summaryLoader;
        InitializeComponent();
        ContentHost.Child = _content;
        YearComboBox.SelectionChanged += YearComboBox_OnSelectionChanged;
        _suppressYearChange = true;
        YearComboBox.ItemsSource = _years;
        YearComboBox.SelectedItem = _years.Contains(selectedYear) ? selectedYear : _years[0];
        _suppressYearChange = false;
        _ = RenderYearAsync((int)YearComboBox.SelectedItem!);
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
    }

    /// <summary>Renders the visible summary card into a PNG file chosen by the user.</summary>
    /// <param name="sender">The export action.</param>
    /// <param name="e">Click details.</param>
    private async void SaveImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Current.SaveAsImage,
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
            DefaultExtension = "png",
            SuggestedFileName = $"orynivo-{_summary?.Year ?? DateTime.Now.Year}.png"
        });
        if (file?.TryGetLocalPath() is not { Length: > 0 } filePath)
            return;

        try
        {
            var size = ContentHost.Bounds;
            if (size.Width < 1 || size.Height < 1)
            {
                StatusTextBlock.Text = LocalizationManager.Current.YearInReviewImageFailed;
                return;
            }

            using var bitmap = new RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)),
                new Vector(96, 96));
            bitmap.Render(ContentHost);
            bitmap.Save(filePath);
            StatusTextBlock.Text = string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Current.YearInReviewImageSaved,
                filePath);
        }
        catch (Exception exception)
        {
            CrashLogger.Log(exception, "Year in review image export");
            StatusTextBlock.Text = LocalizationManager.Current.YearInReviewImageFailed;
        }
    }

    /// <summary>Renders the current year summary into a PDF file chosen by the user.</summary>
    /// <param name="sender">The export action.</param>
    /// <param name="e">Click details.</param>
    private async void SavePdfButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_summary is null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Current.SaveAsPdf,
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
            DefaultExtension = "pdf",
            SuggestedFileName = $"orynivo-{_summary.Year}.pdf"
        });
        if (file?.TryGetLocalPath() is not { Length: > 0 } filePath)
            return;

        if (YearInReviewPdfExporter.TryWrite(_summary, filePath))
        {
            StatusTextBlock.Text = string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Current.YearInReviewPdfSaved,
                filePath);
            return;
        }

        StatusTextBlock.Text = LocalizationManager.Current.YearInReviewPdfFailed;
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close();

    private void YearComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressYearChange || YearComboBox.SelectedItem is not int year)
            return;
        _ = RenderYearAsync(year);
    }

    private async Task RenderYearAsync(int year)
    {
        _summary = await _summaryLoader(year).ConfigureAwait(true);
        Title = $"{LocalizationManager.Current.YearInReview} {year.ToString(CultureInfo.CurrentCulture)}";
        _content.Children.Clear();

        if (_summary is null || _summary.TotalListeningSeconds <= 0)
        {
            _content.Children.Add(new TextBlock
            {
                Text = LocalizationManager.Current.YearInReviewEmpty,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("AppMutedTextBrush")
            });
            SaveImageButton.IsEnabled = false;
            SavePdfButton.IsEnabled = false;
            return;
        }

        SaveImageButton.IsEnabled = true;
        SavePdfButton.IsEnabled = true;
        _content.Children.Add(BuildHeadlineNumbers());
        _content.Children.Add(BuildSection(LocalizationManager.Current.YearInReviewMonthly, BuildMonthBars()));
        AddTopList(
            LocalizationManager.Current.YearInReviewTopGenres,
            [.. _summary.TopGenres.Select(entry => (entry.Genre, entry.Seconds))]);
        AddTopList(
            LocalizationManager.Current.YearInReviewTopAlbums,
            [.. _summary.TopAlbums.Select(entry => (FormatAlbum(entry), entry.Seconds))]);
        AddTopList(
            LocalizationManager.Current.YearInReviewTopArtists,
            [.. _summary.TopArtists.Select(entry => (entry.Name, entry.Seconds))]);
    }

    private Control BuildHeadlineNumbers()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        grid.Children.Add(BuildNumberCard(
            (_summary!.TotalListeningSeconds / 3600d).ToString("N1", CultureInfo.CurrentCulture),
            LocalizationManager.Current.YearInReviewHours));
        var daysCard = BuildNumberCard(
            _summary.ActiveDays.ToString("N0", CultureInfo.CurrentCulture),
            LocalizationManager.Current.YearInReviewActiveDays);
        Grid.SetColumn(daysCard, 1);
        grid.Children.Add(daysCard);
        return grid;
    }

    private Control BuildNumberCard(string value, string label)
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 12, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("AppPrimaryTextBrush")
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = Brush("AppMutedTextBrush")
        });
        return panel;
    }

    private Control BuildMonthBars()
    {
        var months = _summary!.MonthlySeconds;
        var peak = months.Count == 0 ? 0d : months.Max();
        var bars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Height = 90
        };
        for (var month = 0; month < 12; month++)
        {
            var seconds = month < months.Count ? months[month] : 0d;
            var ratio = peak <= 0 ? 0d : Math.Clamp(seconds / peak, 0d, 1d);
            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month + 1);
            var column = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            column.Children.Add(new Border
            {
                Width = 26,
                Height = Math.Max(2d, 78d * ratio),
                CornerRadius = new CornerRadius(4),
                Background = Brush("AppAccentBrush"),
                VerticalAlignment = VerticalAlignment.Bottom
            });
            column.Children.Add(new TextBlock
            {
                Text = name.Length > 3 ? name[..3] : name,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Brush("AppMutedTextBrush")
            });
            bars.Children.Add(column);
        }
        return bars;
    }

    private void AddTopList(string title, IReadOnlyList<(string Label, double Seconds)> entries)
    {
        if (entries.Count == 0)
            return;

        var list = new StackPanel { Spacing = 6 };
        foreach (var (label, seconds) in entries)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush("AppPrimaryTextBrush")
            });
            var duration = new TextBlock
            {
                Text = TimeSpan.FromSeconds(seconds).ToString(@"h\:mm", CultureInfo.CurrentCulture),
                FontSize = 12,
                Foreground = Brush("AppMutedTextBrush")
            };
            Grid.SetColumn(duration, 1);
            row.Children.Add(duration);
            list.Children.Add(row);
        }
        _content.Children.Add(BuildSection(title, list));
    }

    private Control BuildSection(string title, Control content)
    {
        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("AppPrimaryTextBrush")
        });
        section.Children.Add(content);
        return section;
    }

    private static string FormatAlbum(TopAlbumStat album) =>
        string.IsNullOrWhiteSpace(album.Artist) ? album.Title : $"{album.Title} — {album.Artist}";

    private IBrush Brush(string key)
    {
        if (TryGetResource(key, ThemeVariant.Default, out var value) && value is IBrush brush)
            return brush;
        if (Application.Current?.TryGetResource(key, ThemeVariant.Default, out value) == true &&
            value is IBrush applicationBrush)
        {
            return applicationBrush;
        }
        return Brushes.Transparent;
    }
}
