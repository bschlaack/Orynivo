using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Orynivo.Audio;
using Orynivo.Controls;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;
using Windows.Media;

namespace Orynivo;

public partial class MainWindow : Window
{
    private int _plexNavigationLoadVersion;
    private int _plexViewLoadVersion;
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
    private readonly Dictionary<string, TreeViewItem> _localFolderTrackItems =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _localFolderTrackHeaders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<PlexNavigationState> _plexNavigationStack = [];
    private const int ArtworkPageSize = 120;
    private List<ContentRow> _albumArtworkRows = [];
    private List<ContentRow> _artistArtworkRows = [];
    private readonly ObservableCollection<ContentRow> _visibleAlbumArtworkRows = [];
    private readonly ObservableCollection<ContentRow> _visibleArtistArtworkRows = [];
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
    private readonly DispatcherTimer _transportTimer;
    private bool _isSeekingWithSlider;
    private bool _showAlbumArtworkView;
    private bool _showArtistArtworkView;
    private long? _currentPlayHistoryId;
    private long? _currentTrackId;
    private long? _currentArtistId;
    private long? _artistInfoDisplayedId;
    private string? _currentArtistName;
    private string? _artistInfoSourceUrl;
    private bool  _currentTrackIsFavorite;
    private long? _activePlaylistId;
    private long? _activeAlbumFilterId;
    private string? _activeAlbumFilterTitle;
    private long? _activeArtistFilterId;
    private string? _activeArtistFilterName;
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
    private readonly HashSet<string> _expandedTrackFilterSections = new(StringComparer.Ordinal);
    private readonly HashSet<long> _artistProfilesLoading = [];
    private bool _isDraggingAlphabetIndex;
    private bool _alphabetScrollUpdatePending;
    private bool _isAlphabetProgrammaticScroll;
    private ScrollBar? _contentDataGridVerticalScrollBar;
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
    private readonly List<int> _shuffleHistory = [];
    private int _shuffleHistoryPosition = -1;
    private string _currentFilePath = string.Empty;
    private RadioStationRecord? _currentRadioStation;
    private PodcastPlayback? _currentPodcastPlayback;
    private PodcastRecord? _activePodcast;
    private DateTimeOffset _lastPodcastProgressSave = DateTimeOffset.MinValue;
    private TimeSpan _currentPlaybackDuration;
    private CancellationTokenSource? _lyricsCts;
    private CancellationTokenSource? _artistProfileCts;
    private WindowsEndpointVolumeSynchronizer? _endpointVolumeSynchronizer;
    private int _endpointVolumeSynchronizationVersion;
    private WindowsMediaTransportService? _windowsMediaTransport;
    private readonly Mcp.McpPlayerBridge _mcpBridge = new();
    private readonly Mcp.McpServerService _mcpServer = new();
    private bool _updatingVolumeFromSystem;
    private CancellationTokenSource _backgroundArtistLoadCts = new();
    private int _activeLyricIndex = -1;
    private bool _updatingViewMode;
    private string? _contentColumnWidthKey;

    private int _dashboardYear;
    private int _dashboardMonth;
    private StackPanel? _calendarInner;
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

