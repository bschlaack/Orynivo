namespace Orynivo.Library;

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
