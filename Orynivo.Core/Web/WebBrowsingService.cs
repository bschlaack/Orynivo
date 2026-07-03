using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Orynivo.Web;

/// <summary>
/// Raised when a request is rejected by the web-browsing safety guards
/// (blocked address, disallowed scheme, allowlist miss, or too many redirects).
/// </summary>
public sealed class WebBrowsingException : Exception
{
    /// <summary>Initializes a new instance with the given message.</summary>
    /// <param name="message">The human-readable failure reason.</param>
    public WebBrowsingException(string message) : base(message)
    {
    }
}

/// <summary>
/// Provides a small, controlled set of internet capabilities to language-model
/// tools: SearXNG search plus safe page fetching as plain text or Markdown.
///
/// Security model: page fetches are hardened against SSRF. Only http/https is
/// allowed, connections to private, loopback, and link-local addresses are
/// refused at connect time (closing the DNS-rebinding window), redirects are
/// followed manually with a limit, responses are size-capped, non-text content
/// types are refused (no arbitrary downloads), and every request is logged. The
/// configured SearXNG endpoint is trusted and bypasses the private-network guard
/// so a LAN/Docker instance remains reachable.
/// </summary>
public sealed class WebBrowsingService : IDisposable
{
    private const string UserAgent = "Orynivo/1.0 (+web-tools)";
    private const int MaxOutputChars = 15000;

    private readonly HttpClient _searchClient;
    private readonly HttpClient _fetchClient;

    /// <summary>Initializes the service with the given options.</summary>
    /// <param name="options">The initial browsing options. May be replaced later via <see cref="Options"/>.</param>
    public WebBrowsingService(WebBrowsingOptions options)
    {
        Options = options;

        _searchClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _fetchClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = SafeConnectAsync,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _searchClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _fetchClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    /// <summary>Gets or sets the active browsing options. Read at the start of each request.</summary>
    public WebBrowsingOptions Options { get; set; }

    /// <summary>Gets or sets an optional sink that receives one line per request for auditing.</summary>
    public Action<string>? RequestLog { get; set; }

    /// <summary>Searches the web through the configured SearXNG instance.</summary>
    /// <param name="query">The free-text query.</param>
    /// <param name="maxResults">Maximum number of results (falls back to the configured default when non-positive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A formatted result list, or a readable error message.</returns>
    public async Task<string> SearchAsync(string query, int maxResults, CancellationToken ct = default)
    {
        if (!Options.Enabled) return "Web browsing is disabled in Settings.";
        if (string.IsNullOrWhiteSpace(query)) return "Provide a non-empty search query.";

        var baseUrl = Options.SearxngUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "No SearXNG URL is configured. Set it in Settings → Integration → MCP Server.";

        var limit = Math.Clamp(maxResults <= 0 ? Options.MaxResults : maxResults, 1, 25);
        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query)}&format=json&safesearch=1";
            using var timeoutCts = CreateTimeoutScope(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await _searchClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
            Log($"search '{query}' (limit {limit}) -> {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode)
                return $"Search failed: SearXNG returned HTTP {(int)response.StatusCode}. " +
                       "Ensure the instance exposes the JSON API (search.formats includes 'json').";
            var (body, _) = await ReadLimitedAsync(response, timeoutCts.Token).ConfigureAwait(false);
            return FormatSearchResults(query, body, limit);
        }
        catch (Exception ex)
        {
            return DescribeError("search_web", query, ex);
        }
    }

    /// <summary>Fetches a page and returns its readable plain text.</summary>
    /// <param name="url">The absolute http/https URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page text (title + body), or a readable error message.</returns>
    public async Task<string> FetchTextAsync(string url, CancellationToken ct = default)
    {
        if (!Options.Enabled) return "Web browsing is disabled in Settings.";
        try
        {
            var result = await FetchGuardedAsync(url, ct).ConfigureAwait(false);
            Log($"fetch_page {url} -> {result.FinalUrl} ({result.Body.Length} bytes{(result.Truncated ? ", truncated" : "")})");
            var title = HtmlContentExtractor.ExtractTitle(result.Body);
            var text = HtmlContentExtractor.ToText(result.Body, result.MediaType);
            return FormatPage(result.FinalUrl, title, text, result.Truncated);
        }
        catch (Exception ex)
        {
            return DescribeError("fetch_page", url, ex);
        }
    }

    /// <summary>Fetches a page and returns a compact Markdown rendering.</summary>
    /// <param name="url">The absolute http/https URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page as Markdown (title + body), or a readable error message.</returns>
    public async Task<string> FetchMarkdownAsync(string url, CancellationToken ct = default)
    {
        if (!Options.Enabled) return "Web browsing is disabled in Settings.";
        try
        {
            var result = await FetchGuardedAsync(url, ct).ConfigureAwait(false);
            Log($"fetch_page_as_markdown {url} -> {result.FinalUrl} ({result.Body.Length} bytes{(result.Truncated ? ", truncated" : "")})");
            var title = HtmlContentExtractor.ExtractTitle(result.Body);
            var markdown = HtmlContentExtractor.ToMarkdown(result.Body, result.MediaType);
            return FormatPage(result.FinalUrl, title, markdown, result.Truncated);
        }
        catch (Exception ex)
        {
            return DescribeError("fetch_page_as_markdown", url, ex);
        }
    }

