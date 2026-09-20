namespace Orynivo.Library;

/// <summary>
/// Builds and validates cloud backup target URLs. Only plain HTTP(S) WebDAV
/// endpoints are accepted, and credentials must never be embedded in the URL.
/// </summary>
public static class BackupTargets
{
    /// <summary>
    /// Validates a WebDAV backup target base URL. The value must be an absolute
    /// <c>http</c> or <c>https</c> URI without embedded user information.
    /// </summary>
    /// <param name="url">Candidate base URL.</param>
    /// <returns><see langword="true"/> when the URL can be used as a target.</returns>
    public static bool IsSupportedTargetUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        return string.IsNullOrEmpty(uri.UserInfo);
    }

    /// <summary>
    /// Combines the target base URL, an optional remote directory, and a file name
    /// into the upload URL. Credentials are never added here.
    /// </summary>
    /// <param name="baseUrl">WebDAV collection base URL.</param>
    /// <param name="remoteDirectory">Optional sub-directory inside the collection.</param>
    /// <param name="fileName">Archive file name.</param>
    /// <returns>The absolute upload URL.</returns>
    /// <exception cref="ArgumentException">The base URL or file name is not usable.</exception>
    public static string BuildTargetUrl(string baseUrl, string? remoteDirectory, string fileName)
    {
        if (!IsSupportedTargetUrl(baseUrl))
            throw new ArgumentException("Unsupported backup target URL.", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A backup file name is required.", nameof(fileName));

        var builder = new System.Text.StringBuilder(baseUrl.Trim().TrimEnd('/'));
        var directory = remoteDirectory?.Trim().Trim('/');
        if (!string.IsNullOrEmpty(directory))
        {
            foreach (var segment in directory.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                builder.Append('/').Append(Uri.EscapeDataString(segment));
            }
        }

        return builder.Append('/').Append(Uri.EscapeDataString(fileName.Trim())).ToString();
    }
}
