using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

/// <summary>
/// Dialog for adding or editing a remote Orynivo Server connection.
/// Returns <see langword="true"/> via <see cref="Window.Close(object?)"/> when the user saves.
/// </summary>
public partial class OrynivoServerDialog : Window
{
    private readonly string _serverId;
    private readonly List<string> _serverLibraryPaths = [];
    private CancellationTokenSource? _scanStatusCts;
    private bool _serverLibraryPathsLoaded;
    private bool _serverLibraryPathsChanged;

    /// <summary>Initializes a dialog for a new server.</summary>
    public OrynivoServerDialog()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a dialog pre-filled with an existing server's settings.
    /// </summary>
    /// <param name="server">Existing server to edit, or <see langword="null"/> for a new entry.</param>
    public OrynivoServerDialog(OrynivoServerSettings? server = null)
    {
        InitializeComponent();
        _serverId = server?.Id ?? Guid.NewGuid().ToString("N");
        NameTextBox.Text   = server?.Name   ?? string.Empty;
        UrlTextBox.Text    = server?.BaseUrl ?? string.Empty;
        ApiKeyTextBox.Text = server?.ApiKey  ?? string.Empty;
        RebuildServerDirectoryList();
        Opened += (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            NameTextBox.Focus();
            if (server is not null)
                _ = LoadServerLibraryPathsAsync();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close(false);
        };
        Closed += (_, _) => CancelScanStatusPolling();
    }

    /// <summary>Gets the confirmed server settings after the dialog closes with <see langword="true"/>.</summary>
    public OrynivoServerSettings Server { get; private set; } = new();