    /// <summary>Performs the SSRF-guarded fetch, following redirects manually.</summary>
    /// <param name="url">The requested URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The final URL, media type, body, and truncation flag.</returns>
    private async Task<FetchResult> FetchGuardedAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
            throw new WebBrowsingException($"Invalid URL: '{url}'.");

        var maxRedirects = Math.Clamp(Options.MaxRedirects, 0, 20);
        var current = uri;
        for (var redirect = 0; ; redirect++)
        {
            ValidateFetchTarget(current);
            using var timeoutCts = CreateTimeoutScope(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.9,*/*;q=0.1");
            var response = await _fetchClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
            try
            {
                var status = (int)response.StatusCode;
                if (status is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    if (redirect >= maxRedirects)
                        throw new WebBrowsingException($"Too many redirects (> {maxRedirects}) for '{url}'.");
                    current = new Uri(current, location);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!IsTextualMediaType(mediaType))
                    throw new WebBrowsingException(
                        $"Refusing content type '{mediaType}' at {current}: only HTML/text pages are fetched.");

                var (body, truncated) = await ReadLimitedAsync(response, timeoutCts.Token).ConfigureAwait(false);
                return new FetchResult(current, mediaType, body, truncated);
            }
            finally
            {
                response.Dispose();
            }
        }
    }

    /// <summary>Validates the scheme and optional allowlist for a fetch target.</summary>
    /// <param name="uri">The target URI.</param>
    private void ValidateFetchTarget(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new WebBrowsingException($"Only http/https URLs are allowed (got '{uri.Scheme}').");

        var allowlist = Options.DomainAllowlist;
        if (allowlist is { Count: > 0 })
        {
            var host = uri.Host;
            var permitted = allowlist.Any(domain =>
                !string.IsNullOrWhiteSpace(domain) &&
                (host.Equals(domain.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 host.EndsWith("." + domain.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (!permitted)
                throw new WebBrowsingException($"Domain '{uri.Host}' is not in the configured allowlist.");
        }
    }

    /// <summary>
    /// Custom connect callback for the fetch client. Resolves the host, refuses
    /// private/reserved addresses (when enabled), and connects to a public address,
    /// which prevents DNS-rebinding between validation and connection.
    /// </summary>
    /// <param name="context">The connection context supplied by the handler.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A connected network stream.</returns>
    private async ValueTask<Stream> SafeConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

        var blockPrivate = Options.BlockPrivateNetworks;
        var target = addresses.FirstOrDefault(address => !blockPrivate || !IsPrivateOrReserved(address));
        if (target is null)
            throw new WebBrowsingException($"Blocked '{host}': it resolves only to private or reserved addresses.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(target, port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Determines whether an IP address is loopback, private, link-local, or otherwise reserved.</summary>
    /// <param name="address">The address to classify.</param>
    /// <returns><see langword="true"/> when the address must not be reached by fetches.</returns>
    private static bool IsPrivateOrReserved(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0                                        // 0.0.0.0/8
                || b[0] == 10                                       // 10.0.0.0/8 private
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)       // 100.64.0.0/10 CGNAT
                || b[0] == 127                                      // 127.0.0.0/8 loopback
                || (b[0] == 169 && b[1] == 254)                     // 169.254.0.0/16 link-local (incl. cloud metadata)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)        // 172.16.0.0/12 private
                || (b[0] == 192 && b[1] == 168)                     // 192.168.0.0/16 private
                || b[0] >= 224;                                     // 224.0.0.0/4 multicast + reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return true;
            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC)                              // fc00::/7 unique local
                return true;
            if (address.Equals(IPAddress.IPv6Any))                 // ::
                return true;
        }

        return false;
    }

    /// <summary>Reads the response body up to the configured size cap.</summary>
    /// <param name="response">The HTTP response.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The decoded body and a flag indicating whether it was truncated.</returns>
    private async Task<(string Body, bool Truncated)> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var maxBytes = Math.Clamp(Options.MaxResponseKilobytes, 16, 20480) * 1024;
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var truncated = false;
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            var remaining = maxBytes - (int)buffer.Length;
            if (read >= remaining)
            {
                if (remaining > 0) buffer.Write(chunk, 0, remaining);
                truncated = true;
                break;
            }
            buffer.Write(chunk, 0, read);
        }

        var encoding = ResolveEncoding(response.Content.Headers.ContentType?.CharSet);
        return (encoding.GetString(buffer.ToArray()), truncated);
    }

    /// <summary>Resolves a text encoding from a charset name, defaulting to UTF-8.</summary>
    /// <param name="charset">The charset from the content type, or <see langword="null"/>.</param>
    /// <returns>The resolved encoding.</returns>
    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
            return Encoding.UTF8;
        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"'));
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    /// <summary>Determines whether a media type is textual and therefore safe to render.</summary>
    /// <param name="mediaType">The response media type.</param>
    /// <returns><see langword="true"/> for HTML, XHTML, plain text, and similar.</returns>
    private static bool IsTextualMediaType(string mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
            return true; // Some servers omit it; treat as text and let extraction handle it.
        return mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Builds a linked cancellation scope that also enforces the request timeout.</summary>
    /// <param name="ct">The caller's cancellation token.</param>
    /// <returns>A cancellation source that fires on cancellation or timeout.</returns>
    private CancellationTokenSource CreateTimeoutScope(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Options.TimeoutSeconds, 1, 120)));
        return cts;
    }

    /// <summary>Parses SearXNG JSON and formats the top results for a language model.</summary>
    /// <param name="query">The original query, echoed in the header.</param>
    /// <param name="json">The SearXNG JSON body.</param>
    /// <param name="limit">Maximum number of results to include.</param>
    /// <returns>A formatted, numbered result list.</returns>
    private static string FormatSearchResults(string query, string json, int limit)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch
        {
            return "Search returned a non-JSON response. Enable the JSON format on your SearXNG instance.";
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                return $"No results for \"{query}\".";
            }

            var sb = new StringBuilder();
            sb.Append("Search results for \"").Append(query).Append("\":");
            var count = 0;
            foreach (var result in results.EnumerateArray())
            {
                if (count >= limit) break;
                var link = GetString(result, "url");
                if (string.IsNullOrWhiteSpace(link)) continue;
                var title = GetString(result, "title");
                var content = GetString(result, "content");
                count++;
                sb.Append("\n\n").Append(count).Append(". ")
                  .Append(string.IsNullOrWhiteSpace(title) ? link : title);
                sb.Append("\n   ").Append(link);
                if (!string.IsNullOrWhiteSpace(content))
                    sb.Append("\n   ").Append(Truncate(content, 300));
            }

            return count == 0 ? $"No results found for \"{query}\"." : sb.ToString();
        }
    }

    /// <summary>Formats a fetched page for tool output, truncating overly long bodies.</summary>
    /// <param name="finalUrl">The final URL after redirects.</param>
    /// <param name="title">The extracted document title.</param>
    /// <param name="body">The extracted text or Markdown.</param>
    /// <param name="responseTruncated">Whether the raw response was already size-capped.</param>
    /// <returns>The formatted page string.</returns>
    private static string FormatPage(Uri finalUrl, string title, string body, bool responseTruncated)
    {
        var outputTruncated = body.Length > MaxOutputChars;
        if (outputTruncated)
            body = body[..MaxOutputChars];

        var sb = new StringBuilder();
        sb.Append("URL: ").Append(finalUrl);
        if (!string.IsNullOrWhiteSpace(title))
            sb.Append("\nTitle: ").Append(title);
        sb.Append("\n\n").Append(body.Trim());
        if (responseTruncated || outputTruncated)
            sb.Append("\n\n[content truncated]");
        return sb.ToString();
    }

    /// <summary>Reads a string property from a JSON element, or an empty string.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The string value, or an empty string.</returns>
    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>Truncates a string to a maximum length, appending an ellipsis when cut.</summary>
    /// <param name="text">The input text.</param>
    /// <param name="max">The maximum length.</param>
    /// <returns>The possibly-truncated text.</returns>
    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    /// <summary>Turns an exception into a concise, model-friendly error string.</summary>
    /// <param name="tool">The tool name for context.</param>
    /// <param name="target">The query or URL that failed.</param>
    /// <param name="ex">The exception.</param>
    /// <returns>A readable error message.</returns>
    private string DescribeError(string tool, string target, Exception ex)
    {
        var message = ex switch
        {
            WebBrowsingException web => web.Message,
            OperationCanceledException => "the request timed out.",
            HttpRequestException http => http.Message,
            _ => ex.Message,
        };
        Log($"{tool} error for '{target}': {message}");
        return $"{tool} failed for '{target}': {message}";
    }

    /// <summary>Writes a timestamped line to the request log sink, if configured.</summary>
    /// <param name="line">The message to log.</param>
    private void Log(string line) =>
        RequestLog?.Invoke($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {line}");

    /// <inheritdoc/>
    public void Dispose()
    {
        _searchClient.Dispose();
        _fetchClient.Dispose();
    }

    /// <summary>The outcome of a guarded fetch.</summary>
    /// <param name="FinalUrl">The final URL after following redirects.</param>
    /// <param name="MediaType">The response media type.</param>
    /// <param name="Body">The decoded response body (size-capped).</param>
    /// <param name="Truncated">Whether the body was truncated at the size cap.</param>
    private sealed record FetchResult(Uri FinalUrl, string MediaType, string Body, bool Truncated);
}
