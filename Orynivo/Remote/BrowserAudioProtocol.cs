namespace Orynivo.Remote;

/// <summary>Describes a transient browser output without exposing source paths or authentication.</summary>
/// <param name="ClientId">Page instance owning this output.</param>
/// <param name="StreamId">Current opaque stream revision, or null while idle.</param>
/// <param name="StartSeconds">Logical offset at the beginning of the current MP3 stream.</param>
/// <param name="Paused">Whether playback is requested to pause.</param>
/// <param name="Volume">Requested browser gain.</param>
/// <param name="ControlRevision">Version rejecting feedback from earlier commands.</param>
/// <param name="Direct">Whether the original compatible media is being relayed.</param>
/// <param name="SeekSeconds">Native media-element seek target for direct file playback.</param>
/// <param name="Format">Actual delivered audio format.</param>
/// <param name="FormatId">Selected music delivery preference.</param>
public sealed record BrowserAudioState(string ClientId, string? StreamId, double StartSeconds,
    bool Paused, double Volume, long ControlRevision, bool Direct = false, double SeekSeconds = 0, string? Format = null, string? FormatId = null);

/// <summary>Reports media-element progress for exactly one browser-owned stream revision.</summary>
/// <param name="ClientId">Owning page.</param>
/// <param name="StreamId">Opaque stream revision.</param>
/// <param name="ControlRevision">Last applied command version.</param>
/// <param name="PositionSeconds">Logical audible position.</param>
/// <param name="Playing">Whether the element is actually rendering audio.</param>
/// <param name="Ended">Whether the media element reached its natural end.</param>
/// <param name="Failed">Whether the element encountered an unrecoverable media error.</param>
/// <param name="DurationSeconds">Optional finite media duration discovered by the browser.</param>
public sealed record BrowserAudioReport(string ClientId, string StreamId, long ControlRevision,
    double PositionSeconds, bool Playing, bool Ended = false, bool Failed = false, double? DurationSeconds = null);

/// <summary>Claims or releases the transient output for a page instance.</summary>
/// <param name="ClientId">Random page identifier held only in browser memory.</param>
/// <param name="FormatId">Optional validated music preference; does not affect radio or podcasts.</param>
public sealed record BrowserAudioSelection(string ClientId, string? FormatId = null);
