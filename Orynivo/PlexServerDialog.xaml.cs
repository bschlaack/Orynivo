using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

public partial class PlexServerDialog : Window
{
    private readonly string _serverId;

    /// <summary>
    /// Initializes a runtime-loader instance for a new Plex server.
    /// </summary>
    public PlexServerDialog()
        : this(null, null)
    {
    }

    public PlexServerDialog(PlexServerSettings? server = null, string? token = null)
    {
        InitializeComponent();
        _serverId = server?.Id ?? Guid.NewGuid().ToString("N");
        NameTextBox.Text = server?.Name ?? string.Empty;
        UrlTextBox.Text = server?.BaseUrl ?? string.Empty;
        TokenTextBox.Text = token ?? string.Empty;
        Opened += (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            NameTextBox.Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close(false);
        };
    }

    public PlexServerSettings Server { get; private set; } = new();
    public string Token => TokenTextBox.Text?.Trim() ?? string.Empty;

    private async void TestConnectionButton_OnClick(object? sender, RoutedEventArgs e)
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

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryCreateServer(out var server))
            return;

        Server = server;
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private bool TryCreateServer(out PlexServerSettings server)
    {
        server = new PlexServerSettings();
        var name = NameTextBox.Text?.Trim() ?? string.Empty;
        var url = UrlTextBox.Text?.Trim() ?? string.Empty;
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
}
