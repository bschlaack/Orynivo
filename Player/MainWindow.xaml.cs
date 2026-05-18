using System.IO;
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
using Player.Audio;
using Player.Library;
using Player.Localization;
using Button = System.Windows.Controls.Button;
using WpfImage = System.Windows.Controls.Image;
using WpfCursors = System.Windows.Input.Cursors;
using WpfBrush = System.Windows.Media.Brush;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace Player;

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
    private long? _currentPlayHistoryId;
    private long? _activeAlbumFilterId;
    private string? _activeAlbumFilterTitle;
    private long? _activeArtistFilterId;
    private string? _activeArtistFilterName;
    private readonly Stack<NavigationState> _navigationStack = [];
    private readonly DispatcherTimer _searchTimer;
    private bool _trackFavoritesOnly;
    private readonly HashSet<string> _selectedTrackGenres = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedTrackFormats = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _selectedTrackBitrates = [];
    private readonly HashSet<string> _expandedTrackFilterSections = new(StringComparer.Ordinal);

    private readonly ObservableCollection<PlaylistItem> _queue = [];
    private int _queueIndex = -1;
    private string _currentFilePath = string.Empty;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dsf", ".dff", ".flac", ".mp3", ".wav", ".aiff", ".aif",
        ".m4a", ".aac", ".ogg", ".opus", ".wma"
    };

    private sealed record FolderTag(bool IsFile, string FilePath, string FolderPath);
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
        public string? Title       { get; init; }
        public string? Artist      { get; init; }
        public string? Album       { get; init; }
        public string? AlbumArtist { get; init; }
        public string? Year        { get; init; }
        public string? Genre       { get; init; }
        public string? Folder      { get; init; }
        public string? ArtworkPath { get; init; }
        public string? ThumbnailPath { get; init; }
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

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // ------------------------------------------------------------------
    // Initialisierung
    // ------------------------------------------------------------------

    public MainWindow()
    {
        InitializeComponent();
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
            tag is "Artists" or "Albums" or "Tracks" or "Folders")
        {
            _settings.LastMainView = tag;
        }
        _settings.AlbumArtworkView = _showAlbumArtworkView;
        _settings.Volume = VolumeSlider.Value;
        _settings.LastTrackPath = File.Exists(_currentFilePath) ? _currentFilePath : null;
        _settingsStore.Save(_settings);
    }

    private void LoadSettings()
    {
        _settings = _settingsStore.Load();
        RefreshSelectedDriverText();
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
            NowPlayingArtworkImage.Source =
                CreateArtworkImage(db.GetArtworkPathsByTrackPath(path)?.Thumb96Path, 96);
            PlayButton.IsEnabled = true;
            SetPlayPauseIcon(isPlaying: false);
        }
        catch
        {
            _currentFilePath = string.Empty;
            NowPlayingTitleBlock.Text = string.Empty;
            NowPlayingArtistBlock.Text = string.Empty;
            NowPlayingArtworkImage.Source = null;
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
            _ => "Kein Gerät ausgewählt."
        };
    }

    // ------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------

    private void LoadNavPlaylists()
    {
        // Dynamisch hinzugefügte Playlist-Einträge (Index 6+) entfernen
        while (NavListBox.Items.Count > 6)
            NavListBox.Items.RemoveAt(NavListBox.Items.Count - 1);

        try
        {
            using var db = AudioDatabase.OpenDefault();
            foreach (var pl in db.GetAllPlaylists())
            {
                NavListBox.Items.Add(new ListBoxItem
                {
                    Content = pl.Name,
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
        SearchResultsScrollViewer.Visibility = Visibility.Collapsed;
        ContentCountTextBlock.Text  = "";
        AlbumViewModeBorder.Visibility = tag == "Albums" ? Visibility.Visible : Visibility.Collapsed;
        TrackFilterButton.Visibility = tag == "Tracks" ? Visibility.Visible : Visibility.Collapsed;
        TrackFilterPopup.IsOpen = false;
        if (tag == "Folders")
        {
            ContentDataGrid.Visibility = Visibility.Collapsed;
            FolderTreeView.Visibility  = Visibility.Visible;
            AlbumArtworkListBox.Visibility = Visibility.Collapsed;

            var tracks = await Task.Run(() =>
            {
                try { using var db = AudioDatabase.OpenDefault(); return db.GetTracksLite(); }
                catch { return new List<TrackLite>(); }
            });

            BuildFolderTree(tracks);
            ContentCountTextBlock.Text = $"{tracks.Count:N0} Titel";
        }
        else
        {
            ContentDataGrid.Visibility = tag == "Albums" && _showAlbumArtworkView
                ? Visibility.Collapsed
                : Visibility.Visible;
            FolderTreeView.Visibility  = Visibility.Collapsed;
            AlbumArtworkListBox.Visibility = tag == "Albums" && _showAlbumArtworkView
                ? Visibility.Visible
                : Visibility.Collapsed;

            var rows = tag == "Tracks"
                ? await Task.Run(GetFilteredTrackRows)
                : await Task.Run(() => QueryRows(tag));
            ApplyColumns(tag);
            ContentDataGrid.ItemsSource = rows;
            AlbumArtworkListBox.ItemsSource = tag == "Albums" ? rows : null;
            ContentCountTextBlock.Text  = rows.Count == 1 ? "1 Eintrag" : $"{rows.Count:N0} Einträge";
        }
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
                var ptracks = db.GetPlaylistTracks(pid).ToList();
                return ptracks.Select((pt, i) =>
                {
                    var t = db.GetByPath(pt.Path);
                    return new ContentRow
                    {
                        Nr       = (i + 1).ToString(),
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
                "Search" => db.GetTrackListByIds(TrackSearchIndex.Search(searchQuery ?? string.Empty))
                    .Select(ToTrackContentRow)
                    .ToList(),

                "Artists" => db.GetArtistsLite()
                    .Select(a => new ContentRow
                    {
                        Id       = a.Id,
                        Title    = string.IsNullOrEmpty(a.Artist) ? LocalizationManager.Current.Unknown : a.Artist,
                        IsFavorite = a.IsFavorite,
                        FilePath = ""
                    }).ToList(),

                "Albums" => (_activeArtistFilterId is long artistId
                        ? db.GetAlbumsByArtist(artistId, _showAlbumArtworkView)
                        : db.GetAlbumsLite(_showAlbumArtworkView))
                    .Select(a => new ContentRow
                    {
                        Title    = string.IsNullOrEmpty(a.Album) ? LocalizationManager.Current.Unknown : a.Album,
                        Id       = a.Id,
                        Artist   = string.IsNullOrEmpty(a.DisplayArtist) ? null : a.DisplayArtist,
                        Year     = a.Year?.ToString(),
                        ArtworkPath = a.ArtworkPath,
                        ThumbnailPath = a.ThumbnailPath,
                        IsFavorite = a.IsFavorite,
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
        ContentCountTextBlock.Text = rows.Count == 1 ? "1 Eintrag" : $"{rows.Count:N0} Einträge";
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
        if (string.IsNullOrWhiteSpace(query))
        {
            if (NavListBox.SelectedItem is ListBoxItem { Tag: string tag })
                await ShowTopLevelViewAsync(tag);
            return;
        }

        ResetDrilldownState();
        ContentTitleTextBlock.Text = LocalizationManager.Current.Search;
        AlbumViewModeBorder.Visibility = Visibility.Collapsed;
        TrackFilterButton.Visibility = Visibility.Collapsed;
        ContentDataGrid.Visibility = Visibility.Collapsed;
        FolderTreeView.Visibility = Visibility.Collapsed;
        AlbumArtworkListBox.Visibility = Visibility.Collapsed;
        SearchResultsScrollViewer.Visibility = Visibility.Visible;

        var result = await Task.Run(() =>
        {
            var ids = TrackSearchIndex.Search(query);
            using var db = AudioDatabase.OpenDefault();
            return (
                Tracks: db.GetTrackListByIds(ids).Select(ToTrackContentRow).ToList(),
                Albums: db.GetAlbumsByTrackIds(ids).Select(ToAlbumContentRow).ToList(),
                Artists: db.GetArtistsByTrackIds(ids).Select(ToArtistContentRow).ToList());
        });

        ApplySearchColumns();
        SearchTracksDataGrid.ItemsSource = result.Tracks;
        SearchAlbumsDataGrid.ItemsSource = result.Albums;
        SearchArtistsDataGrid.ItemsSource = result.Artists;
        ContentCountTextBlock.Text =
            $"{result.Tracks.Count:N0} Titel · {result.Albums.Count:N0} Alben · {result.Artists.Count:N0} Künstler";
    }

    private static ContentRow ToAlbumContentRow(AlbumInfo a) => new()
    {
        Id = a.Id,
        Title = string.IsNullOrEmpty(a.Album) ? LocalizationManager.Current.Unknown : a.Album,
        Artist = string.IsNullOrEmpty(a.DisplayArtist) ? null : a.DisplayArtist,
        Year = a.Year?.ToString(),
        ArtworkPath = a.ArtworkPath,
        ThumbnailPath = a.ThumbnailPath,
        IsFavorite = a.IsFavorite
    };

    private static ContentRow ToArtistContentRow(ArtistInfo a) => new()
    {
        Id = a.Id,
        Title = string.IsNullOrEmpty(a.Artist) ? LocalizationManager.Current.Unknown : a.Artist,
        IsFavorite = a.IsFavorite
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

    private static void ConfigureSearchGrid(DataGrid grid, params (string Header, string Binding, double Width)[] columns)
    {
        grid.Columns.Clear();
        foreach (var column in columns)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new Binding(column.Binding),
                Width = new DataGridLength(column.Width)
            });
    }

    private static ImageSource? CreateArtworkImage(string? path, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
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
        if (row.Artwork is null)
            row.Artwork = CreateArtworkImage(row.ArtworkPath, 320);
        if (row.Thumbnail is null)
            row.Thumbnail = CreateArtworkImage(row.ThumbnailPath, 96);
    }

    private static void EnsureThumbnailHydrated(ContentRow row)
    {
        if (row.Thumbnail is null)
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
        if (!IsLoaded)
            return;

        _showAlbumArtworkView = AlbumArtworkViewRadioButton.IsChecked == true;
        _settings.AlbumArtworkView = _showAlbumArtworkView;
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: "Albums" })
            return;

        ContentDataGrid.Visibility = _showAlbumArtworkView ? Visibility.Collapsed : Visibility.Visible;
        AlbumArtworkListBox.Visibility = _showAlbumArtworkView ? Visibility.Visible : Visibility.Collapsed;

        var rows = await Task.Run(() => QueryRows("Albums"));
        ContentDataGrid.ItemsSource = rows;
        AlbumArtworkListBox.ItemsSource = rows;
        ContentCountTextBlock.Text = rows.Count == 1 ? "1 Eintrag" : $"{rows.Count:N0} Einträge";
    }

    private void AlbumArtworkItem_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ContentRow row })
            EnsureArtworkHydrated(row);
    }

    private void ContentDataGrid_OnLoadingRow(object sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is ContentRow row && AlbumViewModeBorder.Visibility == Visibility.Visible)
            EnsureThumbnailHydrated(row);
    }

    private void ApplyColumns(string view)
    {
        ContentDataGrid.Columns.Clear();
        switch (view)
        {
            case "Artists":
                AddFavorite();
                Add(LocalizationManager.Current.Artist, nameof(ContentRow.Title), 0, star: true);
                break;
            case "Albums":
                AddFavorite();
                AddThumbnail();
                Add(LocalizationManager.Current.Album,       nameof(ContentRow.Title),  0,   star: true);
                Add(LocalizationManager.Current.AlbumArtist, nameof(ContentRow.Artist), 220);
                Add(LocalizationManager.Current.Year,        nameof(ContentRow.Year),   60,  right: true);
                break;
            case string s when s.StartsWith("Playlist:"):
                Add("#",        nameof(ContentRow.Nr),     44,  right: true);
                Add(LocalizationManager.Current.Title,    nameof(ContentRow.Title),  0,   star: true);
                Add(LocalizationManager.Current.Artist,   nameof(ContentRow.Artist), 180);
                Add(LocalizationManager.Current.Album,    nameof(ContentRow.Album),  160);
                Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 70, right: true);
                break;
            default: // Tracks
                AddFavorite();
                Add(LocalizationManager.Current.Title,    nameof(ContentRow.Title),  0,   star: true);
                Add(LocalizationManager.Current.Artist,   nameof(ContentRow.Artist), 180);
                Add(LocalizationManager.Current.Album,    nameof(ContentRow.Album),  160);
                Add(LocalizationManager.Current.Genre,    nameof(ContentRow.Genre),  100);
                Add(LocalizationManager.Current.Duration, nameof(ContentRow.Duration), 70, right: true);
                Add(LocalizationManager.Current.Format,   nameof(ContentRow.Format), 70);
                break;
        }

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
            RefreshQueueNavigationButtons();

            await StartPlaybackAsync(filePath);
        }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    // ------------------------------------------------------------------
    // Content-Doppelklick → Wiedergabe
    // ------------------------------------------------------------------

    private async void ContentDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ContentDataGrid.SelectedItem is not ContentRow row)
            return;
        await HandleContentRowDoubleClickAsync(row);
    }

    private async void AlbumArtworkListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AlbumArtworkListBox.SelectedItem is not ContentRow row)
            return;
        await HandleContentRowDoubleClickAsync(row);
    }

    private async void SearchTracksDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchTracksDataGrid.SelectedItem is not ContentRow row)
            return;

        var allRows = (SearchTracksDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        await PlayTrackFromRowsAsync(row, allRows);
    }

    private async void SearchAlbumsDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchAlbumsDataGrid.SelectedItem is ContentRow { Id: long albumId } row)
        {
            _navigationStack.Push(new NavigationState("Search", albumId, null, null, SearchTextBox.Text));
            await ShowAlbumTracksAsync(albumId, row.Title ?? LocalizationManager.Current.Unknown);
        }
    }

    private async void SearchArtistsDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchArtistsDataGrid.SelectedItem is ContentRow { Id: long artistId } row)
        {
            _navigationStack.Push(new NavigationState("Search", artistId, null, null, SearchTextBox.Text));
            await ShowArtistAlbumsAsync(artistId, row.Title ?? LocalizationManager.Current.Unknown);
        }
    }

    private async Task HandleContentRowDoubleClickAsync(ContentRow row)
    {

        if (ContentTitleTextBlock.Text == "Künstler" && row.Id is long artistId)
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
        ContentTitleTextBlock.Text = $"Tracks · {albumTitle}";
        AlbumViewModeBorder.Visibility = Visibility.Collapsed;
        ContentDataGrid.Visibility = Visibility.Visible;
        AlbumArtworkListBox.Visibility = Visibility.Collapsed;
        FolderTreeView.Visibility = Visibility.Collapsed;
        SearchResultsScrollViewer.Visibility = Visibility.Collapsed;

        var rows = await Task.Run(() => QueryRows("Tracks"));
        ApplyColumns("Tracks");
        ContentDataGrid.ItemsSource = rows;
        ContentCountTextBlock.Text = rows.Count == 1 ? "1 Titel" : $"{rows.Count:N0} Titel";
        BackButton.Visibility = Visibility.Visible;
    }

    private async Task ShowArtistAlbumsAsync(long artistId, string artistName)
    {
        if (_navigationStack.Count == 0 || _navigationStack.Peek().View != "Search")
            _navigationStack.Push(new NavigationState("Artists", artistId, null, null));
        _activeArtistFilterId = artistId;
        _activeArtistFilterName = artistName;
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;
        ContentTitleTextBlock.Text = $"Alben · {artistName}";
        AlbumViewModeBorder.Visibility = Visibility.Visible;
        ContentDataGrid.Visibility = _showAlbumArtworkView ? Visibility.Collapsed : Visibility.Visible;
        AlbumArtworkListBox.Visibility = _showAlbumArtworkView ? Visibility.Visible : Visibility.Collapsed;
        FolderTreeView.Visibility = Visibility.Collapsed;
        SearchResultsScrollViewer.Visibility = Visibility.Collapsed;

        var rows = await Task.Run(() => QueryRows("Albums"));
        ApplyColumns("Albums");
        ContentDataGrid.ItemsSource = rows;
        AlbumArtworkListBox.ItemsSource = rows;
        ContentCountTextBlock.Text = rows.Count == 1 ? "1 Eintrag" : $"{rows.Count:N0} Einträge";
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

        await ReloadAlbumRowsAsync();
    }

    private async Task ReloadAlbumRowsAsync()
    {
        var rows = await Task.Run(() => QueryRows("Albums"));
        ApplyColumns("Albums");
        ContentDataGrid.ItemsSource = rows;
        AlbumArtworkListBox.ItemsSource = rows;
        ContentCountTextBlock.Text = rows.Count == 1 ? "1 Eintrag" : $"{rows.Count:N0} Einträge";
    }

    private async void BackButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_navigationStack.Count == 0)
            return;

        var state = _navigationStack.Pop();
        _activeArtistFilterId = state.ArtistFilterId;
        _activeArtistFilterName = state.ArtistFilterName;
        _activeAlbumFilterId = null;
        _activeAlbumFilterTitle = null;

        switch (state.View)
        {
            case "Search":
                SearchTextBox.Text = state.SearchQuery ?? string.Empty;
                await ShowSearchResultsAsync(state.SearchQuery ?? string.Empty);
                return;

            case "Artists":
                ContentTitleTextBlock.Text = "Künstler";
                AlbumViewModeBorder.Visibility = Visibility.Collapsed;
                ContentDataGrid.Visibility = Visibility.Visible;
                AlbumArtworkListBox.Visibility = Visibility.Collapsed;
                var artists = await Task.Run(() => QueryRows("Artists"));
                ApplyColumns("Artists");
                ContentDataGrid.ItemsSource = artists;
                ContentCountTextBlock.Text = artists.Count == 1 ? "1 Eintrag" : $"{artists.Count:N0} Einträge";
                RestoreSelection(artists, state.SelectedId);
                break;

            case "Albums":
                ContentTitleTextBlock.Text = _activeArtistFilterId is long
                    ? $"Alben · {_activeArtistFilterName}"
                    : "Alben";
                AlbumViewModeBorder.Visibility = Visibility.Visible;
                ContentDataGrid.Visibility = _showAlbumArtworkView ? Visibility.Collapsed : Visibility.Visible;
                AlbumArtworkListBox.Visibility = _showAlbumArtworkView ? Visibility.Visible : Visibility.Collapsed;
                var albums = await Task.Run(() => QueryRows("Albums"));
                ApplyColumns("Albums");
                ContentDataGrid.ItemsSource = albums;
                AlbumArtworkListBox.ItemsSource = albums;
                ContentCountTextBlock.Text = albums.Count == 1 ? "1 Eintrag" : $"{albums.Count:N0} Einträge";
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
    }

    private void FavoriteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow row } || row.Id is not long id)
            return;

        row.IsFavorite = !row.IsFavorite;
        using var db = AudioDatabase.OpenDefault();
        if (ContentTitleTextBlock.Text == "Künstler")
            db.SetArtistFavorite(id, row.IsFavorite);
        else if (AlbumViewModeBorder.Visibility == Visibility.Visible)
            db.SetAlbumFavorite(id, row.IsFavorite);
        else
            db.SetTrackFavorite(id, row.IsFavorite);

        ContentDataGrid.Items.Refresh();
        AlbumArtworkListBox.Items.Refresh();
        e.Handled = true;
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
            NowPlayingArtworkImage.Source =
                CreateArtworkImage(db.GetArtworkPathsByTrackPath(filePath)?.Thumb96Path, 96);
        }
        catch
        {
            NowPlayingArtworkImage.Source = null;
        }

        StatusTextBlock.Text = _settings.OutputBackend == OutputBackend.Asio
            ? $"Wiedergabe über {_settings.SelectedDriverName}"
            : $"Wiedergabe über {_settings.SelectedWasapiDeviceName}";

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
        if (_queueIndex <= 0 || _queueIndex >= _queue.Count)
            return;

        _queueIndex--;
        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private async void NextButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_queueIndex < 0 || _queueIndex + 1 >= _queue.Count)
            return;

        _queueIndex++;
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

        NowPlayingTitleBlock.Text  = "";
        NowPlayingArtistBlock.Text = "";
        FileInfoTextBlock.Text     = "";
        NowPlayingArtworkImage.Source = null;
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
        if (_queueIndex < 0 || _queueIndex + 1 >= _queue.Count)
            return false;

        _queueIndex++;
        RefreshQueueNavigationButtons();
        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); return true; }
        catch { return false; }
    }

    private void RefreshQueueNavigationButtons()
    {
        PreviousButton.IsEnabled = _queueIndex > 0 && _queueIndex < _queue.Count;
        NextButton.IsEnabled = _queueIndex >= 0 && _queueIndex + 1 < _queue.Count;
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
        if (_player?.CanSeek == true)
            _isSeekingWithSlider = true;
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
            _settingsStore.Save(_settings);
            ThemeManager.Apply(_settings.Theme);
            LocalizationManager.Apply(_settings.Language);
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

    private void AboutButton_OnClick(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }
}
