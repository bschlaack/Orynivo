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
    private readonly Action<bool, EqualizerProfile?>? _onEqualizerPreviewChanged;
    private readonly bool _originalEqualizerEnabled;
    private readonly EqualizerProfile? _originalEqualizerProfile;
    private readonly SemaphoreSlim _driverLoadGate = new(1, 1);
    private EqualizerProfile? _equalizerProfile;
    private int _driverLoadVersion;
    private int _equalizerPreviewVersion;
    private bool _initializing = true;
    private bool _settingsAccepted;
    private bool _plexCredentialsChanged;

    /// <summary>
    /// Initializes a runtime-loader instance with default settings.
    /// </summary>
    public SettingsWindow()
        : this(new AppSettings())
    {
    }

    /// <summary>Initializes the settings window from persisted application settings.</summary>
    /// <param name="settings">Settings displayed and edited by the window.</param>
    /// <param name="onLibraryPathsChanged">Optional callback for immediate library-path updates.</param>
    /// <param name="onEqualizerPreviewChanged">Optional callback for live equalizer preview changes.</param>
    public SettingsWindow(
        AppSettings settings,
        Action<List<string>>? onLibraryPathsChanged = null,
        Action<bool, EqualizerProfile?>? onEqualizerPreviewChanged = null)
    {
        InitializeComponent();
        _settings = settings;
        _onLibraryPathsChanged = onLibraryPathsChanged;
        _onEqualizerPreviewChanged = onEqualizerPreviewChanged;
        _originalEqualizerEnabled = settings.EqualizerEnabled;
        _originalEqualizerProfile = settings.EqualizerProfile?.Clone();
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
        var replayGainChoices = new[]
        {
            new SettingChoice<ReplayGainMode>(ReplayGainMode.Off, LocalizationManager.Current.ReplayGainOff),
            new SettingChoice<ReplayGainMode>(ReplayGainMode.Track, LocalizationManager.Current.ReplayGainTrack),
            new SettingChoice<ReplayGainMode>(ReplayGainMode.Album, LocalizationManager.Current.ReplayGainAlbum)
        };
        ReplayGainModeComboBox.ItemsSource = replayGainChoices;
        ReplayGainModeComboBox.SelectedItem =
            replayGainChoices.FirstOrDefault(choice => choice.Value == settings.ReplayGainMode)
            ?? replayGainChoices[0];
        AlwaysConvertDsdToPcmCheckBox.IsChecked = settings.AlwaysConvertDsdToPcm;
        _equalizerProfile = settings.EqualizerProfile?.Clone();
        EqualizerEnabledCheckBox.IsChecked = settings.EqualizerEnabled;
        RefreshEqualizerProfileText();
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
        _initializing = false;
        _ = LoadDriversAsync();
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
    /// <summary>Gets the ReplayGain mode selected in the settings window.</summary>
    public ReplayGainMode SelectedReplayGainMode =>
        ReplayGainModeComboBox.SelectedItem is SettingChoice<ReplayGainMode> choice
            ? choice.Value
            : ReplayGainMode.Off;
    /// <summary>Gets a value indicating whether DSF and DFF sources should always be converted to PCM.</summary>
    public bool AlwaysConvertDsdToPcm => AlwaysConvertDsdToPcmCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether the imported equalizer profile is enabled.</summary>
    public bool EqualizerEnabled =>
        _equalizerProfile is not null && EqualizerEnabledCheckBox.IsChecked == true;
    /// <summary>Gets an independent copy of the imported equalizer profile.</summary>
    public EqualizerProfile? SelectedEqualizerProfile => _equalizerProfile?.Clone();
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
    /// <summary>Gets a value indicating whether Plex credentials were edited in this window.</summary>
    public bool PlexCredentialsChanged => _plexCredentialsChanged;
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
        _plexCredentialsChanged = true;
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
        _plexCredentialsChanged = true;
        RebuildPlexServerList();
    }

    private void RemovePlexServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
            return;
        _plexServers.RemoveAll(server => server.Id == id);
        _plexTokens.Remove(id);
        _plexCredentialsChanged = true;
        RebuildPlexServerList();
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var cts in _activeScans.Values)
            cts.Cancel();
        Interlocked.Increment(ref _driverLoadVersion);
        Interlocked.Increment(ref _equalizerPreviewVersion);
        if (!_settingsAccepted)
        {
            var originalProfile = _originalEqualizerProfile?.Clone();
            _ = Task.Run(() => _onEqualizerPreviewChanged?.Invoke(
                _originalEqualizerEnabled,
                originalProfile));
        }
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

    private async Task LoadDriversAsync()
    {
        var loadVersion = Interlocked.Increment(ref _driverLoadVersion);
        var backend = SelectedOutputBackend;
        DriverComboBox.IsEnabled = false;
        DeviceInfoButton.IsEnabled = false;
        StatusTextBlock.Text = LocalizationManager.Current.OutputDevicesLoading;
        try
        {
            await _driverLoadGate.WaitAsync();
            if (loadVersion != Volatile.Read(ref _driverLoadVersion))
                return;
            if (backend == OutputBackend.Wasapi)
            {
                var devices = await Task.Run(WasapiDeviceProvider.GetRenderDevices);
                if (loadVersion != Volatile.Read(ref _driverLoadVersion))
                    return;
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
            else if (backend is OutputBackend.Asio or OutputBackend.CwAsio)
            {
                var drivers = await Task.Run(() => SteinbergAsioStream.GetDriverNames(backend));
                if (loadVersion != Volatile.Read(ref _driverLoadVersion))
                    return;
                DriverComboBox.ItemsSource = drivers;
                DriverComboBox.DisplayMemberBinding = null;
                DriverComboBox.SelectedItem = drivers.FirstOrDefault(name =>
                    string.Equals(name, _settings.SelectedDriverName, StringComparison.Ordinal))
                    ?? drivers.FirstOrDefault(name =>
                        name.Contains("TOPPING", StringComparison.OrdinalIgnoreCase))
                    ?? drivers.FirstOrDefault();
                DeviceLabelTextBlock.Text = backend == OutputBackend.CwAsio
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
            if (loadVersion == Volatile.Read(ref _driverLoadVersion))
                StatusTextBlock.Text = LocalizationManager.Current.AsioBridgeMissing;
        }
        catch
        {
            if (loadVersion == Volatile.Read(ref _driverLoadVersion))
                StatusTextBlock.Text = backend == OutputBackend.Wasapi
                    ? LocalizationManager.Current.NoWasapiDevices
                    : LocalizationManager.Current.NoAsioDrivers;
        }
        finally
        {
            if (_driverLoadGate.CurrentCount == 0)
                _driverLoadGate.Release();
            if (loadVersion == Volatile.Read(ref _driverLoadVersion))
            {
                DriverComboBox.IsEnabled = backend != OutputBackend.KernelStreaming;
                DeviceInfoButton.IsEnabled =
                    DriverComboBox.SelectedItem is string or WasapiDeviceInfo;
            }
        }
    }

    private void DriverComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        DeviceInfoButton.IsVisible = DriverComboBox.SelectedItem is string or WasapiDeviceInfo;
    }

    private async void OutputBackendComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;
        var backend = SelectedOutputBackend;
        await LoadDriversAsync();
        if (backend != SelectedOutputBackend)
            return;
        if (backend == OutputBackend.KernelStreaming)
        {
            DriverComboBox.ItemsSource = null;
            StatusTextBlock.Text = LocalizationManager.Current.KernelStreamingUnavailable;
        }
    }

    /// <summary>Imports an Equalizer APO or AutoEQ text profile.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void ImportEqualizerProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Current.EqualizerImportTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationManager.Current.EqualizerProfileFileType)
                {
                    Patterns = ["*.txt", "*.cfg"]
                }
            ]
        });
        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;
        ImportEqualizerProfileButton.IsEnabled = false;
        EqualizerProfileTextBlock.Text = LocalizationManager.Current.EqualizerImporting;
        try
        {
            _equalizerProfile = await Task.Run(() => EqualizerApoParser.ParseFile(path));
            EqualizerEnabledCheckBox.IsChecked = true;
            RefreshEqualizerProfileText();
        }
        catch
        {
            EqualizerProfileTextBlock.Text = LocalizationManager.Current.EqualizerImportFailed;
        }
        finally
        {
            ImportEqualizerProfileButton.IsEnabled = true;
        }
    }

    /// <summary>Refreshes the imported equalizer profile summary.</summary>
    private void RefreshEqualizerProfileText()
    {
        EqualizerEnabledCheckBox.IsEnabled = _equalizerProfile is not null;
        EqualizerProfileTextBlock.Text = _equalizerProfile is null
            ? LocalizationManager.Current.EqualizerNoProfile
            : string.Format(
                LocalizationManager.Current.EqualizerProfileSummary,
                _equalizerProfile.Name,
                _equalizerProfile.PreampDb,
                _equalizerProfile.Filters.Count);
    }

    private void EqualizerEnabledCheckBox_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        QueueEqualizerPreview();
    }

    private void QueueEqualizerPreview()
    {
        var previewVersion = Interlocked.Increment(ref _equalizerPreviewVersion);
        var enabled = EqualizerEnabled;
        var profile = _equalizerProfile?.Clone();
        _ = Task.Run(async () =>
        {
            await Task.Delay(75);
            if (previewVersion != Volatile.Read(ref _equalizerPreviewVersion))
                return;
            try
            {
                _onEqualizerPreviewChanged?.Invoke(enabled, profile);
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Equalizer live preview");
            }
        });
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
                    result.Total, result.Added, result.Updated, result.Removed, failed);
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

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref _equalizerPreviewVersion);
        _settingsAccepted = true;
        Close(true);
    }
    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}
