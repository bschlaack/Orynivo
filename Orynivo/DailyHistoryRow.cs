using Orynivo.Library;

namespace Orynivo;

/// <summary>
/// One playback-history row of the daily history dialog. It lives outside the window so
/// XAML can name it in an <c>x:DataType</c> directive, which compiled bindings require
/// (a nested type cannot be referenced from XAML).
/// </summary>
public sealed class DailyHistoryRow
{
    /// <summary>Gets the underlying playback-history entry.</summary>
    public required DailyHistoryEntry Entry { get; init; }

    /// <summary>Gets the source category used by the filter chips.</summary>
    public required HistorySource Source { get; init; }

    /// <summary>Gets the localized playback timestamp.</summary>
    public required string PlayedAt { get; init; }

    /// <summary>Gets the localized media type.</summary>
    public required string MediaType { get; init; }

    /// <summary>Gets the display title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the display artist.</summary>
    public required string Artist { get; init; }

    /// <summary>Gets the display album.</summary>
    public required string Album { get; init; }

    /// <summary>Gets the localized listened duration.</summary>
    public required string ListenedDuration { get; init; }

    /// <summary>Gets the localized total duration.</summary>
    public required string TotalDuration { get; init; }

    /// <summary>Gets the original playback timestamp used for chronological table sorting.</summary>
    public DateTime PlayedAtSort => Entry.StartedAt;

    /// <summary>Gets the listened duration in seconds used for numeric table sorting.</summary>
    public double ListenedDurationSort => Entry.ListenedSeconds;

    /// <summary>Gets the media duration in seconds used for numeric table sorting.</summary>
    public double TotalDurationSort => Entry.DurationSeconds ?? double.PositiveInfinity;

    /// <summary>Gets a value indicating whether the entry can open its track.</summary>
    public bool CanOpenTrack =>
        Entry.TrackId.HasValue &&
        (File.Exists(Entry.Path) ||
         CueSheetParser.IsVirtualPath(Entry.Path));

    /// <summary>Gets a value indicating whether the entry can open its artist.</summary>
    public bool CanOpenArtist =>
        Entry.ArtistId.HasValue ||
        (DailyHistoryDialog.IsPotentialOrynivoTrack(Entry) && !string.IsNullOrWhiteSpace(Entry.Artist)) ||
        DailyHistoryDialog.IsPlexTrackWithArtist(Entry);

    /// <summary>Gets a value indicating whether the entry can open its album.</summary>
    public bool CanOpenAlbum =>
        Entry.AlbumId.HasValue ||
        (DailyHistoryDialog.IsPotentialOrynivoTrack(Entry) && !string.IsNullOrWhiteSpace(Entry.Album)) ||
        DailyHistoryDialog.IsPlexTrackWithAlbum(Entry);

    /// <summary>Gets a value indicating whether the title is rendered as plain text.</summary>
    public bool IsPlainTitle => !CanOpenTrack;

    /// <summary>Gets a value indicating whether the artist is rendered as plain text.</summary>
    public bool IsPlainArtist => !CanOpenArtist;

    /// <summary>Gets a value indicating whether the album is rendered as plain text.</summary>
    public bool IsPlainAlbum => !CanOpenAlbum;
}
