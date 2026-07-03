using System.Text;

namespace Orynivo.Audio;

/// <summary>
/// Writes sanitized diagnostics for transport seeks, FFmpeg decoder startup, and
/// server-side stream transcodes.
/// </summary>
public static class SeekDiagnostics
{
    private static readonly object SyncRoot = new();

    /// <summary>Gets the seek diagnostics log path below the application data directory.</summary>
    public static string LogPath => AppPaths.GetDataPath("logs", "seek.log");

    /// <summary>
    /// Appends a diagnostic entry to the seek log.
    /// </summary>
    /// <param name="component">Short component name that produced the entry.</param>
    /// <param name="message">Diagnostic message to append.</param>
    /// <param name="exception">Optional exception details to append.</param>
    public static void Log(string component, string message, Exception? exception = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [")
                .Append(component)
                .Append("] ")
                .AppendLine(message);

            if (exception is not null)
                builder.AppendLine(exception.ToString());

            lock (SyncRoot)
            {
                File.AppendAllText(LogPath, builder.ToString());
            }
        }
        catch
        {
            // Diagnostics must never affect playback.
        }
    }

    /// <summary>
    /// Redacts credential-bearing URL parts before they are written to diagnostics.
    /// </summary>
    /// <param name="value">URL or path value to sanitize.</param>
    /// <returns>A sanitized value safe for local diagnostic logs.</returns>
    public static string SanitizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return value;
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty
        };

        if (!string.IsNullOrEmpty(builder.Query))
        {
            var query = builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            var sanitized = new List<string>(query.Length);
            foreach (var part in query)
            {
                var equalsIndex = part.IndexOf('=', StringComparison.Ordinal);
                var name = equalsIndex >= 0 ? part[..equalsIndex] : part;
                var decodedName = Uri.UnescapeDataString(name);
                if (IsSecretQueryName(decodedName))
                    sanitized.Add($"{name}=<redacted>");
                else
                    sanitized.Add(part);
            }

            builder.Query = string.Join("&", sanitized);
        }

        return builder.Uri.ToString();
    }

    private static bool IsSecretQueryName(string name) =>
        name.Equals("key", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("api_key", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("apikey", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("access_token", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Plex-Token", StringComparison.OrdinalIgnoreCase);
}
