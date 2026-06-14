using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orynivo.Audio;
using Orynivo.Controls;
using Orynivo.Library;
using Orynivo.Localization;
using Button = System.Windows.Controls.Button;
using WpfImage = System.Windows.Controls.Image;
using WpfCursors = System.Windows.Input.Cursors;
using WpfBrush = System.Windows.Media.Brush;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace Orynivo;

public partial class MainWindow : Window
{
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
    private readonly DispatcherTimer _searchTimer;
    private bool _trackFavoritesOnly;
    private readonly HashSet<string> _selectedTrackGenres = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedTrackFormats = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _selectedTrackBitrates = [];
    private readonly HashSet<string> _expandedTrackFilterSections = new(StringComparer.Ordinal);
    private readonly HashSet<long> _artistProfilesLoading = [];
    private bool _isDraggingAlphabetIndex;
    private bool _alphabetScrollUpdatePending;
    private bool _isAlphabetProgrammaticScroll;

    private readonly ObservableCollection<PlaylistItem> _queue = [];
    private readonly ObservableCollection<LyricLineViewModel> _lyricLines = [];
    private int _queueIndex = -1;
    private bool _shuffleEnabled;
    private readonly HashSet<string> _playedQueuePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<int> _shuffleHistory = [];
    private int _shuffleHistoryPosition = -1;
    private string _currentFilePath = string.Empty;
    private CancellationTokenSource? _lyricsCts;
    private CancellationTokenSource? _artistProfileCts;
    private CancellationTokenSource _backgroundArtistLoadCts = new();
    private int _activeLyricIndex = -1;
    private bool _updatingViewMode;

    private int _dashboardYear;
    private int _dashboardMonth;
    private StackPanel? _calendarInner;

