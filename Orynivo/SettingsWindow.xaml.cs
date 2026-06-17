using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Orynivo.Audio;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;
using AvaloniaApp = Avalonia.Application;
using UiLanguage = Orynivo.Localization.Language;

namespace Orynivo;

public partial class SettingsWindow : Window
{
    private sealed record SettingChoice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppSettings _settings;
    private readonly List<string> _libraryPaths = [];
    private readonly List<PlexServerSettings> _plexServers = [];
    private readonly Dictionary<string, string> _plexTokens = [];
    private readonly Dictionary<string, CancellationTokenSource> _activeScans = [];
    private readonly Action<List<string>>? _onLibraryPathsChanged;
    private readonly bool _steinbergAsioAvailable = SteinbergAsioStream.IsAvailable;
    private readonly bool _cwAsioAvailable = SteinbergAsioStream.IsCwAsioAvailable;

    /// <summary>
    /// Initializes a runtime-loader instance with default settings.
    /// </summary>
    public SettingsWindow()
        : this(new AppSettings())
    {
    }

    public SettingsWindow(AppSettings settings, Action<List<string>>? onLibraryPathsChanged = null)
    {
        InitializeComponent();
        _settings = settings;
        _onLibraryPathsChanged = onLibraryPathsChanged;
        var availableBackends = new[]
        {
            new SettingChoice<OutputBackend>(OutputBackend.Asio, LocalizationManager.Current.SteinbergAsio),
            new SettingChoice<OutputBackend>(OutputBackend.CwAsio, LocalizationManager.Current.CwAsio),
            new SettingChoice<OutputBackend>(OutputBackend.Wasapi, "WASAPI"),
            new SettingChoice<OutputBackend>(OutputBackend.KernelStreaming, "Kernel Streaming")
        }
            .Where(choice => choice.Value != OutputBackend.Asio || _steinbergAsioAvailable)
            .Where(choice => choice.Value != OutputBackend.CwAsio || _cwAsioAvailable)
            .ToArray();
        OutputBackendComboBox.ItemsSource = availableBackends;
        OutputBackendComboBox.SelectedItem =
            availableBackends.FirstOrDefault(choice => choice.Value == settings.OutputBackend)
            ?? availableBackends.First(choice => choice.Value == OutputBackend.Wasapi);
        var themeChoices = new[]
        {
            new SettingChoice<AppTheme>(AppTheme.Light, LocalizationManager.Current.ThemeLight),
            new SettingChoice<AppTheme>(AppTheme.Dark, LocalizationManager.Current.ThemeDark)
        };
        ThemeComboBox.ItemsSource = themeChoices;
        ThemeComboBox.SelectedItem = themeChoices.First(choice => choice.Value == settings.Theme);
        var languageChoices = new[]
        {
            new SettingChoice<UiLanguage>(UiLanguage.German, LocalizationManager.Current.LanguageGerman),
            new SettingChoice<UiLanguage>(UiLanguage.English, LocalizationManager.Current.LanguageEnglish),
            new SettingChoice<UiLanguage>(UiLanguage.French, LocalizationManager.Current.LanguageFrench),
            new SettingChoice<UiLanguage>(UiLanguage.Spanish, LocalizationManager.Current.LanguageSpanish)
        };
        LanguageComboBox.ItemsSource = languageChoices;
        LanguageComboBox.SelectedItem = languageChoices.First(choice => choice.Value == settings.Language);
        ShowLocalLibrarySectionCheckBox.IsChecked = settings.ShowLocalLibrarySection;
        ShowOwnRadiosSectionCheckBox.IsChecked = settings.ShowOwnRadiosSection;
        ShowMyPodcastsSectionCheckBox.IsChecked = settings.ShowMyPodcastsSection;
        ShowPlexSectionCheckBox.IsChecked = settings.ShowPlexSection;
        ShowPlaylistsSectionCheckBox.IsChecked = settings.ShowPlaylistsSection;
        ArtistInfoSourceComboBox.ItemsSource = Enum.GetValues<ArtistInfoSource>();
        ArtistInfoSourceComboBox.SelectedItem = settings.ArtistInfoSource;
        LastFmApiKeyTextBox.Text = settings.LastFmApiKey ?? string.Empty;
        QobuzApplicationIdTextBox.Text = settings.QobuzApplicationId ?? string.Empty;
        LastFmPanel.IsVisible = settings.ArtistInfoSource == ArtistInfoSource.LastFm;
        _libraryPaths.AddRange(settings.LibraryPaths);
        _plexServers.AddRange((settings.PlexServers ?? []).Select(ClonePlexServer));
        try
        {
            foreach (var credential in new WindowsPlexCredentialStore().LoadAll())
                _plexTokens[credential.Key] = credential.Value;
        }
        catch { }
        RebuildDirectoryList();
        RebuildPlexServerList();
        LoadDrivers();
        NavListBox.SelectedIndex = 1;
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
    }

