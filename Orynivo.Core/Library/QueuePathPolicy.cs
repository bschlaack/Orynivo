namespace Orynivo.Library;

/// <summary>
/// Decides whether a playback or queue path may be persisted without storing
/// credentials.
/// </summary>
/// <remarks>
/// Local files, <c>cue://</c> virtual paths, and credential-free
/// <c>orynivo://</c> references are always persistable. Remote HTTP/HTTPS URLs
/// are persistable only when they carry no user information and no query
/// parameters that are known to transport credentials (Plex tokens, generic
/// <c>token=</c>, or the Orynivo Server <c>key=</c> parameter).
/// </remarks>
public static class QueuePathPolicy
{
    /// <summary>
    /// Determines whether the supplied path can be persisted without leaking
    /// credentials.
    /// </summary>
    /// <param name="path">The candidate path or URL.</param>
    /// <returns>
    /// <see langword="true"/> when the path may be persisted; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool CanPersist(string? path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.IsFile)
            return true;
        if (uri.Scheme.Equals("cue", StringComparison.OrdinalIgnoreCase))
            return true;
        if (uri.Scheme.Equals("orynivo", StringComparison.OrdinalIgnoreCase))
            return true;
        if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        return !uri.Query.Contains("X-Plex-Token", StringComparison.OrdinalIgnoreCase) &&
               !uri.Query.Contains("token=", StringComparison.OrdinalIgnoreCase) &&
               !uri.Query.Contains("key=", StringComparison.OrdinalIgnoreCase);
    }
}
