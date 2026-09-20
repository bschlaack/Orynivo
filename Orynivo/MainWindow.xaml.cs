using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using Orynivo.Audio;
using Orynivo.Controls;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;
using Windows.Media;

namespace Orynivo;

public partial class MainWindow : Window
{
    private const float PcmOutputBoostFactor = 1.9952623f;
    internal const string LocalSourceKey = "local";
    private int _plexNavigationLoadVersion;
    private int _plexViewLoadVersion;
    private int _unifiedLibraryLoadVersion;
    private CancellationTokenSource? _unifiedLibraryAppendCts;
    private CancellationTokenSource? _folderViewCts;
    private const int PlexPageSize = 500;
    private readonly PlexServerClient _plexClient = new();
    private PlexServerSettings? _activePlexServer;
    private string? _activePlexToken;
    private string? _activePlexSectionKey;
    private string? _activePlexSectionTitle;
    private string _activePlexView = "Artists";
    private int _plexLoadedCount;
    private int _plexTotalCount;
    private CancellationTokenSource? _plexViewCts;
    private readonly Dictionary<string, ContentRow> _plexTracksByUrl =
        new(StringComparer.Ordinal);
    private readonly LocalLibraryCatalogProvider _localCatalogProvider = new();
    private readonly LocalLibraryPlaylistProvider _localPlaylistProvider = new();
    private readonly LocalNowPlayingMetadataProvider _localNowPlayingProvider = new();
    private INowPlayingMetadataProvider? _currentNowPlayingProvider;
    private ContentRow? _currentOrynivoTrackRow;
    private readonly OrynivoServerClient _orynivoClient = new();
    private static readonly HttpClient MusicBrainzRatingHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
    private static readonly TimeSpan MusicBrainzRatingCacheLifetime = TimeSpan.FromDays(30);
    private CancellationTokenSource? _albumMusicBrainzRatingCts;
    private readonly CancellationTokenSource _musicBrainzBackgroundCts = new();
    private int _musicBrainzBackgroundStarted;
    private int _musicBrainzForegroundRequests;
    private OrynivoServerSettings? _activeOrynivoServer;
    private string _activeOrynivoView = "Artists";
    private int _orynivoNavigationLoadVersion;
    private CancellationTokenSource? _orynivoViewCts;
    private readonly Stack<(string View, long? FilterId, string? FilterName)> _orynivoNavigationStack = [];
    private readonly Dictionary<string, ContentRow> _orynivoTracksByUrl =
        new(StringComparer.OrdinalIgnoreCase);
    private List<TrackFacetInfo>? _orynivoTrackFacets;
    private static readonly HttpClient RemoteArtworkHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private readonly Dictionary<string, TreeViewItem> _localFolderTrackItems =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _localFolderTrackHeaders =
        new(StringComparer.OrdinalIgnoreCase);
    // In-memory folder trees backing the current folder view, keyed by source key
    // (LocalSourceKey or GetServerSourceKey). Used to collect all descendant paths for a
    // remote directory node whose children are lazily built and may not be materialized yet.
    private readonly Dictionary<string, FolderTree> _folderTreesBySource =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<PlexNavigationState> _plexNavigationStack = [];
    private const int ArtworkPageSize = 120;
    private const double AlbumArtworkItemWidth = 196d;
    private const double AlbumArtworkItemHeight = 292d;
    private const double ArtistArtworkItemWidth = 216d;
    private const double ArtistArtworkItemHeight = 272d;
    private List<ContentRow> _albumArtworkRows = [];
    private List<ContentRow> _artistArtworkRows = [];
    private readonly ObservableCollection<ContentRow> _visibleAlbumArtworkRows = [];
    private readonly ObservableCollection<ContentRow> _visibleArtistArtworkRows = [];
    private readonly Control[] _animatedViewSurfaces = [];
    private int _contentLoadingDepth;
    private int _contentLoadingGeneration;
    private int _artworkBindingVersion;

    // ------------------------------------------------------------------
    // Felder
    // ------------------------------------------------------------------

