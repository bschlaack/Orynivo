using System.Runtime.InteropServices;
using System.Windows;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

public partial class PlexServerDialog : Window
{
    private readonly string _serverId;

    public PlexServerDialog(PlexServerSettings? server = null, string? token = null)
    {
        InitializeComponent();
        _serverId = server?.Id ?? Guid.NewGuid().ToString("N");
        NameTextBox.Text = server?.Name ?? string.Empty;
        UrlTextBox.Text = server?.BaseUrl ?? string.Empty;
        TokenPasswordBox.Password = token ?? string.Empty;
        Loaded += (_, _) => NameTextBox.Focus();
    }

    public PlexServerSettings Server { get; private set; } = new();
    public string Token => TokenPasswordBox.Password.Trim();

    private async void TestConnectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryCreateServer(out var server))
            return;

        TestConnectionButton.IsEnabled = false;
        StatusTextBlock.Text = LocalizationManager.Current.PlexTestingConnection;
        try
        {
            var libraries = await new PlexServerClient()
                .GetAudioLibrariesAsync(server, Token);
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.PlexConnectionSuccessful,
                libraries.Count);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.PlexConnectionFailed,
                ex.Message);
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryCreateServer(out var server))
            return;

        Server = server;
        DialogResult = true;
    }

    private bool TryCreateServer(out PlexServerSettings server)
    {
        server = new PlexServerSettings();
        var name = NameTextBox.Text.Trim();
        var url = UrlTextBox.Text.Trim();
        if (name.Length == 0 || url.Length == 0)
        {
            StatusTextBlock.Text = LocalizationManager.Current.PlexServerFieldsRequired;
            return false;
        }

        try
        {
            var normalizedUrl = PlexServerClient.NormalizeBaseUri(url).ToString().TrimEnd('/');
            server = new PlexServerSettings
            {
                Id = _serverId,
                Name = name,
                BaseUrl = normalizedUrl
            };
            return true;
        }
        catch (ArgumentException)
        {
            StatusTextBlock.Text = LocalizationManager.Current.PlexServerUrlInvalid;
            return false;
        }
    }

    private void PlexServerDialog_OnSourceInitialized(object sender, EventArgs e)
        => ApplyTitleBarColors();

    private void ApplyTitleBarColors()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            const int DwmwaCaptionColor = 35;
            const int DwmwaTextColor = 36;
            var dark = System.Windows.Application.Current.Resources["AppHeaderBrush"] is
                           System.Windows.Media.SolidColorBrush brush &&
                       brush.Color == System.Windows.Media.Color.FromRgb(0x13, 0x14, 0x2A);
            var captionColor = dark
                ? ColorRef(0x13, 0x14, 0x2A)
                : ColorRef(0xEA, 0xEA, 0xF5);
            var textColor = dark
                ? ColorRef(0xFF, 0xFF, 0xFF)
                : ColorRef(0x13, 0x14, 0x2A);
            _ = DwmSetWindowAttribute(
                handle,
                DwmwaCaptionColor,
                ref captionColor,
                sizeof(int));
            _ = DwmSetWindowAttribute(
                handle,
                DwmwaTextColor,
                ref textColor,
                sizeof(int));
        }
        catch
        {
        }
    }

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr handle,
        int attribute,
        ref int value,
        int valueSize);
}
