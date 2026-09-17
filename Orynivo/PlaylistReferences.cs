using System.Globalization;

namespace Orynivo;

/// <summary>
/// Builds and parses the stable, credential-free references used to persist
/// remote Orynivo Server tracks and albums in playlists and queues.
/// </summary>
/// <remarks>
/// Track references use the form <c>orynivo://serverId/track/trackId</c>;
/// album drag references use <c>orynivo-album:serverId:albumId</c>. These
/// references never contain an authenticated stream URL or API key.
/// </remarks>
internal static class PlaylistReferences
{
    /// <summary>The URI scheme used by persisted remote track references.</summary>
    internal const string TrackScheme = "orynivo";

    /// <summary>The prefix used by remote album drag references.</summary>
    internal const string AlbumPrefix = "orynivo-album:";

    /// <summary>Builds a stable, credential-free reference for a remote track.</summary>
    /// <param name="serverId">Stable configured server identifier.</param>
    /// <param name="trackId">Provider-local track identifier.</param>
    /// <returns>The <c>orynivo://</c> track reference.</returns>
    internal static string BuildTrack(string serverId, long trackId) =>
        $"{TrackScheme}://{Uri.EscapeDataString(serverId)}/track/{trackId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Parses a stable remote track reference.</summary>
    /// <param name="path">Candidate reference.</param>
    /// <param name="serverId">Receives the encoded server identifier.</param>
    /// <param name="trackId">Receives the track identifier.</param>
    /// <returns><see langword="true"/> when the value is a valid track reference.</returns>
    internal static bool TryParseTrack(string? path, out string serverId, out long trackId)
    {
        serverId = string.Empty;
        trackId = 0;
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(TrackScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2 ||
            !segments[0].Equals("track", StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(segments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out trackId))
        {
            return false;
        }

        serverId = Uri.UnescapeDataString(uri.Host);
        return serverId.Length > 0;
    }

    /// <summary>Builds a remote album drag reference.</summary>
    /// <param name="serverId">Stable configured server identifier.</param>
    /// <param name="albumId">Provider-local album identifier.</param>
    /// <returns>The <c>orynivo-album:</c> reference.</returns>
    internal static string BuildAlbum(string serverId, long albumId) =>
        $"{AlbumPrefix}{serverId}:{albumId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Parses a remote album drag reference.</summary>
    /// <param name="token">Candidate reference.</param>
    /// <param name="serverId">Receives the server identifier.</param>
    /// <param name="albumId">Receives the album identifier.</param>
    /// <returns><see langword="true"/> when the value is a valid album reference.</returns>
    internal static bool TryParseAlbum(string? token, out string serverId, out long albumId)
    {
        serverId = string.Empty;
        albumId = 0;
        if (string.IsNullOrEmpty(token) ||
            !token.StartsWith(AlbumPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = token[AlbumPrefix.Length..];
        var separator = rest.LastIndexOf(':');
        if (separator <= 0 ||
            !long.TryParse(rest[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out albumId))
        {
            return false;
        }

        serverId = rest[..separator];
        return true;
    }
}
