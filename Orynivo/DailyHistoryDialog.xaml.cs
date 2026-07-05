using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

public partial class DailyHistoryDialog : Window
{
    private readonly AppSettings _settings;
    public sealed class HistoryRow
    {
        public required DailyHistoryEntry Entry { get; init; }
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
            (IsPotentialOrynivoTrack(Entry) && !string.IsNullOrWhiteSpace(Entry.Artist));
        public bool CanOpenAlbum => Entry.AlbumId.HasValue;
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
        Rows = new ObservableCollection<HistoryRow>(entries.Select(CreateRow));
        InitializeComponent();
        DataContext = this;
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

    private static HistoryRow CreateRow(DailyHistoryEntry entry) => new()
    {
        Entry = entry,
        PlayedAt = entry.StartedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
        MediaType = entry.MediaType switch
        {
            "radio" => LocalizationManager.Current.InternetRadio,
            "podcast" => LocalizationManager.Current.Podcast,
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

    private static bool IsPotentialOrynivoTrack(DailyHistoryEntry entry)
    {
        if (!string.Equals(entry.MediaType, "track", StringComparison.OrdinalIgnoreCase))
            return false;

        if (entry.ExternalId?.StartsWith("orynivo:", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return Uri.TryCreate(entry.Path, UriKind.Absolute, out var uri) &&
               uri.AbsolutePath.Contains("/api/stream/", StringComparison.OrdinalIgnoreCase);
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