    private IAudioPlayer?  _player;
    private bool _audioDeviceExplicitlyReleased;
    private string? _releasedOutputPath;
    private RadioStationRecord? _releasedOutputRadioStation;
    private PodcastPlayback? _releasedOutputPodcastPlayback;
    private TimeSpan _releasedOutputPosition;
    private bool _releasedOutputWasPaused;
    private CancellationTokenSource? _playbackCts;
    private readonly SettingsStore _settingsStore = new();
    private AppSettings _settings = new();
    private readonly Scrobbling.LastFmScrobblingService _lastFmScrobbler =
        new(new Scrobbling.PendingScrobbleStore(AppPaths.GetDataPath("scrobbling", "pending.json")));
    private UserProfileManager? _profileManager;
    private UserProfile ActiveUserProfile => _profileManager?.ActiveProfile
        ?? throw new InvalidOperationException("The user profile context is not initialized.");
    private LibraryWatcherService? _libraryWatcher;
    private int _libraryWatcherRefreshPending;
    private bool _libraryScanActive;
    private DateTime _lastLibraryActivityUiUpdate;
    private bool _libraryRefreshAvailable;
    private string? _localScanText;
    private string? _remoteScanText;
    private bool _remoteScanPollInProgress;
    private DispatcherTimer? _remoteScanPollTimer;
    private readonly DispatcherTimer _transportTimer;
    private bool _isSeekingWithSlider;
    private DateTimeOffset _positionSliderSeekStartedAt;
    private TimeSpan? _pendingTransportSeekPosition;
    private int _transportSeekVersion;
    private CancellationTokenSource? _waveformCts;
    private bool _showAlbumArtworkView;
    private bool _showArtistArtworkView;
    private long? _currentPlayHistoryId;
    private long? _currentTrackId;
    private long? _currentArtistId;
    private long? _artistInfoDisplayedId;
    private ContentRow? _artistInfoDisplayedRemoteRow;
    private string? _artistInfoUnifiedArtistName;
    private bool _artistInfoIsFavorite;
    private int _artistDetailLoadVersion;
    private bool _nowPlayingRemoteArtistInfo;
    private string? _currentArtistName;
    private string? _artistInfoSourceUrl;
    private bool  _currentTrackIsFavorite;
    private long? _activePlaylistId;
    private OrynivoServerSettings? _activeOrynivoPlaylistServer;
    private long? _activeOrynivoPlaylistId;
    private long? _activeAlbumFilterId;
    private string? _activeAlbumFilterTitle;
    private long? _activeArtistFilterId;
    private string? _activeArtistFilterName;
    private ILibraryCatalogProvider? _activeAlbumCatalogProvider;
    private LibraryCatalogAlbum? _activeCatalogAlbum;
    private IReadOnlyList<long>? _activeLogicalAlbumIds;
    private IReadOnlyList<LogicalAlbumPart>? _activeLogicalAlbumParts;
    private ContentRow? _activeLogicalAlbumRow;
    private bool _showAllAlbumTracks;
    private bool _updatingAlbumTrackScope;
    private readonly List<DataGrid> _albumFolderGroupGrids = [];
    private readonly Stack<NavigationState> _navigationStack = [];
    private bool _restoringNavigationHistory;
    private readonly DispatcherTimer _searchTimer;
    private bool _trackFavoritesOnly;
    private bool _artistFavoritesOnly;
    private bool _albumFavoritesOnly;
    private bool _updatingEntityFavoritesFilter;
    private bool _eqPickerUpdating;
    private bool _outputPickerUpdating;
    private readonly HashSet<string> _selectedTrackGenres = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedTrackFormats = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _selectedTrackBitrates = [];
    private readonly HashSet<string> _selectedTrackSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedTrackFilterSections = new(StringComparer.Ordinal);
    private readonly HashSet<long> _artistProfilesLoading = [];
    private bool _isDraggingAlphabetIndex;
    private bool _alphabetScrollUpdatePending;
    private bool _isAlphabetProgrammaticScroll;
    private ScrollBar? _contentDataGridVerticalScrollBar;
    private double _contentDataGridAverageRowHeight = 32d;
    private int _diagnosticScrollEventCount;
    private int _diagnosticLoadingRowCount;
    private readonly RadioBrowserService _radioBrowserService = new();
    private readonly RadioStreamMetadataService _radioMetadataService = new();
    private readonly PodcastService _podcastService = new();
    private readonly CatalogFilterCache _catalogFilterCache = new();
    private CatalogFilterCacheData _catalogFilterCacheData = new();
    private CancellationTokenSource? _radioSearchCts;
    private CancellationTokenSource? _podcastSearchCts;
    private CancellationTokenSource? _podcastFeedCts;
    private CancellationTokenSource? _radioMetadataCts;
    private static readonly HttpClient RadioImageHttpClient = CreateRadioImageHttpClient();
    private readonly List<RadioStationViewModel> _radioSearchResults = [];
    private readonly HashSet<string> _selectedRadioGenres = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PodcastViewModel> _podcastSearchResults = [];
    private readonly HashSet<string> _selectedPodcastCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedPodcastLanguages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CatalogFilterOption> _radioGenreCatalog = [];
    private readonly List<CatalogFilterOption> _podcastCategoryCatalog = [];
    private readonly List<CatalogFilterOption> _podcastLanguageCatalog = [];
    private bool _radioFilterCatalogLoading;
    private bool _podcastFilterCatalogLoading;
    private bool _podcastLanguagesLoading;

