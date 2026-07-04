using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Dialog for manually searching and selecting a Wikimedia artist image.</summary>
public partial class ArtistImageSearchWindow : Window
{
    private sealed record ResultViewModel(ArtistImageSearchResult Result, Bitmap Image)
    {
        public string Title => Result.Title;
        public string? Attribution => Result.Attribution;
        public string? License => Result.License;
    }

    private readonly ObservableCollection<ResultViewModel> _results = [];
    private readonly DispatcherTimer _busyTimer;
    private readonly string[] _busyFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private int _busyFrameIndex;

    public ArtistImageSearchResult? SelectedResult { get; private set; }

    /// <summary>
    /// Initializes a runtime-loader instance with an empty artist query.
    /// </summary>
    public ArtistImageSearchWindow()
        : this(string.Empty)
    {
    }

    /// <summary>Initializes a new artist-image search dialog with an editable initial query.</summary>
    /// <param name="artistName">Initial artist-image search query.</param>
    public ArtistImageSearchWindow(string artistName)
    {
        InitializeComponent();
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
            var results = await ArtistImageSearchService.SearchAsync(QueryTextBox.Text ?? string.Empty);
            foreach (var result in results)
                _results.Add(new ResultViewModel(result, CreateBitmap(result.ImageData)));

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
