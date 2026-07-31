using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>
/// Dialog for manually searching Fanart.tv first and Wikimedia Commons as a fallback,
/// using an editable artist query.
/// </summary>
public partial class ArtistImageSearchWindow : Window
{
    private sealed record ResultViewModel(
        ArtistImageDownload Result,
        Bitmap Image,
        string Title,
        string? Attribution,
        string? License)
    {
    }

    private readonly ObservableCollection<ResultViewModel> _results = [];
    private readonly DispatcherTimer _busyTimer;
    private readonly string[] _busyFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private readonly string _fanartTvApiKey;
    private int _busyFrameIndex;

    /// <summary>Gets the image selected by the user, or <see langword="null"/>.</summary>
    public ArtistImageDownload? SelectedResult { get; private set; }

    /// <summary>
    /// Initializes a runtime-loader instance with an empty artist query.
    /// </summary>
    public ArtistImageSearchWindow()
        : this(string.Empty, null)
    {
    }

    /// <summary>Initializes a new artist-image search dialog with an editable initial query.</summary>
    /// <param name="artistName">Initial artist-image search query.</param>
    /// <param name="fanartTvApiKey">Optional Fanart.tv API key used before the Wikimedia fallback.</param>
    public ArtistImageSearchWindow(string artistName, string? fanartTvApiKey)
    {
        InitializeComponent();
        _fanartTvApiKey = fanartTvApiKey?.Trim() ?? string.Empty;
        QueryTextBox.Text = artistName;
        ResultsListBox.ItemsSource = _results;
        _busyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _busyTimer.Tick += (_, _) =>
        {
            _busyFrameIndex = (_busyFrameIndex + 1) % _busyFrames.Length;
            BusyIndicatorTextBlock.Text = _busyFrames[_busyFrameIndex];
        };
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
        Loaded += async (_, _) =>
        {
            QueryTextBox.Focus();
            QueryTextBox.SelectAll();
            await SearchAsync();
        };
        QueryTextBox.KeyDown += QueryTextBox_OnKeyDown;
    }

    private async void SearchAgainButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await SearchAsync();

    private async void QueryTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        _results.Clear();
        BusyIndicatorTextBlock.IsVisible = true;
        StatusTextBlock.Text = LocalizationManager.Current.ArtistImageSearchRunning;
        _busyTimer.Start();
        try
        {
            var query = QueryTextBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_fanartTvApiKey))
            {
                try
                {
                    var fanartResult = await FanartTvArtistImageService.FindBestAsync(
                        query,
                        null,
                        _fanartTvApiKey);
                    if (fanartResult is not null)
                    {
                        _results.Add(new ResultViewModel(
                            fanartResult,
                            CreateBitmap(fanartResult.ImageData),
                            query,
                            "Fanart.tv",
                            null));
                        StatusTextBlock.Text = string.Empty;
                        return;
                    }
                }
                catch
                {
                    // Fanart.tv is preferred, but a provider failure must not prevent the Wikimedia fallback.
                }
            }

            var results = await ArtistImageSearchService.SearchAsync(query);
            foreach (var result in results)
            {
                _results.Add(new ResultViewModel(
                    new ArtistImageDownload(result.ImageData, result.MimeType, result.SourceUrl),
                    CreateBitmap(result.ImageData),
                    result.Title,
                    result.Attribution ?? "Wikimedia Commons",
                    result.License));
            }

            StatusTextBlock.Text = _results.Count == 0
                ? LocalizationManager.Current.ArtistImageSearchNoResults
                : string.Empty;
        }
        catch
        {
            StatusTextBlock.Text = LocalizationManager.Current.ArtistImageSearchFailed;
        }
        finally
        {
            _busyTimer.Stop();
            BusyIndicatorTextBlock.IsVisible = false;
        }
    }

    private void UseSelectedImageButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ResultsListBox.SelectedItem is not ResultViewModel selected)
            return;

        SelectedResult = selected.Result;
        Close(true);
    }

    private static Bitmap CreateBitmap(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return new Bitmap(stream);
    }
}
