using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

public partial class ArtistImageSearchWindow : Window
{
    private sealed record ResultViewModel(ArtistImageSearchResult Result, BitmapImage Image)
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
        Loaded += ArtistImageSearchWindow_OnLoaded;
    }

    private async void ArtistImageSearchWindow_OnLoaded(object sender, RoutedEventArgs e) =>
        await SearchAsync();

    private async void SearchAgainButton_OnClick(object sender, RoutedEventArgs e) =>
        await SearchAsync();

    private async Task SearchAsync()
    {
        _results.Clear();
        BusyIndicatorTextBlock.Visibility = Visibility.Visible;
        StatusTextBlock.Text = LocalizationManager.Current.ArtistImageSearchRunning;
        _busyTimer.Start();
        try
        {
            var results = await ArtistImageSearchService.SearchAsync(QueryTextBox.Text);
            foreach (var result in results)
                _results.Add(new ResultViewModel(result, CreateImage(result.ImageData)));

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
            BusyIndicatorTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    private void UseSelectedImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResultsListBox.SelectedItem is not ResultViewModel selected)
            return;

        SelectedResult = selected.Result;
        DialogResult = true;
    }

    private void ArtistImageSearchWindow_OnSourceInitialized(object sender, EventArgs e)
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

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
