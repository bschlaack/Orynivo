namespace Orynivo.Library;

/// <summary>
/// Represents a track entry in the audio database.
/// All nullable fields are optional and may be absent when the corresponding metadata is not available.
/// </summary>
public sealed class TrackRecord
{
    /// <summary>Primary key of the <c>tracks</c> table.</summary>
    public long   Id               { get; set; }

    /// <summary>Absolute file path.</summary>
    public string Path             { get; set; } = string.Empty;

    /// <summary>
    /// Physical audio source. For ordinary tracks this equals <see cref="Path"/>;
    /// CUE tracks use a stable virtual <see cref="Path"/> and share this source file.
    /// </summary>
    public string SourcePath       { get; set; } = string.Empty;

    /// <summary>Optional CUE sheet that defines this virtual track.</summary>
    public string? CuePath         { get; set; }

    /// <summary>Zero-based segment start in the physical source, in seconds.</summary>
    public double? SegmentStart    { get; set; }

    /// <summary>Exclusive segment end in the physical source, in seconds.</summary>
    public double? SegmentEnd      { get; set; }

    /// <summary>File name including extension.</summary>
    public string FileName         { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long?  FileSize         { get; set; }

    /// <summary>Last-modified time of the file as a Unix timestamp in seconds (UTC).</summary>
    public long   ModifiedAt       { get; set; }

    /// <summary>Time the track was first added to the library as a Unix timestamp in seconds (UTC).</summary>
    public long   AddedAt          { get; set; }

    /// <summary>Container format in lowercase, e.g. <c>flac</c>, <c>mp3</c>, <c>dsf</c>.</summary>
    public string? Format          { get; set; }

    /// <summary>Duration in seconds.</summary>
    public double? Duration        { get; set; }

    /// <summary>Sample rate in Hz, e.g. 44100, 96000, or 2822400 for DSD64.</summary>
    public int?    SampleRate      { get; set; }

    /// <summary>Bit depth, e.g. 16, 24, 32; <see langword="null"/> for lossy or DSD formats.</summary>
    public int?    BitDepth        { get; set; }

    /// <summary>Channel count: 1 = mono, 2 = stereo.</summary>
    public int?    Channels        { get; set; }

    /// <summary>Encoded bitrate in kbps; only meaningful for compressed formats.</summary>
    public int?    Bitrate         { get; set; }

    /// <summary><see langword="true"/> for lossless formats (FLAC, WAV, AIFF, DSF, DFF).</summary>
    public bool    IsLossless      { get; set; }

    /// <summary><see langword="true"/> for DSD files (DSF, DFF).</summary>
    public bool    IsDsd           { get; set; }

    /// <summary>DSD multiplier: 64, 128, 256, or 512; <see langword="null"/> for non-DSD files.</summary>
    public int?    DsdRate         { get; set; }

    /// <summary>Track title (ID3 TIT2).</summary>
    public string? Title           { get; set; }

    /// <summary>Sort title (ID3 TSOT).</summary>
    public string? SortTitle       { get; set; }

    /// <summary>Lead performer / artist (ID3 TPE1).</summary>
    public string? Artist          { get; set; }

    /// <summary>Sort artist (ID3 TSOP).</summary>
    public string? SortArtist      { get; set; }

    /// <summary>Album artist (ID3 TPE2).</summary>
    public string? AlbumArtist     { get; set; }

    /// <summary>Sort album artist (ID3 TSO2).</summary>
    public string? SortAlbumArtist { get; set; }

    /// <summary>Album title (ID3 TALB).</summary>
    public string? Album           { get; set; }

    /// <summary>Sort album (ID3 TSOA).</summary>
    public string? SortAlbum       { get; set; }

    /// <summary>Genre (ID3 TCON).</summary>
    public string? Genre           { get; set; }

    /// <summary>Release year (ID3 TYER / TDRC year part).</summary>
    public int?    Year            { get; set; }

    /// <summary>Full release date string, e.g. <c>2003-08-15</c> (ID3 TDRC).</summary>
    public string? Date            { get; set; }

    /// <summary>Track number (ID3 TRCK numerator).</summary>
    public int?    TrackNumber     { get; set; }

    /// <summary>Total track count on the disc (ID3 TRCK denominator, e.g. 12 from "3/12").</summary>
    public int?    TrackTotal      { get; set; }

    /// <summary>Disc number (ID3 TPOS numerator).</summary>
    public int?    DiscNumber      { get; set; }

    /// <summary>Total disc count (ID3 TPOS denominator).</summary>
    public int?    DiscTotal       { get; set; }

    /// <summary>Composer (ID3 TCOM).</summary>
    public string? Composer        { get; set; }

    /// <summary>Conductor (ID3 TPE3).</summary>
    public string? Conductor       { get; set; }

    /// <summary>Lyricist (ID3 TEXT).</summary>
    public string? Lyricist        { get; set; }

    /// <summary>Unsynchronised embedded lyrics (ID3 USLT).</summary>
    public string? Lyrics          { get; set; }

    /// <summary>Plain lyrics downloaded from LRCLIB; overrides embedded lyrics in the lyrics view.</summary>
    public string? DownloadedLyrics { get; set; }

    /// <summary>LRC-formatted synchronised lyrics downloaded from LRCLIB.</summary>
    public string? SyncedLyrics     { get; set; }

    /// <summary>Source label for the downloaded lyrics, e.g. <c>lrclib</c>.</summary>
    public string? LyricsSource     { get; set; }

    /// <summary>Timestamp of the last lyrics download attempt as a Unix timestamp in seconds (UTC).</summary>
    public long?   LyricsFetchedAt  { get; set; }

    /// <summary>Comment (ID3 COMM).</summary>
    public string? Comment         { get; set; }

    /// <summary>Copyright (ID3 TCOP).</summary>
    public string? Copyright       { get; set; }

    /// <summary>Publisher / record label (ID3 TPUB).</summary>
    public string? Publisher       { get; set; }

    /// <summary>Encoded-by field (ID3 TENC).</summary>
    public string? EncodedBy       { get; set; }

    /// <summary>Encoding settings (ID3 TSSE).</summary>
    public string? EncodingSettings{ get; set; }

    /// <summary>Beats per minute (ID3 TBPM).</summary>
    public int?    Bpm             { get; set; }

    /// <summary><see langword="true"/> when part of a compilation (iTunes TCMP extension).</summary>
    public bool    Compilation     { get; set; }

    /// <summary>International Standard Recording Code (ID3 TSRC).</summary>
    public string? Isrc            { get; set; }

    /// <summary>Language of the lyrics (ID3 TLAN).</summary>
    public string? Language        { get; set; }

    /// <summary>Mood (ID3 TMOO).</summary>
    public string? Mood            { get; set; }

    /// <summary>Track-level ReplayGain value, e.g. <c>-6.54 dB</c> (TXXX:REPLAYGAIN_TRACK_GAIN).</summary>
    public string? ReplayGainTrack { get; set; }

    /// <summary>Album-level ReplayGain value (TXXX:REPLAYGAIN_ALBUM_GAIN).</summary>
    public string? ReplayGainAlbum { get; set; }

    /// <summary>MusicBrainz track/recording ID.</summary>
    public string? MusicBrainzTrackId   { get; set; }

    /// <summary>MusicBrainz release ID; used for Cover Art Archive lookups.</summary>
    public string? MusicBrainzReleaseId { get; set; }

    /// <summary>MusicBrainz artist ID.</summary>
    public string? MusicBrainzArtistId  { get; set; }

    /// <summary>AcoustID audio fingerprint.</summary>
    public string? AcoustIdFingerprint  { get; set; }

    /// <summary><see langword="true"/> when the file contains embedded cover art.</summary>
    public bool    HasCover        { get; set; }

    /// <summary>MIME type of the embedded cover, e.g. <c>image/jpeg</c>.</summary>
    public string? CoverMimeType   { get; set; }

    /// <summary>Raw bytes of the embedded cover image; <see langword="null"/> when not loaded or absent.</summary>
    public byte[]? CoverData       { get; set; }
}
