namespace Orynivo;

/// <summary>Persisted user preferences and feedback used to build Infinite Mix queues.</summary>
public sealed class InfiniteMixSettings
{
    /// <summary>Gets or sets the requested mood bias.</summary>
    public InfiniteMixMood Mood { get; set; } = InfiniteMixMood.Balanced;
    /// <summary>Gets or sets the discovery level from zero (familiar) through one hundred (adventurous).</summary>
    public int DiscoveryLevel { get; set; } = 50;
    /// <summary>Gets or sets the number of recent listening days used for affinity calculation.</summary>
    public int HistoryDays { get; set; } = 30;
    /// <summary>Gets or sets whether the local library participates.</summary>
    public bool IncludeLocalLibrary { get; set; } = true;
    /// <summary>Gets or sets the enabled Orynivo Server IDs.</summary>
    public HashSet<string> EnabledServerIds { get; set; } = [];
    /// <summary>Gets or sets whether the server selection has been explicitly configured.</summary>
    public bool ServerSelectionConfigured { get; set; }
    /// <summary>Gets or sets whether favorites receive an additional recommendation boost.</summary>
    public bool WeightFavorites { get; set; } = true;
    /// <summary>Gets or sets whether less frequently played tracks receive a stronger boost.</summary>
    public bool PreferRareTracks { get; set; }
    /// <summary>Gets or sets explicitly included genre names or taxonomy keys.</summary>
    public List<string> IncludedGenres { get; set; } = [];
    /// <summary>Gets or sets explicitly excluded genre names or taxonomy keys.</summary>
    public List<string> ExcludedGenres { get; set; } = [];
    /// <summary>Gets or sets persistent per-genre feedback weights.</summary>
    public Dictionary<string, int> GenreFeedback { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets credential-free stable IDs of tracks excluded from future mixes.</summary>
    public HashSet<string> ExcludedTrackKeys { get; set; } = [];
}

/// <summary>Optional energy bias for Infinite Mix candidate ranking.</summary>
public enum InfiniteMixMood
{
    /// <summary>Prefer calmer genres.</summary>
    Calm,
    /// <summary>Apply no explicit energy bias.</summary>
    Balanced,
    /// <summary>Prefer energetic genres.</summary>
    Energetic
}
