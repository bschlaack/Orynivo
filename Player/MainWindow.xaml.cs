using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using Player.Audio;
using Player.Library;

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

    private readonly ObservableCollection<PlaylistItem> _queue = [];
    private int _queueIndex = -1;
    private string _currentFilePath = string.Empty;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dsf", ".dff", ".flac", ".mp3", ".wav", ".aiff", ".aif",
        ".m4a", ".aac", ".ogg", ".opus", ".wma"
    };

    private sealed record FolderTag(bool IsFile, string FilePath, string FolderPath);

    private sealed class ContentRow
    {
        public string? Nr          { get; init; }
        public string? Title       { get; init; }
        public string? Artist      { get; init; }
        public string? Album       { get; init; }
        public string? AlbumArtist { get; init; }
        public string? Year        { get; init; }
        public string? Genre       { get; init; }
        public string? Folder      { get; init; }
        public string  Duration    { get; init; } = "";
        public string? Format      { get; init; }
        public string  FilePath    { get; init; } = "";
    }

    // ------------------------------------------------------------------
    // Initialisierung
    // ------------------------------------------------------------------

    public MainWindow()
    {
        InitializeComponent();
        _transportTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _transportTimer.Tick += (_, _) => RefreshTransport();
        // Register with handledEventsToo=true so our handler fires even after
        // IsMoveToPointEnabled marks PreviewMouseLeftButtonDown as handled.
        PositionSlider.AddHandler(
            PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(PositionSlider_OnPreviewMouseLeftButtonDown),
            handledEventsToo: true);
        LoadSettings();
        LoadNavPlaylists();
        NavListBox.SelectedIndex = 2; // Tracks als Standard
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPlayback();
        base.OnClosed(e);
    }

    private void LoadSettings()
    {
        _settings = _settingsStore.Load();
        RefreshSelectedDriverText();
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

        ContentTitleTextBlock.Text = tag switch
        {
            "Artists" => "Künstler",
            "Albums"  => "Alben",
            "Tracks"  => "Tracks",
            "Folders" => "Ordnerstruktur",
            _         => tag.StartsWith("Playlist:") ? GetPlaylistName(tag) : tag
        };

        ContentDataGrid.ItemsSource = null;
        ContentCountTextBlock.Text  = "";

        if (tag == "Folders")
        {
            ContentDataGrid.Visibility = Visibility.Collapsed;
            FolderTreeView.Visibility  = Visibility.Visible;

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
            ContentDataGrid.Visibility = Visibility.Visible;
            FolderTreeView.Visibility  = Visibility.Collapsed;

            var rows = await Task.Run(() => QueryRows(tag));
            ApplyColumns(tag);
            ContentDataGrid.ItemsSource = rows;
            ContentCountTextBlock.Text  = rows.Count == 1 ? "1 Eintrag" : $"{rows.Count:N0} Einträge";
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

    private List<ContentRow> QueryRows(string view)
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

            var tracks = db.GetAll().ToList();

            return view switch
            {
                "Artists" => tracks
                    .GroupBy(t => t.Artist ?? "")
                    .OrderBy(g => string.IsNullOrEmpty(g.Key) ? "\xFF" : g.Key, StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => new ContentRow
                    {
                        Title    = string.IsNullOrEmpty(g.Key) ? "(Unbekannt)" : g.Key,
                        FilePath = ""
                    }).ToList(),

                "Albums" => tracks
                    .GroupBy(t => (Album: t.Album ?? "", AlbumArtist: t.AlbumArtist ?? t.Artist ?? ""))
                    .OrderBy(g => string.IsNullOrEmpty(g.Key.AlbumArtist) ? "\xFF" : g.Key.AlbumArtist, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(g => string.IsNullOrEmpty(g.Key.Album) ? "\xFF" : g.Key.Album, StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => new ContentRow
                    {
                        Title    = string.IsNullOrEmpty(g.Key.Album)       ? "(Unbekannt)" : g.Key.Album,
                        Artist   = string.IsNullOrEmpty(g.Key.AlbumArtist) ? null          : g.Key.AlbumArtist,
                        Year     = g.Max(t => t.Year)?.ToString(),
                        FilePath = ""
                    }).ToList(),

                _ => tracks  // "Tracks" und Fallback
                    .OrderBy(t => t.SortTitle ?? t.Title ?? t.FileName)
                    .Select(t => new ContentRow
                    {
                        Title    = t.Title ?? t.FileName,
                        Artist   = t.Artist,
                        Album    = t.Album,
                        Duration = FormatSeconds(t.Duration),
                        Genre    = t.Genre,
                        Format   = t.Format?.ToUpperInvariant(),
                        FilePath = t.Path
                    }).ToList()
            };
        }
        catch { return []; }
    }

    private static string FormatSeconds(double? seconds)
    {
        if (seconds is null) return "";
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    private void ApplyColumns(string view)
    {
        ContentDataGrid.Columns.Clear();
        switch (view)
        {
            case "Artists":
                Add("Künstler", nameof(ContentRow.Title), 0, star: true);
                break;
            case "Albums":
                Add("Album",          nameof(ContentRow.Title),  0,   star: true);
                Add("Album-Künstler", nameof(ContentRow.Artist), 220);
                Add("Jahr",           nameof(ContentRow.Year),   60,  right: true);
                break;
            case string s when s.StartsWith("Playlist:"):
                Add("#",        nameof(ContentRow.Nr),     44,  right: true);
                Add("Titel",    nameof(ContentRow.Title),  0,   star: true);
                Add("Künstler", nameof(ContentRow.Artist), 180);
                Add("Album",    nameof(ContentRow.Album),  160);
                Add("Dauer",    nameof(ContentRow.Duration), 70, right: true);
                break;
            default: // Tracks
                Add("Titel",    nameof(ContentRow.Title),  0,   star: true);
                Add("Künstler", nameof(ContentRow.Artist), 180);
                Add("Album",    nameof(ContentRow.Album),  160);
                Add("Genre",    nameof(ContentRow.Genre),  100);
                Add("Dauer",    nameof(ContentRow.Duration), 70, right: true);
                Add("Format",   nameof(ContentRow.Format), 70);
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

            await StartPlaybackAsync(filePath);
        }
        catch (OperationCanceledException) { StatusTextBlock.Text = "Wiedergabe gestoppt."; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    // ------------------------------------------------------------------
    // Content-Doppelklick → Wiedergabe
    // ------------------------------------------------------------------

    private async void ContentDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ContentDataGrid.SelectedItem is not ContentRow row || string.IsNullOrEmpty(row.FilePath))
            return;

        // Alle Zeilen als Queue laden
        var allRows = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        _queue.Clear();
        foreach (var r in allRows.Where(r => !string.IsNullOrEmpty(r.FilePath)))
            _queue.Add(new PlaylistItem(r.FilePath));

        _queueIndex = _queue.IndexOf(_queue.FirstOrDefault(p => p.FilePath == row.FilePath) ?? _queue[0]);

        try { await StartPlaybackAsync(row.FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = "Wiedergabe gestoppt."; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    // ------------------------------------------------------------------
    // Wiedergabe
    // ------------------------------------------------------------------

    private async void PlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            StatusTextBlock.Text = "Bitte zuerst einen Track doppelklicken.";
            return;
        }
        try { await StartPlaybackAsync(_currentFilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = "Wiedergabe gestoppt."; }
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
                StatusTextBlock.Text = "Bitte zuerst ein ASIO-Gerät in den Einstellungen auswählen.";
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
                StatusTextBlock.Text = "Bitte zuerst ein WASAPI-Gerät in den Einstellungen auswählen.";
                return;
            }
            (player, info) = await WasapiAudioPlayer.CreateAsync(filePath, _settings.SelectedWasapiDeviceId, _playbackCts.Token);
        }
        else
        {
            StatusTextBlock.Text = $"{_settings.OutputBackend} ist noch nicht implementiert.";
            return;
        }

        _player        = player;
        _player.Volume = (float)VolumeSlider.Value;
        PlayButton.IsEnabled   = false;
        StopButton.IsEnabled   = true;
        PauseButton.IsEnabled  = true;
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

        StatusTextBlock.Text = _settings.OutputBackend == OutputBackend.Asio
            ? $"Wiedergabe über {_settings.SelectedDriverName}"
            : $"Wiedergabe über {_settings.SelectedWasapiDeviceName}";

        PlayButton.Content = "▶";

        await player.WaitForCompletionAsync();

        if (_player == player)
        {
            if (!await TryPlayNextAsync())
            {
                StopPlayback();
                StatusTextBlock.Text = "Wiedergabe beendet.";
            }
        }
    }

    private void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        StatusTextBlock.Text = "Wiedergabe gestoppt.";
    }

    private void StopPlayback()
    {
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = null;
        _player?.Dispose();
        _player = null;

        PlayButton.IsEnabled   = true;
        PlayButton.Content     = "▶";
        StopButton.IsEnabled   = false;
        PauseButton.IsEnabled  = false;
        PauseButton.Content    = "⏸";
        PositionSlider.IsEnabled = false;
        _transportTimer.Stop();

        NowPlayingTitleBlock.Text  = "";
        NowPlayingArtistBlock.Text = "";
        FileInfoTextBlock.Text     = "";
    }

    private async Task<bool> TryPlayNextAsync()
    {
        if (_queueIndex < 0 || _queueIndex + 1 >= _queue.Count)
            return false;

        _queueIndex++;
        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); return true; }
        catch { return false; }
    }

    // ------------------------------------------------------------------
    // Pause / Seek / Volume
    // ------------------------------------------------------------------

    private void PauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_player is null) return;
        if (_player.IsPaused)
        {
            _player.Resume();
            PauseButton.Content = "⏸";
        }
        else
        {
            _player.Pause();
            PauseButton.Content = "▶";
        }
    }

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
            _settingsStore.Save(_settings);
            RefreshSelectedDriverText();
            LoadNavPlaylists();

            StatusTextBlock.Text = _settings.OutputBackend switch
            {
                OutputBackend.Asio when string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
                    "Bitte ein ASIO-Gerät in den Einstellungen auswählen.",
                OutputBackend.Wasapi when string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId) =>
                    "Bitte ein WASAPI-Gerät in den Einstellungen auswählen.",
                OutputBackend.KernelStreaming =>
                    "KernelStreaming ist noch nicht implementiert.",
                _ => "Einstellungen gespeichert."
            };
        }
    }
}
