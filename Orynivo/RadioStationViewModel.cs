using Orynivo.Library;

namespace Orynivo;

/// <summary>
/// One radio search result or saved station row. It lives outside the main window so
/// XAML can name it in an <c>x:DataType</c> directive, which compiled bindings require
/// (a nested type cannot be referenced from XAML).
/// </summary>
internal sealed class RadioStationViewModel
{
    /// <summary>Gets the stable Radio Browser station identifier.</summary>
    public required string StationUuid { get; init; }

    /// <summary>Gets the station name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the stream URL.</summary>
    public required string StreamUrl { get; init; }

    /// <summary>Gets the station homepage, when known.</summary>
    public string? Homepage { get; init; }

    /// <summary>Gets the station favicon URL, when known.</summary>
    public string? Favicon { get; init; }

    /// <summary>Gets the country code, when known.</summary>
    public string? CountryCode { get; init; }

    /// <summary>Gets the audio codec, when known.</summary>
    public string? Codec { get; init; }

    /// <summary>Gets the stream bitrate in kbps, or zero when unknown.</summary>
    public int Bitrate { get; init; }

    /// <summary>Gets the raw Radio Browser tags.</summary>
    public string? Tags { get; init; }

    /// <summary>Gets the normalized genres derived from the tags.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Gets the compact codec and bitrate summary.</summary>
    public string FormatSummary => Bitrate > 0
        ? $"{Codec ?? "Audio"} - {Bitrate} kbps"
        : Codec ?? "Audio";

    /// <summary>Gets the localized bitrate summary.</summary>
    public string BitrateSummary => Bitrate > 0 ? $"{Bitrate:N0} kbps" : string.Empty;

    /// <summary>Gets the first three genres joined for display.</summary>
    public string GenreSummary => string.Join(", ", Genres.Take(3));

    /// <summary>Converts the row back into a Radio Browser station record.</summary>
    /// <returns>The station record.</returns>
    public RadioBrowserStation ToBrowserStation() =>
        new(StationUuid, Name, StreamUrl, Homepage, Favicon, CountryCode, Codec, Bitrate, Tags);

    /// <summary>Converts the row into a persisted personal station record.</summary>
    /// <param name="id">Database identifier, or zero for a new record.</param>
    /// <returns>The personal station record.</returns>
    public RadioStationRecord ToRecord(long id = 0) =>
        new(id, StationUuid, Name, StreamUrl, Homepage, Favicon, CountryCode, Codec, Bitrate, Tags);
}
