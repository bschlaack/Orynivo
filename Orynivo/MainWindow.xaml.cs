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
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Orynivo.Audio;
using Orynivo.Controls;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

public partial class MainWindow : Window
{
    private int _plexNavigationLoadVersion;
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
    private readonly Stack<PlexNavigationState> _plexNavigationStack = [];
    private const int ArtworkPageSize = 120;
    private List<ContentRow> _albumArtworkRows = [];
    private List<ContentRow> _artistArtworkRows = [];
    private readonly ObservableCollection<ContentRow> _visibleAlbumArtworkRows = [];
    private readonly ObservableCollection<ContentRow> _visibleArtistArtworkRows = [];

    // ------------------------------------------------------------------
    // Felder
    // ------------------------------------------------------------------

    private IAudioPlayer?  _player;
    private CancellationTokenSource? _playbackCts;
    private readonly SettingsStore _settingsStore = new();
    private AppSettings _settings = new();
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
    private readonly Stack<NavigationState> _navigationStack = [];
    private bool _restoringNavigationHistory;
    private readonly DispatcherTimer _searchTimer;
    private bool _trackFavoritesOnly;
    private bool _artistFavoritesOnly;
    private bool _albumFavoritesOnly;
    private bool _updatingEntityFavoritesFilter;
    private readonly HashSet<string> _selectedTrackGenres = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedTrackFormats = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _selectedTrackBitrates = [];
    private readonly HashSet<string> _expandedTrackFilterSections = new(StringComparer.Ordinal);
    private readonly HashSet<long> _artistProfilesLoading = [];
    private bool _isDraggingAlphabetIndex;
    private bool _alphabetScrollUpdatePending;
    private bool _isAlphabetProgrammaticScroll;
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
    private CancellationTokenSource? _lyricsCts;
    private CancellationTokenSource? _artistProfileCts;
    private CancellationTokenSource _backgroundArtistLoadCts = new();
    private int _activeLyricIndex = -1;
    private bool _updatingViewMode;

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
        string? SearchQuery = null);

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
        public string? Nr          { get; init; }
        public long? Id            { get; init; }
        public long? ArtistId       { get; set; }
        public long? AlbumId        { get; set; }
        public string? Title       { get; init; }
        public string? Artist      { get; init; }
        public string? Album       { get; init; }
        public string? AlbumArtist { get; init; }
        public string? Year        { get; init; }
        public string? Genre       { get; init; }
        public string? Folder      { get; init; }
        public string? ArtworkPath { get; set; }
        public string? ThumbnailPath { get; set; }
        public string? Biography { get; set; }
        public string? SourceUrl { get; set; }
        public string? ProfileLanguage { get; set; }
        public long? ProfileFetchedAt { get; set; }
        public bool ImageIsManual { get; set; }
        public string EntityType { get; init; } = "Track";
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
        public long?   PlaylistEntryId { get; init; }

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
        ContentDataGrid.AddHandler(
            ScrollViewer.ScrollChangedEvent, new EventHandler<ScrollChangedEventArgs>(AlphabetTarget_OnScrollChanged));
        AlbumArtworkListBox.AddHandler(
            ScrollViewer.ScrollChangedEvent, new EventHandler<ScrollChangedEventArgs>(AlphabetTarget_OnScrollChanged));
        ArtistArtworkListBox.AddHandler(
            ScrollViewer.ScrollChangedEvent, new EventHandler<ScrollChangedEventArgs>(AlphabetTarget_OnScrollChanged));
        NavListBox.AddHandler(
            PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>(NavListBox_OnPreviewMouseLeftButtonDown),
            handledEventsToo: true);
        PositionSlider.AddHandler(Slider.PointerPressedEvent,
            new EventHandler<PointerPressedEventArgs>(PositionSlider_OnPreviewMouseLeftButtonDown),
            handledEventsToo: true);
        QueueHydrateVisibleArtworkRows(AlbumArtworkListBox);
        QueueHydrateVisibleArtworkRows(ArtistArtworkListBox);
    }

    protected override void OnClosed(EventArgs e)
    {
        PersistViewState();
        CancelAndDispose(ref _radioSearchCts);
        CancelAndDispose(ref _podcastSearchCts);
        CancelAndDispose(ref _podcastFeedCts);
        CancelAndDispose(ref _plexViewCts);
        StopPlayback();
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
            tag is "Dashboard" or "InternetRadio" or "Podcasts" or "Artists" or "Albums" or "Tracks" or "Folders")
        {
            _settings.LastMainView = tag;
        }
        _settings.AlbumArtworkView = _showAlbumArtworkView;
        _settings.ArtistArtworkView = _showArtistArtworkView;
        _settings.Volume = VolumeSlider.Value;
        _settings.LastTrackPath = File.Exists(_currentFilePath) ? _currentFilePath : null;
        _settingsStore.Save(_settings);
    }

    private void LoadSettings()
    {
        _settings = _settingsStore.Load();
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

    private void RestoreLastTrackState()
    {
        var path = _settings.LastTrackPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
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
                    Theme = FindResource<ControlTheme>("NavItemTheme")
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
                    Theme = FindResource<ControlTheme>("NavItemTheme")
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
                    Theme = FindResource<ControlTheme>("NavItemTheme")
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

    private void ApplySidebarNavigationSettings()
    {
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

        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        if (tag is "InternetRadio" or "Podcasts" or "Artists" or "Albums" or "Tracks" or "Folders")
            _settings.LastMainView = tag;
        await ShowTopLevelViewAsync(tag);
    }

    private async void NavListBox_OnPreviewMouseLeftButtonUp(object? sender, PointerReleasedEventArgs e)
    {
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
                SearchTextBox.Text ?? string.Empty);

        if (AlbumDetailHeader.IsVisible && _activeAlbumFilterId is long albumId)
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
                artistId,
                null,
                null,
                _activeArtistFilterName);
        }

        if (!string.IsNullOrWhiteSpace(_currentTopLevelTag) &&
            !_currentTopLevelTag.StartsWith("Section:", StringComparison.Ordinal))
        {
            return new NavigationState(
                _currentTopLevelTag,
                GetSelectedContentRowId(),
                _activeArtistFilterId,
                _activeArtistFilterName,
                SearchTextBox.Text ?? string.Empty);
        }

        return null;
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
        SearchTextBox.IsVisible = !(tag is "InternetRadio" or "Podcasts" ||
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
            .GroupBy(row => GetAlphabetIndexKey(row.Title))
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
            sender is not RadioButton { Tag: string view } ||
            _activePlexServer is null)
        {
            return;
        }

        _activePlexView = view;
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
        if (reset)
        {
            _plexLoadedCount = 0;
            _plexTotalCount = 0;
        }

        ContentDataGrid.IsVisible = !(_activePlexView == "Folders");
        FolderTreeView.IsVisible = _activePlexView == "Folders";
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        SearchResultsScrollViewer.IsVisible = false;
        PlexLoadMoreButton.IsVisible = false;
        StatusTextBlock.Text = LocalizationManager.Current.PlexLoading;

        try
        {
            if (_activePlexView == "Folders")
            {
                await BuildPlexFolderTreeAsync(cancellationToken);
                ContentCountTextBlock.Text = string.Empty;
                StatusTextBlock.Text = string.Empty;
                return;
            }

            var mediaType = _activePlexView switch
            {
                "Artists" => 8,
                "Albums" => 9,
                _ => 10
            };
            var page = await _plexClient.GetLibraryItemsAsync(
                _activePlexServer,
                _activePlexToken,
                _activePlexSectionKey,
                mediaType,
                _plexLoadedCount,
                PlexPageSize,
                cancellationToken);
            var newRows = page.Items.Select(ToPlexContentRow).ToList();
            var rows = reset
                ? newRows
                : ((ContentDataGrid.ItemsSource as IEnumerable<ContentRow>) ?? [])
                    .Concat(newRows)
                    .ToList();
            _plexLoadedCount = rows.Count;
            _plexTotalCount = page.TotalSize;
            ApplyColumns("Plex" + _activePlexView);
            ContentDataGrid.ItemsSource = rows;
            UpdateAlphabetIndex(rows, _activePlexView is "Artists" or "Albums" or "Tracks");
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

    private ContentRow ToPlexContentRow(PlexMediaItem item)
    {
        var entityType = _activePlexView switch
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
            FilePath = item.PartKey is not null &&
                       _activePlexServer is not null
                ? PlexServerClient.CreateStreamUrl(
                    _activePlexServer,
                    item.PartKey,
                    _activePlexToken)
                : string.Empty,
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
            var rows = page.Items.Select(ToPlexContentRow).ToList();
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

    private async Task BuildPlexFolderTreeAsync(CancellationToken cancellationToken)
    {
        FolderTreeView.Items.Clear();
        var page = await _plexClient.GetFoldersAsync(
            _activePlexServer!,
            _activePlexToken,
            _activePlexSectionKey!,
            null,
            cancellationToken);
        foreach (var folder in page.Items)
            FolderTreeView.Items.Add(CreatePlexFolderItem(folder));
    }

    private TreeViewItem CreatePlexFolderItem(PlexMediaItem item)
    {
        var row = item.PartKey is null ? null : ToPlexContentRow(item);
        var treeItem = new TreeViewItem
        {
            Header = item.Title,
            Tag = new PlexFolderTag(item.Key, row is not null, row)
        };
        if (row is not null)
            return treeItem;

        var placeholder = new TreeViewItem();
        treeItem.Items.Add(placeholder);
        treeItem.Expanded += async (_, _) =>
        {
            if (treeItem.Items.Count != 1 || treeItem.Items[0] != placeholder)
                return;
            try
            {
                var page = await _plexClient.GetFoldersAsync(
                    _activePlexServer!,
                    _activePlexToken,
                    _activePlexSectionKey!,
                    item.Key);
                treeItem.Items.Clear();
                foreach (var child in page.Items)
                    treeItem.Items.Add(CreatePlexFolderItem(child));
            }
            catch
            {
                treeItem.Items.Clear();
            }
        };
        return treeItem;
    }

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
        if (sender is not Button { Tag: ContentRow row })
            return;

        ScrollToAlphabetRow(row);
    }

    private void ScrollToAlphabetRow(ContentRow row)
    {
        var targetKey = GetAlphabetIndexKey(row.Title);
        _isAlphabetProgrammaticScroll = true;
        SetActiveAlphabetButton(targetKey);

        if (ContentDataGrid.IsVisible)
        {
            var index = ((ContentDataGrid.ItemsSource as System.Collections.IList)?.IndexOf(row) ?? -1);
            var scrollViewer = FindVisualChild<ScrollViewer>(ContentDataGrid);
            if (index >= 0 && scrollViewer is not null)
                scrollViewer.SetCurrentValue(Avalonia.Controls.ScrollViewer.OffsetProperty, new Avalonia.Vector(0, index));
            else
                ContentDataGrid.ScrollIntoView(row, null);
        }
        else
        {
            var listBox = AlbumArtworkListBox.IsVisible
                ? AlbumArtworkListBox
                : ArtistArtworkListBox;
            EnsureArtworkRowBound(listBox, row);
            var index = (listBox.ItemsSource as System.Collections.IList)?.IndexOf(row) ?? -1;
            if (index >= 0)
                listBox.ScrollIntoView(row);
        }

        Dispatcher.UIThread.Post(() =>
        {
            SetActiveAlphabetButton(targetKey);
            _isAlphabetProgrammaticScroll = false;
        }, DispatcherPriority.Background);
    }

    private void AlphabetTarget_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ListBox listBox && (ReferenceEquals(listBox, AlbumArtworkListBox) || ReferenceEquals(listBox, ArtistArtworkListBox)))
        {
            AppendArtworkRowsIfNeeded(listBox);
            QueueHydrateVisibleArtworkRows(listBox);
        }

        if (!AlphabetIndexBorder.IsVisible ||
            e.OffsetDelta.Y == 0 ||
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

    private void UpdateActiveAlphabetButton()
    {
        if (!AlphabetIndexBorder.IsVisible)
            return;

        var row = GetTopVisibleAlphabetRow();
        var activeKey = row is null ? null : GetAlphabetIndexKey(row.Title);
        SetActiveAlphabetButton(activeKey);
    }

    private void SetActiveAlphabetButton(string? activeKey)
    {
        foreach (var button in AlphabetIndexPanel.Children.OfType<Button>())
            button.IsDefault = string.Equals(button.Content as string, activeKey, StringComparison.Ordinal);
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
            if (container.DataContext is not ContentRow)
                continue;

            var topLeft = container.TranslatePoint(new Point(0, 0), targetControl) ?? new Point(0, 0);
            var bounds = new Rect(topLeft.X, topLeft.Y, container.Bounds.Width, container.Bounds.Height);
            if (bounds.Bottom <= visibleTop || bounds.Top >= targetControl.Bounds.Height)
                continue;
            if (bounds.Top + 0.5 >= visibleTop && bounds.Top < bestTop)
            {
                bestTop = bounds.Top;
                bestContainer = container;
            }
        }

        return bestContainer?.DataContext as ContentRow;
    }

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

        if (button is { IsEnabled: true, Tag: ContentRow row })
            ScrollToAlphabetRow(row);
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

                    var facets = db.GetTrackFacets();
                    var ids = facets.Where(f => MatchesCriteria(f, criteria)).Select(f => f.Id).ToList();
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
                    return new ContentRow
                    {
                        Nr              = (i + 1).ToString(),
                        PlaylistEntryId = pt.Id,
                        Title    = t?.Title ?? Path.GetFileName(pt.Path),
                        Artist   = t?.Artist,
                        Album    = t?.Album,
                        Duration = t is not null ? FormatSeconds(t.Duration) : "",
                        Format   = t?.Format?.ToUpperInvariant(),
                        FilePath = pt.Path
                    };
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
        Title = t.Title ?? t.FileName,
        Id = t.Id,
        Artist = t.Artist,
        Album = t.Album,
        Duration = FormatSeconds(t.Duration),
        Genre = t.Genre,
        Format = t.Format?.ToUpperInvariant(),
        FilePath = t.Path,
        IsFavorite = t.IsFavorite
    };

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
            HorizontalAlignment = HorizontalAlignment.Stretch
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

    private static bool MatchesCriteria(TrackFacetInfo facet, SmartPlaylistCriteria criteria)
    {
        if (criteria.FavoritesOnly && !facet.IsFavorite)
            return false;
        if (criteria.Genres.Count > 0)
        {
            var trackGenres = string.IsNullOrWhiteSpace(facet.Genre)
                ? Array.Empty<string>()
                : facet.Genre.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!trackGenres.Any(g => criteria.Genres.Contains(g, StringComparer.OrdinalIgnoreCase)))
                return false;
        }
        if (criteria.Formats.Count > 0 &&
            (string.IsNullOrWhiteSpace(facet.Format) ||
             !criteria.Formats.Contains(facet.Format, StringComparer.OrdinalIgnoreCase)))
            return false;
        if (criteria.Bitrates.Count > 0 &&
            (!facet.Bitrate.HasValue || !criteria.Bitrates.Contains(facet.Bitrate.Value)))
            return false;
        return true;
    }

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

        var criteria = new SmartPlaylistCriteria(
            FavoritesOnly: _trackFavoritesOnly,
            Genres:   [.. _selectedTrackGenres.OrderBy(g => g)],
            Formats:  [.. _selectedTrackFormats.OrderBy(f => f)],
            Bitrates: [.. _selectedTrackBitrates.OrderBy(b => b)]);

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
            (LocalizationManager.Current.Title, nameof(ContentRow.Title), 240),
            (LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 180),
            (LocalizationManager.Current.Album, nameof(ContentRow.Album), 220),
            (LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 90),
            (LocalizationManager.Current.Format, nameof(ContentRow.Format), 80));
        ConfigureSearchGrid(SearchAlbumsDataGrid,
            (LocalizationManager.Current.Album, nameof(ContentRow.Title), 260),
            (LocalizationManager.Current.AlbumArtist, nameof(ContentRow.Artist), 220),
            (LocalizationManager.Current.Year, nameof(ContentRow.Year), 90));
        ConfigureSearchGrid(SearchArtistsDataGrid,
            (LocalizationManager.Current.Artist, nameof(ContentRow.Title), 320));
    }

    private void ConfigureSearchGrid(DataGrid grid, params (string Header, string Binding, double Width)[] columns)
    {
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
            grid.Columns.Add(entityType is null
                ? new DataGridTextColumn
                {
                    Header = column.Header,
                    Binding = new Binding(column.Binding),
                    Width = new DataGridLength(column.Width)
                }
                : CreateEntityLinkColumn(column.Header, column.Binding, column.Width, false, entityType));
        }
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
        if (tag == "Albums")
        {
            _albumArtworkRows = rows as List<ContentRow> ?? rows.ToList();
            ResetVisibleArtworkRows(_visibleAlbumArtworkRows, _albumArtworkRows);
            AlbumArtworkListBox.ItemsSource = _visibleAlbumArtworkRows;
            QueueHydrateVisibleArtworkRows(AlbumArtworkListBox);
        }
        else if (tag == "Artists")
        {
            _artistArtworkRows = rows as List<ContentRow> ?? rows.ToList();
            ResetVisibleArtworkRows(_visibleArtistArtworkRows, _artistArtworkRows);
            ArtistArtworkListBox.ItemsSource = _visibleArtistArtworkRows;
            QueueHydrateVisibleArtworkRows(ArtistArtworkListBox);
        }
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
        if (e.Row.DataContext is not ContentRow row)
            return;
        EnsureThumbnailHydrated(row);
        if (row.EntityType == "Artist")
            _ = EnsureArtistProfileAsync(row);
    }

    private void ApplyColumns(string view)
    {
        ContentDataGrid.Columns.Clear();
        switch (view)
        {
            case "PlexArtists":
                Add(LocalizationManager.Current.Artist, nameof(ContentRow.Title), 0, star: true);
                break;
            case "PlexAlbums":
                Add(LocalizationManager.Current.Album, nameof(ContentRow.Title), 0, star: true);
                Add(LocalizationManager.Current.AlbumArtist, nameof(ContentRow.Artist), 220);
                Add(LocalizationManager.Current.Year, nameof(ContentRow.Year), 70, right: true);
                break;
            case "PlexTracks":
                Add(LocalizationManager.Current.Title, nameof(ContentRow.Title), 0, star: true);
                Add(LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 180);
                Add(LocalizationManager.Current.Album, nameof(ContentRow.Album), 180);
                Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 70, right: true);
                Add(LocalizationManager.Current.Format, nameof(ContentRow.Format), 80);
                break;
            case "Artists":
                AddFavorite();
                AddThumbnail();
                AddArtistInfo();
                AddEntityLink(LocalizationManager.Current.Artist, nameof(ContentRow.Title), 0, true, "Artist");
                break;
            case "Albums":
                AddFavorite();
                AddThumbnail();
                AddEntityLink(LocalizationManager.Current.Album, nameof(ContentRow.Title), 0, true, "Album");
                AddEntityLink(LocalizationManager.Current.AlbumArtist, nameof(ContentRow.Artist), 220, false, "Artist");
                Add(LocalizationManager.Current.Year,        nameof(ContentRow.Year),   60,  right: true);
                break;
            case string s when s.StartsWith("Playlist:"):
                Add("#",        nameof(ContentRow.Nr),     38,  right: true);
                Add(LocalizationManager.Current.Title,    nameof(ContentRow.Title),  0,   star: true, starWeight: 2.2);
                AddEntityLink(LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 0, true, "Artist", 1.05);
                AddEntityLink(LocalizationManager.Current.Album, nameof(ContentRow.Album), 0, true, "Album", 1.05);
                Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 70, right: true);
                Add(LocalizationManager.Current.Format, nameof(ContentRow.Format), 70);
                break;
            default: // Tracks
                AddFavorite();
                Add(LocalizationManager.Current.Title,    nameof(ContentRow.Title),  0,   star: true, starWeight: 2.3);
                AddEntityLink(LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 0, true, "Artist", 1.05);
                AddEntityLink(LocalizationManager.Current.Album, nameof(ContentRow.Album), 0, true, "Album", 1.05);
                Add(LocalizationManager.Current.Genre,    nameof(ContentRow.Genre),  110);
                Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 70, right: true);
                Add(LocalizationManager.Current.Format,   nameof(ContentRow.Format), 76);
                break;
        }

        void AddEntityLink(string header, string prop, double width, bool star, string entityType, double starWeight = 1)
            => ContentDataGrid.Columns.Add(CreateEntityLinkColumn(header, prop, width, star, entityType, starWeight));

        void Add(string header, string prop, double width, bool star = false, bool right = false, double starWeight = 1)
        {
            if (right)
            {
                ContentDataGrid.Columns.Add(new DataGridTemplateColumn
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
                });
            }
            else
            {
                ContentDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = header,
                    Binding = new Binding(prop),
                    Width = star ? new DataGridLength(starWeight, DataGridLengthUnitType.Star) : new DataGridLength(width)
                });
            }
        }

        void AddFavorite()
        {
            ContentDataGrid.Columns.Add(new DataGridTemplateColumn
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
            ContentDataGrid.Columns.Add(new DataGridTemplateColumn
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
            ContentDataGrid.Columns.Add(new DataGridTemplateColumn
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
                .GroupBy(t => Path.GetDirectoryName(t.Path) ?? string.Empty,
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

    private void BuildFolderTree(List<TrackLite> tracks)
    {
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

    private static TreeViewItem CreateDirItemLazy(string dirPath, FolderTree tree, bool isRoot)
    {
        var name = Path.GetFileName(dirPath);
        var item = new TreeViewItem
        {
            Header = isRoot ? dirPath : (string.IsNullOrEmpty(name) ? dirPath : name),
            Tag    = new FolderTag(false, dirPath, dirPath)
        };

        if (!tree.HasChildren(dirPath))
            return item;

        PopulateDirNode(item, dirPath, tree);
        if (isRoot)
            item.IsExpanded = true;

        return item;
    }

    private static void PopulateDirNode(TreeViewItem parent, string dirPath, FolderTree tree)
    {
        parent.Items.Clear();
        foreach (var sub in tree.SubDirs(dirPath))
            parent.Items.Add(CreateDirItemLazy(sub, tree, isRoot: false));
        foreach (var track in tree.Files(dirPath))
            parent.Items.Add(new TreeViewItem
            {
                Header = track.DisplayName,
                Tag    = new FolderTag(true, track.Path, dirPath)
            });
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
            RefreshQueueNavigationButtons();

            await StartPlaybackAsync(filePath);
        }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private void FolderTreeView_OnPreviewMouseRightButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) return;
        var treeItem = FindAncestor<TreeViewItem>(e.Source as Visual);
        if (treeItem?.Tag is not FolderTag tag)
        {
            FolderTreeView.ContextMenu = null;
            return;
        }

        treeItem.IsSelected = true;

        List<string> paths;
        if (tag.IsFile)
        {
            paths = [tag.FilePath];
        }
        else
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                paths = db.GetTrackPathsUnderDirectory(tag.FilePath);
            }
            catch { paths = []; }
        }

        if (paths.Count == 0)
        {
            FolderTreeView.ContextMenu = null;
            return;
        }

        FolderTreeView.ContextMenu = BuildPlaylistContextMenu(paths);
    }

    // ------------------------------------------------------------------
    // Content-Doppelklick → Wiedergabe
    // ------------------------------------------------------------------

    private async void ContentDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null)
            return;
        if (ContentDataGrid.SelectedItem is not ContentRow row)
            return;
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
            return (
                Album: db.GetAlbumById(albumId),
                Tracks: db.GetTrackListByAlbum(albumId, artistId)
                    .Select(ToTrackContentRow)
                    .ToList());
        });
        var rows = result.Tracks;
        ApplyColumns("Tracks");
        ContentDataGrid.ItemsSource = rows;
        ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
        UpdateAlphabetIndex(null, false);
        ApplyAlbumDetailHeader(result.Album);
    }

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

        var row = new ContentRow
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
        EnsureArtworkHydrated(row);
        AlbumDetailHeader.DataContext = row;
        AlbumDetailHeader.IsVisible = true;
    }

    private void HideAlbumDetailHeader()
    {
        AlbumDetailHeader.IsVisible = false;
        AlbumDetailHeader.DataContext = null;
        ShowAllAlbumTracksCheckBox.IsVisible = false;
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

        using (var db = AudioDatabase.OpenDefault())
            db.ClearArtworkFromAlbum(albumId);

        if (_activeAlbumFilterId == albumId)
            await ReloadAlbumDetailHeaderAsync(albumId);
        else
            await ReloadAlbumRowsAsync();
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

        var dialog = new CoverSearchWindow(row.Title ?? string.Empty) ;
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        using (var db = AudioDatabase.OpenDefault())
            db.AttachArtworkToAlbum(albumId, selected.ImageData, selected.MimeType);

        if (_activeAlbumFilterId == albumId)
            await ReloadAlbumDetailHeaderAsync(albumId);
        else
            await ReloadAlbumRowsAsync();
    }

    private async Task ReloadAlbumRowsAsync()
    {
        var rows = await Task.Run(() => QueryRows("Albums"));
        ApplyColumns("Albums");
        ContentDataGrid.ItemsSource = rows;
        BindArtworkRows("Albums", rows);
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
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

            case "ArtistAlbums" when state.SelectedId is long artistId:
                await ShowArtistAlbumsAsync(
                    artistId,
                    string.IsNullOrWhiteSpace(state.SearchQuery)
                        ? LocalizationManager.Current.Unknown
                        : state.SearchQuery);
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
                RestoreSelection(artists, state.SelectedId);
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
                RestoreSelection(albums, state.SelectedId);
                break;

            default:
                SelectNavigationItem(state.View);
                await ShowTopLevelViewAsync(state.View);
                RestoreSelectionFromCurrentItems(state.SelectedId);
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

    private void RestoreSelectionFromCurrentItems(long? selectedId)
    {
        var rows = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList()
                   ?? (AlbumArtworkListBox.ItemsSource as IEnumerable<ContentRow>)?.ToList()
                   ?? (ArtistArtworkListBox.ItemsSource as IEnumerable<ContentRow>)?.ToList()
                   ?? [];
        RestoreSelection(rows, selectedId);
    }

    private void RestoreSelection(List<ContentRow> rows, long? selectedId)
    {
        if (selectedId is not long id)
            return;
        var row = rows.FirstOrDefault(r => r.Id == id);
        if (row is null)
            return;
        ContentDataGrid.SelectedItem = row;
        ContentDataGrid.ScrollIntoView(row, null);
        EnsureArtworkRowBound(AlbumArtworkListBox, row);
        EnsureArtworkRowBound(ArtistArtworkListBox, row);
        AlbumArtworkListBox.SelectedItem = row;
        AlbumArtworkListBox.ScrollIntoView(row);
        ArtistArtworkListBox.SelectedItem = row;
        ArtistArtworkListBox.ScrollIntoView(row);
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

    private void ContentDataGrid_OnPreviewMouseRightButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) return;
        var dataRow = FindAncestor<DataGridRow>(e.Source as Visual);
        if (dataRow?.DataContext is not ContentRow contentRow)
        {
            ContentDataGrid.ContextMenu = null;
            return;
        }

        ContentDataGrid.SelectedItem = contentRow;
        if (contentRow.EntityType.StartsWith("Plex", StringComparison.Ordinal))
        {
            ContentDataGrid.ContextMenu = null;
            return;
        }

        bool isTrack = !string.IsNullOrEmpty(contentRow.FilePath);
        bool isAlbum = contentRow.EntityType == "Album";

        if (!isTrack && !isAlbum)
        {
            ContentDataGrid.ContextMenu = null;
            return;
        }

        if (_activePlaylistId.HasValue && isTrack && contentRow.PlaylistEntryId.HasValue)
        {
            ContentDataGrid.ContextMenu = BuildRemoveFromPlaylistContextMenu(contentRow.PlaylistEntryId.Value);
            return;
        }

        ContentDataGrid.ContextMenu = BuildPlaylistContextMenu(GetPathsForRow(contentRow));
    }

    private void SearchTracksDataGrid_OnPreviewMouseRightButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) return;
        var dataRow = FindAncestor<DataGridRow>(e.Source as Visual);
        if (dataRow?.DataContext is not ContentRow contentRow || string.IsNullOrEmpty(contentRow.FilePath))
        {
            SearchTracksDataGrid.ContextMenu = null;
            return;
        }
        SearchTracksDataGrid.SelectedItem = contentRow;
        SearchTracksDataGrid.ContextMenu = BuildPlaylistContextMenu(GetPathsForRow(contentRow));
    }

    private void SearchAlbumsDataGrid_OnPreviewMouseRightButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) return;
        var dataRow = FindAncestor<DataGridRow>(e.Source as Visual);
        if (dataRow?.DataContext is not ContentRow contentRow || contentRow.Id is null)
        {
            SearchAlbumsDataGrid.ContextMenu = null;
            return;
        }
        SearchAlbumsDataGrid.SelectedItem = contentRow;
        SearchAlbumsDataGrid.ContextMenu = BuildPlaylistContextMenu(GetPathsForRow(contentRow));
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

    private ContextMenu BuildPlaylistContextMenu(IReadOnlyList<string> paths)
    {
        var menu = new ContextMenu();
                AppendPlaylistItems(menu, paths);
        return menu;
    }

    private void AppendPlaylistItems(ItemsControl menu, IReadOnlyList<string> paths)
    {
        var header = new MenuItem
        {
            Header           = LocalizationManager.Current.AddToPlaylist,
            IsHitTestVisible = false,
            Focusable        = false,
            FontSize         = 11,
            FontWeight       = FontWeight.SemiBold,
            Foreground       = new SolidColorBrush(
                                   Color.FromRgb(0x6C, 0x63, 0xFF))
        };
        menu.Items.Add(header);

        var sep0 = new Separator();
        menu.Items.Add(sep0);

        List<PlaylistRecord> playlists;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            playlists = db.GetAllPlaylists().ToList();
        }
        catch { playlists = []; }

        foreach (var pl in playlists)
        {
            var item = new MenuItem { Header = pl.Name, Tag = new PlaylistMenuTag(pl.Id, paths) };
            item.Click += PlaylistMenuItem_OnClick;
            menu.Items.Add(item);
        }

        if (playlists.Count > 0)
        {
            var sep1 = new Separator();
            menu.Items.Add(sep1);
        }

        var newItem = new MenuItem { Header = LocalizationManager.Current.NewPlaylist, Tag = paths };
        newItem.Click += NewPlaylistMenuItem_OnClick;
        menu.Items.Add(newItem);
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

    private void NavListBox_OnPreviewMouseRightButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) return;
        var item = FindAncestor<ListBoxItem>(e.Source as Visual);
        if (item?.Tag is not string tag)
        {
            NavListBox.ContextMenu = null;
            return;
        }

        if (tag.StartsWith("Radio:", StringComparison.Ordinal) &&
            long.TryParse(tag.AsSpan("Radio:".Length), out var radioId))
        {
            RadioStationRecord? radio;
            try
            {
                using var db = AudioDatabase.OpenDefault();
                radio = db.GetRadioStation(radioId);
            }
            catch
            {
                radio = null;
            }
            NavListBox.ContextMenu = radio is null ? null : BuildDeleteRadioContextMenu(radio);
            return;
        }

        if (tag.StartsWith("Podcast:", StringComparison.Ordinal) &&
            long.TryParse(tag.AsSpan("Podcast:".Length), out var podcastId))
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
            NavListBox.ContextMenu = podcast is null ? null : BuildDeletePodcastContextMenu(podcast);
            return;
        }

        item.IsSelected = true;
        if (!tag.StartsWith("Playlist:", StringComparison.Ordinal) ||
            !long.TryParse(tag.AsSpan("Playlist:".Length), out long playlistId))
        {
            NavListBox.ContextMenu = null;
            return;
        }

        string name;
        try { using var db = AudioDatabase.OpenDefault(); name = db.GetPlaylistById(playlistId)?.Name ?? string.Empty; }
        catch { name = string.Empty; }
        NavListBox.ContextMenu = BuildDeletePlaylistContextMenu(playlistId, name);
    }

    private ContextMenu BuildDeleteRadioContextMenu(RadioStationRecord radio)
    {
        var menu = new ContextMenu();
        var header = new MenuItem
        {
            Header = radio.Name,
            IsHitTestVisible = false,
            Focusable = false,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF))
        };
        menu.Items.Add(header);

        var separator = new Separator();
        menu.Items.Add(separator);

        var deleteItem = new MenuItem
        {
            Header = LocalizationManager.Current.DeleteRadio,
            Tag = radio,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B))
        };
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

    private ContextMenu BuildDeletePodcastContextMenu(PodcastRecord podcast)
    {
        var menu = new ContextMenu();
        var header = new MenuItem
        {
            Header = podcast.Name,
            IsHitTestVisible = false,
            Focusable = false,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x63, 0xFF))
        };
        menu.Items.Add(header);

        var separator = new Separator();
        menu.Items.Add(separator);

        var deleteItem = new MenuItem
        {
            Header = LocalizationManager.Current.DeletePodcast,
            Tag = podcast,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B))
        };
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

    private ContextMenu BuildDeletePlaylistContextMenu(long playlistId, string playlistName)
    {
        var menu = new ContextMenu();
        var header = new MenuItem
        {
            Header           = playlistName,
            IsHitTestVisible = false,
            Focusable        = false,
            FontSize         = 11,
            FontWeight       = FontWeight.SemiBold,
            Foreground       = new SolidColorBrush(
                                   Color.FromRgb(0x6C, 0x63, 0xFF))
        };
        menu.Items.Add(header);

        var sep = new Separator();
        menu.Items.Add(sep);

        var deleteItem = new MenuItem
        {
            Header     = LocalizationManager.Current.DeletePlaylist,
            Tag        = playlistId,
            Foreground = new SolidColorBrush(
                             Color.FromRgb(0xFF, 0x6B, 0x6B))
        };
        deleteItem.Click += DeletePlaylistMenuItem_OnClick;
        menu.Items.Add(deleteItem);

        return menu;
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

    private ContextMenu BuildRemoveFromPlaylistContextMenu(long playlistEntryId)
    {
        var menu = new ContextMenu();
        var header = new MenuItem
        {
            Header           = LocalizationManager.Current.RemoveFromPlaylist,
            IsHitTestVisible = false,
            Focusable        = false,
            FontSize         = 11,
            FontWeight       = FontWeight.SemiBold,
            Foreground       = new SolidColorBrush(
                                   Color.FromRgb(0x6C, 0x63, 0xFF))
        };
        menu.Items.Add(header);

        var sep = new Separator();
        menu.Items.Add(sep);

        var removeItem = new MenuItem
        {
            Header = LocalizationManager.Current.RemoveFromPlaylist,
            Tag    = new RemovePlaylistEntryTag(playlistEntryId)
        };
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
        _queue.Clear();
        _queueIndex = -1;
        ResetQueuePlaybackState();
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
        _queue.Clear();
        _queueIndex = -1;
        ResetQueuePlaybackState();
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

    private async void PlayButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_player is not null)
        {
            if (_player.IsPaused)
            {
                _player.Resume();
                SetPlayPauseIcon(isPlaying: true);
            }
            else
            {
                _player.Pause();
                SetPlayPauseIcon(isPlaying: false);
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

    private async Task StartPlaybackAsync(
        string filePath,
        RadioStationRecord? radioStation = null,
        PodcastPlayback? podcastPlayback = null)
    {
        StopPlayback();
        _currentFilePath = filePath;
        _currentRadioStation = radioStation;
        _currentPodcastPlayback = podcastPlayback;
        if (radioStation is null && podcastPlayback is null)
            _playedQueuePaths.Add(filePath);
        _playbackCts     = new CancellationTokenSource();

        var ext = Path.GetExtension(filePath);
        IAudioPlayer player;
        AudioFileInfo info;

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
            if (ext.Equals(".dsf", StringComparison.OrdinalIgnoreCase))
                (player, info) = await DsfAudioPlayer.CreateAsync(
                    filePath,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    _playbackCts.Token);
            else if (ext.Equals(".dff", StringComparison.OrdinalIgnoreCase))
                (player, info) = await DffAudioPlayer.CreateAsync(
                    filePath,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    _playbackCts.Token);
            else
                (player, info) = await FfmpegAudioPlayer.CreateAsync(
                    filePath,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    _playbackCts.Token);
        }
        else if (_settings.OutputBackend == OutputBackend.Wasapi)
        {
            if (string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId))
            {
                StatusTextBlock.Text = LocalizationManager.Current.SelectWasapiDevice;
                return;
            }
            (player, info) = await WasapiAudioPlayer.CreateAsync(filePath, _settings.SelectedWasapiDeviceId, _playbackCts.Token);
        }
        else
        {
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.NotImplemented, _settings.OutputBackend);
            return;
        }

        _player        = player;
        _player.Volume = (float)VolumeSlider.Value;
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
        FileInfoTextBlock.Text     = info.IsDsd && info.ContainerName is "dsf" or "dff"
            ? $"{info.ContainerName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  DSD nativ"
            : info.IsDsd
                ? $"DSD → PCM  ·  {info.OutputSampleRate:N0} Hz"
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

        var outputName = _settings.OutputBackend is OutputBackend.Asio or OutputBackend.CwAsio
            ? _settings.SelectedDriverName
            : _settings.SelectedWasapiDeviceName;
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.PlaybackThrough, outputName);

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

    private async void PreviousButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryMoveToPreviousQueueIndex())
            return;

        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private async void NextButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryMoveToNextQueueIndex())
            return;

        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private void StopPlayback()
    {
        RecordPlaybackEnd(completed: false);
        SavePodcastProgress(completed: false);
        CancelAndDispose(ref _radioMetadataCts);
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = null;
        _player?.Dispose();
        _player = null;

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
        LyricsButton.IsEnabled = false;
        ArtistInfoButton.IsEnabled = false;
        ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
        PodcastInfoView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        ClearRadioNowPlaying();
        UpdateNowPlayingFavoriteButton();
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

    private void RecordPlaybackEnd(bool completed)
    {
        if (_currentPlayHistoryId is not long historyId)
            return;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.RecordPlaybackEnd(historyId, _player?.Position.TotalSeconds ?? 0, completed);
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
            return;
        }

        PreviousButton.IsEnabled = _queueIndex > 0 && _queueIndex < _queue.Count;
        NextButton.IsEnabled = _queueIndex >= 0 && _queueIndex + 1 < _queue.Count;
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
        if (_player is null || !_player.CanSeek) return;
        await _player.SeekAsync(TimeSpan.FromSeconds(PositionSlider.Value));
        _isSeekingWithSlider = false;
        RefreshTransport();
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

        var editDialog = new EditArtistNameDialog(artist.Artist) ;
        if (await editDialog.ShowDialog<bool>(this) == false)
            return;

        ArtistRenameResult result;
        long? matchingArtistId = null;
        try
        {
            using (var db = AudioDatabase.OpenDefault())
            {
                var matchingArtist = db.FindArtistByName(editDialog.ArtistName, artistId);
                if (matchingArtist is null)
                {
                    result = db.RenameArtist(artistId, editDialog.ArtistName);
                }
                else
                {
                    matchingArtistId = matchingArtist.Id;
                    var mergeDialog = new ArtistMergeDialog(
                        artist.Id,
                        artist.Artist,
                        matchingArtist.Id,
                        matchingArtist.Artist);
                    if (await mergeDialog.ShowDialog<bool>(this) == false)
                        return;

                    result = db.MergeArtists(
                        artist.Id,
                        matchingArtist.Id,
                        mergeDialog.PreferredArtistId,
                        editDialog.ArtistName);
                }
            }

            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                TrackSearchIndex.Rebuild(db.GetAll().ToList());
            });

            if (_currentArtistId == artistId || _currentArtistId == matchingArtistId)
            {
                _currentArtistId = result.ArtistId;
                _currentArtistName = result.ArtistName;
            }
            if (_activeArtistFilterId == artistId || _activeArtistFilterId == matchingArtistId)
            {
                _activeArtistFilterId = result.ArtistId;
                _activeArtistFilterName = result.ArtistName;
            }

            await ReloadVisibleArtistListAsync();
            await ShowArtistInfoAsync(result.ArtistId, forceRefresh: false);
        }
        catch
        {
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistRenameFailed;
            ArtistInfoStatusTextBlock.IsVisible = true;
        }
    }

    private async Task ReloadVisibleArtistListAsync()
    {
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: "Artists" })
            return;

        var rows = await Task.Run(() => QueryRows("Artists"));
        ApplyColumns("Artists");
        ContentDataGrid.ItemsSource = rows;
        BindArtworkRows("Artists", rows);
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
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
            LyricsListBox.ScrollIntoView(activeLine);
        }
    }

    private void ShowLyricsStatus(string text)
    {
        LyricsStatusTextBlock.Text = text;
        LyricsStatusTextBlock.IsVisible = true;
    }

    private void ClearLyrics()
    {
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
        if (_player is not null) _player.Volume = (float)VolumeSlider.Value;
        _settings.Volume = VolumeSlider.Value;
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    // ------------------------------------------------------------------
    // Einstellungen
    // ------------------------------------------------------------------

    private async void SettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings, paths =>
        {
            _settings.LibraryPaths = paths;
            _settingsStore.Save(_settings);
        })
        ;

        if (await window.ShowDialog<bool>(this) == true)
        {
            _settings.OutputBackend          = window.SelectedOutputBackend;
            _settings.SelectedDriverName     = window.SelectedDriverName;
            _settings.SelectedWasapiDeviceId = window.SelectedWasapiDeviceId;
            _settings.SelectedWasapiDeviceName = window.SelectedWasapiDeviceName;
            _settings.LibraryPaths           = window.SelectedLibraryPaths.ToList();
            _settings.Theme                  = window.SelectedTheme;
            _settings.Language               = window.SelectedLanguage;
            _settings.ArtistInfoSource       = window.SelectedArtistInfoSource;
            _settings.LastFmApiKey           = window.SelectedLastFmApiKey;
            _settings.QobuzApplicationId      = window.SelectedQobuzApplicationId;
            _settings.PlexServers             = window.SelectedPlexServers.ToList();
            _settings.ShowLocalLibrarySection = window.ShowLocalLibrarySection;
            _settings.ShowOwnRadiosSection    = window.ShowOwnRadiosSection;
            _settings.ShowMyPodcastsSection   = window.ShowMyPodcastsSection;
            _settings.ShowPlexSection         = window.ShowPlexSection;
            _settings.ShowPlaylistsSection    = window.ShowPlaylistsSection;
            try
            {
                new WindowsPlexCredentialStore().SaveAll(window.SelectedPlexTokens);
            }
            catch { }
            _settingsStore.Save(_settings);
            ThemeManager.Apply(_settings.Theme);
            LocalizationManager.Apply(_settings.Language);
            ApplyArtistInfoSettings();
            RefreshSelectedDriverText();
            LoadNavPlaylists();
            ApplySidebarNavigationSettings();

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
    }

    internal void PrepareForLibraryImport()
    {
        _searchTimer.Stop();
        StopPlayback();
    }

    private async void AboutButton_OnClick(object? sender, RoutedEventArgs e)
    {
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

        var dialog = new DailyHistoryDialog(date, entries);
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
        if (entry.TrackId is null || !File.Exists(entry.Path))
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