    public string? SelectedDriverName => DriverComboBox.SelectedItem as string;
    public string? SelectedWasapiDeviceId =>
        DriverComboBox.SelectedItem is WasapiDeviceInfo device ? device.Id : null;
    public string? SelectedWasapiDeviceName =>
        DriverComboBox.SelectedItem is WasapiDeviceInfo device ? device.Name : null;
    public OutputBackend SelectedOutputBackend =>
        OutputBackendComboBox.SelectedItem is SettingChoice<OutputBackend> choice
            ? choice.Value
            : OutputBackend.Wasapi;
    public IReadOnlyList<string> SelectedLibraryPaths => _libraryPaths.AsReadOnly();
    public AppTheme SelectedTheme =>
        ThemeComboBox.SelectedItem is SettingChoice<AppTheme> theme ? theme.Value : AppTheme.Dark;
    public UiLanguage SelectedLanguage =>
        LanguageComboBox.SelectedItem is SettingChoice<UiLanguage> language ? language.Value : UiLanguage.German;
    public ArtistInfoSource SelectedArtistInfoSource =>
        ArtistInfoSourceComboBox.SelectedItem is ArtistInfoSource src ? src : ArtistInfoSource.Wikipedia;
    public string SelectedLastFmApiKey => LastFmApiKeyTextBox.Text?.Trim() ?? string.Empty;
    public string SelectedQobuzApplicationId => QobuzApplicationIdTextBox.Text?.Trim() ?? string.Empty;
    public IReadOnlyList<PlexServerSettings> SelectedPlexServers =>
        _plexServers.Select(ClonePlexServer).ToList().AsReadOnly();
    public IReadOnlyDictionary<string, string> SelectedPlexTokens => _plexTokens;
    public bool ShowLocalLibrarySection => ShowLocalLibrarySectionCheckBox.IsChecked == true;
    public bool ShowOwnRadiosSection => ShowOwnRadiosSectionCheckBox.IsChecked == true;
    public bool ShowMyPodcastsSection => ShowMyPodcastsSectionCheckBox.IsChecked == true;
    public bool ShowPlexSection => ShowPlexSectionCheckBox.IsChecked == true;
    public bool ShowPlaylistsSection => ShowPlaylistsSectionCheckBox.IsChecked == true;

    private static PlexServerSettings ClonePlexServer(PlexServerSettings server) => new()
    {
        Id = server.Id,
        Name = server.Name,
        BaseUrl = server.BaseUrl
    };

    private Button CreateStyledButton(string content, double width, double height, Thickness margin = default)
    {
        var btn = new Button
        {
            Content = content,
            Width = width,
            Height = height,
            Margin = margin
        };
        if (TryGetResource("SettingsButtonTheme", ThemeVariant.Default, out var res) && res is ControlTheme theme)
            btn.Theme = theme;
        return btn;
    }

