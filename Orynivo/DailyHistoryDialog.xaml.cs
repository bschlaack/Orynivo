using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Controls;

namespace Orynivo;

public enum DailyHistoryAction
{
    None,
    Track,
    Album,
    Artist
}

/// <summary>Playback-history source category used by the day dialog's source filter.</summary>
public enum HistorySource
{
    /// <summary>A local library track.</summary>
    Track,
    /// <summary>An internet-radio session.</summary>
    Radio,
    /// <summary>A podcast episode.</summary>
    Podcast,
    /// <summary>A remote Orynivo Server track.</summary>
    Remote,
    /// <summary>A Plex Media Server track.</summary>
    Plex
}

public partial class DailyHistoryDialog : Window
{
    private readonly AppSettings _settings;
    private readonly List<HistoryRow> _allRows = new();
    private readonly HashSet<HistorySource> _activeSources = new();
    public sealed class HistoryRow
    {
        public required DailyHistoryEntry Entry { get; init; }
        public required HistorySource Source { get; init; }
        public required string PlayedAt { get; init; }
        public required string MediaType { get; init; }
        public required string Title { get; init; }
        public required string Artist { get; init; }
        public required string Album { get; init; }
        public required string ListenedDuration { get; init; }
        public required string TotalDuration { get; init; }
        public bool CanOpenTrack =>
            Entry.TrackId.HasValue &&
            (File.Exists(Entry.Path) ||
             Entry.Path.StartsWith("cue://", StringComparison.OrdinalIgnoreCase));
        public bool CanOpenArtist =>
            Entry.ArtistId.HasValue ||
            (IsPotentialOrynivoTrack(Entry) && !string.IsNullOrWhiteSpace(Entry.Artist)) ||
            IsPlexTrackWithArtist(Entry);
        public bool CanOpenAlbum =>
            Entry.AlbumId.HasValue ||
            (IsPotentialOrynivoTrack(Entry) && !string.IsNullOrWhiteSpace(Entry.Album)) ||
            IsPlexTrackWithAlbum(Entry);
        public bool IsPlainTitle => !CanOpenTrack;
        public bool IsPlainArtist => !CanOpenArtist;
        public bool IsPlainAlbum => !CanOpenAlbum;
    }

    public string DialogTitle { get; }
    public ObservableCollection<HistoryRow> Rows { get; }
    public bool ShowEmpty => Rows.Count == 0;
    public bool ShowGrid => Rows.Count > 0;
    public DailyHistoryAction SelectedAction { get; private set; }
    public DailyHistoryEntry? SelectedEntry { get; private set; }

    /// <summary>
    /// Initializes a runtime-loader instance for an empty history day.
    /// </summary>
    public DailyHistoryDialog()
        : this(DateTime.Today, Array.Empty<DailyHistoryEntry>(), new AppSettings())
    {
    }

