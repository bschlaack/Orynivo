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
    private const string LocalSourceKey = "local";
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
    private CancellationTokenSource? _playbackCts;
    private readonly SettingsStore _settingsStore = new();
    private AppSettings _settings = new();
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
        ".m4a", ".aac", ".ogg", ".opus", ".wma"
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
        List<LibraryCatalogTrack> Tracks);
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
        string? NavigationTag = null);

    private sealed class RadioStationViewModel
    {
        public required string StationUuid { get; init; }
        public required string Name { get; init; }
        public required string StreamUrl { get; init; }
        public string? Homepage { get; init; }
        public string? Favicon { get; init; }
        public string? CountryCode { get; init; }
        public string? Codec { get; init; }
        public int Bitrate { get; init; }
        public string? Tags { get; init; }
        public IReadOnlyList<string> Genres { get; init; } = [];
        public string FormatSummary => Bitrate > 0
            ? $"{Codec ?? "Audio"} · {Bitrate} kbps"
            : Codec ?? "Audio";
        public string BitrateSummary => Bitrate > 0 ? $"{Bitrate:N0} kbps" : string.Empty;
        public string GenreSummary => string.Join(", ", Genres.Take(3));

        public RadioBrowserStation ToBrowserStation() =>
            new(StationUuid, Name, StreamUrl, Homepage, Favicon, CountryCode, Codec, Bitrate, Tags);

        public RadioStationRecord ToRecord(long id = 0) =>
            new(id, StationUuid, Name, StreamUrl, Homepage, Favicon, CountryCode, Codec, Bitrate, Tags);
    }

    private sealed class PodcastViewModel
    {
        public long CollectionId { get; init; }
        public required string Name { get; init; }
        public string? Author { get; init; }
        public required string FeedUrl { get; init; }
        public string? ArtworkUrl { get; init; }
        public string? Genre { get; init; }
        public IReadOnlyList<string> Genres { get; init; } = [];
        public IReadOnlyList<string> GenreIds { get; init; } = [];
        public string? Language { get; set; }
        public string LanguageDisplay => FormatPodcastLanguage(Language);

        public PodcastSearchResult ToSearchResult() =>
            new(CollectionId, Name, Author, FeedUrl, ArtworkUrl, Genre, Genres, GenreIds, Language);

        public PodcastRecord ToRecord(long id = 0) =>
            new(id, CollectionId, Name, Author, FeedUrl, ArtworkUrl, Genre);
    }

    private sealed class PodcastEpisodeViewModel
    {
        public required PodcastEpisode Episode { get; init; }
        public required string Title { get; init; }
        public required string Published { get; init; }
        public required string Duration { get; init; }
        public required string Progress { get; init; }
        public required string Status { get; init; }
    }

    private sealed class ContentRow : INotifyPropertyChanged
    {
        public string? Nr          { get; set; }
        public long? Id            { get; init; }
        public long? ArtistId       { get; set; }
        public long? AlbumId        { get; set; }
        public string? Title       { get; init; }
        public string? AlphabetIndexText { get; init; }
        public string? Artist      { get; init; }
        public bool HasArtist => !string.IsNullOrWhiteSpace(Artist);
        public string? Album       { get; init; }
        public string? AlbumArtist { get; init; }
        public string? Year        { get; init; }
        public string? TrackNumber { get; init; }
        public string? DiscNumber  { get; init; }
        public string? Genre       { get; init; }
        public string? Bitrate     { get; init; }
        public string? SampleRate  { get; init; }
        public int?    SampleRateHz { get; init; }
        public string? BitDepth    { get; init; }
        public string? Channels    { get; init; }
        public int?    ChannelCount { get; init; }
        public string? Composer    { get; init; }
        public string? Bpm         { get; init; }
        public string? FileName    { get; init; }
        public string? FileSize    { get; init; }
        public string? AddedAt     { get; init; }
        public string? ReplayGainTrack { get; init; }
        public string? ReplayGainAlbum { get; init; }
        public string? Folder      { get; init; }
        public string? ArtworkPath { get; set; }
        public string? ThumbnailPath { get; set; }
        public string? Biography { get; set; }
        public string? SourceUrl { get; set; }
        public string? ProfileLanguage { get; set; }
        public long? ProfileFetchedAt { get; set; }
        public bool ImageIsManual { get; set; }
        public string EntityType { get; set; } = "Track";
        public string? ExternalId { get; init; }
        /// <summary>Gets the Plex server identifier a Plex track/album/artist row belongs to, or <see langword="null"/>.</summary>
        public string? PlexServerId { get; init; }
        /// <summary>Gets the Plex album (parent) rating key for a Plex track row, or <see langword="null"/>.</summary>
        public string? PlexAlbumRatingKey { get; init; }
        /// <summary>Gets the Plex artist (grandparent) rating key for a Plex track row, or <see langword="null"/>.</summary>
        public string? PlexArtistRatingKey { get; init; }
        public OrynivoServerSettings? OrynivoServer { get; set; }
        public string SourceKey => OrynivoServer is null ? LocalSourceKey : GetServerSourceKey(OrynivoServer.Id);
        public string SourceBadge => EntityType == "UnifiedArtist"
            ? $"{LocalizationManager.Current.LocalSourceShort}+OS"
            : OrynivoServer is null ? LocalizationManager.Current.LocalSourceShort : "OS";
        public string? SourceName => EntityType == "UnifiedArtist"
            ? $"{LocalizationManager.Current.LocalSource} + OS"
            : OrynivoServer?.Name ?? LocalizationManager.Current.LocalSource;
        private IImage? _artwork;
        private IImage? _thumbnail;
        public bool ArtworkLoadQueued { get; set; }
        public bool ArtworkLoadCompleted { get; set; }
        public bool ThumbnailLoadQueued { get; set; }
        public bool ThumbnailLoadCompleted { get; set; }
        public IImage? Artwork
        {
            get => _artwork;
            set => SetField(ref _artwork, value);
        }
        public IImage? Thumbnail
        {
            get => _thumbnail;
            set => SetField(ref _thumbnail, value);
        }
        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite == value)
                    return;
                _isFavorite = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteGlyph)));
            }
        }
        public string FavoriteGlyph => IsFavorite ? "❤" : "♡";
        public string  Duration    { get; init; } = "";
        public string? Format      { get; init; }
        public string  FilePath    { get; init; } = "";
        public string? SourcePath   { get; init; }
        public IReadOnlyList<string>? PlexPartUrls { get; init; }
        public TimeSpan? KnownDuration { get; init; }
        public long?   PlaylistEntryId { get; set; }
        public PlaylistItem? QueueItem { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class LyricLineViewModel : INotifyPropertyChanged
    {
        private bool _isActive;

        public LyricLineViewModel(string text, TimeSpan? time)
        {
            Text = text;
            Time = time;
        }

        public string Text { get; }
        public TimeSpan? Time { get; }
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                    return;
                _isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

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
            _ = _mcpServer.StartAsync(_settings.McpServerPort, _mcpBridge);
        }
        using (StartupTimingLog.Time("MainWindow.RestorePlaybackQueueState"))
            RestorePlaybackQueueState();
        LogUiDiagnostics("MainWindow RestorePlaybackQueueState completed");
        _libraryWatcher = new LibraryWatcherService(OnWatchedLibraryChanged, OnLibraryScanActivity);
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
    }

    private void InitMcpBridge()
    {
        using var db = Library.AudioDatabase.OpenDefault();
        _mcpBridge.GetStateFunc = () =>
        {
            var status = _player is null ? "stopped"
                : _player.IsPaused ? "paused"
                : "playing";
            return new Mcp.PlayerState(
                status,
                NowPlayingTitleBlock.Text,
                NowPlayingArtistBlock.Text,
                null,
                _player is not null && _queueIndex >= 0 && _queueIndex < _queue.Count
                    ? _queue[_queueIndex].FilePath : null,
                _player?.Position.TotalSeconds ?? 0,
                _player?.Duration.TotalSeconds ?? 0,
                VolumeSlider.Value,
                _queueIndex,
                _queue.Count);
        };
        _mcpBridge.GetQueueFunc = () =>
            _queue.Select((item, i) => new Mcp.QueueEntry(
                i, i == _queueIndex, item.FilePath, item.DisplayTitle)).ToList();
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
        ContentDataGrid.VerticalScroll -= ContentDataGrid_OnVerticalScroll;
        _libraryWatcher?.Dispose();
        _libraryWatcher = null;
        DisposeEndpointVolumeSynchronizationInBackground();
        CaptureAllDataGridColumnWidths();
        PersistViewState();
        CancelAndDispose(ref _radioSearchCts);
        CancelAndDispose(ref _podcastSearchCts);
        CancelAndDispose(ref _podcastFeedCts);
        CancelAndDispose(ref _plexViewCts);
        CancelAndDispose(ref _unifiedLibraryAppendCts);
        StopPlayback();
        _windowsMediaTransport?.Dispose();
        _windowsMediaTransport = null;
        base.OnClosed(e);
    }

    private void SelectInitialView()
    {
        var tag = _settings.LastMainView;
        ApplySidebarNavigationSettings();
        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.Ordinal));
        NavListBox.SelectedItem = item
            ?? NavListBox.Items.OfType<ListBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, "Tracks", StringComparison.Ordinal));
    }

    private void PersistViewState()
    {
        if (NavListBox.SelectedItem is ListBoxItem { Tag: string tag } &&
            tag is "Dashboard" or "InternetRadio" or "Podcasts" or "Queue" or "Artists" or "Albums" or "Tracks" or "Folders")
        {
            _settings.LastMainView = tag;
        }
        _settings.AlbumArtworkView = _showAlbumArtworkView;
        _settings.ArtistArtworkView = _showArtistArtworkView;
        _settings.Volume = VolumeSlider.Value;
        _settings.LastTrackPath = IsAvailableLocalTrack(_currentFilePath) ? _currentFilePath : null;
        CapturePlaybackQueueState();
        _settingsStore.Save(_settings);
    }

    private void RestoreFixedDataGridColumnWidths()
    {
        RestoreColumnWidths("RadioStations", RadioStationsDataGrid);
        RestoreColumnWidths("Podcasts", PodcastsDataGrid);
        RestoreColumnWidths("PodcastEpisodes", PodcastEpisodesDataGrid);
    }

    private void AttachDataGridColumnChoosers()
    {
        DataGridColumnChooser.Attach(
            ContentDataGrid,
            () => _contentColumnWidthKey ?? "Content.Tracks",
            _settings);
        DataGridColumnChooser.Attach(SearchTracksDataGrid, "SearchTracks", _settings);
        DataGridColumnChooser.Attach(SearchAlbumsDataGrid, "SearchAlbums", _settings);
        DataGridColumnChooser.Attach(SearchArtistsDataGrid, "SearchArtists", _settings);
        DataGridColumnChooser.Attach(RadioStationsDataGrid, "RadioStations", _settings);
        DataGridColumnChooser.Attach(PodcastsDataGrid, "Podcasts", _settings);
        DataGridColumnChooser.Attach(PodcastEpisodesDataGrid, "PodcastEpisodes", _settings);
    }

    private void CaptureAllDataGridColumnWidths()
    {
        CaptureContentDataGridColumnWidths();
        CaptureColumnWidths("RadioStations", RadioStationsDataGrid);
        CaptureColumnWidths("Podcasts", PodcastsDataGrid);
        CaptureColumnWidths("PodcastEpisodes", PodcastEpisodesDataGrid);
        CaptureColumnWidths("SearchTracks", SearchTracksDataGrid);
        CaptureColumnWidths("SearchAlbums", SearchAlbumsDataGrid);
        CaptureColumnWidths("SearchArtists", SearchArtistsDataGrid);
    }

    private void CaptureContentDataGridColumnWidths()
    {
        if (!string.IsNullOrWhiteSpace(_contentColumnWidthKey))
            CaptureColumnWidths(_contentColumnWidthKey, ContentDataGrid);
    }

    private void CaptureColumnWidths(string key, DataGrid grid) =>
        DataGridColumnWidthStore.Capture(_settings.DataGridColumnWidths, key, grid);

    private void RestoreColumnWidths(string key, DataGrid grid) =>
        DataGridColumnWidthStore.Restore(_settings.DataGridColumnWidths, key, grid);

    private void LoadSettings()
    {
        _settings = _settingsStore.Load();
        _settings.DataGridColumnWidths ??= new Dictionary<string, List<double>>(StringComparer.Ordinal);
        _settings.VisibleDataGridColumns ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
        _settings.DataGridColumnOrders ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
        _settings.PlaybackQueuePaths ??= [];
        _settings.CollapsedOrynivoServerLibraryGroups ??= new HashSet<string>(StringComparer.Ordinal);
        _settings.CollapsedOrynivoServerPlaylistGroups ??= new HashSet<string>(StringComparer.Ordinal);
        if (_settings.OutputBackend == OutputBackend.Asio && !SteinbergAsioStream.IsAvailable)
        {
            _settings.OutputBackend = SteinbergAsioStream.IsCwAsioAvailable
                ? OutputBackend.CwAsio
                : OutputBackend.Wasapi;
            _settings.SelectedDriverName = null;
            _settingsStore.Save(_settings);
        }
        else if (_settings.OutputBackend == OutputBackend.CwAsio && !SteinbergAsioStream.IsCwAsioAvailable)
        {
            _settings.OutputBackend = SteinbergAsioStream.IsAvailable
                ? OutputBackend.Asio
                : OutputBackend.Wasapi;
            _settings.SelectedDriverName = null;
            _settingsStore.Save(_settings);
        }
        RefreshSelectedDriverText();
        ApplyArtistInfoSettings();
    }

    private void ApplyArtistInfoSettings()
    {
        ArtistProfileService.Source = _settings.ArtistInfoSource;
        ArtistProfileService.LastFmApiKey = _settings.LastFmApiKey;
    }

    private void OnWatchedLibraryChanged()
    {
        // Coalesce bursts of change signals into a single UI-thread pass.
        if (Interlocked.Exchange(ref _libraryWatcherRefreshPending, 1) != 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _libraryWatcherRefreshPending, 0);
            try
            {
                // The sidebar playlist list is lightweight metadata, not a content
                // reload, so keep it in sync immediately.
                LoadNavPlaylists();

                // Do NOT auto-reload the visible content view. Instead offer a
                // controlled "new library data available" refresh action on views
                // that can safely reload in place. No automatic navigation.
                if (CanReloadCurrentViewAfterLibraryChange())
                    SetLibraryRefreshAvailable(true);
            }
            catch
            {
                // Background library refreshes must not affect playback or input handling.
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Updates the subtle sidebar activity indicator while the background scanner or
    /// indexer is running. Throttled so a fast per-file scan does not flood the UI thread.
    /// </summary>
    /// <param name="activity">The reported scan-activity snapshot.</param>
    private void OnLibraryScanActivity(LibraryScanActivity activity)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Always render start/stop transitions; throttle intermediate progress.
            var stateChanged = activity.Active != _libraryScanActive;
            if (!stateChanged && activity.Active &&
                (DateTime.UtcNow - _lastLibraryActivityUiUpdate).TotalMilliseconds < 200)
            {
                return;
            }

            _libraryScanActive = activity.Active;
            _lastLibraryActivityUiUpdate = DateTime.UtcNow;
            _localScanText = activity.Active
                ? (activity.Total > 0 && activity.Current > 0
                    ? string.Format(
                        LocalizationManager.Current.LibraryUpdatingWithCount,
                        activity.Current,
                        activity.Total)
                    : LocalizationManager.Current.LibraryUpdating)
                : null;
            UpdateLibraryActivityIndicator();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Shows the subtle sidebar activity line. The local scan takes priority; when no local
    /// scan is running the most recent remote server scan status (if any) is shown instead.
    /// </summary>
    private void UpdateLibraryActivityIndicator()
    {
        var text = _localScanText ?? _remoteScanText;
        LibraryActivityPanel.IsVisible = text is not null;
        if (text is not null)
            LibraryActivityTextBlock.Text = text;
    }

    /// <summary>
    /// Polls every configured remote Orynivo Server for an in-progress scan and surfaces its
    /// progress in the sidebar activity line, without reloading or blocking the current view.
    /// </summary>
    /// <returns>A task representing the asynchronous poll.</returns>
    private async Task PollRemoteServerScansAsync()
    {
        if (_remoteScanPollInProgress)
            return;
        var servers = _settings.OrynivoServers;
        if (servers is null || servers.Count == 0)
        {
            if (_remoteScanText is not null)
            {
                _remoteScanText = null;
                UpdateLibraryActivityIndicator();
            }
            return;
        }

        _remoteScanPollInProgress = true;
        try
        {
            string? scanning = null;
            foreach (var server in servers)
            {
                try
                {
                    var status = await _orynivoClient.GetScanStatusAsync(server, CancellationToken.None);
                    if (status is { IsRunning: true })
                    {
                        scanning = status is { Total: > 0, Current: > 0 }
                            ? string.Format(
                                LocalizationManager.Current.RemoteScanningWithCount,
                                server.Name, status.Current, status.Total)
                            : string.Format(LocalizationManager.Current.RemoteScanning, server.Name);
                        break;
                    }
                }
                catch
                {
                    // Unreachable or older servers are skipped silently.
                }
            }

            _remoteScanText = scanning;
            UpdateLibraryActivityIndicator();
        }
        finally
        {
            _remoteScanPollInProgress = false;
        }
    }

    /// <summary>Shows or hides the per-view "new library data available" refresh action.</summary>
    /// <param name="available">Whether fresh library data is available for the current view.</param>
    private void SetLibraryRefreshAvailable(bool available)
    {
        _libraryRefreshAvailable = available;
        LibraryRefreshButton.IsVisible = available;
    }

    /// <summary>Reloads the current view on demand when the user accepts the refresh prompt.</summary>
    /// <param name="sender">The refresh button.</param>
    /// <param name="e">The click event data.</param>
    private async void LibraryRefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SetLibraryRefreshAvailable(false);
        if (_currentTopLevelTag is { } tag && CanReloadCurrentViewAfterLibraryChange())
            await ShowTopLevelViewAsync(tag);
    }

    private bool CanReloadCurrentViewAfterLibraryChange()
    {
        if (_currentTopLevelTag is not ("Artists" or "Albums" or "Tracks" or "Folders"))
            return false;
        if (_activeAlbumFilterId is not null ||
            _activeArtistFilterId is not null ||
            _activePlaylistId is not null ||
            _activeOrynivoPlaylistId is not null ||
            _activeAlbumCatalogProvider is not null ||
            _activeCatalogAlbum is not null ||
            LyricsView.IsVisible ||
            ArtistInfoView.IsVisible ||
            PodcastInfoView.IsVisible ||
            SearchResultsScrollViewer.IsVisible)
        {
            return false;
        }

        return true;
    }

    private void RestoreLastTrackState()
    {
        var path = _settings.LastTrackPath;
        if (string.IsNullOrWhiteSpace(path) || !IsAvailableLocalTrack(path))
            return;

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(path);
            if (track is null)
                return;

            _currentFilePath = path;
            NowPlayingTitleBlock.Text = track.Title ?? Path.GetFileNameWithoutExtension(path);
            NowPlayingArtistBlock.Text = track.Artist ?? string.Empty;
            var navigationIds = db.GetTrackNavigationIds(path);
            SetNowPlayingAlbum(track.Album, navigationIds.AlbumId, navigationIds.AlbumId is not null);
            var artworkPaths = db.GetArtworkPathsByTrackPath(path);
            NowPlayingArtworkImage.Source = CreateArtworkImage(artworkPaths?.Thumb96Path, 96);
            LyricsBackgroundImage.Source = CreateArtworkImage(
                artworkPaths?.Thumb320Path ?? artworkPaths?.OriginalPath,
                900);
            var trackInfo = db.GetTrackIdAndFavorite(path);
            var artist = db.GetArtistByTrackPath(path);
            _currentTrackId = trackInfo?.Id;
            _currentTrackIsFavorite = trackInfo?.IsFavorite ?? false;
            _currentArtistId = artist?.Id;
            _currentArtistName = artist?.Artist;
            _currentAlbumId = navigationIds.AlbumId;
            NowPlayingArtistButton.IsEnabled = artist is not null;
            ArtistInfoButton.IsEnabled = artist is not null;
            UpdateNowPlayingFavoriteButton();
            LyricsButton.IsEnabled = true;
            _ = LoadLyricsForTrackAsync(path, forceRefresh: false);
            PlayButton.IsEnabled = true;
            SetPlayPauseIcon(isPlaying: false);
        }
        catch
        {
            _currentFilePath = string.Empty;
            NowPlayingTitleBlock.Text = string.Empty;
            NowPlayingArtistBlock.Text = string.Empty;
            ClearNowPlayingAlbum();
            NowPlayingArtworkImage.Source = null;
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
            _currentTrackIsFavorite = false;
            UpdateNowPlayingFavoriteButton();
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
        }
    }

    private void RefreshSelectedDriverText()
    {
        SelectedDriverTextBlock.Text = _settings.OutputBackend switch
        {
            OutputBackend.Asio when !string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
                $"{_settings.SelectedDriverName}  ·  Steinberg ASIO",
            OutputBackend.CwAsio when !string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
                $"{_settings.SelectedDriverName}  ·  cwASIO",
            OutputBackend.Wasapi when !string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceName) =>
                $"{_settings.SelectedWasapiDeviceName}  ·  WASAPI",
            OutputBackend.KernelStreaming => "KernelStreaming",
            _ => LocalizationManager.Current.NoDeviceSelected
        };
    }

    // ------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------

    private void LoadNavPlaylists()
    {
        var selectedTag = (NavListBox.SelectedItem as ListBoxItem)?.Tag as string;
        _suppressNavSelectionChanged = true;
        try
        {
        PlaylistsHeaderItem.ContextFlyout = BuildPlaylistsHeaderContextFlyout();
        foreach (var dynamicItem in NavListBox.Items
                     .OfType<ListBoxItem>()
                     .Where(item => item.Tag is string tag &&
                                    (tag.StartsWith("Radio:", StringComparison.Ordinal) ||
                                     tag.StartsWith("Podcast:", StringComparison.Ordinal) ||
                                     tag.StartsWith("Plex", StringComparison.Ordinal) ||
                                     tag == "LibraryGroup:LocalPlaylists" ||
                                     tag.StartsWith("Playlist:", StringComparison.Ordinal)))
                     .ToList())
            NavListBox.Items.Remove(dynamicItem);

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var podcastHeaderIndex = NavListBox.Items.IndexOf(MyPodcastsHeaderItem);
            var savedRadios = db.GetRadioStations().ToList();
            if (savedRadios.Count == 0 && podcastHeaderIndex >= 0)
            {
                NavListBox.Items.Insert(podcastHeaderIndex++, CreateSidebarHintItem(
                    "Radio:EmptyHint",
                    LocalizationManager.Current.OwnRadiosEmptyHint));
            }

            foreach (var radio in savedRadios)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(CreateSidebarIcon("IconRadio"));
                content.Children.Add(CreateSidebarEntryText(radio.Name));

                NavListBox.Items.Insert(podcastHeaderIndex++, new ListBoxItem
                {
                    Content = content,
                    Tag = $"Radio:{radio.Id}",
                    Theme = FindResource<ControlTheme>("NavItemTheme"),
                    ContextFlyout = BuildDeleteRadioContextFlyout(radio)
                });
            }

            var plexHeaderIndex = NavListBox.Items.IndexOf(PlexHeaderItem);
            var savedPodcasts = db.GetPodcasts().ToList();
            if (savedPodcasts.Count == 0 && plexHeaderIndex >= 0)
            {
                NavListBox.Items.Insert(plexHeaderIndex++, CreateSidebarHintItem(
                    "Podcast:EmptyHint",
                    LocalizationManager.Current.MyPodcastsEmptyHint));
            }

            foreach (var podcast in savedPodcasts)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(CreateSidebarIcon("IconPodcast"));
                content.Children.Add(CreateSidebarEntryText(podcast.Name));

                NavListBox.Items.Insert(plexHeaderIndex++, new ListBoxItem
                {
                    Content = content,
                    Tag = $"Podcast:{podcast.Id}",
                    Theme = FindResource<ControlTheme>("NavItemTheme"),
                    ContextFlyout = BuildDeletePodcastContextFlyout(podcast)
                });
            }

            var localPlaylistInsertIndex = NavListBox.Items.IndexOf(FoldersNavItem);
            if (localPlaylistInsertIndex >= 0)
            {
                localPlaylistInsertIndex++;
                NavListBox.Items.Insert(localPlaylistInsertIndex++, new ListBoxItem
                {
                    Content = CreateLibraryGroupHeader(LocalizationManager.Current.Playlists, _settings.IsPlaylistsSectionExpanded),
                    Tag = "LibraryGroup:LocalPlaylists",
                    FontWeight = FontWeight.SemiBold,
                    Theme = FindResource<ControlTheme>("NavItemTheme"),
                    ContextFlyout = BuildPlaylistsHeaderContextFlyout()
                });
            }

            foreach (var pl in db.GetAllPlaylists())
            {
                Control content = pl.IsSmartPlaylist
                    ? CreateSmartPlaylistSidebarContent(pl.Name)
                    : CreateSidebarEntryContent("IconPlaylist", pl.Name);
                content.Margin = new Thickness(16, 0, 0, 0);

                var item = new ListBoxItem
                {
                    Content = content,
                    Tag     = $"Playlist:{pl.Id}",
                    Theme = FindResource<ControlTheme>("NavItemTheme"),
                    ContextFlyout = BuildPlaylistSidebarContextFlyout(pl)
                };
                if (localPlaylistInsertIndex >= 0)
                    NavListBox.Items.Insert(localPlaylistInsertIndex++, item);
                else
                    NavListBox.Items.Add(item);
            }

            LoadPlexNavigationAsync();
            LoadOrynivoServerNavigation();
        }
        catch { /* DB noch nicht angelegt */ }

        ApplySidebarNavigationSettings();
        RestoreSelectedNavigationTag(selectedTag);
        }
        finally
        {
            _suppressNavSelectionChanged = false;
        }
    }

    private void RestoreSelectedNavigationTag(string? selectedTag)
    {
        if (string.IsNullOrWhiteSpace(selectedTag))
            return;

        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Tag as string, selectedTag, StringComparison.Ordinal));
        if (item is not null)
            NavListBox.SelectedItem = item;
    }

    private async void LoadPlexNavigationAsync()
    {
        var loadVersion = ++_plexNavigationLoadVersion;
        foreach (var item in NavListBox.Items
                     .OfType<ListBoxItem>()
                     .Where(item => item.Tag is string tag &&
                                    tag.StartsWith("Plex", StringComparison.Ordinal))
                     .ToList())
            NavListBox.Items.Remove(item);

        Dictionary<string, string> tokens;
        try
        {
            tokens = await new WindowsPlexCredentialStore().LoadAllAsync();
        }
        catch
        {
            tokens = [];
        }
        if (loadVersion != _plexNavigationLoadVersion)
            return;

        var insertIndex = NavListBox.Items.IndexOf(PlaylistsHeaderItem);
        var client = new PlexServerClient();
        foreach (var server in _settings.PlexServers ?? [])
        {
            NavListBox.Items.Insert(insertIndex++, new ListBoxItem
            {
                Content = CreateSidebarEntryContent("IconServer", server.Name),
                Tag = $"PlexServer:{server.Id}",
                IsEnabled = false,
                FontWeight = FontWeight.SemiBold,
                Theme = FindResource<ControlTheme>("NavItemTheme")
            });

            try
            {
                var libraries = await client.GetAudioLibrariesAsync(
                    server,
                    tokens.GetValueOrDefault(server.Id));
                if (loadVersion != _plexNavigationLoadVersion)
                    return;
                foreach (var library in libraries)
                    NavListBox.Items.Insert(insertIndex++, CreatePlexLibraryItem(
                        server.Id,
                        library.Key,
                        library.Title));
            }
            catch { }
        }

        ApplySidebarNavigationSettings();
    }

    private ListBoxItem CreatePlexLibraryItem(string serverId, string libraryKey, string title)
    {
        var content = CreateSidebarEntryContent("IconAlbum", title);
        content.Margin = new Thickness(16, 0, 0, 0);
        return new ListBoxItem
        {
            Content = content,
            Tag = $"PlexLibrary:{serverId}:{libraryKey}",
            Theme = FindResource<ControlTheme>("NavItemTheme")
        };
    }

    private TextBlock CreateSidebarEntryText(string text)
    {
        var tb = new TextBlock { Text = text };
        tb.Classes.Add("navItemText");
        return tb;
    }

    private StackPanel CreateSidebarEntryContent(string iconResourceKey, string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(CreateSidebarIcon(iconResourceKey));
        content.Children.Add(CreateSidebarEntryText(text));
        return content;
    }

    private AvaloniaPath CreateSidebarIcon(string resourceKey)
    {
        return new AvaloniaPath
        {
            Width = 13,
            Height = 13,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Data = FindResource<Geometry>(resourceKey),
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
    }

    private ListBoxItem CreateSidebarHintItem(string tag, string text)
    {
        var hint = CreateSidebarEntryText(text);
        hint.Margin = new Thickness(16, 0, 8, 0);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Foreground = FindResource<IBrush>("AppMutedTextBrush");
        hint.FontSize = ResolveFontSize("FontSizeMeta");
        return new ListBoxItem
        {
            Content = hint,
            Tag = tag,
            IsHitTestVisible = false,
            Focusable = false,
            Theme = FindResource<ControlTheme>("NavItemTheme")
        };
    }

    private Grid CreateLibraryGroupHeader(string title, bool isExpanded, double leftIndent = 0)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(20)));

        var content = CreateSidebarEntryContent("IconPlaylist", title);
        content.Children.OfType<TextBlock>().First().FontWeight = FontWeight.SemiBold;
        if (leftIndent > 0)
            content.Margin = new Thickness(leftIndent, 0, 0, 0);
        Grid.SetColumn(content, 0);
        grid.Children.Add(content);

        var arrow = new Avalonia.Controls.Shapes.Path
        {
            Width = 8,
            Height = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse(isExpanded ? "M 0 5 L 4 0 L 8 5" : "M 0 0 L 4 5 L 8 0"),
            Stroke = FindResource<IBrush>("AppNavHoverTextBrush"),
            StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
        Grid.SetColumn(arrow, 1);
        grid.Children.Add(arrow);

        return grid;
    }

    private void NavListBox_OnPreviewMouseLeftButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(NavListBox).Properties.IsLeftButtonPressed)
            return;
        if (FindAncestor<ListBoxItem>(e.Source as Visual) is not { Tag: string tag })
        {
            return;
        }

        if (tag.StartsWith("Section:", StringComparison.Ordinal))
        {
            var section = tag["Section:".Length..];
            SetSidebarSectionExpanded(section, !IsSidebarSectionExpanded(section));
            ApplySidebarNavigationSettings();
            e.Handled = true;
            return;
        }

        if (tag.StartsWith("LibraryGroup:", StringComparison.Ordinal))
        {
            ToggleLibraryGroupExpanded(tag["LibraryGroup:".Length..]);
            ApplySidebarNavigationSettings();
            e.Handled = true;
        }
    }

    private void RestorePlaybackQueueState()
    {
        _queue.Clear();
        using var db = AudioDatabase.OpenDefault();
        var snapshot = db.GetPlaybackQueue();
        var paths = snapshot.Paths;
        var currentIndex = snapshot.CurrentIndex;

        if (paths.Count == 0 && _settings.PlaybackQueuePaths.Count > 0)
        {
            paths = _settings.PlaybackQueuePaths.Where(CanPersistQueuePath).ToList();
            currentIndex = paths.Count == 0
                ? -1
                : Math.Clamp(_settings.PlaybackQueueIndex, 0, paths.Count - 1);
            db.SavePlaybackQueue(paths, currentIndex);
            if (ClearLegacyPlaybackQueueSettings())
                _settingsStore.Save(_settings);
        }

        var restoredPaths = paths.Where(CanPersistQueuePath).ToList();
        if (restoredPaths.Count != paths.Count)
        {
            currentIndex = currentIndex < 0
                ? -1
                : Math.Min(currentIndex, restoredPaths.Count - 1);
            db.SavePlaybackQueue(restoredPaths, currentIndex);
        }

        foreach (var path in restoredPaths)
        {
            _queue.Add(CreatePlaylistItem(path));
        }

        _queueIndex = _queue.Count == 0
            ? -1
            : currentIndex < 0 ? -1 : Math.Clamp(currentIndex, 0, _queue.Count - 1);
        ResetQueuePlaybackState();
        RefreshQueueNavigationButtons();
    }

    private void CapturePlaybackQueueState()
    {
        var persisted = new List<string>();
        var persistedIndex = -1;
        for (var index = 0; index < _queue.Count; index++)
        {
            var path = _queue[index].FilePath;
            if (!CanPersistQueuePath(path))
                continue;
            if (index == _queueIndex)
                persistedIndex = persisted.Count;
            persisted.Add(path);
        }

        var currentIndex = persistedIndex >= 0
            ? persistedIndex
            : persisted.Count == 0 ? -1 : Math.Min(_queueIndex, persisted.Count - 1);
        using var db = AudioDatabase.OpenDefault();

        // Detect a wholesale replacement (playing a completely different selection) and keep
        // the outgoing queue as a restorable "previous queue". Appends, moves, removals, and
        // next/previous keep some of the old items, so the intersection stays non-empty.
        var previous = db.GetPlaybackQueue();
        if (previous.Paths.Count > 0)
        {
            var newSet = new HashSet<string>(persisted, StringComparer.OrdinalIgnoreCase);
            if (!previous.Paths.Any(newSet.Contains))
                db.SavePreviousPlaybackQueue(previous.Paths);
        }

        db.SavePlaybackQueue(persisted, currentIndex);
        ClearLegacyPlaybackQueueSettings();
    }

    /// <summary>Restores the queue that was playing before the most recent wholesale replacement.</summary>
    /// <returns>A task representing the asynchronous restore.</returns>
    private async Task RestorePreviousQueueAsync()
    {
        IReadOnlyList<string> paths;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            paths = db.GetPreviousPlaybackQueue();
        }
        catch { paths = []; }

        if (paths.Count == 0)
        {
            StatusTextBlock.Text = LocalizationManager.Current.NoPreviousQueue;
            return;
        }

        _queue.Clear();
        foreach (var path in paths)
            _queue.Add(CreatePlaylistItem(path));
        _queueIndex = _queue.Count > 0 ? 0 : -1;
        ResetQueuePlaybackState();
        // Persisting captures the outgoing queue as the new "previous", making restore reversible.
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();

        if (_queue.Count == 0)
            return;
        try { await StartPlaybackAsync(_queue[0].FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
        UpdateNowPlayingRowHighlights();
    }

    /// <summary>Handles the Up Next "restore last queue" header button.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click event data.</param>
    private async void RestoreQueueButton_OnClick(object? sender, RoutedEventArgs e)
        => await RestorePreviousQueueAsync();

    /// <summary>Shows the "restore last queue" button in the Up Next view when a previous queue exists.</summary>
    /// <param name="tag">The current top-level view tag.</param>
    private void UpdateRestoreQueueButtonState(string tag)
    {
        var available = false;
        if (tag == "Queue")
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                available = db.GetPreviousPlaybackQueue().Count > 0;
            }
            catch { available = false; }
        }
        RestoreQueueButton.IsVisible = available;
    }

    private bool ClearLegacyPlaybackQueueSettings()
    {
        if (_settings.PlaybackQueuePaths.Count == 0 && _settings.PlaybackQueueIndex == -1)
            return false;
        _settings.PlaybackQueuePaths = [];
        _settings.PlaybackQueueIndex = -1;
        return true;
    }

    private static bool CanPersistQueuePath(string path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.IsFile)
            return true;
        if (uri.Scheme.Equals("cue", StringComparison.OrdinalIgnoreCase))
            return true;
        if (uri.Scheme.Equals("orynivo", StringComparison.OrdinalIgnoreCase))
            return true;
        if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        return !uri.Query.Contains("X-Plex-Token", StringComparison.OrdinalIgnoreCase) &&
               !uri.Query.Contains("token=", StringComparison.OrdinalIgnoreCase) &&
               !uri.Query.Contains("key=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAvailableLocalTrack(string path)
    {
        if (File.Exists(path))
            return true;
        if (!CueSheetParser.IsVirtualPath(path))
            return false;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(path);
            return track is not null &&
                   File.Exists(track.SourcePath) &&
                   (track.CuePath is null || File.Exists(track.CuePath));
        }
        catch
        {
            return false;
        }
    }

    private sealed record AlbumTrackGroup(
        string Directory,
        string Album,
        string? Artist,
        string? Year,
        List<ContentRow> Rows);

    private void PersistPlaybackQueue()
    {
        CapturePlaybackQueueState();
        if (ClearLegacyPlaybackQueueSettings())
            _settingsStore.Save(_settings);
    }

    private void NavListBox_OnPreviewMouseRightButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(NavListBox).Properties.IsRightButtonPressed)
            return;
        if (FindAncestor<ListBoxItem>(e.Source as Visual) is not
            { ContextFlyout: PopupFlyoutBase flyout } item)
            return;

        // Prevent SelectingItemsControl from treating a context click as a
        // primary selection click, then show the flyout at the pointer location.
        e.Handled = true;
        flyout.ShowAt(item, showAtPointer: true);
    }

    private void ApplySidebarNavigationSettings()
    {
        SetSidebarItemVisibility(InternetRadioNavItem, _settings.ShowInternetRadioItem);
        SetSidebarItemVisibility(PodcastsNavItem, _settings.ShowPodcastsItem);
        SetSidebarItemVisibility(QueueNavItem, _settings.ShowQueueItem);
        ApplyLibrarySectionVisibility();
        SetSidebarSectionVisibility(
            OwnRadiosHeaderItem,
            "OwnRadios",
            _settings.ShowOwnRadiosSection,
            dynamicPrefix: "Radio:");
        SetSidebarSectionVisibility(
            MyPodcastsHeaderItem,
            "MyPodcasts",
            _settings.ShowMyPodcastsSection,
            dynamicPrefix: "Podcast:");
        SetSidebarSectionVisibility(
            PlexHeaderItem,
            "Plex",
            _settings.ShowPlexSection,
            dynamicPrefix: "Plex");
        SetSidebarItemVisibility(PlaylistsHeaderItem, false);
    }

    private void SetSidebarSectionVisibility(
        ListBoxItem header,
        string section,
        bool isVisible,
        IReadOnlyList<ListBoxItem>? staticItems = null,
        string? dynamicPrefix = null)
    {
        SetSidebarItemVisibility(header, isVisible);
        var showItems = isVisible && IsSidebarSectionExpanded(section);
        var arrow = section switch
        {
            "LocalLibrary" => LocalLibraryHeaderArrow,
            "OwnRadios" => OwnRadiosHeaderArrow,
            "MyPodcasts" => MyPodcastsHeaderArrow,
            "Plex" => PlexHeaderArrow,
            "Playlists" => PlaylistsHeaderArrow,
            _ => null
        };
        if (arrow is not null)
            arrow.Data = Geometry.Parse(showItems ? "M 0 5 L 4 0 L 8 5" : "M 0 0 L 4 5 L 8 0");

        if (staticItems is not null)
        {
            foreach (var item in staticItems)
                SetSidebarItemVisibility(item, showItems);
        }

        if (dynamicPrefix is null)
            return;

        foreach (var item in NavListBox.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is string tag && tag.StartsWith(dynamicPrefix, StringComparison.Ordinal))
                SetSidebarItemVisibility(item, showItems);
        }
    }

    private void ApplyLibrarySectionVisibility()
    {
        SetSidebarItemVisibility(LocalLibraryHeaderItem, _settings.ShowLocalLibrarySection);
        var showLibraryItems = _settings.ShowLocalLibrarySection && IsSidebarSectionExpanded("LocalLibrary");
        SetArrowData(LocalLibraryHeaderArrow, showLibraryItems);

        var hasLocalMedia = (_settings.LibraryPaths?.Count ?? 0) > 0;
        var hasOrynivoServers = (_settings.OrynivoServers?.Count ?? 0) > 0;

        // Hint shown directly under the Library header when neither local media
        // directories nor any Orynivo Server is configured. It disappears as soon
        // as a directory or server is added (this method re-runs on every settings
        // save and navigation rebuild).
        SetSidebarItemVisibility(LibraryEmptyHintItem, showLibraryItems && !hasLocalMedia && !hasOrynivoServers);

        SetSidebarItemVisibility(LocalLibraryRootItem, false);
        SetArrowData(LocalMediaGroupArrow, false);
        var showUnifiedLibraryItems = showLibraryItems && (hasLocalMedia || hasOrynivoServers);
        SetSidebarItemVisibility(ArtistsNavItem, showUnifiedLibraryItems);
        SetSidebarItemVisibility(AlbumsNavItem, showUnifiedLibraryItems);
        SetSidebarItemVisibility(TracksNavItem, showUnifiedLibraryItems);
        SetSidebarItemVisibility(FoldersNavItem, showLibraryItems && hasLocalMedia);

        foreach (var item in NavListBox.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is not string tag)
                continue;

            if (tag.StartsWith("LibraryGroup:OrynivoServerPlaylists:", StringComparison.Ordinal))
            {
                var serverId = tag["LibraryGroup:OrynivoServerPlaylists:".Length..];
                SetSidebarItemVisibility(item, showLibraryItems && IsOrynivoServerLibraryGroupExpanded(serverId));
                UpdateLibraryGroupHeaderArrow(item, IsOrynivoServerPlaylistGroupExpanded(serverId));
                continue;
            }

            if (tag.StartsWith("LibraryGroup:OrynivoServer:", StringComparison.Ordinal))
            {
                var serverId = tag["LibraryGroup:OrynivoServer:".Length..];
                SetSidebarItemVisibility(item, showLibraryItems);
                UpdateLibraryGroupHeaderArrow(item, IsOrynivoServerLibraryGroupExpanded(serverId));
                continue;
            }

            if (tag == "LibraryGroup:LocalPlaylists")
            {
                SetSidebarItemVisibility(item, showLibraryItems);
                UpdateLibraryGroupHeaderArrow(item, _settings.IsPlaylistsSectionExpanded);
                continue;
            }

            if (tag.StartsWith("Playlist:", StringComparison.Ordinal))
            {
                SetSidebarItemVisibility(item, showLibraryItems && _settings.IsPlaylistsSectionExpanded);
                continue;
            }

            if (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal))
            {
                var parts = tag.Split(':');
                var serverId = parts.Length > 1 ? parts[1] : string.Empty;
                SetSidebarItemVisibility(item, showLibraryItems && IsOrynivoServerLibraryGroupExpanded(serverId));
                continue;
            }

            if (tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal))
            {
                var parts = tag.Split(':');
                var serverId = parts.Length > 1 ? parts[1] : string.Empty;
                SetSidebarItemVisibility(
                    item,
                    showLibraryItems
                    && IsOrynivoServerLibraryGroupExpanded(serverId)
                    && IsOrynivoServerPlaylistGroupExpanded(serverId));
            }
        }
    }

    private static void SetArrowData(Avalonia.Controls.Shapes.Path arrow, bool isExpanded) =>
        arrow.Data = Geometry.Parse(isExpanded ? "M 0 5 L 4 0 L 8 5" : "M 0 0 L 4 5 L 8 0");

    private static void SetSidebarItemVisibility(ListBoxItem item, bool isVisible)
    {
        if (isVisible)
        {
            item.IsVisible = true;
            item.MaxHeight = 96;
            item.Opacity = 1;
            return;
        }

        item.Opacity = 0;
        item.MaxHeight = 0;
        _ = HideCollapsedSidebarItemAsync(item);
    }

    private static async Task HideCollapsedSidebarItemAsync(ListBoxItem item)
    {
        await Task.Delay(180);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (item.Opacity <= 0.05 && item.MaxHeight <= 0.5)
                item.IsVisible = false;
        });
    }

    private static void UpdateLibraryGroupHeaderArrow(ListBoxItem item, bool isExpanded)
    {
        if (item.Content is not Grid grid)
            return;

        foreach (var child in grid.Children)
        {
            if (child is Avalonia.Controls.Shapes.Path arrow)
            {
                SetArrowData(arrow, isExpanded);
                return;
            }
        }
    }

    private bool IsOrynivoServerLibraryGroupExpanded(string serverId) =>
        !_settings.CollapsedOrynivoServerLibraryGroups.Contains(serverId);

    private bool IsOrynivoServerPlaylistGroupExpanded(string serverId) =>
        !_settings.CollapsedOrynivoServerPlaylistGroups.Contains(serverId);

    private void ToggleLibraryGroupExpanded(string group)
    {
        if (group.Equals("LocalMedia", StringComparison.Ordinal))
        {
            _settings.IsLocalMediaLibraryGroupExpanded = !_settings.IsLocalMediaLibraryGroupExpanded;
            return;
        }

        if (group.Equals("LocalPlaylists", StringComparison.Ordinal))
        {
            _settings.IsPlaylistsSectionExpanded = !_settings.IsPlaylistsSectionExpanded;
            return;
        }

        if (group.StartsWith("OrynivoServerPlaylists:", StringComparison.Ordinal))
        {
            var playlistServerId = group["OrynivoServerPlaylists:".Length..];
            if (!_settings.CollapsedOrynivoServerPlaylistGroups.Add(playlistServerId))
                _settings.CollapsedOrynivoServerPlaylistGroups.Remove(playlistServerId);
            return;
        }

        if (!group.StartsWith("OrynivoServer:", StringComparison.Ordinal))
            return;

        var serverId = group["OrynivoServer:".Length..];
        if (!_settings.CollapsedOrynivoServerLibraryGroups.Add(serverId))
            _settings.CollapsedOrynivoServerLibraryGroups.Remove(serverId);
    }

    private bool IsSidebarSectionExpanded(string section) => section switch
    {
        "LocalLibrary" => _settings.IsLocalLibrarySectionExpanded,
        "OwnRadios" => _settings.IsOwnRadiosSectionExpanded,
        "MyPodcasts" => _settings.IsMyPodcastsSectionExpanded,
        "Plex" => _settings.IsPlexSectionExpanded,
        "Playlists" => _settings.IsPlaylistsSectionExpanded,
        _ => false
    };

    private void SetSidebarSectionExpanded(string section, bool isExpanded)
    {
        switch (section)
        {
            case "LocalLibrary":
                _settings.IsLocalLibrarySectionExpanded = isExpanded;
                break;
            case "OwnRadios":
                _settings.IsOwnRadiosSectionExpanded = isExpanded;
                break;
            case "MyPodcasts":
                _settings.IsMyPodcastsSectionExpanded = isExpanded;
                break;
            case "Plex":
                _settings.IsPlexSectionExpanded = isExpanded;
                break;
            case "Playlists":
                _settings.IsPlaylistsSectionExpanded = isExpanded;
                break;
        }
    }

    private async void NavListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressNavSelectionChanged)
            return;
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: string tag })
            return;
        if (IsNavigationContainerTag(tag))
            return;

        CloseEmbeddedSettings();
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        if (tag is "InternetRadio" or "Podcasts" or "Queue" or "Artists" or "Albums" or "Tracks" or "Folders")
            _settings.LastMainView = tag;
        await ShowTopLevelViewAsync(tag);
    }

    private async void NavListBox_OnPreviewMouseLeftButtonUp(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (FindAncestor<ListBoxItem>(e.Source as Visual) is not { Tag: string tag, IsSelected: true })
            return;
        if (IsNavigationContainerTag(tag))
            return;

        // Beim erneuten Klick auf den bereits markierten Hauptpunkt feuert kein SelectionChanged.
        // Trotzdem soll die ungefilterte Top-Level-Ansicht wiederhergestellt werden.
        if (_activeAlbumFilterId is null && _activeArtistFilterId is null)
            return;

        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        await ShowTopLevelViewAsync(tag);
    }

    private static bool IsNavigationContainerTag(string tag) =>
        tag.StartsWith("Section:", StringComparison.Ordinal) ||
        tag.StartsWith("LibraryGroup:", StringComparison.Ordinal);

    private void ResetDrilldownState(bool clearNavigationHistory = true)
    {
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        _activeArtistFilterId = null;
        _activeArtistFilterName = null;
        _activeAlbumCatalogProvider = null;
        _activeCatalogAlbum = null;
        _plexNavigationStack.Clear();
        if (clearNavigationHistory)
            _navigationStack.Clear();
        BackButton.IsVisible = _navigationStack.Count > 0;
    }

    private void PushCurrentNavigationState()
    {
        if (_restoringNavigationHistory)
            return;

        var state = CaptureCurrentNavigationState();
        if (state is null)
            return;
        if (_navigationStack.TryPeek(out var previous) && previous == state)
            return;

        _navigationStack.Push(state);
        BackButton.IsVisible = true;
    }

    private NavigationState? CaptureCurrentNavigationState()
    {
        if (SearchResultsScrollViewer.IsVisible)
            return new NavigationState(
                "Search",
                GetSelectedContentRowId(),
                null,
                null,
                SearchTextBox.Text ?? string.Empty,
                CaptureCurrentVerticalOffset());

        if (_activeAlbumFilterId is long orynivoAlbumId &&
            _activeCatalogAlbum is { Source: LibraryCatalogSource.OrynivoServer } &&
            _activeOrynivoServer is { } orynivoAlbumServer)
        {
            // A provider-backed remote album must be restored through the remote
            // path. Detecting it only via the top-level tag failed when the album
            // was opened from the dashboard (tag "Dashboard"), so it was captured
            // as a local "AlbumTracks" state and Back reopened a local album that
            // merely shared the numeric id. Detect it by the catalog album source
            // instead and rebuild a server navigation tag when needed.
            var albumNavigationTag =
                _currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true
                    ? _currentTopLevelTag
                    : $"OrynivoServer:{orynivoAlbumServer.Id}:Albums";
            return new NavigationState(
                "OrynivoAlbumTracks",
                orynivoAlbumId,
                _activeArtistFilterId,
                _activeArtistFilterName,
                _activeAlbumFilterTitle,
                CaptureCurrentVerticalOffset(),
                albumNavigationTag);
        }

        if (_activeAlbumFilterId is long albumId)
            return new NavigationState(
                "AlbumTracks",
                albumId,
                _activeArtistFilterId,
                _activeArtistFilterName,
                _activeAlbumFilterTitle);

        if (_activeAlbumFilterId is null &&
            _activeArtistFilterId is long remoteArtistId &&
            _currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true)
        {
            return new NavigationState(
                "OrynivoArtistAlbums",
                GetSelectedContentRowId(),
                remoteArtistId,
                _activeArtistFilterName,
                _activeArtistFilterName,
                CaptureCurrentVerticalOffset(),
                _currentTopLevelTag);
        }

        if (_activeAlbumFilterId is null &&
            _activeArtistFilterId is long artistId &&
            _activeAlbumCatalogProvider is null &&
            !(_currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true))
        {
            return new NavigationState(
                "ArtistAlbums",
                GetSelectedContentRowId(),
                artistId,
                _activeArtistFilterName,
                _activeArtistFilterName,
                CaptureCurrentVerticalOffset());
        }

        if (_activeAlbumFilterId is null &&
            _activeArtistFilterId is null &&
            !string.IsNullOrWhiteSpace(_activeArtistFilterName) &&
            string.Equals(_currentTopLevelTag, "Albums", StringComparison.Ordinal))
        {
            return new NavigationState(
                "UnifiedArtistAlbums",
                GetSelectedContentRowId(),
                null,
                _activeArtistFilterName,
                _activeArtistFilterName,
                CaptureCurrentVerticalOffset());
        }

        if (!string.IsNullOrWhiteSpace(_currentTopLevelTag) &&
            !_currentTopLevelTag.StartsWith("Section:", StringComparison.Ordinal))
        {
            return new NavigationState(
                _currentTopLevelTag,
                GetSelectedContentRowId(),
                _activeArtistFilterId,
                _activeArtistFilterName,
                SearchTextBox.Text ?? string.Empty,
                CaptureCurrentVerticalOffset());
        }

        return null;
    }

    private double? CaptureCurrentVerticalOffset()
    {
        if (ContentDataGrid.IsVisible)
        {
            AttachContentDataGridVerticalScrollBar();
            return _contentDataGridVerticalScrollBar?.Value;
        }

        var listBox = AlbumArtworkListBox.IsVisible
            ? AlbumArtworkListBox
            : ArtistArtworkListBox.IsVisible
                ? ArtistArtworkListBox
                : null;
        return listBox?
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault()?
            .Offset.Y;
    }

    private long? GetSelectedContentRowId()
    {
        if (ContentDataGrid.SelectedItem is ContentRow { Id: long gridId })
            return gridId;
        if (AlbumArtworkListBox.SelectedItem is ContentRow { Id: long albumId })
            return albumId;
        if (ArtistArtworkListBox.SelectedItem is ContentRow { Id: long artistId })
            return artistId;
        return null;
    }

    private async Task ShowTopLevelViewAsync(string tag)
    {
        var diagnosticStopwatch = Stopwatch.StartNew();
        LogUiDiagnostics($"ShowTopLevelViewAsync start tag={tag}");
        var showLoadingSkeleton = !tag.StartsWith("Radio:", StringComparison.Ordinal);
        if (showLoadingSkeleton)
            ShowContentLoadingSkeleton();
        _currentTopLevelTag = tag;
        _orynivoTrackFacets = null;
        // A fresh load reflects current library data, so any pending refresh prompt is stale.
        SetLibraryRefreshAvailable(false);
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        _activePlaylistId = tag.StartsWith("Playlist:") &&
                            long.TryParse(tag.AsSpan("Playlist:".Length), out long parsedPid)
            ? parsedPid : null;
        _activeOrynivoPlaylistServer = null;
        _activeOrynivoPlaylistId = null;
        if (TryParseOrynivoPlaylistTag(tag, out var playlistServer, out var playlistId))
        {
            _activeOrynivoPlaylistServer = playlistServer;
            _activeOrynivoPlaylistId = playlistId;
        }

        ContentTitleTextBlock.Text = tag switch
        {
            "Artists" => LocalizationManager.Current.Artists,
            "Albums"  => LocalizationManager.Current.Albums,
            "Tracks"  => LocalizationManager.Current.Tracks,
            "Folders" => LocalizationManager.Current.FolderStructure,
            "Queue" => LocalizationManager.Current.UpNext,
            "AiChat" => LocalizationManager.Current.AiChat,
            "InternetRadio" => LocalizationManager.Current.InternetRadio,
            "Podcasts" => LocalizationManager.Current.Podcasts,
            "RecentAlbumsAll" => LocalizationManager.Current.RecentAlbums,
            "RecentlyPlayedAll" => LocalizationManager.Current.RecentlyPlayed,
            _ when tag.StartsWith("PlexLibrary:", StringComparison.Ordinal) =>
                _activePlexSectionTitle ?? LocalizationManager.Current.PlexServers,
            _ when tag.StartsWith("OrynivoServer:", StringComparison.Ordinal) =>
                _activeOrynivoServer?.Name ?? LocalizationManager.Current.OrynivoServers,
            _ when tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal) => GetOrynivoPlaylistName(tag),
            _ when tag.StartsWith("Radio:", StringComparison.Ordinal) => GetRadioName(tag),
            _ when tag.StartsWith("Podcast:", StringComparison.Ordinal) => GetPodcastName(tag),
            _         => tag.StartsWith("Playlist:") ? GetPlaylistName(tag) : tag
        };

        // Stop any in-flight unified folder-tree load so a late completion cannot mutate
        // the tree or the count of the view the user is switching to.
        CancelAndDispose(ref _folderViewCts);
        ContentDataGrid.ItemsSource = null;
        AlbumArtworkListBox.ItemsSource = null;
        ArtistArtworkListBox.ItemsSource = null;
        _albumArtworkRows = [];
        _artistArtworkRows = [];
        _visibleAlbumArtworkRows.Clear();
        _visibleArtistArtworkRows.Clear();
        UpdateAlphabetIndex(null, false);
        SearchResultsScrollViewer.IsVisible = false;
        DashboardScrollViewer.IsVisible = false;
        AiChatViewControl.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        PodcastEpisodesView.IsVisible = false;
        HideAlbumDetailHeader();
        ContentCountTextBlock.Text  = "";
        UpdateLibraryIntroCard(tag);
        var isOrynivoServerTag = tag.StartsWith("OrynivoServer:", StringComparison.Ordinal);
        var isOrynivoTracksTag = TryParseOrynivoServerTag(tag, out _, out var orynivoViewForHeader) &&
                                 orynivoViewForHeader == "Tracks";
        SearchTextBox.IsVisible = isOrynivoTracksTag ||
                                   !(tag is "InternetRadio" or "Podcasts" or "Queue" or "AiChat"
                                        or "RecentAlbumsAll" or "RecentlyPlayedAll" ||
                                    tag.StartsWith("Radio:", StringComparison.Ordinal) ||
                                    tag.StartsWith("Podcast:", StringComparison.Ordinal) ||
                                    tag.StartsWith("PlexLibrary:", StringComparison.Ordinal) ||
                                    tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal) ||
                                    isOrynivoServerTag);
        UpdateEntityFavoritesFilterToggle(tag);
        AlbumViewModeBorder.IsVisible = tag is "Albums" or "Artists";
        PlexViewModeBorder.IsVisible = tag.StartsWith("PlexLibrary:", StringComparison.Ordinal);
        OrynivoServerViewModeBorder.IsVisible = false;
        if (tag is "Albums" or "Artists")
            SetViewModeButtons(tag == "Albums" ? _showAlbumArtworkView : _showArtistArtworkView);
        TrackFilterButton.IsVisible = tag == "Tracks" ? true : false;
        SaveSmartPlaylistButton.IsVisible = tag == "Tracks" ? true : false;
        ClearQueueButton.IsVisible = tag == "Queue";
        ClearQueueButton.IsEnabled = _queue.Count > 0;
        SaveQueueAsPlaylistButton.IsVisible = tag == "Queue";
        SaveQueueAsPlaylistButton.IsEnabled =
            _queue.Any(item => CanPersistQueuePath(item.FilePath));
        UpdateRestoreQueueButtonState(tag);
        if (tag == "Tracks") UpdateSaveSmartPlaylistButtonState();
        TrackFilterPopup.IsOpen = false;
        try
        {
            if (tag == "Dashboard")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                DashboardScrollViewer.IsVisible = true;
                await ShowDashboardAsync();
            }
            else if (tag == "RecentAlbumsAll")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                DashboardScrollViewer.IsVisible = true;
                await BuildAllRecentAlbumsViewAsync();
            }
            else if (tag == "RecentlyPlayedAll")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                DashboardScrollViewer.IsVisible = true;
                await BuildAllRecentlyPlayedViewAsync();
            }
            else if (tag == "AiChat")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                AiChatViewControl.IsVisible = true;
            }
            else if (tag == "InternetRadio")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                InternetRadioView.IsVisible = true;
                await EnsureRadioFilterCatalogAsync();
                if (RadioStationsDataGrid.ItemsSource is null)
                {
                    RadioStatusTextBlock.Text = LocalizationManager.Current.RadioEmptyState;
                    RadioStatusTextBlock.IsVisible = true;
                }
            }
            else if (tag == "Podcasts")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                PodcastView.IsVisible = true;
                PodcastEpisodesView.IsVisible = false;
                await EnsurePodcastFilterCatalogAsync();
                if (PodcastsDataGrid.ItemsSource is null)
                {
                    PodcastStatusTextBlock.Text = LocalizationManager.Current.PodcastEmptyState;
                    PodcastStatusTextBlock.IsVisible = true;
                }
            }
            else if (tag == "Queue")
            {
                ContentDataGrid.IsVisible = true;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                ApplyColumns("Queue");
                RefreshQueueRows();
            }
            else if (tag.StartsWith("Podcast:", StringComparison.Ordinal) &&
                     long.TryParse(tag.AsSpan("Podcast:".Length), out var podcastId))
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                PodcastView.IsVisible = true;
                await ShowSavedPodcastAsync(podcastId);
            }
            else if (tag.StartsWith("Radio:", StringComparison.Ordinal) &&
                     long.TryParse(tag.AsSpan("Radio:".Length), out var radioId))
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                InternetRadioView.IsVisible = true;
                _ = PlaySavedRadioAsync(radioId);
            }
            else if (tag.StartsWith("PlexLibrary:", StringComparison.Ordinal))
            {
                await ShowPlexLibraryAsync(tag);
            }
            else if (isOrynivoServerTag)
            {
                await ShowOrynivoServerAsync(tag);
            }
            else if (tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal))
            {
                await ShowOrynivoPlaylistAsync(tag);
            }
            else if (tag == "Folders")
            {
                ContentDataGrid.IsVisible = false;
                FolderTreeView.IsVisible = true;
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = false;
                await ShowUnifiedFolderTreeAsync();
            }
            else
            {
                var showArtwork = tag == "Albums"
                    ? _showAlbumArtworkView
                    : tag == "Artists" && _showArtistArtworkView;
                ContentDataGrid.IsVisible = !(showArtwork);
                FolderTreeView.IsVisible = false;
                AlbumArtworkListBox.IsVisible = tag == "Albums" && _showAlbumArtworkView
                    ? true : false;
                ArtistArtworkListBox.IsVisible = tag == "Artists" && _showArtistArtworkView
                    ? true : false;

                if (tag is "Artists" or "Albums" or "Tracks")
                {
                    await BindLocalRowsAndStartRemoteAppendAsync(tag);
                }
                else
                {
                    var rows = await Task.Run(() => QueryRows(tag));
                    ApplyColumns(tag);
                    ContentDataGrid.ItemsSource = rows;
                    UpdateAlphabetIndex(rows, false);
                    ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                }
            }
        }
        finally
        {
            HideContentLoadingSkeleton();
            FadeInVisibleContentSurface();
            LogUiDiagnostics(
                $"ShowTopLevelViewAsync finish tag={tag} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms gridVisible={ContentDataGrid.IsVisible} gridItems={GetDiagnosticItemCount(ContentDataGrid.ItemsSource)}");
        }
    }

    private void ShowContentLoadingSkeleton()
    {
        _contentLoadingDepth++;
        var generation = ++_contentLoadingGeneration;
        if (_contentLoadingDepth > 1)
            return;

        ContentLoadingOverlay.Opacity = 0;
        ContentLoadingOverlay.IsVisible = true;
        ContentLoadingOverlay.IsHitTestVisible = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_contentLoadingGeneration == generation && _contentLoadingDepth > 0)
                    ContentLoadingOverlay.Opacity = 1;
            },
            DispatcherPriority.Render);
    }

    private void HideContentLoadingSkeleton()
    {
        if (_contentLoadingDepth > 0)
            _contentLoadingDepth--;
        var generation = ++_contentLoadingGeneration;
        if (_contentLoadingDepth > 0)
            return;

        ContentLoadingOverlay.Opacity = 0;
        ContentLoadingOverlay.IsHitTestVisible = false;
        _ = HideContentLoadingSkeletonAsync(generation);
    }

    private async Task HideContentLoadingSkeletonAsync(int generation)
    {
        await Task.Delay(170);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_contentLoadingGeneration == generation &&
                _contentLoadingDepth == 0)
            {
                ContentLoadingOverlay.Opacity = 0;
                ContentLoadingOverlay.IsHitTestVisible = false;
                ContentLoadingOverlay.IsVisible = false;
            }
        });
    }

    private void FadeInVisibleContentSurface()
    {
        foreach (var surface in _animatedViewSurfaces)
        {
            if (surface.IsVisible)
                surface.Opacity = 0;
            else
                surface.Opacity = 1;
        }

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var surface in _animatedViewSurfaces)
            {
                if (surface.IsVisible)
                    surface.Opacity = 1;
            }
        }, DispatcherPriority.Render);
    }

    private void UpdateAlphabetIndex(IEnumerable<ContentRow>? source, bool visible)
    {
        var rows = source?.ToList() ?? [];
        LogUiDiagnostics(
            $"UpdateAlphabetIndex start visible={visible} rows={rows.Count} currentTag={_currentTopLevelTag ?? "<null>"}");
        var firstRows = rows
            .GroupBy(GetAlphabetIndexKey)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        AlphabetIndexPanel.Children.Clear();
        foreach (var label in AlphabetIndexLabels)
        {
            var button = new Button
            {
                Content = label,
                Tag = firstRows.GetValueOrDefault(label),
                IsEnabled = firstRows.ContainsKey(label),
                Theme = FindResource<ControlTheme>("AlphabetIndexButtonTheme")
            };
            button.Click += AlphabetIndexButton_OnClick;
            AlphabetIndexPanel.Children.Add(button);
        }

        var showIndex = visible && rows.Count > 0;
        AlphabetIndexBorder.IsVisible = showIndex;
        var indexMargin = showIndex ? new Thickness(0, 0, 46, 0) : new Thickness(0);
        ContentDataGrid.Margin = indexMargin;
        AlbumArtworkListBox.Margin = indexMargin;
        ArtistArtworkListBox.Margin = indexMargin;
        FolderTreeView.Margin = new Thickness(0);
        LogUiDiagnostics(
            $"UpdateAlphabetIndex finish showIndex={showIndex} enabledLetters={firstRows.Count} currentTag={_currentTopLevelTag ?? "<null>"}");
        Dispatcher.UIThread.Post(UpdateActiveAlphabetButton, DispatcherPriority.Loaded);
    }

    private void UpdatePlexFolderAlphabetIndex()
    {
        var roots = FolderTreeView.Items
            .OfType<TreeViewItem>()
            .Where(item => item.Tag is PlexFolderTag { IsTrack: false })
            .Select(item => new AlphabetTreeTarget(
                GetAlphabetIndexKey(GetTreeItemHeader(item)),
                item))
            .ToList();
        var firstTargets = roots
            .GroupBy(target => target.Key)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        AlphabetIndexPanel.Children.Clear();
        foreach (var label in AlphabetIndexLabels)
        {
            var button = new Button
            {
                Content = label,
                Tag = firstTargets.GetValueOrDefault(label),
                IsEnabled = firstTargets.ContainsKey(label),
                Theme = FindResource<ControlTheme>("AlphabetIndexButtonTheme")
            };
            button.Click += AlphabetIndexButton_OnClick;
            AlphabetIndexPanel.Children.Add(button);
        }

        var showIndex = roots.Count > 0;
        AlphabetIndexBorder.IsVisible = showIndex;
        FolderTreeView.Margin = showIndex
            ? new Thickness(0, 0, 46, 0)
            : new Thickness(0);
        ContentDataGrid.Margin = new Thickness(0);
        AlbumArtworkListBox.Margin = new Thickness(0);
        ArtistArtworkListBox.Margin = new Thickness(0);
        Dispatcher.UIThread.Post(UpdateActiveAlphabetButton, DispatcherPriority.Loaded);
    }

    private void UpdateLibraryIntroCard(string? tag)
    {
        var strings = LocalizationManager.Current;
        var intro = tag switch
        {
            "Artists" => (strings.ArtistsIntroTitle, strings.ArtistsIntroHint, "IconArtist"),
            "Albums" => (strings.AlbumsIntroTitle, strings.AlbumsIntroHint, "IconAlbum"),
            "Tracks" => (strings.TracksIntroTitle, strings.TracksIntroHint, "IconTrack"),
            "Folders" => (strings.FoldersIntroTitle, strings.FoldersIntroHint, "IconFolder"),
            _ => default
        };

        var visible = !string.IsNullOrWhiteSpace(intro.Item1);
        LibraryIntroCard.IsVisible = visible;
        if (!visible)
            return;

        LibraryIntroTitleTextBlock.Text = intro.Item1;
        LibraryIntroHintTextBlock.Text = intro.Item2;
        LibraryIntroIconPath.Data = FindResource<Geometry>(intro.Item3);
    }

    private async Task ShowPlexLibraryAsync(string tag)
    {
        var parts = tag.Split(':', 3);
        if (parts.Length != 3)
            return;

        _activePlexServer = (_settings.PlexServers ?? [])
            .FirstOrDefault(server => string.Equals(server.Id, parts[1], StringComparison.Ordinal));
        if (_activePlexServer is null)
            return;

        _activePlexSectionKey = parts[2];
        _activePlexSectionTitle = NavListBox.Items.OfType<ListBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            ?.Content is TextBlock text
                ? (text.Text ?? string.Empty).Trim()
                : _activePlexServer.Name;
        try
        {
            _activePlexToken = new WindowsPlexCredentialStore()
                .LoadAll()
                .GetValueOrDefault(_activePlexServer.Id);
        }
        catch
        {
            _activePlexToken = null;
        }

        _activePlexView = "Artists";
        _plexNavigationStack.Clear();
        _updatingViewMode = true;
        PlexArtistsViewRadioButton.IsChecked = true;
        _updatingViewMode = false;
        ContentTitleTextBlock.Text = _activePlexSectionTitle;
        await LoadPlexViewAsync(reset: true);
    }

    private async void PlexViewModeRadioButton_OnChecked(object? sender, RoutedEventArgs e)
    {
        if (!IsVisible || _updatingViewMode ||
            sender is not RadioButton { IsChecked: true, Tag: string view } ||
            _activePlexServer is null)
        {
            return;
        }

        _plexNavigationStack.Clear();
        _activePlexView = view;
        ContentTitleTextBlock.Text = _activePlexSectionTitle;
        BackButton.IsVisible = _navigationStack.Count > 0;
        await LoadPlexViewAsync(reset: true);
    }

    private async void PlexLoadMoreButton_OnClick(object? sender, RoutedEventArgs e)
        => await LoadPlexViewAsync(reset: false);

    // ------------------------------------------------------------------
    // Orynivo Server browsing
    // ------------------------------------------------------------------

    private async Task ShowOrynivoServerAsync(string tag)
    {
        if (!TryParseOrynivoServerTag(tag, out var serverId, out var view))
            return;
        _activeOrynivoServer = (_settings.OrynivoServers ?? [])
            .FirstOrDefault(s => string.Equals(s.Id, serverId, StringComparison.Ordinal));
        if (_activeOrynivoServer is null)
            return;

        _orynivoNavigationStack.Clear();
        // Start each server visit with a clean facet slate; genres/formats from another
        // library would otherwise filter the remote Tracks view to nothing.
        _selectedTrackGenres.Clear();
        _selectedTrackFormats.Clear();
        _selectedTrackBitrates.Clear();
        _activeOrynivoView = view == "Root" ? "Artists" : view;
        _updatingViewMode = true;
        OrynivoArtistsViewRadioButton.IsChecked = _activeOrynivoView == "Artists";
        _updatingViewMode = false;
        ContentTitleTextBlock.Text = $"{_activeOrynivoServer.Name} · {GetOrynivoViewTitle(_activeOrynivoView)}";
        await LoadOrynivoViewAsync();
    }

    private static bool TryParseOrynivoServerTag(string tag, out string serverId, out string view)
    {
        serverId = string.Empty;
        view = string.Empty;
        if (!tag.StartsWith("OrynivoServer:", StringComparison.Ordinal))
            return false;
        var parts = tag.Split(':', 3);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            return false;
        serverId = parts[1];
        view = parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : "Root";
        return true;
    }

    private static string GetOrynivoFavoriteKey(string serverId, string entityType, long id)
        => $"{serverId}:{entityType}:{id}";

    private static string GetServerSourceKey(string serverId) => $"server:{serverId}";

    private bool IsOrynivoFavorite(OrynivoServerSettings server, string entityType, long id)
        => _settings.OrynivoServerFavorites.Contains(GetOrynivoFavoriteKey(server.Id, entityType, id));

    /// <summary>Returns the server-side track IDs the client currently marks as favourites for a remote server.</summary>
    /// <param name="server">Remote server whose client-side track favourites are collected.</param>
    /// <returns>The favourite track IDs.</returns>
    private List<long> GetOrynivoFavoriteTrackIds(OrynivoServerSettings server)
    {
        var prefix = $"{server.Id}:Track:";
        var ids = new List<long>();
        foreach (var key in _settings.OrynivoServerFavorites)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) &&
                long.TryParse(key.AsSpan(prefix.Length), out var id))
                ids.Add(id);
        }
        return ids;
    }

    private string GetOrynivoViewTitle(string view) => view switch
    {
        "Artists" => LocalizationManager.Current.Artists,
        "Albums" => LocalizationManager.Current.Albums,
        "Tracks" => LocalizationManager.Current.Tracks,
        "Folders" => LocalizationManager.Current.FolderStructure,
        _ => LocalizationManager.Current.OrynivoServers
    };

    private async void OrynivoServerViewModeRadioButton_OnChecked(object? sender, RoutedEventArgs e)
    {
        if (!IsVisible || _updatingViewMode ||
            sender is not RadioButton { IsChecked: true, Tag: string view } ||
            _activeOrynivoServer is null)
            return;

        _orynivoNavigationStack.Clear();
        _activeOrynivoView = view;
        ContentTitleTextBlock.Text = _activeOrynivoServer.Name;
        BackButton.IsVisible = _navigationStack.Count > 0;
        await LoadOrynivoViewAsync();
    }

    private async Task LoadOrynivoViewAsync(long? filterArtistId = null, long? filterAlbumId = null)
    {
        if (_activeOrynivoServer is null)
            return;

        ShowContentLoadingSkeleton();
        CancelAndDispose(ref _orynivoViewCts);
        _orynivoViewCts = new CancellationTokenSource();
        var ct = _orynivoViewCts.Token;
        var server = _activeOrynivoServer;
        var provider = CreateOrynivoCatalogProvider(server);
        var view = filterAlbumId.HasValue ? "AlbumTracks"
            : filterArtistId.HasValue ? "ArtistAlbums"
            : _activeOrynivoView;

        ContentDataGrid.IsVisible = true;
        FolderTreeView.IsVisible = false;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        // Hide the other full-area views so navigating into a remote view from the
        // dashboard, radio, podcast, lyrics, or artist-info view (e.g. a dashboard
        // recent-album card) does not leave that view covering the remote content.
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        StatusTextBlock.Text = LocalizationManager.Current.OrynivoLoading;
        ContentDataGrid.ItemsSource = null;
        if (view != "AlbumTracks")
            HideAlbumDetailHeader();
        UpdateLibraryIntroCard(view is "Artists" or "Albums" or "Tracks" or "Folders" ? view : null);
        UpdateEntityFavoritesFilterToggle(_currentTopLevelTag);
        TrackFilterButton.IsVisible = view == "Tracks";
        SaveSmartPlaylistButton.IsVisible = view == "Tracks";
        if (view == "Tracks")
            UpdateSaveSmartPlaylistButtonState();
        _orynivoTrackFacets = null;

        try
        {
            switch (view)
            {
                case "Artists":
                {
                    ApplyColumns("Artists");
                    var artists = await provider.GetArtistsAsync(ct);
                    if (ct.IsCancellationRequested) return;
                    var rows = artists.Select(artist => ToCatalogArtistContentRow(artist, server))
                    .Where(row => !_trackFavoritesOnly || row.IsFavorite)
                    .ToList();
                    AlbumViewModeBorder.IsVisible = true;
                    SetViewModeButtons(_showArtistArtworkView);
                    ContentDataGrid.IsVisible = !_showArtistArtworkView;
                    AlbumArtworkListBox.IsVisible = false;
                    ArtistArtworkListBox.IsVisible = _showArtistArtworkView;
                    ContentDataGrid.ItemsSource = rows;
                    BindArtworkRows("Artists", rows);
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                    break;
                }

                case "ArtistAlbums":
                {
                    ApplyColumns("Albums");
                    var albums = await provider.GetAlbumsByArtistAsync(
                        filterArtistId!.Value,
                        includeArtwork: true,
                        cancellationToken: ct);
                    if (ct.IsCancellationRequested) return;
                    var rows = albums.Select(album => ToCatalogAlbumContentRow(album, server))
                        .Where(row => !_trackFavoritesOnly || row.IsFavorite)
                        .ToList();
                    AlbumViewModeBorder.IsVisible = true;
                    SetViewModeButtons(_showAlbumArtworkView);
                    ContentDataGrid.IsVisible = !_showAlbumArtworkView;
                    AlbumArtworkListBox.IsVisible = _showAlbumArtworkView;
                    ArtistArtworkListBox.IsVisible = false;
                    ContentDataGrid.ItemsSource = rows;
                    BindArtworkRows("Albums", rows);
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                    break;
                }

                case "Albums":
                {
                    ApplyColumns("Albums");
                    var albums = await provider.GetAlbumsAsync(includeArtwork: true, cancellationToken: ct);
                    if (ct.IsCancellationRequested) return;
                    var rows = albums.Select(album => ToCatalogAlbumContentRow(album, server))
                        .Where(row => !_trackFavoritesOnly || row.IsFavorite)
                        .ToList();
                    AlbumViewModeBorder.IsVisible = true;
                    SetViewModeButtons(_showAlbumArtworkView);
                    ContentDataGrid.IsVisible = !_showAlbumArtworkView;
                    AlbumArtworkListBox.IsVisible = _showAlbumArtworkView;
                    ArtistArtworkListBox.IsVisible = false;
                    ContentDataGrid.ItemsSource = rows;
                    BindArtworkRows("Albums", rows);
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
                    break;
                }

                case "AlbumTracks":
                case "Tracks":
                {
                    AlbumViewModeBorder.IsVisible = false;
                    IReadOnlyList<LibraryCatalogTrack> catalogTracks;
                    if (filterAlbumId.HasValue)
                    {
                        catalogTracks = await provider.GetTracksByAlbumAsync(filterAlbumId.Value, filterArtistId, ct);
                    }
                    else
                    {
                        _orynivoTrackFacets = (await _orynivoClient.GetTrackFacetsAsync(server, ct))
                            .Select(f => f with
                            {
                                IsFavorite = IsOrynivoFavorite(server, "Track", f.Id),
                                SourceKey = GetServerSourceKey(server.Id)
                            })
                            .ToList();
                        if (ct.IsCancellationRequested) return;
                        catalogTracks = await ResolveOrynivoTrackRowsAsync(server, provider, ct);
                    }
                    if (ct.IsCancellationRequested) return;
                    // Build the columns immediately before binding, mirroring the local
                    // Tracks view. Applying them before the long-running async load lets
                    // the DataGrid run layout passes while still empty; Avalonia then
                    // fails to realize the rows that are bound in the async continuation,
                    // leaving only the column headers visible (most noticeable with the
                    // large unfiltered track set, while the small favourites set still
                    // happened to render).
                    ApplyColumns("Tracks");
                    var rows = catalogTracks.Select(track => ToCatalogTrackContentRow(track, server)).ToList();
                    ContentDataGrid.ItemsSource = rows;
                    UpdateAlphabetIndex(rows, true);
                    ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
                    break;
                }

                case "Folders":
                {
                    ContentDataGrid.IsVisible = false;
                    FolderTreeView.IsVisible = true;
                    ShowOrynivoFolderLoadingState();
                    var tracks = await LoadOrynivoFolderTracksAsync(server, ct);
                    if (ct.IsCancellationRequested) return;
                    var metadata = await LoadOrynivoFolderTrackMetadataAsync(server, tracks, ct);
                    if (ct.IsCancellationRequested) return;
                    BuildOrynivoFolderTree(server, tracks, metadata);
                    ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(tracks.Count);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }
        finally
        {
            HideContentLoadingSkeleton();
            FadeInVisibleContentSurface();
        }

        StatusTextBlock.Text = string.Empty;
    }

    private OrynivoServerLibraryCatalogProvider CreateOrynivoCatalogProvider(OrynivoServerSettings server)
        => new(server, _orynivoClient, (entityType, id) => IsOrynivoFavorite(server, entityType, id));

    private OrynivoServerNowPlayingMetadataProvider CreateOrynivoNowPlayingProvider(OrynivoServerSettings server)
        => new(server, _orynivoClient);

    private async Task<List<LibraryCatalogTrack>> LoadAllOrynivoTracksAsync(
        OrynivoServerSettings server,
        ILibraryCatalogProvider provider,
        CancellationToken cancellationToken)
    {
        // The server reports when its library index last changed; reuse the
        // locally cached track list while that timestamp is unchanged so the
        // full list does not have to be downloaded on every visit.
        var scanStatus = await _orynivoClient.GetScanStatusAsync(server, cancellationToken);
        var libraryChangedAt = scanStatus?.LibraryChangedAt;
        List<LibraryCatalogTrack>? tracks = null;
        if (libraryChangedAt.HasValue)
        {
            // Reading and deserializing the (potentially large) cache file runs on
            // a background thread so the UI thread is never blocked on disk I/O.
            tracks = await Task.Run(
                () => TryLoadOrynivoTrackListCache(server, libraryChangedAt.Value, out var cached)
                    ? cached
                    : null,
                cancellationToken);
        }

        if (tracks is null)
        {
            // Use a large page size so even big libraries load in one or two
            // requests. A page size of 500 issued one round-trip per 500 tracks,
            // which on a ~75k-track library meant ~150 sequential HTTP requests and
            // over a minute of loading, leaving the Tracks view apparently empty
            // (only the small favourites set, fetched in a single by-id request,
            // appeared). The server returns tens of thousands of rows per request in
            // well under a second, so a large page loads the whole library almost
            // instantly while staying comfortably below the client's HTTP timeout.
            const int pageSize = 50000;
            tracks = [];
            for (var page = 0; ; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await provider.GetTracksAsync(page, pageSize, cancellationToken);
                tracks.AddRange(batch);
                if (batch.Count < pageSize)
                    break;
            }

            if (libraryChangedAt.HasValue)
            {
                var toCache = tracks;
                await Task.Run(
                    () => SaveOrynivoTrackListCache(server, libraryChangedAt.Value, toCache),
                    cancellationToken);
            }
        }

        // Favourites are stored client-side and can change without the server's
        // library timestamp changing, so re-apply them after loading (including
        // from the cache) instead of trusting the cached favourite flags.
        return tracks
            .Select(track => track with { IsFavorite = IsOrynivoFavorite(server, "Track", track.Id) })
            .ToList();
    }

    private static bool TryLoadOrynivoTrackListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        out List<LibraryCatalogTrack> tracks)
    {
        tracks = [];
        try
        {
            var path = GetOrynivoTrackListCachePath(server);
            if (!File.Exists(path))
                return false;
            var cache = JsonSerializer.Deserialize<OrynivoTrackListCache>(File.ReadAllText(path));
            if (cache?.Tracks is null || cache.LibraryChangedAt != libraryChangedAt)
                return false;
            tracks = cache.Tracks;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveOrynivoTrackListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        IReadOnlyList<LibraryCatalogTrack> tracks)
    {
        try
        {
            var path = GetOrynivoTrackListCachePath(server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new OrynivoTrackListCache(
                libraryChangedAt,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                tracks.ToList());
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch
        {
            // Cache failures must never prevent browsing a remote server.
        }
    }

    private static string GetOrynivoTrackListCachePath(OrynivoServerSettings server)
        => RemoteServerCache.TrackListCachePath(server);

    private async Task<List<LibraryCatalogArtist>> LoadAllOrynivoArtistsAsync(
        OrynivoServerSettings server,
        ILibraryCatalogProvider provider,
        CancellationToken cancellationToken)
    {
        var scanStatus = await _orynivoClient.GetScanStatusAsync(server, cancellationToken);
        var libraryChangedAt = scanStatus?.LibraryChangedAt;
        List<LibraryCatalogArtist>? artists = null;
        if (libraryChangedAt.HasValue)
        {
            artists = await Task.Run(
                () => TryLoadOrynivoArtistListCache(server, libraryChangedAt.Value, out var cached)
                    ? cached
                    : null,
                cancellationToken);
        }

        if (artists is null)
        {
            artists = (await provider.GetArtistsAsync(cancellationToken)).ToList();
            if (libraryChangedAt.HasValue)
            {
                var toCache = artists;
                await Task.Run(
                    () => SaveOrynivoArtistListCache(server, libraryChangedAt.Value, toCache),
                    cancellationToken);
            }
        }

        return artists
            .Select(artist => artist with { IsFavorite = IsOrynivoFavorite(server, "Artist", artist.Id) })
            .ToList();
    }

    private async Task<List<LibraryCatalogAlbum>> LoadAllOrynivoAlbumsAsync(
        OrynivoServerSettings server,
        ILibraryCatalogProvider provider,
        CancellationToken cancellationToken)
    {
        var scanStatus = await _orynivoClient.GetScanStatusAsync(server, cancellationToken);
        var libraryChangedAt = scanStatus?.LibraryChangedAt;
        List<LibraryCatalogAlbum>? albums = null;
        if (libraryChangedAt.HasValue)
        {
            albums = await Task.Run(
                () => TryLoadOrynivoAlbumListCache(server, libraryChangedAt.Value, out var cached)
                    ? cached
                    : null,
                cancellationToken);
        }

        if (albums is null)
        {
            albums = (await provider.GetAlbumsAsync(includeArtwork: true, cancellationToken)).ToList();
            if (libraryChangedAt.HasValue)
            {
                var toCache = albums;
                await Task.Run(
                    () => SaveOrynivoAlbumListCache(server, libraryChangedAt.Value, toCache),
                    cancellationToken);
            }
        }

        return albums
            .Select(album => album with { IsFavorite = IsOrynivoFavorite(server, "Album", album.Id) })
            .ToList();
    }

    private static bool TryLoadOrynivoArtistListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        out List<LibraryCatalogArtist> artists)
    {
        artists = [];
        try
        {
            var path = GetOrynivoArtistListCachePath(server);
            if (!File.Exists(path))
                return false;
            var cache = JsonSerializer.Deserialize<OrynivoArtistListCache>(File.ReadAllText(path));
            if (cache?.Artists is null || cache.LibraryChangedAt != libraryChangedAt)
                return false;
            artists = cache.Artists;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadOrynivoAlbumListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        out List<LibraryCatalogAlbum> albums)
    {
        albums = [];
        try
        {
            var path = GetOrynivoAlbumListCachePath(server);
            if (!File.Exists(path))
                return false;
            var cache = JsonSerializer.Deserialize<OrynivoAlbumListCache>(File.ReadAllText(path));
            if (cache?.Albums is null || cache.LibraryChangedAt != libraryChangedAt)
                return false;
            albums = cache.Albums;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveOrynivoArtistListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        IReadOnlyList<LibraryCatalogArtist> artists)
    {
        try
        {
            var path = GetOrynivoArtistListCachePath(server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new OrynivoArtistListCache(
                libraryChangedAt,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                artists.ToList());
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch
        {
        }
    }

    /// <summary>Deletes the cached remote artist list for a server after profile or image metadata changes.</summary>
    /// <param name="server">Server whose artist list cache should be removed.</param>
    private static void DeleteOrynivoArtistListCache(OrynivoServerSettings server)
    {
        try
        {
            var path = GetOrynivoArtistListCachePath(server);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void SaveOrynivoAlbumListCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        IReadOnlyList<LibraryCatalogAlbum> albums)
    {
        try
        {
            var path = GetOrynivoAlbumListCachePath(server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new OrynivoAlbumListCache(
                libraryChangedAt,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                albums.ToList());
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch
        {
        }
    }

    private static string GetOrynivoArtistListCachePath(OrynivoServerSettings server)
    {
        var key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{server.Id}|{server.BaseUrl}|{server.ApiKey}")));
        return AppPaths.GetDataPath("remote-artist-cache", $"{key}.json");
    }

    private static string GetOrynivoAlbumListCachePath(OrynivoServerSettings server)
    {
        var key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{server.Id}|{server.BaseUrl}|{server.ApiKey}")));
        return AppPaths.GetDataPath("remote-album-cache", $"{key}.json");
    }

    /// <summary>
    /// Resolves the remote Tracks row set: facet-filtered when filters are active,
    /// otherwise the server search results, otherwise all tracks. Uses the same
    /// <see cref="MatchesTrackFilters"/> logic as the local Tracks view.
    /// </summary>
    /// <param name="provider">Active remote catalog provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The track rows to display.</returns>
    private async Task<IReadOnlyList<LibraryCatalogTrack>> ResolveOrynivoTrackRowsAsync(
        OrynivoServerSettings server,
        ILibraryCatalogProvider provider,
        CancellationToken cancellationToken)
    {
        if (HasActiveFilters && _orynivoTrackFacets is { } facets)
        {
            var ids = facets.Where(f => MatchesTrackFilters(f)).Select(f => f.Id).ToList();
            return await provider.GetTracksByIdsAsync(ids, cancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
            return await provider.SearchTracksAsync(SearchTextBox.Text.Trim(), 500, cancellationToken);
        return await LoadAllOrynivoTracksAsync(server, provider, cancellationToken);
    }

    /// <summary>Re-resolves and rebinds remote Tracks rows after a facet filter change.</summary>
    private async Task ApplyOrynivoTrackFiltersAsync()
    {
        if (_activeOrynivoServer is null)
            return;
        var provider = CreateOrynivoCatalogProvider(_activeOrynivoServer);
        var catalogTracks = await ResolveOrynivoTrackRowsAsync(_activeOrynivoServer, provider, CancellationToken.None);
        var rows = catalogTracks.Select(track => ToCatalogTrackContentRow(track, _activeOrynivoServer)).ToList();
        BindRemoteTrackRows(rows);
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
        UpdateSaveSmartPlaylistButtonState();
    }

    private void BindRemoteTrackRows(IReadOnlyList<ContentRow> rows)
    {
        ContentDataGrid.ItemsSource = rows;
    }

    private void ShowOrynivoFolderLoadingState()
    {
        _localFolderTrackItems.Clear();
        _localFolderTrackHeaders.Clear();
        FolderTreeView.Items.Clear();
        FolderTreeView.Items.Add(new TreeViewItem
        {
            Header = new TextBlock
            {
                Text = LocalizationManager.Current.OrynivoLoading,
                Foreground = FindResource<IBrush>("AppMutedTextBrush")
            },
            IsEnabled = false
        });
        ContentCountTextBlock.Text = string.Empty;
    }

    private async Task<List<OrynivoTrackLiteInfo>> LoadOrynivoFolderTracksAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken)
    {
        var scanStatus = await _orynivoClient.GetScanStatusAsync(server, cancellationToken);
        var libraryChangedAt = scanStatus?.LibraryChangedAt;
        if (libraryChangedAt.HasValue)
        {
            // Reading and deserializing the (potentially tens of MB) cache file must not block
            // the UI thread; only re-download from the server when the cache misses.
            var cachedTracks = await Task.Run(
                () => TryLoadOrynivoFolderTrackCache(server, libraryChangedAt.Value, out var t) ? t : null,
                cancellationToken);
            if (cachedTracks is not null)
                return cachedTracks;
        }

        var tracks = await _orynivoClient.GetTrackFoldersAsync(server, cancellationToken);
        if (libraryChangedAt.HasValue)
            await Task.Run(() => SaveOrynivoFolderTrackCache(server, libraryChangedAt.Value, tracks), cancellationToken);
        return tracks;
    }

    private static bool TryLoadOrynivoFolderTrackCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        out List<OrynivoTrackLiteInfo> tracks)
    {
        tracks = [];
        try
        {
            var path = GetOrynivoFolderTrackCachePath(server);
            if (!File.Exists(path))
                return false;
            var cache = JsonSerializer.Deserialize<OrynivoFolderTrackCache>(File.ReadAllText(path));
            if (cache?.Tracks is null || cache.LibraryChangedAt != libraryChangedAt)
                return false;
            tracks = cache.Tracks;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveOrynivoFolderTrackCache(
        OrynivoServerSettings server,
        long libraryChangedAt,
        IReadOnlyList<OrynivoTrackLiteInfo> tracks)
    {
        try
        {
            var path = GetOrynivoFolderTrackCachePath(server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new OrynivoFolderTrackCache(
                libraryChangedAt,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                tracks.ToList());
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch
        {
            // Cache failures must never prevent browsing a remote server.
        }
    }

    private static string GetOrynivoFolderTrackCachePath(OrynivoServerSettings server)
        => RemoteServerCache.FolderTrackCachePath(server);

    private async Task<Dictionary<long, OrynivoTrackInfo>> LoadOrynivoFolderTrackMetadataAsync(
        OrynivoServerSettings server,
        IReadOnlyList<OrynivoTrackLiteInfo> tracks,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, OrynivoTrackInfo>();
        if (tracks.All(track =>
                !string.IsNullOrWhiteSpace(track.Artist) ||
                !string.IsNullOrWhiteSpace(track.Album) ||
                track.ArtistId.HasValue ||
                track.AlbumId.HasValue))
        {
            return result;
        }

        var ids = tracks
            .Where(track => track.Id > 0)
            .Select(track => track.Id)
            .Distinct()
            .ToList();
        const int batchSize = 500;
        for (var index = 0; index < ids.Count; index += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = ids.Skip(index).Take(batchSize).ToList();
            var rows = await _orynivoClient.GetTracksByIdsAsync(server, batch, cancellationToken);
            foreach (var row in rows)
                result[row.Id] = row;
        }

        return result;
    }

    private void BuildOrynivoFolderTree(
        OrynivoServerSettings server,
        IReadOnlyList<OrynivoTrackLiteInfo> tracks,
        IReadOnlyDictionary<long, OrynivoTrackInfo> metadata)
    {
        BuildFolderTree(MapOrynivoFolderTrackLites(server, tracks, metadata), server);
    }

    /// <summary>
    /// Maps remote Orynivo Server folder tracks to <see cref="TrackLite"/> rows whose
    /// playback path is the authenticated stream URL and whose grouping path is the
    /// server-side source path. Registers each mapped row for transport metadata.
    /// </summary>
    /// <param name="server">The remote server the tracks belong to.</param>
    /// <param name="tracks">Lightweight folder tracks reported by the server.</param>
    /// <param name="metadata">Optional richer metadata keyed by track ID.</param>
    /// <returns>The mapped <see cref="TrackLite"/> rows for the folder tree.</returns>
    private List<TrackLite> MapOrynivoFolderTrackLites(
        OrynivoServerSettings server,
        IReadOnlyList<OrynivoTrackLiteInfo> tracks,
        IReadOnlyDictionary<long, OrynivoTrackInfo> metadata)
    {
        return tracks
            .Where(track => track.Id > 0)
            .Select(track =>
            {
                var row = metadata.TryGetValue(track.Id, out var fullTrack)
                    ? ToOrynivoTrackContentRow(server, fullTrack)
                    : ToOrynivoTrackContentRow(server, track);
                return new TrackLite(
                    row.FilePath,
                    string.IsNullOrWhiteSpace(track.SourcePath) ? track.Path : track.SourcePath,
                    track.FileName,
                    track.Title,
                    track.DiscNumber,
                    track.TrackNumber);
            })
            .ToList();
    }

    /// <summary>
    /// Builds the unified folder-structure view: a top-level <c>Local</c> group (only
    /// when a local library directory is configured) followed by one group per
    /// configured Orynivo Server (only when the server reports folder tracks), each
    /// containing its own folder tree. Loading runs off the UI thread and is cancelled
    /// when the user navigates away.
    /// </summary>
    /// <returns>A task that completes when the unified folder tree has been built.</returns>
    private async Task ShowUnifiedFolderTreeAsync()
    {
        CancelAndDispose(ref _folderViewCts);
        _folderViewCts = new CancellationTokenSource();
        var ct = _folderViewCts.Token;

        UpdateAlphabetIndex(null, false);
        ShowOrynivoFolderLoadingState();

        var hasLocalDirectory = _settings.LibraryPaths
            .Any(path => !string.IsNullOrWhiteSpace(path));
        var servers = _settings.OrynivoServers ?? [];

        try
        {
            var localTracks = hasLocalDirectory
                ? await Task.Run(() =>
                {
                    try { using var db = AudioDatabase.OpenDefault(); return db.GetTracksLite(); }
                    catch { return new List<TrackLite>(); }
                }, ct)
                : [];
            if (ct.IsCancellationRequested)
                return;

            var serverGroups = new List<(OrynivoServerSettings Server, List<TrackLite> Tracks)>();
            foreach (var server in servers)
            {
                try
                {
                    var tracks = await LoadOrynivoFolderTracksAsync(server, ct);
                    if (ct.IsCancellationRequested)
                        return;
                    var metadata = await LoadOrynivoFolderTrackMetadataAsync(server, tracks, ct);
                    if (ct.IsCancellationRequested)
                        return;
                    serverGroups.Add((server, MapOrynivoFolderTrackLites(server, tracks, metadata)));
                }
                catch (OperationCanceledException) { return; }
                catch
                {
                    // An unavailable server must not prevent browsing the remaining sources.
                }
            }

            if (ct.IsCancellationRequested)
                return;

            var totalTracks = BuildUnifiedFolderTree(localTracks, serverGroups);
            ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(totalTracks);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Populates <see cref="FolderTreeView"/> with a top-level source group per available
    /// source (local library plus each Orynivo Server) and their folder roots below.
    /// </summary>
    /// <param name="localTracks">Local library tracks (empty when no local directory is configured).</param>
    /// <param name="serverGroups">Per-server mapped folder tracks.</param>
    /// <returns>The total number of tracks placed in the tree.</returns>
    private int BuildUnifiedFolderTree(
        List<TrackLite> localTracks,
        IReadOnlyList<(OrynivoServerSettings Server, List<TrackLite> Tracks)> serverGroups)
    {
        _localFolderTrackItems.Clear();
        _localFolderTrackHeaders.Clear();
        _folderTreesBySource.Clear();
        FolderTreeView.Items.Clear();

        var total = 0;
        var hasLocal = localTracks.Count > 0 &&
                       _settings.LibraryPaths.Any(path => !string.IsNullOrWhiteSpace(path));
        var populatedServers = serverGroups.Where(group => group.Tracks.Count > 0).ToList();

        // With a single source, expand its group and roots so the folders show immediately (as
        // the local-only view did before). With several sources, keep the groups collapsed so
        // every source stays visible at the top instead of one burying the others.
        var expandGroups = (hasLocal ? 1 : 0) + populatedServers.Count <= 1;

        if (hasLocal)
        {
            var localNode = CreateFolderSourceGroupNode(LocalizationManager.Current.LocalSource, server: null);
            AddFolderRootsInto(localNode, localTracks, _settings.LibraryPaths, server: null, autoExpandRoots: expandGroups);
            localNode.IsExpanded = expandGroups;
            FolderTreeView.Items.Add(localNode);
            total += localTracks.Count;
        }

        foreach (var (server, tracks) in populatedServers)
        {
            var serverNode = CreateFolderSourceGroupNode(server.Name, server);
            AddFolderRootsInto(serverNode, tracks, preferredRoots: null, server, autoExpandRoots: expandGroups);
            serverNode.IsExpanded = expandGroups;
            FolderTreeView.Items.Add(serverNode);
            total += tracks.Count;
        }

        return total;
    }

    /// <summary>
    /// Creates a non-playable top-level tree node that groups the folder roots of one
    /// library source (the local library or a specific Orynivo Server).
    /// </summary>
    /// <param name="title">The display label of the source group.</param>
    /// <param name="server">The Orynivo Server this group represents, or <see langword="null"/> for local.</param>
    /// <returns>The created source group tree node.</returns>
    private TreeViewItem CreateFolderSourceGroupNode(string title, OrynivoServerSettings? server)
    {
        var item = new TreeViewItem
        {
            Header = new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            },
            Tag = new FolderTag(false, string.Empty, string.Empty, server)
        };
        return item;
    }

    private ContentRow ToOrynivoTrackContentRow(OrynivoServerSettings server, OrynivoTrackLiteInfo track)
    {
        var streamUrl = OrynivoServerClient.GetStreamUrl(server, track.Id);
        var row = new ContentRow
        {
            Title = track.Title?.Trim() ?? track.FileName.Trim(),
            AlphabetIndexText = track.Title?.Trim() ?? track.FileName.Trim(),
            Id = track.Id,
            Artist = track.Artist,
            Album = track.Album,
            AlbumArtist = track.AlbumArtist,
            TrackNumber = FormatPartNumber(track.TrackNumber, null),
            DiscNumber = FormatPartNumber(track.DiscNumber, null),
            Duration = FormatSeconds(track.Duration),
            Format = track.Format?.ToUpperInvariant(),
            FileName = track.FileName,
            FilePath = streamUrl,
            SourcePath = string.IsNullOrWhiteSpace(track.SourcePath) ? track.Path : track.SourcePath,
            ArtworkPath = OrynivoServerClient.GetTrackArtworkUrl(server, track.Id, 320),
            ThumbnailPath = OrynivoServerClient.GetTrackArtworkUrl(server, track.Id, 96),
            IsFavorite = IsOrynivoFavorite(server, "Track", track.Id),
            ArtistId = track.ArtistId,
            AlbumId = track.AlbumId,
            EntityType = "OrynivoTrack",
            ExternalId = track.Id.ToString(CultureInfo.InvariantCulture),
            KnownDuration = track.Duration.HasValue ? TimeSpan.FromSeconds(track.Duration.Value) : null,
            OrynivoServer = server
        };
        _orynivoTracksByUrl[streamUrl] = row;
        return row;
    }

    private static async Task LoadOrynivoArtworkAsync(ContentRow row, string artUrl)
    {
        try
        {
            var artwork = await LoadRemoteArtworkImageAsync(artUrl, 320);
            var thumbnail = await LoadRemoteArtworkImageAsync(artUrl, 96);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                row.Artwork = artwork;
                row.Thumbnail = thumbnail;
                row.ArtworkLoadQueued = false;
                row.ArtworkLoadCompleted = true;
                row.ThumbnailLoadQueued = false;
                row.ThumbnailLoadCompleted = true;
            }, DispatcherPriority.Background);
        }
        catch { }
    }

    private async Task LoadPlexViewAsync(bool reset)
    {
        if (_activePlexServer is null || string.IsNullOrWhiteSpace(_activePlexSectionKey))
            return;

        CancelAndDispose(ref _plexViewCts);
        _plexViewCts = new CancellationTokenSource();
        var cancellationToken = _plexViewCts.Token;
        var loadVersion = ++_plexViewLoadVersion;
        var server = _activePlexServer;
        var token = _activePlexToken;
        var sectionKey = _activePlexSectionKey;
        var view = _activePlexView;
        if (reset)
        {
            _plexLoadedCount = 0;
            _plexTotalCount = 0;
        }

        ContentDataGrid.IsVisible = view != "Folders";
        FolderTreeView.IsVisible = view == "Folders";
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        PlexLoadMoreButton.IsVisible = false;
        StatusTextBlock.Text = LocalizationManager.Current.PlexLoading;
        if (view == "Folders")
            UpdateAlphabetIndex(null, false);

        try
        {
            if (view == "Folders")
            {
                await BuildPlexFolderTreeAsync(
                    server,
                    token,
                    sectionKey,
                    cancellationToken);
                if (loadVersion != _plexViewLoadVersion ||
                    !string.Equals(view, _activePlexView, StringComparison.Ordinal))
                {
                    return;
                }
                UpdatePlexFolderAlphabetIndex();
                ContentCountTextBlock.Text = string.Empty;
                StatusTextBlock.Text = string.Empty;
                return;
            }

            var mediaType = view switch
            {
                "Artists" => 8,
                "Albums" => 9,
                _ => 10
            };
            var page = await _plexClient.GetLibraryItemsAsync(
                server,
                token,
                sectionKey,
                mediaType,
                _plexLoadedCount,
                PlexPageSize,
                cancellationToken);
            if (loadVersion != _plexViewLoadVersion ||
                !string.Equals(view, _activePlexView, StringComparison.Ordinal))
            {
                return;
            }

            var newRows = page.Items
                .Select(item => ToPlexContentRow(item, view, server, token))
                .ToList();
            var rows = reset
                ? newRows
                : ((ContentDataGrid.ItemsSource as IEnumerable<ContentRow>) ?? [])
                    .Concat(newRows)
                    .ToList();
            _plexLoadedCount = rows.Count;
            _plexTotalCount = page.TotalSize;
            ApplyColumns("Plex" + view);
            ContentDataGrid.ItemsSource = rows;
            UpdateAlphabetIndex(rows, view is "Artists" or "Albums" or "Tracks");
            ContentCountTextBlock.Text = $"{_plexLoadedCount:N0} / {_plexTotalCount:N0}";
            PlexLoadMoreButton.IsVisible = _plexLoadedCount < _plexTotalCount;
            StatusTextBlock.Text = string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.PlexConnectionFailed,
                ex.Message);
        }
    }

    private ContentRow ToPlexContentRow(
        PlexMediaItem item,
        string view,
        PlexServerSettings server,
        string? token)
    {
        var entityType = view switch
        {
            "Artists" => "PlexArtist",
            "Albums" => "PlexAlbum",
            _ => "PlexTrack"
        };
        var row = new ContentRow
        {
            ExternalId = item.RatingKey,
            Title = item.Title,
            Artist = item.Artist,
            Album = item.Album,
            Year = item.Year?.ToString(),
            Duration = item.DurationMilliseconds is long duration
                ? FormatSeconds(duration / 1000d)
                : string.Empty,
            Format = item.Format?.ToUpperInvariant(),
            FilePath = item.PartKeys.Count > 0
                ? PlexServerClient.CreateStreamUrl(
                    server,
                    item.PartKeys[0],
                    token)
                : string.Empty,
            PlexPartUrls = item.PartKeys
                .Select(partKey => PlexServerClient.CreateStreamUrl(server, partKey, token))
                .ToArray(),
            KnownDuration = item.DurationMilliseconds is long durationMilliseconds
                ? TimeSpan.FromMilliseconds(durationMilliseconds)
                : null,
            EntityType = entityType,
            PlexServerId = server.Id,
            PlexAlbumRatingKey = item.ParentRatingKey,
            PlexArtistRatingKey = item.GrandparentRatingKey
        };
        if (entityType == "PlexTrack" && row.FilePath.Length > 0)
            _plexTracksByUrl[row.FilePath] = row;
        return row;
    }

    private async Task ShowPlexChildrenAsync(ContentRow parent)
    {
        if (_activePlexServer is null || string.IsNullOrWhiteSpace(parent.ExternalId))
            return;

        StatusTextBlock.Text = LocalizationManager.Current.PlexLoading;
        try
        {
            var page = await _plexClient.GetChildrenAsync(
                _activePlexServer,
                _activePlexToken,
                parent.ExternalId);
            _plexNavigationStack.Push(new PlexNavigationState(
                ContentTitleTextBlock.Text ?? string.Empty,
                _activePlexView,
                (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? []));
            BackButton.IsVisible = true;
            _activePlexView = parent.EntityType == "PlexArtist" ? "Albums" : "Tracks";
            var rows = page.Items
                .Select(item => ToPlexContentRow(
                    item,
                    _activePlexView,
                    _activePlexServer,
                    _activePlexToken))
                .ToList();
            ApplyColumns("Plex" + _activePlexView);
            ContentDataGrid.ItemsSource = rows;
            ContentTitleTextBlock.Text = parent.Title ?? _activePlexSectionTitle;
            ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
            PlexViewModeBorder.IsVisible = false;
            StatusTextBlock.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.PlexConnectionFailed,
                ex.Message);
        }
    }

    private async Task BuildPlexFolderTreeAsync(
        PlexServerSettings server,
        string? token,
        string sectionKey,
        CancellationToken cancellationToken)
    {
        _localFolderTrackItems.Clear();
        _localFolderTrackHeaders.Clear();
        FolderTreeView.Items.Clear();
        var page = await _plexClient.GetFoldersAsync(
            server,
            token,
            sectionKey,
            null,
            cancellationToken);
        foreach (var folder in page.Items)
            FolderTreeView.Items.Add(CreatePlexFolderItem(folder, server, token, sectionKey));
    }

    private TreeViewItem CreatePlexFolderItem(
        PlexMediaItem item,
        PlexServerSettings server,
        string? token,
        string sectionKey)
    {
        var row = item.IsFolder || item.PartKeys.Count == 0
            ? null
            : ToPlexContentRow(item, "Tracks", server, token);
        var treeItem = new TreeViewItem
        {
            Header = item.Title,
            Tag = new PlexFolderTag(item.Key, row is not null, row)
        };
        ApplyNowPlayingClass(treeItem);
        if (row is not null)
        {
            treeItem.ContextFlyout = BuildQueueContextFlyout([row.FilePath]);
            AttachFolderPlaylistContextHandler(treeItem);
            return treeItem;
        }

        var placeholder = new TreeViewItem();
        treeItem.Items.Add(placeholder);
        var isLoaded = false;
        Task? loadingTask = null;

        async Task LoadChildrenAsync()
        {
            if (isLoaded)
                return;
            if (loadingTask is not null)
            {
                await loadingTask;
                return;
            }

            loadingTask = LoadCoreAsync();
            await loadingTask;
            loadingTask = null;

            async Task LoadCoreAsync()
            {
                try
                {
                    var page = await _plexClient.GetFoldersAsync(
                        server,
                        token,
                        sectionKey,
                        item.Key);
                    treeItem.Items.Clear();
                    foreach (var child in page.Items)
                        treeItem.Items.Add(CreatePlexFolderItem(child, server, token, sectionKey));
                    isLoaded = true;
                    treeItem.InvalidateMeasure();
                }
                catch (Exception ex)
                {
                    treeItem.Items.Clear();
                    treeItem.Items.Add(placeholder);
                    StatusTextBlock.Text = string.Format(
                        LocalizationManager.Current.PlexConnectionFailed,
                        ex.Message);
                }
            }
        }

        treeItem.AddHandler(
            PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>(async (_, e) =>
            {
                if (!e.GetCurrentPoint(treeItem).Properties.IsLeftButtonPressed ||
                    !ReferenceEquals(FindAncestor<TreeViewItem>(e.Source as Visual), treeItem))
                {
                    return;
                }

                var isChevronPress = FindAncestor<ToggleButton>(e.Source as Visual) is not null;
                if (isChevronPress)
                {
                    if (isLoaded)
                        return;
                    e.Handled = true;
                    await LoadChildrenAsync();
                    if (isLoaded)
                        treeItem.IsExpanded = true;
                    return;
                }

                if (e.ClickCount < 2)
                    return;

                e.Handled = true;
                if (treeItem.IsExpanded)
                {
                    treeItem.IsExpanded = false;
                    return;
                }

                await LoadChildrenAsync();
                if (isLoaded)
                    treeItem.IsExpanded = true;
            }),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        treeItem.AddHandler(
            InputElement.DoubleTappedEvent,
            new EventHandler<TappedEventArgs>((_, e) =>
            {
                if (ReferenceEquals(FindAncestor<TreeViewItem>(e.Source as Visual), treeItem) &&
                    FindAncestor<ToggleButton>(e.Source as Visual) is null)
                    e.Handled = true;
            }),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        treeItem.Expanded += async (_, _) =>
        {
            if (isLoaded)
                return;

            treeItem.IsExpanded = false;
            await LoadChildrenAsync();
            if (isLoaded)
                treeItem.IsExpanded = true;
        };
        return treeItem;
    }

    private static string GetAlphabetIndexKey(ContentRow row) =>
        GetAlphabetIndexKey(row.AlphabetIndexText ?? row.Title);

    private static string GetAlphabetIndexKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "#";

        var trimmed = value.TrimStart();
        if (!Rune.TryGetRuneAt(trimmed, 0, out var first))
            return "#";

        var mapped = first.Value switch
        {
            'ß' or 'ẞ' => "S",
            'Æ' or 'æ' => "A",
            'Ø' or 'ø' => "O",
            _ => first.ToString()
        };

        var normalized = mapped.Normalize(NormalizationForm.FormD);
        foreach (var character in normalized.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            var upper = Rune.ToUpperInvariant(character);
            return upper.Value is >= 'A' and <= 'Z'
                ? ((char)upper.Value).ToString()
                : "#";
        }

        return "#";
    }

    private void AlphabetIndexButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;
        if (button.Tag is ContentRow row)
            ScrollToAlphabetRow(row);
        else if (button.Tag is AlphabetTreeTarget target)
            ScrollToAlphabetTreeTarget(target);
    }

    private void ScrollToAlphabetTreeTarget(AlphabetTreeTarget target)
    {
        _isAlphabetProgrammaticScroll = true;
        SetActiveAlphabetButton(target.Key);
        FolderTreeView.ScrollIntoView(target.Item);
        Dispatcher.UIThread.Post(() =>
        {
            _isAlphabetProgrammaticScroll = false;
        }, DispatcherPriority.Loaded);
    }

    private void ScrollToAlphabetRow(ContentRow row)
    {
        var targetKey = GetAlphabetIndexKey(row);
        _isAlphabetProgrammaticScroll = true;
        SetActiveAlphabetButton(targetKey);

        if (ContentDataGrid.IsVisible)
        {
            // Let DataGrid translate the logical item into its internal pixel offset.
            ContentDataGrid.ScrollIntoView(row, null);
            Dispatcher.UIThread.Post(() =>
            {
                _isAlphabetProgrammaticScroll = false;
            }, DispatcherPriority.Loaded);
        }
        else
        {
            var listBox = AlbumArtworkListBox.IsVisible
                ? AlbumArtworkListBox
                : ArtistArtworkListBox;
            EnsureArtworkRowBound(listBox, row);
            ScrollArtworkRowIntoViewAfterLayout(listBox, row);
        }
    }

    private void AlphabetTarget_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ListBox listBox && (ReferenceEquals(listBox, AlbumArtworkListBox) || ReferenceEquals(listBox, ArtistArtworkListBox)))
        {
            AppendArtworkRowsIfNeeded(listBox);
            QueueHydrateVisibleArtworkRows(listBox);
        }

        if (!AlphabetIndexBorder.IsVisible ||
            _isAlphabetProgrammaticScroll ||
            _alphabetScrollUpdatePending)
            return;

        _alphabetScrollUpdatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _alphabetScrollUpdatePending = false;
            UpdateActiveAlphabetButton();
        }, DispatcherPriority.Background);
    }

    private void ContentDataGrid_OnVerticalScroll(object? sender, Avalonia.Controls.Primitives.ScrollEventArgs e)
    {
        var scrollEventCount = Interlocked.Increment(ref _diagnosticScrollEventCount);
        if (scrollEventCount <= 5 || scrollEventCount % 20 == 0)
        {
            LogUiDiagnostics(
                $"ContentDataGrid_OnVerticalScroll event={scrollEventCount} value={e.NewValue:F2} scrollbarValue={_contentDataGridVerticalScrollBar?.Value.ToString("F2", CultureInfo.InvariantCulture) ?? "<null>"} rowHeight={_contentDataGridAverageRowHeight:F2}");
        }
        UpdateContentDataGridPageSize();
        if (!AlphabetIndexBorder.IsVisible || _isAlphabetProgrammaticScroll || _alphabetScrollUpdatePending)
            return;

        _alphabetScrollUpdatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _alphabetScrollUpdatePending = false;
            UpdateActiveAlphabetButton();
        }, DispatcherPriority.Background);
    }

    private void UpdateActiveAlphabetButton()
    {
        if (!AlphabetIndexBorder.IsVisible)
            return;

        if (FolderTreeView.IsVisible &&
            string.Equals(_activePlexView, "Folders", StringComparison.Ordinal))
        {
            SetActiveAlphabetButton(GetTopVisiblePlexFolderAlphabetKey());
            return;
        }

        var row = GetTopVisibleAlphabetRow();
        var activeKey = row is null ? null : GetAlphabetIndexKey(row);
        SetActiveAlphabetButton(activeKey);
    }

    private void SetActiveAlphabetButton(string? activeKey)
    {
        foreach (var button in AlphabetIndexPanel.Children.OfType<Button>())
        {
            var isActive = string.Equals(button.Content as string, activeKey, StringComparison.Ordinal);
            button.Classes.Set("active", isActive);
        }
    }

    private void AttachContentDataGridVerticalScrollBar()
    {
        // PART_VerticalScrollbar is created by the DataGrid template after layout.
        _contentDataGridVerticalScrollBar = ContentDataGrid
            .GetVisualDescendants()
            .OfType<ScrollBar>()
            .FirstOrDefault(scrollBar =>
                scrollBar.Orientation == Orientation.Vertical &&
                string.Equals(scrollBar.Name, "PART_VerticalScrollbar", StringComparison.Ordinal));
        if (_contentDataGridVerticalScrollBar is not null)
        {
            LogUiDiagnostics(
                $"AttachContentDataGridVerticalScrollBar attached value={_contentDataGridVerticalScrollBar.Value:F2} viewport={_contentDataGridVerticalScrollBar.ViewportSize:F2} max={_contentDataGridVerticalScrollBar.Maximum:F2}");
            UpdateContentDataGridPageSize();
            UpdateActiveAlphabetButton();
        }
        else
        {
            LogUiDiagnostics("AttachContentDataGridVerticalScrollBar no PART_VerticalScrollbar found");
        }
    }

    private void UpdateContentDataGridPageSize()
    {
        if (_contentDataGridVerticalScrollBar is null)
            return;

        // DataGrid scroll values are pixels. One page intentionally overlaps by one
        // row so users retain visual context after clicking the scrollbar track.
        var rowHeight = FindVisualChildren<DataGridRow>(ContentDataGrid)
            .Where(row => row.IsVisible && row.Bounds.Height > 0)
            .Select(row => row.Bounds.Height)
            .FirstOrDefault();
        if (rowHeight > 0 && double.IsFinite(rowHeight))
            _contentDataGridAverageRowHeight = rowHeight;
        var pageSize = _contentDataGridVerticalScrollBar.ViewportSize - rowHeight;
        if (pageSize > 0 && double.IsFinite(pageSize))
            _contentDataGridVerticalScrollBar.LargeChange = pageSize;
    }

    private ContentRow? GetTopVisibleAlphabetRow()
    {
        Control? targetControl = ContentDataGrid.IsVisible
            ? ContentDataGrid
            : AlbumArtworkListBox.IsVisible
                ? AlbumArtworkListBox
                : ArtistArtworkListBox.IsVisible
                    ? ArtistArtworkListBox
                    : null;
        if (targetControl is null)
            return null;

        if (targetControl is DataGrid &&
            _contentDataGridVerticalScrollBar is not null &&
            ContentDataGrid.ItemsSource is System.Collections.IList items &&
            items.Count > 0)
        {
            var rowHeight = _contentDataGridAverageRowHeight > 0
                ? _contentDataGridAverageRowHeight
                : 32d;
            var topIndex = (int)Math.Floor(_contentDataGridVerticalScrollBar.Value / rowHeight);
            topIndex = Math.Clamp(topIndex, 0, items.Count - 1);
            return items[topIndex] as ContentRow;
        }

        Control? bestContainer = null;
        var bestTop = double.PositiveInfinity;
        var visibleTop = 0d;
        if (targetControl is DataGrid &&
            FindVisualChild<Avalonia.Controls.Primitives.DataGridColumnHeadersPresenter>(targetControl) is { } headers)
        {
            visibleTop = (headers.TranslatePoint(new Point(0, headers.Bounds.Height), targetControl)?.Y ?? 0);
        }
        var candidates = targetControl is DataGrid
            ? FindVisualChildren<DataGridRow>(targetControl).Cast<Control>()
            : FindVisualChildren<ListBoxItem>(targetControl).Cast<Control>();
        foreach (var container in candidates)
        {
            if (!container.IsVisible || container.DataContext is not ContentRow)
                continue;

            var topLeft = container.TranslatePoint(new Point(0, 0), targetControl) ?? new Point(0, 0);
            var bounds = new Rect(topLeft.X, topLeft.Y, container.Bounds.Width, container.Bounds.Height);
            if (bounds.Bottom <= visibleTop || bounds.Top >= targetControl.Bounds.Height)
                continue;
            if (bounds.Top < bestTop)
            {
                bestTop = bounds.Top;
                bestContainer = container;
            }
        }

        return bestContainer?.DataContext as ContentRow;
    }

    private string? GetTopVisiblePlexFolderAlphabetKey()
    {
        TreeViewItem? bestItem = null;
        var bestTop = double.PositiveInfinity;
        foreach (var item in FolderTreeView.Items.OfType<TreeViewItem>())
        {
            if (!item.IsVisible || item.Tag is not PlexFolderTag { IsTrack: false })
                continue;
            var top = item.TranslatePoint(new Point(0, 0), FolderTreeView)?.Y;
            if (top is null || top + item.Bounds.Height <= 0 || top >= FolderTreeView.Bounds.Height)
                continue;
            if (top < bestTop)
            {
                bestTop = top.Value;
                bestItem = item;
            }
        }

        return bestItem is null
            ? null
            : GetAlphabetIndexKey(GetTreeItemHeader(bestItem));
    }

    private static string? GetTreeItemHeader(TreeViewItem item) =>
        item.Header switch
        {
            string text => text,
            TextBlock textBlock => textBlock.Text,
            _ => item.Header?.ToString()
        };

    private void AlphabetIndexBorder_OnPreviewMouseLeftButtonDown(object sender, PointerPressedEventArgs e)
    {
        _isDraggingAlphabetIndex = true;
        e.Pointer.Capture(AlphabetIndexBorder);
        ScrollToAlphabetPosition(e.GetPosition(AlphabetIndexPanel));
        e.Handled = true;
    }

    private void AlphabetIndexBorder_OnPreviewMouseMove(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingAlphabetIndex || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;
        ScrollToAlphabetPosition(e.GetPosition(AlphabetIndexPanel));
        e.Handled = true;
    }

    private void AlphabetIndexBorder_OnPreviewMouseLeftButtonUp(object sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingAlphabetIndex)
            return;
        ScrollToAlphabetPosition(e.GetPosition(AlphabetIndexPanel));
        _isDraggingAlphabetIndex = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ScrollToAlphabetPosition(Point point)
    {
        if (AlphabetIndexPanel.Children.Count == 0 || AlphabetIndexPanel.Bounds.Height <= 0)
            return;

        var button = AlphabetIndexPanel.Children
            .OfType<Button>()
            .OrderBy(candidate =>
            {
                var top = candidate.TranslatePoint(new Point(0, 0), AlphabetIndexPanel)?.Y ?? 0;
                return Math.Abs(point.Y - (top + candidate.Bounds.Height / 2));
            })
            .FirstOrDefault();

        if (button is not { IsEnabled: true })
            return;
        if (button.Tag is ContentRow row)
            ScrollToAlphabetRow(row);
        else if (button.Tag is AlphabetTreeTarget target)
            ScrollToAlphabetTreeTarget(target);
    }

    private T? FindResource<T>(string key) where T : class
    {
        if (TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var value) && value is T t)
            return t;
        if (Avalonia.Application.Current?.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out value) == true && value is T t2)
            return t2;
        return null;
    }

    /// <summary>Resolves one of the shared <c>FontSize…</c> typography tokens for code-built controls.</summary>
    /// <param name="key">The typography resource key (e.g. <c>FontSizeBody</c>).</param>
    /// <returns>The token's pixel size, or a body-text fallback when it is missing.</returns>
    private double ResolveFontSize(string key)
    {
        if (TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var value) && value is double d)
            return d;
        if (Avalonia.Application.Current?.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out value) == true && value is double d2)
            return d2;
        return 13;
    }

    private static T? FindAncestor<T>(Visual? current) where T : Visual
    {
        return current?.GetSelfAndVisualAncestors().OfType<T>().FirstOrDefault();
    }

    private static T? FindVisualChild<T>(Visual parent) where T : Visual
        => FindVisualChildren<T>(parent).FirstOrDefault();

    private static IEnumerable<T> FindVisualChildren<T>(Visual parent) where T : Visual
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is T match)
                yield return match;
            if (child is Visual v)
                foreach (var descendant in FindVisualChildren<T>(v))
                    yield return descendant;
        }
    }

    private string GetPlaylistName(string tag)
    {
        if (!long.TryParse(tag.AsSpan("Playlist:".Length), out long id))
            return "Playlist";
        try
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetPlaylistById(id)?.Name ?? "Playlist";
        }
        catch { return "Playlist"; }
    }

    private string GetOrynivoPlaylistName(string tag)
        => _orynivoPlaylistsByTag.TryGetValue(tag, out var playlist)
            ? playlist.Name
            : LocalizationManager.Current.Playlists;

    private bool TryParseOrynivoPlaylistTag(
        string tag,
        out OrynivoServerSettings? server,
        out long playlistId)
    {
        server = null;
        playlistId = 0;
        if (!tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal))
            return false;

        var parts = tag.Split(':');
        if (parts.Length != 3 || !long.TryParse(parts[2], out playlistId))
            return false;

        server = _settings.OrynivoServers.FirstOrDefault(item => item.Id == parts[1]);
        return server is not null;
    }

    private string GetRadioName(string tag)
    {
        if (!long.TryParse(tag.AsSpan("Radio:".Length), out var id))
            return LocalizationManager.Current.InternetRadio;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetRadioStation(id)?.Name ?? LocalizationManager.Current.InternetRadio;
        }
        catch
        {
            return LocalizationManager.Current.InternetRadio;
        }
    }

    private string GetPodcastName(string tag)
    {
        if (!long.TryParse(tag.AsSpan("Podcast:".Length), out var id))
            return LocalizationManager.Current.Podcasts;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetPodcast(id)?.Name ?? LocalizationManager.Current.Podcasts;
        }
        catch
        {
            return LocalizationManager.Current.Podcasts;
        }
    }

    private async Task ShowOrynivoPlaylistAsync(string tag)
    {
        if (!TryParseOrynivoPlaylistTag(tag, out var server, out var playlistId) || server is null)
            return;

        ContentDataGrid.IsVisible = true;
        FolderTreeView.IsVisible = false;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        ApplyColumns(tag);
        StatusTextBlock.Text = LocalizationManager.Current.OrynivoLoading;

        // Smart playlists are resolved with the client's favourites because remote
        // favourite state lives client-side (settings.json), not on the server.
        var isSmart = _orynivoPlaylistsByTag.TryGetValue(tag, out var playlistInfo) &&
                      playlistInfo.IsSmartPlaylist;
        var entries = isSmart
            ? await _orynivoClient.ResolveSmartPlaylistTracksAsync(
                server,
                playlistId,
                GetOrynivoFavoriteTrackIds(server))
            : await _orynivoClient.GetPlaylistTracksAsync(server, playlistId);
        var rows = entries.Select((entry, index) =>
        {
            if (entry.Track is null)
            {
                return new ContentRow
                {
                    Nr = (index + 1).ToString(CultureInfo.CurrentCulture),
                    PlaylistEntryId = entry.PlaylistEntryId > 0 ? entry.PlaylistEntryId : null,
                    Title = Path.GetFileName(entry.Path),
                    FileName = Path.GetFileName(entry.Path),
                    FilePath = string.Empty,
                    EntityType = "OrynivoTrack",
                    OrynivoServer = server
                };
            }

            var row = ToOrynivoTrackContentRow(server, entry.Track);
            row.Nr = entry.Position.ToString(CultureInfo.CurrentCulture);
            row.PlaylistEntryId = entry.PlaylistEntryId > 0 ? entry.PlaylistEntryId : null;
            return row;
        }).ToList();

        ContentDataGrid.ItemsSource = rows;
        ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
        StatusTextBlock.Text = string.Empty;
    }

    private ContentRow ToOrynivoTrackContentRow(OrynivoServerSettings server, OrynivoTrackInfo track)
    {
        var streamUrl = OrynivoServerClient.GetStreamUrl(server, track.Id);
        var row = new ContentRow
        {
            Title = track.Title?.Trim() ?? track.FileName.Trim(),
            AlphabetIndexText = track.SortTitle?.Trim() ?? track.Title?.Trim() ?? track.FileName.Trim(),
            Id = track.Id,
            Artist = track.Artist,
            Album = track.Album,
            AlbumArtist = track.AlbumArtist,
            Year = track.Year?.ToString(CultureInfo.CurrentCulture),
            TrackNumber = FormatPartNumber(track.TrackNumber, track.TrackTotal),
            DiscNumber = FormatPartNumber(track.DiscNumber, track.DiscTotal),
            Duration = FormatSeconds(track.Duration),
            Genre = track.Genre,
            Format = track.Format?.ToUpperInvariant(),
            Bitrate = track.Bitrate is > 0 ? $"{track.Bitrate:N0} kbps" : null,
            SampleRate = track.SampleRate is > 0 ? $"{track.SampleRate:N0} Hz" : null,
            SampleRateHz = track.SampleRate,
            BitDepth = track.BitDepth is > 0 ? $"{track.BitDepth:N0} Bit" : null,
            Channels = track.Channels?.ToString(CultureInfo.CurrentCulture),
            ChannelCount = track.Channels,
            Composer = track.Composer,
            Bpm = track.Bpm?.ToString(CultureInfo.CurrentCulture),
            FileName = track.FileName,
            FileSize = FormatFileSize(track.FileSize),
            AddedAt = track.AddedAt.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(track.AddedAt.Value)
                    .ToLocalTime()
                    .ToString("d", CultureInfo.CurrentCulture)
                : null,
            ReplayGainTrack = FormatReplayGainDisplay(track.ReplayGainTrack),
            ReplayGainAlbum = FormatReplayGainDisplay(track.ReplayGainAlbum),
            FilePath = streamUrl,
            SourcePath = track.SourcePath ?? track.Path,
            ArtworkPath = OrynivoServerClient.GetTrackArtworkUrl(server, track.Id, 320),
            ThumbnailPath = OrynivoServerClient.GetTrackArtworkUrl(server, track.Id, 96),
            IsFavorite = IsOrynivoFavorite(server, "Track", track.Id),
            ArtistId = track.ArtistId,
            AlbumId = track.AlbumId,
            EntityType = "OrynivoTrack",
            ExternalId = track.Id.ToString(CultureInfo.InvariantCulture),
            KnownDuration = track.Duration.HasValue ? TimeSpan.FromSeconds(track.Duration.Value) : null,
            OrynivoServer = server
        };
        _orynivoTracksByUrl[streamUrl] = row;
        return row;
    }

    // ------------------------------------------------------------------
    // DB-Abfragen
    // ------------------------------------------------------------------

    private List<ContentRow> QueryRows(string view, string? searchQuery = null)
    {
        try
        {
            using var db = AudioDatabase.OpenDefault();

            if (view.StartsWith("Playlist:") && long.TryParse(view.AsSpan("Playlist:".Length), out long pid))
            {
                var playlist = db.GetPlaylistById(pid);
                if (playlist is null) return [];

                if (playlist.IsSmartPlaylist && playlist.FilterCriteria is not null)
                {
                    SmartPlaylistCriteria criteria;
                    try { criteria = JsonSerializer.Deserialize<SmartPlaylistCriteria>(playlist.FilterCriteria)!; }
                    catch { return []; }

                    return ResolveUnifiedSmartPlaylistRows(criteria);
                }

                var ptracks = db.GetPlaylistTracks(pid).ToList();
                return ptracks.Select((pt, i) =>
                {
                    var t = db.GetByPath(pt.Path);
                    if (t is null)
                    {
                        if (TryResolveOrynivoPlaylistReference(pt.Path, out var server, out var trackId))
                        {
                            try
                            {
                                var provider = CreateOrynivoCatalogProvider(server);
                                var remoteTrack = provider.GetTracksByIdsAsync([trackId])
                                    .GetAwaiter()
                                    .GetResult()
                                    .FirstOrDefault();
                                if (remoteTrack is not null)
                                {
                                    var remoteRow = ToCatalogTrackContentRow(remoteTrack, server);
                                    remoteRow.Nr = (i + 1).ToString();
                                    remoteRow.PlaylistEntryId = pt.Id;
                                    return remoteRow;
                                }
                            }
                            catch
                            {
                                // Fall through to a plain placeholder row.
                            }
                        }

                        return new ContentRow
                        {
                            Nr = (i + 1).ToString(),
                            PlaylistEntryId = pt.Id,
                            Title = Path.GetFileName(pt.Path),
                            FileName = Path.GetFileName(pt.Path),
                            FilePath = pt.Path
                        };
                    }

                    var row = ToTrackContentRow(ToTrackListInfo(t));
                    row.Nr = (i + 1).ToString();
                    row.PlaylistEntryId = pt.Id;
                    return row;
                }).ToList();
            }

            return view switch
            {
                "Search" => db.GetTrackListByIds(TrackSearchIndex.SearchByCategory(searchQuery ?? string.Empty).Tracks.Ids)
                    .Select(ToTrackContentRow)
                    .ToList(),

                "Artists" => _localCatalogProvider.GetArtistsAsync()
                    .GetAwaiter()
                    .GetResult()
                    .Where(a => !_artistFavoritesOnly || a.IsFavorite)
                    .Select(artist => ToCatalogArtistContentRow(artist))
                    .ToList(),

                "Albums" => (_activeArtistFilterId is long artistId
                        ? _localCatalogProvider.GetAlbumsByArtistAsync(artistId, _showAlbumArtworkView)
                        : _localCatalogProvider.GetAlbumsAsync(_showAlbumArtworkView))
                    .GetAwaiter()
                    .GetResult()
                    .Where(a => !_albumFavoritesOnly || a.IsFavorite)
                    .Select(album => ToCatalogAlbumContentRow(album))
                    .ToList(),

                _ => (_activeAlbumFilterId is long albumId
                        ? _localCatalogProvider.GetTracksByAlbumAsync(albumId)
                    : _localCatalogProvider.GetTracksAsync())  // "Tracks" und Fallback
                    .GetAwaiter()
                    .GetResult()
                    .Select(track => ToCatalogTrackContentRow(track))
                    .ToList()
            };
        }
        catch { return []; }
    }

    private List<ContentRow> GetFilteredTrackRows()
    {
        using var db = AudioDatabase.OpenDefault();
        if (!HasActiveFilters)
        {
            return db.GetTrackList()
                .Select(ToTrackContentRow)
                .ToList();
        }

        var facets = db.GetTrackFacets();
        var ids = facets
            .Where(f => MatchesTrackFilters(f))
            .Select(f => f.Id)
            .ToList();
        return db.GetTrackListFiltered(ids)
            .Select(ToTrackContentRow)
            .ToList();
    }

    private sealed class StringNotEmptyConverter : IValueConverter
    {
        public static readonly StringNotEmptyConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string text && !string.IsNullOrWhiteSpace(text);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private async Task BindLocalRowsAndStartRemoteAppendAsync(string tag)
    {
        var diagnosticStopwatch = Stopwatch.StartNew();
        LogUiDiagnostics($"BindLocalRowsAndStartRemoteAppendAsync start tag={tag}");
        CancelAndDispose(ref _unifiedLibraryAppendCts);
        _unifiedLibraryAppendCts = new CancellationTokenSource();
        var cancellationToken = _unifiedLibraryAppendCts.Token;
        var version = ++_unifiedLibraryLoadVersion;
        var rows = tag == "Tracks"
            ? await Task.Run(GetFilteredTrackRows)
            : await Task.Run(() => QueryRows(tag));
        LogUiDiagnostics(
            $"BindLocalRowsAndStartRemoteAppendAsync local rows loaded tag={tag} count={rows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
        if (cancellationToken.IsCancellationRequested || version != _unifiedLibraryLoadVersion)
        {
            LogUiDiagnostics($"BindLocalRowsAndStartRemoteAppendAsync canceled before bind tag={tag} version={version}");
            return;
        }

        if ((_settings.OrynivoServers?.Count ?? 0) > 0)
        {
            var remoteRows = await LoadRemoteUnifiedRowsAsync(tag, version, cancellationToken, diagnosticStopwatch);
            if (remoteRows.Count > 0)
            {
                rows.AddRange(remoteRows);
                LogUiDiagnostics(
                    $"BindLocalRowsAndStartRemoteAppendAsync remote rows merged before bind tag={tag} remote={remoteRows.Count} total={rows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
            }
        }

        if (cancellationToken.IsCancellationRequested || version != _unifiedLibraryLoadVersion)
        {
            LogUiDiagnostics($"BindLocalRowsAndStartRemoteAppendAsync canceled before combined bind tag={tag} version={version}");
            return;
        }

        var sortedRows = SortUnifiedRows(rows);
        if (tag == "Artists")
            sortedRows = MergeUnifiedArtistRows(sortedRows);
        LogUiDiagnostics(
            $"BindLocalRowsAndStartRemoteAppendAsync combined rows sorted tag={tag} count={sortedRows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
        ApplyColumns(tag);
        ContentDataGrid.ItemsSource = sortedRows;
        BindUnifiedArtworkRowsIfVisible(tag, sortedRows);
        UpdateAlphabetIndex(sortedRows, true);
        UpdateUnifiedContentCount(tag, sortedRows.Count);
        LogUiDiagnostics(
            $"BindLocalRowsAndStartRemoteAppendAsync combined bind completed tag={tag} count={sortedRows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
    }

    private async Task<List<ContentRow>> LoadRemoteUnifiedRowsAsync(
        string tag,
        int version,
        CancellationToken cancellationToken,
        Stopwatch diagnosticStopwatch)
    {
        var rows = new List<ContentRow>();
        LogUiDiagnostics(
            $"LoadRemoteUnifiedRowsAsync start tag={tag} version={version} servers={_settings.OrynivoServers?.Count ?? 0}");
        foreach (var server in _settings.OrynivoServers ?? [])
        {
            if (cancellationToken.IsCancellationRequested || version != _unifiedLibraryLoadVersion)
            {
                LogUiDiagnostics(
                    $"LoadRemoteUnifiedRowsAsync canceled before server tag={tag} version={version} currentVersion={_unifiedLibraryLoadVersion}");
                return rows;
            }

            try
            {
                LogUiDiagnostics(
                    $"LoadRemoteUnifiedRowsAsync server start tag={tag} server={server.Name} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                var provider = CreateOrynivoCatalogProvider(server);
                List<ContentRow> serverRows = tag switch
                {
                    "Artists" => (await LoadAllOrynivoArtistsAsync(server, provider, linkedCts.Token))
                        .Where(artist => !_artistFavoritesOnly || artist.IsFavorite)
                        .Select(artist => ToCatalogArtistContentRow(artist, server))
                        .ToList(),
                    "Albums" => (await LoadAllOrynivoAlbumsAsync(server, provider, linkedCts.Token))
                        .Where(album => !_albumFavoritesOnly || album.IsFavorite)
                        .Select(album => ToCatalogAlbumContentRow(album, server))
                        .ToList(),
                    "Tracks" => (await LoadRemoteTracksForUnifiedViewAsync(server, provider, linkedCts.Token))
                        .Select(track => ToCatalogTrackContentRow(track, server))
                        .ToList(),
                    _ => []
                };
                LogUiDiagnostics(
                    $"LoadRemoteUnifiedRowsAsync server rows loaded tag={tag} server={server.Name} count={serverRows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");

                if (serverRows.Count == 0 || cancellationToken.IsCancellationRequested || version != _unifiedLibraryLoadVersion)
                    continue;

                rows.AddRange(serverRows);
                LogUiDiagnostics(
                    $"LoadRemoteUnifiedRowsAsync server rows merged tag={tag} server={server.Name} remoteTotal={rows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
            }
            catch (OperationCanceledException)
            {
                LogUiDiagnostics(
                    $"LoadRemoteUnifiedRowsAsync canceled tag={tag} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
                return rows;
            }
            catch (Exception ex)
            {
                LogUiDiagnostics(
                    $"LoadRemoteUnifiedRowsAsync server failed tag={tag} server={server.Name} error={ex.GetType().Name}: {ex.Message}");
                // An unavailable server must not prevent local browsing.
            }
        }
        LogUiDiagnostics(
            $"LoadRemoteUnifiedRowsAsync finish tag={tag} rows={rows.Count} elapsed={diagnosticStopwatch.ElapsedMilliseconds}ms");
        return rows;
    }

    private async Task<IReadOnlyList<LibraryCatalogTrack>> LoadRemoteTracksForUnifiedViewAsync(
        OrynivoServerSettings server,
        ILibraryCatalogProvider provider,
        CancellationToken cancellationToken)
    {
        if (HasActiveFilters)
        {
            var facets = await _orynivoClient.GetTrackFacetsAsync(server, cancellationToken);
            var ids = facets
                .Select(facet => facet with
                {
                    IsFavorite = IsOrynivoFavorite(server, "Track", facet.Id),
                    SourceKey = GetServerSourceKey(server.Id)
                })
                .Where(facet => MatchesTrackFilters(facet))
                .Select(facet => facet.Id)
                .ToList();
            return ids.Count == 0
                ? []
                : await provider.GetTracksByIdsAsync(ids, cancellationToken);
        }

        return await ResolveOrynivoTrackRowsAsync(server, provider, cancellationToken);
    }

    private void BindUnifiedRows(string tag, List<ContentRow> rows)
    {
        rows = SortUnifiedRows(rows);
        ApplyColumns(tag);
        ContentDataGrid.ItemsSource = rows;
        BindUnifiedArtworkRowsIfVisible(tag, rows);
        UpdateAlphabetIndex(rows, true);
        UpdateUnifiedContentCount(tag, rows.Count);
    }

    private void BindUnifiedArtworkRowsIfVisible(string tag, IReadOnlyList<ContentRow> rows)
    {
        if (tag == "Albums" && _showAlbumArtworkView)
            BindArtworkRows(tag, rows);
        else if (tag == "Artists" && _showArtistArtworkView)
            BindArtworkRows(tag, rows);
    }

    private void UpdateUnifiedContentCount(string tag, int count)
    {
        ContentCountTextBlock.Text = tag == "Tracks"
            ? LocalizationManager.FormatTrackCount(count)
            : LocalizationManager.FormatEntryCount(count);
    }

    private static List<ContentRow> SortUnifiedRows(IEnumerable<ContentRow> rows) =>
        rows
            .OrderBy(row => row.AlphabetIndexText ?? row.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.SourceName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static List<ContentRow> MergeUnifiedArtistRows(IEnumerable<ContentRow> rows) =>
        rows
            .GroupBy(row => ArtistNameNormalizer.CreateComparisonKey(row.Title), StringComparer.Ordinal)
            .Select(group =>
            {
                var candidates = group.ToList();
                var row = candidates.FirstOrDefault(candidate => candidate.EntityType == "Artist")
                          ?? candidates[0];
                if (candidates.Count > 1)
                {
                    row.EntityType = "UnifiedArtist";
                    row.IsFavorite = candidates.Any(candidate => candidate.IsFavorite);
                }
                return row;
            })
            .OrderBy(row => row.AlphabetIndexText ?? row.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static ContentRow ToTrackContentRow(TrackListInfo t) => new()
    {
        Title = t.Title?.Trim() ?? t.FileName.Trim(),
        AlphabetIndexText = t.SortTitle?.Trim() ?? t.Title?.Trim() ?? t.FileName.Trim(),
        Id = t.Id,
        Artist = t.Artist,
        Album = t.Album,
        AlbumArtist = t.AlbumArtist,
        Year = t.Year?.ToString(CultureInfo.CurrentCulture),
        TrackNumber = FormatPartNumber(t.TrackNumber, t.TrackTotal),
        DiscNumber = FormatPartNumber(t.DiscNumber, t.DiscTotal),
        Duration = FormatSeconds(t.Duration),
        Genre = t.Genre,
        Format = t.Format?.ToUpperInvariant(),
        Bitrate = t.Bitrate is > 0 ? $"{t.Bitrate:N0} kbps" : null,
        SampleRate = t.SampleRate is > 0 ? $"{t.SampleRate:N0} Hz" : null,
        BitDepth = t.BitDepth is > 0 ? $"{t.BitDepth:N0} Bit" : null,
        Channels = t.Channels?.ToString(CultureInfo.CurrentCulture),
        Composer = t.Composer,
        Bpm = t.Bpm?.ToString(CultureInfo.CurrentCulture),
        FileName = t.FileName,
        FileSize = FormatFileSize(t.FileSize),
        AddedAt = DateTimeOffset.FromUnixTimeSeconds(t.AddedAt)
            .ToLocalTime()
            .ToString("d", CultureInfo.CurrentCulture),
        ReplayGainTrack = FormatReplayGainDisplay(t.ReplayGainTrack),
        ReplayGainAlbum = FormatReplayGainDisplay(t.ReplayGainAlbum),
        FilePath = t.Path,
        SourcePath = t.Path,
        IsFavorite = t.IsFavorite
    };

    private ContentRow ToCatalogTrackContentRow(LibraryCatalogTrack track, OrynivoServerSettings? server = null)
    {
        server ??= track.Source == LibraryCatalogSource.OrynivoServer ? _activeOrynivoServer : null;
        var row = new ContentRow
        {
            Title = track.Title?.Trim() ?? track.FileName.Trim(),
            AlphabetIndexText = track.SortTitle?.Trim() ?? track.Title?.Trim() ?? track.FileName.Trim(),
            Id = track.Id,
            Artist = track.Artist,
            Album = track.Album,
            AlbumArtist = track.AlbumArtist,
            Year = track.Year?.ToString(CultureInfo.CurrentCulture),
            TrackNumber = FormatPartNumber(track.TrackNumber, track.TrackTotal),
            DiscNumber = FormatPartNumber(track.DiscNumber, track.DiscTotal),
            Duration = FormatSeconds(track.Duration),
            Genre = track.Genre,
            Format = track.Format?.ToUpperInvariant(),
            Bitrate = track.Bitrate is > 0 ? $"{track.Bitrate:N0} kbps" : null,
            SampleRate = track.SampleRate is > 0 ? $"{track.SampleRate:N0} Hz" : null,
            SampleRateHz = track.SampleRate,
            BitDepth = track.BitDepth is > 0 ? $"{track.BitDepth:N0} Bit" : null,
            Channels = track.Channels?.ToString(CultureInfo.CurrentCulture),
            ChannelCount = track.Channels,
            Composer = track.Composer,
            Bpm = track.Bpm?.ToString(CultureInfo.CurrentCulture),
            FileName = track.FileName,
            FileSize = FormatFileSize(track.FileSize),
            AddedAt = track.AddedAt.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(track.AddedAt.Value)
                    .ToLocalTime()
                    .ToString("d", CultureInfo.CurrentCulture)
                : null,
            ReplayGainTrack = FormatReplayGainDisplay(track.ReplayGainTrack),
            ReplayGainAlbum = FormatReplayGainDisplay(track.ReplayGainAlbum),
            FilePath = track.PlaybackPath,
            SourcePath = track.SourcePath,
            IsFavorite = track.IsFavorite,
            ArtistId = track.ArtistId,
            AlbumId = track.AlbumId,
            EntityType = track.Source == LibraryCatalogSource.OrynivoServer ? "OrynivoTrack" : "Track",
            ExternalId = track.Source == LibraryCatalogSource.OrynivoServer
                ? track.Id.ToString(CultureInfo.InvariantCulture)
                : null,
            KnownDuration = track.KnownDuration
        };
        if (track.Source == LibraryCatalogSource.OrynivoServer && server is not null)
        {
            row.OrynivoServer = server;
            _orynivoTracksByUrl[track.PlaybackPath] = row;
        }
        return row;
    }

    private static ContentRow ToCatalogAlbumContentRow(LibraryCatalogAlbum album, OrynivoServerSettings? server = null) => new()
    {
        Id = album.Id,
        AlbumId = album.Id,
        ArtistId = album.ArtistId,
        Title = string.IsNullOrEmpty(album.Title) ? LocalizationManager.Current.Unknown : album.Title,
        AlphabetIndexText = string.IsNullOrEmpty(album.Title) ? LocalizationManager.Current.Unknown : album.Title,
        Artist = string.IsNullOrEmpty(album.DisplayArtist) ? null : album.DisplayArtist,
        Year = album.Year?.ToString(CultureInfo.CurrentCulture),
        ArtworkPath = album.ArtworkPath,
        ThumbnailPath = album.ThumbnailPath,
        IsFavorite = album.IsFavorite,
        EntityType = album.Source == LibraryCatalogSource.OrynivoServer ? "OrynivoAlbum" : "Album",
        ExternalId = album.Source == LibraryCatalogSource.OrynivoServer
            ? album.Id.ToString(CultureInfo.InvariantCulture)
            : null,
        OrynivoServer = album.Source == LibraryCatalogSource.OrynivoServer ? server : null
    };

    private static ContentRow ToCatalogArtistContentRow(LibraryCatalogArtist artist, OrynivoServerSettings? server = null) => new()
    {
        Id = artist.Id,
        ArtistId = artist.Id,
        Title = string.IsNullOrEmpty(artist.Name) ? LocalizationManager.Current.Unknown : artist.Name,
        AlphabetIndexText = string.IsNullOrEmpty(artist.Name) ? LocalizationManager.Current.Unknown : artist.Name,
        IsFavorite = artist.IsFavorite,
        ArtworkPath = artist.ArtworkPath,
        ThumbnailPath = artist.ThumbnailPath,
        Biography = artist.Biography,
        SourceUrl = artist.SourceUrl,
        ProfileLanguage = artist.ProfileLanguage,
        ProfileFetchedAt = artist.ProfileFetchedAt,
        ImageIsManual = artist.ImageIsManual,
        EntityType = artist.Source == LibraryCatalogSource.OrynivoServer ? "OrynivoArtist" : "Artist",
        ExternalId = artist.Source == LibraryCatalogSource.OrynivoServer
            ? artist.Id.ToString(CultureInfo.InvariantCulture)
            : null,
        OrynivoServer = artist.Source == LibraryCatalogSource.OrynivoServer ? server : null,
        FilePath = ""
    };

    private static TrackListInfo ToTrackListInfo(TrackRecord track) => new(
        track.Path, track.FileName, track.Title, track.Artist, track.Album, track.AlbumArtist,
        track.Genre, track.Format, track.Bitrate, track.Duration, track.SortTitle, track.Id,
        false, track.Year, track.TrackNumber, track.TrackTotal, track.DiscNumber,
        track.DiscTotal, track.SampleRate, track.BitDepth, track.Channels, track.Composer,
        track.Bpm, track.FileSize, track.AddedAt, track.ReplayGainTrack, track.ReplayGainAlbum);

    private static string? FormatPartNumber(int? number, int? total) =>
        number is null ? null : total is > 0 ? $"{number}/{total}" : number.Value.ToString(CultureInfo.CurrentCulture);

    private static string? FormatReplayGainDisplay(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{value} dB";

    private static string? FormatFileSize(long? bytes)
    {
        if (bytes is null || bytes < 0)
            return null;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private async void TrackFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshTrackFilterPopupAsync();
        TrackFilterPopup.IsOpen = !TrackFilterPopup.IsOpen;
    }

    private async Task RefreshTrackFilterPopupAsync()
    {
        var facets = await BuildTrackFilterFacetsAsync();

        TrackFilterPanel.Children.Clear();
        AddTrackFilterCheckBox(
            TrackFilterPanel,
            LocalizationManager.Current.Favorites,
            facets.Count(f => f.IsFavorite && MatchesTrackFilters(f, ignoredDimension: "favorite")),
            _trackFavoritesOnly,
            isChecked => _trackFavoritesOnly = isChecked);

        var genreSection = AddTrackFilterSection(LocalizationManager.Current.Genre);
        foreach (var option in BuildGenreFacetCounts(facets))
            AddTrackFilterCheckBox(
                genreSection,
                option.Key,
                option.Value,
                _selectedTrackGenres.Contains(option.Key),
                isChecked => ToggleSelection(_selectedTrackGenres, option.Key, isChecked));

        var audioTypeSection = AddTrackFilterSection(LocalizationManager.Current.AudioTypes);
        foreach (var option in BuildStringFacetCounts(facets, f => f.Format, "format"))
            AddTrackFilterCheckBox(
                audioTypeSection,
                option.Key.ToUpperInvariant(),
                option.Value,
                _selectedTrackFormats.Contains(option.Key),
                isChecked => ToggleSelection(_selectedTrackFormats, option.Key, isChecked));

        var bitrateSection = AddTrackFilterSection(LocalizationManager.Current.Bitrate);
        foreach (var option in BuildBitrateFacetCounts(facets))
            AddTrackFilterCheckBox(
                bitrateSection,
                $"{option.Key:N0} kbps",
                option.Value,
                _selectedTrackBitrates.Contains(option.Key),
                isChecked => ToggleSelection(_selectedTrackBitrates, option.Key, isChecked));

        var sourceSection = AddTrackFilterSection(LocalizationManager.Current.SourceColumn);
        foreach (var option in BuildSourceFacetCounts(facets))
            AddTrackFilterCheckBox(
                sourceSection,
                GetSourceDisplayName(option.Key),
                option.Value,
                _selectedTrackSources.Contains(option.Key),
                isChecked => ToggleSelection(_selectedTrackSources, option.Key, isChecked));
    }

    private async Task<List<TrackFacetInfo>> BuildTrackFilterFacetsAsync()
    {
        if (_orynivoTrackFacets is not null)
            return _orynivoTrackFacets;

        var facets = await Task.Run(() =>
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTrackFacets();
            }
            catch
            {
                return [];
            }
        });

        if (_currentTopLevelTag != "Tracks")
            return facets;

        foreach (var server in _settings.OrynivoServers ?? [])
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var serverFacets = await _orynivoClient.GetTrackFacetsAsync(server, cts.Token);
                facets.AddRange(serverFacets.Select(facet => facet with
                {
                    IsFavorite = IsOrynivoFavorite(server, "Track", facet.Id),
                    SourceKey = GetServerSourceKey(server.Id)
                }));
            }
            catch
            {
                // Unavailable servers simply do not contribute filter counts.
            }
        }

        return facets;
    }

    private StackPanel AddTrackFilterSection(string title)
    {
        var content = new StackPanel();
        var expander = new Expander
        {
            Header = title,
            Content = content,
            IsExpanded = _expandedTrackFilterSections.Contains(title),
            Margin = TrackFilterPanel.Children.Count == 0 ? new Thickness(0, 0, 0, 0) : new Thickness(0, 8, 0, 0),
            Theme = FindResource<ControlTheme>("TrackFilterExpanderTheme")
        };
        expander.Expanded += (_, _) => _expandedTrackFilterSections.Add(title);
        expander.Collapsed += (_, _) => _expandedTrackFilterSections.Remove(title);
        TrackFilterPanel.Children.Add(expander);
        return content;
    }

    private void AddTrackFilterCheckBox(StackPanel section, string label, int count, bool isChecked, Action<bool> update)
    {
        var content = new Grid();
        content.Margin = new Thickness(8, 0, 0, 0);
        content.Width = 530;
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        content.Children.Add(new TextBlock
        {
            Text = label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush")
        });
        var countText = new TextBlock
        {
            Text = count.ToString("N0"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FindResource<IBrush>("AppMutedTextBrush")
        };
        Grid.SetColumn(countText, 1);
        content.Children.Add(countText);

        var checkBox = new CheckBox
        {
            Content = content,
            IsChecked = isChecked,
            Margin = new Thickness(0, 6, 0, 6),
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Theme = FindResource<ControlTheme>("HeaderCheckBoxTheme")
        };
        checkBox.IsCheckedChanged += async (_, _) => await OnTrackFilterChangedAsync(update, checkBox.IsChecked == true);
        section.Children.Add(checkBox);
    }

    private async Task OnTrackFilterChangedAsync(Action<bool> update, bool isChecked)
    {
        update(isChecked);
        if (_orynivoTrackFacets is not null && _activeOrynivoServer is not null)
        {
            await ApplyOrynivoTrackFiltersAsync();
        }
        else if (_currentTopLevelTag == "Tracks")
        {
            await BindLocalRowsAndStartRemoteAppendAsync("Tracks");
            UpdateSaveSmartPlaylistButtonState();
        }
        else
        {
            var rows = await Task.Run(GetFilteredTrackRows);
            ContentDataGrid.ItemsSource = rows;
            UpdateAlphabetIndex(rows, true);
            ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
            UpdateSaveSmartPlaylistButtonState();
        }
        await RefreshTrackFilterPopupAsync();
    }

    private static void ToggleSelection<T>(HashSet<T> values, T value, bool isChecked)
    {
        if (isChecked)
            values.Add(value);
        else
            values.Remove(value);
    }

    private bool MatchesTrackFilters(TrackFacetInfo facet, string? ignoredDimension = null)
    {
        if (ignoredDimension != "favorite" && _trackFavoritesOnly && !facet.IsFavorite)
            return false;
        if (ignoredDimension != "genre" && _selectedTrackGenres.Count > 0 &&
            !SplitGenres(facet.Genre).Any(_selectedTrackGenres.Contains))
            return false;
        if (ignoredDimension != "format" && _selectedTrackFormats.Count > 0 &&
            (string.IsNullOrWhiteSpace(facet.Format) || !_selectedTrackFormats.Contains(facet.Format)))
            return false;
        if (ignoredDimension != "bitrate" && _selectedTrackBitrates.Count > 0 &&
            (!facet.Bitrate.HasValue || !_selectedTrackBitrates.Contains(facet.Bitrate.Value)))
            return false;
        if (ignoredDimension != "source" && _selectedTrackSources.Count > 0 &&
            !_selectedTrackSources.Contains(facet.SourceKey))
            return false;
        return true;
    }

    private static bool MatchesCriteria(SmartPlaylistTrackInfo facet, SmartPlaylistCriteria criteria)
    {
        if (criteria.FavoritesOnly && !facet.IsFavorite)
            return false;
        if (criteria.Genres is { Count: > 0 })
        {
            var trackGenres = string.IsNullOrWhiteSpace(facet.Genre)
                ? Array.Empty<string>()
                : facet.Genre.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!trackGenres.Any(g => criteria.Genres.Contains(g, StringComparer.OrdinalIgnoreCase)))
                return false;
        }
        if (criteria.Formats is { Count: > 0 } &&
            (string.IsNullOrWhiteSpace(facet.Format) ||
             !criteria.Formats.Contains(facet.Format, StringComparer.OrdinalIgnoreCase)))
            return false;
        if (criteria.Bitrates is { Count: > 0 } &&
            (!facet.Bitrate.HasValue || !criteria.Bitrates.Contains(facet.Bitrate.Value)))
            return false;
        if (criteria.SourceKeys is { Count: > 0 } &&
            !criteria.SourceKeys.Contains(facet.SourceKey, StringComparer.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(criteria.SearchText))
        {
            var needle = criteria.SearchText.Trim();
            var matchesText =
                (facet.SortTitle?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (facet.Artist?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (facet.Album?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false);
            if (!matchesText)
                return false;
        }
        if (criteria.MinimumYear is int minimumYear &&
            (!facet.Year.HasValue || facet.Year.Value < minimumYear))
            return false;
        if (criteria.MaximumYear is int maximumYear &&
            (!facet.Year.HasValue || facet.Year.Value > maximumYear))
            return false;
        if (!string.IsNullOrWhiteSpace(criteria.ArtistContains) &&
            (string.IsNullOrWhiteSpace(facet.Artist) ||
             !facet.Artist.Contains(criteria.ArtistContains.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            return false;
        if (!string.IsNullOrWhiteSpace(criteria.AlbumContains) &&
            (string.IsNullOrWhiteSpace(facet.Album) ||
             !facet.Album.Contains(criteria.AlbumContains.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            return false;
        if (criteria.MinimumDurationSeconds is double minimumDuration &&
            (!facet.Duration.HasValue || facet.Duration.Value < minimumDuration))
            return false;
        if (criteria.MaximumDurationSeconds is double maximumDuration &&
            (!facet.Duration.HasValue || facet.Duration.Value > maximumDuration))
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (criteria.AddedWithinDays is > 0 &&
            facet.AddedAt < now - (long)criteria.AddedWithinDays.Value * 24 * 60 * 60)
            return false;
        if (criteria.PlayedWithinDays is > 0 &&
            (!facet.LastPlayedAt.HasValue ||
             facet.LastPlayedAt.Value < now - (long)criteria.PlayedWithinDays.Value * 24 * 60 * 60))
            return false;
        if (criteria.NeverPlayed && facet.PlayCount > 0)
            return false;
        if (criteria.MinimumPlayCount is int minimumPlayCount && facet.PlayCount < minimumPlayCount)
            return false;
        if (criteria.MaximumPlayCount is int maximumPlayCount && facet.PlayCount > maximumPlayCount)
            return false;
        return true;
    }

    /// <summary>
    /// Builds the unified smart-playlist candidate set — the local library plus every configured
    /// remote Orynivo Server — that a smart playlist is resolved against. Remote tracks get
    /// negative pseudo-IDs mapped through <paramref name="remoteTracks"/>. Blocks on server
    /// calls, so it must run off the UI thread.
    /// </summary>
    /// <param name="remoteTracks">Receives the pseudo-ID → (server, track) map for remote candidates.</param>
    /// <returns>The merged candidate list.</returns>
    private List<SmartPlaylistTrackInfo> BuildUnifiedSmartPlaylistCandidates(
        out Dictionary<long, (OrynivoServerSettings Server, LibraryCatalogTrack Track)> remoteTracks)
    {
        var candidates = new List<SmartPlaylistTrackInfo>();
        remoteTracks = new Dictionary<long, (OrynivoServerSettings Server, LibraryCatalogTrack Track)>();
        long nextRemoteId = -1;

        try
        {
            using var db = AudioDatabase.OpenDefault();
            candidates.AddRange(db.GetSmartPlaylistTracks());
        }
        catch
        {
        }

        foreach (var server in _settings.OrynivoServers ?? [])
        {
            try
            {
                using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var provider = CreateOrynivoCatalogProvider(server);
                var tracks = LoadAllOrynivoTracksAsync(server, provider, serverCts.Token)
                    .GetAwaiter()
                    .GetResult();
                foreach (var track in tracks)
                {
                    var pseudoId = nextRemoteId--;
                    remoteTracks[pseudoId] = (server, track);
                    candidates.Add(ToSmartPlaylistCandidate(pseudoId, track, GetServerSourceKey(server.Id)));
                }
            }
            catch
            {
                // An unavailable server must not prevent local smart playlist results.
            }
        }

        return candidates;
    }

    /// <summary>
    /// Counts how many tracks match the criteria across the unified candidate set (local library
    /// plus configured remote servers), matching what a smart playlist actually contains when
    /// opened. Runs off the UI thread.
    /// </summary>
    /// <param name="criteria">The criteria to evaluate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of matching tracks.</returns>
    private Task<int?> ResolveUnifiedSmartPlaylistCountAsync(
        SmartPlaylistCriteria criteria,
        CancellationToken cancellationToken)
        => Task.Run<int?>(() =>
        {
            var candidates = BuildUnifiedSmartPlaylistCandidates(out _);
            return criteria.Resolve(candidates).Count;
        }, cancellationToken);

    private List<ContentRow> ResolveUnifiedSmartPlaylistRows(SmartPlaylistCriteria criteria)
    {
        var candidates = BuildUnifiedSmartPlaylistCandidates(out var remoteTracks);
        var resolved = criteria.Resolve(candidates);
        var localIds = resolved
            .Where(candidate => candidate.Id > 0)
            .Select(candidate => candidate.Id)
            .ToList();
        var localRows = new Dictionary<long, ContentRow>();
        if (localIds.Count > 0)
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                foreach (var track in db.GetTrackListByIds(localIds))
                    localRows[track.Id] = ToTrackContentRow(track);
            }
            catch
            {
            }
        }

        var rows = new List<ContentRow>();
        foreach (var candidate in resolved)
        {
            ContentRow? row = null;
            if (candidate.Id > 0)
            {
                localRows.TryGetValue(candidate.Id, out row);
            }
            else if (remoteTracks.TryGetValue(candidate.Id, out var remote))
            {
                row = ToCatalogTrackContentRow(remote.Track, remote.Server);
            }

            if (row is null)
                continue;
            row.Nr = (rows.Count + 1).ToString(CultureInfo.CurrentCulture);
            rows.Add(row);
        }

        return rows;
    }

    private static SmartPlaylistTrackInfo ToSmartPlaylistCandidate(long id, LibraryCatalogTrack track, string sourceKey) =>
        new(
            id,
            track.IsFavorite,
            track.Genre,
            track.Format,
            track.Bitrate,
            track.Year,
            track.Artist,
            track.Album,
            track.Duration,
            track.AddedAt ?? 0,
            PlayCount: 0,
            LastPlayedAt: null,
            track.SortTitle?.Trim() ?? track.Title?.Trim() ?? track.FileName.Trim(),
            sourceKey);

    private static IEnumerable<SmartPlaylistTrackInfo> OrderSmartPlaylistTracks(
        IEnumerable<SmartPlaylistTrackInfo> tracks,
        SmartPlaylistSortOrder sortOrder) =>
        sortOrder switch
        {
            SmartPlaylistSortOrder.Random => tracks.OrderBy(_ => Random.Shared.Next()),
            SmartPlaylistSortOrder.LastPlayedNewest => tracks
                .OrderByDescending(track => track.LastPlayedAt.HasValue)
                .ThenByDescending(track => track.LastPlayedAt),
            SmartPlaylistSortOrder.LeastRecentlyPlayed => tracks
                .OrderBy(track => track.LastPlayedAt.HasValue)
                .ThenBy(track => track.LastPlayedAt),
            _ => tracks.OrderBy(
                track => track.SortTitle,
                StringComparer.CurrentCultureIgnoreCase)
        };

    private bool HasActiveFilters =>
        _trackFavoritesOnly || _selectedTrackGenres.Count > 0 ||
        _selectedTrackFormats.Count > 0 || _selectedTrackBitrates.Count > 0 ||
        _selectedTrackSources.Count > 0;

    private void UpdateSaveSmartPlaylistButtonState()
    {
        // A smart playlist can also capture just the search text, so the save action stays
        // available when the Tracks search box holds a query even without facet filters.
        SaveSmartPlaylistButton.IsEnabled =
            HasActiveFilters || !string.IsNullOrWhiteSpace(SearchTextBox.Text);
    }

    /// <summary>
    /// Builds smart-playlist criteria from the currently active Tracks facet filters.
    /// Shared by the local and remote Orynivo Server smart-playlist save paths.
    /// </summary>
    /// <returns>The criteria mirroring the active favourite/genre/format/bitrate facets.</returns>
    private SmartPlaylistCriteria BuildCurrentTrackFilterCriteria() => new()
    {
        FavoritesOnly = _trackFavoritesOnly,
        SearchText = string.IsNullOrWhiteSpace(SearchTextBox.Text) ? null : SearchTextBox.Text.Trim(),
        Genres = [.. _selectedTrackGenres.OrderBy(g => g)],
        Formats = [.. _selectedTrackFormats.OrderBy(f => f)],
        Bitrates = [.. _selectedTrackBitrates.OrderBy(b => b)],
        SourceKeys = [.. _selectedTrackSources.OrderBy(s => s)]
    };

    private async void SaveSmartPlaylistButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new NewPlaylistDialog();
        if (await dialog.ShowDialog<bool>(this) == false || string.IsNullOrWhiteSpace(dialog.PlaylistName))
            return;

        var json = JsonSerializer.Serialize(BuildCurrentTrackFilterCriteria());
        var name = dialog.PlaylistName.Trim();

        // Remote Tracks view: persist the smart playlist on the active server.
        if (_orynivoTrackFacets is not null && _activeOrynivoServer is { } server)
        {
            var created = await _orynivoClient.CreateSmartPlaylistAsync(server, name, json);
            if (created is null)
            {
                StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
                return;
            }

            LoadOrynivoServerNavigation();
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.SmartPlaylistSaved, name);
            return;
        }

        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.CreateSmartPlaylist(name, json);
        }
        catch { return; }

        LoadNavPlaylists();
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.SmartPlaylistSaved, name);
    }

    private IEnumerable<KeyValuePair<string, int>> BuildGenreFacetCounts(IEnumerable<TrackFacetInfo> facets)
    {
        var counts = facets
            .Where(f => MatchesTrackFilters(f, "genre"))
            .SelectMany(f => SplitGenres(f.Genre))
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var selected in _selectedTrackGenres)
            counts.TryAdd(selected, 0);
        return counts
            .Select(x => new KeyValuePair<string, int>(x.Key, x.Value))
            .OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase);
    }

    private IEnumerable<KeyValuePair<string, int>> BuildStringFacetCounts(
        IEnumerable<TrackFacetInfo> facets,
        Func<TrackFacetInfo, string?> selector,
        string ignoredDimension)
    {
        var counts = facets
            .Where(f => MatchesTrackFilters(f, ignoredDimension))
            .Select(selector)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var selected in _selectedTrackFormats)
            counts.TryAdd(selected, 0);
        return counts
            .Select(x => new KeyValuePair<string, int>(x.Key, x.Value))
            .OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase);
    }

    private IEnumerable<KeyValuePair<int, int>> BuildBitrateFacetCounts(IEnumerable<TrackFacetInfo> facets)
    {
        var counts = facets
            .Where(f => MatchesTrackFilters(f, "bitrate"))
            .Where(f => f.Bitrate.HasValue)
            .GroupBy(f => f.Bitrate!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var selected in _selectedTrackBitrates)
            counts.TryAdd(selected, 0);
        return counts
            .Select(x => new KeyValuePair<int, int>(x.Key, x.Value))
            .OrderBy(x => x.Key);
    }

    private IEnumerable<KeyValuePair<string, int>> BuildSourceFacetCounts(IEnumerable<TrackFacetInfo> facets)
    {
        var counts = facets
            .Where(f => MatchesTrackFilters(f, "source"))
            .GroupBy(f => f.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var selected in _selectedTrackSources)
            counts.TryAdd(selected, 0);
        return counts
            .Select(x => new KeyValuePair<string, int>(x.Key, x.Value))
            .OrderBy(x => GetSourceDisplayName(x.Key), StringComparer.CurrentCultureIgnoreCase);
    }

    private string GetSourceDisplayName(string sourceKey)
    {
        if (string.Equals(sourceKey, LocalSourceKey, StringComparison.OrdinalIgnoreCase))
            return LocalizationManager.Current.LocalSource;
        const string serverPrefix = "server:";
        if (sourceKey.StartsWith(serverPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var serverId = sourceKey[serverPrefix.Length..];
            var server = _settings.OrynivoServers?.FirstOrDefault(item =>
                string.Equals(item.Id, serverId, StringComparison.OrdinalIgnoreCase));
            if (server is not null)
                return server.Name;
        }

        return sourceKey;
    }

    private static IEnumerable<string> SplitGenres(string? genre)
        => string.IsNullOrWhiteSpace(genre)
            ? []
            : genre.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSaveSmartPlaylistButtonState();
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void EntityFavoritesOnlyCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingEntityFavoritesFilter)
            return;

        var tag = GetActiveEntityFavoritesView();
        if (tag is null)
            return;

        var isChecked = EntityFavoritesOnlyCheckBox.IsChecked == true;
        if (tag.StartsWith("Orynivo:", StringComparison.Ordinal))
        {
            _trackFavoritesOnly = isChecked;
            await LoadOrynivoViewAsync();
            return;
        }

        if (tag == "Artists")
            _artistFavoritesOnly = isChecked;
        else
            _albumFavoritesOnly = isChecked;

        await ReloadEntityRowsAsync(tag);
    }

    private string? GetActiveEntityFavoritesView()
    {
        if (_currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true &&
            _activeOrynivoView is "Artists" or "Albums" or "Tracks")
        {
            return $"Orynivo:{_activeOrynivoView}";
        }

        if (_activeAlbumFilterId is null &&
            _activeArtistFilterId is long &&
            AlbumViewModeBorder.IsVisible)
        {
            return "Albums";
        }

        return _currentTopLevelTag is "Artists" or "Albums"
            ? _currentTopLevelTag
            : null;
    }

    private void UpdateEntityFavoritesFilterToggle(string? tag)
    {
        var visible = tag is "Artists" or "Albums" ||
                      (tag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true &&
                       _activeOrynivoView is "Artists" or "Albums" or "Tracks");
        _updatingEntityFavoritesFilter = true;
        try
        {
            EntityFavoritesOnlyCheckBox.IsVisible = visible;
            EntityFavoritesOnlyCheckBox.IsChecked = tag switch
            {
                "Artists" => _artistFavoritesOnly,
                "Albums" => _albumFavoritesOnly,
                _ when tag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true => _trackFavoritesOnly,
                _ => false
            };
        }
        finally
        {
            _updatingEntityFavoritesFilter = false;
        }
    }

    private async Task ReloadEntityRowsAsync(string tag)
    {
        var artworkMode = tag == "Albums"
            ? _showAlbumArtworkView
            : _showArtistArtworkView;

        ContentDataGrid.IsVisible = !artworkMode;
        AlbumArtworkListBox.IsVisible = tag == "Albums" && artworkMode;
        ArtistArtworkListBox.IsVisible = tag == "Artists" && artworkMode;

        await BindLocalRowsAndStartRemoteAppendAsync(tag);
    }

    private async Task ShowSearchResultsAsync(string query)
    {
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        UpdateEntityFavoritesFilterToggle(null);
        if (string.IsNullOrWhiteSpace(query))
        {
            if (NavListBox.SelectedItem is ListBoxItem { Tag: string tag })
                await ShowTopLevelViewAsync(tag);
            return;
        }

        if (!SearchResultsScrollViewer.IsVisible)
            PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        UpdateAlphabetIndex(null, false);
        UpdateLibraryIntroCard(null);
        ContentTitleTextBlock.Text = LocalizationManager.Current.Search;
        AlbumViewModeBorder.IsVisible = false;
        TrackFilterButton.IsVisible = false;
        ContentDataGrid.IsVisible = false;
        FolderTreeView.IsVisible = false;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = true;
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        HideAlbumDetailHeader();

        // Honour the active Tracks facet filters during search: the source facet gates which
        // sources contribute results at all, while favourite/genre/format/bitrate additionally
        // filter the track section. An empty source facet means "all sources".
        var includeLocal = _selectedTrackSources.Count == 0 ||
                           _selectedTrackSources.Contains(LocalSourceKey);
        var applyTrackFacets = HasActiveFilters;
        var result = await Task.Run(() =>
        {
            if (!includeLocal)
                return (Tracks: new List<ContentRow>(), Albums: new List<ContentRow>(), Artists: new List<ContentRow>());

            var ids = TrackSearchIndex.SearchByCategory(query);
            using var db = AudioDatabase.OpenDefault();

            var trackIds = ids.Tracks.Ids.ToList();
            if (applyTrackFacets)
            {
                var allowed = db.GetTrackFacets()
                    .Where(facet => MatchesTrackFilters(facet))
                    .Select(facet => facet.Id)
                    .ToHashSet();
                trackIds = trackIds.Where(allowed.Contains).ToList();
            }

            var albumScores = BuildEntityScores(db.GetAlbumIdsByTrackIds(ids.Albums.Ids), ids.Albums.Scores);
            var artistScores = MergeSearchScores(
                BuildEntityScores(db.GetArtistIdsByTrackIds(ids.Artists.Ids), ids.Artists.Scores),
                BuildEntityScores(db.GetAlbumArtistIdsByTrackIds(ids.AlbumArtists.Ids), ids.AlbumArtists.Scores));
            return (
                Tracks: SortBySearchScore(
                    db.GetTrackListByIds(trackIds).Select(ToTrackContentRow),
                    ids.Tracks.Scores),
                Albums: SortBySearchScore(
                    db.GetAlbumsByTrackIds(ids.Albums.Ids).Select(ToAlbumContentRow),
                    albumScores),
                Artists: SortBySearchScore(
                    db.GetArtistsByIds(artistScores.Keys).Select(ToArtistContentRow),
                    artistScores));
        });
        await AddRemoteSearchResultsAsync(query, result.Tracks, result.Albums, result.Artists);
        result.Artists = MergeUnifiedArtistRows(result.Artists);

        ApplySearchColumns();
        SearchTracksDataGrid.ItemsSource = result.Tracks;
        SearchAlbumsDataGrid.ItemsSource = result.Albums;
        SearchArtistsDataGrid.ItemsSource = result.Artists;
        UpdateSearchEmptyState(SearchTracksEmptyTextBlock, result.Tracks.Count, LocalizationManager.Current.SearchTermNotFoundInTracks, query);
        UpdateSearchEmptyState(SearchAlbumsEmptyTextBlock, result.Albums.Count, LocalizationManager.Current.SearchTermNotFoundInAlbums, query);
        UpdateSearchEmptyState(SearchArtistsEmptyTextBlock, result.Artists.Count, LocalizationManager.Current.SearchTermNotFoundInArtists, query);
        ContentCountTextBlock.Text = string.Format(
            LocalizationManager.Current.SearchResultSummary,
            result.Tracks.Count,
            result.Albums.Count,
            result.Artists.Count);
    }

    private static void UpdateSearchEmptyState(TextBlock textBlock, int count, string format, string query)
    {
        textBlock.Text = string.Format(format, query);
        textBlock.IsVisible = count == 0;
    }

    /// <summary>Adds matching Orynivo Server rows to the shared local search result lists.</summary>
    /// <param name="query">Search query.</param>
    /// <param name="trackRows">Track rows to extend.</param>
    /// <param name="albumRows">Album rows to extend.</param>
    /// <param name="artistRows">Artist rows to extend.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddRemoteSearchResultsAsync(
        string query,
        List<ContentRow> trackRows,
        List<ContentRow> albumRows,
        List<ContentRow> artistRows)
    {
        foreach (var server in _settings.OrynivoServers ?? [])
        {
            // The source facet gates which servers may contribute search results.
            if (_selectedTrackSources.Count > 0 &&
                !_selectedTrackSources.Contains(GetServerSourceKey(server.Id)))
                continue;

            try
            {
                var provider = CreateOrynivoCatalogProvider(server);
                var (tracks, albums, artists) = await provider.SearchFullAsync(query.Trim(), 50);
                IEnumerable<LibraryCatalogTrack> matchingTracks = HasActiveFilters
                    ? tracks.Where(track => RemoteSearchTrackMatchesFilters(server, track))
                    : tracks;
                trackRows.AddRange(matchingTracks.Select(track => ToCatalogTrackContentRow(track, server)));
                albumRows.AddRange(albums.Select(album => ToCatalogAlbumContentRow(album, server)));
                artistRows.AddRange(artists.Select(artist => ToCatalogArtistContentRow(artist, server)));
            }
            catch
            {
                // An unavailable server must not hide local search results.
            }
        }
    }

    /// <summary>
    /// Evaluates a remote Orynivo Server search track against the active Tracks facet filters,
    /// applying the client-side favourite state for that server's tracks.
    /// </summary>
    /// <param name="server">The server the track belongs to.</param>
    /// <param name="track">The remote catalog track to test.</param>
    /// <returns><see langword="true"/> when the track satisfies the active facet filters.</returns>
    private bool RemoteSearchTrackMatchesFilters(OrynivoServerSettings server, LibraryCatalogTrack track)
    {
        var facet = new TrackFacetInfo(
            track.Id,
            IsOrynivoFavorite(server, "Track", track.Id),
            track.Genre,
            track.Format,
            track.Bitrate,
            GetServerSourceKey(server.Id));
        return MatchesTrackFilters(facet);
    }

    /// <summary>
    /// Shows the shared three-section (tracks/albums/artists) search result view for the
    /// active remote Orynivo Server, mirroring the local <see cref="ShowSearchResultsAsync"/>.
    /// </summary>
    /// <param name="query">The search query; an empty value restores the remote Tracks list.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ShowOrynivoSearchResultsAsync(string query)
    {
        if (_activeOrynivoServer is not { } server)
            return;

        if (string.IsNullOrWhiteSpace(query))
        {
            // Clearing the search box restores the full remote Tracks list.
            await LoadOrynivoViewAsync();
            return;
        }

        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        UpdateEntityFavoritesFilterToggle(null);

        if (!SearchResultsScrollViewer.IsVisible)
            PushCurrentNavigationState();
        UpdateAlphabetIndex(null, false);
        UpdateLibraryIntroCard(null);
        ContentTitleTextBlock.Text = LocalizationManager.Current.Search;
        AlbumViewModeBorder.IsVisible = false;
        TrackFilterButton.IsVisible = false;
        SaveSmartPlaylistButton.IsVisible = false;
        ContentDataGrid.IsVisible = false;
        FolderTreeView.IsVisible = false;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = true;
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        HideAlbumDetailHeader();

        var provider = CreateOrynivoCatalogProvider(server);
        var (tracks, albums, artists) = await provider.SearchFullAsync(query.Trim(), 50);

        // A newer query may have started while this request was in flight.
        if (!string.Equals((SearchTextBox.Text ?? string.Empty).Trim(), query.Trim(), StringComparison.Ordinal))
            return;

        IEnumerable<LibraryCatalogTrack> matchingTracks = HasActiveFilters
            ? tracks.Where(track => RemoteSearchTrackMatchesFilters(server, track))
            : tracks;
        var trackRows = matchingTracks.Select(track => ToCatalogTrackContentRow(track, server)).ToList();
        var albumRows = albums.Select(album => ToCatalogAlbumContentRow(album, server)).ToList();
        var artistRows = artists.Select(artist => ToCatalogArtistContentRow(artist, server)).ToList();

        ApplySearchColumns();
        SearchTracksDataGrid.ItemsSource = trackRows;
        SearchAlbumsDataGrid.ItemsSource = albumRows;
        SearchArtistsDataGrid.ItemsSource = artistRows;
        UpdateSearchEmptyState(SearchTracksEmptyTextBlock, trackRows.Count, LocalizationManager.Current.SearchTermNotFoundInTracks, query);
        UpdateSearchEmptyState(SearchAlbumsEmptyTextBlock, albumRows.Count, LocalizationManager.Current.SearchTermNotFoundInAlbums, query);
        UpdateSearchEmptyState(SearchArtistsEmptyTextBlock, artistRows.Count, LocalizationManager.Current.SearchTermNotFoundInArtists, query);
        ContentCountTextBlock.Text = string.Format(
            LocalizationManager.Current.SearchResultSummary,
            trackRows.Count,
            albumRows.Count,
            artistRows.Count);
    }

    private static Dictionary<long, float> BuildEntityScores(
        IReadOnlyDictionary<long, long> trackToEntityIds,
        IReadOnlyDictionary<long, float> trackScores)
    {
        var result = new Dictionary<long, float>();
        foreach (var (trackId, entityId) in trackToEntityIds)
        {
            if (!trackScores.TryGetValue(trackId, out var score))
                continue;
            if (!result.TryGetValue(entityId, out var existing) || score > existing)
                result[entityId] = score;
        }
        return result;
    }

    private static Dictionary<long, float> MergeSearchScores(params IReadOnlyDictionary<long, float>[] sources)
    {
        var result = new Dictionary<long, float>();
        foreach (var source in sources)
        {
            foreach (var (id, score) in source)
            {
                if (!result.TryGetValue(id, out var existing) || score > existing)
                    result[id] = score;
            }
        }
        return result;
    }

    private static List<ContentRow> SortBySearchScore(
        IEnumerable<ContentRow> rows,
        IReadOnlyDictionary<long, float> scores)
        => rows
            .OrderByDescending(row => row.Id is long id && scores.TryGetValue(id, out var score) ? score : 0)
            .ThenBy(row => row.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static ContentRow ToAlbumContentRow(AlbumInfo a) => new()
    {
        Id = a.Id,
        AlbumId = a.Id,
        Title = string.IsNullOrEmpty(a.Album) ? LocalizationManager.Current.Unknown : a.Album,
        Artist = string.IsNullOrEmpty(a.DisplayArtist) ? null : a.DisplayArtist,
        Year = a.Year?.ToString(),
        ArtworkPath = a.ArtworkPath,
        ThumbnailPath = a.ThumbnailPath,
        IsFavorite = a.IsFavorite,
        EntityType = "Album"
    };

    private static ContentRow ToArtistContentRow(ArtistInfo a) => new()
    {
        Id = a.Id,
        ArtistId = a.Id,
        Title = string.IsNullOrEmpty(a.Artist) ? LocalizationManager.Current.Unknown : a.Artist,
        IsFavorite = a.IsFavorite,
        ArtworkPath = a.ImagePath,
        ThumbnailPath = a.ImagePath,
        Biography = a.Biography,
        SourceUrl = a.SourceUrl,
        ProfileLanguage = a.ProfileLanguage,
        ProfileFetchedAt = a.ProfileFetchedAt,
        ImageIsManual = a.ImageIsManual,
        EntityType = "Artist"
    };

    private void ApplySearchColumns()
    {
        ConfigureSearchGrid(SearchTracksDataGrid,
            (LocalizationManager.Current.Title, nameof(ContentRow.Title), 240, "title", true),
            (LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 180, "artist", true),
            (LocalizationManager.Current.Album, nameof(ContentRow.Album), 220, "album", true),
            (LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 90, "duration", true),
            (LocalizationManager.Current.Format, nameof(ContentRow.Format), 80, "format", true),
            (LocalizationManager.Current.AlbumArtist, nameof(ContentRow.AlbumArtist), 180, "albumArtist", false),
            (LocalizationManager.Current.Year, nameof(ContentRow.Year), 80, "year", false),
            (LocalizationManager.Current.Genre, nameof(ContentRow.Genre), 150, "genre", false),
            (LocalizationManager.Current.Bitrate, nameof(ContentRow.Bitrate), 100, "bitrate", false),
            (LocalizationManager.Current.SampleRate, nameof(ContentRow.SampleRate), 110, "sampleRate", false),
            (LocalizationManager.Current.BitDepth, nameof(ContentRow.BitDepth), 90, "bitDepth", false),
            (LocalizationManager.Current.Composer, nameof(ContentRow.Composer), 180, "composer", false),
            (LocalizationManager.Current.FileName, nameof(ContentRow.FileName), 220, "fileName", false));
        ConfigureSearchGrid(SearchAlbumsDataGrid,
            (LocalizationManager.Current.Album, nameof(ContentRow.Title), 260, "album", true),
            (LocalizationManager.Current.AlbumArtist, nameof(ContentRow.Artist), 220, "artist", true),
            (LocalizationManager.Current.Year, nameof(ContentRow.Year), 90, "year", true));
        ConfigureSearchGrid(SearchArtistsDataGrid,
            (LocalizationManager.Current.Artist, nameof(ContentRow.Title), 320, "artist", true));
    }

    private void ConfigureSearchGrid(
        DataGrid grid,
        params (string Header, string Binding, double Width, string Key, bool DefaultVisible)[] columns)
    {
        var widthKey = grid.Name switch
        {
            nameof(SearchTracksDataGrid) => "SearchTracks",
            nameof(SearchAlbumsDataGrid) => "SearchAlbums",
            _ => "SearchArtists"
        };
        CaptureColumnWidths(widthKey, grid);
        grid.Columns.Clear();
        grid.Columns.Add(CreateFavoriteColumn());
        grid.Columns.Add(CreateSourceBadgeColumn());
        foreach (var column in columns)
        {
            var entityType = grid == SearchArtistsDataGrid && column.Binding == nameof(ContentRow.Title)
                ? "Artist"
                : grid == SearchAlbumsDataGrid && column.Binding == nameof(ContentRow.Title)
                    ? "Album"
                    : column.Binding == nameof(ContentRow.Artist)
                        ? "Artist"
                        : column.Binding == nameof(ContentRow.Album)
                            ? "Album"
                            : null;
            DataGridColumn dataGridColumn = entityType is null
                ? new DataGridTextColumn
                {
                    Header = column.Header,
                    Binding = new Binding(column.Binding),
                    Width = new DataGridLength(column.Width),
                    Tag = column.Key,
                    IsVisible = column.DefaultVisible
                }
                : CreateEntityLinkColumn(column.Header, column.Binding, column.Width, false, entityType);
            dataGridColumn.Tag = column.Key;
            dataGridColumn.IsVisible = column.DefaultVisible;
            grid.Columns.Add(dataGridColumn);
        }
        DataGridColumnChooser.Apply(grid, widthKey, _settings);
        RestoreColumnWidths(widthKey, grid);
    }

    private DataGridColumn CreateFavoriteColumn() =>
        new DataGridTemplateColumn
        {
            Header = "",
            Width = new DataGridLength(42),
            CellTemplate = new FuncDataTemplate<ContentRow>((_, _) =>
            {
                var button = new Button
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Width = 28,
                    Height = 28,
                    MinWidth = 0,
                    MinHeight = 0,
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = FindResource<IBrush>("AppFavoriteBrush"),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = 17
                };
                button.Bind(Button.ContentProperty, new Binding(nameof(ContentRow.FavoriteGlyph)));
                button.Bind(Button.TagProperty, new Binding("."));
                button.Click += FavoriteButton_OnClick;
                return button;
            })
        };

    private DataGridColumn CreateSourceBadgeColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = LocalizationManager.Current.SourceColumn,
            Width = new DataGridLength(54),
            CellTemplate = new FuncDataTemplate<ContentRow>((_, _) =>
            {
                var badge = new Border
                {
                    Height = 22,
                    MinWidth = 28,
                    Padding = new Thickness(7, 0),
                    CornerRadius = new CornerRadius(11),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = FindResource<IBrush>("AppSurfaceHoverBrush"),
                    BorderBrush = FindResource<IBrush>("AppAccentBrush"),
                    BorderThickness = new Thickness(1)
                };
                var text = new TextBlock
                {
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = FindResource<IBrush>("AppAccentBrush")
                };
                text.Bind(TextBlock.TextProperty, new Binding(nameof(ContentRow.SourceBadge)));
                // The default tooltip theme renders a string tip in a TextBlock that does not
                // inherit ToolTip.Foreground, which left the source name black on the dark
                // surface. Provide an explicit TextBlock with a theme foreground instead.
                var tipText = new TextBlock
                {
                    Foreground = FindResource<IBrush>("AppPrimaryTextBrush")
                };
                tipText.Bind(TextBlock.TextProperty, new Binding(nameof(ContentRow.SourceName)));
                var tip = new ToolTip
                {
                    Background = FindResource<IBrush>("AppSurfaceBrush"),
                    Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                    BorderBrush = FindResource<IBrush>("AppGridLineBrush"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 4),
                    Content = tipText
                };
                ToolTip.SetTip(badge, tip);
                badge.Bind(IsVisibleProperty, new Binding(nameof(ContentRow.SourceBadge))
                {
                    Converter = StringNotEmptyConverter.Instance
                });
                badge.Child = text;
                return badge;
            })
        };
        column.Tag = "source";
        return column;
    }

    private DataGridTemplateColumn CreateEntityLinkColumn(
        string header,
        string property,
        double width,
        bool star,
        string entityType,
        double starWeight = 1)
    {
        EventHandler<RoutedEventArgs> clickHandler = entityType == "Artist"
            ? ArtistLinkButton_OnClick
            : AlbumLinkButton_OnClick;

        return new DataGridTemplateColumn
        {
            Header = header,
            Width = star
                ? new DataGridLength(starWeight, DataGridLengthUnitType.Star)
                : new DataGridLength(width),
            CellTemplate = new FuncDataTemplate<ContentRow>((row, _) =>
            {
                var button = new Button
                {
                    Theme = FindResource<ControlTheme>("EntityLinkButtonTheme")
                };
                button.Bind(Button.ContentProperty, new Binding(property));
                button.Bind(Button.TagProperty, new Binding("."));
                button.Click += clickHandler;
                return button;
            })
        };
    }

    private static IImage? CreateArtworkImage(string? path, int decodeWidth, bool ignoreCache = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            return decodeWidth > 0
                ? Bitmap.DecodeToWidth(stream, decodeWidth)
                : new Bitmap(stream);
        }
        catch { return null; }
    }

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           uri.Scheme is "http" or "https";

    private static string GetRemoteArtworkCachePath(string url, int decodeWidth)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return AppPaths.GetDataPath("remote-artworks", $"{hash}_{decodeWidth}.img");
    }

    private static void InvalidateRemoteArtworkCache(string? url)
    {
        if (!IsHttpUrl(url))
            return;

        var prefix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url!)));
        var directory = AppPaths.GetDataPath("remote-artworks");
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(directory, $"{prefix}_*.img"))
        {
            try { File.Delete(file); }
            catch { }
        }
    }

    private static void WriteRemoteArtworkCache(string? url, byte[] imageData)
    {
        if (!IsHttpUrl(url))
            return;

        var directory = AppPaths.GetDataPath("remote-artworks");
        Directory.CreateDirectory(directory);
        var prefix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url!)));
        foreach (var width in new[] { 96, 320, 1000 })
        {
            try
            {
                File.WriteAllBytes(Path.Combine(directory, $"{prefix}_{width}.img"), imageData);
            }
            catch { }
        }
    }

    private static async Task<IImage?> LoadRemoteArtworkImageAsync(
        string url,
        int decodeWidth,
        CancellationToken cancellationToken = default)
    {
        var cachePath = GetRemoteArtworkCachePath(url, decodeWidth);
        if (File.Exists(cachePath))
        {
            var cached = await Task.Run(() => CreateArtworkImage(cachePath, decodeWidth), cancellationToken);
            if (cached is not null)
                return cached;

            try { File.Delete(cachePath); }
            catch { }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var data = await RemoteArtworkHttpClient.GetByteArrayAsync(url, cancellationToken);
            await File.WriteAllBytesAsync(cachePath, data, cancellationToken);
            await using var stream = new MemoryStream(data);
            return Bitmap.DecodeToWidth(stream, decodeWidth);
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureArtworkHydrated(ContentRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ArtworkPath))
            row.Artwork = null;
        else if (IsHttpUrl(row.ArtworkPath) && row.Artwork is null && !row.ArtworkLoadQueued && !row.ArtworkLoadCompleted)
        {
            row.ArtworkLoadQueued = true;
            _ = LoadArtworkAsync(row, row.ArtworkPath);
        }
        else if (row.Artwork is null)
            row.Artwork = CreateArtworkImage(row.ArtworkPath, 320);

        if (string.IsNullOrWhiteSpace(row.ThumbnailPath))
            row.Thumbnail = null;
        else if (IsHttpUrl(row.ThumbnailPath) && row.Thumbnail is null && !row.ThumbnailLoadQueued && !row.ThumbnailLoadCompleted)
        {
            row.ThumbnailLoadQueued = true;
            _ = LoadRemoteThumbnailAsync(row, row.ThumbnailPath);
        }
        else if (row.Thumbnail is null)
            row.Thumbnail = CreateArtworkImage(row.ThumbnailPath, 96);
    }

    private void QueueHydrateVisibleArtworkRows(ListBox listBox)
    {
        if (!listBox.IsVisible)
            return;

        Dispatcher.UIThread.Post(() => HydrateVisibleArtworkRows(listBox), DispatcherPriority.Background);
    }

    private void BindArtworkRows(string tag, IReadOnlyList<ContentRow> rows)
    {
        var bindingVersion = ++_artworkBindingVersion;
        if (tag == "Albums")
        {
            _albumArtworkRows = rows as List<ContentRow> ?? rows.ToList();
            ResetVisibleArtworkRows(_visibleAlbumArtworkRows, _albumArtworkRows);
            AlbumArtworkListBox.ItemsSource = _visibleAlbumArtworkRows;
            ResetArtworkScrollPositionAfterLayout(AlbumArtworkListBox, bindingVersion);
            QueueHydrateVisibleArtworkRows(AlbumArtworkListBox);
        }
        else if (tag == "Artists")
        {
            _artistArtworkRows = rows as List<ContentRow> ?? rows.ToList();
            ResetVisibleArtworkRows(_visibleArtistArtworkRows, _artistArtworkRows);
            ArtistArtworkListBox.ItemsSource = _visibleArtistArtworkRows;
            ResetArtworkScrollPositionAfterLayout(ArtistArtworkListBox, bindingVersion);
            QueueHydrateVisibleArtworkRows(ArtistArtworkListBox);
        }
    }

    private void ResetArtworkScrollPositionAfterLayout(ListBox listBox, int bindingVersion)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (bindingVersion != _artworkBindingVersion)
                return;

            var scrollViewer = listBox.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();
            if (scrollViewer is not null)
                scrollViewer.Offset = new Vector(0, 0);
            UpdateActiveAlphabetButton();
        }, DispatcherPriority.Loaded);
    }

    private static void ResetVisibleArtworkRows(
        ObservableCollection<ContentRow> visibleRows,
        IReadOnlyList<ContentRow> allRows)
    {
        visibleRows.Clear();
        var count = Math.Min(ArtworkPageSize, allRows.Count);
        for (var i = 0; i < count; i++)
            visibleRows.Add(allRows[i]);
    }

    private double GetArtworkItemWidth(ListBox listBox) =>
        ReferenceEquals(listBox, AlbumArtworkListBox)
            ? AlbumArtworkItemWidth
            : ArtistArtworkItemWidth;

    private double GetArtworkItemHeight(ListBox listBox) =>
        ReferenceEquals(listBox, AlbumArtworkListBox)
            ? AlbumArtworkItemHeight
            : ArtistArtworkItemHeight;

    private void AppendArtworkRowsIfNeeded(ListBox listBox)
    {
        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
            return;

        var itemHeight = GetArtworkItemHeight(listBox);
        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height < scrollViewer.Extent.Height - itemHeight * 3)
            return;

        if (AppendArtworkRows(listBox))
            QueueHydrateVisibleArtworkRows(listBox);
    }

    private bool AppendArtworkRows(ListBox listBox)
    {
        var allRows = ReferenceEquals(listBox, AlbumArtworkListBox)
            ? _albumArtworkRows
            : _artistArtworkRows;
        var visibleRows = ReferenceEquals(listBox, AlbumArtworkListBox)
            ? _visibleAlbumArtworkRows
            : _visibleArtistArtworkRows;

        if (visibleRows.Count >= allRows.Count)
            return false;

        var end = Math.Min(visibleRows.Count + ArtworkPageSize, allRows.Count);
        for (var i = visibleRows.Count; i < end; i++)
            visibleRows.Add(allRows[i]);
        return true;
    }

    private void EnsureArtworkRowBound(ListBox listBox, ContentRow row)
    {
        var allRows = ReferenceEquals(listBox, AlbumArtworkListBox)
            ? _albumArtworkRows
            : _artistArtworkRows;
        var visibleRows = ReferenceEquals(listBox, AlbumArtworkListBox)
            ? _visibleAlbumArtworkRows
            : _visibleArtistArtworkRows;
        var index = allRows.IndexOf(row);
        if (index < 0)
            return;
        while (visibleRows.Count <= index && AppendArtworkRows(listBox))
        {
        }
    }

    private void ScrollArtworkRowIntoViewAfterLayout(ListBox listBox, ContentRow row)
    {
        var bindingVersion = _artworkBindingVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (bindingVersion != _artworkBindingVersion ||
                (listBox.ItemsSource as System.Collections.IList)?.Contains(row) != true)
            {
                _isAlphabetProgrammaticScroll = false;
                return;
            }

            listBox.ScrollIntoView(row);
            Dispatcher.UIThread.Post(() =>
            {
                if (bindingVersion == _artworkBindingVersion)
                {
                    listBox.ScrollIntoView(row);
                    QueueHydrateVisibleArtworkRows(listBox);
                }
                _isAlphabetProgrammaticScroll = false;
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Loaded);
    }

    private void HydrateVisibleArtworkRows(ListBox listBox)
    {
        if (!listBox.IsVisible)
            return;

        var rows = listBox.ItemsSource as IReadOnlyList<ContentRow>
            ?? (listBox.ItemsSource as IEnumerable<ContentRow>)?.ToList();
        if (rows is null || rows.Count == 0)
            return;

        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
        {
            HydrateInitialArtworkRows(rows);
            Dispatcher.UIThread.Post(() => QueueHydrateVisibleArtworkRows(listBox), DispatcherPriority.Loaded);
            return;
        }

        var itemWidth = GetArtworkItemWidth(listBox);
        var itemHeight = GetArtworkItemHeight(listBox);
        if (scrollViewer.Viewport.Width <= 0 || scrollViewer.Viewport.Height <= 0)
        {
            HydrateInitialArtworkRows(rows);
            Dispatcher.UIThread.Post(() => QueueHydrateVisibleArtworkRows(listBox), DispatcherPriority.Loaded);
            return;
        }

        var perRow = Math.Max(1, (int)Math.Floor(scrollViewer.Viewport.Width / itemWidth));
        var firstRow = Math.Max(0, (int)Math.Floor(scrollViewer.Offset.Y / itemHeight) - 1);
        var lastRow = Math.Min(
            (int)Math.Ceiling(rows.Count / (double)perRow) - 1,
            (int)Math.Ceiling((scrollViewer.Offset.Y + scrollViewer.Viewport.Height) / itemHeight) + 1);

        for (var index = firstRow * perRow; index < rows.Count && index <= ((lastRow + 1) * perRow) - 1; index++)
        {
            QueueArtworkHydration(rows[index]);
            if (rows[index].EntityType == "Artist")
                _ = EnsureArtistProfileAsync(rows[index]);
        }
    }

    private void HydrateInitialArtworkRows(IReadOnlyList<ContentRow> rows)
    {
        var count = Math.Min(ArtworkPageSize, rows.Count);
        for (var index = 0; index < count; index++)
        {
            QueueArtworkHydration(rows[index]);
            if (rows[index].EntityType == "Artist")
                _ = EnsureArtistProfileAsync(rows[index]);
        }
    }

    private void QueueArtworkHydration(ContentRow row)
    {
        if (row.Artwork is not null || row.ArtworkLoadQueued || row.ArtworkLoadCompleted)
            return;

        var path = row.ArtworkPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            row.Artwork = null;
            row.ArtworkLoadCompleted = true;
            return;
        }

        row.ArtworkLoadQueued = true;
        _ = LoadArtworkAsync(row, path);
    }

    private static async Task LoadArtworkAsync(ContentRow row, string path)
    {
        IImage? image = null;
        try
        {
            if (IsHttpUrl(path))
            {
                image = await LoadRemoteArtworkImageAsync(path, 320);
            }
            else
            {
                image = await Task.Run(() => CreateArtworkImage(path, 320));
            }
        }
        catch
        {
            image = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            row.ArtworkLoadQueued = false;
            row.ArtworkLoadCompleted = image is not null || !IsHttpUrl(path);
            if (string.Equals(row.ArtworkPath, path, StringComparison.OrdinalIgnoreCase))
                row.Artwork = image;
        }, DispatcherPriority.Background);
    }

    private static void EnsureThumbnailHydrated(ContentRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ThumbnailPath))
            row.Thumbnail = null;
        else if (IsHttpUrl(row.ThumbnailPath) &&
                 row.Thumbnail is null &&
                 !row.ThumbnailLoadQueued &&
                 !row.ThumbnailLoadCompleted)
        {
            row.ThumbnailLoadQueued = true;
            _ = LoadRemoteThumbnailAsync(row, row.ThumbnailPath);
        }
        else if (row.Thumbnail is null)
            row.Thumbnail = CreateArtworkImage(row.ThumbnailPath, 96);
    }

    private static async Task LoadRemoteThumbnailAsync(ContentRow row, string url)
    {
        IImage? image = null;
        try
        {
            image = await LoadRemoteArtworkImageAsync(url, 96);
        }
        catch
        {
            image = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            row.ThumbnailLoadQueued = false;
            row.ThumbnailLoadCompleted = image is not null || !IsHttpUrl(url);
            if (string.Equals(row.ThumbnailPath, url, StringComparison.OrdinalIgnoreCase))
                row.Thumbnail = image;
        }, DispatcherPriority.Background);
    }

    private static string FormatSeconds(double? seconds)
    {
        if (seconds is null) return "";
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    private async void AlbumViewModeRadioButton_OnChecked(object? sender, RoutedEventArgs e)
    {
        if (!IsVisible || _updatingViewMode)
            return;

        var artworkMode = AlbumArtworkViewRadioButton.IsChecked == true;
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: string tag })
            return;

        if (tag == "Albums" ||
            (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal) && _activeOrynivoView == "Albums"))
        {
            _showAlbumArtworkView = artworkMode;
            _settings.AlbumArtworkView = artworkMode;
            if (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal))
            {
                await LoadOrynivoViewAsync();
                return;
            }
        }
        else if (tag == "Artists" ||
                 (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal) && _activeOrynivoView == "Artists"))
        {
            _showArtistArtworkView = artworkMode;
            _settings.ArtistArtworkView = artworkMode;
            if (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal))
            {
                await LoadOrynivoViewAsync();
                return;
            }
        }
        else
        {
            return;
        }

        await ReloadEntityRowsAsync(tag);
    }

    private void SetViewModeButtons(bool artworkMode)
    {
        _updatingViewMode = true;
        AlbumArtworkViewRadioButton.IsChecked = artworkMode;
        AlbumTableViewRadioButton.IsChecked = !artworkMode;
        _updatingViewMode = false;
    }

    private void ContentDataGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        var loadingRowCount = Interlocked.Increment(ref _diagnosticLoadingRowCount);
        if (loadingRowCount <= 5 || loadingRowCount % 250 == 0)
            LogUiDiagnostics($"ContentDataGrid_OnLoadingRow count={loadingRowCount} tag={_currentTopLevelTag ?? "<null>"}");
        ApplyNowPlayingClass(e.Row);
        SetPlaylistContextFlyout(e.Row);
        if (e.Row.DataContext is not ContentRow row)
            return;
        if (ContentDataGrid.Columns.Any(column =>
                column.IsVisible &&
                string.Equals(column.Tag as string, "thumbnail", StringComparison.Ordinal)))
        {
            EnsureThumbnailHydrated(row);
        }
        if (row.EntityType == "Artist")
            _ = EnsureArtistProfileAsync(row);
    }

    private void TrackDataGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        ApplyNowPlayingClass(e.Row);
        if (ReferenceEquals(sender, SearchTracksDataGrid))
            SetPlaylistContextFlyout(e.Row);
    }

    private void PlaylistDataGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e) =>
        SetPlaylistContextFlyout(e.Row);

    private void SetPlaylistContextFlyout(DataGridRow row)
    {
        row.RemoveHandler(
            PointerPressedEvent,
            PlaylistContextItem_OnPreviewMouseRightButtonDown);
        row.ContextFlyout = null;
        if (row.DataContext is not ContentRow contentRow)
        {
            return;
        }

        var isTrack = !string.IsNullOrEmpty(contentRow.FilePath);
        var isAlbum = contentRow.EntityType == "Album";
        var isRemoteArtworkEntity = contentRow.EntityType is "OrynivoArtist" or "OrynivoAlbum";
        if (isRemoteArtworkEntity)
        {
            row.ContextFlyout = BuildOrynivoArtworkContextFlyout(contentRow);
            row.AddHandler(
                PointerPressedEvent,
                PlaylistContextItem_OnPreviewMouseRightButtonDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            return;
        }

        if (!isTrack && !isAlbum)
            return;

        row.ContextFlyout = _activeOrynivoPlaylistServer is not null &&
                            isTrack &&
                            contentRow.PlaylistEntryId.HasValue
            ? BuildRemoveFromOrynivoPlaylistContextFlyout(
                _activeOrynivoPlaylistServer,
                contentRow.PlaylistEntryId.Value,
                contentRow.FilePath)
            : contentRow.EntityType.StartsWith("Plex", StringComparison.Ordinal) ||
                            (!CanPersistQueuePath(contentRow.FilePath) &&
                             contentRow.EntityType != "OrynivoTrack")
            ? BuildQueueContextFlyout([contentRow.FilePath])
            : _activePlaylistId.HasValue &&
                            isTrack &&
                            contentRow.PlaylistEntryId.HasValue
            ? BuildRemoveFromPlaylistContextFlyout(
                contentRow.PlaylistEntryId.Value,
                contentRow.FilePath)
            : BuildPlaylistContextFlyout(GetPathsForRow(contentRow));
        row.AddHandler(
            PointerPressedEvent,
            PlaylistContextItem_OnPreviewMouseRightButtonDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private MenuFlyout BuildOrynivoArtworkContextFlyout(ContentRow row)
    {
        var menu = CreateSidebarMenuFlyout();
        if (row.EntityType == "OrynivoArtist")
        {
            var refreshItem = CreateFlyoutMenuItem(LocalizationManager.Current.RefreshArtistInfo);
            refreshItem.Tag = row;
            refreshItem.Click += OrynivoArtistInfoRefreshMenuItem_OnClick;
            menu.Items.Add(refreshItem);
            menu.Items.Add(new Separator());
        }

        var label = row.EntityType == "OrynivoArtist"
            ? LocalizationManager.Current.SearchArtistImage
            : LocalizationManager.Current.SearchCover;
        var item = CreateFlyoutMenuItem(label);
        item.Tag = row;
        item.Click += OrynivoArtworkMenuItem_OnClick;
        menu.Items.Add(item);
        if (row.EntityType == "OrynivoAlbum")
        {
            menu.Items.Add(new Separator());
            // Resolving a remote album's track list requires a server round-trip.
            // This flyout is built while the row is being realized, so fetching the
            // paths synchronously here blocks the UI thread inside the DataGrid layout
            // pass and freezes the whole album table. Populate the playlist targets
            // asynchronously the first time the flyout is opened instead.
            var populated = false;
            menu.Opened += async (_, _) =>
            {
                if (populated)
                    return;
                populated = true;

                List<string> paths;
                try { paths = await Task.Run(() => GetPathsForRow(row)); }
                catch { paths = []; }
                if (paths.Count > 0)
                    AppendPlaylistItems(menu, paths);
            };
        }
        return menu;
    }

    private void ApplyNowPlayingClass(DataGridRow row)
    {
        var rowPath = GetPlaybackPath(row.DataContext);
        row.Classes.Set(
            "nowPlaying",
            _player is not null &&
            !string.IsNullOrWhiteSpace(rowPath) &&
            string.Equals(rowPath, _currentFilePath, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyNowPlayingClass(TreeViewItem item)
    {
        var audiblePath = (_player as IGaplessAudioPlayer)?.CurrentFilePath ??
                          _currentFilePath;
        var itemPath = item.Tag switch
        {
            FolderTag { IsFile: true } folder => folder.FilePath,
            PlexFolderTag { IsTrack: true, Track: not null } plex => plex.Track.FilePath,
            _ => null
        };
        var isNowPlaying =
            _player is not null &&
            !string.IsNullOrWhiteSpace(itemPath) &&
            string.Equals(itemPath, audiblePath, StringComparison.OrdinalIgnoreCase);
        item.Classes.Set("nowPlaying", isNowPlaying);

        if (item.Tag is FolderTag { IsFile: true })
        {
            item.ClearValue(TemplatedControl.BackgroundProperty);
            if (itemPath is not null &&
                _localFolderTrackHeaders.TryGetValue(itemPath, out var header))
            {
                header.Background = isNowPlaying
                    ? FindResource<IBrush>("AppNowPlayingRowBrush")
                    : Brushes.Transparent;
            }
        }
    }

    private static string? GetPlaybackPath(object? row) => row switch
    {
        ContentRow content => content.FilePath,
        RadioStationViewModel radio => radio.StreamUrl,
        PodcastEpisodeViewModel podcast => podcast.Episode.AudioUrl,
        _ => null
    };

    private void UpdateNowPlayingRowHighlights()
    {
        foreach (var row in this.GetVisualDescendants().OfType<DataGridRow>())
            ApplyNowPlayingClass(row);
        foreach (var item in _localFolderTrackItems.Values)
            ApplyNowPlayingClass(item);
        foreach (var item in FolderTreeView.Items.OfType<TreeViewItem>())
            UpdateNowPlayingTreeHighlights(item);
    }

    private void UpdateNowPlayingTreeHighlights(TreeViewItem item)
    {
        ApplyNowPlayingClass(item);
        foreach (var child in item.Items.OfType<TreeViewItem>())
            UpdateNowPlayingTreeHighlights(child);
    }

    private Button CreateArtistInfoIconButton()
    {
        var foreground = FindResource<IBrush>("AppAccentBrush");
        var canvas = new Canvas
        {
            Width = 18,
            Height = 18
        };

        var ring = new AvaloniaEllipse
        {
            Width = 16,
            Height = 16,
            Stroke = foreground,
            StrokeThickness = 1.8
        };
        Canvas.SetLeft(ring, 1);
        Canvas.SetTop(ring, 1);
        canvas.Children.Add(ring);

        var dot = new AvaloniaEllipse
        {
            Width = 2,
            Height = 2,
            Fill = foreground
        };
        Canvas.SetLeft(dot, 8);
        Canvas.SetTop(dot, 4.4);
        canvas.Children.Add(dot);

        canvas.Children.Add(new AvaloniaPath
        {
            Stroke = foreground,
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M 9 8 L 9 13")
        });

        var button = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = foreground,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = new Viewbox
            {
                Width = 16,
                Height = 16,
                Child = canvas
            }
        };
        ToolTip.SetTip(button, LocalizationManager.Current.ShowArtistInfo);
        return button;
    }

    private void ApplyColumns(
        string view,
        DataGrid? targetGrid = null,
        bool captureCurrentWidths = true)
    {
        var grid = targetGrid ?? ContentDataGrid;
        if (captureCurrentWidths && ReferenceEquals(grid, ContentDataGrid))
        {
            CaptureContentDataGridColumnWidths();
            _contentColumnWidthKey = GetContentColumnWidthKey(view);
        }
        var widthKey = GetContentColumnWidthKey(view);
        grid.Columns.Clear();
        switch (view)
        {
            case "PlexArtists":
                Add(LocalizationManager.Current.Artist, nameof(ContentRow.Title), 0, "artist", star: true);
                break;
            case "PlexAlbums":
                Add(LocalizationManager.Current.Album, nameof(ContentRow.Title), 0, "album", star: true);
                Add(LocalizationManager.Current.AlbumArtist, nameof(ContentRow.Artist), 220, "artist");
                Add(LocalizationManager.Current.Year, nameof(ContentRow.Year), 70, "year", right: true);
                break;
            case "PlexTracks":
                Add(LocalizationManager.Current.Title, nameof(ContentRow.Title), 0, "title", star: true);
                Add(LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 180, "artist");
                Add(LocalizationManager.Current.Album, nameof(ContentRow.Album), 180, "album");
                Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 70, "duration", right: true);
                Add(LocalizationManager.Current.Format, nameof(ContentRow.Format), 80, "format");
                break;
            case "Artists":
                AddFavorite();
                AddSourceBadge();
                AddThumbnail();
                AddArtistInfo();
                AddEntityLink(LocalizationManager.Current.Artist, nameof(ContentRow.Title), 0, "artist", true, "Artist");
                break;
            case "Albums":
                AddFavorite();
                AddSourceBadge();
                AddThumbnail();
                AddEntityLink(LocalizationManager.Current.Album, nameof(ContentRow.Title), 0, "album", true, "Album");
                AddEntityLink(LocalizationManager.Current.AlbumArtist, nameof(ContentRow.Artist), 220, "artist", false, "Artist");
                Add(LocalizationManager.Current.Year, nameof(ContentRow.Year), 60, "year", right: true);
                break;
            case string playlistTag when playlistTag.StartsWith("Playlist:", StringComparison.Ordinal) ||
                                         playlistTag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal):
                AddFavorite();
                AddSourceBadge();
                Add("#", nameof(ContentRow.Nr), 38, "position", right: true);
                AddTrackColumns(includeFavorite: false, includeSource: false, includeGenreByDefault: false);
                break;
            case "Queue":
                AddSourceBadge();
                Add("#", nameof(ContentRow.Nr), 38, "position", right: true);
                Add(LocalizationManager.Current.Title, nameof(ContentRow.Title), 0, "title", star: true, starWeight: 2.3);
                Add(LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 0, "artist", star: true, starWeight: 1.05);
                Add(LocalizationManager.Current.Album, nameof(ContentRow.Album), 0, "album", star: true, starWeight: 1.05);
                Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 80, "duration", right: true);
                AddQueueActions();
                break;
            default: // Tracks
                AddTrackColumns(includeFavorite: true, includeSource: true, includeGenreByDefault: true);
                break;
        }

        DataGridColumnChooser.Apply(grid, widthKey, _settings);
        RestoreColumnWidths(widthKey, grid);

        void AddEntityLink(
            string header,
            string prop,
            double width,
            string key,
            bool star,
            string entityType,
            double starWeight = 1,
            bool defaultVisible = true)
        {
            var column = CreateEntityLinkColumn(header, prop, width, star, entityType, starWeight);
            column.Tag = key;
            column.IsVisible = defaultVisible;
            grid.Columns.Add(column);
        }

        void Add(
            string header,
            string prop,
            double width,
            string key,
            bool star = false,
            bool right = false,
            double starWeight = 1,
            bool defaultVisible = true)
        {
            DataGridColumn column;
            if (right)
            {
                column = new DataGridTemplateColumn
                {
                    Header = header,
                    Width = star ? new DataGridLength(starWeight, DataGridLengthUnitType.Star) : new DataGridLength(width),
                    CellTemplate = new FuncDataTemplate<ContentRow>((_, _) =>
                    {
                        var tb = new TextBlock
                        {
                            HorizontalAlignment = HorizontalAlignment.Right,
                            FontSize = 12,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        };
                        tb.Bind(TextBlock.TextProperty, new Binding(prop));
                        return tb;
                    })
                };
            }
            else
            {
                column = new DataGridTextColumn
                {
                    Header = header,
                    Binding = new Binding(prop),
                    Width = star ? new DataGridLength(starWeight, DataGridLengthUnitType.Star) : new DataGridLength(width)
                };
            }
            column.Tag = key;
            column.IsVisible = defaultVisible;
            grid.Columns.Add(column);
        }

        void AddTrackColumns(bool includeFavorite, bool includeSource, bool includeGenreByDefault)
        {
            if (includeFavorite)
                AddFavorite();
            if (includeSource)
                AddSourceBadge();
            Add(LocalizationManager.Current.Title, nameof(ContentRow.Title), 0, "title", star: true, starWeight: 2.3);
            AddEntityLink(LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 0, "artist", true, "Artist", 1.05);
            AddEntityLink(LocalizationManager.Current.Album, nameof(ContentRow.Album), 0, "album", true, "Album", 1.05);
            Add(LocalizationManager.Current.Genre, nameof(ContentRow.Genre), 120, "genre", defaultVisible: includeGenreByDefault);
            Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 80, "duration", right: true);
            Add(LocalizationManager.Current.Format, nameof(ContentRow.Format), 80, "format");
            Add(LocalizationManager.Current.AlbumArtist, nameof(ContentRow.AlbumArtist), 180, "albumArtist", defaultVisible: false);
            Add(LocalizationManager.Current.Year, nameof(ContentRow.Year), 80, "year", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.TrackNumber, nameof(ContentRow.TrackNumber), 90, "trackNumber", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.DiscNumber, nameof(ContentRow.DiscNumber), 90, "discNumber", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.Bitrate, nameof(ContentRow.Bitrate), 100, "bitrate", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.SampleRate, nameof(ContentRow.SampleRate), 110, "sampleRate", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.BitDepth, nameof(ContentRow.BitDepth), 90, "bitDepth", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.Channels, nameof(ContentRow.Channels), 80, "channels", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.Composer, nameof(ContentRow.Composer), 180, "composer", defaultVisible: false);
            Add(LocalizationManager.Current.Bpm, nameof(ContentRow.Bpm), 70, "bpm", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.FileName, nameof(ContentRow.FileName), 220, "fileName", defaultVisible: false);
            Add(LocalizationManager.Current.FileSize, nameof(ContentRow.FileSize), 100, "fileSize", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.AddedAt, nameof(ContentRow.AddedAt), 110, "addedAt", defaultVisible: false);
            Add(LocalizationManager.Current.ReplayGainTrackColumn, nameof(ContentRow.ReplayGainTrack), 120, "replayGainTrack", right: true, defaultVisible: false);
            Add(LocalizationManager.Current.ReplayGainAlbumColumn, nameof(ContentRow.ReplayGainAlbum), 120, "replayGainAlbum", right: true, defaultVisible: false);
        }

        void AddSourceBadge()
        {
            grid.Columns.Add(CreateSourceBadgeColumn());
        }

        void AddFavorite()
        {
            grid.Columns.Add(CreateFavoriteColumn());
        }

        void AddThumbnail(bool defaultVisible = true)
        {
            var column = new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(64),
                CellTemplate = new FuncDataTemplate<ContentRow>((_, _) =>
                {
                    var image = new Image
                    {
                        Width = 32,
                        Height = 32,
                        Stretch = Stretch.UniformToFill,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    image.Bind(Image.SourceProperty, new Binding(nameof(ContentRow.Thumbnail)));
                    return image;
                })
            };
            column.Tag = "thumbnail";
            column.IsVisible = defaultVisible;
            grid.Columns.Add(column);
        }

        void AddArtistInfo()
        {
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(44),
                CellTemplate = new FuncDataTemplate<ContentRow>((_, _) =>
                {
                    var button = CreateArtistInfoIconButton();
                    button.HorizontalAlignment = HorizontalAlignment.Center;
                    button.Bind(Button.TagProperty, new Binding("."));
                    button.Click += ArtistInfoListButton_OnClick;
                    return button;
                })
            });
        }

        void AddQueueActions()
        {
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(132),
                CellTemplate = new FuncDataTemplate<ContentRow>((row, _) =>
                {
                    var panel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 4
                    };
                    panel.Children.Add(CreateQueueActionButton(
                        "↑",
                        LocalizationManager.Current.MoveUp,
                        row,
                        QueueMoveUpButton_OnClick));
                    panel.Children.Add(CreateQueueActionButton(
                        "↓",
                        LocalizationManager.Current.MoveDown,
                        row,
                        QueueMoveDownButton_OnClick));
                    panel.Children.Add(CreateQueueActionButton(
                        "×",
                        LocalizationManager.Current.RemoveFromQueue,
                        row,
                        QueueRemoveButton_OnClick));
                    return panel;
                })
            });
        }
    }

    // ------------------------------------------------------------------
    // Ordnerstruktur-Baum  (lazy loading für Performance)
    // ------------------------------------------------------------------

    // Vorberechnete Baumstruktur – reine C#-Objekte, keine WPF-Elemente
    private sealed class FolderTree
    {
        private readonly Dictionary<string, List<string>>    _childDirs;
        private readonly Dictionary<string, List<TrackLite>> _filesPerDir;
        private readonly HashSet<string>                     _allDirs;

        public FolderTree(List<TrackLite> tracks)
        {
            _filesPerDir = tracks
                .GroupBy(t => Path.GetDirectoryName(t.SourcePath) ?? string.Empty,
                         StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(t => t.DiscNumber ?? 0)
                           .ThenBy(t => t.TrackNumber ?? 0)
                           .ThenBy(t => t.FileName).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            // Alle Zwischenverzeichnisse ergänzen
            _allDirs = new HashSet<string>(_filesPerDir.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in _filesPerDir.Keys.ToList())
            {
                var anc = Path.GetDirectoryName(dir);
                while (anc != null && _allDirs.Add(anc))
                    anc = Path.GetDirectoryName(anc);
            }

            // Parent→Children-Map aufbauen (sortiert)
            _childDirs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _allDirs)
            {
                var parent = Path.GetDirectoryName(d);
                if (parent == null) continue;
                if (!_childDirs.TryGetValue(parent, out var list))
                    _childDirs[parent] = list = [];
                list.Add(d);
            }
            foreach (var list in _childDirs.Values)
                list.Sort(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<string>    SubDirs(string dir)    => _childDirs.TryGetValue(dir,  out var d) ? d : [];
        public IReadOnlyList<TrackLite> Files(string dir)      => _filesPerDir.TryGetValue(dir, out var f) ? f : [];
        public bool HasChildren(string dir)                    => _childDirs.ContainsKey(dir) || _filesPerDir.ContainsKey(dir);

        /// <summary>Returns every track playback path at or below <paramref name="dir"/>, regardless of whether the tree nodes are materialized.</summary>
        /// <param name="dir">Directory to collect files under.</param>
        /// <returns>All descendant file playback paths.</returns>
        public IEnumerable<string> AllFilePathsUnder(string dir)
        {
            foreach (var file in Files(dir))
                yield return file.Path;
            foreach (var sub in SubDirs(dir))
                foreach (var path in AllFilePathsUnder(sub))
                    yield return path;
        }
        public bool HasRoot(string root)
            => _allDirs.Any(dir => IsSameOrBelow(dir, root)) ||
               _filesPerDir.Keys.Any(dir => IsSameOrBelow(dir, root));

        public IEnumerable<string> AutoRoots() =>
            _allDirs
                .Where(d => { var p = Path.GetDirectoryName(d); return p is null || !_allDirs.Contains(p); })
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);

        private static bool IsSameOrBelow(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
                return false;
            var normalizedPath = Path.TrimEndingDirectorySeparator(path);
            var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
            return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    private Button CreateQueueActionButton(
        string content,
        string tooltip,
        ContentRow row,
        EventHandler<RoutedEventArgs> clickHandler)
    {
        var button = new Button
        {
            Content = content,
            Tag = row,
            Width = 34,
            Height = 28,
            Padding = new Thickness(0),
            Theme = FindResource<ControlTheme>("HeaderFilterButtonTheme")
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += clickHandler;
        return button;
    }

    private void RefreshQueueRows()
    {
        _queueRows.Clear();
        using var db = AudioDatabase.OpenDefault();
        var localTracks = db.GetTrackListByPaths(_queue.Select(item => item.FilePath))
            .ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _queue.Count; index++)
        {
            var item = _queue[index];
            ContentRow row;
            if (localTracks.TryGetValue(item.FilePath, out var track))
            {
                row = ToTrackContentRow(track);
            }
            else if (_plexTracksByUrl.TryGetValue(item.FilePath, out var plexRow))
            {
                row = new ContentRow
                {
                    Title = plexRow.Title,
                    Artist = plexRow.Artist,
                    Album = plexRow.Album,
                    Duration = plexRow.Duration,
                    Format = plexRow.Format,
                    FilePath = item.FilePath
                };
            }
            else if (_orynivoTracksByUrl.TryGetValue(item.FilePath, out var orynivoRow))
            {
                row = new ContentRow
                {
                    Title = orynivoRow.Title,
                    Artist = orynivoRow.Artist,
                    Album = orynivoRow.Album,
                    Duration = orynivoRow.Duration,
                    Format = orynivoRow.Format,
                    FilePath = item.FilePath
                };
            }
            else
            {
                row = new ContentRow
                {
                    Title = item.DisplayTitle,
                    Artist = item.Artist,
                    Album = item.Album,
                    Duration = item.Duration ?? string.Empty,
                    Format = item.Format,
                    FileName = item.FileName,
                    FilePath = item.FilePath
                };
            }

            row.Nr = (index + 1).ToString(CultureInfo.CurrentCulture);
            row.EntityType = "Queue";
            row.QueueItem = item;
            _queueRows.Add(row);
        }

        ContentDataGrid.ItemsSource = _queueRows;
        ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(_queueRows.Count);
        ClearQueueButton.IsEnabled = _queue.Count > 0;
        SaveQueueAsPlaylistButton.IsEnabled =
            _queue.Any(item => CanPersistQueuePath(item.FilePath));
        Dispatcher.UIThread.Post(UpdateNowPlayingRowHighlights, DispatcherPriority.Loaded);
    }

    private async void QueueMoveUpButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow { QueueItem: not null } row })
            return;
        var index = IndexOfQueueItem(row.QueueItem);
        if (index <= 0)
            return;
        await MoveQueueItemAsync(index, index - 1);
    }

    private async void QueueMoveDownButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow { QueueItem: not null } row })
            return;
        var index = IndexOfQueueItem(row.QueueItem);
        if (index < 0 || index + 1 >= _queue.Count)
            return;
        await MoveQueueItemAsync(index, index + 1);
    }

    private async void QueueRemoveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow { QueueItem: not null } row })
            return;
        var index = IndexOfQueueItem(row.QueueItem);
        if (index < 0)
            return;

        var currentItem = GetCurrentQueueItem();
        _queue.RemoveAt(index);
        if (ReferenceEquals(currentItem, row.QueueItem))
            _queueIndex = index - 1;
        else
            _queueIndex = IndexOfQueueItem(currentItem);
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        await RefreshActiveGaplessQueueAsync();
    }

    private async Task MoveQueueItemAsync(int oldIndex, int newIndex)
    {
        var currentItem = GetCurrentQueueItem();
        _queue.Move(oldIndex, newIndex);
        _queueIndex = IndexOfQueueItem(currentItem);
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        await RefreshActiveGaplessQueueAsync();
    }

    /// <summary>Handles the Up Next header button that clears the complete queue.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click event data.</param>
    private void ClearQueueButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ClearPlaybackQueue();
        StatusTextBlock.Text = LocalizationManager.Current.QueueCleared;
    }

    /// <summary>Clears the editable playback queue without stopping the currently playing item.</summary>
    private void ClearPlaybackQueue()
    {
        _queue.Clear();
        _queueIndex = -1;
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        ClearQueueButton.IsEnabled = false;
        SaveQueueAsPlaylistButton.IsEnabled = false;
    }

    private PlaylistItem? GetCurrentQueueItem() =>
        _queueIndex >= 0 && _queueIndex < _queue.Count ? _queue[_queueIndex] : null;

    private int IndexOfQueueItem(PlaylistItem? item)
    {
        if (item is null)
            return -1;
        for (var index = 0; index < _queue.Count; index++)
        {
            if (ReferenceEquals(_queue[index], item))
                return index;
        }
        return -1;
    }

    private void RefreshQueueRowsIfVisible()
    {
        if (_currentTopLevelTag == "Queue")
            RefreshQueueRows();
    }

    private async Task RefreshActiveGaplessQueueAsync()
    {
        if (_player is not IGaplessAudioPlayer ||
            string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        var path = _currentFilePath;
        var position = _player.Position;
        var wasPaused = _player.IsPaused;
        await StartPlaybackAsync(path);
        if (_player?.CanSeek == true && position > TimeSpan.Zero)
            await _player.SeekAsync(position);
        if (wasPaused)
            PausePlayback();
    }

    private async void SaveQueueAsPlaylistButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var paths = _queue
            .Select(item => item.FilePath)
            .Where(CanPersistQueuePath)
            .ToList();
        if (paths.Count == 0)
            return;

        var dialog = new NewPlaylistDialog();
        if (await dialog.ShowDialog<bool>(this) == false ||
            string.IsNullOrWhiteSpace(dialog.PlaylistName))
        {
            return;
        }

        var name = dialog.PlaylistName.Trim();
        using (var db = AudioDatabase.OpenDefault())
            db.CreatePlaylist(name, paths);
        LoadNavPlaylists();
        StatusTextBlock.Text = string.Format(
            LocalizationManager.Current.TracksAddedToPlaylist,
            paths.Count,
            name);
    }

    private void BuildFolderTree(List<TrackLite> tracks, OrynivoServerSettings? server = null)
    {
        _localFolderTrackItems.Clear();
        _localFolderTrackHeaders.Clear();
        _folderTreesBySource.Clear();
        FolderTreeView.Items.Clear();
        if (tracks.Count == 0) return;

        // Local trees prefer the configured library roots; remote trees derive their
        // roots automatically from the server-side source paths.
        AddFolderRootsInto(
            FolderTreeView,
            tracks,
            server is null ? _settings.LibraryPaths : null,
            server,
            autoExpandRoots: true);
    }

    /// <summary>
    /// Builds a folder tree from <paramref name="tracks"/> and appends its root directory
    /// nodes to <paramref name="parent"/>. Child nodes are materialized lazily.
    /// </summary>
    /// <param name="parent">The tree control or node that receives the root directory items.</param>
    /// <param name="tracks">The tracks the folder tree is built from.</param>
    /// <param name="preferredRoots">Preferred root directories (e.g. configured local paths), or <see langword="null"/>.</param>
    /// <param name="server">The Orynivo Server the tracks belong to, or <see langword="null"/> for the local library.</param>
    /// <param name="autoExpandRoots">Whether to expand each root one level immediately.</param>
    private void AddFolderRootsInto(
        ItemsControl parent,
        List<TrackLite> tracks,
        IReadOnlyList<string>? preferredRoots,
        OrynivoServerSettings? server,
        bool autoExpandRoots)
    {
        if (tracks.Count == 0) return;

        var tree = new FolderTree(tracks);
        _folderTreesBySource[server is null ? LocalSourceKey : GetServerSourceKey(server.Id)] = tree;
        var roots = preferredRoots?
                        .Where(p => !string.IsNullOrWhiteSpace(p) && tree.HasRoot(p))
                        .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    ?? [];
        if (roots.Count == 0)
            roots = [.. tree.AutoRoots()];

        foreach (var root in roots)
            parent.Items.Add(CreateDirItemLazy(root, tree, isRoot: true, server, autoExpandRoots));
    }

    private TreeViewItem CreateDirItemLazy(
        string dirPath,
        FolderTree tree,
        bool isRoot,
        OrynivoServerSettings? server = null,
        bool autoExpand = false)
    {
        var name = Path.GetFileName(dirPath);
        var item = new TreeViewItem
        {
            Header = isRoot ? dirPath : (string.IsNullOrEmpty(name) ? dirPath : name),
            Tag = new FolderTag(false, dirPath, dirPath, server),
            ContextFlyout = CreateSidebarMenuFlyout()
        };
        ApplyNowPlayingClass(item);
        AttachFolderPlaylistContextHandler(item);

        if (!tree.HasChildren(dirPath))
            return item;

        // Lazy population: add a placeholder so the expander shows, and materialize the real
        // child nodes only the first time the node is expanded. Building the whole subtree up
        // front froze the UI for many seconds on large (merged) libraries. Avalonia does not
        // reliably re-render a node whose children are swapped inside its Expanded pass, so the
        // real children must be inserted *before* the node expands. This mirrors the proven
        // Plex lazy-folder pattern: intercept the expand gesture, populate, then expand.
        var populated = false;
        item.Items.Add(CreateFolderPlaceholderNode());

        void Populate()
        {
            if (populated)
                return;
            populated = true;
            item.Items.Clear();
            PopulateDirNode(item, dirPath, tree, server);
        }

        item.AddHandler(
            PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>((_, e) =>
            {
                if (!e.GetCurrentPoint(item).Properties.IsLeftButtonPressed ||
                    !ReferenceEquals(FindAncestor<TreeViewItem>(e.Source as Visual), item))
                {
                    return;
                }

                var isChevronPress = FindAncestor<ToggleButton>(e.Source as Visual) is not null;
                if (isChevronPress)
                {
                    if (populated)
                        return;
                    e.Handled = true;
                    Populate();
                    item.IsExpanded = true;
                    return;
                }

                if (e.ClickCount < 2)
                    return;
                e.Handled = true;
                if (item.IsExpanded)
                {
                    item.IsExpanded = false;
                    return;
                }
                Populate();
                item.IsExpanded = true;
            }),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        // Suppress the default double-tap expand-toggle on the header; the pointer handler above
        // already performs the populate-then-expand, so the default toggle would only re-collapse.
        item.AddHandler(
            InputElement.DoubleTappedEvent,
            new EventHandler<TappedEventArgs>((_, e) =>
            {
                if (ReferenceEquals(FindAncestor<TreeViewItem>(e.Source as Visual), item) &&
                    FindAncestor<ToggleButton>(e.Source as Visual) is null)
                    e.Handled = true;
            }),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        // Keyboard-accessibility fallback: collapse, populate, then expand so the children exist
        // before the visual expansion.
        item.Expanded += (_, _) =>
        {
            if (populated)
                return;
            item.IsExpanded = false;
            Populate();
            item.IsExpanded = true;
        };

        if (autoExpand)
        {
            // Auto-expansion happens before the node is attached, so populating first and then
            // expanding renders correctly.
            Populate();
            item.IsExpanded = true;
        }

        return item;
    }

    /// <summary>Creates a lightweight, non-interactive placeholder child that makes a lazy folder node show its expander.</summary>
    /// <returns>The placeholder tree node.</returns>
    private static TreeViewItem CreateFolderPlaceholderNode() =>
        new() { Header = string.Empty, IsHitTestVisible = false, Focusable = false };

    private void PopulateDirNode(TreeViewItem parent, string dirPath, FolderTree tree, OrynivoServerSettings? server = null)
    {
        parent.Items.Clear();
        foreach (var sub in tree.SubDirs(dirPath))
            parent.Items.Add(CreateDirItemLazy(sub, tree, isRoot: false, server));
        foreach (var track in tree.Files(dirPath))
        {
            var title = new TextBlock
            {
                Text = track.DisplayName,
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var header = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 7,
                    Children = { title }
                }
            };
            var item = new TreeViewItem
            {
                Header = header,
                Tag = new FolderTag(true, track.Path, dirPath, server),
                ContextFlyout = CreateSidebarMenuFlyout()
            };
            _localFolderTrackItems[track.Path] = item;
            _localFolderTrackHeaders[track.Path] = header;
            ApplyNowPlayingClass(item);
            AttachFolderPlaylistContextHandler(item);
            parent.Items.Add(item);
        }
    }

    private void AttachFolderPlaylistContextHandler(TreeViewItem item)
    {
        item.AddHandler(
            PointerPressedEvent,
            PlaylistContextItem_OnPreviewMouseRightButtonDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private async void FolderTreeView_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FolderTreeView.SelectedItem is TreeViewItem
            {
                Tag: PlexFolderTag { IsTrack: true, Track: not null } plexTag
            } plexTreeItem)
        {
            e.Handled = true;
            var parent = ItemsControl.ItemsControlFromItemContainer(plexTreeItem);
            var siblingTracks = parent?.Items
                .OfType<TreeViewItem>()
                .Select(item => item.Tag)
                .OfType<PlexFolderTag>()
                .Where(tag => tag.IsTrack && tag.Track is not null)
                .Select(tag => tag.Track!)
                .ToList() ?? [];
            if (siblingTracks.Count == 0)
                siblingTracks.Add(plexTag.Track);

            await PlayTrackFromRowsAsync(plexTag.Track, siblingTracks);
            return;
        }

        if (FolderTreeView.SelectedItem is not TreeViewItem { Tag: FolderTag { IsFile: true } tag })
            return;
        e.Handled = true;

        var filePath   = tag.FilePath;
        var folderPath = tag.FolderPath;

        try
        {
            if (!CanPersistQueuePath(filePath))
            {
                var parent = ItemsControl.ItemsControlFromItemContainer((TreeViewItem)FolderTreeView.SelectedItem);
                var siblingPaths = parent?.Items
                    .OfType<TreeViewItem>()
                    .Select(item => item.Tag)
                    .OfType<FolderTag>()
                    .Where(folder => folder.IsFile)
                    .Select(folder => folder.FilePath)
                    .ToList() ?? [filePath];
                _queue.Clear();
                foreach (var path in siblingPaths)
                    _queue.Add(CreatePlaylistItem(path));
                _queueIndex = Math.Max(0, siblingPaths.FindIndex(path =>
                    string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase)));
                ResetQueuePlaybackState();
                PersistPlaybackQueue();
                RefreshQueueNavigationButtons();
                await StartPlaybackAsync(filePath);
                return;
            }

            var folderTracks = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTracksByDirectory(folderPath);
            });

            _queue.Clear();
            foreach (var t in folderTracks)
            _queue.Add(CreatePlaylistItem(t.Path));

            _queueIndex = 0;
            for (int i = 0; i < _queue.Count; i++)
            {
                if (string.Equals(_queue[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                { _queueIndex = i; break; }
            }
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueNavigationButtons();

            await StartPlaybackAsync(filePath);
        }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private IReadOnlyList<string> GetPathsForFolderItem(TreeViewItem treeItem)
    {
        if (treeItem.Tag is not FolderTag tag)
            return [];

        if (tag.IsFile)
            return [tag.FilePath];

        // Remote server directory (and source-group) nodes cannot be resolved through the
        // local database; their descendant stream URLs are collected from the built tree.
        // This is detected from the node itself so it works in the unified folder view too,
        // not only when a single server's folder view is active.
        var isRemoteNode = tag.Server is not null ||
            (_currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true &&
             _activeOrynivoView == "Folders");
        if (isRemoteNode)
        {
            // Child nodes are materialized lazily and may not exist yet, so collect the
            // descendant paths from the in-memory folder tree rather than the visual tree.
            if (tag.Server is { } remoteServer &&
                _folderTreesBySource.TryGetValue(GetServerSourceKey(remoteServer.Id), out var remoteTree))
            {
                return remoteTree.AllFilePathsUnder(tag.FolderPath)
                    .Where(path => !CanPersistQueuePath(path))
                    .ToList();
            }

            return CollectFolderTreePaths(treeItem)
                .Where(path => !CanPersistQueuePath(path))
                .ToList();
        }

        // A local source-group node has no directory of its own; collect its subtree.
        if (string.IsNullOrEmpty(tag.FilePath))
            return CollectFolderTreePaths(treeItem);

        try
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetTrackPathsUnderDirectory(tag.FilePath);
        }
        catch
        {
            return [];
        }
    }

    private static List<string> CollectFolderTreePaths(TreeViewItem item)
    {
        var result = new List<string>();
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            if (child.Tag is FolderTag { IsFile: true } file)
                result.Add(file.FilePath);
            else
                result.AddRange(CollectFolderTreePaths(child));
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Content-Doppelklick → Wiedergabe
    // ------------------------------------------------------------------

    private async void ContentDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (sender is not DataGrid grid ||
            !TryGetDoubleTappedRow<ContentRow>(grid, e, out var row))
            return;

        if (row.EntityType is "Track" or "OrynivoTrack" &&
            grid.ItemsSource is IEnumerable<ContentRow> rows)
        {
            // Build the queue from the grid that was actually double-clicked so
            // an album directory group ("CD1"/"CD2") queues exactly its own,
            // correctly ordered rows. Remote ("OrynivoTrack") rows must take
            // this path too; otherwise the queue would be rebuilt from the
            // hidden ContentDataGrid, whose source is the raw, ungrouped album
            // track list (interleaved across directories when disc numbers are
            // missing).
            var contextRows = IsUnfilteredTopLevelTracksView() ? [row] : rows.ToList();
            await PlayTrackFromRowsAsync(row, contextRows);
            return;
        }

        await HandleContentRowDoubleClickAsync(row);
    }

    private bool IsUnfilteredTopLevelTracksView() =>
        _currentTopLevelTag == "Tracks" &&
        _activePlaylistId is null &&
        _activeAlbumFilterId is null &&
        _activeArtistFilterId is null &&
        !HasActiveFilters;

    private async void AlbumArtworkListBox_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (AlbumArtworkListBox.SelectedItem is not ContentRow row)
            return;
        await HandleContentRowDoubleClickAsync(row);
    }

    private async void ArtistArtworkListBox_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (ArtistArtworkListBox.SelectedItem is ContentRow row)
            await HandleContentRowDoubleClickAsync(row);
    }

    private async void SearchTracksDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (!TryGetDoubleTappedRow<ContentRow>(SearchTracksDataGrid, e, out var row))
            return;

        var allRows = (SearchTracksDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        await PlayTrackFromRowsAsync(row, allRows);
    }

    private async void SearchAlbumsDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (!TryGetDoubleTappedRow<ContentRow>(SearchAlbumsDataGrid, e, out var row) ||
            row.Id is not long albumId)
            return;

        // Remote albums must open within the remote library; their IDs can collide
        // with local album IDs, so never route them through the local album view.
        if (row.EntityType == "OrynivoAlbum")
        {
            // Search has no artist context; clear any stale artist filter so the
            // album shows all of its tracks.
            _activeArtistFilterId = null;
            _activeArtistFilterName = null;
            ActivateRowOrynivoServer(row);
            await OpenOrynivoAlbumTracksAsync(row);
            return;
        }

        await ShowAlbumTracksAsync(albumId, row.Title ?? LocalizationManager.Current.Unknown);
    }

    private async void SearchArtistsDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (!TryGetDoubleTappedRow<ContentRow>(SearchArtistsDataGrid, e, out var row) ||
            row.Id is not long artistId)
            return;

        if (row.EntityType == "UnifiedArtist")
        {
            await ShowUnifiedArtistAlbumsAsync(row.Title ?? LocalizationManager.Current.Unknown);
            return;
        }

        // Remote artists must open within the remote library (IDs can collide).
        if (row.EntityType == "OrynivoArtist")
        {
            ActivateRowOrynivoServer(row);
            await OpenOrynivoArtistAlbumsAsync(artistId, row.Title);
            return;
        }

        await ShowArtistAlbumsAsync(artistId, row.Title ?? LocalizationManager.Current.Unknown);
    }

    private static bool TryGetDoubleTappedRow<T>(DataGrid grid, Avalonia.Input.TappedEventArgs e, out T row)
        where T : class
    {
        row = null!;
        if (FindAncestor<DataGridRow>(e.Source as Visual) is { DataContext: T sourceRow })
        {
            grid.SelectedItem = sourceRow;
            row = sourceRow;
            return true;
        }

        if (grid.SelectedItem is T selectedRow)
        {
            row = selectedRow;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Activates the remote server carried by a self-describing row so the shared
    /// album/artist/favorite/cover handlers operate on the row's own server even
    /// when opened outside a server view (e.g. the dashboard's mixed recent list).
    /// Rows without a server (local, or normal server-view rows) leave the ambient
    /// server unchanged.
    /// </summary>
    /// <param name="row">The row whose server context should be activated.</param>
    private void ActivateRowOrynivoServer(ContentRow row)
    {
        if (row.OrynivoServer is { } server)
            _activeOrynivoServer = server;
    }

    /// <summary>Resolves the Orynivo Server context for a remote row and activates it.</summary>
    /// <param name="row">Remote row carrying the server context when it came from a mixed view.</param>
    /// <returns>The row server, or the current ambient server as a fallback.</returns>
    private OrynivoServerSettings? ResolveRowOrynivoServer(ContentRow row)
    {
        if (row.OrynivoServer is { } server)
        {
            _activeOrynivoServer = server;
            return server;
        }

        return _activeOrynivoServer;
    }

    private async void ArtistLinkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row })
            return;

        e.Handled = true;

        if (row.EntityType == "UnifiedArtist")
        {
            await ShowUnifiedArtistAlbumsAsync(row.Title ?? LocalizationManager.Current.Unknown);
            return;
        }

        if (row.EntityType.StartsWith("Orynivo", StringComparison.Ordinal))
        {
            ActivateRowOrynivoServer(row);
            var remoteArtistId = row.EntityType == "OrynivoArtist" ? row.Id : row.ArtistId;
            if (remoteArtistId is long remoteId)
                await OpenOrynivoArtistAlbumsAsync(remoteId, row.EntityType == "OrynivoArtist" ? row.Title : row.Artist);
            return;
        }

        var artistId = row.ArtistId;
        if (artistId is null)
        {
            using var db = AudioDatabase.OpenDefault();
            if (row.EntityType == "Artist")
                artistId = row.Id;
            else if ((row.AlbumId ?? (row.EntityType == "Album" ? row.Id : null)) is long albumId)
                artistId = db.GetAlbumArtistId(albumId);
            else if (!string.IsNullOrWhiteSpace(row.FilePath))
                artistId = db.GetTrackNavigationIds(row.FilePath).ArtistId;
            row.ArtistId = artistId;
        }

        if (artistId is not long id)
            return;

        var artistName = row.EntityType == "Artist" ? row.Title : row.Artist;
        await ShowArtistAlbumsAsync(id, artistName ?? LocalizationManager.Current.Unknown);
    }

    private async void AlbumLinkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row })
            return;

        e.Handled = true;

        if (row.EntityType.StartsWith("Orynivo", StringComparison.Ordinal))
        {
            ActivateRowOrynivoServer(row);
            var remoteAlbumId = row.EntityType == "OrynivoAlbum" ? row.Id : row.AlbumId;
            if (remoteAlbumId is long remoteId)
            {
                if (row.EntityType == "OrynivoAlbum")
                    await OpenOrynivoAlbumTracksAsync(row);
                else
                    await OpenOrynivoAlbumTracksAsync(remoteId, row.Album, row.Artist);
            }
            return;
        }

        var albumId = row.AlbumId ?? (row.EntityType == "Album" ? row.Id : null);
        if (albumId is null && !string.IsNullOrWhiteSpace(row.FilePath))
        {
            using var db = AudioDatabase.OpenDefault();
            albumId = db.GetTrackNavigationIds(row.FilePath).AlbumId;
            row.AlbumId = albumId;
        }

        if (albumId is not long id)
            return;

        var albumTitle = row.EntityType == "Album" ? row.Title : row.Album;
        await ShowAlbumTracksAsync(id, albumTitle ?? LocalizationManager.Current.Unknown);
    }

    private async void NowPlayingArtistButton_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await OpenNowPlayingArtistAsync();
    }

    private async void NowPlayingAlbumButton_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        await OpenNowPlayingAlbumAsync();
    }

    private async void NowPlayingCoverOpenAlbumMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await OpenNowPlayingAlbumAsync();
    }

    private async void NowPlayingCoverOpenArtistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await OpenNowPlayingArtistAsync();
    }

    private async void NowPlayingCoverSearchMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await SearchNowPlayingCoverAsync();
    }

    private void NowPlayingCoverFavoriteMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        NowPlayingFavoriteButton_OnClick(sender, e);
    }

    private async Task OpenNowPlayingAlbumAsync()
    {
        if (_currentOrynivoTrackRow is { OrynivoServer: { } server, AlbumId: long remoteAlbumId })
        {
            _activeOrynivoServer = server;
            await OpenOrynivoAlbumTracksAsync(
                remoteAlbumId,
                _currentOrynivoTrackRow.Album ?? _currentAlbumTitle,
                _currentOrynivoTrackRow.Artist);
            return;
        }

        if (_currentAlbumId is long albumId)
            await ShowAlbumTracksAsync(
                albumId,
                _currentAlbumTitle ?? LocalizationManager.Current.Unknown);
    }

    private async Task OpenNowPlayingArtistAsync()
    {
        if (_currentOrynivoTrackRow is { OrynivoServer: { } server, ArtistId: long remoteArtistId })
        {
            _activeOrynivoServer = server;
            await OpenOrynivoArtistAlbumsAsync(remoteArtistId, _currentArtistName);
            return;
        }

        if (_currentArtistId is long artistId)
            await ShowArtistAlbumsAsync(
                artistId,
                _currentArtistName ?? LocalizationManager.Current.Unknown);
    }

    private async Task SearchNowPlayingCoverAsync()
    {
        if (_currentOrynivoTrackRow is { OrynivoServer: { } server, AlbumId: long remoteAlbumId })
        {
            var row = new ContentRow
            {
                Id = remoteAlbumId,
                Title = _currentOrynivoTrackRow.Album ?? _currentAlbumTitle ?? LocalizationManager.Current.Unknown,
                Artist = _currentOrynivoTrackRow.Artist,
                AlbumArtist = _currentOrynivoTrackRow.AlbumArtist,
                EntityType = "OrynivoAlbum",
                ExternalId = remoteAlbumId.ToString(CultureInfo.InvariantCulture),
                OrynivoServer = server
            };
            await OpenOrynivoAlbumCoverSearchAsync(server, remoteAlbumId, row);
            NowPlayingArtworkImage.Source = row.Thumbnail ?? row.Artwork ?? NowPlayingArtworkImage.Source;
            LyricsBackgroundImage.Source = row.Artwork ?? row.Thumbnail ?? LyricsBackgroundImage.Source;
            return;
        }

        if (_currentAlbumId is not long albumId)
            return;

        var localRow = new ContentRow
        {
            Id = albumId,
            Title = _currentAlbumTitle ?? LocalizationManager.Current.Unknown,
            Artist = _currentArtistName,
            EntityType = "Album"
        };
        await OpenCoverSearchAsync(localRow);
        NowPlayingArtworkImage.Source = localRow.Thumbnail ?? localRow.Artwork ?? NowPlayingArtworkImage.Source;
        LyricsBackgroundImage.Source = localRow.Artwork ?? localRow.Thumbnail ?? LyricsBackgroundImage.Source;
    }

    private void SetNowPlayingAlbum(string? albumTitle, long? albumId, bool canNavigate)
    {
        _currentAlbumId = albumId;
        _currentAlbumTitle = string.IsNullOrWhiteSpace(albumTitle) ? null : albumTitle;
        NowPlayingAlbumBlock.Text = _currentAlbumTitle ?? string.Empty;
        NowPlayingAlbumButton.IsVisible = !string.IsNullOrWhiteSpace(_currentAlbumTitle);
        NowPlayingAlbumButton.IsEnabled = canNavigate && albumId is not null;
    }

    private void ClearNowPlayingAlbum() => SetNowPlayingAlbum(null, null, false);

    private async Task HandleContentRowDoubleClickAsync(ContentRow row)
    {
        if (row.EntityType == "Queue" && row.QueueItem is not null)
        {
            var queueIndex = IndexOfQueueItem(row.QueueItem);
            if (queueIndex < 0)
                return;
            _queueIndex = queueIndex;
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueNavigationButtons();
            try { await StartPlaybackAsync(row.FilePath); }
            catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
            catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
            return;
        }

        if (row.EntityType is "PlexArtist" or "PlexAlbum")
        {
            await ShowPlexChildrenAsync(row);
            return;
        }

        if (row.EntityType == "UnifiedArtist")
        {
            await ShowUnifiedArtistAlbumsAsync(row.Title ?? LocalizationManager.Current.Unknown);
            return;
        }

        if (row.EntityType == "OrynivoArtist" && row.Id is long orynivoArtistId)
        {
            // In the unified Artists view no server is "active"; navigate within the
            // row's own server (IDs can collide between local and remote libraries).
            ActivateRowOrynivoServer(row);
            await OpenOrynivoArtistAlbumsAsync(orynivoArtistId, row.Title);
            return;
        }

        if (row.EntityType == "OrynivoAlbum" && row.Id is long orynivoAlbumId)
        {
            ActivateRowOrynivoServer(row);
            await OpenOrynivoAlbumTracksAsync(row);
            return;
        }

        if (row.EntityType == "Artist" && row.Id is long artistId)
        {
            await ShowArtistAlbumsAsync(artistId, row.Title ?? "(Unbekannt)");
            return;
        }

        if (AlbumViewModeBorder.IsVisible &&
            row.EntityType is "Album" or null &&
            (row.AlbumId ?? row.Id) is long albumId)
        {
            await ShowAlbumTracksAsync(albumId, row.Title ?? "(Unbekannt)");
            return;
        }

        if (string.IsNullOrEmpty(row.FilePath))
            return;

        var allRows = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        await PlayTrackFromRowsAsync(row, allRows);
    }

    /// <summary>Opens every matching local and Orynivo Server album for a remote artist name.</summary>
    /// <param name="artistId">Remote server artist identifier.</param>
    /// <param name="title">Artist display name for the header.</param>
    private Task OpenOrynivoArtistAlbumsAsync(long artistId, string? title) =>
        ShowUnifiedArtistAlbumsAsync(title ?? LocalizationManager.Current.Unknown);

    /// <summary>Opens the tracks of a remote server album, pushing navigation state.</summary>
    /// <param name="albumId">Remote server album identifier.</param>
    /// <param name="title">Album title for the header.</param>
    /// <param name="artist">Album artist for the header, or <see langword="null"/>.</param>
    private async Task OpenOrynivoAlbumTracksAsync(long albumId, string? title, string? artist)
    {
        PushCurrentNavigationState();
        _orynivoNavigationStack.Push((_activeOrynivoView, null, null));
        BackButton.IsVisible = true;
        if (_activeOrynivoServer is null)
            return;

        var provider = CreateOrynivoCatalogProvider(_activeOrynivoServer);
        var album = await provider.GetAlbumAsync(albumId, includeArtwork: true)
                    ?? new LibraryCatalogAlbum(
                        LibraryCatalogSource.OrynivoServer,
                        albumId,
                        title ?? LocalizationManager.Current.Unknown,
                        artist,
                        null,
                        null,
                        null,
                        IsOrynivoFavorite(_activeOrynivoServer, "Album", albumId));
        await ShowProviderAlbumTracksAsync(provider, album, _activeArtistFilterId, _activeArtistFilterName);
    }

    private async Task OpenOrynivoAlbumTracksAsync(ContentRow row)
    {
        if (row.Id is not long albumId)
            return;

        PushCurrentNavigationState();
        _orynivoNavigationStack.Push((_activeOrynivoView, null, null));
        BackButton.IsVisible = true;
        if (_activeOrynivoServer is null)
            return;

        var provider = CreateOrynivoCatalogProvider(_activeOrynivoServer);
        var album = await provider.GetAlbumAsync(albumId, includeArtwork: true)
                    ?? new LibraryCatalogAlbum(
                        LibraryCatalogSource.OrynivoServer,
                        albumId,
                        row.Title ?? LocalizationManager.Current.Unknown,
                        row.Artist,
                        int.TryParse(row.Year, NumberStyles.Integer, CultureInfo.CurrentCulture, out var year) ? year : null,
                        row.ArtworkPath,
                        row.ThumbnailPath,
                        row.IsFavorite,
                        row.ArtistId);
        await ShowProviderAlbumTracksAsync(provider, album, _activeArtistFilterId, _activeArtistFilterName);
    }

    private async Task RestoreOrynivoAlbumTracksAsync(
        string? navigationTag,
        long albumId,
        string? albumTitle,
        long? artistFilterId,
        string? artistFilterName,
        double? verticalOffset)
    {
        if (string.IsNullOrWhiteSpace(navigationTag))
            return;

        SelectNavigationItem(navigationTag);
        await ShowTopLevelViewAsync(navigationTag);
        if (_activeOrynivoServer is null)
            return;

        var provider = CreateOrynivoCatalogProvider(_activeOrynivoServer);
        var album = await provider.GetAlbumAsync(albumId, includeArtwork: true)
                    ?? new LibraryCatalogAlbum(
                        LibraryCatalogSource.OrynivoServer,
                        albumId,
                        albumTitle ?? LocalizationManager.Current.Unknown,
                        null,
                        null,
                        null,
                        null,
                        IsOrynivoFavorite(_activeOrynivoServer, "Album", albumId));
        await ShowProviderAlbumTracksAsync(provider, album, artistFilterId, artistFilterName);
        RestoreSelectionFromCurrentItems(null, verticalOffset);
    }

    /// <summary>Restores the filtered album list for a remote server artist.</summary>
    /// <param name="navigationTag">Remote server navigation tag to restore.</param>
    /// <param name="artistId">Remote server artist identifier.</param>
    /// <param name="artistName">Artist display name for the header.</param>
    /// <param name="selectedAlbumId">Album identifier to reselect.</param>
    /// <param name="verticalOffset">Optional vertical scroll offset to restore.</param>
    private async Task RestoreOrynivoArtistAlbumsAsync(
        string? navigationTag,
        long artistId,
        string? artistName,
        long? selectedAlbumId,
        double? verticalOffset)
    {
        if (string.IsNullOrWhiteSpace(navigationTag))
            return;

        SelectNavigationItem(navigationTag);
        await ShowTopLevelViewAsync(navigationTag);
        if (_activeOrynivoServer is null)
            return;

        _currentTopLevelTag = navigationTag;
        _activeOrynivoView = "Albums";
        _activeArtistFilterId = artistId;
        _activeArtistFilterName = artistName;
        ContentTitleTextBlock.Text = $"{_activeOrynivoServer.Name} · {artistName}";
        await LoadOrynivoViewAsync(filterArtistId: artistId);
        RestoreSelectionFromCurrentItems(selectedAlbumId, verticalOffset);
    }

    private async Task PlayTrackFromRowsAsync(ContentRow row, List<ContentRow> allRows)
    {
        if (string.IsNullOrEmpty(row.FilePath))
            return;

        _queue.Clear();
        foreach (var r in allRows.Where(r => !string.IsNullOrEmpty(r.FilePath)))
            _queue.Add(ToPlaylistItem(r));

        _queueIndex = _queue.IndexOf(_queue.FirstOrDefault(p => p.FilePath == row.FilePath) ?? _queue[0]);
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(row.FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private static PlaylistItem ToPlaylistItem(ContentRow row) =>
        new(
            row.FilePath,
            string.IsNullOrWhiteSpace(row.Title) ? null : row.Title,
            string.IsNullOrWhiteSpace(row.Artist) ? null : row.Artist,
            string.IsNullOrWhiteSpace(row.Album) ? null : row.Album,
            string.IsNullOrWhiteSpace(row.Duration) ? null : row.Duration,
            string.IsNullOrWhiteSpace(row.Format) ? null : row.Format,
            row.KnownDuration);

    private PlaylistItem CreatePlaylistItem(string path)
    {
        if (TryResolveOrynivoPlaylistReferenceRow(path, out var referencedRow))
            return ToPlaylistItem(referencedRow);
        if (_orynivoTracksByUrl.TryGetValue(path, out var orynivoRow))
            return ToPlaylistItem(orynivoRow);
        if (_plexTracksByUrl.TryGetValue(path, out var plexRow))
            return ToPlaylistItem(plexRow);
        return new PlaylistItem(path);
    }

    private PlaylistItem? GetPlaylistMetadata(string path)
    {
        var queueItem = _queue.FirstOrDefault(item =>
            string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (queueItem is not null &&
            (!string.IsNullOrWhiteSpace(queueItem.Title) ||
             !string.IsNullOrWhiteSpace(queueItem.Artist) ||
             !string.IsNullOrWhiteSpace(queueItem.Album)))
        {
            return queueItem;
        }

        if (_orynivoTracksByUrl.TryGetValue(path, out var orynivoRow))
            return ToPlaylistItem(orynivoRow);
        if (TryResolveOrynivoPlaylistReferenceRow(path, out var referencedRow))
            return ToPlaylistItem(referencedRow);
        if (_plexTracksByUrl.TryGetValue(path, out var plexRow))
            return ToPlaylistItem(plexRow);
        return null;
    }

    private bool TryResolveOrynivoPlaylistReferenceRow(string path, out ContentRow row)
    {
        row = null!;
        if (!TryResolveOrynivoPlaylistReference(path, out var server, out var trackId))
            return false;

        try
        {
            var provider = CreateOrynivoCatalogProvider(server);
            var remoteTrack = provider.GetTracksByIdsAsync([trackId])
                .GetAwaiter()
                .GetResult()
                .FirstOrDefault();
            if (remoteTrack is null)
                return false;
            row = ToCatalogTrackContentRow(remoteTrack, server);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ShowAlbumTracksAsync(long albumId, string albumTitle)
    {
        PushCurrentNavigationState();
        _activeAlbumFilterId = albumId;
        _activeAlbumFilterTitle = albumTitle;
        _activeAlbumCatalogProvider = null;
        _activeCatalogAlbum = null;
        _showAllAlbumTracks = false;
        UpdateLibraryIntroCard(null);
        UpdateEntityFavoritesFilterToggle(null);
        _updatingAlbumTrackScope = true;
        ShowAllAlbumTracksCheckBox.IsChecked = false;
        ShowAllAlbumTracksCheckBox.IsVisible = _activeArtistFilterId.HasValue;
        _updatingAlbumTrackScope = false;
        ContentTitleTextBlock.Text = $"{LocalizationManager.Current.Tracks} · {albumTitle}";
        AlbumViewModeBorder.IsVisible = false;
        ContentDataGrid.IsVisible = true;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        FolderTreeView.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        UpdateAlphabetIndex(null, false);

        await ReloadVisibleAlbumTracksAsync();
        BackButton.IsVisible = true;
    }

    private async Task ReloadVisibleAlbumTracksAsync()
    {
        if (_activeAlbumFilterId is not long albumId)
            return;

        ShowContentLoadingSkeleton();
        try
        {
            var artistId = _showAllAlbumTracks ? null : _activeArtistFilterId;
            var result = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                var tracks = db.GetTrackListByAlbum(albumId, artistId);
                return (
                    Album: db.GetAlbumById(albumId),
                    Tracks: tracks,
                    Directories: db.GetAlbumTrackDirectories(albumId, artistId));
            });
            var groupedRows = result.Tracks
                .GroupBy(
                    track => (
                        Directory: NormalizeAlbumGroupValue(
                            result.Directories.TryGetValue(track.Id, out var directory)
                                ? directory
                                : Path.GetDirectoryName(track.Path)),
                        Album: NormalizeAlbumGroupValue(track.Album)))
                .Select(group =>
                {
                    var first = group.First();
                    var albumArtists = group
                        .Select(track => ArtistNameNormalizer.NormalizeDisplayName(
                            track.AlbumArtist))
                        .Where(artist => artist.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    var primaryArtists = group
                        .Select(track => ArtistNameNormalizer.NormalizeDisplayName(track.Artist))
                        .Where(artist => artist.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    return new AlbumTrackGroup(
                        result.Directories.TryGetValue(first.Id, out var directory)
                            ? directory
                            : Path.GetDirectoryName(first.Path) ?? string.Empty,
                        string.IsNullOrWhiteSpace(first.Album)
                            ? LocalizationManager.Current.Unknown
                            : first.Album.Trim(),
                        albumArtists.Count == 1
                            ? albumArtists[0]
                            : albumArtists.Count == 0 && primaryArtists.Count == 1
                                ? primaryArtists[0]
                                : null,
                        first.Year?.ToString(CultureInfo.CurrentCulture),
                        group.Select(ToTrackContentRow).ToList());
                })
                .OrderBy(group => group.Directory, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Album, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(group => group.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var rows = groupedRows.SelectMany(group => group.Rows).ToList();
            HideAlbumFolderGroups();
            ContentDataGrid.IsVisible = true;
            ApplyColumns("Tracks");
            ContentDataGrid.ItemsSource = rows;
            ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
            UpdateAlphabetIndex(null, false);
            ApplyAlbumDetailHeader(result.Album);
            if (result.Album is not null && groupedRows.Count > 1)
                ShowAlbumFolderGroups(groupedRows);
        }
        finally
        {
            HideContentLoadingSkeleton();
            FadeInVisibleContentSurface();
        }
    }

    private async Task ShowProviderAlbumTracksAsync(
        ILibraryCatalogProvider provider,
        LibraryCatalogAlbum album,
        long? artistFilterId = null,
        string? artistFilterName = null)
    {
        _activeAlbumFilterId = album.Id;
        _activeAlbumFilterTitle = album.Title;
        _activeArtistFilterId = artistFilterId;
        _activeArtistFilterName = artistFilterName;
        _activeAlbumCatalogProvider = provider;
        _activeCatalogAlbum = album;
        _showAllAlbumTracks = false;
        UpdateLibraryIntroCard(null);
        UpdateEntityFavoritesFilterToggle(null);
        _updatingAlbumTrackScope = true;
        ShowAllAlbumTracksCheckBox.IsChecked = false;
        ShowAllAlbumTracksCheckBox.IsVisible = artistFilterId.HasValue;
        _updatingAlbumTrackScope = false;
        ContentTitleTextBlock.Text = $"{LocalizationManager.Current.Tracks} · {album.Title}";
        AlbumViewModeBorder.IsVisible = false;
        ContentDataGrid.IsVisible = true;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        FolderTreeView.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        DashboardScrollViewer.IsVisible = false;
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        UpdateAlphabetIndex(null, false);

        await ReloadVisibleProviderAlbumTracksAsync();
    }

    private async Task ReloadVisibleProviderAlbumTracksAsync()
    {
        if (_activeAlbumCatalogProvider is not { } provider ||
            _activeCatalogAlbum is not { } album)
        {
            return;
        }

        ShowContentLoadingSkeleton();
        try
        {
            var artistId = _showAllAlbumTracks ? null : _activeArtistFilterId;
            var catalogTracks = await provider.GetTracksByAlbumAsync(album.Id, artistId);
            var rows = catalogTracks
                .Select(track => ToCatalogTrackContentRow(track, album.Source == LibraryCatalogSource.OrynivoServer ? _activeOrynivoServer : null))
                .ToList();
            var groupedRows = rows
                .GroupBy(row => (
                    Directory: NormalizeAlbumGroupValue(
                        Path.GetDirectoryName(row.SourcePath ?? row.FilePath)),
                    Album: NormalizeAlbumGroupValue(row.Album)))
                .Select(group =>
                {
                    var first = group.First();
                    var albumArtists = group
                        .Select(track => ArtistNameNormalizer.NormalizeDisplayName(track.AlbumArtist))
                        .Where(artist => artist.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    var primaryArtists = group
                        .Select(track => ArtistNameNormalizer.NormalizeDisplayName(track.Artist))
                        .Where(artist => artist.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    return new AlbumTrackGroup(
                        Path.GetDirectoryName(first.SourcePath ?? first.FilePath) ?? string.Empty,
                        string.IsNullOrWhiteSpace(first.Album)
                            ? album.Title
                            : first.Album.Trim(),
                        albumArtists.Count == 1
                            ? albumArtists[0]
                            : albumArtists.Count == 0 && primaryArtists.Count == 1
                                ? primaryArtists[0]
                                : null,
                        first.Year,
                        group.ToList());
                })
                .OrderBy(group => group.Directory, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Album, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(group => group.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            HideAlbumFolderGroups();
            ContentDataGrid.IsVisible = true;
            ApplyColumns("Tracks");
            ContentDataGrid.ItemsSource = rows;
            ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
            UpdateAlphabetIndex(null, false);
            ApplyAlbumDetailHeader(album);
            if (groupedRows.Count > 1)
                ShowAlbumFolderGroups(groupedRows);
        }
        finally
        {
            HideContentLoadingSkeleton();
            FadeInVisibleContentSurface();
        }
    }

    private static string NormalizeAlbumGroupValue(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    private async void ShowAllAlbumTracksCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingAlbumTrackScope || _activeAlbumFilterId is null)
            return;

        _showAllAlbumTracks = ShowAllAlbumTracksCheckBox.IsChecked == true;
        if (_activeAlbumCatalogProvider is not null)
            await ReloadVisibleProviderAlbumTracksAsync();
        else
            await ReloadVisibleAlbumTracksAsync();
    }

    private void ApplyAlbumDetailHeader(AlbumInfo? album)
    {
        if (album is null)
        {
            HideAlbumDetailHeader();
            return;
        }

        var row = CreateAlbumDetailRow(album);
        EnsureArtworkHydrated(row);
        AlbumDetailHeader.DataContext = row;
        AlbumDetailHeader.IsVisible = true;
        AlbumSaveAsPlaylistButton.IsVisible = true;
    }

    private void ApplyAlbumDetailHeader(LibraryCatalogAlbum album)
    {
        var row = CreateAlbumDetailRow(album);
        EnsureArtworkHydrated(row);
        AlbumDetailHeader.DataContext = row;
        AlbumDetailHeader.IsVisible = true;
        AlbumSaveAsPlaylistButton.IsVisible = album.Source == LibraryCatalogSource.Local;
    }

    private static ContentRow CreateAlbumDetailRow(AlbumInfo album) =>
        new()
        {
            Id = album.Id,
            AlbumId = album.Id,
            Title = string.IsNullOrWhiteSpace(album.Album)
                ? LocalizationManager.Current.Unknown
                : album.Album,
            ArtistId = album.ArtistId,
            Artist = string.IsNullOrWhiteSpace(album.DisplayArtist)
                ? null
                : album.DisplayArtist,
            Year = album.Year?.ToString(),
            ArtworkPath = album.ArtworkPath,
            ThumbnailPath = album.ThumbnailPath,
            IsFavorite = album.IsFavorite,
            EntityType = "Album",
            FilePath = ""
        };

    private static ContentRow CreateAlbumDetailRow(LibraryCatalogAlbum album) =>
        new()
        {
            Id = album.Id,
            AlbumId = album.Id,
            ArtistId = album.ArtistId,
            Title = string.IsNullOrWhiteSpace(album.Title)
                ? LocalizationManager.Current.Unknown
                : album.Title,
            Artist = string.IsNullOrWhiteSpace(album.DisplayArtist)
                ? null
                : album.DisplayArtist,
            Year = album.Year?.ToString(CultureInfo.CurrentCulture),
            ArtworkPath = album.ArtworkPath,
            ThumbnailPath = album.ThumbnailPath,
            IsFavorite = album.IsFavorite,
            EntityType = album.Source == LibraryCatalogSource.OrynivoServer ? "OrynivoAlbum" : "Album",
            ExternalId = album.Source == LibraryCatalogSource.OrynivoServer
                ? album.Id.ToString(CultureInfo.InvariantCulture)
                : null,
            FilePath = ""
        };

    private void HideAlbumDetailHeader()
    {
        AlbumDetailHeader.IsVisible = false;
        AlbumDetailHeader.DataContext = null;
        ShowAllAlbumTracksCheckBox.IsVisible = false;
        HideAlbumFolderGroups();
    }

    private void ShowAlbumFolderGroups(IReadOnlyList<AlbumTrackGroup> groups)
    {
        AlbumFolderGroupsPanel.Children.Clear();

        foreach (var group in groups)
        {
            var groupPanel = new StackPanel { Spacing = 10 };
            groupPanel.Children.Add(CreateAlbumFolderGroupHeader(
                group));

            var grid = new DataGrid
            {
                ItemsSource = group.Rows,
                // The horizontal grid line adds 1px to each row's pitch (40 -> 41), so the
                // table must be sized with the real pitch plus a small buffer; otherwise the
                // grid is a few pixels too short, shows an internal scrollbar, and the mouse
                // wheel scrolls the table content instead of the whole album page.
                Height = 46 + (group.Rows.Count * 41),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = FindResource<IBrush>("AppContentBrush"),
                BorderThickness = new Thickness(0),
                RowHeight = 40,
                ColumnHeaderHeight = 44,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = FindResource<IBrush>("AppGridLineBrush"),
                AutoGenerateColumns = false,
                CanUserResizeColumns = true,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                FontSize = 12
            };
            ScrollViewer.SetBringIntoViewOnFocusChange(grid, false);
            grid.LoadingRow += ContentDataGrid_OnLoadingRow;
            grid.DoubleTapped += ContentDataGrid_OnMouseDoubleClick;
            ApplyColumns("Tracks", grid, captureCurrentWidths: false);
            DataGridColumnChooser.Attach(
                grid,
                GetContentColumnWidthKey("Tracks"),
                _settings);
            grid.AddHandler(
                PointerReleasedEvent,
                AlbumFolderDataGrid_OnPointerReleased,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _albumFolderGroupGrids.Add(grid);
            groupPanel.Children.Add(grid);
            AlbumFolderGroupsPanel.Children.Add(groupPanel);
        }

        ContentDataGrid.IsVisible = false;
        AlbumFolderGroupsScrollViewer.IsVisible = true;
    }

    private Border CreateAlbumFolderGroupHeader(AlbumTrackGroup group)
    {
        var representativeTrack = group.Rows[0];
        var albumRow = new ContentRow
        {
            Id = representativeTrack.Id,
            AlbumId = representativeTrack.AlbumId,
            ArtistId = representativeTrack.ArtistId,
            Title = group.Album,
            Artist = group.Artist,
            Album = group.Album,
            Year = group.Year,
            EntityType = "AlbumGroup",
            FilePath = representativeTrack.FilePath
        };
        var titleButton = new Button
        {
            Content = albumRow.Title,
            Tag = albumRow,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Theme = FindResource<ControlTheme>("EntityLinkButtonTheme")
        };
        titleButton.Click += AlbumLinkButton_OnClick;

        var artistButton = new Button
        {
            Content = albumRow.Artist,
            Tag = albumRow,
            FontSize = 14,
            Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
            Theme = FindResource<ControlTheme>("EntityLinkButtonTheme")
        };
        artistButton.Click += ArtistLinkButton_OnClick;

        var metadataPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        if (!string.IsNullOrWhiteSpace(group.Artist))
            metadataPanel.Children.Add(artistButton);
        if (!string.IsNullOrWhiteSpace(albumRow.Year))
        {
            metadataPanel.Children.Add(new TextBlock
            {
                Text = albumRow.Year,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = FindResource<IBrush>("AppMutedTextBrush")
            });
        }
        metadataPanel.Children.Add(new TextBlock
        {
            Text = LocalizationManager.FormatTrackCount(group.Rows.Count),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FindResource<IBrush>("AppMutedTextBrush")
        });

        var content = new StackPanel { Spacing = 5 };
        content.Children.Add(titleButton);
        content.Children.Add(metadataPanel);
        content.Children.Add(new TextBlock
        {
            Text = $"{LocalizationManager.Current.AlbumPath}: {group.Directory}",
            FontSize = 12,
            Foreground = FindResource<IBrush>("AppMutedTextBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        return new Border
        {
            Padding = new Thickness(16, 12),
            Background = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0, 16, 0, 16),
            Child = content
        };
    }

    private void HideAlbumFolderGroups()
    {
        AlbumFolderGroupsScrollViewer.IsVisible = false;
        AlbumFolderGroupsPanel.Children.Clear();
        _albumFolderGroupGrids.Clear();
    }

    private void AlbumFolderDataGrid_OnPointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        var widthKey = GetContentColumnWidthKey("Tracks");
        CaptureColumnWidths(widthKey, grid);
        foreach (var otherGrid in _albumFolderGroupGrids.Where(other => !ReferenceEquals(other, grid)))
            RestoreColumnWidths(widthKey, otherGrid);
    }

    private async Task ReloadAlbumDetailHeaderAsync(long albumId)
    {
        var album = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetAlbumById(albumId);
        });
        ApplyAlbumDetailHeader(album);
    }

    private void AlbumDetailFavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow { Id: long albumId } row })
            return;

        row.IsFavorite = !row.IsFavorite;
        if (row.EntityType == "OrynivoAlbum" &&
            _activeOrynivoServer is not null)
        {
            var key = GetOrynivoFavoriteKey(_activeOrynivoServer.Id, "Album", albumId);
            if (row.IsFavorite)
                _settings.OrynivoServerFavorites.Add(key);
            else
                _settings.OrynivoServerFavorites.Remove(key);
            _settingsStore.Save(_settings);
            e.Handled = true;
            return;
        }

        using var db = AudioDatabase.OpenDefault();
        db.SetAlbumFavorite(albumId, row.IsFavorite);
        e.Handled = true;
    }

    private void AlbumSaveAsPlaylistButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var paths = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)
            ?.Where(row => !string.IsNullOrWhiteSpace(row.FilePath))
            .Select(GetPersistablePlaylistPath)
            .ToList() ?? [];
        if (paths.Count == 0)
            return;

        button.ContextFlyout = BuildPlaylistContextFlyout(paths);
        e.Handled = true;
        button.ContextFlyout.ShowAt(button);
    }

    /// <summary>Opens every matching local and Orynivo Server album for a local artist name.</summary>
    /// <param name="artistId">Local artist identifier retained by the calling navigation surface.</param>
    /// <param name="artistName">Artist display name used for cross-library identity matching.</param>
    /// <returns>A task representing the unified navigation operation.</returns>
    private Task ShowArtistAlbumsAsync(long artistId, string artistName) =>
        ShowUnifiedArtistAlbumsAsync(artistName);

    private async Task ShowUnifiedArtistAlbumsAsync(string artistName)
    {
        PushCurrentNavigationState();
        _currentTopLevelTag = "Albums";
        _activeArtistFilterId = null;
        _activeArtistFilterName = artistName;
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        ContentTitleTextBlock.Text = $"{LocalizationManager.Current.Albums} · {artistName}";
        UpdateLibraryIntroCard(null);
        UpdateEntityFavoritesFilterToggle("Albums");
        AlbumViewModeBorder.IsVisible = true;
        SetViewModeButtons(_showAlbumArtworkView);
        ContentDataGrid.IsVisible = !_showAlbumArtworkView;
        AlbumArtworkListBox.IsVisible = _showAlbumArtworkView;
        ArtistArtworkListBox.IsVisible = false;
        FolderTreeView.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        HideAlbumDetailHeader();

        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(artistName);
        var rows = new List<ContentRow>();
        var localArtists = await _localCatalogProvider.GetArtistsAsync();
        foreach (var artist in localArtists.Where(candidate =>
                     ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
        {
            var albums = await _localCatalogProvider.GetAlbumsByArtistAsync(artist.Id, _showAlbumArtworkView);
            rows.AddRange(albums
                .Where(album => !_albumFavoritesOnly || album.IsFavorite)
                .Select(album => ToCatalogAlbumContentRow(album)));
        }

        foreach (var server in _settings.OrynivoServers ?? [])
        {
            try
            {
                var provider = CreateOrynivoCatalogProvider(server);
                var artists = await provider.GetArtistsAsync();
                foreach (var artist in artists.Where(candidate =>
                             ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
                {
                    var albums = await provider.GetAlbumsByArtistAsync(artist.Id, _showAlbumArtworkView);
                    rows.AddRange(albums
                        .Where(album => !_albumFavoritesOnly || album.IsFavorite)
                        .Select(album => ToCatalogAlbumContentRow(album, server)));
                }
            }
            catch
            {
                // An unavailable server must not hide albums from the other libraries.
            }
        }

        rows = SortUnifiedRows(rows);
        ApplyColumns("Albums");
        ContentDataGrid.ItemsSource = rows;
        BindArtworkRows("Albums", rows);
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
        BackButton.IsVisible = true;
    }

    private async void SearchCoverButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row })
            return;

        ActivateRowOrynivoServer(row);
        if (row.EntityType == "OrynivoAlbum" &&
            _activeOrynivoServer is not null &&
            long.TryParse(row.ExternalId, out var remoteAlbumId))
        {
            await OpenOrynivoAlbumCoverSearchAsync(_activeOrynivoServer, remoteAlbumId, row);
            return;
        }

        await OpenCoverSearchAsync(row);
    }

    private async void DeleteCoverMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow { Id: long albumId } row })
            return;

        if (row.EntityType == "OrynivoAlbum")
            return;

        var verticalOffset = CaptureCurrentVerticalOffset();
        using (var db = AudioDatabase.OpenDefault())
            db.ClearArtworkFromAlbum(albumId);

        // On the dashboard, clear the bound card in place instead of rebuilding
        // the hidden Albums list.
        if (DashboardScrollViewer.IsVisible)
        {
            row.ArtworkPath = null;
            row.ThumbnailPath = null;
            UpdateRowArtworkFromBytes(row, null);
            return;
        }
        if (_activeAlbumFilterId == albumId)
            await ReloadAlbumDetailHeaderAsync(albumId);
        else
            await ReloadAlbumRowsAsync(albumId, verticalOffset);
    }

    private async void ReassignCoverMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow row })
            return;

        ActivateRowOrynivoServer(row);
        if (row.EntityType == "OrynivoAlbum" &&
            _activeOrynivoServer is not null &&
            long.TryParse(row.ExternalId, out var remoteAlbumId))
        {
            await OpenOrynivoAlbumCoverSearchAsync(_activeOrynivoServer, remoteAlbumId, row);
            return;
        }

        await OpenCoverSearchAsync(row);
    }

    private async void OrynivoArtworkMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow row } ||
            ResolveRowOrynivoServer(row) is not { } server ||
            !long.TryParse(row.ExternalId, out var entityId))
        {
            return;
        }

        if (row.EntityType == "OrynivoAlbum")
        {
            await OpenOrynivoAlbumCoverSearchAsync(server, entityId, row);
            return;
        }

        if (row.EntityType == "OrynivoArtist")
            await OpenOrynivoArtistImageSearchAsync(server, entityId, row);
    }

    private async void OrynivoArtistInfoRefreshMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow row } ||
            row.EntityType != "OrynivoArtist" ||
            ResolveRowOrynivoServer(row) is not { } server ||
            !long.TryParse(row.ExternalId, out var artistId))
        {
            return;
        }

        StatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloading;
        var language = GetProfileLanguageCode();
        var profile = await ArtistProfileService.DownloadAsync(
            artistId,
            row.Title ?? string.Empty,
            language,
            downloadImage: !row.ImageIsManual);
        byte[]? imageData = null;
        string? imageMimeType = null;
        if (!string.IsNullOrWhiteSpace(profile?.ImagePath) && File.Exists(profile.ImagePath))
        {
            imageData = await File.ReadAllBytesAsync(profile.ImagePath);
            imageMimeType = GuessImageMimeType(profile.ImagePath);
        }

        var refreshed = await _orynivoClient.UpdateArtistProfileAsync(
            server,
            artistId,
            profile?.Biography,
            profile?.SourceUrl,
            profile?.Language ?? language,
            imageData,
            imageMimeType);
        if (refreshed is null)
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }

        row.Biography = refreshed.Biography;
        row.SourceUrl = refreshed.SourceUrl;
        row.ProfileLanguage = refreshed.ProfileLanguage;
        row.ProfileFetchedAt = refreshed.ProfileFetchedAt;
        row.ImageIsManual = refreshed.ImageIsManual;
        if (imageData is not null)
        {
            EnsureOrynivoArtistArtworkPaths(server, artistId, row);
            ApplyRemoteArtwork(row, imageData);
        }
        else
        {
            InvalidateRemoteArtworkCache(row.ArtworkPath);
            _ = LoadOrynivoArtworkAsync(
                row,
                OrynivoServerClient.GetArtistArtworkUrl(server, artistId));
        }
        DeleteOrynivoArtistListCache(server);
        StatusTextBlock.Text = string.IsNullOrWhiteSpace(refreshed.Biography)
            ? LocalizationManager.Current.ArtistInfoNotFound
            : string.Empty;
    }

    private static string GuessImageMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

    private async Task OpenCoverSearchAsync(ContentRow row)
    {
        if (row.Id is not long albumId)
            return;

        var verticalOffset = CaptureCurrentVerticalOffset();
        var dialog = new CoverSearchWindow(row.Title ?? string.Empty, GetCoverSearchArtist(row));
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        await _localCatalogProvider.SetAlbumArtworkAsync(albumId, selected.ImageData, selected.MimeType);

        // Refresh the bound row directly so a card outside the Albums list (e.g. a
        // dashboard recent-album card) updates immediately.
        UpdateRowArtworkFromBytes(row, selected.ImageData);

        // On the dashboard surface the card is already refreshed above; do not
        // rebuild the (hidden) Albums list, which would also overwrite the count.
        if (DashboardScrollViewer.IsVisible)
            return;
        if (_activeAlbumFilterId == albumId)
            await ReloadAlbumDetailHeaderAsync(albumId);
        else
            await ReloadAlbumRowsAsync(albumId, verticalOffset);
    }

    /// <summary>Decodes image bytes and assigns them to a row's artwork/thumbnail in place.</summary>
    /// <param name="row">The row whose artwork should be updated.</param>
    /// <param name="imageData">The new image bytes, or <see langword="null"/> to clear.</param>
    private static void UpdateRowArtworkFromBytes(ContentRow row, byte[]? imageData)
    {
        if (imageData is not { Length: > 0 })
        {
            row.Artwork = null;
            row.Thumbnail = null;
            return;
        }

        try
        {
            using var stream = new MemoryStream(imageData);
            var bitmap = new Bitmap(stream);
            row.Artwork = bitmap;
            row.Thumbnail = bitmap;
            row.ArtworkLoadQueued = false;
            row.ArtworkLoadCompleted = true;
            row.ThumbnailLoadQueued = false;
            row.ThumbnailLoadCompleted = true;
        }
        catch { /* leave the existing artwork on decode failure */ }
    }

    private async Task OpenOrynivoAlbumCoverSearchAsync(
        OrynivoServerSettings server,
        long albumId,
        ContentRow row)
    {
        var dialog = new CoverSearchWindow(row.Title ?? string.Empty, GetCoverSearchArtist(row));
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        var provider = CreateOrynivoCatalogProvider(server);
        var uploaded = await provider.SetAlbumArtworkAsync(albumId, selected.ImageData, selected.MimeType);
        if (!uploaded)
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }

        row.ArtworkPath ??= OrynivoServerClient.GetAlbumArtworkUrl(server, albumId, 320);
        row.ThumbnailPath ??= OrynivoServerClient.GetAlbumArtworkUrl(server, albumId, 96);
        ApplyRemoteArtwork(row, selected.ImageData);
        StatusTextBlock.Text = string.Empty;
    }

    /// <summary>Returns the best available artist name for narrowing manual cover searches.</summary>
    /// <param name="row">Album row or dashboard album card row.</param>
    /// <returns>The artist query to prefill, or <see langword="null"/>.</returns>
    private static string? GetCoverSearchArtist(ContentRow row) =>
        !string.IsNullOrWhiteSpace(row.AlbumArtist)
            ? row.AlbumArtist
            : !string.IsNullOrWhiteSpace(row.Artist)
                ? row.Artist
                : null;

    private async Task OpenOrynivoArtistImageSearchAsync(
        OrynivoServerSettings server,
        long artistId,
        ContentRow row)
    {
        var dialog = new ArtistImageSearchWindow(row.Title ?? string.Empty);
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        var uploaded = await _orynivoClient.UploadArtistImageAsync(
            server,
            artistId,
            selected.ImageData,
            selected.MimeType);
        if (!uploaded)
        {
            StatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            return;
        }

        EnsureOrynivoArtistArtworkPaths(server, artistId, row);
        ApplyRemoteArtwork(row, selected.ImageData);
        DeleteOrynivoArtistListCache(server);
        StatusTextBlock.Text = string.Empty;
    }

    /// <summary>Ensures a remote artist row has stable artwork URLs after an upload.</summary>
    /// <param name="server">Server that owns the artist.</param>
    /// <param name="artistId">Server-side artist identifier.</param>
    /// <param name="row">Artist row to update.</param>
    private static void EnsureOrynivoArtistArtworkPaths(
        OrynivoServerSettings server,
        long artistId,
        ContentRow row)
    {
        var imageUrl = OrynivoServerClient.GetArtistArtworkUrl(server, artistId);
        row.ArtworkPath = imageUrl;
        row.ThumbnailPath = imageUrl;
    }

    /// <summary>Copies server-cached artist profile metadata into a remote artist row.</summary>
    /// <param name="server">Server that owns the artist.</param>
    /// <param name="row">Row to update.</param>
    /// <param name="artist">Server artist profile response.</param>
    private static void ApplyOrynivoArtistProfile(
        OrynivoServerSettings server,
        ContentRow row,
        OrynivoArtistInfo artist)
    {
        row.ArtistId = artist.Id;
        row.Biography = artist.Biography;
        row.SourceUrl = artist.SourceUrl;
        row.ProfileLanguage = artist.ProfileLanguage;
        row.ProfileFetchedAt = artist.ProfileFetchedAt;
        row.ImageIsManual = artist.ImageIsManual;
        if (artist.HasImage)
        {
            EnsureOrynivoArtistArtworkPaths(server, artist.Id, row);
            row.ArtworkLoadCompleted = false;
            row.ThumbnailLoadCompleted = false;
        }
        else
        {
            row.ArtworkPath = null;
            row.ThumbnailPath = null;
            row.Artwork = null;
            row.Thumbnail = null;
            row.ArtworkLoadCompleted = true;
            row.ThumbnailLoadCompleted = true;
        }
    }

    private static void ApplyRemoteArtwork(ContentRow row, byte[] imageData)
    {
        InvalidateRemoteArtworkCache(row.ArtworkPath);
        InvalidateRemoteArtworkCache(row.ThumbnailPath);
        WriteRemoteArtworkCache(row.ArtworkPath, imageData);
        WriteRemoteArtworkCache(row.ThumbnailPath, imageData);
        using var stream = new MemoryStream(imageData);
        var bitmap = new Bitmap(stream);
        row.Artwork = bitmap;
        row.Thumbnail = bitmap;
        row.ArtworkLoadQueued = false;
        row.ArtworkLoadCompleted = true;
        row.ThumbnailLoadQueued = false;
        row.ThumbnailLoadCompleted = true;
    }

    private async Task ReloadAlbumRowsAsync(
        long? selectedAlbumId = null,
        double? verticalOffset = null)
    {
        selectedAlbumId ??= GetSelectedContentRowId();
        verticalOffset ??= CaptureCurrentVerticalOffset();
        var rows = await Task.Run(() => QueryRows("Albums"));
        ApplyColumns("Albums");
        ContentDataGrid.ItemsSource = rows;
        BindArtworkRows("Albums", rows);
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
        RestoreSelection(rows, selectedAlbumId, verticalOffset);
    }

    private async void BackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (LyricsView.IsVisible ||
            ArtistInfoView.IsVisible ||
            PodcastInfoView.IsVisible)
        {
            CloseNowPlayingDetailViews();
            return;
        }

        if (_plexNavigationStack.Count > 0)
        {
            var plexState = _plexNavigationStack.Pop();
            _activePlexView = plexState.View;
            ContentTitleTextBlock.Text = plexState.Title;
            ApplyColumns("Plex" + plexState.View);
            ContentDataGrid.ItemsSource = plexState.Rows;
            ContentDataGrid.IsVisible = true;
            FolderTreeView.IsVisible = false;
            PlexViewModeBorder.IsVisible = _plexNavigationStack.Count == 0;
            ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(plexState.Rows.Count);
            UpdateAlphabetIndex(plexState.Rows, true);
            BackButton.IsVisible = _plexNavigationStack.Count > 0 || _navigationStack.Count > 0;
            return;
        }

        if (_navigationStack.Count == 0)
            return;

        var state = _navigationStack.Pop();
        await RestoreNavigationStateAsync(state);
        BackButton.IsVisible = _navigationStack.Count > 0;
    }

    private async Task RestoreNavigationStateAsync(NavigationState state)
    {
        _restoringNavigationHistory = true;
        try
        {
            await RestoreNavigationStateCoreAsync(state);
        }
        finally
        {
            _restoringNavigationHistory = false;
        }
    }

    private async Task RestoreNavigationStateCoreAsync(NavigationState state)
    {
        _activeArtistFilterId = state.ArtistFilterId;
        _activeArtistFilterName = state.ArtistFilterName;
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        HideAlbumDetailHeader();

        switch (state.View)
        {
            case "AlbumTracks" when state.SelectedId is long albumId:
                await ShowAlbumTracksAsync(
                    albumId,
                    string.IsNullOrWhiteSpace(state.SearchQuery)
                        ? LocalizationManager.Current.Unknown
                        : state.SearchQuery);
                return;

            case "ArtistAlbums" when state.ArtistFilterId is long artistId:
                await ShowArtistAlbumsAsync(
                    artistId,
                    string.IsNullOrWhiteSpace(state.SearchQuery)
                        ? LocalizationManager.Current.Unknown
                        : state.SearchQuery);
                RestoreSelectionFromCurrentItems(
                    state.SelectedId,
                    state.VerticalOffset);
                return;

            case "UnifiedArtistAlbums" when !string.IsNullOrWhiteSpace(state.ArtistFilterName):
                await ShowUnifiedArtistAlbumsAsync(state.ArtistFilterName);
                RestoreSelectionFromCurrentItems(state.SelectedId, state.VerticalOffset);
                return;

            case "OrynivoAlbumTracks" when state.SelectedId is long albumId:
                await RestoreOrynivoAlbumTracksAsync(
                    state.NavigationTag,
                    albumId,
                    state.SearchQuery,
                    state.ArtistFilterId,
                    state.ArtistFilterName,
                    state.VerticalOffset);
                return;

            case "OrynivoArtistAlbums" when state.ArtistFilterId is long artistId:
                await RestoreOrynivoArtistAlbumsAsync(
                    state.NavigationTag,
                    artistId,
                    state.SearchQuery,
                    state.SelectedId,
                    state.VerticalOffset);
                return;

            case "Search":
                SearchTextBox.Text = state.SearchQuery ?? string.Empty;
                await ShowSearchResultsAsync(state.SearchQuery ?? string.Empty);
                return;

            case "Artists":
                ContentTitleTextBlock.Text = LocalizationManager.Current.Artists;
                UpdateLibraryIntroCard("Artists");
                UpdateEntityFavoritesFilterToggle("Artists");
                AlbumViewModeBorder.IsVisible = true;
                SetViewModeButtons(_showArtistArtworkView);
                ContentDataGrid.IsVisible = !(_showArtistArtworkView);
                AlbumArtworkListBox.IsVisible = false;
                ArtistArtworkListBox.IsVisible = _showArtistArtworkView;
                var artists = await Task.Run(() => QueryRows("Artists"));
                ApplyColumns("Artists");
                ContentDataGrid.ItemsSource = artists;
                BindArtworkRows("Artists", artists);
                UpdateAlphabetIndex(artists, true);
                ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(artists.Count);
                RestoreSelection(artists, state.SelectedId, state.VerticalOffset);
                break;

            case "Albums":
                ContentTitleTextBlock.Text = _activeArtistFilterId is long
                    ? $"{LocalizationManager.Current.Albums} · {_activeArtistFilterName}"
                    : LocalizationManager.Current.Albums;
                UpdateLibraryIntroCard("Albums");
                UpdateEntityFavoritesFilterToggle("Albums");
                AlbumViewModeBorder.IsVisible = true;
                SetViewModeButtons(_showAlbumArtworkView);
                ContentDataGrid.IsVisible = !(_showAlbumArtworkView);
                AlbumArtworkListBox.IsVisible = _showAlbumArtworkView;
                var albums = await Task.Run(() => QueryRows("Albums"));
                ApplyColumns("Albums");
                ContentDataGrid.ItemsSource = albums;
                BindArtworkRows("Albums", albums);
                ArtistArtworkListBox.IsVisible = false;
                UpdateAlphabetIndex(albums, true);
                ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(albums.Count);
                RestoreSelection(albums, state.SelectedId, state.VerticalOffset);
                break;

            default:
                SelectNavigationItem(state.View);
                await ShowTopLevelViewAsync(state.View);
                RestoreSelectionFromCurrentItems(
                    state.SelectedId,
                    state.VerticalOffset);
                break;
        }
    }

    private void SelectNavigationItem(string tag)
    {
        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(navItem => string.Equals(navItem.Tag as string, tag, StringComparison.Ordinal));
        if (item is null || ReferenceEquals(NavListBox.SelectedItem, item))
            return;

        _suppressNavSelectionChanged = true;
        try
        {
            NavListBox.SelectedItem = item;
        }
        finally
        {
            _suppressNavSelectionChanged = false;
        }
    }

    private void RestoreSelectionFromCurrentItems(
        long? selectedId,
        double? verticalOffset = null)
    {
        var rows = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList()
                   ?? (AlbumArtworkListBox.ItemsSource as IEnumerable<ContentRow>)?.ToList()
                   ?? (ArtistArtworkListBox.ItemsSource as IEnumerable<ContentRow>)?.ToList()
                   ?? [];
        RestoreSelection(rows, selectedId, verticalOffset);
    }

    private void RestoreSelection(
        List<ContentRow> rows,
        long? selectedId,
        double? verticalOffset = null)
    {
        var row = selectedId is long id
            ? rows.FirstOrDefault(candidate => candidate.Id == id)
            : null;

        if (ContentDataGrid.IsVisible)
        {
            ContentDataGrid.SelectedItem = row;
            RestoreDataGridPositionAfterLayout(row, verticalOffset);
            return;
        }

        var listBox = AlbumArtworkListBox.IsVisible
            ? AlbumArtworkListBox
            : ArtistArtworkListBox.IsVisible
                ? ArtistArtworkListBox
                : null;
        if (listBox is null)
            return;

        if (row is not null)
        {
            EnsureArtworkRowBound(listBox, row);
            listBox.SelectedItem = row;
        }
        RestoreArtworkPositionAfterLayout(listBox, row, verticalOffset);
    }

    private void RestoreDataGridPositionAfterLayout(
        ContentRow? row,
        double? verticalOffset)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AttachContentDataGridVerticalScrollBar();
            if (verticalOffset is double offset &&
                _contentDataGridVerticalScrollBar is { } scrollBar)
            {
                scrollBar.Value = Math.Clamp(offset, scrollBar.Minimum, scrollBar.Maximum);
            }
            else if (row is not null)
            {
                ContentDataGrid.ScrollIntoView(row, null);
            }
        }, DispatcherPriority.Loaded);
    }

    private void RestoreArtworkPositionAfterLayout(
        ListBox listBox,
        ContentRow? row,
        double? verticalOffset)
    {
        var bindingVersion = _artworkBindingVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (bindingVersion != _artworkBindingVersion)
                return;

            var scrollViewer = listBox.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();
            if (scrollViewer is null)
                return;

            if (verticalOffset is double offset)
            {
                var itemWidth = GetArtworkItemWidth(listBox);
                var itemHeight = GetArtworkItemHeight(listBox);
                var perRow = Math.Max(1, (int)Math.Floor(scrollViewer.Viewport.Width / itemWidth));
                var requiredItems = Math.Max(
                    ArtworkPageSize,
                    ((int)Math.Ceiling((offset + scrollViewer.Viewport.Height) / itemHeight) + 1) * perRow);
                var visibleRows = ReferenceEquals(listBox, AlbumArtworkListBox)
                    ? _visibleAlbumArtworkRows
                    : _visibleArtistArtworkRows;
                while (visibleRows.Count < requiredItems && AppendArtworkRows(listBox))
                {
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (bindingVersion != _artworkBindingVersion)
                        return;
                    var restoredViewer = listBox.GetVisualDescendants()
                        .OfType<ScrollViewer>()
                        .FirstOrDefault();
                    if (restoredViewer is null)
                        return;
                    restoredViewer.Offset = new Vector(
                        restoredViewer.Offset.X,
                        Math.Clamp(offset, 0, Math.Max(0, restoredViewer.Extent.Height - restoredViewer.Viewport.Height)));
                    QueueHydrateVisibleArtworkRows(listBox);
                    UpdateActiveAlphabetButton();
                }, DispatcherPriority.Background);
            }
            else if (row is not null)
            {
                ScrollArtworkRowIntoViewAfterLayout(listBox, row);
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The remote Orynivo Server track currently playing, expressed as its server and
    /// track ID, or <see langword="null"/> when no remote track is the favourite target.
    /// </summary>
    private (OrynivoServerSettings Server, long Id)? CurrentOrynivoFavoriteTarget =>
        _currentTrackId is null && _currentOrynivoTrackRow is { OrynivoServer: { } server, Id: long id }
            ? (server, id)
            : null;

    private void UpdateNowPlayingFavoriteButton()
    {
        NowPlayingFavoriteButton.IsEnabled = _currentTrackId.HasValue || CurrentOrynivoFavoriteTarget is not null;
        NowPlayingFavoriteGlyph.Text = _currentTrackIsFavorite ? "❤" : "♡";
        NowPlayingFavoriteGlyph.FontSize = _currentTrackIsFavorite ? 18 : 15;
    }

    private void NowPlayingFavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // Remote Orynivo Server track: toggle the client-side favorite (settings.json).
        if (CurrentOrynivoFavoriteTarget is { } target)
        {
            var (favServer, favId) = target;
            _currentTrackIsFavorite = !_currentTrackIsFavorite;
            var key = GetOrynivoFavoriteKey(favServer.Id, "Track", favId);
            if (_currentTrackIsFavorite)
                _settings.OrynivoServerFavorites.Add(key);
            else
                _settings.OrynivoServerFavorites.Remove(key);
            _settingsStore.Save(_settings);

            if (_currentOrynivoTrackRow is not null)
                _currentOrynivoTrackRow.IsFavorite = _currentTrackIsFavorite;
            UpdateNowPlayingFavoriteButton();
            RefreshOrynivoFavoriteRows(favServer, favId, _currentTrackIsFavorite);
            return;
        }

        if (_currentTrackId is not long id)
            return;

        _currentTrackIsFavorite = !_currentTrackIsFavorite;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.SetTrackFavorite(id, _currentTrackIsFavorite);
        }
        catch { }

        UpdateNowPlayingFavoriteButton();

        if (ContentDataGrid.ItemsSource is IEnumerable<ContentRow> rows)
        {
            var row = rows.FirstOrDefault(r => r.Id == id);
            if (row is not null)
                row.IsFavorite = _currentTrackIsFavorite;
        }
    }

    /// <summary>Reflects a remote track favourite change in any currently visible remote track rows.</summary>
    /// <param name="server">Server owning the toggled track.</param>
    /// <param name="trackId">Toggled remote track ID.</param>
    /// <param name="isFavorite">New favourite state.</param>
    private void RefreshOrynivoFavoriteRows(OrynivoServerSettings server, long trackId, bool isFavorite)
    {
        if (ContentDataGrid.ItemsSource is not IEnumerable<ContentRow> rows)
            return;

        var matches = rows.Where(r => r.EntityType == "OrynivoTrack" &&
                                      r.Id == trackId &&
                                      r.OrynivoServer?.Id == server.Id).ToList();
        if (matches.Count == 0)
            return;

        foreach (var row in matches)
            row.IsFavorite = isFavorite;
    }

    private async Task SetUnifiedArtistFavoriteAsync(string? artistName, bool isFavorite)
    {
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(artistName);
        var localArtists = await _localCatalogProvider.GetArtistsAsync();
        using (var db = AudioDatabase.OpenDefault())
        {
            foreach (var artist in localArtists.Where(candidate =>
                         ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
                db.SetArtistFavorite(artist.Id, isFavorite);
        }

        foreach (var server in _settings.OrynivoServers ?? [])
        {
            try
            {
                var artists = await CreateOrynivoCatalogProvider(server).GetArtistsAsync();
                foreach (var artist in artists.Where(candidate =>
                             ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
                {
                    var key = GetOrynivoFavoriteKey(server.Id, "Artist", artist.Id);
                    if (isFavorite)
                        _settings.OrynivoServerFavorites.Add(key);
                    else
                        _settings.OrynivoServerFavorites.Remove(key);
                }
            }
            catch
            {
                // Keep the available libraries in sync even if one server is offline.
            }
        }

        _settingsStore.Save(_settings);
    }

    private async void FavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row } || row.Id is not long id)
            return;

        row.IsFavorite = !row.IsFavorite;
        if (row.EntityType == "UnifiedArtist")
        {
            await SetUnifiedArtistFavoriteAsync(row.Title, row.IsFavorite);
            if (_artistFavoritesOnly && !row.IsFavorite)
                await ReloadEntityRowsAsync("Artists");
            e.Handled = true;
            return;
        }

        ActivateRowOrynivoServer(row);
        if (row.EntityType.StartsWith("Orynivo", StringComparison.Ordinal) &&
            _activeOrynivoServer is not null)
        {
            var entityType = row.EntityType["Orynivo".Length..];
            var key = GetOrynivoFavoriteKey(_activeOrynivoServer.Id, entityType, id);
            if (row.IsFavorite)
                _settings.OrynivoServerFavorites.Add(key);
            else
                _settings.OrynivoServerFavorites.Remove(key);
            _settingsStore.Save(_settings);
            if (_trackFavoritesOnly && !row.IsFavorite)
                await LoadOrynivoViewAsync();
            e.Handled = true;
            return;
        }

        using (var db = AudioDatabase.OpenDefault())
        {
            if (row.EntityType == "Artist")
                db.SetArtistFavorite(id, row.IsFavorite);
            else if (row.EntityType == "Album")
                db.SetAlbumFavorite(id, row.IsFavorite);
            else
                db.SetTrackFavorite(id, row.IsFavorite);
        }

        if (row.EntityType == "Artist" && _artistFavoritesOnly && !row.IsFavorite)
            await ReloadEntityRowsAsync("Artists");
        else if (row.EntityType == "Album" && _albumFavoritesOnly && !row.IsFavorite)
            await ReloadEntityRowsAsync("Albums");

        QueueHydrateVisibleArtworkRows(AlbumArtworkListBox);
        QueueHydrateVisibleArtworkRows(ArtistArtworkListBox);
        e.Handled = true;
    }

    // ------------------------------------------------------------------
    // Playlist-Kontextmenü
    // ------------------------------------------------------------------

    private void PlaylistContextItem_OnPreviewMouseRightButtonDown(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (sender is not Control target ||
            !e.GetCurrentPoint(target).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (target is DataGridRow dataRow)
        {
            SetPlaylistContextFlyout(dataRow);
            if (dataRow.ContextFlyout is not PopupFlyoutBase)
                return;
            if (FindAncestor<DataGrid>(dataRow) is { } dataGrid)
                dataGrid.SelectedItem = dataRow.DataContext;
        }
        else if (target is TreeViewItem treeItem)
        {
            var isPlexTrack = treeItem.Tag is PlexFolderTag
                {
                    IsTrack: true,
                    Track: not null
                };
            var paths = isPlexTrack
                ? [((PlexFolderTag)treeItem.Tag!).Track!.FilePath]
                : GetPathsForFolderItem(treeItem);
            if (paths.Count == 0)
                return;
            treeItem.IsSelected = true;
            treeItem.ContextFlyout = isPlexTrack
                ? BuildQueueContextFlyout(paths)
                : BuildPlaylistContextFlyout(paths);
        }
        else
        {
            return;
        }

        if (target.ContextFlyout is not PopupFlyoutBase flyout)
            return;
        e.Handled = true;
        flyout.ShowAt(target, showAtPointer: true);
    }

    private void AlbumArtworkContextMenu_OnOpened(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        var row = (menu.PlacementTarget as Control)?.DataContext as ContentRow;
        if (row?.Id is null) return;

        // Vorherige dynamisch hinzugefügte Playlist-Einträge entfernen (erste 2: DeleteCover, ReassignCover)
        while (menu.Items.Count > 2)
            menu.Items.RemoveAt(menu.Items.Count - 1);

        var isRemoteAlbum = row.EntityType == "OrynivoAlbum";
        if (menu.Items.Count > 0 && menu.Items[0] is MenuItem deleteItem)
            deleteItem.IsVisible = !isRemoteAlbum;
        if (menu.Items.Count > 1 && menu.Items[1] is MenuItem reassignItem)
        {
            reassignItem.Header = isRemoteAlbum
                ? LocalizationManager.Current.SearchCover
                : LocalizationManager.Current.ReassignCover;
            reassignItem.IsVisible = true;
        }

        if (isRemoteAlbum)
            return;

        AppendPlaylistItems(menu, GetPathsForRow(row));
    }

    // ------------------------------------------------------------------
    // Podcasts
    // ------------------------------------------------------------------

    private Task EnsurePodcastFilterCatalogAsync()
    {
        BuildPodcastCategoryFilter();
        BuildPodcastLanguageFilter();
        if (_podcastFilterCatalogLoading ||
            (CatalogFilterCache.IsFresh(_catalogFilterCacheData.PodcastCategoriesUpdatedAt) &&
             CatalogFilterCache.IsFresh(_catalogFilterCacheData.PodcastLanguagesUpdatedAt)))
            return Task.CompletedTask;

        _podcastFilterCatalogLoading = true;
        _ = RefreshPodcastFilterCatalogAsync();
        return Task.CompletedTask;
    }

    private async Task RefreshPodcastFilterCatalogAsync()
    {
        try
        {
            if (!CatalogFilterCache.IsFresh(_catalogFilterCacheData.PodcastCategoriesUpdatedAt))
            {
                var categories = await _podcastService.GetCategoryCatalogAsync();
                var options = categories
                    .Select(category => new CatalogFilterOption(
                        category.Name,
                        Key: category.Id))
                    .ToList();
                _podcastCategoryCatalog.Clear();
                _podcastCategoryCatalog.AddRange(options);
                _catalogFilterCacheData.PodcastCategories = options;
                _catalogFilterCacheData.PodcastCategoriesUpdatedAt = DateTimeOffset.UtcNow;
                _catalogFilterCache.Save(_catalogFilterCacheData);
                BuildPodcastCategoryFilter();
            }

            if (!CatalogFilterCache.IsFresh(_catalogFilterCacheData.PodcastLanguagesUpdatedAt))
            {
                var feedUrls = (await _podcastService.GetPopularFeedUrlsAsync()).ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var db = AudioDatabase.OpenDefault();
                    foreach (var podcast in db.GetPodcasts())
                        feedUrls.Add(podcast.FeedUrl);
                }
                catch
                {
                }

                var languages = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(
                    StringComparer.OrdinalIgnoreCase);
                await Parallel.ForEachAsync(
                    feedUrls,
                    new ParallelOptions { MaxDegreeOfParallelism = 10 },
                    async (feedUrl, token) =>
                    {
                        var language = await _podcastService.GetFeedLanguageAsync(feedUrl, token);
                        if (language is not null)
                            languages.TryAdd(language, 0);
                    });
                MergePodcastLanguagesIntoCache(languages.Keys);
                _catalogFilterCacheData.PodcastLanguagesUpdatedAt = DateTimeOffset.UtcNow;
                _catalogFilterCache.Save(_catalogFilterCacheData);
                BuildPodcastLanguageFilter();
            }
        }
        catch
        {
            // Existing cached filter data remains usable.
        }
        finally
        {
            _podcastFilterCatalogLoading = false;
        }
    }

    private void MergePodcastLanguagesIntoCache(IEnumerable<string> languages)
    {
        var values = _podcastLanguageCatalog
            .Select(option => option.Value)
            .Concat(languages)
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(FormatPodcastLanguage, StringComparer.CurrentCultureIgnoreCase)
            .Select(language => new CatalogFilterOption(language))
            .ToList();
        _podcastLanguageCatalog.Clear();
        _podcastLanguageCatalog.AddRange(values);
        _catalogFilterCacheData.PodcastLanguages = values;
    }

    private async void PodcastSearchButton_OnClick(object? sender, RoutedEventArgs e) =>
        await SearchPodcastsAsync();

    private async void PodcastSearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        await SearchPodcastsAsync();
    }

    private async Task SearchPodcastsAsync()
    {
        CancelAndDispose(ref _podcastSearchCts);
        _podcastSearchCts = new CancellationTokenSource();
        var cancellationToken = _podcastSearchCts.Token;

        if (string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text) &&
            _selectedPodcastCategories.Count == 0 &&
            _selectedPodcastLanguages.Count == 0)
        {
            _podcastSearchResults.Clear();
            PodcastsDataGrid.ItemsSource = null;
            PodcastStatusTextBlock.Text = string.Empty;
            PodcastStatusTextBlock.IsVisible = false;
            ContentCountTextBlock.Text = string.Empty;
            BuildPodcastCategoryFilter();
            BuildPodcastLanguageFilter();
            return;
        }

        PodcastsDataGrid.ItemsSource = null;
        PodcastStatusTextBlock.IsVisible = true;
        PodcastStatusTextBlock.Text = LocalizationManager.Current.PodcastLoading;
        try
        {
            var selectedGenreIds = _podcastCategoryCatalog
                .Where(option => _selectedPodcastCategories.Contains(option.Value))
                .Select(option => option.Key)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToList();
            var hasSearch = !string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text);
            IReadOnlyList<PodcastSearchResult> podcasts;
            if (!hasSearch &&
                selectedGenreIds.Count == 0 &&
                _selectedPodcastLanguages.Count > 0)
            {
                podcasts = await _podcastService.GetPopularPodcastsAsync(cancellationToken);
            }
            else
            {
                podcasts = await _podcastService.SearchAsync(
                    PodcastSearchTextBox.Text,
                    selectedGenreIds,
                    cancellationToken);
            }
            var rows = podcasts.Select(podcast => new PodcastViewModel
            {
                CollectionId = podcast.CollectionId,
                Name = podcast.Name,
                Author = podcast.Author,
                FeedUrl = podcast.FeedUrl,
                ArtworkUrl = podcast.ArtworkUrl,
                Genre = podcast.Genre,
                Genres = podcast.Genres,
                GenreIds = podcast.GenreIds,
                Language = podcast.Language
            }).ToList();
            _podcastSearchResults.Clear();
            _podcastSearchResults.AddRange(rows);
            _podcastLanguagesLoading = rows.Count > 0;
            if (rows.Count == 0)
                PrunePodcastLanguageSelection();
            BuildPodcastCategoryFilter();
            BuildPodcastLanguageFilter();
            ApplyPodcastFilters();

            if (rows.Count > 0)
            {
                PodcastStatusTextBlock.IsVisible = true;
                PodcastStatusTextBlock.Text = LocalizationManager.Current.PodcastLanguagesLoading;
                await Parallel.ForEachAsync(
                    rows,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 6,
                        CancellationToken = cancellationToken
                    },
                    async (row, token) =>
                    {
                        row.Language = await _podcastService.GetFeedLanguageAsync(
                            row.FeedUrl,
                            token);
                    });
                var availableLanguages = rows
                    .Select(row => row.Language)
                    .Where(language => language is not null)
                    .Cast<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                MergePodcastLanguagesIntoCache(availableLanguages);
                _catalogFilterCache.Save(_catalogFilterCacheData);
                _podcastLanguagesLoading = false;
                PrunePodcastLanguageSelection();
                BuildPodcastLanguageFilter();
                ApplyPodcastFilters();
            }
        }
        catch (OperationCanceledException)
        {
            _podcastLanguagesLoading = false;
        }
        catch
        {
            _podcastLanguagesLoading = false;
            PodcastStatusTextBlock.Text = LocalizationManager.Current.PodcastSearchFailed;
        }
    }

    private void PodcastCategoryFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        BuildPodcastCategoryFilter();
        PodcastCategoryFilterPopup.IsOpen = !PodcastCategoryFilterPopup.IsOpen;
    }

    private void PodcastLanguageFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        BuildPodcastLanguageFilter();
        PodcastLanguageFilterPopup.IsOpen = !PodcastLanguageFilterPopup.IsOpen;
    }

    private async void ClearPodcastCategoryFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _selectedPodcastCategories.Clear();
        BuildPodcastCategoryFilter();
        await SearchPodcastsAsync();
    }

    private async void ClearPodcastLanguageFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _selectedPodcastLanguages.Clear();
        BuildPodcastLanguageFilter();
        await SearchPodcastsAsync();
    }

    private void BuildPodcastCategoryFilter()
    {
        var useCatalog = string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text);
        var options = useCatalog && _podcastCategoryCatalog.Count > 0
            ? _podcastCategoryCatalog
            : _podcastSearchResults
                .SelectMany(row => row.Genres)
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CatalogFilterOption(group.Key, group.Count()))
                .OrderByDescending(option => option.Count)
                .ThenBy(option => option.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        BuildPodcastFilterOptions(
            PodcastCategoryFilterPanel,
            options,
            _selectedPodcastCategories,
            PodcastCategoryCheckBox_OnChanged,
            value => value);
        UpdatePodcastFilterButtons();
    }

    private void PruneRadioGenreSelection()
    {
        var available = string.IsNullOrWhiteSpace(RadioSearchTextBox.Text)
            ? _radioGenreCatalog.Select(option => option.Value)
            : _radioSearchResults.SelectMany(row => row.Genres);
        var values = available.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedRadioGenres.RemoveWhere(value => !values.Contains(value));
    }

    private void PrunePodcastCategorySelection()
    {
        var available = string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text)
            ? _podcastCategoryCatalog.Select(option => option.Value)
            : _podcastSearchResults.SelectMany(row => row.Genres);
        var values = available.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedPodcastCategories.RemoveWhere(value => !values.Contains(value));
    }

    private void PrunePodcastLanguageSelection()
    {
        var available = string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text)
            ? _podcastLanguageCatalog.Select(option => option.Value)
            : _podcastSearchResults
                .Select(row => row.Language)
                .Where(language => language is not null)
                .Cast<string>();
        var values = available.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedPodcastLanguages.RemoveWhere(value => !values.Contains(value));
    }

    private void BuildPodcastLanguageFilter()
    {
        var useCatalog = string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text);
        var options = useCatalog && _podcastLanguageCatalog.Count > 0
            ? _podcastLanguageCatalog
            : _podcastSearchResults
                .Where(row => !string.IsNullOrWhiteSpace(row.Language))
                .GroupBy(row => row.Language!, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CatalogFilterOption(group.Key, group.Count()))
                .OrderBy(
                    option => FormatPodcastLanguage(option.Value),
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        BuildPodcastFilterOptions(
            PodcastLanguageFilterPanel,
            options,
            _selectedPodcastLanguages,
            PodcastLanguageCheckBox_OnChanged,
            FormatPodcastLanguage);
        UpdatePodcastFilterButtons();
    }

    private void BuildPodcastFilterOptions(
        Panel panel,
        IReadOnlyList<CatalogFilterOption> options,
        IReadOnlySet<string> selected,
        EventHandler<RoutedEventArgs>? changedHandler,
        Func<string, string> displayName)
    {
        panel.Children.Clear();
        foreach (var option in options)
        {
            var checkBox = new CheckBox
            {
                Content = option.Count is > 0
                    ? $"{displayName(option.Value)} ({option.Count:N0})"
                    : displayName(option.Value),
                Tag = option.Value,
                IsChecked = selected.Contains(option.Value),
                Margin = new Thickness(2, 4, 2, 4),
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                Theme = FindResource<ControlTheme>("HeaderCheckBoxTheme")
            };
            checkBox.IsCheckedChanged += changedHandler;
            panel.Children.Add(checkBox);
        }
    }

    private async void PodcastCategoryCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        UpdatePodcastSelection(sender, _selectedPodcastCategories);
        UpdatePodcastFilterButtons();
        await SearchPodcastsAsync();
    }

    private async void PodcastLanguageCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        UpdatePodcastSelection(sender, _selectedPodcastLanguages);
        UpdatePodcastFilterButtons();
        await SearchPodcastsAsync();
    }

    private static void UpdatePodcastSelection(object? sender, HashSet<string> selection)
    {
        if (sender is not CheckBox { Tag: string value } checkBox)
            return;
        if (checkBox.IsChecked == true)
            selection.Add(value);
        else
            selection.Remove(value);
    }

    private void ApplyPodcastFilters()
    {
        var rows = _podcastSearchResults
            .Where(row =>
                (_podcastLanguagesLoading ||
                 _selectedPodcastLanguages.Count == 0 ||
                 (row.Language is not null && _selectedPodcastLanguages.Contains(row.Language))))
            .ToList();
        PodcastsDataGrid.ItemsSource = rows;
        { var _tmp = PodcastsDataGrid.ItemsSource; PodcastsDataGrid.ItemsSource = null; PodcastsDataGrid.ItemsSource = _tmp; };
        PodcastStatusTextBlock.Text = rows.Count == 0
            ? LocalizationManager.Current.PodcastNoResults
            : string.Empty;
        PodcastStatusTextBlock.IsVisible = rows.Count == 0;
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
    }

    private void UpdatePodcastFilterButtons()
    {
        PodcastCategoryFilterButton.Content = _selectedPodcastCategories.Count == 0
            ? LocalizationManager.Current.PodcastCategories
            : $"{LocalizationManager.Current.PodcastCategories} ({_selectedPodcastCategories.Count})";
        PodcastLanguageFilterButton.Content = _selectedPodcastLanguages.Count == 0
            ? LocalizationManager.Current.PodcastLanguages
            : $"{LocalizationManager.Current.PodcastLanguages} ({_selectedPodcastLanguages.Count})";
    }

    private static string FormatPodcastLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return string.Empty;
        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            var name = culture.DisplayName;
            return $"{name} ({culture.TwoLetterISOLanguageName.ToUpperInvariant()})";
        }
        catch (CultureNotFoundException)
        {
            return language.ToUpperInvariant();
        }
    }

    private async void PodcastsDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null ||
            !TryGetDoubleTappedRow<PodcastViewModel>(PodcastsDataGrid, e, out var podcast))
            return;
        await ShowPodcastEpisodesAsync(podcast.ToRecord());
    }

    private async void ShowPodcastEpisodesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PodcastViewModel podcast })
            await ShowPodcastEpisodesAsync(podcast.ToRecord());
    }

    private void AddPodcastButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PodcastViewModel podcast })
            return;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.SavePodcast(podcast.ToSearchResult());
            LoadNavPlaylists();
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.PodcastAdded, podcast.Name);
        }
        catch
        {
            StatusTextBlock.Text = LocalizationManager.Current.PodcastSearchFailed;
        }
    }

    private async Task ShowSavedPodcastAsync(long podcastId)
    {
        PodcastRecord? podcast;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            podcast = db.GetPodcast(podcastId);
        }
        catch
        {
            podcast = null;
        }

        if (podcast is null)
        {
            PodcastsDataGrid.ItemsSource = null;
            PodcastStatusTextBlock.IsVisible = true;
            PodcastStatusTextBlock.Text = LocalizationManager.Current.PodcastNoResults;
            return;
        }

        await ShowPodcastEpisodesAsync(podcast, showBackButton: false);
    }

    private async Task ShowPodcastEpisodesAsync(
        PodcastRecord podcast,
        bool showBackButton = true)
    {
        CancelAndDispose(ref _podcastFeedCts);
        _podcastFeedCts = new CancellationTokenSource();
        var cancellationToken = _podcastFeedCts.Token;
        _activePodcast = podcast;
        PodcastEpisodesView.IsVisible = true;
        PodcastEpisodesBackButton.IsVisible = showBackButton;
        PodcastEpisodesTitle.Text = podcast.Name;
        PodcastEpisodesAuthor.Text = podcast.Author ?? string.Empty;
        PodcastEpisodesStatistics.Text = string.Empty;
        PodcastEpisodesMetadata.Text = string.Empty;
        PodcastEpisodesDescription.Text = string.Empty;
        PodcastEpisodesArtwork.Source = null;
        PodcastEpisodesDataGrid.ItemsSource = null;
        PodcastEpisodesStatusTextBlock.IsVisible = true;
        PodcastEpisodesStatusTextBlock.Text = LocalizationManager.Current.PodcastEpisodesLoading;
        _ = LoadPodcastHeaderArtworkAsync(podcast.ArtworkUrl, cancellationToken);
        try
        {
            var feed = await _podcastService.GetFeedAsync(
                podcast.FeedUrl,
                cancellationToken);
            var progress = podcast.Id > 0
                ? await Task.Run(() =>
                {
                    using var db = AudioDatabase.OpenDefault();
                    return db.GetPodcastEpisodeProgress(podcast.Id);
                }, cancellationToken)
                : new Dictionary<string, PodcastEpisodeProgress>(StringComparer.Ordinal);
            var rows = feed.Episodes.Select(episode => CreatePodcastEpisodeRow(episode, progress))
                .ToList();
            PodcastEpisodesDataGrid.ItemsSource = rows;
            PodcastEpisodesStatusTextBlock.Text = rows.Count == 0
                ? LocalizationManager.Current.PodcastNoEpisodes
                : string.Empty;
            PodcastEpisodesStatusTextBlock.IsVisible = rows.Count == 0;
            ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
            UpdatePodcastHeader(podcast, feed, progress);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            PodcastEpisodesStatusTextBlock.IsVisible = true;
            PodcastEpisodesStatusTextBlock.Text = LocalizationManager.Current.PodcastFeedFailed;
        }
    }

    private void UpdatePodcastHeader(
        PodcastRecord podcast,
        PodcastFeed feed,
        IReadOnlyDictionary<string, PodcastEpisodeProgress> progress)
    {
        var completed = feed.Episodes.Count(episode =>
            progress.TryGetValue(episode.EpisodeKey, out var item) && item.IsCompleted);
        var started = feed.Episodes.Count(episode =>
            progress.TryGetValue(episode.EpisodeKey, out var item) &&
            !item.IsCompleted &&
            item.PositionSeconds > 0);
        var unheard = Math.Max(0, feed.Episodes.Count - completed);
        PodcastEpisodesStatistics.Text = string.Join(
            "  ·  ",
            string.Format(LocalizationManager.Current.PodcastEpisodeTotal, feed.Episodes.Count),
            string.Format(LocalizationManager.Current.PodcastEpisodeUnheard, unheard),
            string.Format(LocalizationManager.Current.PodcastEpisodeStarted, started));

        var metadata = new List<string>();
        var categories = feed.Categories.Count > 0
            ? feed.Categories
            : string.IsNullOrWhiteSpace(podcast.Genre) ? [] : [podcast.Genre];
        if (categories.Count > 0)
            metadata.Add(string.Join(", ", categories.Take(3)));
        if (!string.IsNullOrWhiteSpace(feed.Language))
            metadata.Add(FormatPodcastLanguage(feed.Language));
        if (feed.Episodes.FirstOrDefault()?.PublishedAt is DateTimeOffset latest)
        {
            metadata.Add(string.Format(
                LocalizationManager.Current.PodcastLatestEpisode,
                latest.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
        }
        PodcastEpisodesMetadata.Text = string.Join("  ·  ", metadata);
        PodcastEpisodesDescription.Text = NormalizePodcastDescription(feed.Description);
        ToolTip.SetTip(PodcastEpisodesDescription, PodcastEpisodesDescription.Text);
    }

    private static string NormalizePodcastDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;
        var withoutTags = Regex.Replace(description, "<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    private PodcastEpisodeViewModel CreatePodcastEpisodeRow(
        PodcastEpisode episode,
        IReadOnlyDictionary<string, PodcastEpisodeProgress> progressByEpisode)
    {
        progressByEpisode.TryGetValue(episode.EpisodeKey, out var progress);
        var duration = progress?.DurationSeconds is > 0
            ? TimeSpan.FromSeconds(progress.DurationSeconds.Value)
            : episode.FeedDuration;
        var position = progress?.PositionSeconds is > 0
            ? TimeSpan.FromSeconds(progress.PositionSeconds)
            : TimeSpan.Zero;
        var status = progress?.IsCompleted == true
            ? LocalizationManager.Current.PodcastPlayed
            : position > TimeSpan.Zero
                ? LocalizationManager.Current.PodcastInProgress
                : LocalizationManager.Current.PodcastUnplayed;
        return new PodcastEpisodeViewModel
        {
            Episode = episode,
            Title = episode.Title,
            Published = episode.PublishedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? string.Empty,
            Duration = duration is null ? string.Empty : FormatTime(duration.Value),
            Progress = position <= TimeSpan.Zero
                ? string.Empty
                : duration is null
                    ? FormatTime(position)
                    : $"{FormatTime(position)} / {FormatTime(duration.Value)}",
            Status = status
        };
    }

    private async void PodcastEpisodesDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_activePodcast is null ||
            !TryGetDoubleTappedRow<PodcastEpisodeViewModel>(PodcastEpisodesDataGrid, e, out var row))
            return;
        await PlayPodcastEpisodeAsync(_activePodcast, row.Episode);
    }

    private void PodcastEpisodesBackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CancelAndDispose(ref _podcastFeedCts);
        PodcastEpisodesView.IsVisible = false;
        _activePodcast = null;
        ContentCountTextBlock.Text = ((PodcastsDataGrid.ItemsSource as System.Collections.IList)?.Count ?? 0) > 0
            ? LocalizationManager.FormatEntryCount(((PodcastsDataGrid.ItemsSource as System.Collections.IList)?.Count ?? 0))
            : string.Empty;
    }

    private async Task PlayPodcastEpisodeAsync(PodcastRecord podcast, PodcastEpisode episode)
    {
        RefreshQueueNavigationButtons();
        try
        {
            await StartPlaybackAsync(
                episode.AudioUrl,
                podcastPlayback: new PodcastPlayback(podcast, episode));
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped;
        }
        catch
        {
            StopPlayback();
            PodcastEpisodesStatusTextBlock.IsVisible = true;
            PodcastEpisodesStatusTextBlock.Text = LocalizationManager.Current.PodcastFeedFailed;
        }
    }

    private async Task LoadPodcastArtworkAsync(string? artworkUrl, CancellationToken cancellationToken)
    {
        var image = await DownloadPodcastImageAsync(artworkUrl, 400, cancellationToken);
        if (image is not null && !cancellationToken.IsCancellationRequested)
            NowPlayingArtworkImage.Source = image;
    }

    private async Task LoadPodcastHeaderArtworkAsync(string? artworkUrl, CancellationToken cancellationToken)
    {
        var image = await DownloadPodcastImageAsync(artworkUrl, 160, cancellationToken);
        if (image is not null && !cancellationToken.IsCancellationRequested)
            PodcastEpisodesArtwork.Source = image;
    }

    private static async Task<Bitmap?> DownloadPodcastImageAsync(
        string? artworkUrl,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(artworkUrl, UriKind.Absolute, out var uri))
            return null;
        try
        {
            var bytes = await RadioImageHttpClient.GetByteArrayAsync(uri, cancellationToken);
            await using var stream = new MemoryStream(bytes);
            var image = new Bitmap(stream);
            return image;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Wiedergabe
    // ------------------------------------------------------------------

    private void ConfigureWindowsMediaTransport()
    {
        _windowsMediaTransport = WindowsMediaTransportService.TryCreate();
        if (_windowsMediaTransport is null)
            return;

        _windowsMediaTransport.PlayRequested += () =>
            Dispatcher.UIThread.Post(async () => await ResumeOrStartPlaybackAsync());
        _windowsMediaTransport.PauseRequested += () =>
            Dispatcher.UIThread.Post(PausePlayback);
        _windowsMediaTransport.PreviousRequested += () =>
            Dispatcher.UIThread.Post(async () => await PlayPreviousAsync());
        _windowsMediaTransport.NextRequested += () =>
            Dispatcher.UIThread.Post(async () => await PlayNextAsync());
        _windowsMediaTransport.StopRequested += () =>
            Dispatcher.UIThread.Post(StopPlayback);
        _windowsMediaTransport.PositionChangeRequested += position =>
            Dispatcher.UIThread.Post(async () => await SeekFromSystemAsync(position));
        RefreshQueueNavigationButtons();
    }

    private async void PlayButton_OnClick(object? sender, RoutedEventArgs e) =>
        await TogglePlaybackAsync();

    private async Task TogglePlaybackAsync()
    {
        if (_player is not null)
        {
            if (_player.IsPaused)
            {
                _player.Resume();
                SetPlayPauseIcon(isPlaying: true);
                _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Playing);
            }
            else
            {
                _player.Pause();
                SetPlayPauseIcon(isPlaying: false);
                _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Paused);
            }
            return;
        }

        await ResumeOrStartPlaybackAsync();
    }

    private async Task ResumeOrStartPlaybackAsync()
    {
        if (_player is not null)
        {
            if (_player.IsPaused)
            {
                _player.Resume();
                SetPlayPauseIcon(isPlaying: true);
                _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Playing);
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            StatusTextBlock.Text = LocalizationManager.Current.SelectTrackFirst;
            return;
        }
        try { await StartPlaybackAsync(_currentFilePath, _currentRadioStation, _currentPodcastPlayback); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private void PausePlayback()
    {
        if (_player is null || _player.IsPaused)
            return;
        _player.Pause();
        SetPlayPauseIcon(isPlaying: false);
        _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Paused);
    }

    private async Task SeekFromSystemAsync(TimeSpan position)
    {
        if (_player?.CanSeek != true)
            return;
        try
        {
            await _player.SeekAsync(position);
            RefreshTransport();
            _windowsMediaTransport?.UpdateTimeline(
                _player.Position,
                _player.Duration,
                force: true);
        }
        catch
        {
            // A rejected system seek request must not interrupt playback.
        }
    }

    private async Task StartPlaybackAsync(
        string filePath,
        RadioStationRecord? radioStation = null,
        PodcastPlayback? podcastPlayback = null,
        TimeSpan initialPosition = default)
    {
        await StopPlaybackAsync();
        _currentFilePath = filePath;
        _currentRadioStation = radioStation;
        _currentPodcastPlayback = podcastPlayback;
        if (radioStation is null && podcastPlayback is null)
            _playedQueuePaths.Add(filePath);
        _playbackCts     = new CancellationTokenSource();

        var playbackTrack = ResolveGaplessPlaybackItem(filePath);
        var ext = Path.GetExtension(playbackTrack.PlaybackPath);
        IAudioPlayer player;
        AudioFileInfo info;
        var gaplessItems = BuildGaplessPlaybackItems(
            filePath,
            radioStation is null && podcastPlayback is null);

        if (_settings.OutputBackend is OutputBackend.Asio or OutputBackend.CwAsio)
        {
            if (!SteinbergAsioStream.IsBackendAvailable(_settings.OutputBackend))
            {
                StatusTextBlock.Text = LocalizationManager.Current.AsioBridgeMissing;
                return;
            }
            if (string.IsNullOrWhiteSpace(_settings.SelectedDriverName))
            {
                StatusTextBlock.Text = LocalizationManager.Current.SelectAsioDevice;
                return;
            }
            if (!_settings.AlwaysConvertDsdToPcm &&
                ext.Equals(".dsf", StringComparison.OrdinalIgnoreCase))
                (player, info) = await DsfAudioPlayer.CreateAsync(
                    filePath,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    _playbackCts.Token);
            else if (!_settings.AlwaysConvertDsdToPcm &&
                     IsRemoteOrynivoDsdCandidate(filePath))
                (player, info) = await CreateRemoteOrynivoDsdOrPcmPlayerAsync(filePath, playbackTrack);
            else if (!_settings.AlwaysConvertDsdToPcm &&
                     ext.Equals(".dff", StringComparison.OrdinalIgnoreCase))
                (player, info) = await DffAudioPlayer.CreateAsync(
                    filePath,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    _playbackCts.Token);
            else
                (player, info) = await FfmpegAudioPlayer.CreateAsync(
                    gaplessItems,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    _settings.EqualizerEnabled,
                    _settings.EqualizerProfile,
                    _playbackCts.Token);
        }
        else if (_settings.OutputBackend == OutputBackend.Wasapi)
        {
            if (string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId))
            {
                StatusTextBlock.Text = LocalizationManager.Current.SelectWasapiDevice;
                return;
            }
            (player, info) = await WasapiAudioPlayer.CreateAsync(
                gaplessItems,
                _settings.SelectedWasapiDeviceId,
                _settings.EqualizerEnabled,
                _settings.EqualizerProfile,
                _playbackCts.Token);
        }
        else
        {
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.NotImplemented, _settings.OutputBackend);
            return;
        }

        _player        = player;
        if (player is IGaplessAudioPlayer gaplessPlayer)
            gaplessPlayer.TrackChanged += GaplessPlayer_OnTrackChanged;
        if (initialPosition > TimeSpan.Zero && player.CanSeek)
        {
            try { await player.SeekAsync(initialPosition); }
            catch { /* seek failure must not prevent playback */ }
        }
        UpdateNowPlayingRowHighlights();
        _player.Volume = _settings.OutputBackend == OutputBackend.Wasapi &&
                         _endpointVolumeSynchronizer is not null
            ? 1.0f
            : (float)VolumeSlider.Value;
        _player.ReplayGainFactor = GetReplayGainFactor(filePath);
        _currentPlaybackDuration = player.Duration;
        if (podcastPlayback is not null &&
            podcastPlayback.Podcast.Id > 0 &&
            player.CanSeek)
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var progress = db.GetPodcastEpisodeProgress(
                    podcastPlayback.Podcast.Id,
                    podcastPlayback.Episode.EpisodeKey);
                if (progress is { IsCompleted: false, PositionSeconds: > 5 } &&
                    progress.PositionSeconds < Math.Max(0, player.Duration.TotalSeconds - 10))
                {
                    await player.SeekAsync(TimeSpan.FromSeconds(progress.PositionSeconds));
                }
            }
            catch
            {
                // A corrupt or unavailable resume point must not prevent playback.
            }
        }
        _lastPodcastProgressSave = DateTimeOffset.UtcNow;
        PlayButton.IsEnabled   = false;
        PlayButton.IsEnabled   = true;
        SetPlayPauseIcon(isPlaying: true);
        RefreshQueueNavigationButtons();
        PositionSlider.IsEnabled = player.CanSeek;
        DurationTextBlock.Text = FormatTime(player.Duration);
        _ = LoadTransportWaveformAsync(filePath, player.Duration);
        _transportTimer.Start();

        // Now-playing anzeigen
        var queueMetadata = GetPlaylistMetadata(filePath);
        var filename = podcastPlayback?.Episode.Title ??
                       radioStation?.Name ??
                       queueMetadata?.DisplayTitle ??
                       Path.GetFileNameWithoutExtension(filePath);
        NowPlayingTitleBlock.Text  = filename;
        NowPlayingArtistBlock.Text = podcastPlayback?.Podcast.Name ??
                                     (radioStation is null
                                         ? SelectedDriverTextBlock.Text
                                         : LocalizationManager.Current.InternetRadio);
        ClearNowPlayingAlbum();
        var usesNativeDsd = player is DsfAudioPlayer or DffAudioPlayer or RemoteDsfAudioPlayer or RemoteDffAudioPlayer;
        FileInfoTextBlock.Text = usesNativeDsd
            ? $"{info.ContainerName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  {LocalizationManager.Current.NativeDsdOutput}"
            : info.IsDsd
                ? $"{info.ContainerName.ToUpperInvariant()}  ·  {LocalizationManager.Current.DsdToPcmOutput}  ·  {info.OutputSampleRate:N0} Hz"
                : info.SourceSampleRate != info.OutputSampleRate
                    ? $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz → {info.OutputSampleRate:N0} Hz  ·  {info.Channels} ch"
                    : $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  {info.Channels} ch";
        if (radioStation is null && podcastPlayback is null)
        {
            _currentNowPlayingProvider = _localNowPlayingProvider;
            _currentOrynivoTrackRow = null;
            var isPlexTrack = _plexTracksByUrl.TryGetValue(filePath, out var plexTrack);
            var isOrynivoTrack = _orynivoTracksByUrl.TryGetValue(filePath, out var orynivoTrack);
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var track = db.GetByPath(filePath);
                var artist = db.GetArtistByTrackPath(filePath);
                var trackInfo = db.GetTrackIdAndFavorite(filePath);
                var navigationIds = db.GetTrackNavigationIds(filePath);
                _currentTrackId = trackInfo?.Id;
                _currentTrackIsFavorite = trackInfo?.IsFavorite ?? false;
                _currentArtistId = artist?.Id;
                _currentArtistName = artist?.Artist;
                _currentAlbumId = navigationIds.AlbumId;
                NowPlayingArtistButton.IsEnabled = artist is not null;
                LyricsButton.IsEnabled = track is not null;
                ArtistInfoButton.IsEnabled = artist is not null;
                ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
                if (track is not null)
                {
                    NowPlayingTitleBlock.Text = track.Title ?? filename;
                    NowPlayingArtistBlock.Text = track.Artist ?? string.Empty;
                    SetNowPlayingAlbum(track.Album, navigationIds.AlbumId, navigationIds.AlbumId is not null);
                }
            }
            catch
            {
                NowPlayingArtworkImage.Source = null;
                LyricsBackgroundImage.Source = null;
                _currentTrackId = null;
                _currentArtistId = null;
                _currentArtistName = null;
                ClearNowPlayingAlbum();
                NowPlayingArtistButton.IsEnabled = false;
                _currentTrackIsFavorite = false;
                LyricsButton.IsEnabled = false;
                ArtistInfoButton.IsEnabled = false;
            }
            if (isPlexTrack && plexTrack is not null)
            {
                NowPlayingTitleBlock.Text = plexTrack.Title ?? filename;
                NowPlayingArtistBlock.Text = plexTrack.Artist ?? string.Empty;
                NowPlayingArtworkImage.Source = null;
                LyricsBackgroundImage.Source = null;
                _currentTrackId = null;
                _currentArtistId = null;
                _currentArtistName = plexTrack.Artist;
                SetNowPlayingAlbum(plexTrack.Album, null, canNavigate: false);
                _currentTrackIsFavorite = false;
                NowPlayingArtistButton.IsEnabled = false;
                LyricsButton.IsEnabled = false;
                ArtistInfoButton.IsEnabled = false;
            }
            else if (isOrynivoTrack && orynivoTrack is not null)
            {
                NowPlayingTitleBlock.Text = orynivoTrack.Title ?? filename;
                NowPlayingArtistBlock.Text = orynivoTrack.Artist ?? string.Empty;
                // Show the list thumbnail immediately if already hydrated; the provider
                // then loads full artwork below so it appears even when the row is not.
                NowPlayingArtworkImage.Source = orynivoTrack.Thumbnail ?? orynivoTrack.Artwork;
                LyricsBackgroundImage.Source = orynivoTrack.Artwork ?? orynivoTrack.Thumbnail;
                _currentTrackId = null;
                _currentArtistId = null;
                _currentArtistName = orynivoTrack.Artist;
                SetNowPlayingAlbum(
                    orynivoTrack.Album,
                    orynivoTrack.AlbumId,
                    orynivoTrack.OrynivoServer is not null && orynivoTrack.AlbumId is not null);
                _currentTrackIsFavorite = false;
                _currentOrynivoTrackRow = orynivoTrack;
                if (orynivoTrack.OrynivoServer is { } trackServer)
                {
                    _currentNowPlayingProvider = CreateOrynivoNowPlayingProvider(trackServer);
                    if (orynivoTrack.Id is long favoriteTrackId)
                        _currentTrackIsFavorite = IsOrynivoFavorite(trackServer, "Track", favoriteTrackId);
                }
                // The now-playing artist button navigates within the remote library
                // using the row's server and artist ID (see NowPlayingArtistButton_OnClick).
                NowPlayingArtistButton.IsEnabled = orynivoTrack.ArtistId is not null
                    && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
                LyricsButton.IsEnabled = !string.IsNullOrWhiteSpace(orynivoTrack.Title)
                    && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
                ArtistInfoButton.IsEnabled = orynivoTrack.ArtistId is not null
                    && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
                ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
                if (LyricsButton.IsEnabled)
                    _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
            }
            else
            {
                _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
            }

            if (!isPlexTrack)
                _ = LoadNowPlayingArtworkAsync(
                    filePath,
                    _currentNowPlayingProvider ?? _localNowPlayingProvider,
                    BuildNowPlayingTrackContext(filePath));
        }
        else if (podcastPlayback is not null)
        {
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
            ClearNowPlayingAlbum();
            _currentTrackIsFavorite = false;
            NowPlayingArtistButton.IsEnabled = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = true;
            ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowPodcastInfo);
            _ = LoadPodcastArtworkAsync(
                podcastPlayback.Podcast.ArtworkUrl,
                _playbackCts?.Token ?? CancellationToken.None);
        }
        else if (radioStation is not null)
        {
            ShowRadioNowPlaying(radioStation, info);
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
            ClearNowPlayingAlbum();
            NowPlayingArtistButton.IsEnabled = false;
            _currentTrackIsFavorite = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
            ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
            StartRadioMetadataMonitor(radioStation);
        }
        UpdateNowPlayingFavoriteButton();
        UpdateReplayGainBadge(usesNativeDsd);
        RefreshWindowsMediaMetadata();
        _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Playing);
        _windowsMediaTransport?.UpdateTimeline(
            player.Position,
            player.Duration,
            force: true);

        var outputName = _settings.OutputBackend is OutputBackend.Asio or OutputBackend.CwAsio
            ? _settings.SelectedDriverName
            : _settings.SelectedWasapiDeviceName;
        StatusTextBlock.Text = info.IsDsd && !usesNativeDsd
            ? string.Format(
                LocalizationManager.Current.PlaybackThroughWithDsdConversion,
                outputName,
                info.OutputSampleRate)
            : string.Format(LocalizationManager.Current.PlaybackThrough, outputName);

        try
        {
            using var db = AudioDatabase.OpenDefault();
            if (radioStation is not null)
            {
                _currentPlayHistoryId = db.RecordPlaybackStart(
                    filePath,
                    null,
                    null,
                    mediaType: "radio",
                    title: radioStation.Name,
                    subtitle: LocalizationManager.Current.InternetRadio,
                    externalId: radioStation.StationUuid);
            }
            else if (podcastPlayback is not null)
            {
                _currentPlayHistoryId = db.RecordPlaybackStart(
                    filePath,
                    null,
                    player.Duration.TotalSeconds > 0
                        ? player.Duration.TotalSeconds
                        : podcastPlayback.Episode.FeedDuration?.TotalSeconds,
                    mediaType: "podcast",
                    title: podcastPlayback.Episode.Title,
                    subtitle: podcastPlayback.Podcast.Name,
                    album: podcastPlayback.Podcast.Name,
                    externalId: podcastPlayback.Episode.EpisodeKey);
            }
            else
            {
                _currentPlayHistoryId = db.RecordPlaybackStart(
                    filePath,
                    db.GetTrackIdByPath(filePath),
                    player.Duration.TotalSeconds > 0 ? player.Duration.TotalSeconds : null,
                    title: NowPlayingTitleBlock.Text,
                    subtitle: NowPlayingArtistBlock.Text,
                    album: _currentAlbumTitle,
                    externalId: ResolveNowPlayingExternalId(filePath),
                    genre: ResolveNowPlayingGenre(filePath));
            }
        }
        catch
        {
            _currentPlayHistoryId = null;
        }

        try
        {
            await player.WaitForCompletionAsync();
        }
        catch (OperationCanceledException) when (!ReferenceEquals(_player, player))
        {
            return;
        }

        if (_player == player)
        {
            RecordPlaybackEnd(completed: true);
            SavePodcastProgress(completed: true);
            if (radioStation is not null || podcastPlayback is not null || !await TryPlayNextAsync())
            {
                StopPlayback();
                StatusTextBlock.Text = LocalizationManager.Current.PlaybackFinished;
            }
        }
    }

    private IReadOnlyList<GaplessPlaybackItem> BuildGaplessPlaybackItems(
        string currentFilePath,
        bool allowQueuedTracks)
    {
        if (!allowQueuedTracks ||
            _shuffleEnabled ||
            _queueIndex < 0 ||
            _queueIndex >= _queue.Count ||
            !string.Equals(
                _queue[_queueIndex].FilePath,
                currentFilePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return [ResolveGaplessPlaybackItem(currentFilePath)];
        }

        var items = new List<GaplessPlaybackItem>();
        for (var index = _queueIndex; index < _queue.Count; index++)
        {
            var path = _queue[index].FilePath;
            if (!_settings.AlwaysConvertDsdToPcm &&
                _settings.OutputBackend is OutputBackend.Asio or OutputBackend.CwAsio &&
                Path.GetExtension(path) is string extension &&
                (extension.Equals(".dsf", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".dff", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            items.Add(ResolveGaplessPlaybackItem(path));
        }

        return items.Count == 0
            ? [ResolveGaplessPlaybackItem(currentFilePath)]
            : items;
    }

    /// <summary>
    /// Builds pre-known stream characteristics for a remote server track so the
    /// player can skip the FFmpeg probe. Returns <see langword="null"/> for DSD
    /// sources (which take the native remote players) and when the server did not
    /// report a usable sample rate, so those tracks probe as before.
    /// </summary>
    /// <param name="row">Cached remote track row carrying server metadata.</param>
    /// <returns>The pre-known audio info, or <see langword="null"/> to probe.</returns>
    private static KnownAudioInfo? BuildRemotePcmKnownInfo(ContentRow row)
    {
        if (row.SampleRateHz is not > 0)
            return null;

        var format = row.Format?.Trim();
        var sourceExtension = Path.GetExtension(row.SourcePath);
        var fileNameExtension = Path.GetExtension(row.FileName);
        var isDsd =
            string.Equals(format, "DSF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DSDIFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dff", StringComparison.OrdinalIgnoreCase);
        if (isDsd)
            return null;

        return new KnownAudioInfo(
            row.SampleRateHz.Value,
            row.ChannelCount ?? 2,
            string.IsNullOrWhiteSpace(format) ? "pcm" : format.ToLowerInvariant(),
            IsDsd: false,
            format?.ToLowerInvariant());
    }

    private GaplessPlaybackItem ResolveGaplessPlaybackItem(string path)
    {
        if (_plexTracksByUrl.TryGetValue(path, out var plexTrack))
        {
            return new GaplessPlaybackItem(
                path,
                GetPcmOutputGainFactor(),
                SourcePaths: plexTrack.PlexPartUrls,
                KnownDuration: plexTrack.KnownDuration);
        }

        if (_orynivoTracksByUrl.TryGetValue(path, out var orynivoTrack))
            return new GaplessPlaybackItem(
                path,
                GetPcmOutputGainFactor(),
                KnownDuration: orynivoTrack.KnownDuration,
                KnownInfo: BuildRemotePcmKnownInfo(orynivoTrack));

        if (!CueSheetParser.IsVirtualPath(path))
            return new GaplessPlaybackItem(path, GetReplayGainFactor(path));

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(path);
            if (track is not null)
            {
                return new GaplessPlaybackItem(
                    path,
                    GetReplayGainFactor(path),
                    track.SourcePath,
                    track.SegmentStart is double start ? TimeSpan.FromSeconds(start) : null,
                    track.SegmentEnd is double end ? TimeSpan.FromSeconds(end) : null);
            }
        }
        catch
        {
        }

        return new GaplessPlaybackItem(path, GetReplayGainFactor(path));
    }

    private async Task<(IAudioPlayer Player, AudioFileInfo Info)> CreateRemoteOrynivoDsdOrPcmPlayerAsync(
        string filePath,
        GaplessPlaybackItem playbackTrack)
    {
        var driverName = _settings.SelectedDriverName ?? string.Empty;
        try
        {
            var (dsfPlayer, dsfInfo) = await RemoteDsfAudioPlayer.CreateAsync(
                filePath,
                _settings.OutputBackend,
                driverName,
                _playbackCts?.Token ?? CancellationToken.None);
            return (dsfPlayer, dsfInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            SeekDiagnostics.Log(
                "remote-dsf-player",
                $"native-skip reason={ex.GetType().Name} message={ex.Message}");
        }

        try
        {
            var (dffPlayer, dffInfo) = await RemoteDffAudioPlayer.CreateAsync(
                filePath,
                _settings.OutputBackend,
                driverName,
                _playbackCts?.Token ?? CancellationToken.None);
            return (dffPlayer, dffInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            SeekDiagnostics.Log(
                "remote-dff-player",
                $"native-fallback reason={ex.GetType().Name} message={ex.Message}");
            return await FfmpegAudioPlayer.CreateAsync(
                [playbackTrack],
                _settings.OutputBackend,
                driverName,
                _settings.EqualizerEnabled,
                _settings.EqualizerProfile,
                _playbackCts?.Token ?? CancellationToken.None);
        }
    }

    /// <summary>
    /// Determines whether a remote Orynivo Server stream should be routed through
    /// the native DSD players. When the cached server metadata identifies a
    /// concrete non-DSD format it is treated as authoritative so PCM tracks skip
    /// the native DSF/DFF probes and go straight to the gapless FFmpeg PCM path;
    /// only tracks with no usable format metadata fall back to the conservative
    /// stream-URL check.
    /// </summary>
    /// <param name="path">Remote stream URL of the track.</param>
    /// <returns>
    /// <see langword="true"/> when the track may be native DSD; otherwise
    /// <see langword="false"/>.
    /// </returns>
    private bool IsRemoteOrynivoDsdCandidate(string path)
    {
        if (!_orynivoTracksByUrl.TryGetValue(path, out var row))
            return IsConfiguredOrynivoStreamUrl(path);

        var format = row.Format?.Trim();
        var sourceExtension = Path.GetExtension(row.SourcePath);
        var fileNameExtension = Path.GetExtension(row.FileName);

        if (string.Equals(format, "DSF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DSDIFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dff", StringComparison.OrdinalIgnoreCase))
            return true;

        // Any concrete server-supplied format/extension identifies the track as
        // PCM here, so it must not probe the native DSD players. Each of those
        // probes reads remote header byte ranges, adding two wasted HTTP
        // round-trips before the FFmpeg fallback — the main reason remote
        // playback took several seconds to start on ASIO/cwASIO. Routing PCM
        // tracks through the normal FFmpeg branch also restores gapless playback
        // for them, because that branch uses the full gapless item list instead
        // of a single item. Only genuinely unknown tracks stay conservative.
        var hasKnownFormat =
            !string.IsNullOrWhiteSpace(format) ||
            !string.IsNullOrWhiteSpace(sourceExtension) ||
            !string.IsNullOrWhiteSpace(fileNameExtension);
        return !hasKnownFormat && IsConfiguredOrynivoStreamUrl(path);
    }

    private bool IsConfiguredOrynivoStreamUrl(string path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return false;

        foreach (var server in _settings.OrynivoServers ?? [])
        {
            if (!Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out var baseUri))
                continue;

            if (!string.Equals(uri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                uri.Port != baseUri.Port)
            {
                continue;
            }

            var basePath = baseUri.AbsolutePath.TrimEnd('/');
            var expectedPrefix = string.IsNullOrEmpty(basePath)
                ? "/api/stream/"
                : $"{basePath}/api/stream/";
            if (uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Resolves a history entry to its remote Orynivo Server track target.</summary>
    /// <param name="entry">The playback-history entry.</param>
    /// <param name="server">Resolved server settings.</param>
    /// <param name="trackId">Resolved server-side track identifier.</param>
    /// <returns><see langword="true"/> when the entry identifies a configured remote track.</returns>
    private bool TryGetOrynivoHistoryTarget(
        DailyHistoryEntry entry,
        out OrynivoServerSettings server,
        out long trackId)
    {
        if (TryParseOrynivoHistoryExternalId(entry.ExternalId, out var serverId, out trackId))
        {
            var matchingServer = (_settings.OrynivoServers ?? [])
                .FirstOrDefault(item => string.Equals(item.Id, serverId, StringComparison.Ordinal));
            if (matchingServer is not null)
            {
                server = matchingServer;
                return true;
            }
        }

        return TryGetOrynivoStreamUrlTarget(entry.Path, out server, out trackId);
    }

    /// <summary>Parses an Orynivo Server track identifier stored in playback history.</summary>
    /// <param name="externalId">Stored history external identifier.</param>
    /// <param name="serverId">Parsed server identifier.</param>
    /// <param name="trackId">Parsed server-side track identifier.</param>
    /// <returns><see langword="true"/> when the identifier is valid.</returns>
    private static bool TryParseOrynivoHistoryExternalId(
        string? externalId,
        out string serverId,
        out long trackId)
    {
        serverId = string.Empty;
        trackId = 0;
        if (string.IsNullOrWhiteSpace(externalId))
            return false;

        var parts = externalId.Split(':');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], "orynivo", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parts[2], "track", StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out trackId))
        {
            return false;
        }

        serverId = parts[1];
        return !string.IsNullOrWhiteSpace(serverId);
    }

    /// <summary>Resolves a configured Orynivo Server and track ID from a stream URL.</summary>
    /// <param name="path">Potential remote stream URL.</param>
    /// <param name="server">Resolved server settings.</param>
    /// <param name="trackId">Resolved server-side track identifier.</param>
    /// <returns><see langword="true"/> when the URL points at a configured server stream.</returns>
    private bool TryGetOrynivoStreamUrlTarget(
        string path,
        out OrynivoServerSettings server,
        out long trackId)
    {
        server = null!;
        trackId = 0;
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return false;

        foreach (var candidate in _settings.OrynivoServers ?? [])
        {
            if (!Uri.TryCreate(candidate.BaseUrl, UriKind.Absolute, out var baseUri))
                continue;

            if (!string.Equals(uri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                uri.Port != baseUri.Port)
            {
                continue;
            }

            var basePath = baseUri.AbsolutePath.TrimEnd('/');
            var expectedPrefix = string.IsNullOrEmpty(basePath)
                ? "/api/stream/"
                : $"{basePath}/api/stream/";
            if (!uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var idText = uri.AbsolutePath[expectedPrefix.Length..].Trim('/');
            var slashIndex = idText.IndexOf('/');
            if (slashIndex >= 0)
                idText = idText[..slashIndex];
            if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out trackId))
                continue;

            server = candidate;
            return true;
        }

        return false;
    }

    private void GaplessPlayer_OnTrackChanged(object? sender, GaplessTrackChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(sender, _player))
                return;

            RecordPlaybackEnd(completed: true, _currentPlaybackDuration.TotalSeconds);
            _currentFilePath = e.FilePath;
            _currentPlaybackDuration = e.Info.Duration;
            _playedQueuePaths.Add(e.FilePath);
            UpdateNowPlayingRowHighlights();

            if (_queueIndex + 1 < _queue.Count &&
                string.Equals(
                    _queue[_queueIndex + 1].FilePath,
                    e.FilePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _queueIndex++;
            }
            else
            {
                var matchingIndex = Enumerable.Range(0, _queue.Count)
                    .Where(index => string.Equals(
                        _queue[index].FilePath,
                        e.FilePath,
                        StringComparison.OrdinalIgnoreCase))
                    .DefaultIfEmpty(-1)
                    .First();
                if (matchingIndex >= 0)
                    _queueIndex = matchingIndex;
            }

            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            PositionSlider.IsEnabled = _player?.CanSeek == true;
            DurationTextBlock.Text = FormatTime(e.Info.Duration);
            UpdateGaplessNowPlaying(e.FilePath, e.Info);
            StartLocalPlaybackHistory(e.FilePath);
        });
    }

    private void UpdateGaplessNowPlaying(string filePath, AudioFileInfo info)
    {
        var metadata = GetPlaylistMetadata(filePath);
        var filename = metadata?.DisplayTitle ?? Path.GetFileNameWithoutExtension(filePath);
        NowPlayingTitleBlock.Text = filename;
        NowPlayingArtistBlock.Text = metadata?.Artist ?? SelectedDriverTextBlock.Text;
        ClearNowPlayingAlbum();
        FileInfoTextBlock.Text = info.IsDsd
            ? $"{info.ContainerName.ToUpperInvariant()}  ·  {LocalizationManager.Current.DsdToPcmOutput}  ·  {info.OutputSampleRate:N0} Hz"
            : info.SourceSampleRate != info.OutputSampleRate
                ? $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz → {info.OutputSampleRate:N0} Hz  ·  {info.Channels} ch"
                : $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  {info.Channels} ch";

        _currentNowPlayingProvider = _localNowPlayingProvider;
        _currentOrynivoTrackRow = null;
        var isPlexTrack = _plexTracksByUrl.TryGetValue(filePath, out var plexTrack);
        var isOrynivoTrack = _orynivoTracksByUrl.TryGetValue(filePath, out var orynivoTrack);
        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(filePath);
            var artist = db.GetArtistByTrackPath(filePath);
            var trackInfo = db.GetTrackIdAndFavorite(filePath);
            var navigationIds = db.GetTrackNavigationIds(filePath);
            _currentTrackId = trackInfo?.Id;
            _currentTrackIsFavorite = trackInfo?.IsFavorite ?? false;
            _currentArtistId = artist?.Id;
            _currentArtistName = artist?.Artist;
            _currentAlbumId = navigationIds.AlbumId;
            NowPlayingArtistButton.IsEnabled = artist is not null;
            LyricsButton.IsEnabled = track is not null;
            ArtistInfoButton.IsEnabled = artist is not null;
            if (track is not null)
            {
                NowPlayingTitleBlock.Text = track.Title ?? filename;
                NowPlayingArtistBlock.Text = track.Artist ?? string.Empty;
                SetNowPlayingAlbum(track.Album, navigationIds.AlbumId, navigationIds.AlbumId is not null);
            }
        }
        catch
        {
            NowPlayingArtworkImage.Source = null;
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
            ClearNowPlayingAlbum();
            _currentTrackIsFavorite = false;
            NowPlayingArtistButton.IsEnabled = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
        }

        if (isPlexTrack && plexTrack is not null)
        {
            NowPlayingTitleBlock.Text = plexTrack.Title ?? filename;
            NowPlayingArtistBlock.Text = plexTrack.Artist ?? string.Empty;
            NowPlayingArtworkImage.Source = null;
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = plexTrack.Artist;
            SetNowPlayingAlbum(plexTrack.Album, null, canNavigate: false);
            _currentTrackIsFavorite = false;
            NowPlayingArtistButton.IsEnabled = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
        }
        else if (isOrynivoTrack && orynivoTrack is not null)
        {
            NowPlayingTitleBlock.Text = orynivoTrack.Title ?? filename;
            NowPlayingArtistBlock.Text = orynivoTrack.Artist ?? string.Empty;
            NowPlayingArtworkImage.Source = orynivoTrack.Thumbnail ?? orynivoTrack.Artwork;
            LyricsBackgroundImage.Source = orynivoTrack.Artwork ?? orynivoTrack.Thumbnail;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = orynivoTrack.Artist;
            SetNowPlayingAlbum(
                orynivoTrack.Album,
                orynivoTrack.AlbumId,
                orynivoTrack.OrynivoServer is not null && orynivoTrack.AlbumId is not null);
            _currentTrackIsFavorite = false;
            _currentOrynivoTrackRow = orynivoTrack;
            if (orynivoTrack.OrynivoServer is { } trackServer)
            {
                _currentNowPlayingProvider = CreateOrynivoNowPlayingProvider(trackServer);
                if (orynivoTrack.Id is long favoriteTrackId)
                    _currentTrackIsFavorite = IsOrynivoFavorite(trackServer, "Track", favoriteTrackId);
            }
            // The now-playing artist button navigates within the remote library
            // using the row's server and artist ID (see NowPlayingArtistButton_OnClick).
            NowPlayingArtistButton.IsEnabled = orynivoTrack.ArtistId is not null
                && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
            LyricsButton.IsEnabled = !string.IsNullOrWhiteSpace(orynivoTrack.Title)
                && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
            ArtistInfoButton.IsEnabled = orynivoTrack.ArtistId is not null
                && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
            if (LyricsButton.IsEnabled)
                _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
        }
        else
        {
            _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
        }

        if (!isPlexTrack)
            _ = LoadNowPlayingArtworkAsync(
                filePath,
                _currentNowPlayingProvider ?? _localNowPlayingProvider,
                BuildNowPlayingTrackContext(filePath));

        UpdateNowPlayingFavoriteButton();
        UpdateReplayGainBadge(nativeDsdOutput: false);
        RefreshWindowsMediaMetadata();
        _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Playing);
        if (_player is not null)
        {
            _windowsMediaTransport?.UpdateTimeline(
                _player.Position,
                _player.Duration,
                force: true);
        }
    }

    private void RefreshWindowsMediaMetadata()
    {
        if (_windowsMediaTransport is null)
            return;

        var title = NowPlayingTitleBlock.Text ?? string.Empty;
        var artist = NowPlayingArtistBlock.Text ?? string.Empty;
        var album = string.Empty;
        string? artworkPath = null;
        Uri? artworkUri = null;

        if (_currentPodcastPlayback is { } podcastPlayback)
        {
            album = podcastPlayback.Podcast.Name;
            if (Uri.TryCreate(
                    podcastPlayback.Podcast.ArtworkUrl,
                    UriKind.Absolute,
                    out var podcastArtworkUri))
            {
                artworkUri = podcastArtworkUri;
            }
        }
        else if (_currentRadioStation is { } radioStation)
        {
            album = radioStation.Name;
            artworkPath = _currentRadioArtworkPath;
            if (string.IsNullOrWhiteSpace(artworkPath) &&
                Uri.TryCreate(radioStation.Favicon, UriKind.Absolute, out var radioArtworkUri))
            {
                artworkUri = radioArtworkUri;
            }
        }
        else if (_plexTracksByUrl.TryGetValue(_currentFilePath, out var plexTrack))
        {
            album = plexTrack.Album ?? string.Empty;
        }
        else if (_orynivoTracksByUrl.TryGetValue(_currentFilePath, out var orynivoTrack))
        {
            album = orynivoTrack.Album ?? string.Empty;
            if (orynivoTrack.OrynivoServer is { } server &&
                orynivoTrack.Id is long trackId &&
                Uri.TryCreate(
                    OrynivoServerClient.GetTrackArtworkUrl(server, trackId, 320),
                    UriKind.Absolute,
                    out var orynivoArtworkUri))
            {
                artworkUri = orynivoArtworkUri;
            }
        }
        else
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var track = db.GetByPath(_currentFilePath);
                var artwork = db.GetArtworkPathsByTrackPath(_currentFilePath);
                album = track?.Album ?? string.Empty;
                artworkPath = artwork?.OriginalPath ??
                              artwork?.Thumb320Path ??
                              artwork?.Thumb96Path;
            }
            catch
            {
                // Metadata is optional and must never affect audio playback.
            }
        }

        _ = _windowsMediaTransport.UpdateMetadataAsync(new WindowsMediaMetadata(
            title,
            artist,
            album,
            artworkPath,
            artworkUri));
    }

    private static readonly Color DefaultTransportAccent = Color.Parse("#20D9E8");

    /// <summary>
    /// Updates the cover-derived transport accent brush (progress fill, slider
    /// thumb, play button) from the current now-playing artwork. Falls back to the
    /// app accent when there is no artwork or extraction fails.
    /// </summary>
    /// <param name="source">The current now-playing artwork image, or <see langword="null"/>.</param>
    private void UpdateTransportAccentFromArtwork(IImage? source)
    {
        var fallback = GetThemeAccentColor();
        var color = source is Bitmap bitmap
            ? ExtractAccentColor(bitmap) ?? fallback
            : fallback;

        if (this.TryFindResource("AppTransportAccentBrush", out var resource) &&
            resource is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        if (this.TryFindResource("AppTransportAccentTextBrush", out var textResource) &&
            textResource is SolidColorBrush textBrush)
        {
            textBrush.Color = GetReadableTextColor(color);
        }
    }

    /// <summary>
    /// Resolves the current theme accent colour used when no artwork accent is available.
    /// </summary>
    /// <returns>The theme accent colour, or the built-in default when the resource is unavailable.</returns>
    private Color GetThemeAccentColor()
    {
        return this.TryFindResource("AppAccentBrush", out var resource) &&
            resource is SolidColorBrush brush
            ? brush.Color
            : DefaultTransportAccent;
    }

    /// <summary>
    /// Extracts a vibrant accent color from a bitmap by sampling a small scaled
    /// copy, binning qualifying pixels by hue (weighted by saturation × value),
    /// and normalising the dominant hue into a punchy, readable accent.
    /// </summary>
    /// <param name="bitmap">Source artwork bitmap.</param>
    /// <returns>The extracted accent color, or <see langword="null"/> on failure or when the image is colourless.</returns>
    private static Color? ExtractAccentColor(Bitmap bitmap)
    {
        try
        {
            const int dim = 24;
            using var small = bitmap.CreateScaledBitmap(new PixelSize(dim, dim), BitmapInterpolationMode.MediumQuality);
            var stride = dim * 4;
            var buffer = new byte[stride * dim];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                small.CopyPixels(new PixelRect(0, 0, dim, dim), handle.AddrOfPinnedObject(), buffer.Length, stride);
            }
            finally
            {
                handle.Free();
            }

            const int bins = 12;
            var weight = new double[bins];
            var rSum = new double[bins];
            var gSum = new double[bins];
            var bSum = new double[bins];

            for (var i = 0; i < buffer.Length; i += 4)
            {
                double b = buffer[i], g = buffer[i + 1], r = buffer[i + 2], a = buffer[i + 3];
                if (a < 32)
                    continue;

                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                var value = max / 255.0;
                var sat = max <= 0 ? 0 : (max - min) / max;

                // Skip near-gray, near-black, and blown-out near-white pixels.
                if (sat < 0.18 || value < 0.18 || (value > 0.96 && sat < 0.25))
                    continue;

                var bin = (int)(RgbToHue(r, g, b) / 360.0 * bins) % bins;
                var w = sat * value;
                weight[bin] += w;
                rSum[bin] += r * w;
                gSum[bin] += g * w;
                bSum[bin] += b * w;
            }

            var best = -1;
            for (var i = 0; i < bins; i++)
                if (weight[i] > 0 && (best < 0 || weight[i] > weight[best]))
                    best = i;
            if (best < 0)
                return null;

            return AdjustAccent(rSum[best] / weight[best], gSum[best] / weight[best], bSum[best] / weight[best]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the hue (0–360) of an 8-bit RGB triple.</summary>
    /// <param name="r">Red component (0–255).</param>
    /// <param name="g">Green component (0–255).</param>
    /// <param name="b">Blue component (0–255).</param>
    /// <returns>The hue in degrees.</returns>
    private static double RgbToHue(double r, double g, double b)
    {
        r /= 255; g /= 255; b /= 255;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        if (delta <= 0)
            return 0;

        double hue;
        if (max == r) hue = 60 * (((g - b) / delta) % 6);
        else if (max == g) hue = 60 * (((b - r) / delta) + 2);
        else hue = 60 * (((r - g) / delta) + 4);
        return hue < 0 ? hue + 360 : hue;
    }

    /// <summary>Normalises an averaged RGB accent into a saturated, mid-bright colour.</summary>
    /// <param name="r">Red component (0–255).</param>
    /// <param name="g">Green component (0–255).</param>
    /// <param name="b">Blue component (0–255).</param>
    /// <returns>The adjusted accent color.</returns>
    private static Color AdjustAccent(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var value = max / 255.0;
        var sat = max <= 0 ? 0 : (max - min) / max;
        var hue = RgbToHue(r, g, b);

        sat = Math.Clamp(sat * 1.15 + 0.1, 0.45, 1.0);
        value = Math.Clamp(value, 0.6, 0.86);
        return HsvToColor(hue, sat, value);
    }

    /// <summary>Converts an HSV triple to an opaque <see cref="Color"/>.</summary>
    /// <param name="h">Hue in degrees (0–360).</param>
    /// <param name="s">Saturation (0–1).</param>
    /// <param name="v">Value/brightness (0–1).</param>
    /// <returns>The resulting color.</returns>
    private static Color HsvToColor(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        var m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromRgb(
            (byte)Math.Clamp((r + m) * 255, 0, 255),
            (byte)Math.Clamp((g + m) * 255, 0, 255),
            (byte)Math.Clamp((b + m) * 255, 0, 255));
    }

    /// <summary>Returns a dark or light text colour with readable contrast over a background colour.</summary>
    /// <param name="background">The background colour behind the text.</param>
    /// <returns>A text colour suitable for the supplied background.</returns>
    private static Color GetReadableTextColor(Color background)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.03928
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Linear(background.R) +
                        0.7152 * Linear(background.G) +
                        0.0722 * Linear(background.B);
        return luminance > 0.42
            ? Color.Parse("#102033")
            : Colors.White;
    }

    /// <summary>
    /// Resolves the genre to store with a play-history entry for non-local tracks
    /// (remote Orynivo Server and Plex), so genre statistics include them. Local
    /// tracks return <see langword="null"/> because their genre is resolved through
    /// the <c>play_history.track_id</c> join.
    /// </summary>
    /// <param name="filePath">Playing file path or stream URL.</param>
    /// <returns>The captured genre, or <see langword="null"/> when none applies.</returns>
    private string? ResolveNowPlayingGenre(string filePath)
    {
        if (_currentOrynivoTrackRow?.Genre is { } orynivoGenre && !string.IsNullOrWhiteSpace(orynivoGenre))
            return orynivoGenre;
        if (_plexTracksByUrl.TryGetValue(filePath, out var plexRow) && !string.IsNullOrWhiteSpace(plexRow.Genre))
            return plexRow.Genre;
        return null;
    }

    /// <summary>
    /// Resolves a stable source identifier to store with playback history for
    /// remote tracks whose local history row has no database track ID.
    /// </summary>
    /// <param name="filePath">Playing file path or stream URL.</param>
    /// <returns>A source-specific identifier, or <see langword="null"/> for local tracks.</returns>
    private string? ResolveNowPlayingExternalId(string filePath)
    {
        if (_currentOrynivoTrackRow is { OrynivoServer: { } server, Id: long trackId } &&
            string.Equals(_currentOrynivoTrackRow.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return BuildOrynivoHistoryExternalId(server, trackId);
        }

        if (_orynivoTracksByUrl.TryGetValue(filePath, out var row) &&
            row is { OrynivoServer: { } rowServer, Id: long rowTrackId })
        {
            return BuildOrynivoHistoryExternalId(rowServer, rowTrackId);
        }

        if (_plexTracksByUrl.TryGetValue(filePath, out var plexRow) &&
            plexRow is { PlexServerId: { } plexServerId, ExternalId: { } plexRatingKey } &&
            !string.IsNullOrWhiteSpace(plexServerId) && !string.IsNullOrWhiteSpace(plexRatingKey))
        {
            return BuildPlexHistoryExternalId(
                plexServerId, plexRatingKey, plexRow.PlexAlbumRatingKey, plexRow.PlexArtistRatingKey);
        }

        return null;
    }

    /// <summary>Builds the playback-history external ID for a remote Orynivo Server track.</summary>
    /// <param name="server">The remote server owning the track.</param>
    /// <param name="trackId">Server-side track identifier.</param>
    /// <returns>A compact, parseable history identifier.</returns>
    private static string BuildOrynivoHistoryExternalId(OrynivoServerSettings server, long trackId) =>
        $"orynivo:{server.Id}:track:{trackId}";

    /// <summary>
    /// Builds a stable Plex playback-history external ID carrying the server, track,
    /// album, and artist rating keys so a Plex history entry stays resolvable to its
    /// in-library album and artist. Rating keys are numeric and contain no colons.
    /// </summary>
    /// <param name="serverId">Plex server identifier.</param>
    /// <param name="ratingKey">Plex track rating key.</param>
    /// <param name="albumRatingKey">Plex album (parent) rating key, or <see langword="null"/>.</param>
    /// <param name="artistRatingKey">Plex artist (grandparent) rating key, or <see langword="null"/>.</param>
    /// <returns>A compact, parseable history identifier of the form <c>plex:server:track:album:artist</c>.</returns>
    private static string BuildPlexHistoryExternalId(
        string serverId,
        string ratingKey,
        string? albumRatingKey,
        string? artistRatingKey) =>
        $"plex:{serverId}:{ratingKey}:{albumRatingKey ?? string.Empty}:{artistRatingKey ?? string.Empty}";

    private void StartLocalPlaybackHistory(string filePath)
    {
        try
        {
            using var db = AudioDatabase.OpenDefault();
            _currentPlayHistoryId = db.RecordPlaybackStart(
                filePath,
                db.GetTrackIdByPath(filePath),
                _currentPlaybackDuration.TotalSeconds > 0
                    ? _currentPlaybackDuration.TotalSeconds
                    : null,
                title: NowPlayingTitleBlock.Text,
                subtitle: NowPlayingArtistBlock.Text,
                album: _currentAlbumTitle,
                externalId: ResolveNowPlayingExternalId(filePath),
                genre: ResolveNowPlayingGenre(filePath));
        }
        catch
        {
            _currentPlayHistoryId = null;
        }
    }

    private async void PreviousButton_OnClick(object? sender, RoutedEventArgs e) =>
        await PlayPreviousAsync();

    private async Task PlayPreviousAsync()
    {
        if (!TryMoveToPreviousQueueIndex())
            return;

        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private async void NextButton_OnClick(object? sender, RoutedEventArgs e) =>
        await PlayNextAsync();

    private async Task PlayNextAsync()
    {
        if (!TryMoveToNextQueueIndex())
            return;

        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private void StopPlayback()
    {
        var player = StopPlaybackCore();
        player?.Dispose();
    }

    private async Task StopPlaybackAsync()
    {
        var player = StopPlaybackCore();
        if (player is not null)
        {
            var disposalTask = Task.Run(() =>
            {
                try
                {
                    player.Dispose();
                }
                catch (Exception ex)
                {
                    CrashLogger.Log(ex, "Background audio-player disposal");
                }
            });
            await Task.WhenAny(disposalTask, Task.Delay(TimeSpan.FromSeconds(2)));
        }
    }

    private IAudioPlayer? StopPlaybackCore()
    {
        RecordPlaybackEnd(completed: false);
        SavePodcastProgress(completed: false);
        CancelAndDispose(ref _radioMetadataCts);
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = null;
        var player = _player;
        _player = null;
        _currentPlaybackDuration = TimeSpan.Zero;
        UpdateNowPlayingRowHighlights();

        PlayButton.IsEnabled   = true;
        SetPlayPauseIcon(isPlaying: false);
        RefreshQueueNavigationButtons();
        _isSeekingWithSlider = false;
        PositionSlider.IsEnabled = false;
        ClearTransportWaveform();
        _transportTimer.Stop();
        CancelLyricsLoad();
        CancelArtistProfileLoad();
        ClearLyrics();

        NowPlayingTitleBlock.Text  = "";
        NowPlayingArtistBlock.Text = "";
        ClearNowPlayingAlbum();
        FileInfoTextBlock.Text     = "";
        ReplayGainBadgeBorder.IsVisible = false;
        NowPlayingArtworkImage.Source = null;
        LyricsBackgroundImage.Source = null;
        _currentTrackId = null;
        _currentArtistId = null;
        _currentArtistName = null;
        _currentTrackIsFavorite = false;
        _currentRadioStation = null;
        _currentRadioArtworkPath = null;
        _currentPodcastPlayback = null;
        _windowsMediaTransport?.Clear();
        LyricsButton.IsEnabled = false;
        ArtistInfoButton.IsEnabled = false;
        ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
        PodcastInfoView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        ClearRadioNowPlaying();
        UpdateNowPlayingFavoriteButton();
        return player;
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        var current = Interlocked.Exchange(ref source, null);
        if (current is null)
            return;
        try
        {
            current.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        current.Dispose();
    }

    private void RecordPlaybackEnd(bool completed, double? positionSeconds = null)
    {
        if (_currentPlayHistoryId is not long historyId)
            return;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.RecordPlaybackEnd(
                historyId,
                positionSeconds ?? _player?.Position.TotalSeconds ?? 0,
                completed);
        }
        catch { }
        finally
        {
            _currentPlayHistoryId = null;
        }
    }

    private void SavePodcastProgress(bool completed)
    {
        if (_currentPodcastPlayback is not { Podcast.Id: > 0 } playback ||
            _player is null)
            return;

        var duration = _player.Duration.TotalSeconds > 0
            ? _player.Duration.TotalSeconds
            : playback.Episode.FeedDuration?.TotalSeconds;
        var position = completed && duration is > 0
            ? duration.Value
            : _player.Position.TotalSeconds;
        var isCompleted = completed ||
                          (duration is > 0 && position / duration.Value >= 0.95);
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.SavePodcastEpisodeProgress(
                playback.Podcast.Id,
                playback.Episode.EpisodeKey,
                position,
                duration,
                isCompleted);
            UpdateVisiblePodcastEpisodeProgress(
                playback.Episode.EpisodeKey,
                position,
                duration,
                isCompleted);
        }
        catch
        {
        }

        _lastPodcastProgressSave = DateTimeOffset.UtcNow;
        if (isCompleted)
            _currentPodcastPlayback = null;
    }

    private void UpdateVisiblePodcastEpisodeProgress(
        string episodeKey,
        double positionSeconds,
        double? durationSeconds,
        bool completed)
    {
        if (PodcastEpisodesDataGrid.ItemsSource is not IEnumerable<PodcastEpisodeViewModel> rows)
            return;
        var row = rows.FirstOrDefault(item =>
            string.Equals(item.Episode.EpisodeKey, episodeKey, StringComparison.Ordinal));
        if (row is null)
            return;

        var replacement = CreatePodcastEpisodeRow(
            row.Episode,
            new Dictionary<string, PodcastEpisodeProgress>(StringComparer.Ordinal)
            {
                [episodeKey] = new(
                    episodeKey,
                    positionSeconds,
                    durationSeconds,
                    completed)
            });
        if (PodcastEpisodesDataGrid.ItemsSource is IList<PodcastEpisodeViewModel> list)
        {
            var index = list.IndexOf(row);
            if (index >= 0)
            {
                list[index] = replacement;
                { var _tmp = PodcastEpisodesDataGrid.ItemsSource; PodcastEpisodesDataGrid.ItemsSource = null; PodcastEpisodesDataGrid.ItemsSource = _tmp; };
                UpdateVisiblePodcastStatistics(list);
            }
        }
    }

    private void UpdateVisiblePodcastStatistics(IEnumerable<PodcastEpisodeViewModel> source)
    {
        var rows = source.ToList();
        var completed = rows.Count(row =>
            string.Equals(row.Status, LocalizationManager.Current.PodcastPlayed, StringComparison.Ordinal));
        var started = rows.Count(row =>
            string.Equals(row.Status, LocalizationManager.Current.PodcastInProgress, StringComparison.Ordinal));
        PodcastEpisodesStatistics.Text = string.Join(
            "  ·  ",
            string.Format(LocalizationManager.Current.PodcastEpisodeTotal, rows.Count),
            string.Format(LocalizationManager.Current.PodcastEpisodeUnheard, rows.Count - completed),
            string.Format(LocalizationManager.Current.PodcastEpisodeStarted, started));
    }

    private async Task<bool> TryPlayNextAsync()
    {
        if (!TryMoveToNextQueueIndex())
            return false;

        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); return true; }
        catch { return false; }
    }

    private void MaybeStartNonGaplessFadeTransition(TimeSpan visiblePosition)
    {
        if (_nonGaplessFadeTransitionInProgress ||
            _settings.NonGaplessCrossfadeSeconds <= 0 ||
            _currentRadioStation is not null ||
            _currentPodcastPlayback is not null ||
            _player is null ||
            _player.Duration <= TimeSpan.Zero ||
            IsContinuousGaplessQueueActive() ||
            !HasNextQueueItem())
        {
            return;
        }

        var fade = TimeSpan.FromSeconds(Math.Clamp(_settings.NonGaplessCrossfadeSeconds, 0.5, 10));
        if (_player.Duration <= fade + TimeSpan.FromSeconds(1))
            return;
        if (_player.Duration - visiblePosition > fade)
            return;

        _ = RunNonGaplessFadeTransitionAsync(fade);
    }

    private bool IsContinuousGaplessQueueActive() =>
        _player is IGaplessAudioPlayer &&
        !_shuffleEnabled &&
        _queueIndex >= 0 &&
        _queueIndex + 1 < _queue.Count &&
        _queueIndex < _queue.Count &&
        string.Equals(_queue[_queueIndex].FilePath, _currentFilePath, StringComparison.OrdinalIgnoreCase);

    private bool HasNextQueueItem()
    {
        if (_shuffleEnabled)
            return HasUnplayedShuffleCandidate();
        return _queueIndex == -1
            ? _queue.Count > 0
            : _queueIndex + 1 < _queue.Count;
    }

    private async Task RunNonGaplessFadeTransitionAsync(TimeSpan fade)
    {
        if (_nonGaplessFadeTransitionInProgress || _player is null)
            return;

        _nonGaplessFadeTransitionInProgress = true;
        var outgoing = _player;
        var outgoingVolume = outgoing.Volume;
        try
        {
            await FadePlayerVolumeAsync(outgoing, outgoingVolume, 0f, fade);
            if (!ReferenceEquals(_player, outgoing) || !TryMoveToNextQueueIndex())
                return;

            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();

            var nextPath = _queue[_queueIndex].FilePath;
            var playbackTask = StartPlaybackAsync(nextPath);
            _ = playbackTask.ContinueWith(task =>
            {
                if (task.Exception is { } exception)
                    CrashLogger.Log(exception.GetBaseException(), "Non-gapless fade playback");
            }, TaskContinuationOptions.OnlyOnFaulted);

            var incoming = await WaitForCurrentPlayerReplacementAsync(outgoing, TimeSpan.FromSeconds(3));
            if (incoming is null)
                return;
            var targetVolume = GetNormalPlayerVolume();
            incoming.Volume = 0f;
            await FadePlayerVolumeAsync(incoming, 0f, targetVolume, fade);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Non-gapless fade transition");
        }
        finally
        {
            if (ReferenceEquals(_player, outgoing))
                outgoing.Volume = outgoingVolume;
            _nonGaplessFadeTransitionInProgress = false;
        }
    }

    private async Task<IAudioPlayer?> WaitForCurrentPlayerReplacementAsync(
        IAudioPlayer outgoing,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_player is { } current && !ReferenceEquals(current, outgoing))
                return current;
            await Task.Delay(25);
        }
        return _player is { } player && !ReferenceEquals(player, outgoing) ? player : null;
    }

    private async Task FadePlayerVolumeAsync(
        IAudioPlayer player,
        float from,
        float to,
        TimeSpan duration)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(duration.TotalMilliseconds / 50d));
        for (var step = 1; step <= steps; step++)
        {
            if (!ReferenceEquals(_player, player) && step > 1)
                return;
            var ratio = (float)step / steps;
            player.Volume = from + ((to - from) * ratio);
            await Task.Delay(TimeSpan.FromMilliseconds(duration.TotalMilliseconds / steps));
        }
        player.Volume = to;
    }

    private float GetNormalPlayerVolume() =>
        _settings.OutputBackend == OutputBackend.Wasapi && _endpointVolumeSynchronizer is not null
            ? 1.0f
            : (float)VolumeSlider.Value;

    private void RefreshQueueNavigationButtons()
    {
        if (_shuffleEnabled)
        {
            PreviousButton.IsEnabled = _shuffleHistoryPosition > 0;
            NextButton.IsEnabled =
                _shuffleHistoryPosition + 1 < _shuffleHistory.Count ||
                HasUnplayedShuffleCandidate();
            _windowsMediaTransport?.SetNavigationCapabilities(
                PreviousButton.IsEnabled,
                NextButton.IsEnabled);
            return;
        }

        PreviousButton.IsEnabled = _queueIndex > 0 && _queueIndex < _queue.Count;
        NextButton.IsEnabled =
            (_queueIndex == -1 && _queue.Count > 0) ||
            (_queueIndex >= 0 && _queueIndex + 1 < _queue.Count);
        _windowsMediaTransport?.SetNavigationCapabilities(
            PreviousButton.IsEnabled,
            NextButton.IsEnabled);
    }

    private void ShuffleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _shuffleEnabled = !_shuffleEnabled;
        ResetShuffleHistory();
        UpdateShuffleButton();
        RefreshQueueNavigationButtons();
    }

    private void ResetQueuePlaybackState()
    {
        _playedQueuePaths.Clear();
        ResetShuffleHistory();
    }

    private void ResetShuffleHistory()
    {
        _shuffleHistory.Clear();
        _shuffleHistoryPosition = -1;
        if (_queueIndex < 0 || _queueIndex >= _queue.Count)
            return;

        _shuffleHistory.Add(_queueIndex);
        _shuffleHistoryPosition = 0;
    }

    private bool TryMoveToPreviousQueueIndex()
    {
        if (!_shuffleEnabled)
        {
            if (_queueIndex <= 0 || _queueIndex >= _queue.Count)
                return false;
            _queueIndex--;
            return true;
        }

        if (_shuffleHistoryPosition <= 0)
            return false;
        _shuffleHistoryPosition--;
        _queueIndex = _shuffleHistory[_shuffleHistoryPosition];
        return true;
    }

    private bool TryMoveToNextQueueIndex()
    {
        if (!_shuffleEnabled)
        {
            if (_queueIndex == -1 && _queue.Count > 0)
            {
                _queueIndex = 0;
                return true;
            }
            if (_queueIndex < 0 || _queueIndex + 1 >= _queue.Count)
                return false;
            _queueIndex++;
            return true;
        }

        if (_shuffleHistoryPosition + 1 < _shuffleHistory.Count)
        {
            _shuffleHistoryPosition++;
            _queueIndex = _shuffleHistory[_shuffleHistoryPosition];
            return true;
        }

        var candidates = Enumerable.Range(0, _queue.Count)
            .Where(index =>
                index != _queueIndex &&
                !_playedQueuePaths.Contains(_queue[index].FilePath))
            .ToList();
        if (candidates.Count == 0)
            return false;

        _queueIndex = candidates[Random.Shared.Next(candidates.Count)];
        _shuffleHistory.Add(_queueIndex);
        _shuffleHistoryPosition = _shuffleHistory.Count - 1;
        return true;
    }

    private bool HasUnplayedShuffleCandidate() =>
        Enumerable.Range(0, _queue.Count).Any(index =>
            index != _queueIndex &&
            !_playedQueuePaths.Contains(_queue[index].FilePath));

    private void UpdateShuffleButton()
    {
        ShuffleButton.Background = new SolidColorBrush(
            _shuffleEnabled
                ? Color.FromRgb(0x6C, 0x63, 0xFF)
                : Color.FromRgb(0x25, 0x26, 0x40));
        ShuffleButton.Foreground = _shuffleEnabled
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xEE));
    }

    private void TransportControlsHeaderGrid_OnLayoutChanged(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateResponsivePlaybackControls);

    private void TransportControlsHeaderGrid_OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateResponsivePlaybackControls);

    private void UpdateResponsivePlaybackControls()
    {
        const double gap = 12;
        var centeredPlaybackLeft =
            (TransportControlsHeaderGrid.Bounds.Width - PlaybackControlsPanel.Bounds.Width) / 2;
        var requiredLeft = TransportActionPanel.Bounds.Width + gap;
        var shift = Math.Max(0, requiredLeft - centeredPlaybackLeft);
        var maximumShift = Math.Max(
            0,
            TransportControlsHeaderGrid.Bounds.Width -
            PlaybackControlsPanel.Bounds.Width -
            centeredPlaybackLeft);

        if (PlaybackControlsPanel.RenderTransform is Avalonia.Media.TranslateTransform translate)
            translate.X = Math.Min(shift, maximumShift);
    }

    private void SetPlayPauseIcon(bool isPlaying)
    {
        PlayPauseIcon.Data = Geometry.Parse(isPlaying
            ? "M 6 4 H 9 V 16 H 6 Z M 11 4 H 14 V 16 H 11 Z"
            : "M 8 4 L 17 10 L 8 16 Z");
    }

    // ------------------------------------------------------------------
    // Pause / Seek / Volume
    // ------------------------------------------------------------------

    private async void PositionSlider_OnPreviewMouseLeftButtonUp(object? sender, PointerReleasedEventArgs e) =>
        await CommitPositionSliderSeekAsync(e.Pointer);

    private void PositionSlider_OnPreviewMouseLeftButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (_player?.CanSeek != true || PositionSlider.Bounds.Width <= 0)
            return;

        _isSeekingWithSlider = true;
        _positionSliderSeekStartedAt = DateTimeOffset.UtcNow;
        e.Pointer.Capture(PositionSlider);
        PositionSlider.SetValueFromPoint(e.GetPosition(PositionSlider));
        e.Handled = true;
    }

    private async void PositionSlider_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSeekingWithSlider || _player?.CanSeek != true)
        {
            return;
        }
        if (!e.GetCurrentPoint(PositionSlider).Properties.IsLeftButtonPressed)
        {
            await CommitPositionSliderSeekAsync(e.Pointer);
            return;
        }

        PositionSlider.SetValueFromPoint(e.GetPosition(PositionSlider));
        e.Handled = true;
    }

    private void PositionSlider_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isSeekingWithSlider)
            return;

        _isSeekingWithSlider = false;
        RefreshTransport();
    }

    private void PositionSlider_OnValueChanged(object? sender, EventArgs e)
    {
        if (_isSeekingWithSlider)
            CurrentTimeTextBlock.Text = FormatTime(TimeSpan.FromSeconds(PositionSlider.Value));
    }

    private void RefreshTransport()
    {
        if (_player is null) return;
        if (_isSeekingWithSlider &&
            DateTimeOffset.UtcNow - _positionSliderSeekStartedAt > TimeSpan.FromSeconds(5))
        {
            _isSeekingWithSlider = false;
        }

        var visiblePosition = _pendingTransportSeekPosition ?? _player.Position;
        CurrentTimeTextBlock.Text = FormatTime(visiblePosition);
        DurationTextBlock.Text    = FormatTime(_player.Duration);
        PositionSlider.Maximum    = Math.Max(1, _player.Duration.TotalSeconds);
        if (!_isSeekingWithSlider)
            PositionSlider.Value = Math.Min(PositionSlider.Maximum, visiblePosition.TotalSeconds);
        _windowsMediaTransport?.UpdateTimeline(visiblePosition, _player.Duration);
        if (_currentPodcastPlayback is not null &&
            DateTimeOffset.UtcNow - _lastPodcastProgressSave >= TimeSpan.FromSeconds(5))
        {
            SavePodcastProgress(completed: false);
        }
        UpdateActiveLyric(_player.Position);
        MaybeStartNonGaplessFadeTransition(visiblePosition);
    }

    /// <summary>Commits the pending waveform-progress seek and leaves preview mode.</summary>
    /// <param name="pointer">Pointer that owns capture, or <see langword="null"/>.</param>
    private async Task CommitPositionSliderSeekAsync(IPointer? pointer)
    {
        if (!_isSeekingWithSlider)
            return;

        var target = TimeSpan.FromSeconds(PositionSlider.Value);
        var seekVersion = Interlocked.Increment(ref _transportSeekVersion);
        var stopwatch = Stopwatch.StartNew();
        _pendingTransportSeekPosition = target;
        _isSeekingWithSlider = false;
        pointer?.Capture(null);
        CurrentTimeTextBlock.Text = FormatTime(target);
        PositionSlider.Value = Math.Min(PositionSlider.Maximum, target.TotalSeconds);
        var durationForLog = _player?.Duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) ?? "none";
        SeekDiagnostics.Log(
            "transport-ui",
            $"seek-request version={seekVersion} target={target.TotalSeconds:F3}s duration={durationForLog}s canSeek={_player?.CanSeek} path={SeekDiagnostics.SanitizeUrl(_currentFilePath)}");
        try
        {
            if (seekVersion != Volatile.Read(ref _transportSeekVersion))
            {
                SeekDiagnostics.Log(
                    "transport-ui",
                    $"seek-skipped-stale version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}");
                return;
            }
            if (_player is not null && _player.CanSeek)
            {
                await _player.SeekAsync(target);
                SeekDiagnostics.Log(
                    "transport-ui",
                    $"seek-complete version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds} playerPosition={_player.Position.TotalSeconds:F3}s");
            }
            else
            {
                SeekDiagnostics.Log(
                    "transport-ui",
                    $"seek-skipped-unavailable version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}");
            }
        }
        catch (OperationCanceledException)
        {
            // Track changes and stop requests can cancel an in-flight seek.
            SeekDiagnostics.Log(
                "transport-ui",
                $"seek-canceled version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            SeekDiagnostics.Log(
                "transport-ui",
                $"seek-failed version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}",
                ex);
            CrashLogger.Log(ex, "Transport seek");
            StatusTextBlock.Text = ex.Message;
        }
        finally
        {
            if (seekVersion == Volatile.Read(ref _transportSeekVersion))
                _pendingTransportSeekPosition = null;
            RefreshTransport();
            if (_player is not null)
            {
                _windowsMediaTransport?.UpdateTimeline(
                    _player.Position,
                    _player.Duration,
                    force: true);
            }
        }
    }

    /// <summary>Loads compact waveform data for the current transport item.</summary>
    /// <param name="filePath">Playback path for the current item.</param>
    /// <param name="duration">Known playback duration.</param>
    private async Task LoadTransportWaveformAsync(string filePath, TimeSpan duration)
    {
        CancelAndDispose(ref _waveformCts);
        PositionSlider.SetWaveform(null);
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var source = new CancellationTokenSource();
        _waveformCts = source;
        try
        {
            var samples = await LoadWaveformPeaksAsync(filePath, duration, source.Token);
            if (!source.IsCancellationRequested &&
                string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase) &&
                samples.Count > 0)
            {
                PositionSlider.SetWaveform(samples);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Transport waveform analysis");
        }
    }

    /// <summary>Loads waveform peak data for a local or remote Orynivo track.</summary>
    /// <param name="filePath">Playback path for the current item.</param>
    /// <param name="duration">Known playback duration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Normalized waveform peaks, or an empty list when unavailable.</returns>
    private async Task<IReadOnlyList<float>> LoadWaveformPeaksAsync(
        string filePath,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (_orynivoTracksByUrl.TryGetValue(filePath, out var remoteTrack) &&
            remoteTrack.OrynivoServer is { } server &&
            remoteTrack.Id is long trackId)
        {
            var waveform = await _orynivoClient.GetTrackWaveformAsync(
                server,
                trackId,
                cancellationToken);
            if (waveform?.Peaks is { Length: > 0 } peaks)
                return peaks;

            var fallback = await WaveformCache.GetOrCreateStreamAsync(
                $"orynivo-server:{server.Id}:{trackId}",
                filePath,
                duration,
                900,
                cancellationToken);
            return fallback?.Peaks ?? [];
        }

        if (CueSheetParser.IsVirtualPath(filePath))
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var track = db.GetByPath(filePath);
                if (track is { Duration: > 0 })
                {
                    var cueData = await WaveformCache.GetOrCreateAsync(
                        track.Path,
                        track.SourcePath,
                        TimeSpan.FromSeconds(track.Duration.Value),
                        900,
                        track.SegmentStart is double start ? TimeSpan.FromSeconds(start) : null,
                        track.SegmentEnd is double end ? TimeSpan.FromSeconds(end) : null,
                        cancellationToken);
                    return cueData?.Peaks ?? [];
                }
            }
            catch
            {
                return [];
            }
        }

        if (!File.Exists(filePath))
        {
            return [];
        }

        var data = await WaveformCache.GetOrCreateAsync(
            filePath,
            null,
            duration,
            900,
            cancellationToken: cancellationToken);
        return data?.Peaks ?? [];
    }

    /// <summary>Cancels pending waveform analysis and clears the transport waveform.</summary>
    private void ClearTransportWaveform()
    {
        CancelAndDispose(ref _waveformCts);
        PositionSlider.SetWaveform(null);
    }

    private void LyricsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        LyricsView.IsVisible = !(LyricsView.IsVisible);
        UpdateBackButtonForDetailView();
        if (LyricsView.IsVisible &&
            _lyricLines.Count == 0 &&
            !string.IsNullOrWhiteSpace(_currentFilePath))
        {
            _ = LoadLyricsForTrackAsync(_currentFilePath, forceRefresh: false);
        }
    }

    private void CloseLyricsButton_OnClick(object? sender, RoutedEventArgs e)
        => CloseNowPlayingDetailViews();

    private async void RefreshLyricsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;
        await LoadLyricsForTrackAsync(_currentFilePath, forceRefresh: true);
    }

    private async void SearchLyricsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var filePath = _currentFilePath;
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        // Seed the search from the current track's metadata. Remote tracks have
        // no local database row, so read the title/artist from the remote row
        // instead of GetByPath (which fails on a stream URL and previously
        // suppressed the dialog entirely for remote tracks).
        var remoteRow = _currentOrynivoTrackRow;
        string? seedTitle;
        string? seedArtist;
        TrackRecord? localTrack = null;
        if (remoteRow is not null)
        {
            seedTitle = remoteRow.Title;
            seedArtist = remoteRow.Artist;
        }
        else
        {
            using (var db = AudioDatabase.OpenDefault())
                localTrack = db.GetByPath(filePath);
            if (localTrack is null)
                return;
            seedTitle = localTrack.Title;
            seedArtist = localTrack.Artist;
        }

        var dialog = new LyricsSearchWindow(seedTitle, seedArtist);
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        // Persist the chosen lyrics on the owning store: the remote server for
        // remote tracks, the local database otherwise.
        if (remoteRow is { OrynivoServer: { } server, Id: long trackId })
        {
            await _orynivoClient.UploadTrackLyricsAsync(
                server,
                trackId,
                selected.PlainLyrics,
                selected.SyncedLyrics);
        }
        else
        {
            using var db = AudioDatabase.OpenDefault();
            db.UpdateDownloadedLyrics(
                filePath,
                selected.PlainLyrics,
                selected.SyncedLyrics,
                "LRCLIB manual");
        }

        // Only reflect the change in the view if the same track is still shown.
        if (!string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
            return;

        if (localTrack is not null)
        {
            localTrack.DownloadedLyrics = selected.PlainLyrics;
            localTrack.SyncedLyrics = selected.SyncedLyrics;
            localTrack.LyricsSource = "LRCLIB manual";
            localTrack.LyricsFetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ApplyLyrics(localTrack);
        }
        else
        {
            ApplyLyricsContent(selected.PlainLyrics, selected.SyncedLyrics);
        }
    }

    private async void ArtistInfoButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentPodcastPlayback is { } podcastPlayback)
        {
            LyricsView.IsVisible = false;
            ArtistInfoView.IsVisible = false;
            PodcastInfoView.IsVisible = !(PodcastInfoView.IsVisible);
            if (PodcastInfoView.IsVisible)
                ShowPodcastInfo(podcastPlayback);
            UpdateBackButtonForDetailView();
            return;
        }

        if (_currentOrynivoTrackRow is { ArtistId: not null } &&
            _currentNowPlayingProvider is OrynivoServerNowPlayingMetadataProvider)
        {
            LyricsView.IsVisible = false;
            PodcastInfoView.IsVisible = false;
            ArtistInfoView.IsVisible = !ArtistInfoView.IsVisible;
            UpdateBackButtonForDetailView();
            if (ArtistInfoView.IsVisible)
                await ShowNowPlayingRemoteArtistInfoAsync(forceRefresh: false);
            return;
        }

        if (_currentArtistId is not long artistId || string.IsNullOrWhiteSpace(_currentArtistName))
            return;

        LyricsView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        ArtistInfoView.IsVisible = !(ArtistInfoView.IsVisible);
        UpdateBackButtonForDetailView();
        if (ArtistInfoView.IsVisible)
            await ShowArtistInfoAsync(artistId, forceRefresh: false);
    }

    private void CloseArtistInfoButton_OnClick(object? sender, RoutedEventArgs e)
        => CloseNowPlayingDetailViews();

    private void ClosePodcastInfoButton_OnClick(object? sender, RoutedEventArgs e)
        => CloseNowPlayingDetailViews();

    private async void ArtistInfoTitleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_artistInfoDisplayedRemoteRow is { } remoteRow)
        {
            CloseNowPlayingDetailViews();
            await HandleContentRowDoubleClickAsync(remoteRow);
            return;
        }

        if (_artistInfoDisplayedId is not long artistId)
            return;

        var artistName = ArtistInfoTitleButton.Content as string;
        CloseNowPlayingDetailViews();
        await ShowArtistAlbumsAsync(
            artistId,
            artistName ?? LocalizationManager.Current.Unknown);
    }

    private async void ArtistInfoListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row })
            return;

        var artistId = row.EntityType switch
        {
            "Artist" or "OrynivoArtist" => row.Id,
            _ => row.ArtistId
        };

        if (artistId is null)
        {
            if (row.EntityType == "Album" && (row.AlbumId ?? row.Id) is long albumId)
            {
                using var db = AudioDatabase.OpenDefault();
                artistId = db.GetAlbumArtistId(albumId);
            }
            else if (!string.IsNullOrWhiteSpace(row.FilePath))
            {
                using var db = AudioDatabase.OpenDefault();
                artistId = db.GetTrackNavigationIds(row.FilePath).ArtistId;
            }
            row.ArtistId = artistId;
        }

        if (artistId is not long id)
            return;

        e.Handled = true;
        LyricsView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        ArtistInfoView.IsVisible = true;
        UpdateBackButtonForDetailView();
        if (row.EntityType.StartsWith("Orynivo", StringComparison.Ordinal))
        {
            var artistRow = row.EntityType == "OrynivoArtist"
                ? row
                : new ContentRow
                {
                    Id = id,
                    ArtistId = id,
                    Title = string.IsNullOrWhiteSpace(row.Artist) ? LocalizationManager.Current.Unknown : row.Artist,
                    EntityType = "OrynivoArtist",
                    ExternalId = id.ToString(CultureInfo.InvariantCulture),
                    OrynivoServer = row.OrynivoServer ?? _activeOrynivoServer,
                    FilePath = string.Empty
                };
            await ShowOrynivoArtistInfoAsync(artistRow, forceRefresh: false);
            return;
        }

        await ShowArtistInfoAsync(id, forceRefresh: false);
    }

    private void UpdateBackButtonForDetailView()
    {
        BackButton.IsVisible =
            LyricsView.IsVisible ||
            ArtistInfoView.IsVisible ||
            PodcastInfoView.IsVisible ||
            _plexNavigationStack.Count > 0 ||
            _navigationStack.Count > 0;
    }

    private void CloseNowPlayingDetailViews()
    {
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        BackButton.IsVisible = _plexNavigationStack.Count > 0 || _navigationStack.Count > 0;
    }

    private async void RefreshArtistInfoButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_nowPlayingRemoteArtistInfo)
        {
            await ShowNowPlayingRemoteArtistInfoAsync(forceRefresh: true);
            return;
        }

        if (_artistInfoDisplayedRemoteRow is { } remoteRow)
        {
            await ShowOrynivoArtistInfoAsync(remoteRow, forceRefresh: true);
            return;
        }

        if (_artistInfoDisplayedId is long artistId)
            await ShowArtistInfoAsync(artistId, forceRefresh: true);
    }

    private async void SearchArtistImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_nowPlayingRemoteArtistInfo)
        {
            await OpenNowPlayingRemoteArtistImageSearchAsync();
            return;
        }

        if (_artistInfoDisplayedRemoteRow is { } remoteRow)
        {
            var server = ResolveRowOrynivoServer(remoteRow);
            if (server is null ||
                remoteRow.Id is not long remoteArtistId ||
                remoteRow.EntityType != "OrynivoArtist")
            {
                return;
            }

            await OpenOrynivoArtistImageSearchAsync(server, remoteArtistId, remoteRow);
            ArtistInfoImage.Source = remoteRow.Artwork;
            ArtistInfoImagePlaceholder.IsVisible = ArtistInfoImage.Source is null;
            ArtistInfoImageStatusText.IsVisible = false;
            return;
        }

        if (_artistInfoDisplayedId is not long artistId)
            return;

        ArtistInfo? artist;
        using (var db = AudioDatabase.OpenDefault())
            artist = db.GetArtistById(artistId);
        if (artist is null)
            return;

        var dialog = new ArtistImageSearchWindow(artist.Artist) ;
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        try
        {
            var imagePath = await ArtistImageSearchService.SaveAsync(artistId, selected);
            using (var db = AudioDatabase.OpenDefault())
            {
                db.UpdateArtistImage(artistId, imagePath);
                artist = db.GetArtistById(artistId);
            }

            if (artist is null)
                return;

            ArtistInfoImage.Source = CreateArtworkImage(imagePath, 1000, ignoreCache: true);
            ArtistInfoImagePlaceholder.IsVisible = ArtistInfoImage.Source is null;
            ArtistInfoImageStatusText.IsVisible = false;
            await RefreshVisibleArtistRowAsync(artist);
        }
        catch
        {
            ArtistInfoImageStatusText.Text = LocalizationManager.Current.ArtistImageDownloadFailed;
            ArtistInfoImageStatusText.IsVisible = true;
        }
    }

    /// <summary>Opens manual artist-image search for the currently playing remote track's artist.</summary>
    /// <returns>A task representing the asynchronous search and upload flow.</returns>
    private async Task OpenNowPlayingRemoteArtistImageSearchAsync()
    {
        if (_currentOrynivoTrackRow is not { OrynivoServer: { } server, ArtistId: long artistId })
            return;

        var artistName = ArtistInfoTitleButton.Content as string;
        if (string.IsNullOrWhiteSpace(artistName))
            artistName = _currentOrynivoTrackRow.Artist ?? string.Empty;

        var dialog = new ArtistImageSearchWindow(artistName);
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        var uploaded = await _orynivoClient.UploadArtistImageAsync(
            server,
            artistId,
            selected.ImageData,
            selected.MimeType);
        if (!uploaded)
        {
            ArtistInfoImageStatusText.Text = LocalizationManager.Current.ArtistImageDownloadFailed;
            ArtistInfoImageStatusText.IsVisible = true;
            return;
        }

        var imageUrl = OrynivoServerClient.GetArtistArtworkUrl(server, artistId);
        InvalidateRemoteArtworkCache(imageUrl);
        WriteRemoteArtworkCache(imageUrl, selected.ImageData);
        using var stream = new MemoryStream(selected.ImageData);
        ArtistInfoImage.Source = new Bitmap(stream);
        ArtistInfoImagePlaceholder.IsVisible = ArtistInfoImage.Source is null;
        ArtistInfoImageStatusText.IsVisible = false;
        StatusTextBlock.Text = string.Empty;
    }

    private async void EditArtistNameButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_artistInfoDisplayedRemoteRow is { } remoteRow)
        {
            await EditOrynivoArtistNameAsync(remoteRow);
            return;
        }

        if (_artistInfoDisplayedId is not long artistId)
            return;

        ArtistInfo? artist;
        using (var db = AudioDatabase.OpenDefault())
            artist = db.GetArtistById(artistId);
        if (artist is null)
            return;

        var editDialog = new EditArtistNameDialog(artist.Id, artist.Artist);
        if (await editDialog.ShowDialog<bool>(this) == false)
            return;

        var result = editDialog.Result;
        long? matchingArtistId = null;
        if (result is null && editDialog.MatchingArtist is { } matchingArtist)
        {
            matchingArtistId = matchingArtist.Id;
            var mergeDialog = new ArtistMergeDialog(
                artist.Id,
                artist.Artist,
                matchingArtist.Id,
                matchingArtist.Artist);
            if (await mergeDialog.ShowDialog<bool>(this) == false)
                return;

            try
            {
                result = await Task.Run(() =>
                {
                    using var db = AudioDatabase.OpenDefault();
                    return db.MergeArtists(
                        artist.Id,
                        matchingArtist.Id,
                        mergeDialog.PreferredArtistId,
                        editDialog.ArtistName);
                });
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Artist merge");
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistRenameFailed;
                ArtistInfoStatusTextBlock.IsVisible = true;
                return;
            }
        }

        if (result is null)
            return;

        if (_currentArtistId == artistId || _currentArtistId == matchingArtistId)
        {
            _currentArtistId = result.ArtistId;
            _currentArtistName = result.ArtistName;
            NowPlayingArtistBlock.Text = result.ArtistName;
        }
        if (_activeArtistFilterId == artistId || _activeArtistFilterId == matchingArtistId)
        {
            _activeArtistFilterId = result.ArtistId;
            _activeArtistFilterName = result.ArtistName;
        }

        _artistInfoDisplayedId = result.ArtistId;
        ArtistInfoTitleButton.Content = result.ArtistName;
        ArtistInfoStatusTextBlock.IsVisible = false;
        await ReloadVisibleArtistListAsync(result.ArtistId);
        await ShowArtistInfoAsync(result.ArtistId, forceRefresh: false);
        _ = UpdateSearchIndexAfterArtistRenameAsync(result.ArtistId);
    }

    private async Task EditOrynivoArtistNameAsync(ContentRow row)
    {
        if (ResolveRowOrynivoServer(row) is not { } server ||
            row.Id is not long artistId ||
            row.EntityType != "OrynivoArtist")
        {
            return;
        }

        var editDialog = new EditArtistNameDialog(
            artistId,
            row.Title ?? string.Empty,
            (_, name) => CommitOrynivoArtistRenameAsync(server, artistId, name, preferredArtistId: null));
        if (await editDialog.ShowDialog<bool>(this) == false)
            return;

        var result = editDialog.Result;
        long? matchingArtistId = null;
        if (result is null && editDialog.MatchingArtist is { } matchingArtist)
        {
            matchingArtistId = matchingArtist.Id;
            var mergeDialog = new ArtistMergeDialog(
                artistId,
                row.Title ?? string.Empty,
                matchingArtist.Id,
                matchingArtist.Artist);
            if (await mergeDialog.ShowDialog<bool>(this) == false)
                return;

            try
            {
                (result, _) = await CommitOrynivoArtistRenameAsync(
                    server,
                    artistId,
                    editDialog.ArtistName,
                    mergeDialog.PreferredArtistId);
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Remote artist merge");
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistRenameFailed;
                ArtistInfoStatusTextBlock.IsVisible = true;
                return;
            }
        }

        if (result is null)
            return;

        row.ArtistId = result.ArtistId;
        ArtistInfoTitleButton.Content = result.ArtistName;
        ArtistInfoStatusTextBlock.IsVisible = false;

        if (_currentOrynivoTrackRow is { } currentRemoteTrack &&
            (currentRemoteTrack.ArtistId == artistId || currentRemoteTrack.ArtistId == matchingArtistId))
        {
            currentRemoteTrack.ArtistId = result.ArtistId;
            NowPlayingArtistBlock.Text = result.ArtistName;
        }

        ContentRow detailRow;
        var refreshed = await _orynivoClient.GetArtistAsync(server, result.ArtistId);
        if (refreshed is not null)
        {
            detailRow = ToOrynivoArtistContentRow(server, refreshed);
        }
        else
        {
            detailRow = new ContentRow
            {
                Id = result.ArtistId,
                ArtistId = result.ArtistId,
                Title = result.ArtistName,
                EntityType = "OrynivoArtist",
                ExternalId = result.ArtistId.ToString(CultureInfo.InvariantCulture),
                OrynivoServer = server,
                FilePath = string.Empty
            };
        }

        if (_activeOrynivoView == "Artists")
            await LoadOrynivoViewAsync();
        await ShowOrynivoArtistInfoAsync(detailRow, forceRefresh: false);
    }

    private async Task<(ArtistRenameResult? Result, ArtistInfo? MatchingArtist)> CommitOrynivoArtistRenameAsync(
        OrynivoServerSettings server,
        long artistId,
        string artistName,
        long? preferredArtistId)
    {
        var response = await _orynivoClient.RenameArtistAsync(
            server,
            artistId,
            artistName,
            preferredArtistId);
        if (response is null)
            throw new InvalidOperationException("The remote artist could not be renamed.");

        return (response.Result, response.MatchingArtist is null ? null : ToArtistInfo(response.MatchingArtist));
    }

    private static ArtistInfo ToArtistInfo(OrynivoArtistInfo artist) => new(
        artist.Id,
        artist.Name,
        artist.IsFavorite,
        artist.Biography,
        null,
        artist.SourceUrl,
        artist.ProfileLanguage,
        artist.ProfileFetchedAt,
        artist.ImageIsManual);

    private ContentRow ToOrynivoArtistContentRow(OrynivoServerSettings server, OrynivoArtistInfo artist)
    {
        var artworkUrl = artist.HasImage
            ? OrynivoServerClient.GetArtistArtworkUrl(server, artist.Id)
            : null;
        return new ContentRow
        {
            Id = artist.Id,
            ArtistId = artist.Id,
            Title = string.IsNullOrWhiteSpace(artist.Name) ? LocalizationManager.Current.Unknown : artist.Name,
            IsFavorite = IsOrynivoFavorite(server, "Artist", artist.Id),
            ArtworkPath = artworkUrl,
            ThumbnailPath = artworkUrl,
            Biography = artist.Biography,
            SourceUrl = artist.SourceUrl,
            ProfileLanguage = artist.ProfileLanguage,
            ProfileFetchedAt = artist.ProfileFetchedAt,
            ImageIsManual = artist.ImageIsManual,
            EntityType = "OrynivoArtist",
            ExternalId = artist.Id.ToString(CultureInfo.InvariantCulture),
            OrynivoServer = server,
            FilePath = string.Empty
        };
    }

    private async Task ReloadVisibleArtistListAsync(long? selectedArtistId = null)
    {
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: "Artists" })
            return;

        selectedArtistId ??= GetSelectedContentRowId();
        var verticalOffset = CaptureCurrentVerticalOffset();
        var rows = await Task.Run(() => QueryRows("Artists"));
        ApplyColumns("Artists");
        ContentDataGrid.ItemsSource = rows;
        BindArtworkRows("Artists", rows);
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
        RestoreSelection(rows, selectedArtistId, verticalOffset);
    }

    private void ArtistInfoSourceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_artistInfoSourceUrl))
            return;
        Process.Start(new ProcessStartInfo(_artistInfoSourceUrl) { UseShellExecute = true });
    }

    /// <summary>Clears and hides the album strip shown under the artist biography.</summary>
    private void ResetArtistInfoAlbums()
    {
        ArtistInfoAlbumsPanel.Children.Clear();
        ArtistInfoAlbumsSection.IsVisible = false;
    }

    /// <summary>
    /// Loads the displayed artist's albums through a catalog provider and renders them as a wrapped
    /// strip of clickable cards under the biography. Used for local, remote-library, and now-playing
    /// remote artist-info views so all three look and navigate identically.
    /// </summary>
    /// <param name="provider">Local or remote catalog provider that owns the artist.</param>
    /// <param name="artistId">Provider-local artist identifier.</param>
    /// <param name="server">Owning remote server, or <see langword="null"/> for the local library.</param>
    /// <param name="cancellationToken">Token cancelling a superseded load.</param>
    /// <returns>A task representing the asynchronous load.</returns>
    private async Task LoadArtistInfoAlbumsAsync(
        ILibraryCatalogProvider provider,
        long artistId,
        OrynivoServerSettings? server,
        CancellationToken cancellationToken)
    {
        try
        {
            var albums = await provider.GetAlbumsByArtistAsync(artistId, includeArtwork: true, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            PopulateArtistInfoAlbums(albums, server);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ResetArtistInfoAlbums();
        }
    }

    /// <summary>Renders the artist album cards, or hides the section when there are none.</summary>
    /// <param name="albums">Albums to render.</param>
    /// <param name="server">Owning remote server, or <see langword="null"/> for local albums.</param>
    private void PopulateArtistInfoAlbums(IReadOnlyList<LibraryCatalogAlbum> albums, OrynivoServerSettings? server)
    {
        ArtistInfoAlbumsPanel.Children.Clear();
        if (albums.Count == 0)
        {
            ArtistInfoAlbumsSection.IsVisible = false;
            return;
        }

        foreach (var album in albums)
            ArtistInfoAlbumsPanel.Children.Add(BuildArtistInfoAlbumCard(album, server));
        ArtistInfoAlbumsSection.IsVisible = true;
    }

    /// <summary>Builds one clickable album card for the artist-info album strip.</summary>
    /// <param name="album">The album to render.</param>
    /// <param name="server">Owning remote server, or <see langword="null"/> for a local album.</param>
    /// <returns>The card control.</returns>
    private Control BuildArtistInfoAlbumCard(LibraryCatalogAlbum album, OrynivoServerSettings? server)
    {
        var card = new Border
        {
            Width           = 150,
            Margin          = new Thickness(0, 0, 12, 12),
            Background      = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush     = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(10),
            Cursor          = new Cursor(StandardCursorType.Hand),
            ClipToBounds    = true
        };

        var stack = new StackPanel { Spacing = 2 };

        var avatar = new InitialsAvatar
        {
            DisplayName = album.Title,
            FontSize    = 30,
            Width       = 150,
            Height      = 150
        };
        var image = new Image
        {
            Width   = 150,
            Height  = 150,
            Stretch = Stretch.UniformToFill
        };
        var artworkHost = new Panel { Width = 150, Height = 150, ClipToBounds = true };
        artworkHost.Children.Add(avatar);
        artworkHost.Children.Add(image);
        stack.Children.Add(artworkHost);

        if (server is null)
        {
            var localPath = !string.IsNullOrEmpty(album.ThumbnailPath) && File.Exists(album.ThumbnailPath)
                ? album.ThumbnailPath
                : !string.IsNullOrEmpty(album.ArtworkPath) && File.Exists(album.ArtworkPath)
                    ? album.ArtworkPath
                    : null;
            if (localPath is not null)
            {
                try
                {
                    using var bmpStream = File.OpenRead(localPath);
                    image.Source = new Bitmap(bmpStream);
                }
                catch { image.Source = null; }
            }
        }
        else
        {
            var artUrl = album.ArtworkPath ?? album.ThumbnailPath;
            if (!string.IsNullOrEmpty(artUrl))
                _ = LoadDashboardRemoteArtworkAsync(image, artUrl);
        }

        var titleButton = new Button
        {
            Content    = album.Title,
            FontWeight = FontWeight.SemiBold,
            FontSize   = 12,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            Margin     = new Thickness(10, 8, 10, 1),
            Theme      = FindResource<ControlTheme>("EntityLinkButtonTheme")
        };
        titleButton.Click += (_, e) =>
        {
            e.Handled = true;
            _ = OpenArtistInfoAlbumAsync(album, server);
        };
        stack.Children.Add(titleButton);

        stack.Children.Add(new TextBlock
        {
            Text       = album.Year is int year && year > 0 ? year.ToString(CultureInfo.CurrentCulture) : string.Empty,
            FontSize   = 11,
            Foreground = FindResource<IBrush>("AppMutedTextBrush"),
            Margin     = new Thickness(10, 0, 10, 10)
        });

        card.Child = stack;
        card.PointerReleased += (_, e) =>
        {
            if (FindAncestor<Button>(e.Source as Visual) is not null)
                return;
            _ = OpenArtistInfoAlbumAsync(album, server);
        };
        return card;
    }

    /// <summary>Closes the artist-info overlay and opens the album's tracks (local or remote).</summary>
    /// <param name="album">The album to open.</param>
    /// <param name="server">Owning remote server, or <see langword="null"/> for a local album.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>
    private async Task OpenArtistInfoAlbumAsync(LibraryCatalogAlbum album, OrynivoServerSettings? server)
    {
        CloseNowPlayingDetailViews();
        if (server is null)
        {
            await ShowAlbumTracksAsync(album.Id, album.Title);
            return;
        }

        _activeArtistFilterId = null;
        _activeArtistFilterName = null;
        _activeOrynivoServer = server;
        await OpenOrynivoAlbumTracksAsync(album.Id, album.Title, album.DisplayArtist);
    }

    private async Task ShowArtistInfoAsync(long artistId, bool forceRefresh)
    {
        _artistInfoDisplayedRemoteRow = null;
        _nowPlayingRemoteArtistInfo = false;
        _artistInfoDisplayedId = artistId;
        ResetArtistInfoAlbums();
        CancelArtistProfileLoad();
        var cts = new CancellationTokenSource();
        _artistProfileCts = cts;
        EditArtistNameButton.IsVisible = true;
        SearchArtistImageButton.IsVisible = true;
        RefreshArtistInfoButton.IsEnabled = false;
        ArtistInfoImage.Source = null;
        ArtistInfoImagePlaceholder.IsVisible = true;
        ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoLoading;
        ArtistInfoStatusTextBlock.IsVisible = true;
        ArtistInfoBiographyTextBlock.Text = string.Empty;
        ArtistInfoSourceButton.IsVisible = false;
        ArtistInfoImageStatusText.Text = string.Empty;
        ArtistInfoImageStatusText.IsVisible = false;

        try
        {
            ArtistInfo? artist;
            using (var db = AudioDatabase.OpenDefault())
                artist = db.GetArtistById(artistId);
            if (artist is null)
            {
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoNotFound;
                return;
            }

            ArtistInfoTitleButton.Content = artist.Artist;
            var language = GetProfileLanguageCode();
            var fetchedAt = artist.ProfileFetchedAt is long timestamp
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                : (DateTimeOffset?)null;
            var needsDownload = forceRefresh ||
                string.IsNullOrWhiteSpace(artist.Biography) ||
                !string.Equals(artist.ProfileLanguage, language, StringComparison.OrdinalIgnoreCase) ||
                fetchedAt is null ||
                fetchedAt < DateTimeOffset.UtcNow.AddDays(-90);

            if (needsDownload)
            {
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloading;
                var profile = await ArtistProfileService.DownloadAsync(
                    artist.Id,
                    artist.Artist,
                    language,
                    downloadImage: !artist.ImageIsManual,
                    cancellationToken: cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                using var db = AudioDatabase.OpenDefault();
                db.UpdateArtistProfile(
                    artist.Id,
                    profile?.Biography,
                    profile?.ImagePath,
                    profile?.SourceUrl,
                    language);
                artist = db.GetArtistById(artist.Id);
            }

            if (artist is null)
                return;

            ArtistInfoBiographyTextBlock.Text = artist.Biography ?? string.Empty;
            ArtistInfoImage.Source = CreateArtworkImage(artist.ImagePath, 1000, ignoreCache: true);
            if (ArtistInfoImage.Source is null)
            {
                ArtistInfoImagePlaceholder.IsVisible = true;
                string imageMsg;
                if (string.IsNullOrWhiteSpace(artist.ImagePath))
                {
                    imageMsg = LocalizationManager.Current.ArtistInfoNoImage;
                    var diag = ArtistProfileService.LastImageDiagnostic;
                    if (!string.IsNullOrWhiteSpace(diag))
                        imageMsg += $"\n{diag}";
                }
                else if (!File.Exists(artist.ImagePath))
                    imageMsg = $"{LocalizationManager.Current.ArtistInfoImageMissing}:\n{artist.ImagePath}";
                else
                    imageMsg = $"{LocalizationManager.Current.ArtistInfoImageLoadError}:\n{artist.ImagePath}";
                ArtistInfoImageStatusText.Text = imageMsg;
                ArtistInfoImageStatusText.IsVisible = true;
            }
            else
            {
                ArtistInfoImagePlaceholder.IsVisible = false;
                ArtistInfoImageStatusText.IsVisible = false;
            }
            _artistInfoSourceUrl = artist.SourceUrl;
            ArtistInfoSourceButton.Content = _artistInfoSourceUrl?.Contains("last.fm") == true
                ? LocalizationManager.Current.ArtistInfoSourceLastFm
                : LocalizationManager.Current.ArtistInfoSource;
            ArtistInfoSourceButton.IsVisible = !(string.IsNullOrWhiteSpace(_artistInfoSourceUrl));
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoNotFound;
            ArtistInfoStatusTextBlock.IsVisible = string.IsNullOrWhiteSpace(artist.Biography) && ArtistInfoImage.Source is null;
            await RefreshVisibleArtistRowAsync(artist);
            await LoadArtistInfoAlbumsAsync(_localCatalogProvider, artistId, null, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloadFailed;
        }
        finally
        {
            if (_artistProfileCts == cts)
            {
                _artistProfileCts = null;
                RefreshArtistInfoButton.IsEnabled = true;
            }
            cts.Dispose();
        }
    }

    private async Task ShowOrynivoArtistInfoAsync(ContentRow row, bool forceRefresh)
    {
        if (ResolveRowOrynivoServer(row) is not { } server ||
            row.Id is not long artistId ||
            row.EntityType != "OrynivoArtist")
        {
            return;
        }

        _artistInfoDisplayedRemoteRow = row;
        _nowPlayingRemoteArtistInfo = false;
        _artistInfoDisplayedId = null;
        ResetArtistInfoAlbums();
        CancelArtistProfileLoad();
        var cts = new CancellationTokenSource();
        _artistProfileCts = cts;
        EditArtistNameButton.IsVisible = true;
        SearchArtistImageButton.IsVisible = true;
        RefreshArtistInfoButton.IsEnabled = false;
        ArtistInfoImage.Source = null;
        ArtistInfoImagePlaceholder.IsVisible = true;
        ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoLoading;
        ArtistInfoStatusTextBlock.IsVisible = true;
        ArtistInfoBiographyTextBlock.Text = string.Empty;
        ArtistInfoSourceButton.IsVisible = false;
        ArtistInfoImageStatusText.Text = string.Empty;
        ArtistInfoImageStatusText.IsVisible = false;

        try
        {
            ArtistInfoTitleButton.Content = row.Title ?? LocalizationManager.Current.Unknown;
            var language = GetProfileLanguageCode();
            if (!forceRefresh &&
                (string.IsNullOrWhiteSpace(row.Biography) ||
                 string.IsNullOrWhiteSpace(row.ArtworkPath) ||
                 row.ProfileFetchedAt is null))
            {
                var cached = await _orynivoClient.GetArtistAsync(server, artistId, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                if (cached is not null)
                {
                    ApplyOrynivoArtistProfile(server, row, cached);
                    ArtistInfoTitleButton.Content = row.Title ?? LocalizationManager.Current.Unknown;
                }
            }

            var fetchedAt = row.ProfileFetchedAt is long timestamp
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                : (DateTimeOffset?)null;
            var needsDownload = forceRefresh ||
                string.IsNullOrWhiteSpace(row.Biography) ||
                !string.Equals(row.ProfileLanguage, language, StringComparison.OrdinalIgnoreCase) ||
                fetchedAt is null ||
                fetchedAt < DateTimeOffset.UtcNow.AddDays(-90);

            if (needsDownload)
            {
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloading;
                var profile = await ArtistProfileService.DownloadAsync(
                    artistId,
                    row.Title ?? string.Empty,
                    language,
                    downloadImage: !row.ImageIsManual,
                    cancellationToken: cts.Token);
                cts.Token.ThrowIfCancellationRequested();

                byte[]? imageData = null;
                string? imageMimeType = null;
                if (!string.IsNullOrWhiteSpace(profile?.ImagePath) && File.Exists(profile.ImagePath))
                {
                    imageData = await File.ReadAllBytesAsync(profile.ImagePath, cts.Token);
                    imageMimeType = GuessImageMimeType(profile.ImagePath);
                }

                var refreshed = await _orynivoClient.UpdateArtistProfileAsync(
                    server,
                    artistId,
                    profile?.Biography,
                    profile?.SourceUrl,
                    profile?.Language ?? language,
                    imageData,
                    imageMimeType,
                    cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                if (refreshed is null)
                {
                    ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
                    return;
                }

                row.Biography = refreshed.Biography;
                row.SourceUrl = refreshed.SourceUrl;
                row.ProfileLanguage = refreshed.ProfileLanguage;
                row.ProfileFetchedAt = refreshed.ProfileFetchedAt;
                row.ImageIsManual = refreshed.ImageIsManual;
                if (imageData is not null)
                {
                    EnsureOrynivoArtistArtworkPaths(server, artistId, row);
                    ApplyRemoteArtwork(row, imageData);
                }
                else
                {
                    InvalidateRemoteArtworkCache(row.ArtworkPath);
                }
                DeleteOrynivoArtistListCache(server);
            }

            ArtistInfoBiographyTextBlock.Text = row.Biography ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(row.ArtworkPath))
                ArtistInfoImage.Source = await LoadRemoteArtworkImageAsync(row.ArtworkPath, 1000, cts.Token);

            if (ArtistInfoImage.Source is null)
            {
                ArtistInfoImagePlaceholder.IsVisible = true;
                ArtistInfoImageStatusText.Text = LocalizationManager.Current.ArtistInfoNoImage;
                ArtistInfoImageStatusText.IsVisible = true;
            }
            else
            {
                ArtistInfoImagePlaceholder.IsVisible = false;
                ArtistInfoImageStatusText.IsVisible = false;
            }

            _artistInfoSourceUrl = row.SourceUrl;
            ArtistInfoSourceButton.Content = _artistInfoSourceUrl?.Contains("last.fm") == true
                ? LocalizationManager.Current.ArtistInfoSourceLastFm
                : LocalizationManager.Current.ArtistInfoSource;
            ArtistInfoSourceButton.IsVisible = !string.IsNullOrWhiteSpace(_artistInfoSourceUrl);
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoNotFound;
            ArtistInfoStatusTextBlock.IsVisible =
                string.IsNullOrWhiteSpace(row.Biography) && ArtistInfoImage.Source is null;
            await LoadArtistInfoAlbumsAsync(
                CreateOrynivoCatalogProvider(server),
                artistId,
                server,
                cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloadFailed;
        }
        finally
        {
            if (_artistProfileCts == cts)
            {
                _artistProfileCts = null;
                RefreshArtistInfoButton.IsEnabled = true;
            }
            cts.Dispose();
        }
    }

    /// <summary>
    /// Shows the artist-info view for the artist of the currently playing remote Orynivo Server
    /// track, resolving the profile through the active now-playing metadata provider so the
    /// biography and image are cached on that server.
    /// </summary>
    /// <param name="forceRefresh">Whether to force a fresh download of the artist profile.</param>
    private async Task ShowNowPlayingRemoteArtistInfoAsync(bool forceRefresh)
    {
        if (_currentOrynivoTrackRow is not { ArtistId: long artistId } row ||
            _currentNowPlayingProvider is not OrynivoServerNowPlayingMetadataProvider provider)
        {
            return;
        }

        _nowPlayingRemoteArtistInfo = true;
        _artistInfoDisplayedRemoteRow = null;
        _artistInfoDisplayedId = null;
        ResetArtistInfoAlbums();
        CancelArtistProfileLoad();
        var cts = new CancellationTokenSource();
        _artistProfileCts = cts;
        EditArtistNameButton.IsVisible = false;
        SearchArtistImageButton.IsVisible = row.OrynivoServer is not null;
        RefreshArtistInfoButton.IsEnabled = false;
        ArtistInfoImage.Source = null;
        ArtistInfoImagePlaceholder.IsVisible = true;
        ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoLoading;
        ArtistInfoStatusTextBlock.IsVisible = true;
        ArtistInfoBiographyTextBlock.Text = string.Empty;
        ArtistInfoSourceButton.IsVisible = false;
        ArtistInfoImageStatusText.Text = string.Empty;
        ArtistInfoImageStatusText.IsVisible = false;

        var artistName = string.IsNullOrWhiteSpace(row.Artist)
            ? LocalizationManager.Current.Unknown
            : row.Artist;
        ArtistInfoTitleButton.Content = artistName;

        try
        {
            var language = GetProfileLanguageCode();
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloading;
            var profile = await provider.GetArtistProfileAsync(
                new NowPlayingArtistContext(artistId, artistName),
                language,
                forceRefresh,
                cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            ArtistInfoBiographyTextBlock.Text = profile?.Biography ?? string.Empty;
            ArtistInfoImage.Source = CreateArtworkImage(profile?.ImagePath, 1000, ignoreCache: true);
            if (ArtistInfoImage.Source is null)
            {
                ArtistInfoImagePlaceholder.IsVisible = true;
                ArtistInfoImageStatusText.Text = LocalizationManager.Current.ArtistInfoNoImage;
                ArtistInfoImageStatusText.IsVisible = true;
            }
            else
            {
                ArtistInfoImagePlaceholder.IsVisible = false;
                ArtistInfoImageStatusText.IsVisible = false;
            }

            _artistInfoSourceUrl = profile?.SourceUrl;
            ArtistInfoSourceButton.Content = _artistInfoSourceUrl?.Contains("last.fm") == true
                ? LocalizationManager.Current.ArtistInfoSourceLastFm
                : LocalizationManager.Current.ArtistInfoSource;
            ArtistInfoSourceButton.IsVisible = !string.IsNullOrWhiteSpace(_artistInfoSourceUrl);
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoNotFound;
            ArtistInfoStatusTextBlock.IsVisible =
                string.IsNullOrWhiteSpace(profile?.Biography) && ArtistInfoImage.Source is null;
            if (row.OrynivoServer is { } npServer)
                await LoadArtistInfoAlbumsAsync(
                    CreateOrynivoCatalogProvider(npServer),
                    artistId,
                    npServer,
                    cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoDownloadFailed;
        }
        finally
        {
            if (_artistProfileCts == cts)
            {
                _artistProfileCts = null;
                RefreshArtistInfoButton.IsEnabled = true;
            }
            cts.Dispose();
        }
    }

    private async Task EnsureArtistProfileAsync(ContentRow row)
    {
        if (row.Id is not long artistId || row.EntityType != "Artist")
            return;

        var language = GetProfileLanguageCode();
        var fetchedAt = row.ProfileFetchedAt is long timestamp
            ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
            : (DateTimeOffset?)null;
        if (fetchedAt >= DateTimeOffset.UtcNow.AddDays(-90) &&
            string.Equals(row.ProfileLanguage, language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!_artistProfilesLoading.Add(artistId))
            return;

        // Mark this row as fetched for the current session up front. If the download or
        // database write below fails (e.g. no network, or a concurrent library scan holds
        // the SQLite write lock), the freshness check above must still short-circuit so the
        // same row does not re-trigger a network request and database open on every scroll
        // pass. A freshly merged library has thousands of artists without cached profiles;
        // retrying each visible row repeatedly previously flooded the UI thread and froze
        // the artist table.
        row.ProfileLanguage = language;
        row.ProfileFetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var backgroundToken = _backgroundArtistLoadCts.Token;
        try
        {
            var profile = await ArtistProfileService.DownloadAsync(
                artistId,
                row.Title ?? string.Empty,
                language,
                downloadImage: !row.ImageIsManual,
                cancellationToken: backgroundToken);

            // Persist the (possibly negative) result and decode the artwork off the UI thread.
            // Opening the database runs connection pragmas and schema DDL, which must never
            // happen on the UI thread once per visible artist row.
            var imagePath = profile?.ImagePath;
            var (artwork, thumbnail) = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.UpdateArtistProfile(
                    artistId,
                    profile?.Biography,
                    profile?.ImagePath,
                    profile?.SourceUrl,
                    language);
                return string.IsNullOrWhiteSpace(imagePath)
                    ? ((IImage?)null, (IImage?)null)
                    : (CreateArtworkImage(imagePath, 320, ignoreCache: true),
                       CreateArtworkImage(imagePath, 96, ignoreCache: true));
            }, backgroundToken);

            if (backgroundToken.IsCancellationRequested)
                return;

            // The awaits above do not suppress the captured context, so these row updates
            // run on the UI thread as required by INotifyPropertyChanged.
            row.Biography = profile?.Biography;
            row.SourceUrl = profile?.SourceUrl;
            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                row.ArtworkPath = imagePath;
                row.ThumbnailPath = imagePath;
                row.ArtworkLoadCompleted = false;
                row.Artwork = artwork;
                row.Thumbnail = thumbnail;
                row.ArtworkLoadCompleted = true;
            }
        }
        catch
        {
            // Künstlerkarten bleiben auch ohne Netzwerk nutzbar.
        }
        finally
        {
            _artistProfilesLoading.Remove(artistId);
        }
    }

    private async Task RefreshVisibleArtistRowAsync(ArtistInfo artist)
    {
        var rows = ContentDataGrid.ItemsSource as IEnumerable<ContentRow>
            ?? ArtistArtworkListBox.ItemsSource as IEnumerable<ContentRow>;
        var row = rows?.FirstOrDefault(item => item.Id == artist.Id && item.EntityType == "Artist");
        if (row is null)
            return;

        row.Biography = artist.Biography;
        row.SourceUrl = artist.SourceUrl;
        row.ProfileLanguage = artist.ProfileLanguage;
        row.ProfileFetchedAt = artist.ProfileFetchedAt;
        row.ImageIsManual = artist.ImageIsManual;
        row.ArtworkPath = artist.ImagePath;
        row.ThumbnailPath = artist.ImagePath;
        row.ArtworkLoadCompleted = false;
        row.Artwork = await Task.Run(() => CreateArtworkImage(artist.ImagePath, 320, ignoreCache: true));
        row.Thumbnail = await Task.Run(() => CreateArtworkImage(artist.ImagePath, 96, ignoreCache: true));
        row.ArtworkLoadCompleted = true;
    }

    private string GetProfileLanguageCode() => _settings.Language switch
    {
        Orynivo.Localization.Language.German => "de",
        Orynivo.Localization.Language.French => "fr",
        _ => "en"
    };

    private void CancelArtistProfileLoad()
    {
        _artistProfileCts?.Cancel();
        _backgroundArtistLoadCts.Cancel();
        _backgroundArtistLoadCts = new CancellationTokenSource();
    }

    /// <summary>Builds the now-playing track context for the active metadata provider.</summary>
    /// <param name="filePath">Local path or remote stream URL of the current track.</param>
    /// <returns>Context populated from the remote track row when remote, otherwise just the path.</returns>
    private NowPlayingTrackContext BuildNowPlayingTrackContext(string filePath)
    {
        if (_currentOrynivoTrackRow is { } row &&
            string.Equals(row.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return new NowPlayingTrackContext(
                row.Id,
                filePath,
                row.Title,
                row.Artist,
                row.Album,
                row.KnownDuration?.TotalSeconds);
        }

        return new NowPlayingTrackContext(null, filePath, null, null, null, null);
    }

    /// <summary>
    /// Loads the transport cover and lyrics background for the current track through the
    /// active now-playing metadata provider, so local and remote tracks behave the same.
    /// </summary>
    /// <param name="filePath">Local path or remote stream URL of the current track.</param>
    /// <param name="provider">Provider for the current track's source.</param>
    /// <param name="context">Now-playing track context.</param>
    private async Task LoadNowPlayingArtworkAsync(
        string filePath,
        INowPlayingMetadataProvider provider,
        NowPlayingTrackContext context)
    {
        var token = _playbackCts?.Token ?? CancellationToken.None;
        try
        {
            var artwork = await provider.GetArtworkAsync(context, token);
            if (token.IsCancellationRequested ||
                !string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var thumbnailPath = artwork?.ThumbnailPath ?? artwork?.LargePath;
            var largePath = artwork?.LargePath ?? artwork?.ThumbnailPath;

            // Clear the cover only when the provider authoritatively returned no artwork
            // (local). For remote, keep any list thumbnail already shown if the fetch failed.
            if (thumbnailPath is not null || provider is LocalNowPlayingMetadataProvider)
                NowPlayingArtworkImage.Source = CreateArtworkImage(thumbnailPath, 96);
            if (largePath is not null || provider is LocalNowPlayingMetadataProvider)
                LyricsBackgroundImage.Source = CreateArtworkImage(largePath, 900);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private async Task LoadLyricsForTrackAsync(string filePath, bool forceRefresh)
    {
        CancelLyricsLoad();
        var cts = new CancellationTokenSource();
        _lyricsCts = cts;
        _activeLyricIndex = -1;
        RefreshLyricsButton.IsEnabled = false;
        ShowLyricsStatus(LocalizationManager.Current.LyricsLoading);

        try
        {
            var provider = _currentNowPlayingProvider ?? _localNowPlayingProvider;
            var context = BuildNowPlayingTrackContext(filePath);

            var cached = await provider.GetCachedLyricsAsync(context, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (cached is null && provider is LocalNowPlayingMetadataProvider)
            {
                // A local track with no database row cannot be looked up at all.
                ClearLyrics();
                ShowLyricsStatus(LocalizationManager.Current.LyricsUnavailable);
                return;
            }

            var hasLocalLyrics = cached is { HasLyrics: true }
                && ApplyLyricsContent(cached.Plain, cached.Synced);
            if (cached is null || !cached.HasLyrics)
                ClearLyrics();

            var fetchedAt = cached?.FetchedAt is long timestamp
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                : (DateTimeOffset?)null;
            var lookupExpired = fetchedAt is null ||
                fetchedAt < DateTimeOffset.UtcNow.AddDays(-30);
            var shouldDownload = forceRefresh ||
                (string.IsNullOrWhiteSpace(cached?.Synced) && lookupExpired);
            if (!shouldDownload)
            {
                if (!hasLocalLyrics)
                    ShowLyricsStatus(LocalizationManager.Current.LyricsNotFound);
                return;
            }

            if (!hasLocalLyrics)
                ShowLyricsStatus(LocalizationManager.Current.LyricsDownloading);

            var result = await provider.DownloadLyricsAsync(context, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                return;

            if (result is not { HasLyrics: true } || !ApplyLyricsContent(result.Plain, result.Synced))
                ShowLyricsStatus(LocalizationManager.Current.LyricsNotFound);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (_lyricLines.Count == 0)
                ShowLyricsStatus(LocalizationManager.Current.LyricsDownloadFailed);
        }
        finally
        {
            if (_lyricsCts == cts)
            {
                RefreshLyricsButton.IsEnabled = true;
                _lyricsCts = null;
            }
            cts.Dispose();
        }
    }

    private bool ApplyLyrics(TrackRecord track)
        => ApplyLyricsContent(track.DownloadedLyrics ?? track.Lyrics, track.SyncedLyrics);

    /// <summary>Renders the lyrics view from plain and synchronised lyrics text.</summary>
    /// <param name="plainLyrics">Unsynchronised plain-text lyrics, or <see langword="null"/>.</param>
    /// <param name="syncedLyrics">LRC-formatted synchronised lyrics, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when at least one lyric line was rendered.</returns>
    private bool ApplyLyricsContent(string? plainLyrics, string? syncedLyrics)
    {
        ClearLyrics();
        var timedLines = LyricsService.ParseLrc(syncedLyrics);
        if (timedLines.Count > 0)
        {
            foreach (var line in timedLines)
                _lyricLines.Add(new LyricLineViewModel(line.Text, line.Time));
        }
        else if (!string.IsNullOrWhiteSpace(plainLyrics))
        {
            foreach (var line in plainLyrics.Replace("\r\n", "\n").Split('\n'))
                _lyricLines.Add(new LyricLineViewModel(line.Trim(), null));
        }

        var hasLyrics = _lyricLines.Count > 0;
        LyricsStatusTextBlock.IsVisible = !(hasLyrics);
        if (hasLyrics && _player is not null)
            UpdateActiveLyric(_player.Position);
        return hasLyrics;
    }

    private void UpdateActiveLyric(TimeSpan position)
    {
        if (_lyricLines.Count == 0 || _lyricLines[0].Time is null)
            return;

        var nextIndex = -1;
        var low = 0;
        var high = _lyricLines.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (_lyricLines[middle].Time <= position)
            {
                nextIndex = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (nextIndex == _activeLyricIndex)
            return;
        if (_activeLyricIndex >= 0 && _activeLyricIndex < _lyricLines.Count)
            _lyricLines[_activeLyricIndex].IsActive = false;
        _activeLyricIndex = nextIndex;
        if (_activeLyricIndex >= 0)
        {
            var activeLine = _lyricLines[_activeLyricIndex];
            activeLine.IsActive = true;
            LyricsListBox.SelectedItem = activeLine;
            LyricsListBox.ScrollIntoView(activeLine);
        }
        else
        {
            LyricsListBox.SelectedItem = null;
        }
    }

    private void ShowLyricsStatus(string text)
    {
        LyricsStatusTextBlock.Text = text;
        LyricsStatusTextBlock.IsVisible = true;
    }

    private void ClearLyrics()
    {
        LyricsListBox.SelectedItem = null;
        _lyricLines.Clear();
        _activeLyricIndex = -1;
    }

    private void CancelLyricsLoad()
    {
        _lyricsCts?.Cancel();
    }

    private void VolumeSlider_OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (VolumeValueTextBlock is null) return;
        VolumeValueTextBlock.Text = $"{Math.Round(VolumeSlider.Value * 100):N0} %";
        if (_settings.OutputBackend == OutputBackend.Wasapi &&
            _endpointVolumeSynchronizer is not null)
        {
            if (!_updatingVolumeFromSystem)
                _endpointVolumeSynchronizer.SetVolume((float)VolumeSlider.Value);
            if (_player is not null)
                _player.Volume = 1.0f;
        }
        else if (_player is not null)
        {
            _player.Volume = (float)VolumeSlider.Value;
        }
        _settings.Volume = VolumeSlider.Value;
    }

    private async Task ConfigureEndpointVolumeSynchronizationAsync()
    {
        var synchronizationVersion =
            Interlocked.Increment(ref _endpointVolumeSynchronizationVersion);
        var previous = DetachEndpointVolumeSynchronization();
        if (previous is not null)
            _ = Task.Run(() => DisposeEndpointSynchronizer(previous));
        if (_settings.OutputBackend != OutputBackend.Wasapi ||
            string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId))
        {
            return;
        }

        try
        {
            var deviceId = _settings.SelectedWasapiDeviceId;
            var synchronizer = await Task.Run(() => new WindowsEndpointVolumeSynchronizer(deviceId));
            if (synchronizationVersion !=
                    Volatile.Read(ref _endpointVolumeSynchronizationVersion) ||
                _settings.OutputBackend != OutputBackend.Wasapi ||
                !string.Equals(
                    _settings.SelectedWasapiDeviceId,
                    deviceId,
                    StringComparison.Ordinal))
            {
                _ = Task.Run(() => DisposeEndpointSynchronizer(synchronizer));
                return;
            }
            synchronizer.VolumeChanged += EndpointVolumeSynchronizer_OnVolumeChanged;
            _endpointVolumeSynchronizer = synchronizer;
            var volume = await Task.Run(() => synchronizer.Volume);
            ApplySystemVolume(volume);
        }
        catch
        {
            DisposeEndpointVolumeSynchronizationInBackground();
        }
    }

    private static async Task UpdateSearchIndexAfterArtistRenameAsync(long artistId)
    {
        try
        {
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                TrackSearchIndex.UpdateMany(db.GetTracksForArtistSearchIndex(artistId));
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Artist rename search-index update");
        }
    }

    private WindowsEndpointVolumeSynchronizer? DetachEndpointVolumeSynchronization()
    {
        var synchronizer = _endpointVolumeSynchronizer;
        _endpointVolumeSynchronizer = null;
        if (synchronizer is not null)
            synchronizer.VolumeChanged -= EndpointVolumeSynchronizer_OnVolumeChanged;
        return synchronizer;
    }

    private void DisposeEndpointVolumeSynchronizationInBackground()
    {
        Interlocked.Increment(ref _endpointVolumeSynchronizationVersion);
        var synchronizer = DetachEndpointVolumeSynchronization();
        if (synchronizer is not null)
            _ = Task.Run(() => DisposeEndpointSynchronizer(synchronizer));
    }

    private static void DisposeEndpointSynchronizer(
        WindowsEndpointVolumeSynchronizer synchronizer)
    {
        try
        {
            synchronizer.Dispose();
        }
        catch
        {
        }
    }

    private void EndpointVolumeSynchronizer_OnVolumeChanged(object? sender, float volume) =>
        Dispatcher.UIThread.Post(() => ApplySystemVolume(volume));

    private void ApplySystemVolume(float volume)
    {
        _updatingVolumeFromSystem = true;
        try
        {
            VolumeSlider.Value = Math.Clamp(volume, 0.0f, 1.0f);
            VolumeValueTextBlock.Text = $"{Math.Round(VolumeSlider.Value * 100):N0} %";
            _settings.Volume = VolumeSlider.Value;
        }
        finally
        {
            _updatingVolumeFromSystem = false;
        }
    }

    private float GetReplayGainFactor(string filePath)
    {
        var pcmGain = GetPcmOutputGainFactor();
        if (_settings.ReplayGainMode == ReplayGainMode.Off)
            return pcmGain;

        if (TryGetCurrentReplayGainValues(filePath, out var trackGain, out var albumGain))
        {
            return pcmGain * ReplayGain.GetLinearFactor(
                _settings.ReplayGainMode,
                trackGain,
                albumGain);
        }

        return pcmGain;
    }

    private void UpdateReplayGainBadge(bool nativeDsdOutput)
    {
        ReplayGainBadgeBorder.IsVisible =
            !nativeDsdOutput &&
            _settings.ReplayGainMode != ReplayGainMode.Off &&
            TryGetCurrentReplayGainValues(_currentFilePath, out var trackGain, out var albumGain) &&
            HasPreferredReplayGainValue(trackGain, albumGain);
    }

    private bool HasPreferredReplayGainValue(string? trackGain, string? albumGain) =>
        _settings.ReplayGainMode switch
        {
            ReplayGainMode.Track => !string.IsNullOrWhiteSpace(trackGain) ||
                                    !string.IsNullOrWhiteSpace(albumGain),
            ReplayGainMode.Album => !string.IsNullOrWhiteSpace(albumGain) ||
                                    !string.IsNullOrWhiteSpace(trackGain),
            _ => false
        };

    private bool TryGetCurrentReplayGainValues(
        string? filePath,
        out string? trackGain,
        out string? albumGain)
    {
        trackGain = null;
        albumGain = null;
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        if (_orynivoTracksByUrl.TryGetValue(filePath, out var orynivoRow))
        {
            trackGain = orynivoRow.ReplayGainTrack;
            albumGain = orynivoRow.ReplayGainAlbum;
            return !string.IsNullOrWhiteSpace(trackGain) ||
                   !string.IsNullOrWhiteSpace(albumGain);
        }

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(filePath);
            if (track is null)
                return false;
            trackGain = track.ReplayGainTrack;
            albumGain = track.ReplayGainAlbum;
            return !string.IsNullOrWhiteSpace(trackGain) ||
                   !string.IsNullOrWhiteSpace(albumGain);
        }
        catch
        {
            return false;
        }
    }

    private float GetPcmOutputGainFactor() =>
        _settings.PcmOutputBoostEnabled ? PcmOutputBoostFactor : 1.0f;

    private static string GetContentColumnWidthKey(string view) =>
        view.StartsWith("Playlist:", StringComparison.Ordinal)
            ? "Content.Playlist"
            : $"Content.{view}";

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    // ------------------------------------------------------------------
    // Einstellungen
    // ------------------------------------------------------------------

    private void SettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SettingsViewHost.IsVisible)
            return;

        var view = new SettingsView(_settings, paths =>
        {
            _settings.LibraryPaths = paths;
            _settingsStore.Save(_settings);
            _libraryWatcher?.UpdatePaths(paths);
            var cleanupPaths = paths.ToList();
            _ = Task.Run(() =>
            {
                try { LibraryScanner.RemoveTracksOutsideRoots(cleanupPaths); }
                catch (Exception ex) { CrashLogger.Log(ex, "Library root cleanup"); }
            });
            // Show/hide the Local section and the empty-library hint immediately
            // when directories are added or removed while Settings is open.
            ApplySidebarNavigationSettings();
        }, (enabled, profile) =>
        {
            if (_player is IEqualizerAudioPlayer equalizerPlayer)
                equalizerPlayer.UpdateEqualizer(enabled, profile);
        });
        var completionHandled = false;
        view.CompletionRequested += async (_, accepted) =>
        {
            if (completionHandled)
                return;
            completionHandled = true;
            SettingsViewHost.IsEnabled = false;
            try
            {
                if (accepted)
                    await ApplySettingsAsync(view);
            }
            finally
            {
                CloseEmbeddedSettings();
                SettingsViewHost.IsEnabled = true;
            }
        };
        SettingsViewHost.Content = view;
        SettingsViewHost.IsVisible = true;
    }

    private void CloseEmbeddedSettings()
    {
        if (SettingsViewHost.Content is SettingsView settingsView)
            settingsView.Deactivate();
        SettingsViewHost.Content = null;
        SettingsViewHost.IsVisible = false;
    }

    private void OpenSettingsAt(string sectionTag, bool scrollToEqualizer = false)
    {
        if (!SettingsViewHost.IsVisible)
            SettingsButton_OnClick(null!, null!);
        if (SettingsViewHost.Content is SettingsView sv)
        {
            sv.NavigateToSection(sectionTag);
            if (scrollToEqualizer)
                sv.ScrollToEqualizerSection();
        }
    }

    // ------------------------------------------------------------------
    // EQ + Output quick-pick popups
    // ------------------------------------------------------------------

    private void EqPickerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OutputPickerPopup.IsOpen = false;
        _eqPickerUpdating = true;
        try
        {
            EqPickerComboBox.ItemsSource = _settings.EqualizerProfiles;
            EqPickerComboBox.SelectedItem = _settings.EqualizerProfiles
                .FirstOrDefault(p => string.Equals(
                    p.Name, _settings.SelectedEqualizerProfileName,
                    StringComparison.OrdinalIgnoreCase));
            EqPickerEnabledCheckBox.IsChecked = _settings.EqualizerEnabled;
        }
        finally
        {
            _eqPickerUpdating = false;
        }
        EqPickerPopup.IsOpen = !EqPickerPopup.IsOpen;
    }

    private void EqPickerComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_eqPickerUpdating) return;
        if (EqPickerComboBox.SelectedItem is not EqualizerProfile profile) return;
        _settings.SelectedEqualizerProfileName = profile.Name;
        _settings.EqualizerProfile = profile.Clone();
        if (_player is IEqualizerAudioPlayer eqPlayer)
            eqPlayer.UpdateEqualizer(_settings.EqualizerEnabled, _settings.EqualizerProfile);
        _ = Task.Run(() => _settingsStore.Save(_settings));
    }

    private void EqPickerEnabledCheckBox_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_eqPickerUpdating) return;
        _settings.EqualizerEnabled = EqPickerEnabledCheckBox.IsChecked == true;
        if (_player is IEqualizerAudioPlayer eqPlayer)
            eqPlayer.UpdateEqualizer(_settings.EqualizerEnabled, _settings.EqualizerProfile);
        _ = Task.Run(() => _settingsStore.Save(_settings));
    }

    private void EqPickerSettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EqPickerPopup.IsOpen = false;
        OpenSettingsAt("AudioDevice", scrollToEqualizer: true);
    }

    private void OutputPickerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EqPickerPopup.IsOpen = false;
        _outputPickerUpdating = true;
        try
        {
            OutputPickerComboBox.ItemsSource = _settings.OutputProfiles;
            OutputPickerComboBox.SelectedItem = _settings.OutputProfiles
                .FirstOrDefault(p => string.Equals(
                    p.Name, _settings.SelectedOutputProfileName,
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _outputPickerUpdating = false;
        }
        OutputPickerPopup.IsOpen = !OutputPickerPopup.IsOpen;
    }

    private async void OutputPickerComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_outputPickerUpdating) return;
        if (OutputPickerComboBox.SelectedItem is not OutputProfile profile) return;
        if (string.Equals(profile.Name, _settings.SelectedOutputProfileName, StringComparison.Ordinal)) return;

        var outputChanged =
            _settings.OutputBackend != profile.Backend ||
            !string.Equals(_settings.SelectedDriverName, profile.SelectedDriverName, StringComparison.Ordinal) ||
            !string.Equals(_settings.SelectedWasapiDeviceId, profile.SelectedWasapiDeviceId, StringComparison.Ordinal);

        // Snapshot playback state before StopPlaybackCore clears station/podcast fields.
        var resumePath     = _currentFilePath;
        var resumeStation  = _currentRadioStation;
        var resumePodcast  = _currentPodcastPlayback;
        var resumePosition = outputChanged && _player is not null ? _player.Position : TimeSpan.Zero;
        var wasPaused      = outputChanged && _player?.IsPaused == true;
        var shouldResume   = outputChanged && _player is not null && !string.IsNullOrEmpty(resumePath);

        if (outputChanged && _player is not null)
            await StopPlaybackAsync();

        _settings.SelectedOutputProfileName  = profile.Name;
        _settings.OutputBackend              = profile.Backend;
        _settings.SelectedDriverName         = profile.SelectedDriverName;
        _settings.SelectedWasapiDeviceId     = profile.SelectedWasapiDeviceId;
        _settings.SelectedWasapiDeviceName   = profile.SelectedWasapiDeviceName;
        _ = Task.Run(() => _settingsStore.Save(_settings));

        if (!shouldResume)
            return;
        try
        {
            await StartPlaybackAsync(resumePath, resumeStation, resumePodcast, resumePosition);
            if (wasPaused)
                PausePlayback();
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped;
        }
        catch (Exception ex)
        {
            StopPlayback();
            StatusTextBlock.Text = ex.Message;
        }
    }

    private void OutputPickerSettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OutputPickerPopup.IsOpen = false;
        OpenSettingsAt("AudioDevice");
    }

    private async Task ApplySettingsAsync(SettingsView window)
    {
            var themeChanged = _settings.Theme != window.SelectedTheme;
            var languageChanged = _settings.Language != window.SelectedLanguage;
            var replayGainChanged =
                _settings.ReplayGainMode != window.SelectedReplayGainMode ||
                _settings.PcmOutputBoostEnabled != window.PcmOutputBoostEnabled;
            var artistInfoChanged =
                _settings.ArtistInfoSource != window.SelectedArtistInfoSource ||
                !string.Equals(
                    _settings.LastFmApiKey,
                    window.SelectedLastFmApiKey,
                    StringComparison.Ordinal);
            var sidebarChanged =
                _settings.ShowInternetRadioItem != window.ShowInternetRadioItem ||
                _settings.ShowPodcastsItem != window.ShowPodcastsItem ||
                _settings.ShowQueueItem != window.ShowQueueItem ||
                _settings.ShowLocalLibrarySection != window.ShowLocalLibrarySection ||
                _settings.ShowOwnRadiosSection != window.ShowOwnRadiosSection ||
                _settings.ShowMyPodcastsSection != window.ShowMyPodcastsSection ||
                _settings.ShowPlexSection != window.ShowPlexSection;
            var mcpChanged =
                _settings.McpServerEnabled != window.McpServerEnabled ||
                _settings.McpServerPort    != window.McpServerPort;
            var plexServersChanged = !PlexServerSettingsEqual(
                _settings.PlexServers,
                window.SelectedPlexServers);
            var orynivoServersChanged = !OrynivoServerSettingsEqual(
                _settings.OrynivoServers,
                window.SelectedOrynivoServers);
            var libraryPathsChanged = !(_settings.LibraryPaths ?? []).SequenceEqual(window.SelectedLibraryPaths);
            var hadOrynivoServers = _settings.OrynivoServers.Count > 0;
            var outputChanged =
                _settings.OutputBackend != window.SelectedOutputBackend ||
                !string.Equals(
                    _settings.SelectedDriverName,
                    window.SelectedDriverName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    _settings.SelectedWasapiDeviceId,
                    window.SelectedWasapiDeviceId,
                    StringComparison.Ordinal);
            if (outputChanged && _player is not null)
                await StopPlaybackAsync();

            _settings.OutputProfiles             = window.SelectedOutputProfiles.ToList();
            _settings.SelectedOutputProfileName  = window.SelectedOutputProfileName;
            _settings.OutputBackend              = window.SelectedOutputBackend;
            _settings.SelectedDriverName         = window.SelectedDriverName;
            _settings.SelectedWasapiDeviceId     = window.SelectedWasapiDeviceId;
            _settings.SelectedWasapiDeviceName   = window.SelectedWasapiDeviceName;
            _settings.ReplayGainMode        = window.SelectedReplayGainMode;
            _settings.AlwaysConvertDsdToPcm = window.AlwaysConvertDsdToPcm;
            _settings.PcmOutputBoostEnabled = window.PcmOutputBoostEnabled;
            _settings.NonGaplessCrossfadeSeconds = window.NonGaplessCrossfadeSeconds;
            _settings.EqualizerEnabled      = window.EqualizerEnabled;
            _settings.EqualizerProfile      = window.SelectedEqualizerProfile;
            _settings.EqualizerProfiles     = window.SelectedEqualizerProfiles.ToList();
            _settings.SelectedEqualizerProfileName = window.SelectedEqualizerProfileName;
            _settings.LibraryPaths           = window.SelectedLibraryPaths.ToList();
            _libraryWatcher?.UpdatePaths(_settings.LibraryPaths);
            if (libraryPathsChanged)
            {
                var cleanupPaths = _settings.LibraryPaths.ToList();
                _ = Task.Run(() =>
                {
                    try
                    {
                        LibraryScanner.RemoveTracksOutsideRoots(cleanupPaths);
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.Log(ex, "Library root cleanup");
                    }
                });
            }
            _settings.Theme                  = window.SelectedTheme;
            _settings.Language               = window.SelectedLanguage;
            _settings.ArtistInfoSource       = window.SelectedArtistInfoSource;
            _settings.LastFmApiKey           = window.SelectedLastFmApiKey;
            _settings.QobuzApplicationId      = window.SelectedQobuzApplicationId;
            _settings.PlexServers             = window.SelectedPlexServers.ToList();
            _settings.OrynivoServers          = window.SelectedOrynivoServers.ToList();
            if (!hadOrynivoServers && _settings.OrynivoServers.Count > 0)
                _settings.IsLocalLibrarySectionExpanded = true;
            _settings.McpServerEnabled        = window.McpServerEnabled;
            _settings.McpServerPort           = window.McpServerPort;
            _settings.DisabledMcpTools        = window.DisabledMcpTools;
            _mcpBridge.DisabledTools          = _settings.DisabledMcpTools;
            _settings.AiChat                  = window.AiChatSettingsValue;
            _aiChatView.GetSettings           = () => _settings.AiChat;
            _settings.WebBrowsing             = window.WebBrowsingValue;
            if (_webBrowsing is not null)
                _webBrowsing.Options          = _settings.WebBrowsing;
            _settings.ShowInternetRadioItem   = window.ShowInternetRadioItem;
            _settings.ShowPodcastsItem        = window.ShowPodcastsItem;
            _settings.ShowQueueItem           = window.ShowQueueItem;
            _settings.CheckForUpdatesOnStartup = window.CheckForUpdatesOnStartup;
            _settings.ShowLocalLibrarySection = window.ShowLocalLibrarySection;
            _settings.ShowOwnRadiosSection    = window.ShowOwnRadiosSection;
            _settings.ShowMyPodcastsSection   = window.ShowMyPodcastsSection;
            _settings.ShowPlexSection              = window.ShowPlexSection;
            if (window.PlexCredentialsChanged)
            {
                try
                {
                    var plexTokens = window.SelectedPlexTokens.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value);
                    await Task.Run(() => new WindowsPlexCredentialStore().SaveAll(plexTokens));
                }
                catch
                {
                }
            }
            await Task.Run(() => _settingsStore.Save(_settings));
            if (mcpChanged)
            {
                if (_settings.McpServerEnabled)
                    await _mcpServer.StartAsync(_settings.McpServerPort, _mcpBridge);
                else
                    await _mcpServer.StopAsync();
            }
            if (themeChanged)
            {
                ThemeManager.Apply(_settings.Theme);
                UpdateNowPlayingRowHighlights();
            }
            if (languageChanged)
                LocalizationManager.Apply(_settings.Language);
            if (artistInfoChanged)
                ApplyArtistInfoSettings();
            if (outputChanged)
                RefreshSelectedDriverText();
            if (plexServersChanged || window.PlexCredentialsChanged)
                LoadNavPlaylists();
            else if (sidebarChanged || libraryPathsChanged)
                ApplySidebarNavigationSettings();
            if (orynivoServersChanged)
                LoadOrynivoServerNavigation();
            if (replayGainChanged && _player is not null)
            {
                var currentFilePath = _currentFilePath;
                var replayGainFactor = await Task.Run(() =>
                    GetReplayGainFactorFromDatabase(currentFilePath));
                if (_player is not null &&
                    string.Equals(
                        _currentFilePath,
                        currentFilePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _player.ReplayGainFactor = replayGainFactor;
                    UpdateReplayGainBadge(_player is DsfAudioPlayer or DffAudioPlayer or RemoteDsfAudioPlayer or RemoteDffAudioPlayer);
                }
            }
            if (_player is IEqualizerAudioPlayer equalizerPlayer)
                equalizerPlayer.UpdateEqualizer(
                    _settings.EqualizerEnabled,
                    _settings.EqualizerProfile);
            if (outputChanged)
                _ = ConfigureEndpointVolumeSynchronizationAsync();

            StatusTextBlock.Text = _settings.OutputBackend switch
            {
                OutputBackend.Asio or OutputBackend.CwAsio when string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
                    LocalizationManager.Current.SelectAsioDevice,
                OutputBackend.Wasapi when string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId) =>
                    LocalizationManager.Current.SelectWasapiDevice,
                OutputBackend.KernelStreaming =>
                    string.Format(LocalizationManager.Current.NotImplemented, "KernelStreaming"),
                _ => LocalizationManager.Current.SettingsSaved
            };
    }

    private float GetReplayGainFactorFromDatabase(string filePath)
        => GetReplayGainFactor(filePath);

    private static bool PlexServerSettingsEqual(
        IReadOnlyList<PlexServerSettings>? left,
        IReadOnlyList<PlexServerSettings>? right)
    {
        left ??= [];
        right ??= [];
        return left.Count == right.Count &&
               left.Zip(right).All(pair =>
                   string.Equals(pair.First.Id, pair.Second.Id, StringComparison.Ordinal) &&
                   string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
                   string.Equals(pair.First.BaseUrl, pair.Second.BaseUrl, StringComparison.Ordinal));
    }

    private static bool OrynivoServerSettingsEqual(
        IReadOnlyList<OrynivoServerSettings>? left,
        IReadOnlyList<OrynivoServerSettings>? right)
    {
        left ??= [];
        right ??= [];
        return left.Count == right.Count &&
               left.Zip(right).All(pair =>
                   string.Equals(pair.First.Id, pair.Second.Id, StringComparison.Ordinal) &&
                   string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
                   string.Equals(pair.First.BaseUrl, pair.Second.BaseUrl, StringComparison.Ordinal) &&
                   string.Equals(pair.First.ApiKey, pair.Second.ApiKey, StringComparison.Ordinal));
    }

    internal void PrepareForLibraryImport()
    {
        _searchTimer.Stop();
        _libraryWatcher?.Dispose();
        _libraryWatcher = null;
        StopPlayback();
    }

    private async void AboutButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CloseEmbeddedSettings();
        await new AboutWindow().ShowDialog<object?>(this);
    }

}