    private async void TestConnectionButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryCreateServer(out var server))
            return;

        TestConnectionButton.IsEnabled = false;
        StatusTextBlock.Text = LocalizationManager.Current.OrynivoTestingConnection;
        try
        {
            using var client = new OrynivoServerClient();
            var info = await client.TestConnectionAsync(server);
            StatusTextBlock.Text = info is not null
                ? string.Format(LocalizationManager.Current.OrynivoConnectionSuccessful, info.Name, info.Version)
                : LocalizationManager.Current.OrynivoConnectionFailed;
            if (info is not null)
                await LoadServerLibraryPathsAsync();
        }
        catch
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private async void LoadServerDirectoriesButton_OnClick(object? sender, RoutedEventArgs e) =>
        await LoadServerLibraryPathsAsync();

    private async void TriggerServerScanButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryCreateServer(out var server))
            return;

        TriggerServerScanButton.IsEnabled = false;
        StatusTextBlock.Text = LocalizationManager.Current.OrynivoServerScanStarting;
        try
        {
            using var client = new OrynivoServerClient();
            if (!await client.TriggerScanAsync(server))
            {
                StatusTextBlock.Text = LocalizationManager.Current.OrynivoServerScanStartFailed;
                return;
            }

            StartScanStatusPolling(server);
        }
        finally
        {
            TriggerServerScanButton.IsEnabled = true;
        }
    }

    private async void AddServerDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryCreateServer(out var server))
            return;

        var dialog = new RemoteDirectoryBrowserDialog(server);
        if (await dialog.ShowDialog<bool?>(this) != true)
            return;

        var selectedPath = dialog.SelectedPath;
        if (string.IsNullOrWhiteSpace(selectedPath) ||
            _serverLibraryPaths.Contains(selectedPath, StringComparer.OrdinalIgnoreCase))
            return;

        _serverLibraryPaths.Add(selectedPath);
        _serverLibraryPathsLoaded = true;
        _serverLibraryPathsChanged = true;
        RebuildServerDirectoryList();
    }

    private async void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryCreateServer(out var server))
            return;

        if (_serverLibraryPathsLoaded && _serverLibraryPathsChanged)
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoSavingServerDirectories;
            try
            {
                using var client = new OrynivoServerClient();
                var saved = await client.SetLibraryPathsAsync(server, _serverLibraryPaths);
                if (!saved)
                {
                    StatusTextBlock.Text = LocalizationManager.Current.OrynivoServerDirectoriesSaveFailed;
                    return;
                }
                StartScanStatusPolling(server);
            }
            catch
            {
                StatusTextBlock.Text = LocalizationManager.Current.OrynivoServerDirectoriesSaveFailed;
                return;
            }
        }

        Server = server;
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private bool TryCreateServer(out OrynivoServerSettings server)
    {
        server = new OrynivoServerSettings();
        var name   = NameTextBox.Text?.Trim()   ?? string.Empty;
        var url    = UrlTextBox.Text?.Trim()    ?? string.Empty;
        var apiKey = ApiKeyTextBox.Text?.Trim() ?? string.Empty;

        if (name.Length == 0 || url.Length == 0 || apiKey.Length == 0)
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoServerFieldsRequired;
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoServerFieldsRequired;
            return false;
        }

        server = new OrynivoServerSettings
        {
            Id      = _serverId,
            Name    = name,
            BaseUrl = url.TrimEnd('/'),
            ApiKey  = apiKey
        };
        return true;
    }

    private async Task LoadServerLibraryPathsAsync()
    {
        if (!TryCreateServer(out var server))
            return;

        LoadServerDirectoriesButton.IsEnabled = false;
        AddServerDirectoryButton.IsEnabled = false;
        StatusTextBlock.Text = LocalizationManager.Current.OrynivoLoadingServerDirectories;
        try
        {
            using var client = new OrynivoServerClient();
            var paths = await client.GetLibraryPathsAsync(server);
            _serverLibraryPaths.Clear();
            _serverLibraryPaths.AddRange(paths);
            _serverLibraryPathsLoaded = true;
            _serverLibraryPathsChanged = false;
            RebuildServerDirectoryList();
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoServerDirectoriesLoaded;
            StartScanStatusPolling(server);
        }
        catch
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoServerDirectoriesLoadFailed;
        }
        finally
        {
            LoadServerDirectoriesButton.IsEnabled = true;
            AddServerDirectoryButton.IsEnabled = true;
        }
    }

    private void StartScanStatusPolling(OrynivoServerSettings server)
    {
        CancelScanStatusPolling();
        _scanStatusCts = new CancellationTokenSource();
        var token = _scanStatusCts.Token;
        _ = PollScanStatusAsync(server, token);
    }

    private void CancelScanStatusPolling()
    {
        _scanStatusCts?.Cancel();
        _scanStatusCts?.Dispose();
        _scanStatusCts = null;
    }

    private async Task PollScanStatusAsync(OrynivoServerSettings server, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new OrynivoServerClient();
            while (!cancellationToken.IsCancellationRequested)
            {
                var status = await client.GetScanStatusAsync(server, cancellationToken);
                if (status is not null)
                    UpdateScanStatus(status);

                if (status is { IsRunning: false })
                    break;

                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateScanStatus(OrynivoScanStatus status)
    {
        if (status.Total > 0)
        {
            ServerScanProgressBar.IsIndeterminate = false;
            ServerScanProgressBar.Value = Math.Clamp(status.Current * 100.0 / status.Total, 0, 100);
        }
        else
        {
            ServerScanProgressBar.IsIndeterminate = status.IsRunning;
            ServerScanProgressBar.Value = 0;
        }

        if (!string.IsNullOrWhiteSpace(status.Error))
        {
            ServerScanStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.OrynivoServerScanFailed,
                status.Error);
            return;
        }

        if (status.IsRunning)
        {
            ServerScanStatusTextBlock.Text = status.Total > 0
                ? string.Format(
                    LocalizationManager.Current.OrynivoServerScanProgress,
                    status.Current,
                    status.Total,
                    Path.GetFileName(status.CurrentFile) is { Length: > 0 } name ? name : status.Path ?? string.Empty)
                : string.Format(
                    LocalizationManager.Current.OrynivoServerScanDiscovering,
                    status.Path ?? status.CurrentFile ?? string.Empty);
            return;
        }

        if (status.LastResult is { } result)
        {
            ServerScanStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.OrynivoServerScanCompleted,
                result.Total,
                result.Added,
                result.Updated,
                result.Removed,
                result.Failed);
        }
        else
        {
            ServerScanStatusTextBlock.Text = LocalizationManager.Current.OrynivoServerScanIdle;
        }
    }

    private void RebuildServerDirectoryList()
    {
        ServerDirectoriesPanel.Children.Clear();
        if (_serverLibraryPaths.Count == 0)
        {
            ServerDirectoriesPanel.Children.Add(new TextBlock
            {
                Text = LocalizationManager.Current.OrynivoNoServerDirectories,
                Foreground = Brushes.Gray,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var path in _serverLibraryPaths)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = path,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 8, 0)
            };
            ToolTip.SetTip(text, path);
            row.Children.Add(text);

            TryGetResource("ServerDialogButtonTheme", ThemeVariant.Default, out var buttonTheme);
            var removeButton = new Button
            {
                Content = "×",
                Width = 28,
                Height = 26,
                Theme = buttonTheme as ControlTheme,
                Tag = path
            };
            removeButton.Click += RemoveServerDirectoryButton_OnClick;
            ToolTip.SetTip(removeButton, LocalizationManager.Current.RemoveDirectory);
            Grid.SetColumn(removeButton, 1);
            row.Children.Add(removeButton);

            ServerDirectoriesPanel.Children.Add(row);
        }
    }

    private void RemoveServerDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path })
            return;

        _serverLibraryPaths.Remove(path);
        _serverLibraryPathsLoaded = true;
        _serverLibraryPathsChanged = true;
        RebuildServerDirectoryList();
    }
}
