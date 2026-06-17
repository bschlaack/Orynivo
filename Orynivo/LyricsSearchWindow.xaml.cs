using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

public partial class LyricsSearchWindow : Window
{
    private sealed record ResultViewModel(LyricsSearchResult Result)
    {
        public string TrackName => Result.TrackName;
        public string ArtistName => Result.ArtistName;
        public string Details
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Result.AlbumName))
                    parts.Add(Result.AlbumName);
                if (Result.Duration is > 0)
                    parts.Add(TimeSpan.FromSeconds(Result.Duration.Value).ToString(@"m\:ss"));
                if (!string.IsNullOrWhiteSpace(Result.SyncedLyrics))
                    parts.Add(LocalizationManager.Current.SynchronizedLyrics);
                return string.Join(" · ", parts);
            }
        }
    }

    private readonly ObservableCollection<ResultViewModel> _results = [];
    private readonly DispatcherTimer _busyTimer;
    private readonly string[] _busyFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private int _busyFrameIndex;

    public LyricsSearchResult? SelectedResult { get; private set; }

    /// <summary>
    /// Initializes a runtime-loader instance with empty lyrics search fields.
    /// </summary>
    public LyricsSearchWindow()
        : this(string.Empty, string.Empty)
    {
    }

    public LyricsSearchWindow(string? trackName, string? artistName)
    {
        InitializeComponent();
        TrackNameTextBox.Text = trackName ?? string.Empty;
        ArtistNameTextBox.Text = artistName ?? string.Empty;
        ResultsListBox.ItemsSource = _results;
        _busyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _busyTimer.Tick += (_, _) =>
        {
            _busyFrameIndex = (_busyFrameIndex + 1) % _busyFrames.Length;
            BusyIndicatorTextBlock.Text = _busyFrames[_busyFrameIndex];
        };
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
        Loaded += async (_, _) => await SearchAsync();
    }

    private async void SearchAgainButton_OnClick(object? sender, RoutedEventArgs e) =>
        await SearchAsync();

    private async Task SearchAsync()
    {
        _results.Clear();
        PreviewTextBlock.Text = LocalizationManager.Current.SelectLyricsResult;
        StatusTextBlock.Text = LocalizationManager.Current.LyricsSearchRunning;
        BusyIndicatorTextBlock.IsVisible = true;
        _busyTimer.Start();
        try
        {
            var results = await LyricsService.SearchAsync(
                TrackNameTextBox.Text ?? string.Empty,
                ArtistNameTextBox.Text ?? string.Empty);
            foreach (var result in results)
                _results.Add(new ResultViewModel(result));
            StatusTextBlock.Text = _results.Count == 0
                ? LocalizationManager.Current.LyricsSearchNoResults
                : string.Empty;
        }
        catch
        {
            StatusTextBlock.Text = LocalizationManager.Current.LyricsSearchFailed;
        }
        finally
        {
            _busyTimer.Stop();
            BusyIndicatorTextBlock.IsVisible = false;
        }
    }

    private void ResultsListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ResultsListBox.SelectedItem is not ResultViewModel selected)
            return;
        PreviewTextBlock.Text =
            selected.Result.SyncedLyrics ??
            selected.Result.PlainLyrics ??
            LocalizationManager.Current.LyricsSearchNoResults;
    }

    private void UseSelectedLyricsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ResultsListBox.SelectedItem is not ResultViewModel selected)
            return;
        SelectedResult = selected.Result;
        Close(true);
    }
}
