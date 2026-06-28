using System.IO;
using Avalonia;
using Avalonia.Threading;
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

/// <summary>Provides the complete settings experience embedded in the main window.</summary>
internal partial class SettingsView : UserControl
{
    private const int MaximumEqualizerFilters = 512;

    private sealed record SettingChoice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppSettings _settings;
    private readonly List<string> _libraryPaths = [];
    private readonly List<PlexServerSettings> _plexServers = [];
    private readonly Dictionary<string, string> _plexTokens = [];
    private readonly List<OrynivoServerSettings> _orynivoServers = [];
    private readonly Dictionary<string, CancellationTokenSource> _activeScans = [];
    private readonly Action<List<string>>? _onLibraryPathsChanged;
    private readonly Action<bool, EqualizerProfile?>? _onEqualizerPreviewChanged;
    private readonly bool _originalEqualizerEnabled;
    private readonly EqualizerProfile? _originalEqualizerProfile;
    private readonly List<EqualizerProfile> _equalizerProfiles = [];
    private readonly List<OutputProfile> _outputProfiles = [];
    private EqualizerProfile? _equalizerProfile;
    private OutputProfile? _outputProfile;
    private int _equalizerPreviewVersion;
    private bool _initializing = true;
    private bool _rebuildingEqualizerEditor;
    private bool _settingsAccepted;
    private bool _plexCredentialsChanged;

    /// <summary>
    /// Initializes a runtime-loader instance with default settings.
    /// </summary>
    public SettingsView()
        : this(new AppSettings())
    {
    }

