using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Orynivo.Library;
using Orynivo.Streaming;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>
/// The shared row model of every library, search, playlist, queue, and artwork view. It
/// carries display values plus the source context needed to navigate and play the row.
/// It lives outside the main window so XAML can name it in an <c>x:DataType</c>
/// directive, which compiled bindings require (a nested type cannot be referenced from
/// XAML).
/// </summary>
internal sealed class ContentRow : INotifyPropertyChanged
{
    /// <summary>Gets or sets the leading track number or row index shown in compact lists.</summary>
    public string? Nr { get; set; }

    /// <summary>Gets the provider-local track, album, or artist identifier.</summary>
    public long? Id { get; init; }

    /// <summary>Gets or sets the provider-local primary artist identifier.</summary>
    public long? ArtistId { get; set; }

    /// <summary>Gets or sets the provider-local album identifier.</summary>
    public long? AlbumId { get; set; }

    /// <summary>Gets or sets the provider-local album identifiers of a coalesced album.</summary>
    public IReadOnlyList<long>? LogicalAlbumIds { get; set; }

    /// <summary>Gets or sets the provider-local records of a coalesced album.</summary>
    public IReadOnlyList<LogicalAlbumPart>? LogicalAlbumParts { get; set; }

    /// <summary>Gets the display title.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the text used for A-Z indexing, when it differs from the title.</summary>
    public string? AlphabetIndexText { get; init; }

    /// <summary>Gets the display artist.</summary>
    public string? Artist { get; init; }

    /// <summary>Gets a value indicating whether the row has an artist to display.</summary>
    public bool HasArtist => !string.IsNullOrWhiteSpace(Artist);

    /// <summary>Gets the display album.</summary>
    public string? Album { get; init; }

    /// <summary>Gets the display album artist.</summary>
    public string? AlbumArtist { get; init; }

    /// <summary>Gets the display release year.</summary>
    public string? Year { get; init; }

    /// <summary>Gets the display track number.</summary>
    public string? TrackNumber { get; init; }

    /// <summary>Gets the display disc number.</summary>
    public string? DiscNumber { get; init; }

    private string? _genre;

