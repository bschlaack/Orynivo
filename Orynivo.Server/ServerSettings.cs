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

    /// <summary>Gets or sets whether authenticated clients may stage and request signed package updates.</summary>
    public bool AllowRemoteUpdates { get; set; }

    /// <summary>
    /// Gets or sets the human-readable display name of this server instance,
    /// returned by the <c>/api/info</c> endpoint.
    /// </summary>
    public string ServerName { get; set; } = "Orynivo Server";
}