    /// <summary>Initializes the settings view from persisted application settings.</summary>
    /// <param name="settings">Settings displayed and edited by the window.</param>
    /// <param name="onLibraryPathsChanged">Optional callback for immediate library-path updates.</param>
    /// <param name="onEqualizerPreviewChanged">Optional callback for live equalizer preview changes.</param>
    public SettingsView(
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
        _outputProfiles.AddRange((settings.OutputProfiles ?? []).Select(CloneOutputProfile));
        OutputProfileComboBox.ItemsSource = _outputProfiles;
        OutputProfileComboBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(OutputProfile.Name));
        _outputProfile = _outputProfiles.FirstOrDefault(profile =>
            string.Equals(
                profile.Name,
                settings.SelectedOutputProfileName,
                StringComparison.OrdinalIgnoreCase));
        OutputProfileComboBox.SelectedItem = _outputProfile;
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
        _equalizerProfiles.AddRange((settings.EqualizerProfiles ?? [])
            .Select(static profile => profile.Clone()));
        if (_equalizerProfiles.Count == 0 && settings.EqualizerProfile is not null)
            _equalizerProfiles.Add(settings.EqualizerProfile.Clone());
        EqualizerProfileComboBox.ItemsSource = _equalizerProfiles;
        EqualizerProfileComboBox.DisplayMemberBinding =
            new Avalonia.Data.Binding(nameof(EqualizerProfile.Name));
        _equalizerProfile = _equalizerProfiles.FirstOrDefault(profile =>
            string.Equals(
                profile.Name,
                settings.SelectedEqualizerProfileName ?? settings.EqualizerProfile?.Name,
                StringComparison.OrdinalIgnoreCase));
        EqualizerProfileComboBox.SelectedItem = _equalizerProfile;
        EqualizerEnabledCheckBox.IsChecked = settings.EqualizerEnabled;
        RefreshEqualizerProfileText();
        RebuildEqualizerEditor();
        McpServerEnabledCheckBox.IsChecked        = settings.McpServerEnabled;
        McpServerPortNumericUpDown.Value          = settings.McpServerPort;
        InitMcpToolCheckBoxes(settings.DisabledMcpTools);
        AiChatEnabledCheckBox.IsChecked         = settings.AiChat.Enabled;
        AiChatEndpointUrlTextBox.Text           = settings.AiChat.EndpointUrl;
        AiChatApiKeyTextBox.Text                = settings.AiChat.ApiKey;
        AiChatModelTextBox.Text                 = settings.AiChat.ModelName;
        AiChatMaxTokensNumericUpDown.Value      = settings.AiChat.MaxTokens;
        ShowInternetRadioItemCheckBox.IsChecked = settings.ShowInternetRadioItem;
        ShowPodcastsItemCheckBox.IsChecked      = settings.ShowPodcastsItem;
        ShowQueueItemCheckBox.IsChecked         = settings.ShowQueueItem;
        ShowLocalLibrarySectionCheckBox.IsChecked = settings.ShowLocalLibrarySection;
        ShowOwnRadiosSectionCheckBox.IsChecked = settings.ShowOwnRadiosSection;
        ShowMyPodcastsSectionCheckBox.IsChecked = settings.ShowMyPodcastsSection;
        ShowPlexSectionCheckBox.IsChecked          = settings.ShowPlexSection;
        ShowPlaylistsSectionCheckBox.IsChecked     = settings.ShowPlaylistsSection;
        ArtistInfoSourceComboBox.ItemsSource = Enum.GetValues<ArtistInfoSource>();
        ArtistInfoSourceComboBox.SelectedItem = settings.ArtistInfoSource;
        LastFmApiKeyTextBox.Text = settings.LastFmApiKey ?? string.Empty;
        QobuzApplicationIdTextBox.Text = settings.QobuzApplicationId ?? string.Empty;
        LastFmPanel.IsVisible = settings.ArtistInfoSource == ArtistInfoSource.LastFm;
        _libraryPaths.AddRange(settings.LibraryPaths);
        _plexServers.AddRange((settings.PlexServers ?? []).Select(ClonePlexServer));
        _orynivoServers.AddRange((settings.OrynivoServers ?? []).Select(CloneOrynivoServer));
        try
        {
            foreach (var credential in new WindowsPlexCredentialStore().LoadAll())
                _plexTokens[credential.Key] = credential.Value;
        }
        catch { }
        RebuildDirectoryList();
        RebuildPlexServerList();
        RebuildOrynivoServerList();
        _initializing = false;
        RefreshOutputProfileButtons();
        NavListBox.SelectedIndex = 1;
    }

    /// <summary>Raised when the embedded settings view requests save or cancel.</summary>
    internal event EventHandler<bool>? CompletionRequested;

    /// <summary>Gets the driver name of the selected output profile.</summary>
    public string? SelectedDriverName => _outputProfile?.SelectedDriverName;
    /// <summary>Gets the WASAPI device ID of the selected output profile.</summary>
    public string? SelectedWasapiDeviceId => _outputProfile?.SelectedWasapiDeviceId;
    /// <summary>Gets the WASAPI device display name of the selected output profile.</summary>
    public string? SelectedWasapiDeviceName => _outputProfile?.SelectedWasapiDeviceName;
    /// <summary>Gets the output backend of the selected output profile.</summary>
    public OutputBackend SelectedOutputBackend => _outputProfile?.Backend ?? OutputBackend.Wasapi;
    /// <summary>Gets independent copies of all configured output profiles.</summary>
    public IReadOnlyList<OutputProfile> SelectedOutputProfiles =>
        _outputProfiles.Select(CloneOutputProfile).ToList().AsReadOnly();
    /// <summary>Gets the name of the currently selected output profile.</summary>
    public string? SelectedOutputProfileName => _outputProfile?.Name;
    /// <summary>Gets the ReplayGain mode selected in the settings view.</summary>
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
    /// <summary>Gets independent copies of all configured equalizer profiles.</summary>
    public IReadOnlyList<EqualizerProfile> SelectedEqualizerProfiles =>
        _equalizerProfiles.Select(static profile => profile.Clone()).ToList().AsReadOnly();
    /// <summary>Gets the selected equalizer profile name.</summary>
    public string? SelectedEqualizerProfileName => _equalizerProfile?.Name;
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
    /// <summary>Gets independent copies of all configured Orynivo Server connections.</summary>
    public IReadOnlyList<OrynivoServerSettings> SelectedOrynivoServers =>
        _orynivoServers.Select(CloneOrynivoServer).ToList().AsReadOnly();
    /// <summary>Gets a value indicating whether the MCP server should be enabled.</summary>
    public bool McpServerEnabled => McpServerEnabledCheckBox.IsChecked == true;
    /// <summary>Gets the TCP port configured for the MCP server.</summary>
    public int McpServerPort => (int)(McpServerPortNumericUpDown.Value ?? 49200);
    /// <summary>Gets the set of MCP tool names that should be disabled.</summary>
    public HashSet<string> DisabledMcpTools => ReadDisabledMcpTools();
    /// <summary>Gets the current AI chat settings from the UI controls.</summary>
    public AI.AiChatSettings AiChatSettingsValue => new()
    {
        Enabled      = AiChatEnabledCheckBox.IsChecked == true,
        EndpointUrl  = AiChatEndpointUrlTextBox.Text?.Trim() ?? "http://localhost:1234/v1",
        ApiKey       = AiChatApiKeyTextBox.Text?.Trim() ?? string.Empty,
        ModelName    = AiChatModelTextBox.Text?.Trim() ?? string.Empty,
        MaxTokens    = (int)(AiChatMaxTokensNumericUpDown.Value ?? 2048)
    };
    /// <summary>Gets a value indicating whether the Internet Radio sidebar item should be visible.</summary>
    public bool ShowInternetRadioItem => ShowInternetRadioItemCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether the Podcasts sidebar item should be visible.</summary>
    public bool ShowPodcastsItem => ShowPodcastsItemCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether the Up Next sidebar item should be visible.</summary>
    public bool ShowQueueItem => ShowQueueItemCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether the Library sidebar section should be visible.</summary>
    public bool ShowLocalLibrarySection => ShowLocalLibrarySectionCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether the Own Radios sidebar section should be visible.</summary>
    public bool ShowOwnRadiosSection => ShowOwnRadiosSectionCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether the My Podcasts sidebar section should be visible.</summary>
    public bool ShowMyPodcastsSection => ShowMyPodcastsSectionCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether the Plex sidebar section should be visible.</summary>
    public bool ShowPlexSection => ShowPlexSectionCheckBox.IsChecked == true;
    /// <summary>Gets a value indicating whether the Playlists sidebar section should be visible.</summary>
    public bool ShowPlaylistsSection => ShowPlaylistsSectionCheckBox.IsChecked == true;

    private static PlexServerSettings ClonePlexServer(PlexServerSettings server) => new()
    {
        Id = server.Id,
        Name = server.Name,
        BaseUrl = server.BaseUrl
    };

    private static OrynivoServerSettings CloneOrynivoServer(OrynivoServerSettings server) => new()
    {
        Id = server.Id,
        Name = server.Name,
        BaseUrl = server.BaseUrl,
        ApiKey = server.ApiKey
    };

    private static OutputProfile CloneOutputProfile(OutputProfile profile) => new()
    {
        Name = profile.Name,
        Backend = profile.Backend,
        SelectedDriverName = profile.SelectedDriverName,
        SelectedWasapiDeviceId = profile.SelectedWasapiDeviceId,
        SelectedWasapiDeviceName = profile.SelectedWasapiDeviceName
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
        if (await dialog.ShowDialog<bool?>(GetHostWindow()) != true)
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
        if (await dialog.ShowDialog<bool?>(GetHostWindow()) != true)
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

    private void RebuildOrynivoServerList()
    {
        OrynivoServersPanel.Children.Clear();
        var primaryBrush = AvaloniaApp.Current!.Resources["AppPrimaryTextBrush"] as IBrush;
        var mutedBrush   = AvaloniaApp.Current!.Resources["AppMutedTextBrush"]   as IBrush;
        foreach (var server in _orynivoServers)
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

            var editButton = CreateStyledButton(LocalizationManager.Current.OrynivoEditServer, 80, 28, new Thickness(8, 0, 0, 0));
            editButton.Tag    = server.Id;
            editButton.Click += EditOrynivoServerButton_OnClick;
            Grid.SetColumn(editButton, 1);
            row.Children.Add(editButton);

            var removeButton = CreateStyledButton(LocalizationManager.Current.OrynivoRemoveServer, 80, 28, new Thickness(8, 0, 0, 0));
            removeButton.Tag    = server.Id;
            removeButton.Click += RemoveOrynivoServerButton_OnClick;
            Grid.SetColumn(removeButton, 2);
            row.Children.Add(removeButton);

            OrynivoServersPanel.Children.Add(row);
        }
    }

    private async void AddOrynivoServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new OrynivoServerDialog();
        if (await dialog.ShowDialog<bool?>(GetHostWindow()) != true)
            return;
        _orynivoServers.Add(dialog.Server);
        RebuildOrynivoServerList();
    }

    private async void EditOrynivoServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
            return;
        var index = _orynivoServers.FindIndex(s => s.Id == id);
        if (index < 0)
            return;
        var dialog = new OrynivoServerDialog(_orynivoServers[index]);
        if (await dialog.ShowDialog<bool?>(GetHostWindow()) != true)
            return;
        _orynivoServers[index] = dialog.Server;
        RebuildOrynivoServerList();
    }

    private void RemoveOrynivoServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
            return;
        _orynivoServers.RemoveAll(s => s.Id == id);
        RebuildOrynivoServerList();
    }

    /// <summary>Stops background work and restores the original live equalizer preview when needed.</summary>
    internal void Deactivate()
    {
        foreach (var cts in _activeScans.Values)
            cts.Cancel();
        Interlocked.Increment(ref _equalizerPreviewVersion);
        if (!_settingsAccepted)
        {
            var originalProfile = _originalEqualizerProfile?.Clone();
            _ = Task.Run(() => _onEqualizerPreviewChanged?.Invoke(
                _originalEqualizerEnabled,
                originalProfile));
        }
    }

    /// <summary>Returns the main window hosting this settings view.</summary>
    /// <returns>The hosting window.</returns>
    private Window GetHostWindow() =>
        TopLevel.GetTopLevel(this) as Window
        ?? throw new InvalidOperationException("The settings view is not attached to a window.");

    // ------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------

    private void NavListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: string tag })
            return;
        AudioDevicePanel.IsVisible             = tag == "AudioDevice";
        LibraryPanel.IsVisible                 = tag == "Library";
        OrynivoServersSettingsPanel.IsVisible  = tag == "OrynivoServers";
        StreamingPanel.IsVisible               = tag == "Streaming";
        AppearancePanel.IsVisible              = tag == "Appearance";
        ArtistInfoPanel.IsVisible              = tag == "ArtistInfo";
        McpPanel.IsVisible                     = tag == "Mcp";
        AiChatPanel.IsVisible                  = tag == "AiChat";
    }

    private void ArtistInfoSourceComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        LastFmPanel.IsVisible = SelectedArtistInfoSource == ArtistInfoSource.LastFm;
    }

    // ------------------------------------------------------------------
    // Ausgabeprofile
    // ------------------------------------------------------------------

    private void OutputProfileComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _outputProfile = OutputProfileComboBox.SelectedItem as OutputProfile;
        RefreshOutputProfileButtons();
    }

    /// <summary>Creates a new output profile via dialog and selects it immediately.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void CreateOutputProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new OutputProfileDialog(
            _outputProfiles.Select(static p => p.Name));
        if (await dialog.ShowDialog<bool>(GetHostWindow()) != true || dialog.Result is null)
            return;
        _outputProfiles.Add(dialog.Result);
        RefreshOutputProfileChoices(dialog.Result);
    }

    /// <summary>Opens the selected output profile for editing.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void ConfigureOutputProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_outputProfile is null)
            return;
        var dialog = new OutputProfileDialog(
            _outputProfiles
                .Where(p => p != _outputProfile)
                .Select(static p => p.Name),
            _outputProfile);
        if (await dialog.ShowDialog<bool>(GetHostWindow()) != true || dialog.Result is null)
            return;
        var index = _outputProfiles.IndexOf(_outputProfile);
        if (index >= 0)
            _outputProfiles[index] = dialog.Result;
        RefreshOutputProfileChoices(dialog.Result);
    }

    /// <summary>Deletes the selected output profile after confirmation.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void DeleteOutputProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_outputProfile is null)
            return;
        var confirmed = await AppMessageBox.ConfirmAsync(
            string.Format(
                LocalizationManager.Current.OutputProfileDeleteConfirm,
                _outputProfile.Name),
            LocalizationManager.Current.OutputProfileDeleteTitle,
            GetHostWindow());
        if (!confirmed)
            return;
        _outputProfiles.Remove(_outputProfile);
        _outputProfile = null;
        RefreshOutputProfileChoices(null);
    }

    /// <summary>Refreshes the profile dropdown and applies the requested selection.</summary>
    /// <param name="selectedProfile">Profile to select, or <see langword="null"/> for no selection.</param>
    private void RefreshOutputProfileChoices(OutputProfile? selectedProfile)
    {
        OutputProfileComboBox.ItemsSource = null;
        OutputProfileComboBox.ItemsSource = _outputProfiles;
        OutputProfileComboBox.SelectedItem = selectedProfile;
        _outputProfile = selectedProfile;
        RefreshOutputProfileButtons();
    }

    /// <summary>Updates the enabled state of the configure and delete buttons and the summary label.</summary>
    private void RefreshOutputProfileButtons()
    {
        var hasSelection = _outputProfile is not null;
        ConfigureOutputProfileButton.IsEnabled = hasSelection;
        DeleteOutputProfileButton.IsEnabled    = hasSelection;
        OutputProfileSummaryTextBlock.Text     = _outputProfile is null
            ? string.Empty
            : BuildOutputSummary(_outputProfile);
    }

    private static string BuildOutputSummary(OutputProfile profile)
    {
        var backend = profile.Backend switch
        {
            OutputBackend.Asio   => LocalizationManager.Current.SteinbergAsio,
            OutputBackend.CwAsio => LocalizationManager.Current.CwAsio,
            _                    => "WASAPI"
        };
        var device = profile.Backend is OutputBackend.Asio or OutputBackend.CwAsio
            ? profile.SelectedDriverName
            : profile.SelectedWasapiDeviceName;
        return string.IsNullOrEmpty(device) ? backend : $"{backend}  ·  {device}";
    }

    /// <summary>Programmatically navigates to the settings section identified by <paramref name="tag"/>.</summary>
    internal void NavigateToSection(string tag)
    {
        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.Ordinal));
        if (item is not null)
            NavListBox.SelectedItem = item;
    }

    /// <summary>Scrolls the content area to make the equalizer profile selector visible.</summary>
    internal void ScrollToEqualizerSection()
    {
        Dispatcher.UIThread.Post(
            () => EqualizerProfileComboBox.BringIntoView(),
            DispatcherPriority.Loaded);
    }

    /// <summary>Imports an Equalizer APO or AutoEQ text profile.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void ImportEqualizerProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await GetHostWindow().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
            if (_equalizerProfile is null)
                return;
            var importedProfile = await Task.Run(() => EqualizerApoParser.ParseFile(path));
            _equalizerProfile.PreampDb = importedProfile.PreampDb;
            _equalizerProfile.Filters = importedProfile.Filters
                .Select(static filter => filter.Clone())
                .ToList();
            EqualizerEnabledCheckBox.IsChecked = true;
            RefreshEqualizerProfileText();
            RebuildEqualizerEditor();
            QueueEqualizerPreview();
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
        var hasProfile = _equalizerProfile is not null;
        EqualizerEditorPanel.IsVisible = hasProfile;
        DeleteEqualizerProfileButton.IsVisible = hasProfile;
        EqualizerEnabledCheckBox.IsEnabled = hasProfile;
        EqualizerProfileTextBlock.Text = _equalizerProfile is null
            ? string.Empty
            : string.Format(
                LocalizationManager.Current.EqualizerProfileSummary,
                _equalizerProfile.Name,
                _equalizerProfile.PreampDb,
                _equalizerProfile.Filters.Count);
    }

    /// <summary>Switches the editable and active equalizer profile.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Selection-change event data.</param>
    private void EqualizerProfileComboBox_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        _equalizerProfile = EqualizerProfileComboBox.SelectedItem as EqualizerProfile;
        if (_initializing)
            return;
        if (_equalizerProfile is null)
            EqualizerEnabledCheckBox.IsChecked = false;
        RefreshEqualizerProfileText();
        RebuildEqualizerEditor();
        QueueEqualizerPreview();
    }

    /// <summary>Creates and selects a new empty equalizer profile.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void CreateEqualizerProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new EqualizerProfileNameDialog(
            _equalizerProfiles.Select(static profile => profile.Name));
        if (await dialog.ShowDialog<bool>(GetHostWindow()) != true
            || string.IsNullOrWhiteSpace(dialog.ProfileName))
        {
            return;
        }

        var profile = new EqualizerProfile { Name = dialog.ProfileName };
        _equalizerProfiles.Add(profile);
        RefreshEqualizerProfileChoices(profile);
        EqualizerEnabledCheckBox.IsChecked = true;
        QueueEqualizerPreview();
    }

    /// <summary>Deletes the selected equalizer profile after confirmation.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private async void DeleteEqualizerProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_equalizerProfile is null)
            return;
        var confirmed = await AppMessageBox.ConfirmAsync(
            string.Format(
                LocalizationManager.Current.EqualizerDeleteConfirm,
                _equalizerProfile.Name),
            LocalizationManager.Current.EqualizerDeleteTitle,
            GetHostWindow());
        if (!confirmed)
            return;

        _equalizerProfiles.Remove(_equalizerProfile);
        _equalizerProfile = null;
        EqualizerEnabledCheckBox.IsChecked = false;
        RefreshEqualizerProfileChoices(null);
        QueueEqualizerPreview();
    }

    /// <summary>Refreshes the profile dropdown and applies the requested selection.</summary>
    /// <param name="selectedProfile">Profile to select, or <see langword="null"/> for no selection.</param>
    private void RefreshEqualizerProfileChoices(EqualizerProfile? selectedProfile)
    {
        EqualizerProfileComboBox.ItemsSource = null;
        EqualizerProfileComboBox.ItemsSource = _equalizerProfiles;
        EqualizerProfileComboBox.SelectedItem = selectedProfile;
        _equalizerProfile = selectedProfile;
        RefreshEqualizerProfileText();
        RebuildEqualizerEditor();
    }

    /// <summary>Rebuilds the dynamic equalizer filter editor from the active profile.</summary>
    private void RebuildEqualizerEditor()
    {
        _rebuildingEqualizerEditor = true;
        try
        {
            EqualizerFiltersPanel.Children.Clear();
            EqualizerPreampNumericUpDown.Value = (decimal)(_equalizerProfile?.PreampDb ?? 0);
            EqualizerPreampNumericUpDown.IsEnabled = _equalizerProfile is not null;
            AddEqualizerFilterButton.IsEnabled =
                (_equalizerProfile?.Filters.Count ?? 0) < MaximumEqualizerFilters;
            EqualizerResponseGraph.SetProfile(_equalizerProfile);
            if (_equalizerProfile is null)
                return;

            for (var index = 0; index < _equalizerProfile.Filters.Count; index++)
                EqualizerFiltersPanel.Children.Add(
                    CreateEqualizerFilterRow(_equalizerProfile.Filters[index], index));
        }
        finally
        {
            _rebuildingEqualizerEditor = false;
        }
    }

    /// <summary>Creates one editable row for a parametric equalizer filter.</summary>
    /// <param name="filter">Filter edited by the row.</param>
    /// <param name="index">Zero-based filter index.</param>
    /// <returns>The configured filter row.</returns>
    private Control CreateEqualizerFilterRow(EqualizerFilter filter, int index)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(28)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(220)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var number = new TextBlock
        {
            Text = (index + 1).ToString(),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(number);

        var typeChoices = CreateEqualizerTypeChoices();
        var typeComboBox = new ComboBox
        {
            ItemsSource = typeChoices,
            SelectedItem = typeChoices.First(choice => choice.Value == filter.Type),
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(typeComboBox, 1);
        row.Children.Add(typeComboBox);

        var frequency = CreateEqualizerNumber(filter.Frequency, 1, 100000, 10, "0");
        Grid.SetColumn(frequency, 2);
        row.Children.Add(frequency);

        var gain = CreateEqualizerNumber(filter.GainDb, -100, 100, 0.5, "0.0");
        gain.IsEnabled = SupportsEqualizerGain(filter.Type);
        Grid.SetColumn(gain, 3);
        row.Children.Add(gain);

        var quality = CreateEqualizerNumber(filter.Q, 0.05, 100, 0.05, "0.00");
        Grid.SetColumn(quality, 4);
        row.Children.Add(quality);

        var remove = CreateStyledButton("×", 28, 28);
        remove.Margin = new Thickness(6, 0, 0, 0);
        ToolTip.SetTip(remove, LocalizationManager.Current.EqualizerRemoveFilter);
        Grid.SetColumn(remove, 5);
        row.Children.Add(remove);

        typeComboBox.SelectionChanged += (_, _) =>
        {
            if (typeComboBox.SelectedItem is not SettingChoice<EqualizerFilterType> choice)
                return;
            filter.Type = choice.Value;
            gain.IsEnabled = SupportsEqualizerGain(filter.Type);
            EqualizerEditorChanged();
        };
        frequency.ValueChanged += (_, args) =>
        {
            if (args.NewValue is decimal value)
            {
                filter.Frequency = (double)value;
                EqualizerEditorChanged();
            }
        };
        gain.ValueChanged += (_, args) =>
        {
            if (args.NewValue is decimal value)
            {
                filter.GainDb = (double)value;
                EqualizerEditorChanged();
            }
        };
        quality.ValueChanged += (_, args) =>
        {
            if (args.NewValue is decimal value)
            {
                filter.Q = (double)value;
                EqualizerEditorChanged();
            }
        };
        remove.Click += (_, _) =>
        {
            if (_equalizerProfile is null)
                return;
            _equalizerProfile.Filters.Remove(filter);
            RebuildEqualizerEditor();
            EqualizerEditorChanged();
        };
        return row;
    }

    /// <summary>Creates a numeric editor used by an equalizer filter row.</summary>
    /// <param name="value">Initial value.</param>
    /// <param name="minimum">Minimum accepted value.</param>
    /// <param name="maximum">Maximum accepted value.</param>
    /// <param name="increment">Spinner increment.</param>
    /// <param name="format">Display format.</param>
    /// <returns>The configured numeric editor.</returns>
    private static NumericUpDown CreateEqualizerNumber(
        double value,
        double minimum,
        double maximum,
        double increment,
        string format) =>
        new()
        {
            Value = (decimal)Math.Clamp(value, minimum, maximum),
            Minimum = (decimal)minimum,
            Maximum = (decimal)maximum,
            Increment = (decimal)increment,
            FormatString = format,
            ClipValueToMinMax = true,
            Height = 28,
            Margin = new Thickness(0, 0, 6, 0)
        };

    /// <summary>Creates localized choices for all supported parametric filter types.</summary>
    /// <returns>The localized filter-type choices.</returns>
    private static SettingChoice<EqualizerFilterType>[] CreateEqualizerTypeChoices() =>
    [
        new(EqualizerFilterType.Peak, LocalizationManager.Current.EqualizerPeak),
        new(EqualizerFilterType.LowShelf, LocalizationManager.Current.EqualizerLowShelf),
        new(EqualizerFilterType.HighShelf, LocalizationManager.Current.EqualizerHighShelf),
        new(EqualizerFilterType.LowPass, LocalizationManager.Current.EqualizerLowPass),
        new(EqualizerFilterType.HighPass, LocalizationManager.Current.EqualizerHighPass)
    ];

    /// <summary>Returns whether a filter type uses a gain value.</summary>
    /// <param name="type">Filter type to inspect.</param>
    /// <returns><see langword="true"/> for peak and shelf filters; otherwise <see langword="false"/>.</returns>
    private static bool SupportsEqualizerGain(EqualizerFilterType type) =>
        type is EqualizerFilterType.Peak
            or EqualizerFilterType.LowShelf
            or EqualizerFilterType.HighShelf;

    /// <summary>Updates the graph, profile summary, and debounced live preview after an edit.</summary>
    private void EqualizerEditorChanged()
    {
        if (_initializing)
            return;
        RefreshEqualizerProfileText();
        EqualizerResponseGraph.SetProfile(_equalizerProfile);
        QueueEqualizerPreview();
    }

    /// <summary>Updates the profile preamplification from the numeric editor.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Value-change event data.</param>
    private void EqualizerPreampNumericUpDown_OnValueChanged(
        object? sender,
        NumericUpDownValueChangedEventArgs e)
    {
        if (_initializing
            || _rebuildingEqualizerEditor
            || _equalizerProfile is null
            || e.NewValue is not decimal value)
            return;
        _equalizerProfile.PreampDb = (double)value;
        EqualizerEditorChanged();
    }

    /// <summary>Adds a new peaking filter to the dynamic equalizer profile.</summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Event data.</param>
    private void AddEqualizerFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_equalizerProfile is null)
            return;
        if (_equalizerProfile.Filters.Count >= MaximumEqualizerFilters)
            return;
        _equalizerProfile.Filters.Add(new EqualizerFilter
        {
            Type = EqualizerFilterType.Peak,
            Frequency = 1000,
            GainDb = 0,
            Q = 0.7071067811865476
        });
        EqualizerEnabledCheckBox.IsChecked = true;
        RebuildEqualizerEditor();
        EqualizerEditorChanged();
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
            GetHostWindow());
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
            if (GetHostWindow() is MainWindow mainWindow)
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
                GetHostWindow());
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
        CompletionRequested?.Invoke(this, true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) =>
        CompletionRequested?.Invoke(this, false);

    private static readonly (string ToolName, string PropName)[] McpToolDefs =
    [
        ("get_now_playing",       nameof(McpToolGetNowPlaying)),
        ("get_queue",             nameof(McpToolGetQueue)),
        ("play",                  nameof(McpToolPlay)),
        ("pause_resume",          nameof(McpToolPauseResume)),
        ("next_track",            nameof(McpToolNextTrack)),
        ("previous_track",        nameof(McpToolPreviousTrack)),
        ("stop",                  nameof(McpToolStop)),
        ("seek",                  nameof(McpToolSeek)),
        ("set_volume",            nameof(McpToolSetVolume)),
        ("queue_append",          nameof(McpToolQueueAppend)),
        ("queue_play_next",       nameof(McpToolQueuePlayNext)),
        ("search_library",        nameof(McpToolSearchLibrary)),
        ("list_playlists",        nameof(McpToolListPlaylists)),
        ("get_playlist_tracks",   nameof(McpToolGetPlaylistTracks)),
        ("create_playlist",       nameof(McpToolCreatePlaylist)),
        ("create_smart_playlist", nameof(McpToolCreateSmartPlaylist)),
        ("get_play_history",      nameof(McpToolGetPlayHistory)),
        ("clear_queue",           nameof(McpToolClearQueue)),
        ("replace_queue",         nameof(McpToolReplaceQueue)),
    ];

    /// <summary>Initialises each tool checkbox from the persisted disabled-tool set.</summary>
    /// <param name="disabled">The set of currently disabled tool names.</param>
    private void InitMcpToolCheckBoxes(HashSet<string> disabled)
    {
        McpToolGetNowPlaying.IsChecked       = !disabled.Contains("get_now_playing");
        McpToolGetQueue.IsChecked            = !disabled.Contains("get_queue");
        McpToolPlay.IsChecked                = !disabled.Contains("play");
        McpToolPauseResume.IsChecked         = !disabled.Contains("pause_resume");
        McpToolNextTrack.IsChecked           = !disabled.Contains("next_track");
        McpToolPreviousTrack.IsChecked       = !disabled.Contains("previous_track");
        McpToolStop.IsChecked                = !disabled.Contains("stop");
        McpToolSeek.IsChecked                = !disabled.Contains("seek");
        McpToolSetVolume.IsChecked           = !disabled.Contains("set_volume");
        McpToolQueueAppend.IsChecked         = !disabled.Contains("queue_append");
        McpToolQueuePlayNext.IsChecked       = !disabled.Contains("queue_play_next");
        McpToolSearchLibrary.IsChecked       = !disabled.Contains("search_library");
        McpToolListPlaylists.IsChecked       = !disabled.Contains("list_playlists");
        McpToolGetPlaylistTracks.IsChecked   = !disabled.Contains("get_playlist_tracks");
        McpToolCreatePlaylist.IsChecked      = !disabled.Contains("create_playlist");
        McpToolCreateSmartPlaylist.IsChecked = !disabled.Contains("create_smart_playlist");
        McpToolGetPlayHistory.IsChecked      = !disabled.Contains("get_play_history");
        McpToolClearQueue.IsChecked          = !disabled.Contains("clear_queue");
        McpToolReplaceQueue.IsChecked        = !disabled.Contains("replace_queue");
    }

    /// <summary>Reads the checkbox states and returns the set of tool names that are disabled.</summary>
    /// <returns>A <see cref="HashSet{T}"/> of disabled MCP tool names.</returns>
    private HashSet<string> ReadDisabledMcpTools()
    {
        var disabled = new HashSet<string>();
        if (McpToolGetNowPlaying.IsChecked       != true) disabled.Add("get_now_playing");
        if (McpToolGetQueue.IsChecked            != true) disabled.Add("get_queue");
        if (McpToolPlay.IsChecked                != true) disabled.Add("play");
        if (McpToolPauseResume.IsChecked         != true) disabled.Add("pause_resume");
        if (McpToolNextTrack.IsChecked           != true) disabled.Add("next_track");
        if (McpToolPreviousTrack.IsChecked       != true) disabled.Add("previous_track");
        if (McpToolStop.IsChecked                != true) disabled.Add("stop");
        if (McpToolSeek.IsChecked                != true) disabled.Add("seek");
        if (McpToolSetVolume.IsChecked           != true) disabled.Add("set_volume");
        if (McpToolQueueAppend.IsChecked         != true) disabled.Add("queue_append");
        if (McpToolQueuePlayNext.IsChecked       != true) disabled.Add("queue_play_next");
        if (McpToolSearchLibrary.IsChecked       != true) disabled.Add("search_library");
        if (McpToolListPlaylists.IsChecked       != true) disabled.Add("list_playlists");
        if (McpToolGetPlaylistTracks.IsChecked   != true) disabled.Add("get_playlist_tracks");
        if (McpToolCreatePlaylist.IsChecked      != true) disabled.Add("create_playlist");
        if (McpToolCreateSmartPlaylist.IsChecked != true) disabled.Add("create_smart_playlist");
        if (McpToolGetPlayHistory.IsChecked      != true) disabled.Add("get_play_history");
        if (McpToolClearQueue.IsChecked          != true) disabled.Add("clear_queue");
        if (McpToolReplaceQueue.IsChecked        != true) disabled.Add("replace_queue");
        return disabled;
    }
}
