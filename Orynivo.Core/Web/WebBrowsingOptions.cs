namespace Orynivo.Web;

/// <summary>
/// Configuration for <see cref="WebBrowsingService"/>, persisted as part of the
/// application settings. It controls the SearXNG search endpoint and the safety
/// limits applied to arbitrary page fetches (timeout, response size, redirects,
/// SSRF protection, and an optional domain allowlist).
/// </summary>
public sealed class WebBrowsingOptions
{
    /// <summary>Gets or sets a value indicating whether the web tools are enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the base URL of the SearXNG instance used for searches
    /// (for example a local or self-hosted SearXNG instance). The instance must
    /// have the JSON output format enabled. This endpoint is trusted and is
    /// therefore not subject to the private-network SSRF guard.
    /// </summary>
    public string SearxngUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the default maximum number of search results returned.</summary>
    public int MaxResults { get; set; } = 5;

    /// <summary>Gets or sets the per-request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Gets or sets the maximum response size read from a page, in kilobytes.</summary>
    public int MaxResponseKilobytes { get; set; } = 2048;

    /// <summary>Gets or sets the maximum number of HTTP redirects followed per fetch.</summary>
    public int MaxRedirects { get; set; } = 5;

    /// <summary>
    /// Gets or sets a value indicating whether requests to private, loopback, and
    /// link-local addresses are blocked to prevent SSRF. Applies to page fetches
    /// only, never to the trusted SearXNG endpoint.
    /// </summary>
    public bool BlockPrivateNetworks { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional allowlist of domains. When non-empty, page fetches
    /// are restricted to these domains and their subdomains.
    /// </summary>
    public List<string> DomainAllowlist { get; set; } = [];
}