    /// <summary>
    /// Initializes the playback-history dialog for a calendar day.
    /// </summary>
    /// <param name="date">Calendar date represented by the dialog.</param>
    /// <param name="entries">Playback-history entries to display.</param>
    /// <param name="settings">Shared application settings used for persisted column widths.</param>
    public DailyHistoryDialog(
        DateTime date,
        IReadOnlyList<DailyHistoryEntry> entries,
        AppSettings settings)
    {
        _settings = settings;
        DialogTitle = string.Format(
            LocalizationManager.Current.DailyHistoryTitle,
            date.ToString("D", CultureInfo.CurrentCulture));
        _allRows.AddRange(entries.Select(CreateRow));
        foreach (var source in Enum.GetValues<HistorySource>())
            _activeSources.Add(source);
        Rows = new ObservableCollection<HistoryRow>(_allRows);
        InitializeComponent();
        DataContext = this;
        BuildFilterChips();
        DataGridColumnWidthStore.Restore(
            _settings.DataGridColumnWidths,
            "DailyHistory",
            HistoryDataGrid);
        DataGridColumnChooser.Attach(HistoryDataGrid, "DailyHistory", _settings);
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close(false);
        };
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        DataGridColumnWidthStore.Capture(
            _settings.DataGridColumnWidths,
            "DailyHistory",
            HistoryDataGrid);
        base.OnClosed(e);
    }

    private HistoryRow CreateRow(DailyHistoryEntry entry)
    {
        var source = ClassifySource(entry);
        return new HistoryRow
        {
            Entry = entry,
            Source = source,
            PlayedAt = entry.StartedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            MediaType = source switch
            {
                HistorySource.Radio => LocalizationManager.Current.InternetRadio,
                HistorySource.Podcast => LocalizationManager.Current.Podcast,
                HistorySource.Remote => LocalizationManager.Current.HistorySourceRemote,
                HistorySource.Plex => LocalizationManager.Current.HistorySourcePlex,
                _ => LocalizationManager.Current.Tracks
            },
            Title = entry.Title,
            Artist = entry.Artist ?? string.Empty,
            Album = entry.Album ?? string.Empty,
            ListenedDuration = FormatDuration(entry.ListenedSeconds),
            TotalDuration = entry.DurationSeconds is > 0
                ? FormatDuration(entry.DurationSeconds.Value)
                : string.Empty
        };
    }

    /// <summary>Classifies a playback-history entry into its source category for filtering.</summary>
    /// <param name="entry">The playback-history entry.</param>
    /// <returns>The resolved <see cref="HistorySource"/>.</returns>
    private HistorySource ClassifySource(DailyHistoryEntry entry)
    {
        if (string.Equals(entry.MediaType, "radio", StringComparison.OrdinalIgnoreCase))
            return HistorySource.Radio;
        if (string.Equals(entry.MediaType, "podcast", StringComparison.OrdinalIgnoreCase))
            return HistorySource.Podcast;
        if (entry.ExternalId?.StartsWith("plex:", StringComparison.OrdinalIgnoreCase) == true ||
            IsConfiguredPlexPath(entry.Path))
            return HistorySource.Plex;
        if (IsPotentialOrynivoTrack(entry))
            return HistorySource.Remote;
        return HistorySource.Track;
    }

    /// <summary>Determines whether a playback path targets one of the configured Plex servers.</summary>
    /// <param name="path">The stored playback path or stream URL.</param>
    /// <returns><see langword="true"/> when the host and port match a configured Plex server.</returns>
    private bool IsConfiguredPlexPath(string path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return false;
        foreach (var server in _settings.PlexServers ?? [])
            if (Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out var baseUri) &&
                string.Equals(baseUri.Host, uri.Host, StringComparison.OrdinalIgnoreCase) &&
                baseUri.Port == uri.Port)
                return true;
        return false;
    }

    private static bool IsPotentialOrynivoTrack(DailyHistoryEntry entry)
    {
        if (!string.Equals(entry.MediaType, "track", StringComparison.OrdinalIgnoreCase))
            return false;

        if (entry.ExternalId?.StartsWith("orynivo:", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return Uri.TryCreate(entry.Path, UriKind.Absolute, out var uri) &&
               uri.AbsolutePath.Contains("/api/stream/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlexTrackWithAlbum(DailyHistoryEntry entry) =>
        TryGetPlexKeys(entry, out var albumKey, out _) && !string.IsNullOrWhiteSpace(albumKey);

    private static bool IsPlexTrackWithArtist(DailyHistoryEntry entry) =>
        TryGetPlexKeys(entry, out _, out var artistKey) && !string.IsNullOrWhiteSpace(artistKey);

    private static bool TryGetPlexKeys(DailyHistoryEntry entry, out string albumKey, out string artistKey)
    {
        albumKey = artistKey = string.Empty;
        if (entry.ExternalId is null ||
            !entry.ExternalId.StartsWith("plex:", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = entry.ExternalId.Split(':');
        albumKey = parts.Length > 3 ? parts[3] : string.Empty;
        artistKey = parts.Length > 4 ? parts[4] : string.Empty;
        return true;
    }

    /// <summary>Builds the source-filter toggle chips for the categories present in the day's history.</summary>
    private void BuildFilterChips()
    {
        if (FilterPanel is null)
            return;

        FilterPanel.Children.Clear();
        HistorySource[] order =
        [
            HistorySource.Track,
            HistorySource.Radio,
            HistorySource.Podcast,
            HistorySource.Remote,
            HistorySource.Plex
        ];

        var present = order.Where(source => _allRows.Any(row => row.Source == source)).ToList();
        // With only one category there is nothing to filter, so hide the chip row.
        if (present.Count < 2)
        {
            FilterPanel.IsVisible = false;
            return;
        }

        foreach (var source in present)
        {
            int count = _allRows.Count(row => row.Source == source);
            var chip = new ToggleButton
            {
                Classes = { "filterChip" },
                Content = $"{SourceLabel(source)} ({count})",
                Tag = source,
                IsChecked = true,
                Margin = new Avalonia.Thickness(0, 0, 8, 0)
            };
            chip.IsCheckedChanged += FilterChip_OnCheckedChanged;
            FilterPanel.Children.Add(chip);
        }
    }

    private static string SourceLabel(HistorySource source) => source switch
    {
        HistorySource.Radio => LocalizationManager.Current.InternetRadio,
        HistorySource.Podcast => LocalizationManager.Current.Podcast,
        HistorySource.Remote => LocalizationManager.Current.HistorySourceRemote,
        HistorySource.Plex => LocalizationManager.Current.HistorySourcePlex,
        _ => LocalizationManager.Current.Tracks
    };

    private void FilterChip_OnCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: HistorySource source } chip)
            return;
        if (chip.IsChecked == true)
            _activeSources.Add(source);
        else
            _activeSources.Remove(source);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in _allRows)
            if (_activeSources.Contains(row.Source))
                Rows.Add(row);
    }

    private static string FormatDuration(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)value.TotalHours:D2}:{value.Minutes:D2}:{value.Seconds:D2}";
    }

    private void TrackButton_OnClick(object? sender, RoutedEventArgs e) =>
        SelectAction(sender, DailyHistoryAction.Track);

    private void AlbumButton_OnClick(object? sender, RoutedEventArgs e) =>
        SelectAction(sender, DailyHistoryAction.Album);

    private void ArtistButton_OnClick(object? sender, RoutedEventArgs e) =>
        SelectAction(sender, DailyHistoryAction.Artist);

    private void SelectAction(object? sender, DailyHistoryAction action)
    {
        if (sender is not Control { DataContext: HistoryRow row })
            return;
        SelectedAction = action;
        SelectedEntry = row.Entry;
        Close(true);
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}