    private sealed record FolderTag(bool IsFile, string FilePath, string FolderPath);
    private sealed record PlexFolderTag(string Key, bool IsTrack, ContentRow? Track);
    private sealed record AlphabetTreeTarget(string Key, TreeViewItem Item);
    private sealed record PlexNavigationState(
        string Title,
        string View,
        IReadOnlyList<ContentRow> Rows);
    private sealed record PlaylistMenuTag(long PlaylistId, IReadOnlyList<string> Paths);
    private sealed record RemovePlaylistEntryTag(long PlaylistEntryId);
    private sealed record PodcastPlayback(PodcastRecord Podcast, PodcastEpisode Episode);
    private sealed record NavigationState(
        string View,
        long? SelectedId,
        long? ArtistFilterId,
        string? ArtistFilterName,
        string? SearchQuery = null,
        double? VerticalOffset = null);

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
        public string? Album       { get; init; }
        public string? AlbumArtist { get; init; }
        public string? Year        { get; init; }
        public string? TrackNumber { get; init; }
        public string? DiscNumber  { get; init; }
        public string? Genre       { get; init; }
        public string? Bitrate     { get; init; }
        public string? SampleRate  { get; init; }
        public string? BitDepth    { get; init; }
        public string? Channels    { get; init; }
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
        private IImage? _artwork;
        private IImage? _thumbnail;
        public bool ArtworkLoadQueued { get; set; }
        public bool ArtworkLoadCompleted { get; set; }
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
        public string FavoriteGlyph => IsFavorite ? "♥" : "♡";
        public string  Duration    { get; init; } = "";
        public string? Format      { get; init; }
        public string  FilePath    { get; init; } = "";
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
        InitializeComponent();
        LyricsListBox.ItemsSource = _lyricLines;
        Opened += OnWindowOpened;
        _transportTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _transportTimer.Tick += (_, _) => RefreshTransport();
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await ShowSearchResultsAsync(SearchTextBox.Text ?? string.Empty);
        };
        LoadSettings();
        InitMcpBridge();
        _mcpBridge.DisabledTools = _settings.DisabledMcpTools;
        if (_settings.McpServerEnabled)
            _ = _mcpServer.StartAsync(_settings.McpServerPort, _mcpBridge);
        RestorePlaybackQueueState();
        _libraryWatcher = new LibraryWatcherService(OnWatchedLibraryChanged);
        _libraryWatcher.UpdatePaths(_settings.LibraryPaths);
        RestoreFixedDataGridColumnWidths();
        AttachDataGridColumnChoosers();
        LoadCatalogFilterCache();
        LoadNavPlaylists();
        _showAlbumArtworkView = _settings.AlbumArtworkView;
        _showArtistArtworkView = _settings.ArtistArtworkView;
        VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 1);
        AlbumArtworkViewRadioButton.IsChecked = _showAlbumArtworkView;
        AlbumTableViewRadioButton.IsChecked = !_showAlbumArtworkView;
        SelectInitialView();
        RestoreLastTrackState();
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
        PositionSlider.AddHandler(Slider.PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>(PositionSlider_OnPreviewMouseLeftButtonDown),
            handledEventsToo: true);
        PositionSlider.AddHandler(Slider.PointerReleasedEvent,
            new EventHandler<PointerReleasedEventArgs>(PositionSlider_OnPreviewMouseLeftButtonUp),
            handledEventsToo: true);
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
                i, i == _queueIndex, item.FilePath, item.FileName)).ToList();
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
            _queue.Add(new PlaylistItem(path));
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            await RefreshActiveGaplessQueueAsync();
        };
        _mcpBridge.PlayNextFunc = async path =>
        {
            var insertIndex = Math.Clamp(_queueIndex + 1, 0, _queue.Count);
            _queue.Insert(insertIndex, new PlaylistItem(path));
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            await RefreshActiveGaplessQueueAsync();
        };
        _mcpBridge.RefreshPlaylistsFunc = LoadNavPlaylists;
    }

    protected override void OnClosed(EventArgs e)
    {
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
        if (Interlocked.Increment(ref _libraryWatcherRefreshPending) != 1)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            while (true)
            {
                Interlocked.Exchange(ref _libraryWatcherRefreshPending, 1);
                try
                {
                    LoadNavPlaylists();
                    if (SearchResultsScrollViewer.IsVisible &&
                        !string.IsNullOrWhiteSpace(SearchTextBox.Text))
                    {
                        await ShowSearchResultsAsync(SearchTextBox.Text);
                    }
                    else if (_currentTopLevelTag is "Dashboard" or "Artists" or "Albums" or "Tracks" or "Folders" ||
                             _currentTopLevelTag?.StartsWith("Playlist:", StringComparison.Ordinal) == true)
                    {
                        await ShowTopLevelViewAsync(_currentTopLevelTag);
                    }
                }
                catch
                {
                    // Background library refreshes must not affect playback or input handling.
                }
                if (Interlocked.CompareExchange(ref _libraryWatcherRefreshPending, 0, 1) == 1)
                    break;
            }
        }, DispatcherPriority.Background);
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
        PlaylistsHeaderItem.ContextFlyout = BuildPlaylistsHeaderContextFlyout();
        foreach (var dynamicItem in NavListBox.Items
                     .OfType<ListBoxItem>()
                     .Where(item => item.Tag is string tag &&
                                    (tag.StartsWith("Radio:", StringComparison.Ordinal) ||
                                     tag.StartsWith("Podcast:", StringComparison.Ordinal) ||
                                     tag.StartsWith("Plex", StringComparison.Ordinal) ||
                                     tag.StartsWith("Playlist:", StringComparison.Ordinal)))
                     .ToList())
            NavListBox.Items.Remove(dynamicItem);

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var podcastHeaderIndex = NavListBox.Items.IndexOf(MyPodcastsHeaderItem);
            foreach (var radio in db.GetRadioStations())
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new TextBlock
                {
                    Text = "◉ ",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF))
                });
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
            foreach (var podcast in db.GetPodcasts())
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new TextBlock
                {
                    Text = "◍ ",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF))
                });
                content.Children.Add(CreateSidebarEntryText(podcast.Name));

                NavListBox.Items.Insert(plexHeaderIndex++, new ListBoxItem
                {
                    Content = content,
                    Tag = $"Podcast:{podcast.Id}",
                    Theme = FindResource<ControlTheme>("NavItemTheme"),
                    ContextFlyout = BuildDeletePodcastContextFlyout(podcast)
                });
            }

            LoadPlexNavigationAsync();

            foreach (var pl in db.GetAllPlaylists())
            {
                object content;
                if (pl.IsSmartPlaylist)
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(new TextBlock
                    {
                        Text       = "⚡ ",
                        Foreground = new SolidColorBrush(
                                         Color.FromRgb(0xFF, 0xCC, 0x00))
                    });
                    sp.Children.Add(CreateSidebarEntryText(pl.Name));
                    content = sp;
                }
                else
                {
                    content = CreateSidebarEntryText(pl.Name);
                }

                NavListBox.Items.Add(new ListBoxItem
                {
                    Content = content,
                    Tag     = $"Playlist:{pl.Id}",
                    Theme = FindResource<ControlTheme>("NavItemTheme"),
                    ContextFlyout = BuildPlaylistSidebarContextFlyout(pl)
                });
            }
        }
        catch { /* DB noch nicht angelegt */ }

        ApplySidebarNavigationSettings();
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
                Content = CreateSidebarEntryText(server.Name),
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
        var text = CreateSidebarEntryText($"   {title}");
        text.Foreground = FindResource<IBrush>("AppMutedTextBrush");
        return new ListBoxItem
        {
            Content = text,
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

    private void NavListBox_OnPreviewMouseLeftButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(NavListBox).Properties.IsLeftButtonPressed)
            return;
        if (FindAncestor<ListBoxItem>(e.Source as Visual) is not
            { Tag: string tag } ||
            !tag.StartsWith("Section:", StringComparison.Ordinal))
        {
            return;
        }

        var section = tag["Section:".Length..];
        SetSidebarSectionExpanded(section, !IsSidebarSectionExpanded(section));
        ApplySidebarNavigationSettings();
        e.Handled = true;
    }

    private void RestorePlaybackQueueState()
    {
        _queue.Clear();
        foreach (var path in _settings.PlaybackQueuePaths.Where(CanPersistQueuePath))
        {
            var isRemoteUrl = Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                              !uri.IsFile &&
                              !uri.Scheme.Equals("cue", StringComparison.OrdinalIgnoreCase);
            if (isRemoteUrl || IsAvailableLocalTrack(path))
            {
                _queue.Add(new PlaylistItem(path));
            }
        }

        _queueIndex = _queue.Count == 0
            ? -1
            : Math.Clamp(_settings.PlaybackQueueIndex, 0, _queue.Count - 1);
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

        _settings.PlaybackQueuePaths = persisted;
        _settings.PlaybackQueueIndex = persistedIndex >= 0
            ? persistedIndex
            : persisted.Count == 0 ? -1 : Math.Min(_queueIndex, persisted.Count - 1);
    }

    private static bool CanPersistQueuePath(string path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.IsFile)
            return true;
        if (uri.Scheme.Equals("cue", StringComparison.OrdinalIgnoreCase))
            return true;
        if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        return !uri.Query.Contains("X-Plex-Token", StringComparison.OrdinalIgnoreCase) &&
               !uri.Query.Contains("token=", StringComparison.OrdinalIgnoreCase);
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
        InternetRadioNavItem.IsVisible = _settings.ShowInternetRadioItem;
        PodcastsNavItem.IsVisible      = _settings.ShowPodcastsItem;
        QueueNavItem.IsVisible         = _settings.ShowQueueItem;
        SetSidebarSectionVisibility(
            LocalLibraryHeaderItem,
            "LocalLibrary",
            _settings.ShowLocalLibrarySection,
            staticItems: [ArtistsNavItem, AlbumsNavItem, TracksNavItem, FoldersNavItem]);
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
        SetSidebarSectionVisibility(
            PlaylistsHeaderItem,
            "Playlists",
            _settings.ShowPlaylistsSection,
            dynamicPrefix: "Playlist:");
    }

    private void SetSidebarSectionVisibility(
        ListBoxItem header,
        string section,
        bool isVisible,
        IReadOnlyList<ListBoxItem>? staticItems = null,
        string? dynamicPrefix = null)
    {
        header.IsVisible = isVisible;
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
                item.IsVisible = showItems;
        }

        if (dynamicPrefix is null)
            return;

        foreach (var item in NavListBox.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is string tag && tag.StartsWith(dynamicPrefix, StringComparison.Ordinal))
                item.IsVisible = showItems;
        }
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
        if (tag.StartsWith("Section:", StringComparison.Ordinal))
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
        if (tag.StartsWith("Section:", StringComparison.Ordinal))
            return;

        // Beim erneuten Klick auf den bereits markierten Hauptpunkt feuert kein SelectionChanged.
        // Trotzdem soll die ungefilterte Top-Level-Ansicht wiederhergestellt werden.
        if (_activeAlbumFilterId is null && _activeArtistFilterId is null)
            return;

        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        await ShowTopLevelViewAsync(tag);
    }

    private void ResetDrilldownState(bool clearNavigationHistory = true)
    {
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        _activeArtistFilterId = null;
        _activeArtistFilterName = null;
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

        if (_activeAlbumFilterId is long albumId)
            return new NavigationState(
                "AlbumTracks",
                albumId,
                _activeArtistFilterId,
                _activeArtistFilterName,
                _activeAlbumFilterTitle);

        if (_activeAlbumFilterId is null &&
            _activeArtistFilterId is long artistId &&
            ContentTitleTextBlock.Text?.StartsWith(
                $"{LocalizationManager.Current.Albums} · ",
                StringComparison.Ordinal) == true)
        {
            return new NavigationState(
                "ArtistAlbums",
                GetSelectedContentRowId(),
                artistId,
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
        _currentTopLevelTag = tag;
        LyricsView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        _activePlaylistId = tag.StartsWith("Playlist:") &&
                            long.TryParse(tag.AsSpan("Playlist:".Length), out long parsedPid)
            ? parsedPid : null;

        ContentTitleTextBlock.Text = tag switch
        {
            "Artists" => LocalizationManager.Current.Artists,
            "Albums"  => LocalizationManager.Current.Albums,
            "Tracks"  => LocalizationManager.Current.Tracks,
            "Folders" => LocalizationManager.Current.FolderStructure,
            "Queue" => LocalizationManager.Current.UpNext,
            "InternetRadio" => LocalizationManager.Current.InternetRadio,
            "Podcasts" => LocalizationManager.Current.Podcasts,
            _ when tag.StartsWith("PlexLibrary:", StringComparison.Ordinal) =>
                _activePlexSectionTitle ?? LocalizationManager.Current.PlexServers,
            _ when tag.StartsWith("Radio:", StringComparison.Ordinal) => GetRadioName(tag),
            _ when tag.StartsWith("Podcast:", StringComparison.Ordinal) => GetPodcastName(tag),
            _         => tag.StartsWith("Playlist:") ? GetPlaylistName(tag) : tag
        };

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
        InternetRadioView.IsVisible = false;
        PodcastView.IsVisible = false;
        PodcastEpisodesView.IsVisible = false;
        HideAlbumDetailHeader();
        ContentCountTextBlock.Text  = "";
        UpdateLibraryIntroCard(tag);
        SearchTextBox.IsVisible = !(tag is "InternetRadio" or "Podcasts" or "Queue" ||
                                    tag.StartsWith("Radio:", StringComparison.Ordinal) ||
                                    tag.StartsWith("Podcast:", StringComparison.Ordinal) ||
                                    tag.StartsWith("PlexLibrary:", StringComparison.Ordinal));
        UpdateEntityFavoritesFilterToggle(tag);
        AlbumViewModeBorder.IsVisible = tag is "Albums" or "Artists";
        PlexViewModeBorder.IsVisible = tag.StartsWith("PlexLibrary:", StringComparison.Ordinal);
        if (tag is "Albums" or "Artists")
            SetViewModeButtons(tag == "Albums" ? _showAlbumArtworkView : _showArtistArtworkView);
        TrackFilterButton.IsVisible = tag == "Tracks" ? true : false;
        SaveSmartPlaylistButton.IsVisible = tag == "Tracks" ? true : false;
        SaveQueueAsPlaylistButton.IsVisible = tag == "Queue";
        SaveQueueAsPlaylistButton.IsEnabled =
            _queue.Any(item => CanPersistQueuePath(item.FilePath));
        if (tag == "Tracks") UpdateSaveSmartPlaylistButtonState();
        TrackFilterPopup.IsOpen = false;
        if (tag == "Dashboard")
        {
            ContentDataGrid.IsVisible = false;
            FolderTreeView.IsVisible = false;
            AlbumArtworkListBox.IsVisible = false;
            ArtistArtworkListBox.IsVisible = false;
            DashboardScrollViewer.IsVisible = true;
            await ShowDashboardAsync();
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
                await SearchRadioStationsAsync();
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
            await PlaySavedRadioAsync(radioId);
        }
        else if (tag.StartsWith("PlexLibrary:", StringComparison.Ordinal))
        {
            await ShowPlexLibraryAsync(tag);
        }
        else if (tag == "Folders")
        {
            ContentDataGrid.IsVisible = false;
            FolderTreeView.IsVisible = true;
            AlbumArtworkListBox.IsVisible = false;
            ArtistArtworkListBox.IsVisible = false;

            var tracks = await Task.Run(() =>
            {
                try { using var db = AudioDatabase.OpenDefault(); return db.GetTracksLite(); }
                catch { return new List<TrackLite>(); }
            });

            BuildFolderTree(tracks);
            ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(tracks.Count);
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

            var rows = tag == "Tracks"
                ? await Task.Run(GetFilteredTrackRows)
                : await Task.Run(() => QueryRows(tag));
            ApplyColumns(tag);
            ContentDataGrid.ItemsSource = rows;
            if (tag == "Albums")
                BindArtworkRows(tag, rows);
            else if (tag == "Artists")
                BindArtworkRows(tag, rows);
            UpdateAlphabetIndex(rows, tag is "Artists" or "Albums" or "Tracks");
            ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
        }
    }

    private void UpdateAlphabetIndex(IEnumerable<ContentRow>? source, bool visible)
    {
        var rows = source?.ToList() ?? [];
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
            "Dashboard" => (strings.DashboardIntroTitle, strings.DashboardIntroHint, "▦"),
            "Artists" => (strings.ArtistsIntroTitle, strings.ArtistsIntroHint, "●"),
            "Albums" => (strings.AlbumsIntroTitle, strings.AlbumsIntroHint, "▣"),
            "Tracks" => (strings.TracksIntroTitle, strings.TracksIntroHint, "♪"),
            "Folders" => (strings.FoldersIntroTitle, strings.FoldersIntroHint, "⌁"),
            _ => default
        };

        var visible = !string.IsNullOrWhiteSpace(intro.Item1);
        LibraryIntroCard.IsVisible = visible;
        if (!visible)
            return;

        LibraryIntroTitleTextBlock.Text = intro.Item1;
        LibraryIntroHintTextBlock.Text = intro.Item2;
        LibraryIntroIconTextBlock.Text = intro.Item3;
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
            EntityType = entityType
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
            UpdateContentDataGridPageSize();
            UpdateActiveAlphabetButton();
        }
    }

    private void UpdateContentDataGridPageSize()
    {
        if (_contentDataGridVerticalScrollBar is null)
            return;

        // DataGrid scroll values are pixels. One page intentionally overlaps by one
        // row so users retain visual context after clicking the scrollbar track.
        var visibleRows = FindVisualChildren<DataGridRow>(ContentDataGrid)
            .Where(row => row.IsVisible && row.Bounds.Height > 0)
            .ToList();
        var rowHeight = visibleRows.Count > 0
            ? visibleRows.Average(row => row.Bounds.Height)
            : 0;
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

                    var facets = db.GetSmartPlaylistTracks();
                    var ids = OrderSmartPlaylistTracks(
                            facets.Where(f => MatchesCriteria(f, criteria)),
                            criteria.SortOrder)
                        .Take(criteria.ResultLimit is > 0 ? criteria.ResultLimit.Value : int.MaxValue)
                        .Select(f => f.Id)
                        .ToList();
                    return db.GetTrackListFiltered(ids)
                        .Select((t, i) => new ContentRow
                        {
                            Nr       = (i + 1).ToString(),
                            Id       = t.Id,
                            Title    = t.Title ?? t.FileName,
                            Artist   = t.Artist,
                            Album    = t.Album,
                            Duration = FormatSeconds(t.Duration),
                            Genre    = t.Genre,
                            Format   = t.Format?.ToUpperInvariant(),
                            FilePath = t.Path,
                            IsFavorite = t.IsFavorite
                        }).ToList();
                }

                var ptracks = db.GetPlaylistTracks(pid).ToList();
                return ptracks.Select((pt, i) =>
                {
                    var t = db.GetByPath(pt.Path);
                    if (t is null)
                    {
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

                "Artists" => db.GetArtistsLite()
                    .Where(a => !_artistFavoritesOnly || a.IsFavorite)
                    .Select(a => new ContentRow
                    {
                        Id       = a.Id,
                        ArtistId = a.Id,
                        Title    = string.IsNullOrEmpty(a.Artist) ? LocalizationManager.Current.Unknown : a.Artist,
                        IsFavorite = a.IsFavorite,
                        ArtworkPath = a.ImagePath,
                        ThumbnailPath = a.ImagePath,
                        Biography = a.Biography,
                        SourceUrl = a.SourceUrl,
                        ProfileLanguage = a.ProfileLanguage,
                        ProfileFetchedAt = a.ProfileFetchedAt,
                        ImageIsManual = a.ImageIsManual,
                        EntityType = "Artist",
                        FilePath = ""
                    }).ToList(),

                "Albums" => (_activeArtistFilterId is long artistId
                        ? db.GetAlbumsByArtist(artistId, _showAlbumArtworkView)
                        : db.GetAlbumsLite(_showAlbumArtworkView))
                    .Where(a => !_albumFavoritesOnly || a.IsFavorite)
                    .Select(a => new ContentRow
                    {
                        Title    = string.IsNullOrEmpty(a.Album) ? LocalizationManager.Current.Unknown : a.Album,
                        Id       = a.Id,
                        AlbumId  = a.Id,
                        Artist   = string.IsNullOrEmpty(a.DisplayArtist) ? null : a.DisplayArtist,
                        Year     = a.Year?.ToString(),
                        ArtworkPath = a.ArtworkPath,
                        ThumbnailPath = a.ThumbnailPath,
                        IsFavorite = a.IsFavorite,
                        EntityType = "Album",
                        FilePath = ""
                    }).ToList(),

                _ => (_activeAlbumFilterId is long albumId
                        ? db.GetTrackListByAlbum(albumId)
                        : db.GetTrackList())  // "Tracks" und Fallback
                    .Select(ToTrackContentRow)
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
        IsFavorite = t.IsFavorite
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
        var rows = await Task.Run(GetFilteredTrackRows);
        ContentDataGrid.ItemsSource = rows;
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
        UpdateSaveSmartPlaylistButtonState();
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
        _selectedTrackFormats.Count > 0 || _selectedTrackBitrates.Count > 0;

    private void UpdateSaveSmartPlaylistButtonState()
    {
        SaveSmartPlaylistButton.IsEnabled = HasActiveFilters;
    }

    private async void SaveSmartPlaylistButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new NewPlaylistDialog();
        if (await dialog.ShowDialog<bool>(this) == false || string.IsNullOrWhiteSpace(dialog.PlaylistName))
            return;

        var criteria = new SmartPlaylistCriteria
        {
            FavoritesOnly = _trackFavoritesOnly,
            Genres = [.. _selectedTrackGenres.OrderBy(g => g)],
            Formats = [.. _selectedTrackFormats.OrderBy(f => f)],
            Bitrates = [.. _selectedTrackBitrates.OrderBy(b => b)]
        };
        var json = JsonSerializer.Serialize(criteria);
        var name = dialog.PlaylistName.Trim();

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

    private static IEnumerable<string> SplitGenres(string? genre)
        => string.IsNullOrWhiteSpace(genre)
            ? []
            : genre.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
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
        if (tag == "Artists")
            _artistFavoritesOnly = isChecked;
        else
            _albumFavoritesOnly = isChecked;

        await ReloadEntityRowsAsync(tag);
    }

    private string? GetActiveEntityFavoritesView()
    {
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
        var visible = tag is "Artists" or "Albums";
        _updatingEntityFavoritesFilter = true;
        try
        {
            EntityFavoritesOnlyCheckBox.IsVisible = visible;
            EntityFavoritesOnlyCheckBox.IsChecked = tag switch
            {
                "Artists" => _artistFavoritesOnly,
                "Albums" => _albumFavoritesOnly,
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

        var rows = await Task.Run(() => QueryRows(tag));
        ApplyColumns(tag);
        ContentDataGrid.ItemsSource = rows;
        BindArtworkRows(tag, rows);
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
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

        var result = await Task.Run(() =>
        {
            var ids = TrackSearchIndex.SearchByCategory(query);
            using var db = AudioDatabase.OpenDefault();
            var albumScores = BuildEntityScores(db.GetAlbumIdsByTrackIds(ids.Albums.Ids), ids.Albums.Scores);
            var artistScores = BuildEntityScores(db.GetArtistIdsByTrackIds(ids.Artists.Ids), ids.Artists.Scores);
            return (
                Tracks: SortBySearchScore(
                    db.GetTrackListByIds(ids.Tracks.Ids).Select(ToTrackContentRow),
                    ids.Tracks.Scores),
                Albums: SortBySearchScore(
                    db.GetAlbumsByTrackIds(ids.Albums.Ids).Select(ToAlbumContentRow),
                    albumScores),
                Artists: SortBySearchScore(
                    db.GetArtistsByTrackIds(ids.Artists.Ids).Select(ToArtistContentRow),
                    artistScores));
        });

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

    private static void EnsureArtworkHydrated(ContentRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ArtworkPath))
            row.Artwork = null;
        else if (row.Artwork is null)
            row.Artwork = CreateArtworkImage(row.ArtworkPath, 320);

        if (string.IsNullOrWhiteSpace(row.ThumbnailPath))
            row.Thumbnail = null;
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

    private void AppendArtworkRowsIfNeeded(ListBox listBox)
    {
        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
            return;

        var itemHeight = ReferenceEquals(listBox, AlbumArtworkListBox) ? 292d : 260d;
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
            return;

        var itemWidth = ReferenceEquals(listBox, AlbumArtworkListBox) ? 196d : 216d;
        var itemHeight = ReferenceEquals(listBox, AlbumArtworkListBox) ? 292d : 260d;
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
            image = await Task.Run(() => CreateArtworkImage(path, 320));
        }
        catch
        {
            image = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            row.ArtworkLoadQueued = false;
            row.ArtworkLoadCompleted = true;
            if (string.Equals(row.ArtworkPath, path, StringComparison.OrdinalIgnoreCase))
                row.Artwork = image;
        }, DispatcherPriority.Background);
    }

    private static void EnsureThumbnailHydrated(ContentRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ThumbnailPath))
            row.Thumbnail = null;
        else if (row.Thumbnail is null)
            row.Thumbnail = CreateArtworkImage(row.ThumbnailPath, 96);
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
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: string tag } ||
            tag is not ("Albums" or "Artists"))
            return;

        if (tag == "Albums")
        {
            _showAlbumArtworkView = artworkMode;
            _settings.AlbumArtworkView = artworkMode;
        }
        else
        {
            _showArtistArtworkView = artworkMode;
            _settings.ArtistArtworkView = artworkMode;
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
        ApplyNowPlayingClass(e.Row);
        SetPlaylistContextFlyout(e.Row);
        if (e.Row.DataContext is not ContentRow row)
            return;
        EnsureThumbnailHydrated(row);
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
        if (!isTrack && !isAlbum)
            return;

        row.ContextFlyout = contentRow.EntityType.StartsWith("Plex", StringComparison.Ordinal) ||
                            !CanPersistQueuePath(contentRow.FilePath)
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
                AddThumbnail();
                AddArtistInfo();
                AddEntityLink(LocalizationManager.Current.Artist, nameof(ContentRow.Title), 0, "artist", true, "Artist");
                break;
            case "Albums":
                AddFavorite();
                AddThumbnail();
                AddEntityLink(LocalizationManager.Current.Album, nameof(ContentRow.Title), 0, "album", true, "Album");
                AddEntityLink(LocalizationManager.Current.AlbumArtist, nameof(ContentRow.Artist), 220, "artist", false, "Artist");
                Add(LocalizationManager.Current.Year, nameof(ContentRow.Year), 60, "year", right: true);
                break;
            case string s when s.StartsWith("Playlist:"):
                Add("#", nameof(ContentRow.Nr), 38, "position", right: true);
                AddTrackColumns(includeFavorite: false, includeGenreByDefault: false);
                break;
            case "Queue":
                Add("#", nameof(ContentRow.Nr), 38, "position", right: true);
                Add(LocalizationManager.Current.Title, nameof(ContentRow.Title), 0, "title", star: true, starWeight: 2.3);
                Add(LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 0, "artist", star: true, starWeight: 1.05);
                Add(LocalizationManager.Current.Album, nameof(ContentRow.Album), 0, "album", star: true, starWeight: 1.05);
                Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 80, "duration", right: true);
                AddQueueActions();
                break;
            default: // Tracks
                AddTrackColumns(includeFavorite: true, includeGenreByDefault: true);
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

        void AddTrackColumns(bool includeFavorite, bool includeGenreByDefault)
        {
            if (includeFavorite)
                AddFavorite();
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

        void AddFavorite()
        {
            grid.Columns.Add(new DataGridTemplateColumn
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
                        Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)),
                        Cursor = new Cursor(StandardCursorType.Hand),
                        FontFamily = new FontFamily("Segoe UI Symbol"),
                        FontSize = 17
                    };
                    button.Bind(Button.ContentProperty, new Binding(nameof(ContentRow.FavoriteGlyph)));
                    button.Bind(Button.TagProperty, new Binding("."));
                    button.Click += FavoriteButton_OnClick;
                    return button;
                })
            });
        }

        void AddThumbnail()
        {
            grid.Columns.Add(new DataGridTemplateColumn
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
            });
        }

        void AddArtistInfo()
        {
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(44),
                CellTemplate = new FuncDataTemplate<ContentRow>((_, _) =>
                {
                    var button = new Button
                    {
                        Content = "ℹ",
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(0),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)),
                        Cursor = new Cursor(StandardCursorType.Hand),
                        FontSize = 16,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
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
            else
            {
                row = new ContentRow
                {
                    Title = Path.GetFileNameWithoutExtension(item.FilePath),
                    FileName = Path.GetFileName(item.FilePath),
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

    private void BuildFolderTree(List<TrackLite> tracks)
    {
        _localFolderTrackItems.Clear();
        _localFolderTrackHeaders.Clear();
        FolderTreeView.Items.Clear();
        if (tracks.Count == 0) return;

        var tree  = new FolderTree(tracks);
        var roots = _settings.LibraryPaths.Where(p => !string.IsNullOrWhiteSpace(p) && tree.HasRoot(p))
                              .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                              .ToList();
        if (roots.Count == 0)
            roots = [.. tree.AutoRoots()];

        foreach (var root in roots)
        {
            var item = CreateDirItemLazy(root, tree, isRoot: true);
            FolderTreeView.Items.Add(item);
        }
    }

    private TreeViewItem CreateDirItemLazy(string dirPath, FolderTree tree, bool isRoot)
    {
        var name = Path.GetFileName(dirPath);
        var item = new TreeViewItem
        {
            Header = isRoot ? dirPath : (string.IsNullOrEmpty(name) ? dirPath : name),
            Tag = new FolderTag(false, dirPath, dirPath),
            ContextFlyout = CreateSidebarMenuFlyout()
        };
        ApplyNowPlayingClass(item);
        AttachFolderPlaylistContextHandler(item);

        if (!tree.HasChildren(dirPath))
            return item;

        PopulateDirNode(item, dirPath, tree);
        if (isRoot)
            item.IsExpanded = true;

        return item;
    }

    private void PopulateDirNode(TreeViewItem parent, string dirPath, FolderTree tree)
    {
        parent.Items.Clear();
        foreach (var sub in tree.SubDirs(dirPath))
            parent.Items.Add(CreateDirItemLazy(sub, tree, isRoot: false));
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
                Tag = new FolderTag(true, track.Path, dirPath),
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
            var folderTracks = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetTracksByDirectory(folderPath);
            });

            _queue.Clear();
            foreach (var t in folderTracks)
                _queue.Add(new PlaylistItem(t.Path));

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

    // ------------------------------------------------------------------
    // Content-Doppelklick → Wiedergabe
    // ------------------------------------------------------------------

    private async void ContentDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (sender is not DataGrid grid ||
            grid.SelectedItem is not ContentRow row)
            return;

        if (row.EntityType == "Track" &&
            grid.ItemsSource is IEnumerable<ContentRow> rows)
        {
            await PlayTrackFromRowsAsync(row, rows.ToList());
            return;
        }

        await HandleContentRowDoubleClickAsync(row);
    }

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
        if (SearchTracksDataGrid.SelectedItem is not ContentRow row)
            return;

        var allRows = (SearchTracksDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        await PlayTrackFromRowsAsync(row, allRows);
    }

    private async void SearchAlbumsDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (SearchAlbumsDataGrid.SelectedItem is ContentRow { Id: long albumId } row)
        {
            await ShowAlbumTracksAsync(albumId, row.Title ?? LocalizationManager.Current.Unknown);
        }
    }

    private async void SearchArtistsDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (SearchArtistsDataGrid.SelectedItem is ContentRow { Id: long artistId } row)
        {
            await ShowArtistAlbumsAsync(artistId, row.Title ?? LocalizationManager.Current.Unknown);
        }
    }

    private async void ArtistLinkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row })
            return;

        e.Handled = true;
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
        if (_currentArtistId is long artistId)
            await ShowArtistAlbumsAsync(
                artistId,
                _currentArtistName ?? LocalizationManager.Current.Unknown);
    }

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

        if (row.EntityType == "Artist" && row.Id is long artistId)
        {
            await ShowArtistAlbumsAsync(artistId, row.Title ?? "(Unbekannt)");
            return;
        }

        if (AlbumViewModeBorder.IsVisible && row.Id is long albumId)
        {
            await ShowAlbumTracksAsync(albumId, row.Title ?? "(Unbekannt)");
            return;
        }

        if (string.IsNullOrEmpty(row.FilePath))
            return;

        var allRows = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        await PlayTrackFromRowsAsync(row, allRows);
    }

    private async Task PlayTrackFromRowsAsync(ContentRow row, List<ContentRow> allRows)
    {
        if (string.IsNullOrEmpty(row.FilePath))
            return;

        _queue.Clear();
        foreach (var r in allRows.Where(r => !string.IsNullOrEmpty(r.FilePath)))
            _queue.Add(new PlaylistItem(r.FilePath));

        _queueIndex = _queue.IndexOf(_queue.FirstOrDefault(p => p.FilePath == row.FilePath) ?? _queue[0]);
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(row.FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private async Task ShowAlbumTracksAsync(long albumId, string albumTitle)
    {
        PushCurrentNavigationState();
        _activeAlbumFilterId = albumId;
        _activeAlbumFilterTitle = albumTitle;
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

    private static string NormalizeAlbumGroupValue(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    private async void ShowAllAlbumTracksCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingAlbumTrackScope || _activeAlbumFilterId is null)
            return;

        _showAllAlbumTracks = ShowAllAlbumTracksCheckBox.IsChecked == true;
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
    }

    private static ContentRow CreateAlbumDetailRow(AlbumInfo album) =>
        new()
        {
            Id = album.Id,
            AlbumId = album.Id,
            Title = string.IsNullOrWhiteSpace(album.Album)
                ? LocalizationManager.Current.Unknown
                : album.Album,
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
                Height = 44 + (group.Rows.Count * 40),
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
            .Select(row => row.FilePath)
            .ToList() ?? [];
        if (paths.Count == 0)
            return;

        button.ContextFlyout = BuildPlaylistContextFlyout(paths);
        e.Handled = true;
        button.ContextFlyout.ShowAt(button);
    }

    private async Task ShowArtistAlbumsAsync(long artistId, string artistName)
    {
        PushCurrentNavigationState();
        _activeArtistFilterId = artistId;
        _activeArtistFilterName = artistName;
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        ContentTitleTextBlock.Text = $"{LocalizationManager.Current.Albums} · {artistName}";
        UpdateLibraryIntroCard(null);
        UpdateEntityFavoritesFilterToggle("Albums");
        AlbumViewModeBorder.IsVisible = true;
        SetViewModeButtons(_showAlbumArtworkView);
        ContentDataGrid.IsVisible = !(_showAlbumArtworkView);
        AlbumArtworkListBox.IsVisible = _showAlbumArtworkView;
        ArtistArtworkListBox.IsVisible = false;
        FolderTreeView.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        HideAlbumDetailHeader();

        var rows = await Task.Run(() => QueryRows("Albums"));
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

        await OpenCoverSearchAsync(row);
    }

    private async void DeleteCoverMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow { Id: long albumId } })
            return;

        var verticalOffset = CaptureCurrentVerticalOffset();
        using (var db = AudioDatabase.OpenDefault())
            db.ClearArtworkFromAlbum(albumId);

        if (_activeAlbumFilterId == albumId)
            await ReloadAlbumDetailHeaderAsync(albumId);
        else
            await ReloadAlbumRowsAsync(albumId, verticalOffset);
    }

    private async void ReassignCoverMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow row })
            return;

        await OpenCoverSearchAsync(row);
    }

    private async Task OpenCoverSearchAsync(ContentRow row)
    {
        if (row.Id is not long albumId)
            return;

        var verticalOffset = CaptureCurrentVerticalOffset();
        var dialog = new CoverSearchWindow(row.Title ?? string.Empty) ;
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        using (var db = AudioDatabase.OpenDefault())
            db.AttachArtworkToAlbum(albumId, selected.ImageData, selected.MimeType);

        if (_activeAlbumFilterId == albumId)
            await ReloadAlbumDetailHeaderAsync(albumId);
        else
            await ReloadAlbumRowsAsync(albumId, verticalOffset);
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
                var itemWidth = ReferenceEquals(listBox, AlbumArtworkListBox) ? 196d : 216d;
                var itemHeight = ReferenceEquals(listBox, AlbumArtworkListBox) ? 292d : 260d;
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

    private void UpdateNowPlayingFavoriteButton()
    {
        NowPlayingFavoriteButton.IsEnabled = _currentTrackId.HasValue;
        NowPlayingFavoriteGlyph.Text = _currentTrackIsFavorite ? "♥" : "♡";
    }

    private void NowPlayingFavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
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
            {
                row.IsFavorite = _currentTrackIsFavorite;
                { var _tmp = ContentDataGrid.ItemsSource; ContentDataGrid.ItemsSource = null; ContentDataGrid.ItemsSource = _tmp; };
            }
        }
    }

    private async void FavoriteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row } || row.Id is not long id)
            return;

        row.IsFavorite = !row.IsFavorite;
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

        AppendPlaylistItems(menu, GetPathsForRow(row));
    }

    private MenuFlyout BuildPlaylistContextFlyout(IReadOnlyList<string> paths)
    {
        var menu = CreateSidebarMenuFlyout();
        AppendPlaylistItems(menu, paths);
        return menu;
    }

    private MenuFlyout BuildQueueContextFlyout(IReadOnlyList<string> paths)
    {
        var menu = CreateSidebarMenuFlyout();
        foreach (var item in CreateQueueMenuItems(paths))
            menu.Items.Add(item);
        return menu;
    }

    private void AppendPlaylistItems(MenuFlyout menu, IReadOnlyList<string> paths)
    {
        foreach (var item in CreatePlaylistMenuItems(paths))
            menu.Items.Add(item);
    }

    private void AppendPlaylistItems(ItemsControl menu, IReadOnlyList<string> paths)
    {
        foreach (var item in CreatePlaylistMenuItems(paths))
            menu.Items.Add(item);
    }

    private IReadOnlyList<Control> CreatePlaylistMenuItems(IReadOnlyList<string> paths)
    {
        var items = new List<Control>(CreateQueueMenuItems(paths));
        items.Add(new Separator());

        var header = CreateFlyoutMenuItem(
            LocalizationManager.Current.AddToPlaylist,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontSize = 11;
        header.FontWeight = FontWeight.SemiBold;
        items.Add(header);

        var sep0 = new Separator();
        items.Add(sep0);

        List<PlaylistRecord> playlists;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            playlists = db.GetAllPlaylists().ToList();
        }
        catch { playlists = []; }

        foreach (var pl in playlists)
        {
            var item = CreateFlyoutMenuItem(pl.Name);
            item.Tag = new PlaylistMenuTag(pl.Id, paths);
            item.Click += PlaylistMenuItem_OnClick;
            items.Add(item);
        }

        if (playlists.Count > 0)
        {
            var sep1 = new Separator();
            items.Add(sep1);
        }

        var newItem = CreateFlyoutMenuItem(LocalizationManager.Current.NewPlaylist);
        newItem.Tag = paths;
        newItem.Click += NewPlaylistMenuItem_OnClick;
        items.Add(newItem);
        return items;
    }

    private IReadOnlyList<Control> CreateQueueMenuItems(IReadOnlyList<string> paths)
    {
        var items = new List<Control>();
        var queueHeader = CreateFlyoutMenuItem(
            LocalizationManager.Current.UpNext,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        queueHeader.IsHitTestVisible = false;
        queueHeader.Focusable = false;
        queueHeader.FontSize = 11;
        queueHeader.FontWeight = FontWeight.SemiBold;
        items.Add(queueHeader);

        var playNextItem = CreateFlyoutMenuItem(LocalizationManager.Current.PlayNext);
        playNextItem.Tag = paths;
        playNextItem.Click += PlayNextMenuItem_OnClick;
        items.Add(playNextItem);

        var appendQueueItem = CreateFlyoutMenuItem(LocalizationManager.Current.AppendToQueue);
        appendQueueItem.Tag = paths;
        appendQueueItem.Click += AppendToQueueMenuItem_OnClick;
        items.Add(appendQueueItem);
        return items;
    }

    private async void PlayNextMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: IReadOnlyList<string> paths } || paths.Count == 0)
            return;

        var insertIndex = Math.Clamp(_queueIndex + 1, 0, _queue.Count);
        foreach (var path in paths)
            _queue.Insert(insertIndex++, new PlaylistItem(path));
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        StatusTextBlock.Text = string.Format(
            LocalizationManager.Current.TracksQueuedNext,
            paths.Count);
        await RefreshActiveGaplessQueueAsync();
    }

    private async void AppendToQueueMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: IReadOnlyList<string> paths } || paths.Count == 0)
            return;

        foreach (var path in paths)
            _queue.Add(new PlaylistItem(path));
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        StatusTextBlock.Text = string.Format(
            LocalizationManager.Current.TracksAppendedToQueue,
            paths.Count);
        await RefreshActiveGaplessQueueAsync();
    }

    private void PlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: PlaylistMenuTag tag })
            return;

        var paths = tag.Paths;
        if (paths.Count == 0) return;

        string playlistName;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            playlistName = db.GetPlaylistById(tag.PlaylistId)?.Name ?? string.Empty;
            foreach (var path in paths)
                db.AddTrackToPlaylist(tag.PlaylistId, path, db.GetTrackIdByPath(path));
        }
        catch { return; }

        StatusTextBlock.Text = paths.Count == 1
            ? string.Format(LocalizationManager.Current.TrackAddedToPlaylist, playlistName)
            : string.Format(LocalizationManager.Current.TracksAddedToPlaylist, paths.Count, playlistName);
    }

    private async void NewPlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: IReadOnlyList<string> paths })
            return;

        var dialog = new NewPlaylistDialog();
        if (await dialog.ShowDialog<bool>(this) == false || string.IsNullOrWhiteSpace(dialog.PlaylistName))
            return;

        if (paths.Count == 0) return;

        var playlistName = dialog.PlaylistName.Trim();
        try
        {
            using var db = AudioDatabase.OpenDefault();
            var playlistId = db.CreatePlaylist(playlistName);
            foreach (var path in paths)
                db.AddTrackToPlaylist(playlistId, path, db.GetTrackIdByPath(path));
        }
        catch { return; }

        LoadNavPlaylists();
        StatusTextBlock.Text = paths.Count == 1
            ? string.Format(LocalizationManager.Current.TrackAddedToPlaylist, playlistName)
            : string.Format(LocalizationManager.Current.TracksAddedToPlaylist, paths.Count, playlistName);
    }

    private List<string> GetPathsForRow(ContentRow row)
    {
        if (!string.IsNullOrEmpty(row.FilePath))
            return [row.FilePath];

        if (row.Id is not long albumId)
            return [];

        try
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetTrackListByAlbum(albumId).Select(t => t.Path).ToList();
        }
        catch { return []; }
    }

    // ------------------------------------------------------------------
    // Playlist – Löschen aus Sidebar / Entfernen aus Playlist-Ansicht
    // ------------------------------------------------------------------

    private MenuFlyout BuildDeleteRadioContextFlyout(RadioStationRecord radio)
    {
        var menu = CreateSidebarMenuFlyout();
        var header = CreateFlyoutMenuItem(
            radio.Name,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontWeight = FontWeight.SemiBold;
        menu.Items.Add(header);

        var separator = new Separator();
        menu.Items.Add(separator);

        var deleteItem = CreateFlyoutMenuItem(
            LocalizationManager.Current.DeleteRadio,
            new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
        deleteItem.Tag = radio;
        deleteItem.Click += DeleteRadioMenuItem_OnClick;
        menu.Items.Add(deleteItem);
        return menu;
    }

    private void DeleteRadioMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: RadioStationRecord radio })
            return;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.DeleteRadioStation(radio.Id);
        }
        catch
        {
            return;
        }

        LoadNavPlaylists();
        var internetRadioItem = NavListBox.Items.OfType<ListBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, "InternetRadio", StringComparison.Ordinal));
        NavListBox.SelectedItem = internetRadioItem;
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.RadioDeleted, radio.Name);
    }

    private MenuFlyout BuildDeletePodcastContextFlyout(PodcastRecord podcast)
    {
        var menu = CreateSidebarMenuFlyout();
        var header = CreateFlyoutMenuItem(
            podcast.Name,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontWeight = FontWeight.SemiBold;
        menu.Items.Add(header);

        var separator = new Separator();
        menu.Items.Add(separator);

        var deleteItem = CreateFlyoutMenuItem(
            LocalizationManager.Current.DeletePodcast,
            new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
        deleteItem.Tag = podcast;
        deleteItem.Click += DeletePodcastMenuItem_OnClick;
        menu.Items.Add(deleteItem);
        return menu;
    }

    private void DeletePodcastMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: PodcastRecord podcast })
            return;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.DeletePodcast(podcast.Id);
        }
        catch
        {
            return;
        }

        LoadNavPlaylists();
        var podcastsItem = NavListBox.Items.OfType<ListBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, "Podcasts", StringComparison.Ordinal));
        NavListBox.SelectedItem = podcastsItem;
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.PodcastDeleted, podcast.Name);
    }

    private MenuFlyout BuildPlaylistSidebarContextFlyout(PlaylistRecord playlist)
    {
        var menu = CreateSidebarMenuFlyout();
        var header = CreateFlyoutMenuItem(
            playlist.Name,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontWeight = FontWeight.SemiBold;
        menu.Items.Add(header);

        var sep = new Separator();
        menu.Items.Add(sep);

        if (playlist.IsSmartPlaylist)
        {
            var editItem = CreateFlyoutMenuItem(LocalizationManager.Current.EditSmartPlaylist);
            editItem.Tag = playlist.Id;
            editItem.Click += EditSmartPlaylistMenuItem_OnClick;
            menu.Items.Add(editItem);
        }
        else
        {
            var exportItem = CreateFlyoutMenuItem(LocalizationManager.Current.ExportM3u8Playlist);
            exportItem.Tag = playlist.Id;
            exportItem.Click += ExportM3u8PlaylistMenuItem_OnClick;
            menu.Items.Add(exportItem);
        }

        var deleteItem = CreateFlyoutMenuItem(
            LocalizationManager.Current.DeletePlaylist,
            new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
        deleteItem.Tag = playlist.Id;
        deleteItem.Click += DeletePlaylistMenuItem_OnClick;
        menu.Items.Add(deleteItem);

        return menu;
    }

    private MenuFlyout BuildPlaylistsHeaderContextFlyout()
    {
        var menu = CreateSidebarMenuFlyout();
        var importItem = CreateFlyoutMenuItem(LocalizationManager.Current.ImportM3u8Playlist);
        importItem.Click += ImportM3u8PlaylistMenuItem_OnClick;
        menu.Items.Add(importItem);
        return menu;
    }

    private MenuFlyout CreateSidebarMenuFlyout() => new()
    {
        FlyoutPresenterTheme = FindResource<ControlTheme>("AppMenuFlyoutPresenterTheme"),
        ItemContainerTheme = FindResource<ControlTheme>("AppMenuFlyoutItemTheme")
    };

    private MenuItem CreateFlyoutMenuItem(string header, IBrush? foreground = null)
    {
        foreground ??= FindResource<IBrush>("AppPrimaryTextBrush");
        return new MenuItem
        {
            Header = new TextBlock
            {
                Text = header,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center
            },
            Theme = FindResource<ControlTheme>("AppMenuFlyoutItemTheme"),
            Foreground = foreground
        };
    }

    private async void ImportM3u8PlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Current.ImportM3u8Playlist,
            FileTypeFilter =
            [
                new FilePickerFileType("M3U8") { Patterns = ["*.m3u8", "*.m3u"] }
            ],
            AllowMultiple = false
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { Length: > 0 } filePath)
            return;

        try
        {
            var result = await M3u8PlaylistService.ImportAsync(filePath);
            if (result.Entries.Count == 0)
            {
                StatusTextBlock.Text = LocalizationManager.Current.M3u8ImportNoEntries;
                return;
            }

            var name = Path.GetFileNameWithoutExtension(filePath).Trim();
            if (name.Length == 0)
                name = LocalizationManager.Current.NewPlaylist;
            using var db = AudioDatabase.OpenDefault();
            db.CreatePlaylist(name, result.Entries);
            LoadNavPlaylists();
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.M3u8ImportCompleted,
                name,
                result.Entries.Count,
                result.MissingLocalFiles,
                result.RemoteEntries,
                result.SkippedCredentialUrls + result.SkippedInvalidEntries);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.M3u8ImportFailed,
                ex.Message);
        }
    }

    private async void ExportM3u8PlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: long playlistId })
            return;

        PlaylistRecord? playlist;
        List<string> entries;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            playlist = db.GetPlaylistById(playlistId);
            if (playlist is null || playlist.IsSmartPlaylist)
                return;
            entries = db.GetPlaylistTracks(playlistId).Select(item => item.Path).ToList();
        }
        catch
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Current.ExportM3u8Playlist,
            FileTypeChoices = [new FilePickerFileType("M3U8") { Patterns = ["*.m3u8"] }],
            DefaultExtension = "m3u8",
            SuggestedFileName = SanitizeFileName(playlist.Name) + ".m3u8"
        });
        if (file?.TryGetLocalPath() is not { Length: > 0 } filePath)
            return;

        try
        {
            var result = await M3u8PlaylistService.ExportAsync(filePath, entries);
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.M3u8ExportCompleted,
                playlist.Name,
                result.ExportedEntries,
                result.SkippedCredentialUrls + result.SkippedInvalidEntries);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.M3u8ExportFailed,
                ex.Message);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return sanitized.Length == 0 ? "playlist" : sanitized;
    }

    private async void EditSmartPlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: long playlistId })
            return;

        PlaylistRecord? playlist;
        SmartPlaylistCriteria criteria;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            playlist = db.GetPlaylistById(playlistId);
            if (playlist is not { IsSmartPlaylist: true } ||
                string.IsNullOrWhiteSpace(playlist.FilterCriteria))
                return;
            criteria = JsonSerializer.Deserialize<SmartPlaylistCriteria>(playlist.FilterCriteria)
                       ?? new SmartPlaylistCriteria();
        }
        catch
        {
            return;
        }

        var dialog = new SmartPlaylistDialog(criteria, playlist.Name);
        if (await dialog.ShowDialog<bool>(this) == false ||
            string.IsNullOrWhiteSpace(dialog.PlaylistName) ||
            dialog.Criteria is null)
            return;

        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.UpdateSmartPlaylist(
                playlistId,
                dialog.PlaylistName.Trim(),
                JsonSerializer.Serialize(dialog.Criteria));
        }
        catch
        {
            return;
        }

        LoadNavPlaylists();
        if (_activePlaylistId == playlistId)
            await ShowTopLevelViewAsync($"Playlist:{playlistId}");
        StatusTextBlock.Text = string.Format(
            LocalizationManager.Current.SmartPlaylistUpdated,
            dialog.PlaylistName.Trim());
    }

    private void DeletePlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: long playlistId })
            return;

        string name;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            name = db.GetPlaylistById(playlistId)?.Name ?? string.Empty;
            db.DeletePlaylist(playlistId);
        }
        catch { return; }

        if (_activePlaylistId == playlistId)
        {
            _activePlaylistId = null;
            for (int i = 0; i < NavListBox.Items.Count; i++)
            {
                if (NavListBox.Items[i] is ListBoxItem { Tag: "Tracks" } tracksItem)
                {
                    NavListBox.SelectedItem = tracksItem;
                    break;
                }
            }
        }

        LoadNavPlaylists();
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.PlaylistDeleted, name);
    }

    private MenuFlyout BuildRemoveFromPlaylistContextFlyout(
        long playlistEntryId,
        string path)
    {
        var menu = CreateSidebarMenuFlyout();
        foreach (var item in CreateQueueMenuItems([path]))
            menu.Items.Add(item);
        menu.Items.Add(new Separator());
        var header = CreateFlyoutMenuItem(
            LocalizationManager.Current.RemoveFromPlaylist,
            new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF)));
        header.IsHitTestVisible = false;
        header.Focusable = false;
        header.FontSize = 11;
        header.FontWeight = FontWeight.SemiBold;
        menu.Items.Add(header);

        var sep = new Separator();
        menu.Items.Add(sep);

        var removeItem = CreateFlyoutMenuItem(LocalizationManager.Current.RemoveFromPlaylist);
        removeItem.Tag = new RemovePlaylistEntryTag(playlistEntryId);
        removeItem.Click += RemoveFromPlaylistMenuItem_OnClick;
        menu.Items.Add(removeItem);

        return menu;
    }

    private async void RemoveFromPlaylistMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: RemovePlaylistEntryTag tag })
            return;

        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.RemoveTrackFromPlaylist(tag.PlaylistEntryId);
        }
        catch { return; }

        StatusTextBlock.Text = LocalizationManager.Current.TrackRemovedFromPlaylist;

        if (NavListBox.SelectedItem is ListBoxItem { Tag: string navTag })
            await ShowTopLevelViewAsync(navTag);
    }

    // ------------------------------------------------------------------
    // Internetradio
    // ------------------------------------------------------------------

    private void LoadCatalogFilterCache()
    {
        _catalogFilterCacheData = _catalogFilterCache.Load();
        if (_catalogFilterCacheData.RadioGenres.Any(option => option.Count is not null))
            _catalogFilterCacheData.RadioGenresUpdatedAt = null;
        _radioGenreCatalog.Clear();
        _radioGenreCatalog.AddRange(_catalogFilterCacheData.RadioGenres
            .Select(option => new CatalogFilterOption(option.Value)));
        _podcastCategoryCatalog.Clear();
        _podcastCategoryCatalog.AddRange(_catalogFilterCacheData.PodcastCategories);
        _podcastLanguageCatalog.Clear();
        _podcastLanguageCatalog.AddRange(_catalogFilterCacheData.PodcastLanguages);
        if (_podcastCategoryCatalog.Any(option => string.IsNullOrWhiteSpace(option.Key)))
            _catalogFilterCacheData.PodcastCategoriesUpdatedAt = null;
        BuildRadioGenreFilter();
        BuildPodcastCategoryFilter();
        BuildPodcastLanguageFilter();
    }

    private async Task EnsureRadioFilterCatalogAsync()
    {
        BuildRadioGenreFilter();
        if (_radioFilterCatalogLoading ||
            CatalogFilterCache.IsFresh(_catalogFilterCacheData.RadioGenresUpdatedAt))
            return;

        _radioFilterCatalogLoading = true;
        try
        {
            var tags = await _radioBrowserService.GetTagsAsync();
            var options = tags
                .Select(tag => (Genre: NormalizeRadioGenre(tag.Name), tag.StationCount))
                .Where(item => item.Genre is not null)
                .GroupBy(item => item.Genre!, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CatalogFilterOption(group.Key))
                .OrderBy(option => option.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _radioGenreCatalog.Clear();
            _radioGenreCatalog.AddRange(options);
            _catalogFilterCacheData.RadioGenres = options;
            _catalogFilterCacheData.RadioGenresUpdatedAt = DateTimeOffset.UtcNow;
            _catalogFilterCache.Save(_catalogFilterCacheData);
            BuildRadioGenreFilter();
        }
        catch
        {
            // Existing cached filter data remains usable.
        }
        finally
        {
            _radioFilterCatalogLoading = false;
        }
    }

    private async void RadioSearchButton_OnClick(object? sender, RoutedEventArgs e) =>
        await SearchRadioStationsAsync();

    private async void RadioSearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        await SearchRadioStationsAsync();
    }

    private async Task SearchRadioStationsAsync()
    {
        CancelAndDispose(ref _radioSearchCts);
        _radioSearchCts = new CancellationTokenSource();
        var cancellationToken = _radioSearchCts.Token;

        RadioStationsDataGrid.ItemsSource = null;
        RadioStatusTextBlock.IsVisible = true;
        RadioStatusTextBlock.Text = LocalizationManager.Current.RadioLoading;
        try
        {
            var stations = await _radioBrowserService.SearchAsync(
                RadioSearchTextBox.Text,
                _selectedRadioGenres,
                cancellationToken);
            var rows = stations.Select(station => new RadioStationViewModel
            {
                StationUuid = station.StationUuid,
                Name = station.Name,
                StreamUrl = station.StreamUrl,
                Homepage = station.Homepage,
                Favicon = station.Favicon,
                CountryCode = station.CountryCode,
                Codec = station.Codec,
                Bitrate = station.Bitrate,
                Tags = station.Tags,
                Genres = NormalizeRadioGenres(station.Tags)
            }).ToList();
            _radioSearchResults.Clear();
            _radioSearchResults.AddRange(rows);
            BuildRadioGenreFilter();
            ApplyRadioGenreFilter();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            RadioStatusTextBlock.Text = LocalizationManager.Current.RadioSearchFailed;
        }
    }

    private void RadioGenreFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        BuildRadioGenreFilter();
        RadioGenreFilterPopup.IsOpen = !RadioGenreFilterPopup.IsOpen;
    }

    private async void ClearRadioGenreFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _selectedRadioGenres.Clear();
        BuildRadioGenreFilter();
        await SearchRadioStationsAsync();
    }

    private void BuildRadioGenreFilter()
    {
        var useCatalog = string.IsNullOrWhiteSpace(RadioSearchTextBox.Text);
        var options = useCatalog && _radioGenreCatalog.Count > 0
            ? _radioGenreCatalog
            : _radioSearchResults
                .SelectMany(row => row.Genres)
                .GroupBy(genre => genre, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CatalogFilterOption(group.Key, group.Count()))
                .OrderByDescending(option => option.Count)
                .ThenBy(option => option.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        RadioGenreFilterPanel.Children.Clear();
        foreach (var option in options)
        {
            var checkBox = new CheckBox
            {
                Content = option.Count is > 0
                    ? $"{option.Value} ({option.Count:N0})"
                    : option.Value,
                Tag = option.Value,
                IsChecked = _selectedRadioGenres.Contains(option.Value),
                Margin = new Thickness(2, 4, 2, 4),
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush")
            };
            checkBox.IsCheckedChanged += RadioGenreCheckBox_OnChanged;
            checkBox.IsCheckedChanged += RadioGenreCheckBox_OnChanged;
            RadioGenreFilterPanel.Children.Add(checkBox);
        }

        UpdateRadioGenreFilterButton();
    }

    private async void RadioGenreCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string genre } checkBox)
            return;
        if (checkBox.IsChecked == true)
            _selectedRadioGenres.Add(genre);
        else
            _selectedRadioGenres.Remove(genre);
        UpdateRadioGenreFilterButton();
        await SearchRadioStationsAsync();
    }

    private void ApplyRadioGenreFilter()
    {
        var rows = _radioSearchResults;
        RadioStationsDataGrid.ItemsSource = rows;
        RadioStatusTextBlock.Text = rows.Count == 0
            ? LocalizationManager.Current.RadioNoResults
            : string.Empty;
        RadioStatusTextBlock.IsVisible = rows.Count == 0;
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
    }

    private void UpdateRadioGenreFilterButton()
    {
        RadioGenreFilterButton.Content = _selectedRadioGenres.Count == 0
            ? LocalizationManager.Current.RadioGenres
            : $"{LocalizationManager.Current.RadioGenres} ({_selectedRadioGenres.Count})";
    }

    private static IReadOnlyList<string> NormalizeRadioGenres(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return [];

        return tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeRadioGenre)
            .Where(genre => genre is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(genre => genre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeRadioGenre(string tag)
    {
        var value = tag.Trim().ToLowerInvariant();
        if (value.Length < 2 ||
            value is "aac" or "mp3" or "ogg" or "fm" or "am" or "radio" or "music" or "otr")
            return null;

        if (value.Contains("news", StringComparison.Ordinal) || value == "information") return "News";
        if (value.Contains("talk", StringComparison.Ordinal)) return "Talk";
        if (value.Contains("public radio", StringComparison.Ordinal)) return "Public Radio";
        if (value.Contains("classical", StringComparison.Ordinal)) return "Classical";
        if (value.Contains("jazz", StringComparison.Ordinal)) return "Jazz";
        if (value.Contains("easy listening", StringComparison.Ordinal)) return "Easy Listening";
        if (value.Contains("adult contemporary", StringComparison.Ordinal)) return "Adult Contemporary";
        if (value.Contains("top 40", StringComparison.Ordinal) || value == "hits") return "Hits";
        if (value.Contains("oldies", StringComparison.Ordinal)) return "Oldies";
        if (value.Contains("80s", StringComparison.Ordinal)) return "80s";
        if (value.Contains("90s", StringComparison.Ordinal)) return "90s";
        if (value.Contains("70s", StringComparison.Ordinal)) return "70s";
        if (value.Contains("hip hop", StringComparison.Ordinal) || value.Contains("hip-hop", StringComparison.Ordinal)) return "Hip-Hop";
        if (value.Contains("r&b", StringComparison.Ordinal) || value.Contains("soul", StringComparison.Ordinal)) return "R&B / Soul";
        if (value.Contains("electronic", StringComparison.Ordinal) || value is "edm" or "techno" or "house" or "trance") return "Electronic";
        if (value.Contains("dance", StringComparison.Ordinal)) return "Dance";
        if (value.Contains("alternative", StringComparison.Ordinal)) return "Alternative";
        if (value.Contains("indie", StringComparison.Ordinal)) return "Indie";
        if (value.Contains("metal", StringComparison.Ordinal)) return "Metal";
        if (value.Contains("rock", StringComparison.Ordinal)) return "Rock";
        if (value.Contains("pop", StringComparison.Ordinal)) return "Pop";
        if (value.Contains("blues", StringComparison.Ordinal)) return "Blues";
        if (value.Contains("country", StringComparison.Ordinal)) return "Country";
        if (value.Contains("reggae", StringComparison.Ordinal)) return "Reggae";
        if (value.Contains("folk", StringComparison.Ordinal)) return "Folk";
        if (value.Contains("latin", StringComparison.Ordinal)) return "Latin";
        if (value.Contains("world", StringComparison.Ordinal)) return "World";
        if (value.Contains("culture", StringComparison.Ordinal)) return "Culture";
        if (value.Contains("comedy", StringComparison.Ordinal)) return "Comedy";
        if (value.Contains("sport", StringComparison.Ordinal)) return "Sports";
        return null;
    }

    private async void RadioStationsDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null ||
            RadioStationsDataGrid.SelectedItem is not RadioStationViewModel station)
            return;
        await PlayRadioAsync(station.ToRecord());
    }

    private async void PlayRadioButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RadioStationViewModel station })
            await PlayRadioAsync(station.ToRecord());
    }

    private void AddRadioButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RadioStationViewModel station })
            return;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.SaveRadioStation(station.ToBrowserStation());
            LoadNavPlaylists();
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.RadioAdded, station.Name);
        }
        catch (Exception)
        {
            StatusTextBlock.Text = LocalizationManager.Current.RadioSearchFailed;
        }
    }

    private async Task PlaySavedRadioAsync(long radioId)
    {
        RadioStationRecord? station;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            station = db.GetRadioStation(radioId);
        }
        catch
        {
            station = null;
        }

        if (station is not null)
            await PlayRadioAsync(station);
    }

    private async Task PlayRadioAsync(RadioStationRecord station)
    {
        RefreshQueueNavigationButtons();
        _ = _radioBrowserService.RegisterClickAsync(station.StationUuid);
        try
        {
            await StartPlaybackAsync(station.StreamUrl, station);
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

    private void ShowRadioNowPlaying(RadioStationRecord station, AudioFileInfo info)
    {
        RadioNowPlayingPanel.IsVisible = true;
        RadioNowPlayingStation.Text = station.Name;
        RadioNowPlayingTitle.Text = LocalizationManager.Current.RadioMetadataUnavailable;
        RadioNowPlayingArtist.Text = string.Empty;
        RadioNowPlayingDescription.Text = BuildRadioDescription(station, info, null);
        RadioNowPlayingLogo.Source = null;
        NowPlayingArtworkImage.Source = null;
        _ = LoadRadioLogoAsync(station.Favicon, _playbackCts?.Token ?? CancellationToken.None);
    }

    private void StartRadioMetadataMonitor(RadioStationRecord station)
    {
        CancelAndDispose(ref _radioMetadataCts);
        _radioMetadataCts = CancellationTokenSource.CreateLinkedTokenSource(
            _playbackCts?.Token ?? CancellationToken.None);
        _ = MonitorRadioMetadataAsync(station, _radioMetadataCts.Token);
    }

    private async Task MonitorRadioMetadataAsync(
        RadioStationRecord station,
        CancellationToken cancellationToken)
    {
        string? previousSignature = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var metadata = await _radioMetadataService.ProbeAsync(
                    station.StreamUrl,
                    cancellationToken);
                var signature = metadata is null
                    ? string.Empty
                    : $"{metadata.StreamTitle}\n{metadata.Description}\n{metadata.Genre}";
                if (!string.Equals(signature, previousSignature, StringComparison.Ordinal))
                {
                    previousSignature = signature;
                    await Dispatcher.UIThread.InvokeAsync(() => UpdateRadioMetadata(station, metadata));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (previousSignature is null)
                {
                    previousSignature = string.Empty;
                    await Dispatcher.UIThread.InvokeAsync(() => UpdateRadioMetadata(station, null));
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void UpdateRadioMetadata(RadioStationRecord station, RadioStreamMetadata? metadata)
    {
        if (_currentRadioStation?.StationUuid != station.StationUuid)
            return;

        var title = metadata?.Title;
        var artist = metadata?.Artist;
        if (string.IsNullOrWhiteSpace(title))
        {
            RadioNowPlayingTitle.Text = LocalizationManager.Current.RadioMetadataUnavailable;
            RadioNowPlayingArtist.Text = string.Empty;
            NowPlayingTitleBlock.Text = station.Name;
            NowPlayingArtistBlock.Text = LocalizationManager.Current.InternetRadio;
        }
        else
        {
            RadioNowPlayingTitle.Text = title;
            RadioNowPlayingArtist.Text = artist ?? metadata!.StationName ?? station.Name;
            NowPlayingTitleBlock.Text = title;
            NowPlayingArtistBlock.Text = artist ?? station.Name;
        }

        RadioNowPlayingDescription.Text = BuildRadioDescription(
            station,
            null,
            metadata);
        RefreshWindowsMediaMetadata();
    }

    private static string BuildRadioDescription(
        RadioStationRecord station,
        AudioFileInfo? info,
        RadioStreamMetadata? metadata)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(metadata?.Description))
            parts.Add(metadata.Description);
        if (!string.IsNullOrWhiteSpace(metadata?.Genre))
            parts.Add(metadata.Genre);
        if (!string.IsNullOrWhiteSpace(station.CountryCode))
            parts.Add(station.CountryCode);
        if (!string.IsNullOrWhiteSpace(station.Codec))
            parts.Add(station.Bitrate > 0
                ? $"{station.Codec} · {station.Bitrate} kbps"
                : station.Codec);
        else if (info is not null)
            parts.Add($"{info.CodecName.ToUpperInvariant()} · {info.SourceSampleRate:N0} Hz");
        return string.Join("  ·  ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task LoadRadioLogoAsync(string? favicon, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(favicon, UriKind.Absolute, out var uri))
            return;
        try
        {
            var bytes = await RadioImageHttpClient.GetByteArrayAsync(uri, cancellationToken);
            await using var stream = new MemoryStream(bytes);
            var image = new Bitmap(stream);
            if (!cancellationToken.IsCancellationRequested)
            {
                RadioNowPlayingLogo.Source = image;
                NowPlayingArtworkImage.Source = image;
            }
        }
        catch
        {
            // A missing or invalid station logo does not affect playback.
        }
    }

    private void ClearRadioNowPlaying()
    {
        RadioNowPlayingPanel.IsVisible = false;
        RadioNowPlayingLogo.Source = null;
        RadioNowPlayingStation.Text = string.Empty;
        RadioNowPlayingTitle.Text = string.Empty;
        RadioNowPlayingArtist.Text = string.Empty;
        RadioNowPlayingDescription.Text = string.Empty;
    }

    private static HttpClient CreateRadioImageHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0");
        return client;
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
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush")
            };
            checkBox.IsCheckedChanged += changedHandler;
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
            PodcastsDataGrid.SelectedItem is not PodcastViewModel podcast)
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
            PodcastEpisodesDataGrid.SelectedItem is not PodcastEpisodeViewModel row)
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
        StopPlayback();
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
        _transportTimer.Start();

        // Now-playing anzeigen
        var filename = podcastPlayback?.Episode.Title ??
                       radioStation?.Name ??
                       Path.GetFileNameWithoutExtension(filePath);
        NowPlayingTitleBlock.Text  = filename;
        NowPlayingArtistBlock.Text = podcastPlayback?.Podcast.Name ??
                                     (radioStation is null
                                         ? SelectedDriverTextBlock.Text
                                         : LocalizationManager.Current.InternetRadio);
        var usesNativeDsd = info.IsDsd &&
                            !_settings.AlwaysConvertDsdToPcm &&
                            _settings.OutputBackend is OutputBackend.Asio or OutputBackend.CwAsio;
        FileInfoTextBlock.Text = usesNativeDsd
            ? $"{info.ContainerName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  {LocalizationManager.Current.NativeDsdOutput}"
            : info.IsDsd
                ? $"{info.ContainerName.ToUpperInvariant()}  ·  {LocalizationManager.Current.DsdToPcmOutput}  ·  {info.OutputSampleRate:N0} Hz"
                : info.SourceSampleRate != info.OutputSampleRate
                    ? $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz → {info.OutputSampleRate:N0} Hz  ·  {info.Channels} ch"
                    : $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  {info.Channels} ch";
        if (radioStation is null && podcastPlayback is null)
        {
            var isPlexTrack = _plexTracksByUrl.TryGetValue(filePath, out var plexTrack);
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var artworkPaths = db.GetArtworkPathsByTrackPath(filePath);
                var track = db.GetByPath(filePath);
                var artist = db.GetArtistByTrackPath(filePath);
                NowPlayingArtworkImage.Source = CreateArtworkImage(artworkPaths?.Thumb96Path, 96);
                LyricsBackgroundImage.Source = CreateArtworkImage(
                    artworkPaths?.Thumb320Path ?? artworkPaths?.OriginalPath,
                    900);
                var trackInfo = db.GetTrackIdAndFavorite(filePath);
                _currentTrackId = trackInfo?.Id;
                _currentTrackIsFavorite = trackInfo?.IsFavorite ?? false;
                _currentArtistId = artist?.Id;
                _currentArtistName = artist?.Artist;
                NowPlayingArtistButton.IsEnabled = artist is not null;
                LyricsButton.IsEnabled = track is not null;
                ArtistInfoButton.IsEnabled = artist is not null;
                ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
                if (track is not null)
                {
                    NowPlayingTitleBlock.Text = track.Title ?? filename;
                    NowPlayingArtistBlock.Text = track.Artist ?? string.Empty;
                }
            }
            catch
            {
                NowPlayingArtworkImage.Source = null;
                LyricsBackgroundImage.Source = null;
                _currentTrackId = null;
                _currentArtistId = null;
                _currentArtistName = null;
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
                _currentTrackIsFavorite = false;
                NowPlayingArtistButton.IsEnabled = false;
                LyricsButton.IsEnabled = false;
                ArtistInfoButton.IsEnabled = false;
            }
            else
            {
                _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
            }
        }
        else if (podcastPlayback is not null)
        {
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
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
            NowPlayingArtistButton.IsEnabled = false;
            _currentTrackIsFavorite = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
            ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
            StartRadioMetadataMonitor(radioStation);
        }
        UpdateNowPlayingFavoriteButton();
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
                    externalId: podcastPlayback.Episode.EpisodeKey);
            }
            else
            {
                _currentPlayHistoryId = db.RecordPlaybackStart(
                    filePath,
                    db.GetTrackIdByPath(filePath),
                    player.Duration.TotalSeconds > 0 ? player.Duration.TotalSeconds : null,
                    title: NowPlayingTitleBlock.Text,
                    subtitle: NowPlayingArtistBlock.Text);
            }
        }
        catch
        {
            _currentPlayHistoryId = null;
        }

        await player.WaitForCompletionAsync();

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

    private GaplessPlaybackItem ResolveGaplessPlaybackItem(string path)
    {
        if (_plexTracksByUrl.TryGetValue(path, out var plexTrack))
        {
            return new GaplessPlaybackItem(
                path,
                1.0f,
                SourcePaths: plexTrack.PlexPartUrls,
                KnownDuration: plexTrack.KnownDuration);
        }

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
        var filename = Path.GetFileNameWithoutExtension(filePath);
        NowPlayingTitleBlock.Text = filename;
        NowPlayingArtistBlock.Text = SelectedDriverTextBlock.Text;
        FileInfoTextBlock.Text = info.IsDsd
            ? $"{info.ContainerName.ToUpperInvariant()}  ·  {LocalizationManager.Current.DsdToPcmOutput}  ·  {info.OutputSampleRate:N0} Hz"
            : info.SourceSampleRate != info.OutputSampleRate
                ? $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz → {info.OutputSampleRate:N0} Hz  ·  {info.Channels} ch"
                : $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  {info.Channels} ch";

        var isPlexTrack = _plexTracksByUrl.TryGetValue(filePath, out var plexTrack);
        try
        {
            using var db = AudioDatabase.OpenDefault();
            var artworkPaths = db.GetArtworkPathsByTrackPath(filePath);
            var track = db.GetByPath(filePath);
            var artist = db.GetArtistByTrackPath(filePath);
            NowPlayingArtworkImage.Source = CreateArtworkImage(artworkPaths?.Thumb96Path, 96);
            LyricsBackgroundImage.Source = CreateArtworkImage(
                artworkPaths?.Thumb320Path ?? artworkPaths?.OriginalPath,
                900);
            var trackInfo = db.GetTrackIdAndFavorite(filePath);
            _currentTrackId = trackInfo?.Id;
            _currentTrackIsFavorite = trackInfo?.IsFavorite ?? false;
            _currentArtistId = artist?.Id;
            _currentArtistName = artist?.Artist;
            NowPlayingArtistButton.IsEnabled = artist is not null;
            LyricsButton.IsEnabled = track is not null;
            ArtistInfoButton.IsEnabled = artist is not null;
            if (track is not null)
            {
                NowPlayingTitleBlock.Text = track.Title ?? filename;
                NowPlayingArtistBlock.Text = track.Artist ?? string.Empty;
            }
        }
        catch
        {
            NowPlayingArtworkImage.Source = null;
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
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
            _currentTrackIsFavorite = false;
            NowPlayingArtistButton.IsEnabled = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
        }
        else
        {
            _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
        }

        UpdateNowPlayingFavoriteButton();
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
            if (Uri.TryCreate(radioStation.Favicon, UriKind.Absolute, out var radioArtworkUri))
                artworkUri = radioArtworkUri;
        }
        else if (_plexTracksByUrl.TryGetValue(_currentFilePath, out var plexTrack))
        {
            album = plexTrack.Album ?? string.Empty;
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
                subtitle: NowPlayingArtistBlock.Text);
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
        PositionSlider.IsEnabled = false;
        _transportTimer.Stop();
        CancelLyricsLoad();
        CancelArtistProfileLoad();
        ClearLyrics();

        NowPlayingTitleBlock.Text  = "";
        NowPlayingArtistBlock.Text = "";
        FileInfoTextBlock.Text     = "";
        NowPlayingArtworkImage.Source = null;
        LyricsBackgroundImage.Source = null;
        _currentTrackId = null;
        _currentArtistId = null;
        _currentArtistName = null;
        _currentTrackIsFavorite = false;
        _currentRadioStation = null;
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

    private async void PositionSlider_OnPreviewMouseLeftButtonUp(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isSeekingWithSlider)
            return;
        try
        {
            if (_player is not null && _player.CanSeek)
                await _player.SeekAsync(TimeSpan.FromSeconds(PositionSlider.Value));
        }
        finally
        {
            _isSeekingWithSlider = false;
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

    private void PositionSlider_OnPreviewMouseLeftButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (_player?.CanSeek != true || PositionSlider.Bounds.Width <= 0)
            return;

        _isSeekingWithSlider = true;
        var position = e.GetPosition(PositionSlider);
        var ratio = Math.Clamp(position.X / PositionSlider.Bounds.Width, 0, 1);
        PositionSlider.Value =
            PositionSlider.Minimum +
            ratio * (PositionSlider.Maximum - PositionSlider.Minimum);
    }

    private void PositionSlider_OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isSeekingWithSlider)
            CurrentTimeTextBlock.Text = FormatTime(TimeSpan.FromSeconds(PositionSlider.Value));
    }

    private void RefreshTransport()
    {
        if (_player is null) return;
        CurrentTimeTextBlock.Text = FormatTime(_player.Position);
        DurationTextBlock.Text    = FormatTime(_player.Duration);
        PositionSlider.Maximum    = Math.Max(1, _player.Duration.TotalSeconds);
        if (!_isSeekingWithSlider)
            PositionSlider.Value = Math.Min(PositionSlider.Maximum, _player.Position.TotalSeconds);
        _windowsMediaTransport?.UpdateTimeline(_player.Position, _player.Duration);
        if (_currentPodcastPlayback is not null &&
            DateTimeOffset.UtcNow - _lastPodcastProgressSave >= TimeSpan.FromSeconds(5))
        {
            SavePodcastProgress(completed: false);
        }
        UpdateActiveLyric(_player.Position);
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
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        TrackRecord? track;
        using (var db = AudioDatabase.OpenDefault())
            track = db.GetByPath(_currentFilePath);
        if (track is null)
            return;

        var dialog = new LyricsSearchWindow(track.Title, track.Artist) ;
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        using (var db = AudioDatabase.OpenDefault())
        {
            db.UpdateDownloadedLyrics(
                _currentFilePath,
                selected.PlainLyrics,
                selected.SyncedLyrics,
                "LRCLIB manual");
        }

        track.DownloadedLyrics = selected.PlainLyrics;
        track.SyncedLyrics = selected.SyncedLyrics;
        track.LyricsSource = "LRCLIB manual";
        track.LyricsFetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ApplyLyrics(track);
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
        if (sender is Button { Tag: ContentRow row } && row.Id is long artistId)
        {
            e.Handled = true;
            LyricsView.IsVisible = false;
            PodcastInfoView.IsVisible = false;
            ArtistInfoView.IsVisible = true;
            UpdateBackButtonForDetailView();
            await ShowArtistInfoAsync(artistId, forceRefresh: false);
        }
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
        if (_artistInfoDisplayedId is long artistId)
            await ShowArtistInfoAsync(artistId, forceRefresh: true);
    }

    private async void SearchArtistImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
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

    private async void EditArtistNameButton_OnClick(object? sender, RoutedEventArgs e)
    {
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
        _ = RebuildSearchIndexAfterArtistRenameAsync();
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

    private async Task ShowArtistInfoAsync(long artistId, bool forceRefresh)
    {
        _artistInfoDisplayedId = artistId;
        CancelArtistProfileLoad();
        var cts = new CancellationTokenSource();
        _artistProfileCts = cts;
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

        var backgroundToken = _backgroundArtistLoadCts.Token;
        try
        {
            var profile = await ArtistProfileService.DownloadAsync(
                artistId,
                row.Title ?? string.Empty,
                language,
                downloadImage: !row.ImageIsManual,
                cancellationToken: backgroundToken);
            using var db = AudioDatabase.OpenDefault();
            db.UpdateArtistProfile(
                artistId,
                profile?.Biography,
                profile?.ImagePath,
                profile?.SourceUrl,
                language);
            row.Biography = profile?.Biography;
            row.SourceUrl = profile?.SourceUrl;
            row.ProfileLanguage = language;
            row.ProfileFetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!string.IsNullOrWhiteSpace(profile?.ImagePath))
            {
                row.ArtworkPath = profile.ImagePath;
                row.ThumbnailPath = profile.ImagePath;
                row.ArtworkLoadCompleted = false;
                row.Artwork = await Task.Run(() => CreateArtworkImage(profile.ImagePath, 320, ignoreCache: true));
                row.Thumbnail = await Task.Run(() => CreateArtworkImage(profile.ImagePath, 96, ignoreCache: true));
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
            TrackRecord? track;
            using (var db = AudioDatabase.OpenDefault())
                track = db.GetByPath(filePath);

            if (track is null)
            {
                ClearLyrics();
                ShowLyricsStatus(LocalizationManager.Current.LyricsUnavailable);
                return;
            }

            var hasLocalLyrics = ApplyLyrics(track);
            var fetchedAt = track.LyricsFetchedAt is long timestamp
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                : (DateTimeOffset?)null;
            var lookupExpired = fetchedAt is null ||
                fetchedAt < DateTimeOffset.UtcNow.AddDays(-30);
            var shouldDownload = forceRefresh ||
                (string.IsNullOrWhiteSpace(track.SyncedLyrics) && lookupExpired);
            if (!shouldDownload)
            {
                if (!hasLocalLyrics)
                    ShowLyricsStatus(LocalizationManager.Current.LyricsNotFound);
                return;
            }

            if (!hasLocalLyrics)
                ShowLyricsStatus(LocalizationManager.Current.LyricsDownloading);

            var result = await LyricsService.DownloadAsync(track, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                return;

            using (var db = AudioDatabase.OpenDefault())
            {
                db.UpdateDownloadedLyrics(
                    filePath,
                    result?.PlainLyrics,
                    result?.SyncedLyrics,
                    "LRCLIB");
            }

            track.DownloadedLyrics = result?.PlainLyrics;
            track.SyncedLyrics = result?.SyncedLyrics;
            track.LyricsSource = "LRCLIB";
            track.LyricsFetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!ApplyLyrics(track))
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
    {
        ClearLyrics();
        var timedLines = LyricsService.ParseLrc(track.SyncedLyrics);
        if (timedLines.Count > 0)
        {
            foreach (var line in timedLines)
                _lyricLines.Add(new LyricLineViewModel(line.Text, line.Time));
        }
        else
        {
            var plainLyrics = track.DownloadedLyrics ?? track.Lyrics;
            if (!string.IsNullOrWhiteSpace(plainLyrics))
            {
                foreach (var line in plainLyrics.Replace("\r\n", "\n").Split('\n'))
                    _lyricLines.Add(new LyricLineViewModel(line.Trim(), null));
            }
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

    private static async Task RebuildSearchIndexAfterArtistRenameAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                TrackSearchIndex.Rebuild(db.GetAll().ToList());
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Artist rename search-index rebuild");
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
        if (_settings.ReplayGainMode == ReplayGainMode.Off ||
            _player is DsfAudioPlayer or DffAudioPlayer)
            return 1.0f;

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(filePath);
            return track is null
                ? 1.0f
                : ReplayGain.GetLinearFactor(
                    _settings.ReplayGainMode,
                    track.ReplayGainTrack,
                    track.ReplayGainAlbum);
        }
        catch
        {
            return 1.0f;
        }
    }

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
                _settings.ReplayGainMode != window.SelectedReplayGainMode;
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
                _settings.ShowPlexSection != window.ShowPlexSection ||
                _settings.ShowPlaylistsSection != window.ShowPlaylistsSection;
            var mcpChanged =
                _settings.McpServerEnabled != window.McpServerEnabled ||
                _settings.McpServerPort    != window.McpServerPort;
            var plexServersChanged = !PlexServerSettingsEqual(
                _settings.PlexServers,
                window.SelectedPlexServers);
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
            _settings.EqualizerEnabled      = window.EqualizerEnabled;
            _settings.EqualizerProfile      = window.SelectedEqualizerProfile;
            _settings.EqualizerProfiles     = window.SelectedEqualizerProfiles.ToList();
            _settings.SelectedEqualizerProfileName = window.SelectedEqualizerProfileName;
            _settings.LibraryPaths           = window.SelectedLibraryPaths.ToList();
            _libraryWatcher?.UpdatePaths(_settings.LibraryPaths);
            _settings.Theme                  = window.SelectedTheme;
            _settings.Language               = window.SelectedLanguage;
            _settings.ArtistInfoSource       = window.SelectedArtistInfoSource;
            _settings.LastFmApiKey           = window.SelectedLastFmApiKey;
            _settings.QobuzApplicationId      = window.SelectedQobuzApplicationId;
            _settings.PlexServers             = window.SelectedPlexServers.ToList();
            _settings.McpServerEnabled        = window.McpServerEnabled;
            _settings.McpServerPort           = window.McpServerPort;
            _settings.DisabledMcpTools        = window.DisabledMcpTools;
            _mcpBridge.DisabledTools          = _settings.DisabledMcpTools;
            _settings.ShowInternetRadioItem   = window.ShowInternetRadioItem;
            _settings.ShowPodcastsItem        = window.ShowPodcastsItem;
            _settings.ShowQueueItem           = window.ShowQueueItem;
            _settings.ShowLocalLibrarySection = window.ShowLocalLibrarySection;
            _settings.ShowOwnRadiosSection    = window.ShowOwnRadiosSection;
            _settings.ShowMyPodcastsSection   = window.ShowMyPodcastsSection;
            _settings.ShowPlexSection         = window.ShowPlexSection;
            _settings.ShowPlaylistsSection    = window.ShowPlaylistsSection;
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
            else if (sidebarChanged)
                ApplySidebarNavigationSettings();
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
    {
        if (_settings.ReplayGainMode == ReplayGainMode.Off ||
            _player is DsfAudioPlayer or DffAudioPlayer)
        {
            return 1.0f;
        }

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(filePath);
            return track is null
                ? 1.0f
                : ReplayGain.GetLinearFactor(
                    _settings.ReplayGainMode,
                    track.ReplayGainTrack,
                    track.ReplayGainAlbum);
        }
        catch
        {
            return 1.0f;
        }
    }

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

    // ------------------------------------------------------------------
    // Dashboard
    // ------------------------------------------------------------------

    private async Task ShowDashboardAsync()
    {
        ContentTitleTextBlock.Text = LocalizationManager.Current.Dashboard;
        var now = DateTime.Now;
        if (_dashboardYear == 0)
        {
            _dashboardYear  = now.Year;
            _dashboardMonth = now.Month;
        }
        await BuildDashboardAsync();
    }

    private async Task BuildDashboardAsync()
    {
        DashboardPanel.Children.Clear();
        _calendarInner = null;

        var recentAlbums = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetRecentAlbums(12);
        });

        var calendarData = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetCalendarData(_dashboardYear, _dashboardMonth);
        });

        var topGenres = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetTopGenres(10);
        });

        DashboardAddSectionHeader(LocalizationManager.Current.RecentAlbums);
        DashboardBuildRecentAlbums(recentAlbums);

        DashboardAddSectionHeader(string.Format(
                LocalizationManager.Current.Calendar,
                new DateTime(_dashboardYear, _dashboardMonth, 1).ToString("MMMM yyyy")),
            calendarNav: true);
        DashboardBuildCalendar(calendarData);

        DashboardAddSectionHeader(LocalizationManager.Current.TopGenres);
        DashboardBuildTopGenres(topGenres);
    }

    private void DashboardAddSectionHeader(string title, bool calendarNav = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 24, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (calendarNav)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var tb = new TextBlock
        {
            Text       = title,
            FontSize   = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, 0);
        grid.Children.Add(tb);

        if (calendarNav)
        {
            var prev = CreateCalNavButton("◀");
            var next = CreateCalNavButton("▶");
            prev.Click += CalendarPrev_OnClick;
            next.Click += CalendarNext_OnClick;
            Grid.SetColumn(prev, 2);
            Grid.SetColumn(next, 3);
            grid.Children.Add(prev);
            grid.Children.Add(next);
        }

        DashboardPanel.Children.Add(grid);

        var sep = new Border
        {
            Height     = 1,
            Background = FindResource<IBrush>("AppGridLineBrush"),
            Margin     = new Thickness(0, 0, 0, 12)
        };
        DashboardPanel.Children.Add(sep);
    }

    private Button CreateCalNavButton(string symbol)
    {
        return new Button
        {
            Content    = symbol,
            FontSize   = 13,
            Padding    = new Thickness(8, 3, 8, 3),
            Margin     = new Thickness(4, 0, 0, 0),
            Background = FindResource<IBrush>("AppContentBrush"),
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            BorderBrush = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            Cursor     = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void DashboardBuildRecentAlbums(List<RecentAlbumInfo> albums)
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        foreach (var album in albums)
            panel.Children.Add(BuildAlbumCard(album));

        scroll.Content = panel;
        DashboardPanel.Children.Add(scroll);
    }

    private Control BuildAlbumCard(RecentAlbumInfo album)
    {
        var card = new Border
        {
            Width           = 140,
            Margin          = new Thickness(0, 0, 12, 0),
            Background      = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush     = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Cursor          = new Cursor(StandardCursorType.Hand),
            ClipToBounds    = true
        };

        var stack = new StackPanel();

        var img = new Image
        {
            Width   = 140,
            Height  = 140,
            Stretch = Stretch.UniformToFill
        };

        if (!string.IsNullOrEmpty(album.ThumbPath) && File.Exists(album.ThumbPath))
        {
            try
            {
                using var bmpStream = File.OpenRead(album.ThumbPath);
                img.Source = new Bitmap(bmpStream);
            }
            catch { img.Source = null; }
        }

        var placeholder = new Border
        {
            Width  = 140,
            Height = 140,
            Background = FindResource<IBrush>("AppArtworkPlaceholderBrush")
        };

        if (img.Source is null)
            stack.Children.Add(placeholder);
        else
            stack.Children.Add(img);

        var albumButton = new Button
        {
            Content     = album.Title,
            FontWeight  = FontWeight.SemiBold,
            FontSize    = 11,
            Foreground  = FindResource<IBrush>("AppPrimaryTextBrush"),
            Margin      = new Thickness(8, 6, 8, 2),
            Theme = FindResource<ControlTheme>("EntityLinkButtonTheme")
        };
        albumButton.Click += (_, e) =>
        {
            e.Handled = true;
            _ = ShowAlbumTracksAsync(album.Id, album.Title);
        };
        stack.Children.Add(albumButton);

        var artistButton = new Button
        {
            Content    = album.Artist,
            FontSize   = 10,
            Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
            Margin     = new Thickness(8, 0, 8, 8),
            Theme = FindResource<ControlTheme>("EntityLinkButtonTheme")
        };
        artistButton.Click += async (_, e) =>
        {
            e.Handled = true;
            using var db = AudioDatabase.OpenDefault();
            if (db.GetAlbumArtistId(album.Id) is long artistId)
            {
                await ShowArtistAlbumsAsync(artistId, album.Artist);
            }
        };
        stack.Children.Add(artistButton);

        card.Child = stack;

        card.PointerReleased += (_, _) =>
        {
            _ = ShowAlbumTracksAsync(album.Id, album.Title);
        };

        return card;
    }

    private void DashboardBuildCalendar(List<CalendarDayData> data)
    {
        var dayMap = data.ToDictionary(d => d.Day);
        int daysInMonth = DateTime.DaysInMonth(_dashboardYear, _dashboardMonth);
        var firstDay = new DateTime(_dashboardYear, _dashboardMonth, 1);
        int startDow = ((int)firstDay.DayOfWeek + 6) % 7; // Monday=0

        var outer = new Border
        {
            Background      = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush     = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(12),
            Margin          = new Thickness(0, 0, 0, 4)
        };

        var inner = new StackPanel();
        _calendarInner = inner;

        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        for (int i = 0; i < 7; i++)
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string[] dayNames = ["Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"];
        for (int i = 0; i < 7; i++)
        {
            var tb = new TextBlock
            {
                Text      = dayNames[i],
                FontSize  = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin    = new Thickness(0, 0, 0, 4)
            };
            Grid.SetColumn(tb, i);
            headerRow.Children.Add(tb);
        }
        inner.Children.Add(headerRow);

        DashboardRefreshCalendarContent(inner, dayMap, daysInMonth, startDow);

        outer.Child = inner;
        DashboardPanel.Children.Add(outer);
    }

    private void DashboardRefreshCalendarContent(StackPanel inner, Dictionary<int, CalendarDayData> dayMap,
        int daysInMonth, int startDow)
    {
        // Remove all rows except the header (index 0)
        while (inner.Children.Count > 1)
            inner.Children.RemoveAt(1);

        int col = startDow;
        Grid? rowGrid = null;

        for (int day = 1; day <= daysInMonth; day++)
        {
            if (col == 0 || rowGrid is null)
            {
                rowGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                for (int i = 0; i < 7; i++)
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                inner.Children.Add(rowGrid);
            }

            dayMap.TryGetValue(day, out var dayData);
            var cell = BuildCalDayCell(day, dayData);
            Grid.SetColumn(cell, col);
            rowGrid.Children.Add(cell);

            col = (col + 1) % 7;
        }
    }

    private Control BuildCalDayCell(int day, CalendarDayData? data)
    {
        bool isToday = _dashboardYear == DateTime.Now.Year
                    && _dashboardMonth == DateTime.Now.Month
                    && day == DateTime.Now.Day;

        var border = new Border
        {
            Margin          = new Thickness(2),
            MinHeight       = 64,
            Background      = isToday
                ? new SolidColorBrush(Color.FromArgb(30, 0x54, 0xA0, 0xFF))
                : Brushes.Transparent,
            BorderBrush     = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(4),
            Cursor          = data is not null && data.TotalSeconds > 0
                ? new Cursor(StandardCursorType.Hand)
                : new Cursor(StandardCursorType.Arrow)
        };
        if (data is not null && data.TotalSeconds > 0)
            ToolTip.SetTip(border, string.Format(
                LocalizationManager.Current.DailyHistoryTitle,
                new DateTime(_dashboardYear, _dashboardMonth, day)
                    .ToString("D", CultureInfo.CurrentCulture)));

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text       = day.ToString(),
            FontSize   = 11,
            FontWeight = isToday ? FontWeight.Bold : FontWeight.Normal,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Right
        });

        if (data is not null && data.TotalSeconds > 0)
        {
            var ts = TimeSpan.FromSeconds(data.TotalSeconds);
            stack.Children.Add(new TextBlock
            {
                Text       = FormatDashboardDuration(ts),
                FontSize   = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x54, 0xA0, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            });

            foreach (var genre in data.TopGenres.Take(3))
            {
                var genreButton = new Button
                {
                    Content = genre,
                    FontSize = 9,
                    Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                    Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(2, 0, 2, 0),
                    Tag = genre
                };
                genreButton.Click += DashboardGenreButton_OnClick;
                stack.Children.Add(genreButton);
            }
        }

        if (data is not null && data.TotalSeconds > 0)
        {
            var date = new DateTime(_dashboardYear, _dashboardMonth, day);
            border.PointerReleased += async (_, e) =>
            {
                e.Handled = true;
                await ShowDailyHistoryAsync(date);
            };
        }

        border.Child = stack;
        return border;
    }

    private void ShowPodcastInfo(PodcastPlayback playback)
    {
        var episode = playback.Episode;
        PodcastInfoImage.Source = null;
        PodcastInfoImagePlaceholder.IsVisible = true;
        PodcastInfoPodcastName.Text = playback.Podcast.Name;
        PodcastInfoEpisodeTitle.Text = episode.Title;

        var metadata = new List<string>();
        if (episode.PublishedAt is { } publishedAt)
            metadata.Add(string.Format(
                LocalizationManager.Current.PodcastPublishedOn,
                publishedAt.ToLocalTime().ToString("d")));
        if (episode.FeedDuration is { } duration && duration > TimeSpan.Zero)
            metadata.Add(string.Format(
                LocalizationManager.Current.PodcastEpisodeDuration,
                FormatTime(duration)));
        if (!string.IsNullOrWhiteSpace(playback.Podcast.Author))
            metadata.Add(playback.Podcast.Author);
        if (!string.IsNullOrWhiteSpace(playback.Podcast.Genre))
            metadata.Add(playback.Podcast.Genre);
        PodcastInfoMetadata.Text = string.Join("  ·  ", metadata);

        var description = NormalizePodcastDescription(episode.Description);
        PodcastInfoDescription.Text = string.IsNullOrWhiteSpace(description)
            ? LocalizationManager.Current.PodcastDescriptionUnavailable
            : description;

        _ = LoadPodcastInfoArtworkAsync(
            playback.Podcast.ArtworkUrl,
            _playbackCts?.Token ?? CancellationToken.None);
    }

    private async Task LoadPodcastInfoArtworkAsync(
        string? artworkUrl,
        CancellationToken cancellationToken)
    {
        var image = await DownloadPodcastImageAsync(artworkUrl, 600, cancellationToken);
        if (image is null || cancellationToken.IsCancellationRequested)
            return;

        PodcastInfoImage.Source = image;
        PodcastInfoImagePlaceholder.IsVisible = false;
    }

    private void DashboardBuildTopGenres(List<(string Genre, double Seconds)> genres)
    {
        if (genres.Count == 0)
        {
            DashboardPanel.Children.Add(new TextBlock
            {
                Text       = LocalizationManager.Current.NoData,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                Margin     = new Thickness(0, 4, 0, 0)
            });
            return;
        }

        double maxSecs = genres[0].Seconds;

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        for (int i = 0; i < genres.Count; i++)
        {
            var (genre, secs) = genres[i];
            var color = _genreColors[i % _genreColors.Length];
            var brush = new SolidColorBrush(color);

            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelButton = new Button
            {
                Content = $"{i + 1}. {genre}",
                FontSize = 12,
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 12, 0),
                Tag = genre
            };
            labelButton.Click += DashboardGenreButton_OnClick;
            Grid.SetColumn(labelButton, 0);
            row.Children.Add(labelButton);

            double fraction = maxSecs > 0 ? secs / maxSecs : 0;
            var barHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
            var barBg = new Border
            {
                Height          = 10,
                Background      = FindResource<IBrush>("AppGridLineBrush"),
                CornerRadius    = new CornerRadius(5)
            };
            var barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(fraction, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1 - fraction, GridUnitType.Star) });
            var barFill = new Border
            {
                Height       = 10,
                Background   = brush,
                CornerRadius = new CornerRadius(5)
            };
            Grid.SetColumn(barFill, 0);
            barGrid.Children.Add(barFill);
            barHost.Children.Add(barBg);
            barHost.Children.Add(barGrid);
            Grid.SetColumn(barHost, 1);
            row.Children.Add(barHost);

            var ts = TimeSpan.FromSeconds(secs);
            var durationTb = new TextBlock
            {
                Text      = FormatDashboardDuration(ts),
                FontSize  = 11,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin    = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(durationTb, 2);
            row.Children.Add(durationTb);

            panel.Children.Add(row);
        }

        DashboardPanel.Children.Add(panel);
    }

    private async void DashboardGenreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string genre } || string.IsNullOrWhiteSpace(genre))
            return;
        e.Handled = true;

        _trackFavoritesOnly = false;
        _selectedTrackGenres.Clear();
        _selectedTrackGenres.Add(genre);
        _selectedTrackFormats.Clear();
        _selectedTrackBitrates.Clear();
        _expandedTrackFilterSections.Add(LocalizationManager.Current.Genre);
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        _settings.LastMainView = "Tracks";

        var tracksItem = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item =>
                string.Equals(item.Tag as string, "Tracks", StringComparison.Ordinal));
        if (tracksItem is null)
            return;
        if (ReferenceEquals(NavListBox.SelectedItem, tracksItem))
            await ShowTopLevelViewAsync("Tracks");
        else
            NavListBox.SelectedItem = tracksItem;
    }

    private static string FormatDashboardDuration(TimeSpan value) =>
        $"{(int)value.TotalHours:D2}:{value.Minutes:D2}:{value.Seconds:D2}";

    private async Task ShowDailyHistoryAsync(DateTime date)
    {
        var entries = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetHistoryForDay(date);
        });

        var dialog = new DailyHistoryDialog(date, entries, _settings);
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedEntry is not { } entry)
            return;

        switch (dialog.SelectedAction)
        {
            case DailyHistoryAction.Track:
                await OpenHistoryTrackAsync(entry);
                break;
            case DailyHistoryAction.Album when entry.AlbumId is long albumId:
                await ShowAlbumTracksAsync(
                    albumId,
                    entry.Album ?? LocalizationManager.Current.Unknown);
                break;
            case DailyHistoryAction.Artist when entry.ArtistId is long artistId:
                await ShowArtistAlbumsAsync(
                    artistId,
                    entry.Artist ?? LocalizationManager.Current.Unknown);
                break;
        }
    }

    private async Task OpenHistoryTrackAsync(DailyHistoryEntry entry)
    {
        if (entry.TrackId is null || !IsAvailableLocalTrack(entry.Path))
            return;

        _trackFavoritesOnly = false;
        _selectedTrackGenres.Clear();
        _selectedTrackFormats.Clear();
        _selectedTrackBitrates.Clear();
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        _settings.LastMainView = "Tracks";

        var tracksItem = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item =>
                string.Equals(item.Tag as string, "Tracks", StringComparison.Ordinal));
        if (tracksItem is not null && !ReferenceEquals(NavListBox.SelectedItem, tracksItem))
        {
            _suppressNavSelectionChanged = true;
            try
            {
                NavListBox.SelectedItem = tracksItem;
            }
            finally
            {
                _suppressNavSelectionChanged = false;
            }
        }

        await ShowTopLevelViewAsync("Tracks");
        var rows = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        var row = rows.FirstOrDefault(item =>
            string.Equals(item.FilePath, entry.Path, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            return;

        ContentDataGrid.SelectedItem = row;
        ContentDataGrid.ScrollIntoView(row, null);
        await PlayTrackFromRowsAsync(row, rows);
    }

    private async void CalendarPrev_OnClick(object? sender, RoutedEventArgs e)
    {
        _dashboardMonth--;
        if (_dashboardMonth < 1) { _dashboardMonth = 12; _dashboardYear--; }
        await RefreshCalendarSectionAsync();
    }

    private async void CalendarNext_OnClick(object? sender, RoutedEventArgs e)
    {
        _dashboardMonth++;
        if (_dashboardMonth > 12) { _dashboardMonth = 1; _dashboardYear++; }
        await RefreshCalendarSectionAsync();
    }

    private async Task RefreshCalendarSectionAsync()
    {
        // Rebuild the whole dashboard — section header title contains the month/year
        await BuildDashboardAsync();
    }
}