    private static readonly System.Windows.Media.Color[] _genreColors =
    [
        System.Windows.Media.Color.FromRgb(0x6C, 0x63, 0xFF),
        System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x9D),
        System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x43),
        System.Windows.Media.Color.FromRgb(0x1D, 0xD1, 0xA1),
        System.Windows.Media.Color.FromRgb(0x54, 0xA0, 0xFF),
        System.Windows.Media.Color.FromRgb(0xFE, 0xCE, 0x00),
        System.Windows.Media.Color.FromRgb(0xC4, 0x4E, 0xFC),
        System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B),
        System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71),
        System.Windows.Media.Color.FromRgb(0x3C, 0xC7, 0xF0),
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
    private sealed record PlaylistMenuTag(long PlaylistId, IReadOnlyList<string> Paths);
    private sealed record RemovePlaylistEntryTag(long PlaylistEntryId);
    private sealed record NavigationState(
        string View,
        long? SelectedId,
        long? ArtistFilterId,
        string? ArtistFilterName,
        string? SearchQuery = null);

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
        private ImageSource? _artwork;
        private ImageSource? _thumbnail;
        public ImageSource? Artwork
        {
            get => _artwork;
            set => SetField(ref _artwork, value);
        }
        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set => SetField(ref _thumbnail, value);
        }
        public bool IsFavorite { get; set; }
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
        ContentDataGrid.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(AlphabetTarget_OnScrollChanged));
        AlbumArtworkListBox.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(AlphabetTarget_OnScrollChanged));
        ArtistArtworkListBox.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(AlphabetTarget_OnScrollChanged));
        SourceInitialized += (_, _) => ApplyWindowTitleBarColors();
        _transportTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _transportTimer.Tick += (_, _) => RefreshTransport();
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await ShowSearchResultsAsync(SearchTextBox.Text);
        };
        // Register with handledEventsToo=true so our handler fires even after
        // IsMoveToPointEnabled marks PreviewMouseLeftButtonDown as handled.
        PositionSlider.AddHandler(
            PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(PositionSlider_OnPreviewMouseLeftButtonDown),
            handledEventsToo: true);
        LoadSettings();
        LoadNavPlaylists();
        _showAlbumArtworkView = _settings.AlbumArtworkView;
        _showArtistArtworkView = _settings.ArtistArtworkView;
        VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 1);
        AlbumArtworkViewRadioButton.IsChecked = _showAlbumArtworkView;
        AlbumTableViewRadioButton.IsChecked = !_showAlbumArtworkView;
        SelectInitialView();
        RestoreLastTrackState();
    }

    private void ApplyWindowTitleBarColors()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            const int DwmwaCaptionColor = 35;
            const int DwmwaTextColor = 36;
            var captionColor = ColorRef(0x13, 0x14, 0x2A);
            var textColor = ColorRef(0xFF, 0xFF, 0xFF);
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch
        {
            // Ältere Windows-Versionen oder nicht unterstützte Shells behalten die Standard-Titelleiste.
        }
    }

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    protected override void OnClosed(EventArgs e)
    {
        PersistViewState();
        StopPlayback();
        base.OnClosed(e);
    }

    private void SelectInitialView()
    {
        var tag = _settings.LastMainView;
        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.Ordinal));
        NavListBox.SelectedItem = item
            ?? NavListBox.Items.OfType<ListBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, "Tracks", StringComparison.Ordinal));
    }

    private void PersistViewState()
    {
        if (NavListBox.SelectedItem is ListBoxItem { Tag: string tag } &&
            tag is "Dashboard" or "Artists" or "Albums" or "Tracks" or "Folders")
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
                $"{_settings.SelectedDriverName}  ·  ASIO",
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
        // Dynamisch hinzugefügte Playlist-Einträge (Index 7+) entfernen; Index 0 = Dashboard
        while (NavListBox.Items.Count > 7)
            NavListBox.Items.RemoveAt(NavListBox.Items.Count - 1);

        try
        {
            using var db = AudioDatabase.OpenDefault();
            foreach (var pl in db.GetAllPlaylists())
            {
                object content;
                if (pl.IsSmartPlaylist)
                {
                    var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                    sp.Children.Add(new TextBlock
                    {
                        Text       = "⚡ ",
                        Foreground = new System.Windows.Media.SolidColorBrush(
                                         System.Windows.Media.Color.FromRgb(0xFF, 0xCC, 0x00))
                    });
                    sp.Children.Add(new TextBlock { Text = pl.Name });
                    content = sp;
                }
                else
                {
                    content = pl.Name;
                }

                NavListBox.Items.Add(new ListBoxItem
                {
                    Content = content,
                    Tag     = $"Playlist:{pl.Id}",
                    Style   = (Style)FindResource("NavItemStyle")
                });
            }
        }
        catch { /* DB noch nicht angelegt */ }
    }

    private async void NavListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: string tag })
            return;

        ResetDrilldownState();
        if (tag is "Artists" or "Albums" or "Tracks" or "Folders")
            _settings.LastMainView = tag;
        await ShowTopLevelViewAsync(tag);
    }

    private async void NavListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { Tag: string tag, IsSelected: true })
            return;

        // Beim erneuten Klick auf den bereits markierten Hauptpunkt feuert kein SelectionChanged.
        // Trotzdem soll die ungefilterte Top-Level-Ansicht wiederhergestellt werden.
        if (_activeAlbumFilterId is null && _activeArtistFilterId is null && _navigationStack.Count == 0)
            return;

        ResetDrilldownState();
        await ShowTopLevelViewAsync(tag);
    }

    private void ResetDrilldownState()
    {
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        _activeArtistFilterId = null;
        _activeArtistFilterName = null;
        _navigationStack.Clear();
        BackButton.Visibility = Visibility.Collapsed;
    }

    private async Task ShowTopLevelViewAsync(string tag)
    {
        LyricsView.Visibility = Visibility.Collapsed;
        ArtistInfoView.Visibility = Visibility.Collapsed;
        _activePlaylistId = tag.StartsWith("Playlist:") &&
                            long.TryParse(tag.AsSpan("Playlist:".Length), out long parsedPid)
            ? parsedPid : null;

        ContentTitleTextBlock.Text = tag switch
        {
            "Artists" => LocalizationManager.Current.Artists,
            "Albums"  => LocalizationManager.Current.Albums,
            "Tracks"  => LocalizationManager.Current.Tracks,
            "Folders" => LocalizationManager.Current.FolderStructure,
            _         => tag.StartsWith("Playlist:") ? GetPlaylistName(tag) : tag
        };

        ContentDataGrid.ItemsSource = null;
        AlbumArtworkListBox.ItemsSource = null;
        UpdateAlphabetIndex(null, false);
        SearchResultsScrollViewer.Visibility = Visibility.Collapsed;
        DashboardScrollViewer.Visibility = Visibility.Collapsed;
        HideAlbumDetailHeader();
        ContentCountTextBlock.Text  = "";
        AlbumViewModeBorder.Visibility = tag is "Albums" or "Artists" ? Visibility.Visible : Visibility.Collapsed;
        if (tag is "Albums" or "Artists")
            SetViewModeButtons(tag == "Albums" ? _showAlbumArtworkView : _showArtistArtworkView);
        TrackFilterButton.Visibility = tag == "Tracks" ? Visibility.Visible : Visibility.Collapsed;
        SaveSmartPlaylistButton.Visibility = tag == "Tracks" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "Tracks") UpdateSaveSmartPlaylistButtonState();
        TrackFilterPopup.IsOpen = false;
        if (tag == "Dashboard")
        {
            ContentDataGrid.Visibility = Visibility.Collapsed;
            FolderTreeView.Visibility  = Visibility.Collapsed;
            AlbumArtworkListBox.Visibility = Visibility.Collapsed;
            ArtistArtworkListBox.Visibility = Visibility.Collapsed;
            DashboardScrollViewer.Visibility = Visibility.Visible;
            await ShowDashboardAsync();
        }
        else if (tag == "Folders")
        {
            ContentDataGrid.Visibility = Visibility.Collapsed;
            FolderTreeView.Visibility  = Visibility.Visible;
            AlbumArtworkListBox.Visibility = Visibility.Collapsed;
            ArtistArtworkListBox.Visibility = Visibility.Collapsed;

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
            ContentDataGrid.Visibility = showArtwork
                ? Visibility.Collapsed
                : Visibility.Visible;
            FolderTreeView.Visibility  = Visibility.Collapsed;
            AlbumArtworkListBox.Visibility = tag == "Albums" && _showAlbumArtworkView
                ? Visibility.Visible
                : Visibility.Collapsed;
            ArtistArtworkListBox.Visibility = tag == "Artists" && _showArtistArtworkView
                ? Visibility.Visible
                : Visibility.Collapsed;

            var rows = tag == "Tracks"
                ? await Task.Run(GetFilteredTrackRows)
                : await Task.Run(() => QueryRows(tag));
            ApplyColumns(tag);
            ContentDataGrid.ItemsSource = rows;
            AlbumArtworkListBox.ItemsSource = tag == "Albums" ? rows : null;
            ArtistArtworkListBox.ItemsSource = tag == "Artists" ? rows : null;
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
                Style = (Style)FindResource("AlphabetIndexButtonStyle")
            };
            button.Click += AlphabetIndexButton_OnClick;
            AlphabetIndexPanel.Children.Add(button);
        }

        AlphabetIndexBorder.Visibility = visible && rows.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ContentDataGrid.Margin = new Thickness(0);
        AlbumArtworkListBox.Margin = new Thickness(0);
        ArtistArtworkListBox.Margin = new Thickness(0);
        Dispatcher.BeginInvoke(UpdateActiveAlphabetButton, DispatcherPriority.Loaded);
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

    private void AlphabetIndexButton_OnClick(object sender, RoutedEventArgs e)
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

        if (ContentDataGrid.Visibility == Visibility.Visible)
        {
            var index = ContentDataGrid.Items.IndexOf(row);
            var scrollViewer = FindVisualChild<ScrollViewer>(ContentDataGrid);
            if (index >= 0 && scrollViewer is not null)
                scrollViewer.ScrollToVerticalOffset(index);
            else
                ContentDataGrid.ScrollIntoView(row);
        }
        else
        {
            var listBox = AlbumArtworkListBox.Visibility == Visibility.Visible
                ? AlbumArtworkListBox
                : ArtistArtworkListBox;
            var index = listBox.Items.IndexOf(row);
            var panel = FindVisualChild<VirtualizingWrapPanel>(listBox);
            if (index >= 0 && panel is not null)
            {
                var itemsPerRow = Math.Max(1, (int)Math.Floor(panel.ViewportWidth / panel.ItemWidth));
                panel.SetVerticalOffset((index / itemsPerRow) * panel.ItemHeight);
            }
            else
            {
                listBox.ScrollIntoView(row);
            }
        }

        Dispatcher.BeginInvoke(() =>
        {
            SetActiveAlphabetButton(targetKey);
            _isAlphabetProgrammaticScroll = false;
        }, DispatcherPriority.ContextIdle);
    }

    private void AlphabetTarget_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (AlphabetIndexBorder.Visibility != Visibility.Visible ||
            e.VerticalChange == 0 ||
            _isAlphabetProgrammaticScroll ||
            _alphabetScrollUpdatePending)
            return;

        _alphabetScrollUpdatePending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _alphabetScrollUpdatePending = false;
            UpdateActiveAlphabetButton();
        }, DispatcherPriority.Background);
    }

    private void UpdateActiveAlphabetButton()
    {
        if (AlphabetIndexBorder.Visibility != Visibility.Visible)
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
        ItemsControl? itemsControl = ContentDataGrid.Visibility == Visibility.Visible
            ? ContentDataGrid
            : AlbumArtworkListBox.Visibility == Visibility.Visible
                ? AlbumArtworkListBox
                : ArtistArtworkListBox.Visibility == Visibility.Visible
                    ? ArtistArtworkListBox
                    : null;
        if (itemsControl is null)
            return null;

        FrameworkElement? bestContainer = null;
        var bestTop = double.PositiveInfinity;
        var visibleTop = 0d;
        if (itemsControl is DataGrid &&
            FindVisualChild<System.Windows.Controls.Primitives.DataGridColumnHeadersPresenter>(itemsControl) is { } headers)
        {
            visibleTop = headers.TransformToAncestor(itemsControl)
                .TransformBounds(new Rect(0, 0, headers.ActualWidth, headers.ActualHeight))
                .Bottom;
        }
        var candidates = itemsControl is DataGrid
            ? FindVisualChildren<DataGridRow>(itemsControl).Cast<FrameworkElement>()
            : FindVisualChildren<ListBoxItem>(itemsControl).Cast<FrameworkElement>();
        foreach (var container in candidates)
        {
            if (container.DataContext is not ContentRow)
                continue;

            var bounds = container.TransformToAncestor(itemsControl)
                .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            if (bounds.Bottom <= visibleTop || bounds.Top >= itemsControl.ActualHeight)
                continue;
            if (bounds.Top + 0.5 >= visibleTop && bounds.Top < bestTop)
            {
                bestTop = bounds.Top;
                bestContainer = container;
            }
        }

        return bestContainer?.DataContext as ContentRow;
    }

    private void AlphabetIndexBorder_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingAlphabetIndex = true;
        AlphabetIndexBorder.CaptureMouse();
        ScrollToAlphabetPosition(e.GetPosition(AlphabetIndexPanel));
        e.Handled = true;
    }

    private void AlphabetIndexBorder_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingAlphabetIndex || e.LeftButton != MouseButtonState.Pressed)
            return;
        ScrollToAlphabetPosition(e.GetPosition(AlphabetIndexPanel));
        e.Handled = true;
    }

    private void AlphabetIndexBorder_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingAlphabetIndex)
            return;
        ScrollToAlphabetPosition(e.GetPosition(AlphabetIndexPanel));
        _isDraggingAlphabetIndex = false;
        AlphabetIndexBorder.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ScrollToAlphabetPosition(System.Windows.Point point)
    {
        if (AlphabetIndexPanel.Children.Count == 0 || AlphabetIndexPanel.ActualHeight <= 0)
            return;

        var hit = AlphabetIndexPanel.InputHitTest(point) as DependencyObject;
        var button = FindAncestor<Button>(hit);
        if (button is null)
        {
            button = AlphabetIndexPanel.Children
                .OfType<Button>()
                .OrderBy(candidate =>
                {
                    var top = candidate.TranslatePoint(new System.Windows.Point(0, 0), AlphabetIndexPanel).Y;
                    return Math.Abs(point.Y - (top + candidate.ActualHeight / 2));
                })
                .FirstOrDefault();
        }

        if (button is { IsEnabled: true, Tag: ContentRow row })
            ScrollToAlphabetRow(row);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        => FindVisualChildren<T>(parent).FirstOrDefault();

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualChildren<T>(child))
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

    private async void TrackFilterButton_OnClick(object sender, RoutedEventArgs e)
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
            Style = (Style)FindResource("TrackFilterExpanderStyle")
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
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new TextBlock
        {
            Text = label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (WpfBrush)FindResource("AppPrimaryTextBrush")
        });
        var countText = new TextBlock
        {
            Text = count.ToString("N0"),
            Margin = new Thickness(16, 0, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Foreground = (WpfBrush)FindResource("AppMutedTextBrush")
        };
        Grid.SetColumn(countText, 1);
        content.Children.Add(countText);

        var checkBox = new WpfCheckBox
        {
            Content = content,
            IsChecked = isChecked,
            Margin = new Thickness(0, 6, 0, 6),
            Foreground = (WpfBrush)FindResource("AppPrimaryTextBrush"),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            MinWidth = 590
        };
        checkBox.Checked += async (_, _) => await OnTrackFilterChangedAsync(update, true);
        checkBox.Unchecked += async (_, _) => await OnTrackFilterChangedAsync(update, false);
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

    private void SaveSmartPlaylistButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new NewPlaylistDialog { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.PlaylistName))
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

    private async Task ShowSearchResultsAsync(string query)
    {
        LyricsView.Visibility = Visibility.Collapsed;
        ArtistInfoView.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(query))
        {
            if (NavListBox.SelectedItem is ListBoxItem { Tag: string tag })
                await ShowTopLevelViewAsync(tag);
            return;
        }

        ResetDrilldownState();
        UpdateAlphabetIndex(null, false);
        ContentTitleTextBlock.Text = LocalizationManager.Current.Search;
        AlbumViewModeBorder.Visibility = Visibility.Collapsed;
        TrackFilterButton.Visibility = Visibility.Collapsed;
        ContentDataGrid.Visibility = Visibility.Collapsed;
        FolderTreeView.Visibility = Visibility.Collapsed;
        AlbumArtworkListBox.Visibility = Visibility.Collapsed;
        ArtistArtworkListBox.Visibility = Visibility.Collapsed;
        SearchResultsScrollViewer.Visibility = Visibility.Visible;
        DashboardScrollViewer.Visibility = Visibility.Collapsed;
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
        textBlock.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            (LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 90));
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
        string entityType)
    {
        var buttonFactory = new FrameworkElementFactory(typeof(Button));
        buttonFactory.SetBinding(ContentControl.ContentProperty, new Binding(property));
        buttonFactory.SetBinding(FrameworkElement.TagProperty, new Binding("."));
        buttonFactory.SetValue(FrameworkElement.StyleProperty, FindResource("EntityLinkButtonStyle"));
        RoutedEventHandler clickHandler = entityType == "Artist"
            ? ArtistLinkButton_OnClick
            : AlbumLinkButton_OnClick;
        buttonFactory.AddHandler(Button.ClickEvent, clickHandler);

        return new DataGridTemplateColumn
        {
            Header = header,
            Width = star
                ? new DataGridLength(1, DataGridLengthUnitType.Star)
                : new DataGridLength(width),
            CellTemplate = new DataTemplate { VisualTree = buttonFactory }
        };
    }

    private static ImageSource? CreateArtworkImage(string? path, int decodeWidth, bool ignoreCache = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (ignoreCache)
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();
            return image;
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

    private async void AlbumViewModeRadioButton_OnChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _updatingViewMode)
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

        ContentDataGrid.Visibility = artworkMode ? Visibility.Collapsed : Visibility.Visible;
        AlbumArtworkListBox.Visibility = tag == "Albums" && artworkMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        ArtistArtworkListBox.Visibility = tag == "Artists" && artworkMode
            ? Visibility.Visible
            : Visibility.Collapsed;

        var rows = await Task.Run(() => QueryRows(tag));
        ApplyColumns(tag);
        ContentDataGrid.ItemsSource = rows;
        if (tag == "Albums")
            AlbumArtworkListBox.ItemsSource = rows;
        else
            ArtistArtworkListBox.ItemsSource = rows;
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
    }

    private void SetViewModeButtons(bool artworkMode)
    {
        _updatingViewMode = true;
        AlbumArtworkViewRadioButton.IsChecked = artworkMode;
        AlbumTableViewRadioButton.IsChecked = !artworkMode;
        _updatingViewMode = false;
    }

    private void AlbumArtworkItem_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ContentRow row })
            EnsureArtworkHydrated(row);
    }

    private async void ArtistArtworkItem_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: ContentRow row })
            return;
        EnsureArtworkHydrated(row);
        await EnsureArtistProfileAsync(row);
    }

    private void ContentDataGrid_OnLoadingRow(object sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not ContentRow row)
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
                Add("#",        nameof(ContentRow.Nr),     44,  right: true);
                Add(LocalizationManager.Current.Title,    nameof(ContentRow.Title),  0,   star: true);
                AddEntityLink(LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 180, false, "Artist");
                AddEntityLink(LocalizationManager.Current.Album, nameof(ContentRow.Album), 160, false, "Album");
                Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 70, right: true);
                break;
            default: // Tracks
                AddFavorite();
                Add(LocalizationManager.Current.Title,    nameof(ContentRow.Title),  0,   star: true);
                AddEntityLink(LocalizationManager.Current.Artist, nameof(ContentRow.Artist), 180, false, "Artist");
                AddEntityLink(LocalizationManager.Current.Album, nameof(ContentRow.Album), 160, false, "Album");
                Add(LocalizationManager.Current.Genre,    nameof(ContentRow.Genre),  100);
                Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 70, right: true);
                Add(LocalizationManager.Current.Format,   nameof(ContentRow.Format), 70);
                break;
        }

        void AddEntityLink(string header, string prop, double width, bool star, string entityType)
            => ContentDataGrid.Columns.Add(CreateEntityLinkColumn(header, prop, width, star, entityType));

        void Add(string header, string prop, double width, bool star = false, bool right = false)
        {
            var col = new DataGridTextColumn
            {
                Header  = header,
                Binding = new Binding(prop),
                Width   = star ? new DataGridLength(1, DataGridLengthUnitType.Star)
                               : new DataGridLength(width)
            };
            if (right)
            {
                col.ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters = { new Setter(TextBlock.HorizontalAlignmentProperty,
                                          System.Windows.HorizontalAlignment.Right) }
                };
            }
            ContentDataGrid.Columns.Add(col);
        }

        void AddFavorite()
        {
            var buttonFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetBinding(ContentControl.ContentProperty, new Binding(nameof(ContentRow.FavoriteGlyph)));
            buttonFactory.SetBinding(FrameworkElement.TagProperty, new Binding("."));
            buttonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(FavoriteButton_OnClick));
            buttonFactory.SetValue(Button.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            buttonFactory.SetValue(Button.BorderThicknessProperty, new Thickness(0));
            buttonFactory.SetValue(Button.ForegroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6C, 0x63, 0xFF)));
            buttonFactory.SetValue(Button.CursorProperty, WpfCursors.Hand);
            buttonFactory.SetValue(Button.FontSizeProperty, 16d);

            ContentDataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(42),
                CellTemplate = new DataTemplate { VisualTree = buttonFactory }
            });
        }

        void AddThumbnail()
        {
            var imageFactory = new FrameworkElementFactory(typeof(WpfImage));
            imageFactory.SetBinding(WpfImage.SourceProperty, new Binding(nameof(ContentRow.Thumbnail)));
            imageFactory.SetValue(FrameworkElement.WidthProperty, 32d);
            imageFactory.SetValue(FrameworkElement.HeightProperty, 32d);
            imageFactory.SetValue(WpfImage.StretchProperty, Stretch.UniformToFill);
            ContentDataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(64),
                CellTemplate = new DataTemplate { VisualTree = imageFactory },
                CellStyle = new Style(typeof(DataGridCell))
                {
                    Setters =
                    {
                        new Setter(DataGridCell.PaddingProperty, new Thickness(0)),
                        new Setter(DataGridCell.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Center),
                        new Setter(DataGridCell.VerticalContentAlignmentProperty, System.Windows.VerticalAlignment.Center)
                    }
                }
            });
        }

        void AddArtistInfo()
        {
            var buttonFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetValue(ContentControl.ContentProperty, "ℹ");
            buttonFactory.SetBinding(FrameworkElement.TagProperty, new Binding("."));
            buttonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(ArtistInfoListButton_OnClick));
            buttonFactory.SetValue(Button.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            buttonFactory.SetValue(Button.BorderThicknessProperty, new Thickness(0));
            buttonFactory.SetValue(Button.ForegroundProperty, new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x6C, 0x63, 0xFF)));
            buttonFactory.SetValue(Button.CursorProperty, WpfCursors.Hand);
            buttonFactory.SetValue(Button.FontSizeProperty, 16d);
            buttonFactory.SetValue(Button.PaddingProperty, new Thickness(0));
            buttonFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            ContentDataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(44),
                CellTemplate = new DataTemplate { VisualTree = buttonFactory },
                CellStyle = new Style(typeof(DataGridCell))
                {
                    Setters =
                    {
                        new Setter(DataGridCell.PaddingProperty, new Thickness(0)),
                        new Setter(DataGridCell.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Center),
                        new Setter(DataGridCell.VerticalContentAlignmentProperty, System.Windows.VerticalAlignment.Center)
                    }
                }
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

        public IEnumerable<string> AutoRoots() =>
            _allDirs
                .Where(d => { var p = Path.GetDirectoryName(d); return p is null || !_allDirs.Contains(p); })
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
    }

    private void BuildFolderTree(List<TrackLite> tracks)
    {
        FolderTreeView.Items.Clear();
        if (tracks.Count == 0) return;

        var tree  = new FolderTree(tracks);
        var roots = _settings.LibraryPaths.Where(p => !string.IsNullOrWhiteSpace(p))
                              .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                              .ToList();
        if (roots.Count == 0)
            roots = [.. tree.AutoRoots()];

        foreach (var root in roots)
            FolderTreeView.Items.Add(CreateDirItemLazy(root, tree, isRoot: true));
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

        // Platzhalter → WPF zeigt den Aufklapp-Pfeil
        var placeholder = new TreeViewItem();
        item.Items.Add(placeholder);

        item.Expanded += (_, _) =>
        {
            if (item.Items.Count == 1 && item.Items[0] == placeholder)
                PopulateDirNode(item, dirPath, tree);
        };

        // Wurzel-Knoten sofort aufklappen (löst den Expanded-Handler aus)
        if (isRoot) item.IsExpanded = true;

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

    private async void FolderTreeView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
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

    private void FolderTreeView_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var treeItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
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

    private async void ContentDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (ContentDataGrid.SelectedItem is not ContentRow row)
            return;
        await HandleContentRowDoubleClickAsync(row);
    }

    private async void AlbumArtworkListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (AlbumArtworkListBox.SelectedItem is not ContentRow row)
            return;
        await HandleContentRowDoubleClickAsync(row);
    }

    private async void ArtistArtworkListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (ArtistArtworkListBox.SelectedItem is ContentRow row)
            await HandleContentRowDoubleClickAsync(row);
    }

    private async void SearchTracksDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (SearchTracksDataGrid.SelectedItem is not ContentRow row)
            return;

        var allRows = (SearchTracksDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        await PlayTrackFromRowsAsync(row, allRows);
    }

    private async void SearchAlbumsDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (SearchAlbumsDataGrid.SelectedItem is ContentRow { Id: long albumId } row)
        {
            _navigationStack.Push(new NavigationState("Search", albumId, null, null, SearchTextBox.Text));
            await ShowAlbumTracksAsync(albumId, row.Title ?? LocalizationManager.Current.Unknown);
        }
    }

    private async void SearchArtistsDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (SearchArtistsDataGrid.SelectedItem is ContentRow { Id: long artistId } row)
        {
            _navigationStack.Push(new NavigationState("Search", artistId, null, null, SearchTextBox.Text));
            await ShowArtistAlbumsAsync(artistId, row.Title ?? LocalizationManager.Current.Unknown);
        }
    }

    private async void ArtistLinkButton_OnClick(object sender, RoutedEventArgs e)
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

    private async void AlbumLinkButton_OnClick(object sender, RoutedEventArgs e)
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

    private async void NowPlayingArtistButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_currentArtistId is long artistId)
            await ShowArtistAlbumsAsync(
                artistId,
                _currentArtistName ?? LocalizationManager.Current.Unknown);
    }

    private async Task HandleContentRowDoubleClickAsync(ContentRow row)
    {

        if (row.EntityType == "Artist" && row.Id is long artistId)
        {
            await ShowArtistAlbumsAsync(artistId, row.Title ?? "(Unbekannt)");
            return;
        }

        if (AlbumViewModeBorder.Visibility == Visibility.Visible && row.Id is long albumId)
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
        if (_navigationStack.Count == 0 || _navigationStack.Peek().View != "Search")
            _navigationStack.Push(new NavigationState("Albums", albumId, _activeArtistFilterId, _activeArtistFilterName));
        _activeAlbumFilterId = albumId;
        _activeAlbumFilterTitle = albumTitle;
        _showAllAlbumTracks = false;
        _updatingAlbumTrackScope = true;
        ShowAllAlbumTracksCheckBox.IsChecked = false;
        ShowAllAlbumTracksCheckBox.Visibility = _activeArtistFilterId.HasValue
            ? Visibility.Visible
            : Visibility.Collapsed;
        _updatingAlbumTrackScope = false;
        ContentTitleTextBlock.Text = $"{LocalizationManager.Current.Tracks} · {albumTitle}";
        AlbumViewModeBorder.Visibility = Visibility.Collapsed;
        ContentDataGrid.Visibility = Visibility.Visible;
        AlbumArtworkListBox.Visibility = Visibility.Collapsed;
        ArtistArtworkListBox.Visibility = Visibility.Collapsed;
        FolderTreeView.Visibility = Visibility.Collapsed;
        SearchResultsScrollViewer.Visibility = Visibility.Collapsed;
        DashboardScrollViewer.Visibility = Visibility.Collapsed;
        UpdateAlphabetIndex(null, false);

        await ReloadVisibleAlbumTracksAsync();
        BackButton.Visibility = Visibility.Visible;
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

    private async void ShowAllAlbumTracksCheckBox_OnChanged(object sender, RoutedEventArgs e)
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
        AlbumDetailHeader.Visibility = Visibility.Visible;
    }

    private void HideAlbumDetailHeader()
    {
        AlbumDetailHeader.Visibility = Visibility.Collapsed;
        AlbumDetailHeader.DataContext = null;
        ShowAllAlbumTracksCheckBox.Visibility = Visibility.Collapsed;
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
        if (_navigationStack.Count == 0 || _navigationStack.Peek().View != "Search")
            _navigationStack.Push(new NavigationState("Artists", artistId, null, null));
        _activeArtistFilterId = artistId;
        _activeArtistFilterName = artistName;
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        ContentTitleTextBlock.Text = $"{LocalizationManager.Current.Albums} · {artistName}";
        AlbumViewModeBorder.Visibility = Visibility.Visible;
        SetViewModeButtons(_showAlbumArtworkView);
        ContentDataGrid.Visibility = _showAlbumArtworkView ? Visibility.Collapsed : Visibility.Visible;
        AlbumArtworkListBox.Visibility = _showAlbumArtworkView ? Visibility.Visible : Visibility.Collapsed;
        ArtistArtworkListBox.Visibility = Visibility.Collapsed;
        FolderTreeView.Visibility = Visibility.Collapsed;
        SearchResultsScrollViewer.Visibility = Visibility.Collapsed;
        HideAlbumDetailHeader();

        var rows = await Task.Run(() => QueryRows("Albums"));
        ApplyColumns("Albums");
        ContentDataGrid.ItemsSource = rows;
        AlbumArtworkListBox.ItemsSource = rows;
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
        BackButton.Visibility = Visibility.Visible;
    }

    private async void SearchCoverButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row })
            return;

        await OpenCoverSearchAsync(row);
    }

    private async void DeleteCoverMenuItem_OnClick(object sender, RoutedEventArgs e)
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

    private async void ReassignCoverMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ContentRow row })
            return;

        await OpenCoverSearchAsync(row);
    }

    private async Task OpenCoverSearchAsync(ContentRow row)
    {
        if (row.Id is not long albumId)
            return;

        var dialog = new CoverSearchWindow(row.Title ?? string.Empty) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedResult is not { } selected)
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
        AlbumArtworkListBox.ItemsSource = rows;
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
    }

    private async void BackButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (LyricsView.Visibility == Visibility.Visible ||
            ArtistInfoView.Visibility == Visibility.Visible)
        {
            CloseNowPlayingDetailViews();
            return;
        }

        if (_navigationStack.Count == 0)
            return;

        var state = _navigationStack.Pop();
        _activeArtistFilterId = state.ArtistFilterId;
        _activeArtistFilterName = state.ArtistFilterName;
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        HideAlbumDetailHeader();

        switch (state.View)
        {
            case "Dashboard":
                ContentTitleTextBlock.Text = LocalizationManager.Current.Dashboard;
                UpdateAlphabetIndex(null, false);
                ContentDataGrid.Visibility = Visibility.Collapsed;
                FolderTreeView.Visibility  = Visibility.Collapsed;
                AlbumArtworkListBox.Visibility = Visibility.Collapsed;
                ArtistArtworkListBox.Visibility = Visibility.Collapsed;
                DashboardScrollViewer.Visibility = Visibility.Visible;
                await ShowDashboardAsync();
                return;

            case "Search":
                SearchTextBox.Text = state.SearchQuery ?? string.Empty;
                await ShowSearchResultsAsync(state.SearchQuery ?? string.Empty);
                return;

            case "Artists":
                ContentTitleTextBlock.Text = LocalizationManager.Current.Artists;
                AlbumViewModeBorder.Visibility = Visibility.Visible;
                SetViewModeButtons(_showArtistArtworkView);
                ContentDataGrid.Visibility = _showArtistArtworkView ? Visibility.Collapsed : Visibility.Visible;
                AlbumArtworkListBox.Visibility = Visibility.Collapsed;
                ArtistArtworkListBox.Visibility = _showArtistArtworkView ? Visibility.Visible : Visibility.Collapsed;
                var artists = await Task.Run(() => QueryRows("Artists"));
                ApplyColumns("Artists");
                ContentDataGrid.ItemsSource = artists;
                ArtistArtworkListBox.ItemsSource = artists;
                UpdateAlphabetIndex(artists, true);
                ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(artists.Count);
                RestoreSelection(artists, state.SelectedId);
                break;

            case "Albums":
                ContentTitleTextBlock.Text = _activeArtistFilterId is long
                    ? $"{LocalizationManager.Current.Albums} · {_activeArtistFilterName}"
                    : LocalizationManager.Current.Albums;
                AlbumViewModeBorder.Visibility = Visibility.Visible;
                SetViewModeButtons(_showAlbumArtworkView);
                ContentDataGrid.Visibility = _showAlbumArtworkView ? Visibility.Collapsed : Visibility.Visible;
                AlbumArtworkListBox.Visibility = _showAlbumArtworkView ? Visibility.Visible : Visibility.Collapsed;
                var albums = await Task.Run(() => QueryRows("Albums"));
                ApplyColumns("Albums");
                ContentDataGrid.ItemsSource = albums;
                AlbumArtworkListBox.ItemsSource = albums;
                ArtistArtworkListBox.Visibility = Visibility.Collapsed;
                UpdateAlphabetIndex(albums, true);
                ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(albums.Count);
                RestoreSelection(albums, state.SelectedId);
                break;
        }

        BackButton.Visibility = _navigationStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RestoreSelection(List<ContentRow> rows, long? selectedId)
    {
        if (selectedId is not long id)
            return;
        var row = rows.FirstOrDefault(r => r.Id == id);
        if (row is null)
            return;
        ContentDataGrid.SelectedItem = row;
        ContentDataGrid.ScrollIntoView(row);
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

    private void NowPlayingFavoriteButton_OnClick(object sender, RoutedEventArgs e)
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
                ContentDataGrid.Items.Refresh();
            }
        }
    }

    private void FavoriteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row } || row.Id is not long id)
            return;

        row.IsFavorite = !row.IsFavorite;
        using var db = AudioDatabase.OpenDefault();
        if (row.EntityType == "Artist")
            db.SetArtistFavorite(id, row.IsFavorite);
        else if (row.EntityType == "Album")
            db.SetAlbumFavorite(id, row.IsFavorite);
        else
            db.SetTrackFavorite(id, row.IsFavorite);

        ContentDataGrid.Items.Refresh();
        AlbumArtworkListBox.Items.Refresh();
        ArtistArtworkListBox.Items.Refresh();
        e.Handled = true;
    }

    // ------------------------------------------------------------------
    // Playlist-Kontextmenü
    // ------------------------------------------------------------------

    private void ContentDataGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dataRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (dataRow?.DataContext is not ContentRow contentRow)
        {
            ContentDataGrid.ContextMenu = null;
            return;
        }

        ContentDataGrid.SelectedItem = contentRow;

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

    private void SearchTracksDataGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dataRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (dataRow?.DataContext is not ContentRow contentRow || string.IsNullOrEmpty(contentRow.FilePath))
        {
            SearchTracksDataGrid.ContextMenu = null;
            return;
        }
        SearchTracksDataGrid.SelectedItem = contentRow;
        SearchTracksDataGrid.ContextMenu = BuildPlaylistContextMenu(GetPathsForRow(contentRow));
    }

    private void SearchAlbumsDataGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dataRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (dataRow?.DataContext is not ContentRow contentRow || contentRow.Id is null)
        {
            SearchAlbumsDataGrid.ContextMenu = null;
            return;
        }
        SearchAlbumsDataGrid.SelectedItem = contentRow;
        SearchAlbumsDataGrid.ContextMenu = BuildPlaylistContextMenu(GetPathsForRow(contentRow));
    }

    private void AlbumArtworkContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        var row = (menu.PlacementTarget as FrameworkElement)?.DataContext as ContentRow;
        if (row?.Id is null) return;

        // Vorherige dynamisch hinzugefügte Playlist-Einträge entfernen (erste 2: DeleteCover, ReassignCover)
        while (menu.Items.Count > 2)
            menu.Items.RemoveAt(menu.Items.Count - 1);

        AppendPlaylistItems(menu, GetPathsForRow(row));
    }

    private ContextMenu BuildPlaylistContextMenu(IReadOnlyList<string> paths)
    {
        var menu = new ContextMenu();
        if (System.Windows.Application.Current.Resources[typeof(ContextMenu)] is Style ctxStyle)
            menu.Style = ctxStyle;
        AppendPlaylistItems(menu, paths);
        return menu;
    }

    private void AppendPlaylistItems(ItemsControl menu, IReadOnlyList<string> paths)
    {
        var miStyle  = System.Windows.Application.Current.Resources[typeof(MenuItem)]  as Style;
        var sepStyle = System.Windows.Application.Current.Resources[typeof(Separator)] as Style;

        var header = new MenuItem
        {
            Header           = LocalizationManager.Current.AddToPlaylist,
            IsHitTestVisible = false,
            Focusable        = false,
            FontSize         = 11,
            FontWeight       = FontWeights.SemiBold,
            Foreground       = new System.Windows.Media.SolidColorBrush(
                                   System.Windows.Media.Color.FromRgb(0x6C, 0x63, 0xFF))
        };
        if (miStyle != null) header.Style = miStyle;
        menu.Items.Add(header);

        var sep0 = new Separator();
        if (sepStyle != null) sep0.Style = sepStyle;
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
            if (miStyle != null) item.Style = miStyle;
            item.Click += PlaylistMenuItem_OnClick;
            menu.Items.Add(item);
        }

        if (playlists.Count > 0)
        {
            var sep1 = new Separator();
            if (sepStyle != null) sep1.Style = sepStyle;
            menu.Items.Add(sep1);
        }

        var newItem = new MenuItem { Header = LocalizationManager.Current.NewPlaylist, Tag = paths };
        if (miStyle != null) newItem.Style = miStyle;
        newItem.Click += NewPlaylistMenuItem_OnClick;
        menu.Items.Add(newItem);
    }

    private void PlaylistMenuItem_OnClick(object sender, RoutedEventArgs e)
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

    private void NewPlaylistMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: IReadOnlyList<string> paths })
            return;

        var dialog = new NewPlaylistDialog { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.PlaylistName))
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

    private void NavListBox_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.Tag is not string tag || !tag.StartsWith("Playlist:") ||
            !long.TryParse(tag.AsSpan("Playlist:".Length), out long playlistId))
        {
            NavListBox.ContextMenu = null;
            return;
        }

        item.IsSelected = true;
        string name;
        try { using var db = AudioDatabase.OpenDefault(); name = db.GetPlaylistById(playlistId)?.Name ?? string.Empty; }
        catch { name = string.Empty; }
        NavListBox.ContextMenu = BuildDeletePlaylistContextMenu(playlistId, name);
    }

    private ContextMenu BuildDeletePlaylistContextMenu(long playlistId, string playlistName)
    {
        var menu = new ContextMenu();
        if (System.Windows.Application.Current.Resources[typeof(ContextMenu)] is Style cs)
            menu.Style = cs;

        var miStyle  = System.Windows.Application.Current.Resources[typeof(MenuItem)]  as Style;
        var sepStyle = System.Windows.Application.Current.Resources[typeof(Separator)] as Style;

        var header = new MenuItem
        {
            Header           = playlistName,
            IsHitTestVisible = false,
            Focusable        = false,
            FontSize         = 11,
            FontWeight       = FontWeights.SemiBold,
            Foreground       = new System.Windows.Media.SolidColorBrush(
                                   System.Windows.Media.Color.FromRgb(0x6C, 0x63, 0xFF))
        };
        if (miStyle != null) header.Style = miStyle;
        menu.Items.Add(header);

        var sep = new Separator();
        if (sepStyle != null) sep.Style = sepStyle;
        menu.Items.Add(sep);

        var deleteItem = new MenuItem
        {
            Header     = LocalizationManager.Current.DeletePlaylist,
            Tag        = playlistId,
            Foreground = new System.Windows.Media.SolidColorBrush(
                             System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B))
        };
        if (miStyle != null) deleteItem.Style = miStyle;
        deleteItem.Click += DeletePlaylistMenuItem_OnClick;
        menu.Items.Add(deleteItem);

        return menu;
    }

    private void DeletePlaylistMenuItem_OnClick(object sender, RoutedEventArgs e)
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
        if (System.Windows.Application.Current.Resources[typeof(ContextMenu)] is Style cs)
            menu.Style = cs;

        var miStyle  = System.Windows.Application.Current.Resources[typeof(MenuItem)]  as Style;
        var sepStyle = System.Windows.Application.Current.Resources[typeof(Separator)] as Style;

        var header = new MenuItem
        {
            Header           = LocalizationManager.Current.RemoveFromPlaylist,
            IsHitTestVisible = false,
            Focusable        = false,
            FontSize         = 11,
            FontWeight       = FontWeights.SemiBold,
            Foreground       = new System.Windows.Media.SolidColorBrush(
                                   System.Windows.Media.Color.FromRgb(0x6C, 0x63, 0xFF))
        };
        if (miStyle != null) header.Style = miStyle;
        menu.Items.Add(header);

        var sep = new Separator();
        if (sepStyle != null) sep.Style = sepStyle;
        menu.Items.Add(sep);

        var removeItem = new MenuItem
        {
            Header = LocalizationManager.Current.RemoveFromPlaylist,
            Tag    = new RemovePlaylistEntryTag(playlistEntryId)
        };
        if (miStyle != null) removeItem.Style = miStyle;
        removeItem.Click += RemoveFromPlaylistMenuItem_OnClick;
        menu.Items.Add(removeItem);

        return menu;
    }

    private async void RemoveFromPlaylistMenuItem_OnClick(object sender, RoutedEventArgs e)
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
    // Wiedergabe
    // ------------------------------------------------------------------

    private async void PlayButton_OnClick(object sender, RoutedEventArgs e)
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
        try { await StartPlaybackAsync(_currentFilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private async Task StartPlaybackAsync(string filePath)
    {
        StopPlayback();
        _currentFilePath = filePath;
        _playedQueuePaths.Add(filePath);
        _playbackCts     = new CancellationTokenSource();

        var ext = Path.GetExtension(filePath);
        IAudioPlayer player;
        AudioFileInfo info;

        if (_settings.OutputBackend == OutputBackend.Asio)
        {
            if (string.IsNullOrWhiteSpace(_settings.SelectedDriverName))
            {
                StatusTextBlock.Text = LocalizationManager.Current.SelectAsioDevice;
                return;
            }
            if (ext.Equals(".dsf", StringComparison.OrdinalIgnoreCase))
                (player, info) = await DsfAudioPlayer.CreateAsync(filePath, _settings.SelectedDriverName, _playbackCts.Token);
            else if (ext.Equals(".dff", StringComparison.OrdinalIgnoreCase))
                (player, info) = await DffAudioPlayer.CreateAsync(filePath, _settings.SelectedDriverName, _playbackCts.Token);
            else
                (player, info) = await FfmpegAudioPlayer.CreateAsync(filePath, _settings.SelectedDriverName, _playbackCts.Token);
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
        PlayButton.IsEnabled   = false;
        PlayButton.IsEnabled   = true;
        SetPlayPauseIcon(isPlaying: true);
        RefreshQueueNavigationButtons();
        PositionSlider.IsEnabled = player.CanSeek;
        DurationTextBlock.Text = FormatTime(player.Duration);
        _transportTimer.Start();

        // Now-playing anzeigen
        var filename = Path.GetFileNameWithoutExtension(filePath);
        NowPlayingTitleBlock.Text  = filename;
        NowPlayingArtistBlock.Text = SelectedDriverTextBlock.Text;
        FileInfoTextBlock.Text     = info.IsDsd && info.ContainerName is "dsf" or "dff"
            ? $"{info.ContainerName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  DSD nativ"
            : info.IsDsd
                ? $"DSD → PCM  ·  {info.OutputSampleRate:N0} Hz"
                : $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  {info.Channels} ch";
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
            NowPlayingArtistButton.IsEnabled = false;
            _currentTrackIsFavorite = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
        }
        UpdateNowPlayingFavoriteButton();
        _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);

        var outputName = _settings.OutputBackend == OutputBackend.Asio
            ? _settings.SelectedDriverName
            : _settings.SelectedWasapiDeviceName;
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.PlaybackThrough, outputName);

        try
        {
            using var db = AudioDatabase.OpenDefault();
            _currentPlayHistoryId = db.RecordPlaybackStart(
                filePath,
                db.GetTrackIdByPath(filePath),
                player.Duration.TotalSeconds > 0 ? player.Duration.TotalSeconds : null);
        }
        catch
        {
            _currentPlayHistoryId = null;
        }

        await player.WaitForCompletionAsync();

        if (_player == player)
        {
            RecordPlaybackEnd(completed: true);
            if (!await TryPlayNextAsync())
            {
                StopPlayback();
                StatusTextBlock.Text = LocalizationManager.Current.PlaybackFinished;
            }
        }
    }

    private async void PreviousButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryMoveToPreviousQueueIndex())
            return;

        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private async void NextButton_OnClick(object sender, RoutedEventArgs e)
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
        LyricsButton.IsEnabled = false;
        ArtistInfoButton.IsEnabled = false;
        ArtistInfoView.Visibility = Visibility.Collapsed;
        UpdateNowPlayingFavoriteButton();
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

    private void ShuffleButton_OnClick(object sender, RoutedEventArgs e)
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
                ? System.Windows.Media.Color.FromRgb(0x6C, 0x63, 0xFF)
                : System.Windows.Media.Color.FromRgb(0x25, 0x26, 0x40));
        ShuffleButton.Foreground = _shuffleEnabled
            ? System.Windows.Media.Brushes.White
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xEE));
    }

    private void TransportControlsHeaderGrid_OnLayoutChanged(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(UpdateResponsivePlaybackControls);

    private void TransportControlsHeaderGrid_OnLayoutChanged(object sender, SizeChangedEventArgs e) =>
        Dispatcher.BeginInvoke(UpdateResponsivePlaybackControls);

    private void UpdateResponsivePlaybackControls()
    {
        const double gap = 12;
        var centeredPlaybackLeft =
            (TransportControlsHeaderGrid.ActualWidth - PlaybackControlsPanel.ActualWidth) / 2;
        var requiredLeft = TransportActionPanel.ActualWidth + gap;
        var shift = Math.Max(0, requiredLeft - centeredPlaybackLeft);
        var maximumShift = Math.Max(
            0,
            TransportControlsHeaderGrid.ActualWidth -
            PlaybackControlsPanel.ActualWidth -
            centeredPlaybackLeft);

        PlaybackControlsTranslate.X = Math.Min(shift, maximumShift);
    }

    private void SetPlayPauseIcon(bool isPlaying)
    {
        PlayPauseIcon.Data = Geometry.Parse(isPlaying
            ? "M 4 2 H 7 V 14 H 4 Z M 9 2 H 12 V 14 H 9 Z"
            : "M 4 2 L 14 8 L 4 14 Z");
    }

    // ------------------------------------------------------------------
    // Pause / Seek / Volume
    // ------------------------------------------------------------------

    private async void PositionSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_player is null || !_player.CanSeek) return;
        await _player.SeekAsync(TimeSpan.FromSeconds(PositionSlider.Value));
        _isSeekingWithSlider = false;
        RefreshTransport();
    }

    private void PositionSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_player?.CanSeek != true || PositionSlider.ActualWidth <= 0)
            return;

        _isSeekingWithSlider = true;
        var position = e.GetPosition(PositionSlider);
        var ratio = Math.Clamp(position.X / PositionSlider.ActualWidth, 0, 1);
        PositionSlider.Value =
            PositionSlider.Minimum +
            ratio * (PositionSlider.Maximum - PositionSlider.Minimum);
    }

    private void PositionSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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
        UpdateActiveLyric(_player.Position);
    }

    private void LyricsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ArtistInfoView.Visibility = Visibility.Collapsed;
        LyricsView.Visibility = LyricsView.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateBackButtonForDetailView();
        if (LyricsView.Visibility == Visibility.Visible &&
            _lyricLines.Count == 0 &&
            !string.IsNullOrWhiteSpace(_currentFilePath))
        {
            _ = LoadLyricsForTrackAsync(_currentFilePath, forceRefresh: false);
        }
    }

    private void CloseLyricsButton_OnClick(object sender, RoutedEventArgs e)
        => CloseNowPlayingDetailViews();

    private async void RefreshLyricsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;
        await LoadLyricsForTrackAsync(_currentFilePath, forceRefresh: true);
    }

    private void SearchLyricsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        TrackRecord? track;
        using (var db = AudioDatabase.OpenDefault())
            track = db.GetByPath(_currentFilePath);
        if (track is null)
            return;

        var dialog = new LyricsSearchWindow(track.Title, track.Artist) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedResult is not { } selected)
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

    private async void ArtistInfoButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentArtistId is not long artistId || string.IsNullOrWhiteSpace(_currentArtistName))
            return;

        LyricsView.Visibility = Visibility.Collapsed;
        ArtistInfoView.Visibility = ArtistInfoView.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateBackButtonForDetailView();
        if (ArtistInfoView.Visibility == Visibility.Visible)
            await ShowArtistInfoAsync(artistId, forceRefresh: false);
    }

    private void CloseArtistInfoButton_OnClick(object sender, RoutedEventArgs e)
        => CloseNowPlayingDetailViews();

    private async void ArtistInfoTitleButton_OnClick(object sender, RoutedEventArgs e)
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

    private async void ArtistInfoListButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ContentRow row } && row.Id is long artistId)
        {
            e.Handled = true;
            LyricsView.Visibility = Visibility.Collapsed;
            ArtistInfoView.Visibility = Visibility.Visible;
            UpdateBackButtonForDetailView();
            await ShowArtistInfoAsync(artistId, forceRefresh: false);
        }
    }

    private void UpdateBackButtonForDetailView()
    {
        BackButton.Visibility =
            LyricsView.Visibility == Visibility.Visible ||
            ArtistInfoView.Visibility == Visibility.Visible ||
            _navigationStack.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void CloseNowPlayingDetailViews()
    {
        LyricsView.Visibility = Visibility.Collapsed;
        ArtistInfoView.Visibility = Visibility.Collapsed;
        BackButton.Visibility = _navigationStack.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void RefreshArtistInfoButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_artistInfoDisplayedId is long artistId)
            await ShowArtistInfoAsync(artistId, forceRefresh: true);
    }

    private async void SearchArtistImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_artistInfoDisplayedId is not long artistId)
            return;

        ArtistInfo? artist;
        using (var db = AudioDatabase.OpenDefault())
            artist = db.GetArtistById(artistId);
        if (artist is null)
            return;

        var dialog = new ArtistImageSearchWindow(artist.Artist) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedResult is not { } selected)
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
            ArtistInfoImagePlaceholder.Visibility = ArtistInfoImage.Source is null
                ? Visibility.Visible
                : Visibility.Collapsed;
            ArtistInfoImageStatusText.Visibility = Visibility.Collapsed;
            await RefreshVisibleArtistRowAsync(artist);
        }
        catch
        {
            ArtistInfoImageStatusText.Text = LocalizationManager.Current.ArtistImageDownloadFailed;
            ArtistInfoImageStatusText.Visibility = Visibility.Visible;
        }
    }

    private async void EditArtistNameButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_artistInfoDisplayedId is not long artistId)
            return;

        ArtistInfo? artist;
        using (var db = AudioDatabase.OpenDefault())
            artist = db.GetArtistById(artistId);
        if (artist is null)
            return;

        var editDialog = new EditArtistNameDialog(artist.Artist) { Owner = this };
        if (editDialog.ShowDialog() != true)
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
                        matchingArtist.Artist)
                    {
                        Owner = this
                    };
                    if (mergeDialog.ShowDialog() != true)
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
            ArtistInfoStatusTextBlock.Visibility = Visibility.Visible;
        }
    }

    private async Task ReloadVisibleArtistListAsync()
    {
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: "Artists" })
            return;

        var rows = await Task.Run(() => QueryRows("Artists"));
        ApplyColumns("Artists");
        ContentDataGrid.ItemsSource = rows;
        ArtistArtworkListBox.ItemsSource = rows;
        UpdateAlphabetIndex(rows, true);
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
    }

    private void ArtistInfoSourceButton_OnClick(object sender, RoutedEventArgs e)
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
        ArtistInfoImagePlaceholder.Visibility = Visibility.Visible;
        ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoLoading;
        ArtistInfoStatusTextBlock.Visibility = Visibility.Visible;
        ArtistInfoBiographyTextBlock.Text = string.Empty;
        ArtistInfoSourceButton.Visibility = Visibility.Collapsed;
        ArtistInfoImageStatusText.Text = string.Empty;
        ArtistInfoImageStatusText.Visibility = Visibility.Collapsed;

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
                ArtistInfoImagePlaceholder.Visibility = Visibility.Visible;
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
                ArtistInfoImageStatusText.Visibility = Visibility.Visible;
            }
            else
            {
                ArtistInfoImagePlaceholder.Visibility = Visibility.Collapsed;
                ArtistInfoImageStatusText.Visibility = Visibility.Collapsed;
            }
            _artistInfoSourceUrl = artist.SourceUrl;
            ArtistInfoSourceButton.Content = _artistInfoSourceUrl?.Contains("last.fm") == true
                ? LocalizationManager.Current.ArtistInfoSourceLastFm
                : LocalizationManager.Current.ArtistInfoSource;
            ArtistInfoSourceButton.Visibility = string.IsNullOrWhiteSpace(_artistInfoSourceUrl)
                ? Visibility.Collapsed
                : Visibility.Visible;
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistInfoNotFound;
            ArtistInfoStatusTextBlock.Visibility =
                string.IsNullOrWhiteSpace(artist.Biography) && ArtistInfoImage.Source is null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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
                row.Artwork = CreateArtworkImage(profile.ImagePath, 600, ignoreCache: true);
                row.Thumbnail = CreateArtworkImage(profile.ImagePath, 96, ignoreCache: true);
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

    private Task RefreshVisibleArtistRowAsync(ArtistInfo artist)
    {
        var rows = ContentDataGrid.ItemsSource as IEnumerable<ContentRow>
            ?? ArtistArtworkListBox.ItemsSource as IEnumerable<ContentRow>;
        var row = rows?.FirstOrDefault(item => item.Id == artist.Id && item.EntityType == "Artist");
        if (row is null)
            return Task.CompletedTask;

        row.Biography = artist.Biography;
        row.SourceUrl = artist.SourceUrl;
        row.ProfileLanguage = artist.ProfileLanguage;
        row.ProfileFetchedAt = artist.ProfileFetchedAt;
        row.ImageIsManual = artist.ImageIsManual;
        row.ArtworkPath = artist.ImagePath;
        row.ThumbnailPath = artist.ImagePath;
        row.Artwork = CreateArtworkImage(artist.ImagePath, 600, ignoreCache: true);
        row.Thumbnail = CreateArtworkImage(artist.ImagePath, 96, ignoreCache: true);
        return Task.CompletedTask;
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
        LyricsStatusTextBlock.Visibility = hasLyrics ? Visibility.Collapsed : Visibility.Visible;
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
        LyricsStatusTextBlock.Visibility = Visibility.Visible;
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

    private void VolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings, paths =>
        {
            _settings.LibraryPaths = paths;
            _settingsStore.Save(_settings);
        })
        { Owner = this };

        if (window.ShowDialog() == true)
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
            _settingsStore.Save(_settings);
            ThemeManager.Apply(_settings.Theme);
            LocalizationManager.Apply(_settings.Language);
            ApplyArtistInfoSettings();
            RefreshSelectedDriverText();
            LoadNavPlaylists();

            StatusTextBlock.Text = _settings.OutputBackend switch
            {
                OutputBackend.Asio when string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
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

    private void AboutButton_OnClick(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
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
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("AppPrimaryTextBrush"),
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
            Background = (WpfBrush)FindResource("AppGridLineBrush"),
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
            Background = (WpfBrush)FindResource("AppContentBrush"),
            Foreground = (WpfBrush)FindResource("AppPrimaryTextBrush"),
            BorderBrush = (WpfBrush)FindResource("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            Cursor     = WpfCursors.Hand,
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
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };

        foreach (var album in albums)
            panel.Children.Add(BuildAlbumCard(album));

        scroll.Content = panel;
        DashboardPanel.Children.Add(scroll);
    }

    private FrameworkElement BuildAlbumCard(RecentAlbumInfo album)
    {
        var card = new Border
        {
            Width           = 140,
            Margin          = new Thickness(0, 0, 12, 0),
            Background      = (WpfBrush)FindResource("AppSurfaceBrush"),
            BorderBrush     = (WpfBrush)FindResource("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Cursor          = WpfCursors.Hand,
            ClipToBounds    = true
        };

        var stack = new StackPanel();

        var img = new WpfImage
        {
            Width   = 140,
            Height  = 140,
            Stretch = System.Windows.Media.Stretch.UniformToFill
        };

        if (!string.IsNullOrEmpty(album.ThumbPath) && File.Exists(album.ThumbPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource      = new Uri(album.ThumbPath);
                bmp.CacheOption    = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 140;
                bmp.EndInit();
                img.Source = bmp;
            }
            catch { img.Source = null; }
        }

        var placeholder = new Border
        {
            Width  = 140,
            Height = 140,
            Background = (WpfBrush)FindResource("AppArtworkPlaceholderBrush")
        };

        if (img.Source is null)
            stack.Children.Add(placeholder);
        else
            stack.Children.Add(img);

        var albumButton = new Button
        {
            Content     = album.Title,
            FontWeight  = FontWeights.SemiBold,
            FontSize    = 11,
            Foreground  = (WpfBrush)FindResource("AppPrimaryTextBrush"),
            Margin      = new Thickness(8, 6, 8, 2),
            Style       = (Style)FindResource("EntityLinkButtonStyle")
        };
        albumButton.Click += (_, e) =>
        {
            e.Handled = true;
            _navigationStack.Push(new NavigationState("Dashboard", null, null, null));
            BackButton.Visibility = Visibility.Visible;
            _ = ShowAlbumTracksAsync(album.Id, album.Title);
        };
        stack.Children.Add(albumButton);

        var artistButton = new Button
        {
            Content    = album.Artist,
            FontSize   = 10,
            Foreground = (WpfBrush)FindResource("AppSecondaryTextBrush"),
            Margin     = new Thickness(8, 0, 8, 8),
            Style      = (Style)FindResource("EntityLinkButtonStyle")
        };
        artistButton.Click += async (_, e) =>
        {
            e.Handled = true;
            using var db = AudioDatabase.OpenDefault();
            if (db.GetAlbumArtistId(album.Id) is long artistId)
            {
                _navigationStack.Push(new NavigationState("Dashboard", null, null, null));
                BackButton.Visibility = Visibility.Visible;
                await ShowArtistAlbumsAsync(artistId, album.Artist);
            }
        };
        stack.Children.Add(artistButton);

        card.Child = stack;

        card.MouseLeftButtonUp += (_, _) =>
        {
            _navigationStack.Push(new NavigationState("Dashboard", null, null, null));
            BackButton.Visibility = Visibility.Visible;
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
            Background      = (WpfBrush)FindResource("AppSurfaceBrush"),
            BorderBrush     = (WpfBrush)FindResource("AppGridLineBrush"),
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
                FontWeight = FontWeights.SemiBold,
                Foreground = (WpfBrush)FindResource("AppSecondaryTextBrush"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
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

    private FrameworkElement BuildCalDayCell(int day, CalendarDayData? data)
    {
        bool isToday = _dashboardYear == DateTime.Now.Year
                    && _dashboardMonth == DateTime.Now.Month
                    && day == DateTime.Now.Day;

        var border = new Border
        {
            Margin          = new Thickness(2),
            MinHeight       = 64,
            Background      = isToday
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 0x54, 0xA0, 0xFF))
                : System.Windows.Media.Brushes.Transparent,
            BorderBrush     = (WpfBrush)FindResource("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(4)
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text       = day.ToString(),
            FontSize   = 11,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
            Foreground = (WpfBrush)FindResource("AppPrimaryTextBrush"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        });

        if (data is not null && data.TotalSeconds > 0)
        {
            var ts = TimeSpan.FromSeconds(data.TotalSeconds);
            stack.Children.Add(new TextBlock
            {
                Text       = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}",
                FontSize   = 10,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x54, 0xA0, 0xFF)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            });

            foreach (var genre in data.TopGenres.Take(3))
                stack.Children.Add(new TextBlock
                {
                    Text         = genre,
                    FontSize     = 9,
                    Foreground   = (WpfBrush)FindResource("AppSecondaryTextBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                });
        }

        border.Child = stack;
        return border;
    }

    private void DashboardBuildTopGenres(List<(string Genre, double Seconds)> genres)
    {
        if (genres.Count == 0)
        {
            DashboardPanel.Children.Add(new TextBlock
            {
                Text       = LocalizationManager.Current.NoData,
                Foreground = (WpfBrush)FindResource("AppSecondaryTextBrush"),
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

            var labelTb = new TextBlock
            {
                Text         = $"{i + 1}. {genre}",
                FontSize     = 12,
                Foreground   = (WpfBrush)FindResource("AppPrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin       = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(labelTb, 0);
            row.Children.Add(labelTb);

            double fraction = maxSecs > 0 ? secs / maxSecs : 0;
            var barHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
            var barBg = new Border
            {
                Height          = 10,
                Background      = (WpfBrush)FindResource("AppGridLineBrush"),
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
                Text      = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}",
                FontSize  = 11,
                Foreground = (WpfBrush)FindResource("AppSecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin    = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(durationTb, 2);
            row.Children.Add(durationTb);

            panel.Children.Add(row);
        }

        DashboardPanel.Children.Add(panel);
    }

    private async void CalendarPrev_OnClick(object sender, RoutedEventArgs e)
    {
        _dashboardMonth--;
        if (_dashboardMonth < 1) { _dashboardMonth = 12; _dashboardYear--; }
        await RefreshCalendarSectionAsync();
    }

    private async void CalendarNext_OnClick(object sender, RoutedEventArgs e)
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
