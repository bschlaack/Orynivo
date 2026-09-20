namespace Orynivo.Server;

/// <summary>
/// Root configuration section for the Orynivo Server, read from the
/// <c>Orynivo</c> key in <c>appsettings.json</c>.
/// </summary>
public sealed class ServerSettings
{
    /// <summary>
    /// Gets or sets the pre-shared API key that clients must supply in the
    /// <c>X-Api-Key</c> header or the <c>key</c> query-string parameter.
    /// Set this to a long random string before the first run.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of root directories the server will scan for audio files.
    /// </summary>
    public List<string> LibraryPaths { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a full library scan should run automatically
    /// on server startup. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ScanOnStartup { get; set; } = true;

    /// <summary>
    /// Gets or sets whether normal server scans should run FFmpeg analysis for
    /// tracks without embedded ReplayGain values. Defaults to <see langword="false"/>
    /// so large and chaptered media files do not hold up library discovery.
    /// </summary>
    public bool CalculateMissingReplayGainDuringScan { get; set; }

    /// <summary>Gets or sets the FFmpeg thread limit for server ReplayGain analysis.</summary>
    public int ReplayGainFfmpegThreads { get; set; } = 1;

    /// <summary>Gets or sets the pause in milliseconds between server ReplayGain track analyses.</summary>
    public int ReplayGainDelayMilliseconds { get; set; } = 250;

    /// <summary>Gets or sets whether authenticated clients may stage and request signed package updates.</summary>
    public bool AllowRemoteUpdates { get; set; }

    /// <summary>
    /// Gets or sets the human-readable display name of this server instance,
    /// returned by the <c>/api/info</c> endpoint.
    /// </summary>
    public string ServerName { get; set; } = "Orynivo Server";

    /// <summary>Gets or sets the profiles allowed to store personal server state.</summary>
    public List<ServerProfile> Profiles { get; set; } = [new ServerProfile()];

    /// <summary>
    /// Gets or sets the optional automatic server-side library backup schedule.
    /// It holds no credentials and defaults to disabled.
    /// </summary>
    public BackupScheduleSettings BackupSchedule { get; set; } = new();
}

/// <summary>
/// Optional automatic server-side library backup configuration. The schedule and
/// retention decisions come from the shared <c>BackupRetention</c> helper, and the
/// last run is derived from the newest archive in the target folder, so no extra
/// state has to be persisted.
/// </summary>
public sealed class BackupScheduleSettings
{
    /// <summary>Gets or sets a value indicating whether the server creates backups automatically.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the minimum number of days between automatic backups.</summary>
    public int IntervalDays { get; set; } = 7;

    /// <summary>Gets or sets how many automatic backups are kept before older ones are removed.</summary>
    public int RetentionCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the backup folder; an empty value uses a <c>backups</c> folder
    /// below the server data directory.
    /// </summary>
    public string Directory { get; set; } = string.Empty;
}

/// <summary>Stable server-side profile identity shared with a desktop client.</summary>
public sealed class ServerProfile
{
    /// <summary>Gets or sets the profile identifier.</summary>
    public string Id { get; set; } = "standard";

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = "Standard";
}
