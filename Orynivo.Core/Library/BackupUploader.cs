using System.Net;
using System.Net.Http.Headers;

namespace Orynivo.Library;

/// <summary>
/// Uploads a completed backup archive to a WebDAV collection. Optional Basic
/// credentials are only sent in the request header and are never written to a URL,
/// a log, or the returned error message.
/// </summary>
public sealed class BackupUploader
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient;

    /// <summary>Initializes the uploader.</summary>
    /// <param name="httpClient">
    /// Optional HTTP client, mainly for tests. When omitted, an internal client with
    /// a bounded timeout is used.
    /// </param>
    public BackupUploader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = DefaultTimeout };
    }

    /// <summary>
    /// Uploads one file to the target URL.
    /// </summary>
    /// <param name="targetUrl">Absolute WebDAV file URL.</param>
    /// <param name="filePath">Local archive to upload.</param>
    /// <param name="userName">Optional user name for Basic authentication.</param>
    /// <param name="password">Optional password for Basic authentication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the upload succeeded.</returns>
    /// <exception cref="InvalidOperationException">
    /// The target URL is unsupported or the server rejected the upload.
    /// </exception>
    public async Task UploadAsync(
        string targetUrl,
        string filePath,
        string? userName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (!BackupTargets.IsSupportedTargetUrl(targetUrl))
            throw new InvalidOperationException("Unsupported backup target URL.");
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Backup archive not found.", filePath);

        using var request = new HttpRequestMessage(HttpMethod.Put, targetUrl);
        await using var stream = File.OpenRead(filePath);
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        if (!string.IsNullOrWhiteSpace(userName))
        {
            var raw = $"{userName}:{password ?? string.Empty}";
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw)));
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Backup upload failed with status {(int)response.StatusCode} ({DescribeStatus(response.StatusCode)}).");
        }
    }

    /// <summary>Returns a short, credential-free description of a status code.</summary>
    /// <param name="statusCode">Response status code.</param>
    /// <returns>The reason phrase or the numeric code.</returns>
    private static string DescribeStatus(HttpStatusCode statusCode) =>
        string.IsNullOrEmpty(statusCode.ToString()) ? ((int)statusCode).ToString() : statusCode.ToString();
}