    /// <summary>Gets or sets the displayed genre so bulk edits refresh the row in place.</summary>
    public string? Genre
    {
        get => _genre;
        set
        {
            if (string.Equals(_genre, value, StringComparison.Ordinal))
                return;
            _genre = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Genre)));
        }
    }

    /// <summary>Gets the display bitrate.</summary>
    public string? Bitrate { get; init; }

    /// <summary>Gets the display sample rate.</summary>
    public string? SampleRate { get; init; }

    /// <summary>Gets the numeric sample rate in hertz, when known.</summary>
    public int? SampleRateHz { get; init; }

    /// <summary>Gets the display bit depth.</summary>
    public string? BitDepth { get; init; }

    /// <summary>Gets the display channel layout.</summary>
    public string? Channels { get; init; }

    /// <summary>Gets the numeric channel count, when known.</summary>
    public int? ChannelCount { get; init; }

    /// <summary>Gets the display composer.</summary>
    public string? Composer { get; init; }

    /// <summary>Gets the display tempo in beats per minute.</summary>
    public string? Bpm { get; init; }

    /// <summary>Gets the cached Camelot wheel label of the estimated musical key, when available.</summary>
    public string? CamelotKey { get; init; }

    /// <summary>Gets the display file name.</summary>
    public string? FileName { get; init; }

    /// <summary>Gets the display file size.</summary>
    public string? FileSize { get; init; }

    /// <summary>Gets the display date the track was added to the library.</summary>
    public string? AddedAt { get; init; }

    /// <summary>Gets the display track ReplayGain value.</summary>
    public string? ReplayGainTrack { get; init; }

    /// <summary>Gets the display album ReplayGain value.</summary>
    public string? ReplayGainAlbum { get; init; }

    /// <summary>Gets the numeric sort key for <see cref="Nr"/>.</summary>
    public int NrSort => ParseLeadingInteger(Nr);

    /// <summary>Gets the numeric sort key for <see cref="Year"/>.</summary>
    public int YearSort => ParseLeadingInteger(Year);

    /// <summary>Gets the numeric sort key for <see cref="TrackNumber"/>.</summary>
    public int TrackNumberSort => ParseLeadingInteger(TrackNumber);

    /// <summary>Gets the numeric sort key for <see cref="DiscNumber"/>.</summary>
    public int DiscNumberSort => ParseLeadingInteger(DiscNumber);

    /// <summary>Gets the numeric sort key for <see cref="Bitrate"/>.</summary>
    public int BitrateSort => ParseDisplayNumber(Bitrate);

    /// <summary>Gets the numeric sort key for <see cref="SampleRate"/>.</summary>
    public int SampleRateSort => SampleRateHz ?? ParseDisplayNumber(SampleRate);

    /// <summary>Gets the numeric sort key for <see cref="BitDepth"/>.</summary>
    public int BitDepthSort => ParseLeadingInteger(BitDepth);

    /// <summary>Gets the numeric sort key for <see cref="Channels"/>.</summary>
    public int ChannelsSort => ChannelCount ?? ParseLeadingInteger(Channels);

    /// <summary>Gets the numeric sort key for <see cref="Bpm"/>.</summary>
    public double BpmSort => ParseDisplayDouble(Bpm);

    /// <summary>Gets the numeric sort key for <see cref="FileSize"/>.</summary>
    public long FileSizeSort => ParseFileSize(FileSize);

    /// <summary>Gets the chronological sort key for <see cref="AddedAt"/>.</summary>
    public DateTime AddedAtSort => ParseDisplayDate(AddedAt);

    /// <summary>Gets the numeric sort key for <see cref="ReplayGainTrack"/>.</summary>
    public double ReplayGainTrackSort => ParseDisplayDouble(ReplayGainTrack);

    /// <summary>Gets the numeric sort key for <see cref="ReplayGainAlbum"/>.</summary>
    public double ReplayGainAlbumSort => ParseDisplayDouble(ReplayGainAlbum);

    private int _userRating;

    /// <summary>Gets or sets the personal zero-to-five-star rating.</summary>
    public int UserRating
    {
        get => _userRating;
        set
        {
            if (_userRating == value)
                return;
            _userRating = Math.Clamp(value, 0, 5);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UserRating)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UserRatingGlyph)));
        }
    }

    /// <summary>Gets the personal rating as filled and empty star glyphs.</summary>
    public string UserRatingGlyph => new string('★', UserRating) + new string('☆', 5 - UserRating);

    private double? _musicBrainzRating;

    /// <summary>Gets or sets the cached MusicBrainz community rating.</summary>
    public double? MusicBrainzRating
    {
        get => _musicBrainzRating;
        set
        {
            if (_musicBrainzRating == value)
                return;
            _musicBrainzRating = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MusicBrainzRating)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MusicBrainzRatingDisplay)));
        }
    }

    private int? _musicBrainzRatingVotes;

    /// <summary>Gets or sets the cached MusicBrainz vote count.</summary>
    public int? MusicBrainzRatingVotes
    {
        get => _musicBrainzRatingVotes;
        set
        {
            if (_musicBrainzRatingVotes == value)
                return;
            _musicBrainzRatingVotes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MusicBrainzRatingVotes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MusicBrainzRatingDisplay)));
        }
    }

    /// <summary>Gets or sets the resolved MusicBrainz recording identifier.</summary>
    public string? MusicBrainzTrackId { get; set; }

    /// <summary>Gets or sets the Unix timestamp of the latest MusicBrainz rating lookup.</summary>
    public long? MusicBrainzRatingFetchedAt
    {
        get => _musicBrainzRatingFetchedAt;
        set
        {
            if (_musicBrainzRatingFetchedAt == value)
                return;
            _musicBrainzRatingFetchedAt = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MusicBrainzRatingFetchedAt)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MusicBrainzRatingDisplay)));
        }
    }

    private long? _musicBrainzRatingFetchedAt;
    private bool _isMusicBrainzRatingLoading;
    private bool _musicBrainzRatingTemporaryFailure;

    /// <summary>Gets or sets whether a MusicBrainz lookup is currently running for this row.</summary>
    public bool IsMusicBrainzRatingLoading
    {
        get => _isMusicBrainzRatingLoading;
        set
        {
            if (_isMusicBrainzRatingLoading == value) return;
            _isMusicBrainzRatingLoading = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMusicBrainzRatingLoading)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MusicBrainzRatingDisplay)));
        }
    }

    /// <summary>Gets or sets whether the latest MusicBrainz request failed temporarily.</summary>
    public bool MusicBrainzRatingTemporaryFailure
    {
        get => _musicBrainzRatingTemporaryFailure;
        set
        {
            if (_musicBrainzRatingTemporaryFailure == value) return;
            _musicBrainzRatingTemporaryFailure = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MusicBrainzRatingTemporaryFailure)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MusicBrainzRatingDisplay)));
        }
    }

    /// <summary>
    /// Gets the localized MusicBrainz rating cell text, covering the loading, retry,
    /// not-rated, and not-yet-loaded states.
    /// </summary>
    public string MusicBrainzRatingDisplay => MusicBrainzRating is double rating
        ? $"★ {rating:0.0} ({MusicBrainzRatingVotes.GetValueOrDefault():N0})"
        : IsMusicBrainzRatingLoading
            ? LocalizationManager.Current.MusicBrainzLoadingRating
        : MusicBrainzRatingTemporaryFailure
            ? LocalizationManager.Current.MusicBrainzRetryRating
        : MusicBrainzRatingFetchedAt.HasValue
            ? LocalizationManager.Current.MusicBrainzNoRating
            : LocalizationManager.Current.MusicBrainzLoadRating;

    /// <summary>Gets the display folder path.</summary>
    public string? Folder { get; init; }

    /// <summary>Gets or sets the cached full-size artwork file path.</summary>
    public string? ArtworkPath { get; set; }

    /// <summary>Gets or sets the cached thumbnail artwork file path.</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>Gets or sets the cached artist biography.</summary>
    public string? Biography { get; set; }

    /// <summary>Gets or sets the artist biography source URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Gets or sets the language of the cached biography.</summary>
    public string? ProfileLanguage { get; set; }

    /// <summary>Gets or sets the Unix timestamp of the cached artist profile.</summary>
    public long? ProfileFetchedAt { get; set; }

    /// <summary>Gets or sets whether the artist image was selected manually.</summary>
    public bool ImageIsManual { get; set; }

    /// <summary>Gets or sets the entity type used by the shared link and context handlers.</summary>
    public string EntityType { get; set; } = "Track";

    /// <summary>Gets the stable external identifier of the row, when known.</summary>
    public string? ExternalId { get; init; }

    /// <summary>Gets the Plex server identifier a Plex track/album/artist row belongs to, or <see langword="null"/>.</summary>
    public string? PlexServerId { get; init; }

    /// <summary>Gets the Plex album (parent) rating key for a Plex track row, or <see langword="null"/>.</summary>
    public string? PlexAlbumRatingKey { get; init; }

    /// <summary>Gets the Plex artist (grandparent) rating key for a Plex track row, or <see langword="null"/>.</summary>
    public string? PlexArtistRatingKey { get; init; }

    /// <summary>Gets or sets the remote Orynivo Server owning the row, or <see langword="null"/> for a local row.</summary>
    public OrynivoServerSettings? OrynivoServer { get; set; }

    /// <summary>Gets the stable source key (<c>local</c> or <c>server:&lt;id&gt;</c>) used by facets and smart playlists.</summary>
    public string SourceKey => OrynivoServer is null ? MainWindow.LocalSourceKey : MainWindow.GetServerSourceKey(OrynivoServer.Id);

    /// <summary>Gets the short source badge shown in the source column.</summary>
    public string SourceBadge => EntityType == "UnifiedArtist"
        ? $"{LocalizationManager.Current.LocalSourceShort}+OS"
        : EntityType == "UnifiedAlbum"
            ? LogicalAlbumParts?.Any(part => part.Server is null) == true
                ? $"{LocalizationManager.Current.LocalSourceShort}+OS"
                : "OS"
        : OrynivoServer is null ? LocalizationManager.Current.LocalSourceShort : "OS";

    /// <summary>Gets the readable source name used by tooltips and detail views.</summary>
    public string? SourceName => EntityType == "UnifiedArtist"
        ? $"{LocalizationManager.Current.LocalSource} + OS"
        : EntityType == "UnifiedAlbum"
            ? LogicalAlbumParts?.Any(part => part.Server is null) == true
                ? $"{LocalizationManager.Current.LocalSource} + OS"
                : string.Join(" + ", LogicalAlbumParts?
                    .Select(part => part.Server?.Name)
                    .OfType<string>()
                    .Distinct(StringComparer.CurrentCultureIgnoreCase) ?? [])
        : OrynivoServer?.Name ?? LocalizationManager.Current.LocalSource;

    private IImage? _artwork;
    private IImage? _thumbnail;

    /// <summary>Gets or sets whether a full-size artwork load is already queued for this row.</summary>
    public bool ArtworkLoadQueued { get; set; }

    /// <summary>Gets or sets whether the full-size artwork load finished.</summary>
    public bool ArtworkLoadCompleted { get; set; }

    /// <summary>Gets or sets whether a thumbnail load is already queued for this row.</summary>
    public bool ThumbnailLoadQueued { get; set; }

    /// <summary>Gets or sets whether the thumbnail load finished.</summary>
    public bool ThumbnailLoadCompleted { get; set; }

    /// <summary>Gets or sets the loaded full-size artwork.</summary>
    public IImage? Artwork
    {
        get => _artwork;
        set => SetField(ref _artwork, value);
    }

    /// <summary>Gets or sets the loaded artwork thumbnail.</summary>
    public IImage? Thumbnail
    {
        get => _thumbnail;
        set => SetField(ref _thumbnail, value);
    }

    private bool _isFavorite;

    /// <summary>Gets or sets the favorite state shown by the shared heart column.</summary>
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

    /// <summary>Gets the heart glyph matching the favorite state.</summary>
    public string FavoriteGlyph => IsFavorite ? "❤" : "♡";

    /// <summary>Gets the display duration.</summary>
    public string Duration { get; init; } = "";

    /// <summary>Gets the display audio format.</summary>
    public string? Format { get; init; }

    /// <summary>Gets the playable file path or authenticated stream URL.</summary>
    public string FilePath { get; init; } = "";

    /// <summary>Gets the physical source path for a virtual CUE or MKA track.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Gets the ordered Plex media part URLs of a multi-part track.</summary>
    public IReadOnlyList<string>? PlexPartUrls { get; init; }

    /// <summary>Gets the known duration used to skip an FFprobe on remote tracks.</summary>
    public TimeSpan? KnownDuration { get; init; }

    /// <summary>Gets the duration used for table sorting.</summary>
    public TimeSpan DurationSort => KnownDuration ?? ParseDisplayDuration(Duration);

    /// <summary>Gets or sets the playlist entry identifier for a regular playlist row.</summary>
    public long? PlaylistEntryId { get; set; }

    /// <summary>Gets or sets the editable-queue item backing this row.</summary>
    public PlaylistItem? QueueItem { get; set; }

    private static int ParseLeadingInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return int.MaxValue;
        var digits = new string(value.Trim().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ? result : int.MaxValue;
    }

    private static int ParseDisplayNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return int.MaxValue;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ? result : int.MaxValue;
    }

    private static double ParseDisplayDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return double.PositiveInfinity;
        var token = new string(value.Trim().TakeWhile(character =>
            char.IsDigit(character) || character is '+' or '-' or '.' or ',').ToArray());
        return double.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out var result) ||
               double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            ? result
            : double.PositiveInfinity;
    }

    private static long ParseFileSize(string? value)
    {
        var number = ParseDisplayDouble(value);
        if (!double.IsFinite(number) || string.IsNullOrWhiteSpace(value)) return long.MaxValue;
        var multiplier = value.Contains("TB", StringComparison.OrdinalIgnoreCase) ? 1L << 40
            : value.Contains("GB", StringComparison.OrdinalIgnoreCase) ? 1L << 30
            : value.Contains("MB", StringComparison.OrdinalIgnoreCase) ? 1L << 20
            : value.Contains("KB", StringComparison.OrdinalIgnoreCase) ? 1L << 10
            : 1L;
        return (long)Math.Min(long.MaxValue, number * multiplier);
    }

    private static DateTime ParseDisplayDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var result) ||
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)
            ? result : DateTime.MaxValue;

    private static TimeSpan ParseDisplayDuration(string? value)
    {
        var parts = value?.Split(':');
        if (parts is { Length: 2 } &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return TimeSpan.TryParse(value, CultureInfo.CurrentCulture, out var result) ||
               TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out result)
            ? result : TimeSpan.MaxValue;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
