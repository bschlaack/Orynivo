namespace Orynivo.Library;

/// <summary>
/// A personally saved internet radio station from the Radio Browser directory.
/// </summary>
/// <param name="Id">Local database row ID.</param>
/// <param name="StationUuid">Stable UUID of the station in the Radio Browser database.</param>
/// <param name="Name">Display name of the station.</param>
/// <param name="StreamUrl">Direct stream URL.</param>
/// <param name="Homepage">Optional homepage URL of the station.</param>
/// <param name="Favicon">Optional URL to the station logo.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2 country code, e.g. <c>DE</c>.</param>
/// <param name="Codec">Audio codec of the stream, e.g. <c>MP3</c> or <c>AAC</c>.</param>
/// <param name="Bitrate">Stream bitrate in kbps.</param>
/// <param name="Tags">Comma-separated genre and keyword tags.</param>
public sealed record RadioStationRecord(
    long Id,
    string StationUuid,
    string Name,
    string StreamUrl,
    string? Homepage,
    string? Favicon,
    string? CountryCode,
    string? Codec,
    int Bitrate,
    string? Tags);