    private readonly ObservableCollection<PlaylistItem> _queue = [];
    private readonly ObservableCollection<ContentRow> _queueRows = [];
    private readonly ObservableCollection<LyricLineViewModel> _lyricLines = [];
    private KaraokeWindow? _karaokeWindow;
    private VisualizerWindow? _visualizerWindow;
    private int _queueIndex = -1;
    private bool _shuffleEnabled;
    private readonly HashSet<string> _playedQueuePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrynivoPlaylistInfo> _orynivoPlaylistsByTag = new(StringComparer.Ordinal);
    private readonly List<int> _shuffleHistory = [];
    private int _shuffleHistoryPosition = -1;
    private string _currentFilePath = string.Empty;
    private RadioStationRecord? _currentRadioStation;
    private string? _currentRadioArtworkPath;
    private PodcastPlayback? _currentPodcastPlayback;
    private PodcastRecord? _activePodcast;
    private DateTimeOffset _lastPodcastProgressSave = DateTimeOffset.MinValue;
    private TimeSpan _currentPlaybackDuration;
    private bool _nonGaplessFadeTransitionInProgress;
    private long? _currentAlbumId;
    private string? _currentAlbumTitle;
    private CancellationTokenSource? _lyricsCts;
    private CancellationTokenSource? _artistProfileCts;
    private WindowsEndpointVolumeSynchronizer? _endpointVolumeSynchronizer;
    private int _endpointVolumeSynchronizationVersion;
    private WindowsMediaTransportService? _windowsMediaTransport;
    private readonly Mcp.McpPlayerBridge _mcpBridge = new();
    private readonly Mcp.McpServerService _mcpServer = new();
    private readonly Remote.MobileRemoteServerService _mobileRemoteServer = new();
    private Orynivo.Web.WebBrowsingService? _webBrowsing;
    private static readonly object _webBrowsingLogLock = new();
    private static readonly object UiDiagnosticsLogLock = new();
    private static readonly Stopwatch UiDiagnosticsStopwatch = Stopwatch.StartNew();
    private AI.AiChatView _aiChatView = null!;
    private DispatcherTimer? _uiDiagnosticsHeartbeatTimer;
    private bool _updatingVolumeFromSystem;
    private CancellationTokenSource _backgroundArtistLoadCts = new();
    private int _activeLyricIndex = -1;
    private bool _updatingViewMode;
    private string? _contentColumnWidthKey;

    private int _dashboardYear;
    private int _dashboardMonth;
    private StatsPeriod _dashboardStatsPeriod = StatsPeriod.Last30Days;
    private StackPanel? _calendarInner;
    private bool _dashboardResizeHooked;
    private bool? _dashboardTwoColumnLayout;
    private int _dashboardBuildVersion;
    private StackPanel? _dashboardRootPanel;
    private readonly Dictionary<string, long> _dashboardRemoteLibraryVersions = new(StringComparer.Ordinal);
    private string? _pendingInitialNavigationTag;
    private bool _suppressNavSelectionChanged;
    private string? _currentTopLevelTag;

