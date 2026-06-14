using System.Runtime.InteropServices;
using System.Windows;
using Orynivo.Localization;

namespace Orynivo;

public partial class ArtistMergeDialog : Window
{
    private readonly long _currentArtistId;
    private readonly long _matchingArtistId;

    public long PreferredArtistId { get; private set; }

    public ArtistMergeDialog(
        long currentArtistId,
        string currentArtistName,
        long matchingArtistId,
        string matchingArtistName)
    {
        InitializeComponent();
        _currentArtistId = currentArtistId;
        _matchingArtistId = matchingArtistId;
        MessageTextBlock.Text = string.Format(
            LocalizationManager.Current.ArtistNameExistsMessage,
            matchingArtistName);
        CurrentArtistButton.Content = string.Format(
            LocalizationManager.Current.KeepArtistProfile,
            currentArtistName);
        MatchingArtistButton.Content = string.Format(
            LocalizationManager.Current.KeepArtistProfile,
            matchingArtistName);
    }

    private void CurrentArtistButton_OnClick(object sender, RoutedEventArgs e)
    {
        PreferredArtistId = _currentArtistId;
        DialogResult = true;
    }

    private void MatchingArtistButton_OnClick(object sender, RoutedEventArgs e)
    {
        PreferredArtistId = _matchingArtistId;
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
    private void ArtistMergeDialog_OnSourceInitialized(object sender, EventArgs e) =>
        ApplyTitleBarColors();

    private void ApplyTitleBarColors()
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
