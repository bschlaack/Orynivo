using Orynivo;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the pure mapping from an Infinite Mix preset onto the persisted
/// profile fields.
/// </summary>
public sealed class InfiniteMixPresetsTests
{
    /// <summary>Workout is the energetic, adventurous preset.</summary>
    [Fact]
    public void Apply_Workout_UsesEnergeticCuratedFields()
    {
        var applied = InfiniteMixPresets.Apply(InfiniteMixPreset.Workout, CreateProfile());

        Assert.Equal(InfiniteMixMood.Energetic, applied.Mood);
        Assert.Equal(65, applied.DiscoveryLevel);
        Assert.Equal(30, applied.HistoryDays);
        Assert.True(applied.WeightFavorites);
        Assert.True(applied.PreferRareTracks);
    }

    /// <summary>Wind down is the calm, familiar preset.</summary>
    [Fact]
    public void Apply_WindDown_UsesCalmCuratedFields()
    {
        var applied = InfiniteMixPresets.Apply(InfiniteMixPreset.WindDown, CreateProfile());

        Assert.Equal(InfiniteMixMood.Calm, applied.Mood);
        Assert.Equal(25, applied.DiscoveryLevel);
        Assert.Equal(90, applied.HistoryDays);
        Assert.False(applied.PreferRareTracks);
    }

    /// <summary>Focus stays balanced and familiar.</summary>
    [Fact]
    public void Apply_Focus_UsesBalancedCuratedFields()
    {
        var applied = InfiniteMixPresets.Apply(InfiniteMixPreset.Focus, CreateProfile());

        Assert.Equal(InfiniteMixMood.Balanced, applied.Mood);
        Assert.Equal(35, applied.DiscoveryLevel);
        Assert.Equal(90, applied.HistoryDays);
    }

    /// <summary>Applying a preset never modifies the profile it was given.</summary>
    [Fact]
    public void Apply_DoesNotModifyTheSourceProfile()
    {
        var profile = CreateProfile();
        profile.Mood = InfiniteMixMood.Calm;
        profile.DiscoveryLevel = 10;
        profile.HistoryDays = 3;

        InfiniteMixPresets.Apply(InfiniteMixPreset.Workout, profile);

        Assert.Equal(InfiniteMixMood.Calm, profile.Mood);
        Assert.Equal(10, profile.DiscoveryLevel);
        Assert.Equal(3, profile.HistoryDays);
    }

    /// <summary>Server selection, filters, feedback, and exclusions are preserved and copied.</summary>
    [Fact]
    public void Apply_PreservesUserConfiguration()
    {
        var profile = CreateProfile();

        var applied = InfiniteMixPresets.Apply(InfiniteMixPreset.Focus, profile);

        Assert.True(applied.IncludeLocalLibrary);
        Assert.Equal(profile.EnabledServerIds, applied.EnabledServerIds);
        Assert.Equal(["rock"], applied.IncludedGenres);
        Assert.Equal(["polka"], applied.ExcludedGenres);
        Assert.Equal(profile.GenreFeedback["rock"], applied.GenreFeedback["rock"]);
        Assert.Equal(profile.ExcludedTrackKeys, applied.ExcludedTrackKeys);

        // The copies must be independent from the source.
        applied.EnabledServerIds.Add("other");
        applied.IncludedGenres.Add("jazz");
        Assert.DoesNotContain("other", profile.EnabledServerIds);
        Assert.DoesNotContain("jazz", profile.IncludedGenres);
    }

    /// <summary>The three presets stay distinct.</summary>
    [Fact]
    public void Apply_ProducesDistinctProfiles()
    {
        var profile = CreateProfile();

        var focus = InfiniteMixPresets.Apply(InfiniteMixPreset.Focus, profile);
        var workout = InfiniteMixPresets.Apply(InfiniteMixPreset.Workout, profile);
        var windDown = InfiniteMixPresets.Apply(InfiniteMixPreset.WindDown, profile);

        Assert.NotEqual(focus.Mood, workout.Mood);
        Assert.NotEqual(workout.Mood, windDown.Mood);
        Assert.NotEqual(focus.DiscoveryLevel, workout.DiscoveryLevel);
        Assert.NotEqual(workout.DiscoveryLevel, windDown.DiscoveryLevel);
    }

    private static InfiniteMixSettings CreateProfile() => new()
    {
        Mood = InfiniteMixMood.Balanced,
        DiscoveryLevel = 50,
        HistoryDays = 30,
        IncludeLocalLibrary = true,
        EnabledServerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "server-1" },
        ServerSelectionConfigured = true,
        WeightFavorites = true,
        PreferRareTracks = false,
        IncludedGenres = ["rock"],
        ExcludedGenres = ["polka"],
        GenreFeedback = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["rock"] = 3 },
        ExcludedTrackKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "local:7" }
    };
}
