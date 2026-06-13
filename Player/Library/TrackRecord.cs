namespace Player.Library;

/// <summary>
/// Repräsentiert einen Track-Eintrag in der Audio-Datenbank.
/// Alle nullable-Felder sind optional und können fehlen, wenn die Metadaten nicht vorhanden sind.
/// </summary>
public sealed class TrackRecord
{
    // --- Primärschlüssel ---
    public long   Id               { get; set; }

    // --- Dateisystem ---
    public string Path             { get; set; } = string.Empty;
    public string FileName         { get; set; } = string.Empty;
    public long?  FileSize         { get; set; }   // Bytes
    public long   ModifiedAt       { get; set; }   // Unix-Timestamp (Sekunden)
    public long   AddedAt          { get; set; }   // Unix-Timestamp (Sekunden)

    // --- Technische Audio-Metadaten ---
    public string? Format          { get; set; }   // "flac", "mp3", "dsf", "dff", "aac", …
    public double? Duration        { get; set; }   // Sekunden
    public int?    SampleRate      { get; set; }   // Hz, z.B. 44100, 96000, 2 822 400 (DSD64)
    public int?    BitDepth        { get; set; }   // z.B. 16, 24, 32 – null bei lossy/DSD
    public int?    Channels        { get; set; }   // 1 = Mono, 2 = Stereo, …
    public int?    Bitrate         { get; set; }   // kbps (nur bei komprimierten Formaten)
    public bool    IsLossless      { get; set; }
    public bool    IsDsd           { get; set; }
    public int?    DsdRate         { get; set; }   // Multiplikator: 64, 128, 256, 512

    // --- ID3 / Allgemeine Tags ---
    public string? Title           { get; set; }   // TIT2
    public string? SortTitle       { get; set; }   // TSOT
    public string? Artist          { get; set; }   // TPE1 – Leadinterpret
    public string? SortArtist      { get; set; }   // TSOP
    public string? AlbumArtist     { get; set; }   // TPE2
    public string? SortAlbumArtist { get; set; }   // TSO2
    public string? Album           { get; set; }   // TALB
    public string? SortAlbum       { get; set; }   // TSOA
    public string? Genre           { get; set; }   // TCON
    public int?    Year            { get; set; }   // TYER / TDRC (nur Jahreszahl)
    public string? Date            { get; set; }   // TDRC vollständig, z.B. "2003-08-15"
    public int?    TrackNumber     { get; set; }   // TRCK – Nummer
    public int?    TrackTotal      { get; set; }   // TRCK – Gesamtzahl, z.B. "3/12" → 12
    public int?    DiscNumber      { get; set; }   // TPOS – Nummer
    public int?    DiscTotal       { get; set; }   // TPOS – Gesamtzahl
    public string? Composer        { get; set; }   // TCOM
    public string? Conductor       { get; set; }   // TPE3
    public string? Lyricist        { get; set; }   // TEXT
    public string? Lyrics          { get; set; }   // USLT – unsynchronisierte Liedtexte
    public string? DownloadedLyrics { get; set; }
    public string? SyncedLyrics     { get; set; }   // LRC – zeitcodierte Liedtexte
    public string? LyricsSource     { get; set; }
    public long?   LyricsFetchedAt  { get; set; }   // Unix-Timestamp (Sekunden)
    public string? Comment         { get; set; }   // COMM
    public string? Copyright       { get; set; }   // TCOP
    public string? Publisher       { get; set; }   // TPUB
    public string? EncodedBy       { get; set; }   // TENC
    public string? EncodingSettings{ get; set; }   // TSSE
    public int?    Bpm             { get; set; }   // TBPM
    public bool    Compilation     { get; set; }   // TCMP (iTunes-Erweiterung)
    public string? Isrc            { get; set; }   // TSRC – International Standard Recording Code
    public string? Language        { get; set; }   // TLAN
    public string? Mood            { get; set; }   // TMOO
    public string? ReplayGainTrack { get; set; }   // TXXX:REPLAYGAIN_TRACK_GAIN, z.B. "-6.54 dB"
    public string? ReplayGainAlbum { get; set; }   // TXXX:REPLAYGAIN_ALBUM_GAIN

    // --- MusicBrainz / AcoustID ---
    public string? MusicBrainzTrackId   { get; set; }
    public string? MusicBrainzReleaseId { get; set; }
    public string? MusicBrainzArtistId  { get; set; }
    public string? AcoustIdFingerprint  { get; set; }

    // --- Cover Art ---
    public bool    HasCover        { get; set; }
    public string? CoverMimeType   { get; set; }   // "image/jpeg", "image/png"
    public byte[]? CoverData       { get; set; }   // eingebettetes Cover (optional)
}
