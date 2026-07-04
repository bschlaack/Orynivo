using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Dialog for manually searching and selecting album cover artwork.</summary>
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
        : this(string.Empty, null)
    {
    }

    /// <summary>Initializes a new cover-search dialog with editable album and artist queries.</summary>
    /// <param name="albumTitle">Initial album title query.</param>
    /// <param name="artistName">Initial artist query, or <see langword="null"/>.</param>
    public CoverSearchWindow(string albumTitle, string? artistName = null)
    {
        InitializeComponent();
        QueryTextBox.Text = albumTitle;
        ArtistQueryTextBox.Text = artistName;
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
        QueryTextBox.KeyDown += SearchTextBox_OnKeyDown;
        ArtistQueryTextBox.KeyDown += SearchTextBox_OnKeyDown;
    }

    private async void SearchAgainButton_OnClick(object? sender, RoutedEventArgs e) =>
        await SearchAsync();

    private async void SearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
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
        StatusTextBlock.Text = LocalizationManager.Current.CoverSearchRunning;
        _busyTimer.Start();
        try
        {
            var results = await MusicBrainzCoverSearch.SearchByAlbumTitleAsync(
                QueryTextBox.Text ?? string.Empty,
                ArtistQueryTextBox.Text);
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
