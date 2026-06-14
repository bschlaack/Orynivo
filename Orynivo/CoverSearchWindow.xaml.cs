using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

public partial class CoverSearchWindow : Window
{
    private sealed record CoverResultViewModel(CoverSearchResult Result, BitmapImage Image)
    {
        public string Title => Result.Title;
        public string? Artist => Result.Artist;
    }

    private readonly string _albumTitle;
    private readonly ObservableCollection<CoverResultViewModel> _results = [];
    private readonly DispatcherTimer _busyTimer;
    private readonly string[] _busyFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private int _busyFrameIndex;

    public CoverSearchResult? SelectedResult { get; private set; }

    public CoverSearchWindow(string albumTitle)
    {
        InitializeComponent();
        _albumTitle = albumTitle;
        QueryTextBox.Text = albumTitle;
        ResultsListBox.ItemsSource = _results;
        _busyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _busyTimer.Tick += (_, _) =>
        {
            _busyFrameIndex = (_busyFrameIndex + 1) % _busyFrames.Length;
            BusyIndicatorTextBlock.Text = _busyFrames[_busyFrameIndex];
        };
        Loaded += CoverSearchWindow_OnLoaded;
    }

    private async void CoverSearchWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await SearchAsync(QueryTextBox.Text);
    }

    private async void SearchAgainButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SearchAsync(QueryTextBox.Text);
    }

    private async Task SearchAsync(string query)
    {
        _results.Clear();
        BusyIndicatorTextBlock.Visibility = Visibility.Visible;
        StatusTextBlock.Text = LocalizationManager.Current.CoverSearchRunning;
        _busyTimer.Start();
        try
        {
            var results = await MusicBrainzCoverSearch.SearchByAlbumTitleAsync(query);
            foreach (var result in results)
                _results.Add(new CoverResultViewModel(result, CreateImage(result.ImageData)));

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
            BusyIndicatorTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    private void UseSelectedCoverButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResultsListBox.SelectedItem is not CoverResultViewModel selected)
            return;

        SelectedResult = selected.Result;
        DialogResult = true;
    }

    private void CoverSearchWindow_OnSourceInitialized(object sender, EventArgs e)
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            const int DwmwaCaptionColor = 35;
            const int DwmwaTextColor = 36;
            var dark = System.Windows.Application.Current.Resources["AppHeaderBrush"] is System.Windows.Media.SolidColorBrush b &&
                       b.Color == System.Windows.Media.Color.FromRgb(0x13, 0x14, 0x2A);
            var captionColor = dark ? ColorRef(0x13, 0x14, 0x2A) : ColorRef(0xEA, 0xEA, 0xF5);
            var textColor = dark ? ColorRef(0xFF, 0xFF, 0xFF) : ColorRef(0x13, 0x14, 0x2A);
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch { }
    }

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private static BitmapImage CreateImage(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
