using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
        Loaded += LyricsSearchWindow_OnLoaded;
    }

    private async void LyricsSearchWindow_OnLoaded(object sender, RoutedEventArgs e) =>
        await SearchAsync();

    private async void SearchAgainButton_OnClick(object sender, RoutedEventArgs e) =>
        await SearchAsync();

    private async Task SearchAsync()
    {
        _results.Clear();
        PreviewTextBlock.Text = LocalizationManager.Current.SelectLyricsResult;
        StatusTextBlock.Text = LocalizationManager.Current.LyricsSearchRunning;
        BusyIndicatorTextBlock.Visibility = Visibility.Visible;
        _busyTimer.Start();
        try
        {
            var results = await LyricsService.SearchAsync(
                TrackNameTextBox.Text,
                ArtistNameTextBox.Text);
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
            BusyIndicatorTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    private void ResultsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsListBox.SelectedItem is not ResultViewModel selected)
            return;
        PreviewTextBlock.Text =
            selected.Result.SyncedLyrics ??
            selected.Result.PlainLyrics ??
            LocalizationManager.Current.LyricsSearchNoResults;
    }

    private void UseSelectedLyricsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResultsListBox.SelectedItem is not ResultViewModel selected)
            return;
        SelectedResult = selected.Result;
        DialogResult = true;
    }

    private void LyricsSearchWindow_OnSourceInitialized(object sender, EventArgs e)
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;
            const int DwmwaCaptionColor = 35;
            const int DwmwaTextColor = 36;
            var dark = System.Windows.Application.Current.Resources["AppHeaderBrush"] is System.Windows.Media.SolidColorBrush brush &&
                       brush.Color == System.Windows.Media.Color.FromRgb(0x13, 0x14, 0x2A);
            var captionColor = dark ? ColorRef(0x13, 0x14, 0x2A) : ColorRef(0xEA, 0xEA, 0xF5);
            var textColor = dark ? ColorRef(0xFF, 0xFF, 0xFF) : ColorRef(0x13, 0x14, 0x2A);
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch
        {
        }
    }

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
