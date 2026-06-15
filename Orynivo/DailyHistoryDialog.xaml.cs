using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Orynivo.Library;
using Orynivo.Localization;

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
        public bool CanOpenTrack => Entry.TrackId.HasValue && File.Exists(Entry.Path);
        public bool CanOpenArtist => Entry.ArtistId.HasValue;
        public bool CanOpenAlbum => Entry.AlbumId.HasValue;
    }

    public string DialogTitle { get; }
    public ObservableCollection<HistoryRow> Rows { get; }
    public Visibility EmptyVisibility => Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GridVisibility => Rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    public DailyHistoryAction SelectedAction { get; private set; }
    public DailyHistoryEntry? SelectedEntry { get; private set; }

    public DailyHistoryDialog(DateTime date, IReadOnlyList<DailyHistoryEntry> entries)
    {
        DialogTitle = string.Format(
            LocalizationManager.Current.DailyHistoryTitle,
            date.ToString("D", CultureInfo.CurrentCulture));
        Rows = new ObservableCollection<HistoryRow>(entries.Select(CreateRow));
        InitializeComponent();
        DataContext = this;
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

    private static string FormatDuration(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)value.TotalHours:D2}:{value.Minutes:D2}:{value.Seconds:D2}";
    }

    private void TrackButton_OnClick(object sender, RoutedEventArgs e) =>
        SelectAction(sender, DailyHistoryAction.Track);

    private void AlbumButton_OnClick(object sender, RoutedEventArgs e) =>
        SelectAction(sender, DailyHistoryAction.Album);

    private void ArtistButton_OnClick(object sender, RoutedEventArgs e) =>
        SelectAction(sender, DailyHistoryAction.Artist);

    private void SelectAction(object sender, DailyHistoryAction action)
    {
        if (sender is not FrameworkElement { DataContext: HistoryRow row })
            return;
        SelectedAction = action;
        SelectedEntry = row.Entry;
        DialogResult = true;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void DailyHistoryDialog_OnSourceInitialized(object sender, EventArgs e)
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
        }
    }

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
