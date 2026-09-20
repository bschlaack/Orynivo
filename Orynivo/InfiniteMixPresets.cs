namespace Orynivo;

/// <summary>Curated Infinite Mix presets offered next to the mood selector.</summary>
internal enum InfiniteMixPreset
{
    /// <summary>Steady, low-distraction listening with a familiar selection.</summary>
    Focus,

    /// <summary>Fast, energetic listening with more variety.</summary>
    Workout,

    /// <summary>Slow, calm listening with a familiar selection.</summary>
    WindDown
}

/// <summary>
/// Pure mapping from a curated preset onto the persisted Infinite Mix profile
/// fields. Server selection, genre filters, feedback, and exclusions are preserved
/// so applying a preset never discards user configuration.
/// </summary>
internal static class InfiniteMixPresets
{
    /// <summary>Returns a copy of the profile with the preset's curated fields applied.</summary>
    /// <param name="preset">Preset to apply.</param>
    /// <param name="profile">Profile to base the copy on.</param>
    /// <returns>A new profile; <paramref name="profile"/> is never modified.</returns>
    internal static InfiniteMixSettings Apply(InfiniteMixPreset preset, InfiniteMixSettings profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var (mood, discoveryLevel, historyDays, weightFavorites, preferRareTracks) = preset switch
        {
            InfiniteMixPreset.Focus => (InfiniteMixMood.Balanced, 35, 90, true, false),
            InfiniteMixPreset.Workout => (InfiniteMixMood.Energetic, 65, 30, true, true),
            _ => (InfiniteMixMood.Calm, 25, 90, true, false)
        };

        return new InfiniteMixSettings
        {
            Mood = mood,
            DiscoveryLevel = discoveryLevel,
            HistoryDays = historyDays,
            IncludeLocalLibrary = profile.IncludeLocalLibrary,
            EnabledServerIds = new HashSet<string>(profile.EnabledServerIds, StringComparer.OrdinalIgnoreCase),
            ServerSelectionConfigured = profile.ServerSelectionConfigured,
            WeightFavorites = weightFavorites,
            PreferRareTracks = preferRareTracks,
            IncludedGenres = profile.IncludedGenres.ToList(),
            ExcludedGenres = profile.ExcludedGenres.ToList(),
            GenreFeedback = new Dictionary<string, int>(profile.GenreFeedback, StringComparer.OrdinalIgnoreCase),
            ExcludedTrackKeys = new HashSet<string>(profile.ExcludedTrackKeys, StringComparer.OrdinalIgnoreCase)
        };
    }
}
