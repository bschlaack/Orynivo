using System.Text.Json.Serialization;

namespace Orynivo;

/// <summary>Describes one local Orynivo user profile and its server-profile mappings.</summary>
public sealed class UserProfile
{
    /// <summary>Gets or sets the stable profile identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the user-visible profile name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the server profile identifiers selected for configured servers.</summary>
    public Dictionary<string, string> ServerProfileIds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets client-side favorite identities for remote server rows.</summary>
    public HashSet<string> OrynivoServerFavorites { get; set; } = [];

    /// <summary>Gets or sets local track identifiers marked as favorites for this profile.</summary>
    public HashSet<long> LocalTrackFavorites { get; set; } = [];

    /// <summary>Gets or sets local artist identifiers marked as favorites for this profile.</summary>
    public HashSet<long> LocalArtistFavorites { get; set; } = [];

    /// <summary>Gets or sets local album identifiers marked as favorites for this profile.</summary>
    public HashSet<long> LocalAlbumFavorites { get; set; } = [];

    /// <summary>Gets or sets profile-specific Infinite Mix and recommendation feedback.</summary>
    public InfiniteMixSettings InfiniteMix { get; set; } = new();

    /// <summary>Gets a trimmed display name suitable for profile lists.</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name.Trim();
}