    private static readonly Color[] _genreColors =
    [
        Color.FromRgb(0x6C, 0x63, 0xFF),
        Color.FromRgb(0xFF, 0x6B, 0x9D),
        Color.FromRgb(0xFF, 0x9F, 0x43),
        Color.FromRgb(0x1D, 0xD1, 0xA1),
        Color.FromRgb(0x54, 0xA0, 0xFF),
        Color.FromRgb(0xFE, 0xCE, 0x00),
        Color.FromRgb(0xC4, 0x4E, 0xFC),
        Color.FromRgb(0xFF, 0x6B, 0x6B),
        Color.FromRgb(0x2E, 0xCC, 0x71),
        Color.FromRgb(0x3C, 0xC7, 0xF0),
    ];

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dsf", ".dff", ".flac", ".mp3", ".wav", ".aiff", ".aif",
        ".m4a", ".mka", ".aac", ".ogg", ".opus", ".wma"
    };
    private static readonly string[] AlphabetIndexLabels =
        ["#", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
         "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"];

    private sealed record FolderTag(
        bool IsFile,
        string FilePath,
        string FolderPath,
        OrynivoServerSettings? Server = null);
    private sealed record PlexFolderTag(string Key, bool IsTrack, ContentRow? Track);
    private sealed record AlphabetTreeTarget(string Key, TreeViewItem Item);
    private sealed record OrynivoFolderTrackCache(
        long LibraryChangedAt,
        long CachedAt,
        List<OrynivoTrackLiteInfo> Tracks);
    private sealed record OrynivoTrackListCache(
        long LibraryChangedAt,
        long CachedAt,
        List<LibraryCatalogTrack> Tracks,
        int SchemaVersion);
    private sealed record OrynivoArtistListCache(
        long LibraryChangedAt,
        long CachedAt,
        List<LibraryCatalogArtist> Artists);
    private sealed record OrynivoAlbumListCache(
        long LibraryChangedAt,
        long CachedAt,
        List<LibraryCatalogAlbum> Albums);
    private sealed record PlexNavigationState(
        string Title,
        string View,
        IReadOnlyList<ContentRow> Rows);
    private sealed record PlaylistActionTag(ILibraryPlaylistProvider Provider, long PlaylistId, PlaylistSelection Selection);
    private sealed record NewPlaylistActionTag(ILibraryPlaylistProvider Provider, PlaylistSelection Selection);
    private sealed record OrynivoPlaylistMenuTag(OrynivoServerSettings Server, long PlaylistId, IReadOnlyList<long> TrackIds);
    private sealed record RemovePlaylistEntryTag(long PlaylistEntryId);
    private sealed record RemoveOrynivoPlaylistEntryTag(OrynivoServerSettings Server, long PlaylistEntryId);
    private sealed record PodcastPlayback(PodcastRecord Podcast, PodcastEpisode Episode);
    private sealed record NavigationState(
        string View,
        long? SelectedId,
        long? ArtistFilterId,
        string? ArtistFilterName,
        string? SearchQuery = null,
        double? VerticalOffset = null,
        string? NavigationTag = null,
        string? SelectedSourceKey = null,
        string? GenreKey = null,
        bool? GenreAlbums = null,
        IReadOnlyList<long>? LogicalAlbumIds = null,
        IReadOnlyList<LogicalAlbumPart>? LogicalAlbumParts = null);


    // ------------------------------------------------------------------
    // Initialisierung
    // ------------------------------------------------------------------

    public MainWindow()
    {
        using var timing = StartupTimingLog.Time("MainWindow constructor");
        using (StartupTimingLog.Time("MainWindow.InitializeComponent"))
            InitializeComponent();
        StartUiDiagnosticsLog();
        LogUiDiagnostics("MainWindow constructor after InitializeComponent");
        _dashboardRootPanel = DashboardPanel;
        _animatedViewSurfaces =
        [
            DashboardScrollViewer,
            InternetRadioView,
            PodcastView,
            PodcastEpisodesView,
            ContentDataGrid,
            AlbumFolderGroupsScrollViewer,
            SearchResultsScrollViewer,
            FolderTreeView,
            AlbumArtworkListBox,
            ArtistArtworkListBox
        ];
        LyricsListBox.ItemsSource = _lyricLines;
        // Recompute the transport accent whenever the now-playing cover changes,
        // regardless of which code path set it (local, remote, gapless, async).
        NowPlayingArtworkImage.GetObservable(Image.SourceProperty)
            .Subscribe(new AnonymousObserver<IImage?>(UpdateTransportAccentFromArtwork));
        Opened += OnWindowOpened;
        _transportTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _transportTimer.Tick += (_, _) => RefreshTransport();
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            if (_currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true &&
                _activeOrynivoView == "Tracks")
            {
                await ShowOrynivoSearchResultsAsync(SearchTextBox.Text ?? string.Empty);
            }
            else
            {
                await ShowSearchResultsAsync(SearchTextBox.Text ?? string.Empty);
            }
        };
        using (StartupTimingLog.Time("MainWindow.LoadSettings"))
            LoadSettings();
        UpdateOutputDeviceLockButton();
        RestoreWindowPlacement();
        PositionChanged += (_, _) => CaptureNormalWindowPlacement();
        SizeChanged += (_, _) => CaptureNormalWindowPlacement();
        LogUiDiagnostics(
            $"MainWindow LoadSettings completed libraryPaths={_settings.LibraryPaths?.Count ?? 0} orynivoServers={_settings.OrynivoServers?.Count ?? 0} lastView={_settings.LastMainView}");
        using (StartupTimingLog.Time("MainWindow.InitMcpBridge"))
            InitMcpBridge();
        _mcpBridge.DisabledTools = _settings.DisabledMcpTools;
        _webBrowsing = new Orynivo.Web.WebBrowsingService(_settings.WebBrowsing)
        {
            RequestLog = AppendWebBrowsingLog,
        };
        _mcpBridge.WebBrowsing = _webBrowsing;
        _aiChatView = AiChatViewControl;
        _aiChatView.SetBridge(_mcpBridge);
        _aiChatView.GetSettings = () => _settings.AiChat;
        if (_settings.McpServerEnabled)
        {
            StartupTimingLog.Write("MainWindow starting MCP server");
            _ = _mcpServer.StartAsync(
                _settings.McpServerPort,
                _mcpBridge,
                _settings.McpNetworkAccessEnabled,
                _settings.McpAccessToken);
        }
        if (_settings.MobileRemoteEnabled)
            _ = _mobileRemoteServer.StartAsync(
                _settings.MobileRemotePort,
                _settings.MobileRemoteAccessToken,
                _mcpBridge);
        using (StartupTimingLog.Time("MainWindow.RestorePlaybackQueueState"))
            RestorePlaybackQueueState();
        LogUiDiagnostics("MainWindow RestorePlaybackQueueState completed");
        // Remote queue entries are persisted as credential-free stable references.
        // Hydrate their metadata after startup so restoring the queue never blocks
        // construction of the main window on network I/O.
        _ = HydrateRestoredOrynivoQueueAsync();
        _libraryWatcher = new LibraryWatcherService(
            OnWatchedLibraryChanged,
            OnLibraryScanActivity,
            _settings.CalculateMissingReplayGainDuringScan);
        using (StartupTimingLog.Time("MainWindow.LibraryWatcher.UpdatePaths"))
            _libraryWatcher.UpdatePaths(_settings.LibraryPaths ?? []);
        LogUiDiagnostics("MainWindow LibraryWatcher.UpdatePaths completed");

        // Poll configured remote servers for in-progress scans so their progress shows in the
        // sidebar activity line without blocking or reloading the current view.
        _remoteScanPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _remoteScanPollTimer.Tick += (_, _) => _ = PollRemoteServerScansAsync();
        _remoteScanPollTimer.Start();

        SetupQueueDragAndDrop();
        using (StartupTimingLog.Time("MainWindow.RestoreFixedDataGridColumnWidths"))
            RestoreFixedDataGridColumnWidths();
        using (StartupTimingLog.Time("MainWindow.AttachDataGridColumnChoosers"))
            AttachDataGridColumnChoosers();
        LogUiDiagnostics("MainWindow data grid setup completed");
        using (StartupTimingLog.Time("MainWindow.LoadCatalogFilterCache"))
            LoadCatalogFilterCache();
        using (StartupTimingLog.Time("MainWindow.LoadNavPlaylists"))
            LoadNavPlaylists();
        LogUiDiagnostics("MainWindow LoadNavPlaylists completed");
        _showAlbumArtworkView = _settings.AlbumArtworkView;
        _showArtistArtworkView = _settings.ArtistArtworkView;
        VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 1);
        AlbumArtworkViewRadioButton.IsChecked = _showAlbumArtworkView;
        AlbumTableViewRadioButton.IsChecked = !_showAlbumArtworkView;
        using (StartupTimingLog.Time("MainWindow.SelectInitialView"))
        {
            LogUiDiagnostics("MainWindow SelectInitialView starting");
            SelectInitialView();
            LogUiDiagnostics($"MainWindow SelectInitialView completed currentTag={_currentTopLevelTag ?? "<null>"}");
        }
        using (StartupTimingLog.Time("MainWindow.RestoreLastTrackState"))
            RestoreLastTrackState();
        LogUiDiagnostics("MainWindow constructor completed");
    }

    /// <summary>Sets the sidebar status text from startup/background services.</summary>
    /// <param name="status">The status text to show.</param>
    internal void SetStatusText(string status)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetStatusText(status));
            return;
        }

        StatusTextBlock.Text = status;
    }

    /// <summary>Clears the sidebar status text when it still matches <paramref name="expectedStatus"/>.</summary>
    /// <param name="expectedStatus">The status text that must still be visible before clearing.</param>
    internal void ClearStatusText(string expectedStatus)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ClearStatusText(expectedStatus));
            return;
        }

        if (string.Equals(StatusTextBlock.Text, expectedStatus, StringComparison.Ordinal))
            StatusTextBlock.Text = string.Empty;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        WindowChrome.ApplyTheme(this);
        AlbumArtworkListBox.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new EventHandler<ScrollChangedEventArgs>(AlphabetTarget_OnScrollChanged),
            handledEventsToo: true);
        ArtistArtworkListBox.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new EventHandler<ScrollChangedEventArgs>(AlphabetTarget_OnScrollChanged),
            handledEventsToo: true);
        FolderTreeView.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new EventHandler<ScrollChangedEventArgs>(AlphabetTarget_OnScrollChanged),
            handledEventsToo: true);
        NavListBox.AddHandler(
            PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>(NavListBox_OnPreviewMouseRightButtonDown),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        NavListBox.AddHandler(
            PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>(NavListBox_OnPreviewMouseLeftButtonDown),
            handledEventsToo: true);
        PositionSlider.AddHandler(WaveformProgressControl.PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>(PositionSlider_OnPreviewMouseLeftButtonDown),
            handledEventsToo: true);
        PositionSlider.AddHandler(WaveformProgressControl.PointerMovedEvent,
            new EventHandler<PointerEventArgs>(PositionSlider_OnPointerMoved),
            handledEventsToo: true);
        PositionSlider.AddHandler(WaveformProgressControl.PointerReleasedEvent,
            new EventHandler<PointerReleasedEventArgs>(PositionSlider_OnPreviewMouseLeftButtonUp),
            handledEventsToo: true);
        PositionSlider.PointerCaptureLost += PositionSlider_OnPointerCaptureLost;
        PositionSlider.ValueChanged += PositionSlider_OnValueChanged;
        _ = ConfigureEndpointVolumeSynchronizationAsync();
        ConfigureWindowsMediaTransport();
        QueueHydrateVisibleArtworkRows(AlbumArtworkListBox);
        QueueHydrateVisibleArtworkRows(ArtistArtworkListBox);
        // Avalonia DataGrid owns a dedicated pixel-based vertical ScrollBar instead of
        // exposing its vertical movement through a ScrollViewer.
        ContentDataGrid.VerticalScroll += ContentDataGrid_OnVerticalScroll;
        Dispatcher.UIThread.Post(AttachContentDataGridVerticalScrollBar, DispatcherPriority.Loaded);
        if (!_settings.UserProfilesInitialized)
            _ = PromptInitialUserProfileAsync();
        _ = WarmSimilarityFeatureCacheAsync();
        StartScheduledBackupTimer();
    }

    private async Task PromptInitialUserProfileAsync()
    {
        if (_settings.UserProfilesInitialized || _profileManager is null)
            return;
        var dialog = new UserProfileDialog();
        if (await dialog.ShowDialog<bool>(this) && !string.IsNullOrWhiteSpace(dialog.ProfileName))
        {
            // The first profile represents the existing installation. Rename
            // Standard in place so no personal history or favorites are lost.
            _profileManager.Rename("standard", dialog.ProfileName);
            var profile = _profileManager.ActiveProfile;
            _settings.UserProfilesInitialized = true;
            if (dialog.MigrateFavorites)
                await Task.Run(() => AudioDatabase.MigrateLegacyFavoritesToProfile(profile.Id));
            AudioDatabase.SetActiveProfile(profile.Id);
            ApplyServerProfileContext();
            _settingsStore.Save(_settings);
        }
        else
        {
            // Do not ask repeatedly when the user intentionally skips setup.
            _settings.UserProfilesInitialized = true;
            _settingsStore.Save(_settings);
        }
    }

    private void InitMcpBridge()
    {
        using var db = Library.AudioDatabase.OpenDefault();
        _mcpBridge.GetStateFunc = () =>
        {
            var status = _player is null ? "stopped"
                : _player.IsPaused ? "paused"
                : "playing";
            var currentQueueItem = _queueIndex >= 0 && _queueIndex < _queue.Count
                ? _queue[_queueIndex]
                : null;
            return new Mcp.PlayerState(
                status,
                NowPlayingTitleBlock.Text,
                NowPlayingArtistBlock.Text,
                currentQueueItem?.Album,
                _player is not null ? currentQueueItem?.FilePath : null,
                _player?.Position.TotalSeconds ?? 0,
                _player?.Duration.TotalSeconds ?? 0,
                VolumeSlider.Value,
                _queueIndex,
                _queue.Count);
        };
        _mcpBridge.GetQueueFunc = () =>
            _queue.Select((item, i) => new Mcp.QueueEntry(
                i, i == _queueIndex, item.FilePath, item.DisplayTitle)).ToList();
        _mcpBridge.GetCurrentArtworkFunc = ResolveMobileRemoteArtworkAsync;
        _mcpBridge.PlayQueueIndexFunc = async index =>
        {
            if (index < 0 || index >= _queue.Count)
                return false;
            _queueIndex = index;
            await StartPlaybackAsync(_queue[index].FilePath);
            return true;
        };
        _mcpBridge.SearchMobileTracksFunc = SearchMobileRemoteTracksAsync;
        _mcpBridge.BrowseMobileArtistsFunc = BrowseMobileRemoteArtistsAsync;
        _mcpBridge.BrowseMobileAlbumsFunc = BrowseMobileRemoteAlbumsAsync;
        _mcpBridge.BrowseMobileAlbumTracksFunc = BrowseMobileRemoteAlbumTracksAsync;
        _mcpBridge.BrowseMobilePlaylistsFunc = BrowseMobileRemotePlaylistsAsync;
        _mcpBridge.BrowseMobilePlaylistTracksFunc = BrowseMobileRemotePlaylistTracksAsync;
        _mcpBridge.QueueMobilePlaylistFunc = QueueMobileRemotePlaylistAsync;
        _mcpBridge.QueueMobileTrackFunc = QueueMobileRemoteTrackAsync;
        _mcpBridge.GetCurrentFavoriteFunc = () =>
            _currentTrackId.HasValue || CurrentOrynivoFavoriteTarget is not null
                ? _currentTrackIsFavorite
                : null;
        _mcpBridge.EditMobileQueueFunc = EditMobileRemoteQueueAsync;
        _mcpBridge.PlayFileFunc    = path => StartPlaybackAsync(path);
        _mcpBridge.TogglePauseFunc = TogglePlaybackAsync;
        _mcpBridge.SkipNextFunc    = PlayNextAsync;
        _mcpBridge.SkipPreviousFunc = PlayPreviousAsync;
        _mcpBridge.StopFunc        = StopPlayback;
        _mcpBridge.SeekFunc        = async seconds =>
        {
            if (_player?.CanSeek == true)
                await _player.SeekAsync(TimeSpan.FromSeconds(seconds));
        };
        _mcpBridge.SetVolumeFunc   = v => VolumeSlider.Value = Math.Clamp(v, 0, 1);
        _mcpBridge.AppendToQueueFunc = async path =>
        {
            _queue.Add(CreatePlaylistItem(path));
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            await RefreshActiveGaplessQueueAsync();
        };
        _mcpBridge.PlayNextFunc = async path =>
        {
            var insertIndex = Math.Clamp(_queueIndex + 1, 0, _queue.Count);
            _queue.Insert(insertIndex, CreatePlaylistItem(path));
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            await RefreshActiveGaplessQueueAsync();
        };
        _mcpBridge.ClearQueueFunc = () => ClearPlaybackQueue();
        _mcpBridge.ReplaceQueueFunc = async paths =>
        {
            _queue.Clear();
            foreach (var p in paths)
                _queue.Add(CreatePlaylistItem(p));
            _queueIndex = paths.Count > 0 ? 0 : -1;
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            if (paths.Count > 0)
                await StartPlaybackAsync(paths[0]);
        };
        _mcpBridge.RefreshPlaylistsFunc = LoadNavPlaylists;
        _mcpBridge.GetOrynivoServersFunc = () => _settings.OrynivoServers ?? [];
        _mcpBridge.ResolveRemoteTrackFunc = ResolveRemoteMcpTrackAsync;
        _mcpBridge.SetCurrentFavoriteFunc = SetCurrentTrackFavorite;
        _mcpBridge.SetTracksFavoriteFunc = SetTracksFavoriteByPathsAsync;
        _mcpBridge.SetTracksRatingFunc = SetTracksRatingByPathsAsync;
        _mcpBridge.CreateSimilarPlaylistFunc = CreateSimilarPlaylistByPathAsync;
        _mcpBridge.ControlInfiniteMixFunc = ControlInfiniteMixAsync;
        _mcpBridge.GetOutputProfilesFunc = () => (_settings.OutputProfiles ?? [])
            .Select(profile => string.Equals(profile.Name, _settings.SelectedOutputProfileName, StringComparison.OrdinalIgnoreCase)
                ? $"{profile.Name} (selected)"
                : profile.Name)
            .ToList();
        _mcpBridge.SelectOutputProfileFunc = SelectOutputProfileByNameAsync;
        _mcpBridge.GetEqualizerProfilesFunc = () => new Mcp.EqualizerProfileState(
            (_settings.EqualizerProfiles ?? []).Select(profile => profile.Name).ToList(),
            _settings.SelectedEqualizerProfileName,
            _settings.EqualizerEnabled);
        _mcpBridge.ConfigureEqualizerFunc = ConfigureEqualizerAsync;
        _mcpBridge.GetCurrentLyricsFunc = GetCurrentCachedLyricsForToolAsync;
        _mcpBridge.GetOrynivoServerNamesFunc = () => (_settings.OrynivoServers ?? [])
            .Select(server => server.Name)
            .ToList();
        _mcpBridge.TriggerOrynivoServerScanFunc = TriggerOrynivoServerScanByNameAsync;
    }

    /// <summary>
    /// Resolves a path supplied by an MCP/AI tool into a playable path. A remote Orynivo
    /// Server reference (<c>orynivo://serverId/track/trackId</c>) is resolved to the real
    /// authenticated stream URL, and the track's metadata is registered in
    /// <see cref="_orynivoTracksByUrl"/> so the transport, history, lyrics, and favorite
    /// button work exactly like a track opened from the UI. Any other path is returned
    /// unchanged so local files and already-real URLs pass straight through.
    /// </summary>
    /// <param name="path">The tool-supplied path or remote reference.</param>
    /// <returns>The playable path, or <see langword="null"/> when a remote reference cannot be resolved.</returns>
    private async Task<string?> ResolveRemoteMcpTrackAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        if (!path.StartsWith("orynivo://", StringComparison.OrdinalIgnoreCase))
            return path;
        if (!TryResolveOrynivoPlaylistReference(path, out var server, out var trackId))
            return null;

        try
        {
            var tracks = await _orynivoClient.GetTracksByIdsAsync(server, [trackId], CancellationToken.None);
            var track = tracks.FirstOrDefault(t => t.Id == trackId);
            if (track is null)
                return null;
            // ToOrynivoTrackContentRow registers the row in _orynivoTracksByUrl keyed by the
            // real stream URL, which is what CreatePlaylistItem/StartPlaybackAsync consume.
            // Build/register on the UI thread because that cache and the transport are UI-affine.
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var row = ToOrynivoTrackContentRow(server, track);
                EnsureArtworkHydrated(row);
                return row.FilePath;
            });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Appends one audit line to the web-browsing request log.</summary>
    /// <param name="line">The already-formatted log line.</param>
    private void AppendWebBrowsingLog(string line)
    {
        try
        {
            var path = AppPaths.GetDataPath("logs", "web-browsing.log");
            lock (_webBrowsingLogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging failures must never affect tool execution.
        }
    }

    private void StartUiDiagnosticsLog()
    {
        try
        {
            var path = AppPaths.GetDataPath("logs", "ui-diagnostics-latest.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            lock (UiDiagnosticsLogLock)
            {
                File.WriteAllText(
                    path,
                    $"Orynivo UI diagnostics{Environment.NewLine}" +
                    $"======================{Environment.NewLine}" +
                    $"Timestamp: {DateTimeOffset.Now:O}{Environment.NewLine}" +
                    $"Process ID: {Environment.ProcessId}{Environment.NewLine}" +
                    $"Data root: {AppPaths.DataRoot}{Environment.NewLine}{Environment.NewLine}",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            UiDiagnosticsStopwatch.Restart();
            LogUiDiagnostics("diagnostics started");
            _uiDiagnosticsHeartbeatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _uiDiagnosticsHeartbeatTimer.Tick += (_, _) =>
                LogUiDiagnostics(
                    $"heartbeat currentTag={_currentTopLevelTag ?? "<null>"} gridVisible={ContentDataGrid.IsVisible} items={GetDiagnosticItemCount(ContentDataGrid.ItemsSource)} alphabetVisible={AlphabetIndexBorder.IsVisible}");
            _uiDiagnosticsHeartbeatTimer.Start();
        }
        catch
        {
            // Diagnostic logging must never affect startup.
        }
    }

    private static void LogUiDiagnostics(string message)
    {
        try
        {
            var path = AppPaths.GetDataPath("logs", "ui-diagnostics-latest.log");
            var line =
                $"[{DateTimeOffset.Now:O}] [{UiDiagnosticsStopwatch.ElapsedMilliseconds,8:N0} ms] " +
                $"[thread {Environment.CurrentManagedThreadId}] " +
                $"{message}{Environment.NewLine}";
            lock (UiDiagnosticsLogLock)
                File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Diagnostic logging must never affect UI work.
        }
    }

    private static int GetDiagnosticItemCount(object? itemsSource) =>
        itemsSource switch
        {
            System.Collections.ICollection collection => collection.Count,
            System.Collections.IEnumerable => -2,
            _ => -1
        };

    protected override void OnClosed(EventArgs e)
    {
        LogUiDiagnostics("OnClosed started");
        _uiDiagnosticsHeartbeatTimer?.Stop();
        _uiDiagnosticsHeartbeatTimer = null;
        CloseEmbeddedSettings();
        _ = _mcpServer.StopAsync();
        _ = _mobileRemoteServer.StopAsync();
        ContentDataGrid.VerticalScroll -= ContentDataGrid_OnVerticalScroll;
        _libraryWatcher?.Dispose();
        _libraryWatcher = null;
        DisposeEndpointVolumeSynchronizationInBackground();
        CaptureAllDataGridColumnWidths();
        CaptureNormalWindowPlacement();
        PersistViewState();
        CancelAndDispose(ref _radioSearchCts);
        CancelAndDispose(ref _podcastSearchCts);
        CancelAndDispose(ref _podcastFeedCts);
        CancelAndDispose(ref _plexViewCts);
        CancelAndDispose(ref _unifiedLibraryAppendCts);
        _musicBrainzBackgroundCts.Cancel();
        CancelAudioFeatureWarmup();
        StopPlayback();
        _windowsMediaTransport?.Dispose();
        _windowsMediaTransport = null;
        base.OnClosed(e);
    }

}

