using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

/// <summary>Dialog that browses directories on a remote Orynivo Server.</summary>
public partial class RemoteDirectoryBrowserDialog : Window
{
    private const string ParentEntryTag = "__parent__";

    private readonly OrynivoServerSettings _server;
    private string? _currentPath;
    private bool _showsDriveRoots;

    /// <summary>Initializes a design-time remote directory browser instance.</summary>
    public RemoteDirectoryBrowserDialog()
        : this(new OrynivoServerSettings())
    {
    }

    /// <summary>Initializes a remote directory browser for <paramref name="server"/>.</summary>
    /// <param name="server">Remote Orynivo Server connection settings.</param>
    public RemoteDirectoryBrowserDialog(OrynivoServerSettings server)
    {
        InitializeComponent();
        _server = server;
        Opened += async (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            await LoadDirectoryAsync(null);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close(false);
            if (e.Key == Key.Enter) SelectCurrentPath();
        };
    }

    /// <summary>Gets the selected server-side directory path after confirmation.</summary>
    public string SelectedPath { get; private set; } = string.Empty;

    private async Task LoadDirectoryAsync(string? path)
    {
        DirectoryListBox.IsEnabled = false;
        RootButton.IsEnabled = false;
        SelectButton.IsEnabled = false;
        StatusTextBlock.Text = LocalizationManager.Current.OrynivoServerDirectoryLoading;

        try
        {
            using var client = new OrynivoServerClient();
            var listing = await client.GetDirectoriesAsync(_server, path);
            if (listing is null)
            {
                StatusTextBlock.Text = LocalizationManager.Current.OrynivoServerDirectoryLoadFailed;
                return;
            }

            if (listing.IsRoot)
            {
                _showsDriveRoots = listing.Directories.Any(entry => IsWindowsDriveRoot(entry.Path));
                if (!_showsDriveRoots &&
                    listing.Directories.Count == 1 &&
                    string.Equals(listing.Directories[0].Path, "/", StringComparison.Ordinal))
                {
                    await LoadDirectoryAsync("/");
                    return;
                }
            }

            _currentPath = listing.IsRoot ? null : listing.Path;
            CurrentPathTextBox.Text = listing.IsRoot
                ? LocalizationManager.Current.OrynivoServerDirectoryRoots
                : listing.Path;
            DirectoryListBox.Items.Clear();
            TryGetResource("ServerDirectoryListItemTheme", ThemeVariant.Default, out var itemTheme);
            if (!string.IsNullOrWhiteSpace(_currentPath))
            {
                DirectoryListBox.Items.Add(new ListBoxItem
                {
                    Content = "..",
                    Theme = itemTheme as ControlTheme,
                    Tag = ParentEntryTag
                });
            }

            foreach (var entry in listing.Directories)
            {
                DirectoryListBox.Items.Add(new ListBoxItem
                {
                    Content = entry.HasChildren ? $"{entry.Name} >" : entry.Name,
                    Theme = itemTheme as ControlTheme,
                    Tag = entry
                });
            }

            StatusTextBlock.Text = listing.Directories.Count == 0
                ? LocalizationManager.Current.OrynivoServerDirectoryEmpty
                : string.Empty;
        }
        finally
        {
            DirectoryListBox.IsEnabled = true;
            RootButton.IsVisible = _showsDriveRoots;
            RootButton.IsEnabled = _showsDriveRoots;
            SelectButton.IsEnabled = !string.IsNullOrWhiteSpace(_currentPath);
        }
    }

    private async void RootButton_OnClick(object? sender, RoutedEventArgs e) =>
        await LoadDirectoryAsync(null);

    private async void DirectoryListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DirectoryListBox.SelectedItem is not ListBoxItem item)
            return;

        if (item.Tag is string tag && tag == ParentEntryTag)
        {
            await LoadDirectoryAsync(
                string.IsNullOrWhiteSpace(_currentPath)
                    ? null
                    : GetRemoteParentPath(_currentPath));
            return;
        }

        if (item.Tag is OrynivoDirectoryEntry entry)
            await LoadDirectoryAsync(entry.Path);
    }

    private void SelectButton_OnClick(object? sender, RoutedEventArgs e) =>
        SelectCurrentPath();

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) =>
        Close(false);

    private void SelectCurrentPath()
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
            return;

        SelectedPath = _currentPath;
        Close(true);
    }

    private static string? GetRemoteParentPath(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        if (trimmed.Length == 0 || trimmed == path[..1])
            return null;

        if (trimmed.Length == 2 && trimmed[1] == ':')
            return null;

        var slash = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        if (slash < 0)
            return null;

        if (slash == 0)
            return "/";

        if (slash == 2 && trimmed[1] == ':')
            return trimmed[..3];

        return trimmed[..slash];
    }

    private static bool IsWindowsDriveRoot(string path) =>
        path.Length >= 3 &&
        char.IsLetter(path[0]) &&
        path[1] == ':' &&
        (path[2] == '\\' || path[2] == '/');
}
