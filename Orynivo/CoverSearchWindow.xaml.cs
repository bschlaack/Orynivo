using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

public partial class CoverSearchWindow : Window
{
    private sealed record CoverResultViewModel(CoverSearchResult Result, Bitmap Image)
    {
        public string Title => Result.Title;
        public string? Artist => Result.Artist;
    }

    private readonly ObservableCollection<CoverResultViewModel> _results = [];
    private readonly DispatcherTimer _busyTimer;
    private readonly string[] _busyFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private int _busyFrameIndex;

    public CoverSearchResult? SelectedResult { get; private set; }

    /// <summary>
    /// Initializes a runtime-loader instance with an empty album query.
    /// </summary>
    public CoverSearchWindow()
        : this(string.Empty)
    {
    }

    public CoverSearchWindow(string albumTitle)
    {
        InitializeComponent();
        QueryTextBox.Text = albumTitle;
        ResultsListBox.ItemsSource = _results;
        _busyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _busyTimer.Tick += (_, _) =>
        {
            _busyFrameIndex = (_busyFrameIndex + 1) % _busyFrames.Length;
            BusyIndicatorTextBlock.Text = _busyFrames[_busyFrameIndex];
        };
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
        Loaded += async (_, _) => await SearchAsync(QueryTextBox.Text ?? string.Empty);
    }

    private async void SearchAgainButton_OnClick(object? sender, RoutedEventArgs e) =>
        await SearchAsync(QueryTextBox.Text ?? string.Empty);

    private async Task SearchAsync(string query)
    {
        _results.Clear();
        BusyIndicatorTextBlock.IsVisible = true;
        StatusTextBlock.Text = LocalizationManager.Current.CoverSearchRunning;
        _busyTimer.Start();
        try
        {
            var results = await MusicBrainzCoverSearch.SearchByAlbumTitleAsync(query);
            foreach (var result in results)
                _results.Add(new CoverResultViewModel(result, CreateBitmap(result.ImageData)));

            StatusTextBlock.Text = _results.Count == 0
                ? LocalizationManager.Current.CoverSearchNoResults
                : string.Empty;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = ex.Message;
        }
        finally
        {
            _busyTimer.Stop();
            BusyIndicatorTextBlock.IsVisible = false;
        }
    }

    private void UseSelectedCoverButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ResultsListBox.SelectedItem is not CoverResultViewModel selected)
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