    private void RebuildPlexServerList()
    {
        PlexServersPanel.Children.Clear();
        var primaryBrush = AvaloniaApp.Current!.Resources["AppPrimaryTextBrush"] as IBrush;
        var mutedBrush = AvaloniaApp.Current!.Resources["AppMutedTextBrush"] as IBrush;
        foreach (var server in _plexServers)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var description = new StackPanel();
            description.Children.Add(new TextBlock
            {
                Text = server.Name,
                FontWeight = FontWeight.SemiBold,
                Foreground = primaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            description.Children.Add(new TextBlock
            {
                Text = server.BaseUrl,
                FontSize = 11,
                Foreground = mutedBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(description);

            var editButton = CreateStyledButton(LocalizationManager.Current.PlexEditServer, 80, 28, new Thickness(8, 0, 0, 0));
            editButton.Tag = server.Id;
            editButton.Click += EditPlexServerButton_OnClick;
            Grid.SetColumn(editButton, 1);
            row.Children.Add(editButton);

            var removeButton = CreateStyledButton(LocalizationManager.Current.PlexRemoveServer, 80, 28, new Thickness(8, 0, 0, 0));
            removeButton.Tag = server.Id;
            removeButton.Click += RemovePlexServerButton_OnClick;
            Grid.SetColumn(removeButton, 2);
            row.Children.Add(removeButton);

            PlexServersPanel.Children.Add(row);
        }
    }

    private async void AddPlexServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new PlexServerDialog();
        if (await dialog.ShowDialog<bool?>(this) != true)
            return;
        _plexServers.Add(dialog.Server);
        _plexTokens[dialog.Server.Id] = dialog.Token;
        RebuildPlexServerList();
    }

    private async void EditPlexServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
            return;
        var index = _plexServers.FindIndex(server => server.Id == id);
        if (index < 0)
            return;
        var dialog = new PlexServerDialog(_plexServers[index], _plexTokens.GetValueOrDefault(id));
        if (await dialog.ShowDialog<bool?>(this) != true)
            return;
        _plexServers[index] = dialog.Server;
        _plexTokens[id] = dialog.Token;
        RebuildPlexServerList();
    }

    private void RemovePlexServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
            return;
        _plexServers.RemoveAll(server => server.Id == id);
        _plexTokens.Remove(id);
        RebuildPlexServerList();
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var cts in _activeScans.Values)
            cts.Cancel();
        base.OnClosed(e);
    }

    // ------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------

    private void NavListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: string tag })
            return;
        AudioDevicePanel.IsVisible = tag == "AudioDevice";
        LibraryPanel.IsVisible     = tag == "Library";
        StreamingPanel.IsVisible   = tag == "Streaming";
        AppearancePanel.IsVisible  = tag == "Appearance";
        ArtistInfoPanel.IsVisible  = tag == "ArtistInfo";
    }

    private void ArtistInfoSourceComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        LastFmPanel.IsVisible = SelectedArtistInfoSource == ArtistInfoSource.LastFm;
    }

    // ------------------------------------------------------------------
    // Geräte
    // ------------------------------------------------------------------

    private void LoadDrivers()
    {
        try
        {
            if (SelectedOutputBackend == OutputBackend.Wasapi)
            {
                var devices = WasapiDeviceProvider.GetRenderDevices();
                DriverComboBox.ItemsSource = devices;
                DriverComboBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(WasapiDeviceInfo.Name));
                DriverComboBox.SelectedItem = devices.FirstOrDefault(device =>
                    string.Equals(device.Id, _settings.SelectedWasapiDeviceId, StringComparison.Ordinal))
                    ?? devices.FirstOrDefault();
                DeviceLabelTextBlock.Text = LocalizationManager.Current.WasapiOutputDevice;
                StatusTextBlock.Text = devices.Count == 0
                    ? LocalizationManager.Current.NoWasapiDevices
                    : LocalizationManager.Current.SelectAndSave;
            }
            else if (SelectedOutputBackend is OutputBackend.Asio or OutputBackend.CwAsio)
            {
                var drivers = SteinbergAsioStream.GetDriverNames(SelectedOutputBackend);
                DriverComboBox.ItemsSource = drivers;
                DriverComboBox.DisplayMemberBinding = null;
                DriverComboBox.SelectedItem = drivers.FirstOrDefault(name =>
                    string.Equals(name, _settings.SelectedDriverName, StringComparison.Ordinal))
                    ?? drivers.FirstOrDefault(name =>
                        name.Contains("TOPPING", StringComparison.OrdinalIgnoreCase))
                    ?? drivers.FirstOrDefault();
                DeviceLabelTextBlock.Text = SelectedOutputBackend == OutputBackend.CwAsio
                    ? LocalizationManager.Current.CwAsioOutputDevice
                    : LocalizationManager.Current.AsioOutputDevice;
                StatusTextBlock.Text = drivers.Count == 0
                    ? LocalizationManager.Current.NoAsioDrivers
                    : LocalizationManager.Current.SelectAndSave;
            }
            else
            {
                DriverComboBox.ItemsSource = null;
                DriverComboBox.DisplayMemberBinding = null;
                DeviceLabelTextBlock.Text = LocalizationManager.Current.OutputDevice;
                StatusTextBlock.Text = LocalizationManager.Current.KernelStreamingUnavailable;
            }
        }
        catch (DllNotFoundException)
        {
            StatusTextBlock.Text = LocalizationManager.Current.AsioBridgeMissing;
        }
    }

    private void DriverComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        DeviceInfoButton.IsVisible = DriverComboBox.SelectedItem is string or WasapiDeviceInfo;
    }

    private void OutputBackendComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var backend = SelectedOutputBackend;
        DriverComboBox.IsEnabled = backend != OutputBackend.KernelStreaming;
        DeviceInfoButton.IsEnabled =
            backend == OutputBackend.Wasapi ||
            (backend == OutputBackend.Asio && _steinbergAsioAvailable) ||
            (backend == OutputBackend.CwAsio && _cwAsioAvailable);
        LoadDrivers();
        if (backend == OutputBackend.KernelStreaming)
        {
            DriverComboBox.ItemsSource = null;
            StatusTextBlock.Text = LocalizationManager.Current.KernelStreamingUnavailable;
        }
    }

    private async void DeviceInfoButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DriverComboBox.SelectedItem is string driverName)
            {
                var info = SteinbergAsioStream.GetDeviceInfo(SelectedOutputBackend, driverName);
                await new DeviceInfoWindow(info).ShowDialog(this);
            }
            else if (DriverComboBox.SelectedItem is WasapiDeviceInfo wasapiDevice)
            {
                var info = WasapiDeviceProvider.GetCapabilities(wasapiDevice.Id);
                await new DeviceInfoWindow(info).ShowDialog(this);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.DeviceInfoFailed, ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Bibliothek
    // ------------------------------------------------------------------

    private async void AddDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this)!;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Current.AddMusicDirectory,
            AllowMultiple = false
        });
        if (folders.Count == 0)
            return;
        var path = folders[0].TryGetLocalPath() ?? string.Empty;
        if (string.IsNullOrEmpty(path) || _libraryPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            return;
        _libraryPaths.Add(path);
        RebuildDirectoryList();
        _onLibraryPathsChanged?.Invoke(_libraryPaths.ToList());
    }

    private void RebuildDirectoryList()
    {
        DirectoriesPanel.Children.Clear();
        foreach (var path in _libraryPaths)
            DirectoriesPanel.Children.Add(BuildDirectoryRow(path));
    }

    private static async Task RefreshCountAsync(string path, TextBlock countBlock)
    {
        try
        {
            var count = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.CountByDirectory(path);
            });
            countBlock.Text = LocalizationManager.FormatTrackCount(count);
        }
        catch
        {
            countBlock.Text = string.Empty;
        }
    }

    private Control BuildDirectoryRow(string path)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pathBlock = new TextBlock
        {
            Text = path,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0)
        };
        ToolTip.SetTip(pathBlock, path);
        Grid.SetColumn(pathBlock, 0);

        var countBlock = new TextBlock
        {
            Text = "…",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 10, 0)
        };
        ToolTip.SetTip(countBlock, LocalizationManager.Current.TrackCountTooltip);
        Grid.SetColumn(countBlock, 1);

        var scanBtn = CreateStyledButton(LocalizationManager.Current.Scan, 80, 26, new Thickness(0, 0, 4, 0));
        Grid.SetColumn(scanBtn, 2);

        var removeBtn = CreateStyledButton("×", 26, 26);
        removeBtn.FontSize = 14;
        ToolTip.SetTip(removeBtn, LocalizationManager.Current.RemoveDirectory);
        Grid.SetColumn(removeBtn, 3);

        grid.Children.Add(pathBlock);
        grid.Children.Add(countBlock);
        grid.Children.Add(scanBtn);
        grid.Children.Add(removeBtn);

        _ = RefreshCountAsync(path, countBlock);

        var statusBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            IsVisible = false
        };

        scanBtn.Click += async (_, _) =>
        {
            if (_activeScans.ContainsKey(path))
            {
                _activeScans[path].Cancel();
                return;
            }
            if (!Directory.Exists(path))
            {
                statusBlock.IsVisible = true;
                statusBlock.Text = LocalizationManager.Current.FolderNotFound;
                return;
            }
            var cts = new CancellationTokenSource();
            _activeScans[path] = cts;
            UpdateBackupButtonAvailability();
            scanBtn.Content = LocalizationManager.Current.Cancel;
            statusBlock.IsVisible = true;
            statusBlock.Text = LocalizationManager.Current.ScanRunning;
            var progress = new Progress<ScanProgress>(p =>
                statusBlock.Text = $"{p.Current}/{p.Total} – {Path.GetFileName(p.CurrentFile)}");
            try
            {
                var result = await LibraryScanner.ScanAsync(path, progress, cts.Token);
                var failed = result.Failed > 0
                    ? $" · {string.Format(LocalizationManager.Current.ScanFailed, result.Failed)}"
                    : string.Empty;
                statusBlock.Text = string.Format(
                    LocalizationManager.Current.ScanCompleted,
                    result.Total, result.Added, result.Updated, failed);
            }
            catch (OperationCanceledException)
            {
                statusBlock.Text = LocalizationManager.Current.ScanCanceled;
            }
            catch (Exception ex)
            {
                statusBlock.Text = string.Format(LocalizationManager.Current.ScanFailed, ex.Message);
            }
            finally
            {
                _activeScans.Remove(path);
                cts.Dispose();
                scanBtn.Content = LocalizationManager.Current.Scan;
                UpdateBackupButtonAvailability();
                _ = RefreshCountAsync(path, countBlock);
            }
        };

        removeBtn.Click += (_, _) =>
        {
            if (_activeScans.TryGetValue(path, out var cts))
            {
                cts.Cancel();
                _activeScans.Remove(path);
                UpdateBackupButtonAvailability();
            }
            _libraryPaths.Remove(path);
            RebuildDirectoryList();
            _onLibraryPathsChanged?.Invoke(_libraryPaths.ToList());
        };

        outer.Children.Add(grid);
        outer.Children.Add(statusBlock);
        return outer;
    }

    private async void OptimizeDatabaseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OptimizeDatabaseButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.DatabaseOptimizing;
        try
        {
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.Optimize();
            });
            DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.DatabaseOptimized;
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(LocalizationManager.Current.DatabaseOptimizeFailed, ex.Message);
        }
        finally
        {
            OptimizeDatabaseButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }

    private async void RepairAlbumArtworkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RepairAlbumArtworkButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.AlbumArtworkRepairing;
        var progress = new Progress<ScanProgress>(p =>
            DatabaseMaintenanceStatusTextBlock.Text =
                $"{LocalizationManager.Current.AlbumArtworkRepairing} {p.Current}/{p.Total} – {Path.GetFileName(p.CurrentFile)}");
        try
        {
            var repaired = await LibraryScanner.RepairMissingAlbumArtworkAsync(progress);
            DatabaseMaintenanceStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.AlbumArtworkRepaired, repaired);
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(LocalizationManager.Current.AlbumArtworkRepairFailed, ex.Message);
        }
        finally
        {
            RepairAlbumArtworkButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }

    private async void NormalizeArtistsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        NormalizeArtistsButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.ArtistsNormalizing;
        try
        {
            var result = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                var normalization = db.NormalizeArtists();
                TrackSearchIndex.Rebuild(db.GetAll().ToList());
                return normalization;
            });
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.ArtistsNormalized,
                result.MergedArtists,
                result.UpdatedTracks);
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.ArtistNormalizationFailed, ex.Message);
        }
        finally
        {
            NormalizeArtistsButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }

    private async void DownloadMissingArtworkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        DownloadMissingArtworkButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.MissingArtworkDownloading;
        var progress = new Progress<ScanProgress>(p =>
            DatabaseMaintenanceStatusTextBlock.Text =
                $"{LocalizationManager.Current.MissingArtworkDownloading} {p.Current}/{p.Total}");
        try
        {
            var downloaded = await LibraryScanner.DownloadMissingAlbumArtworkAsync(progress);
            DatabaseMaintenanceStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.MissingArtworkDownloaded, downloaded);
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.MissingArtworkDownloadFailed, ex.Message);
        }
        finally
        {
            DownloadMissingArtworkButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }

    private async void ExportLibraryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanStartLibraryBackupOperation())
            return;
        var topLevel = TopLevel.GetTopLevel(this)!;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Current.ExportLibrary,
            FileTypeChoices = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }],
            DefaultExtension = "zip",
            SuggestedFileName = $"Orynivo-library-{DateTime.Now:yyyyMMdd-HHmm}.zip"
        });
        if (file is null)
            return;
        var filePath = file.TryGetLocalPath() ?? string.Empty;
        if (string.IsNullOrEmpty(filePath))
            return;

        SetLibraryOperationControlsEnabled(false);
        LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryExporting;
        LibraryExportProgressBar.Value = 0;
        LibraryExportProgressBar.IsVisible = true;
        var progress = new Progress<LibraryExportProgress>(value =>
        {
            LibraryExportProgressBar.Value = value.Percentage;
            LibraryBackupStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.LibraryExportProgress,
                value.Percentage, value.CurrentFile ?? string.Empty);
        });
        try
        {
            await LibraryBackupService.ExportAsync(filePath, _libraryPaths, progress);
            LibraryExportProgressBar.Value = 100;
            LibraryBackupStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.LibraryExported, filePath);
        }
        catch (Exception ex)
        {
            LibraryExportProgressBar.IsVisible = false;
            LibraryBackupStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.LibraryExportFailed, ex.Message);
        }
        finally
        {
            SetLibraryOperationControlsEnabled(true);
        }
    }

    private async void ImportLibraryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanStartLibraryBackupOperation())
            return;
        var topLevel = TopLevel.GetTopLevel(this)!;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Current.ImportLibrary,
            FileTypeFilter = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }],
            AllowMultiple = false
        });
        if (files.Count == 0)
            return;
        var filePath = files[0].TryGetLocalPath() ?? string.Empty;
        if (string.IsNullOrEmpty(filePath))
            return;

        var confirmed = await AppMessageBox.ConfirmAsync(
            LocalizationManager.Current.LibraryImportConfirm,
            LocalizationManager.Current.ImportLibrary,
            this);
        if (!confirmed)
            return;

        SetLibraryOperationControlsEnabled(false);
        LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryImporting;
        LibraryExportProgressBar.Value = 0;
        LibraryExportProgressBar.IsVisible = true;
        var progress = new Progress<LibraryImportProgress>(value =>
        {
            LibraryExportProgressBar.Value = value.Percentage;
            LibraryBackupStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.LibraryImportProgress,
                value.Percentage, value.CurrentFile ?? string.Empty);
        });
        try
        {
            if (Owner is MainWindow mainWindow)
                mainWindow.PrepareForLibraryImport();

            var importedPaths = await LibraryBackupService.ImportAsync(filePath, progress);
            LibraryExportProgressBar.Value = 100;
            _libraryPaths.Clear();
            _libraryPaths.AddRange(importedPaths);
            _settings.LibraryPaths = _libraryPaths.ToList();
            _onLibraryPathsChanged?.Invoke(_libraryPaths.ToList());
            RebuildDirectoryList();
            LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryImported;

            await AppMessageBox.ShowAsync(
                LocalizationManager.Current.LibraryImported,
                LocalizationManager.Current.ImportLibrary,
                this);
            (AvaloniaApp.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception ex)
        {
            LibraryExportProgressBar.IsVisible = false;
            LibraryBackupStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.LibraryImportFailed, ex.Message);
            SetLibraryOperationControlsEnabled(true);
        }
    }

    private bool CanStartLibraryBackupOperation()
    {
        var maintenanceActive =
            !OptimizeDatabaseButton.IsEnabled ||
            !RepairAlbumArtworkButton.IsEnabled ||
            !NormalizeArtistsButton.IsEnabled ||
            !DownloadMissingArtworkButton.IsEnabled;
        if (_activeScans.Count == 0 && !maintenanceActive)
            return true;
        LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryOperationScanActive;
        return false;
    }

    private void SetBackupButtonsEnabled(bool enabled)
    {
        ExportLibraryButton.IsEnabled = enabled;
        ImportLibraryButton.IsEnabled = enabled;
    }

    private void UpdateBackupButtonAvailability()
    {
        SetBackupButtonsEnabled(
            _activeScans.Count == 0 &&
            OptimizeDatabaseButton.IsEnabled &&
            RepairAlbumArtworkButton.IsEnabled &&
            NormalizeArtistsButton.IsEnabled &&
            DownloadMissingArtworkButton.IsEnabled);
    }

    private void SetLibraryOperationControlsEnabled(bool enabled)
    {
        AddDirectoryButton.IsEnabled = enabled;
        OptimizeDatabaseButton.IsEnabled = enabled;
        RepairAlbumArtworkButton.IsEnabled = enabled;
        NormalizeArtistsButton.IsEnabled = enabled;
        DownloadMissingArtworkButton.IsEnabled = enabled;
        SetBackupButtonsEnabled(enabled);
        DirectoriesPanel.IsEnabled = enabled;
    }

    // ------------------------------------------------------------------
    // Dialog
    // ------------------------------------------------------------------

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e) => Close(true);
    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}
