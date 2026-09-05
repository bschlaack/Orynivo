namespace Orynivo;

/// <summary>Manages local profile identity and the active profile in application settings.</summary>
internal sealed class UserProfileManager
{
    private readonly AppSettings _settings;

    /// <summary>Initializes a profile manager for the supplied settings instance.</summary>
    /// <param name="settings">Application settings containing profile definitions.</param>
    internal UserProfileManager(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        EnsureDefaultProfile();
    }

    /// <summary>Gets the currently active profile.</summary>
    internal UserProfile ActiveProfile => _settings.UserProfiles.First(profile =>
        string.Equals(profile.Id, _settings.ActiveUserProfileId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets all configured profiles in display order.</summary>
    internal IReadOnlyList<UserProfile> Profiles => _settings.UserProfiles;

    /// <summary>Activates an existing profile by stable identifier.</summary>
    /// <param name="profileId">Profile identifier to activate.</param>
    internal bool Activate(string profileId)
    {
        var profile = _settings.UserProfiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, profileId?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (profile is null)
            return false;
        _settings.ActiveUserProfileId = profile.Id;
        return true;
    }

    /// <summary>Creates and activates a new local profile.</summary>
    /// <param name="name">Display name of the new profile.</param>
    /// <param name="migrateLegacyFavorites">Whether to copy the current Standard-profile favorites into the new profile.</param>
    /// <returns>The newly created profile.</returns>
    internal UserProfile Create(string name, bool migrateLegacyFavorites = false)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("A profile name is required.", nameof(name));
        if (_settings.UserProfiles.Any(profile =>
                string.Equals(profile.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A profile with this name already exists.");

        var profile = new UserProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = trimmed
        };
        _settings.UserProfiles.Add(profile);
        if (migrateLegacyFavorites)
        {
            var standard = _settings.UserProfiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, "standard", StringComparison.OrdinalIgnoreCase));
            if (standard is not null)
            {
                profile.OrynivoServerFavorites = new HashSet<string>(
                    standard.OrynivoServerFavorites,
                    StringComparer.OrdinalIgnoreCase);
                profile.LocalTrackFavorites = new HashSet<long>(standard.LocalTrackFavorites);
                profile.LocalArtistFavorites = new HashSet<long>(standard.LocalArtistFavorites);
                profile.LocalAlbumFavorites = new HashSet<long>(standard.LocalAlbumFavorites);
                profile.InfiniteMix = CloneInfiniteMix(standard.InfiniteMix);
                Orynivo.Library.AudioDatabase.CopyProfileTrackState(standard.Id, profile.Id);
            }
        }
        _settings.ActiveUserProfileId = profile.Id;
        return profile;
    }

    /// <summary>Renames an existing profile.</summary>
    /// <param name="profileId">Identifier of the profile to rename.</param>
    /// <param name="name">New display name.</param>
    internal void Rename(string profileId, string name)
    {
        var profile = Find(profileId);
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("A profile name is required.", nameof(name));
        if (_settings.UserProfiles.Any(candidate =>
                !ReferenceEquals(candidate, profile) &&
                string.Equals(candidate.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A profile with this name already exists.");
        profile.Name = trimmed;
    }

    /// <summary>Deletes a profile, retaining at least the Standard profile.</summary>
    /// <param name="profileId">Identifier of the profile to delete.</param>
    internal void Delete(string profileId)
    {
        var profile = Find(profileId);
        if (string.Equals(profile.Id, "standard", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Standard profile cannot be deleted.");
        _settings.UserProfiles.Remove(profile);
        if (string.Equals(_settings.ActiveUserProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
            _settings.ActiveUserProfileId = "standard";
    }

    private UserProfile Find(string profileId) => _settings.UserProfiles.FirstOrDefault(profile =>
        string.Equals(profile.Id, profileId?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException("The requested profile does not exist.");

    private static InfiniteMixSettings CloneInfiniteMix(InfiniteMixSettings source) => new()
    {
        Mood = source.Mood,
        DiscoveryLevel = source.DiscoveryLevel,
        HistoryDays = source.HistoryDays,
        IncludeLocalLibrary = source.IncludeLocalLibrary,
        EnabledServerIds = new HashSet<string>(source.EnabledServerIds, StringComparer.OrdinalIgnoreCase),
        ServerSelectionConfigured = source.ServerSelectionConfigured,
        WeightFavorites = source.WeightFavorites,
        PreferRareTracks = source.PreferRareTracks,
        IncludedGenres = [.. source.IncludedGenres],
        ExcludedGenres = [.. source.ExcludedGenres],
        GenreFeedback = new Dictionary<string, int>(source.GenreFeedback, StringComparer.OrdinalIgnoreCase),
        ExcludedTrackKeys = new HashSet<string>(source.ExcludedTrackKeys, StringComparer.OrdinalIgnoreCase)
    };

    private void EnsureDefaultProfile()
    {
        var standard = _settings.UserProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, "standard", StringComparison.OrdinalIgnoreCase));
        if (standard is null)
        {
            standard = new UserProfile { Id = "standard", Name = "Standard" };
            _settings.UserProfiles.Insert(0, standard);
        }
        if (string.IsNullOrWhiteSpace(_settings.ActiveUserProfileId) ||
            _settings.UserProfiles.All(profile => !string.Equals(
                profile.Id, _settings.ActiveUserProfileId, StringComparison.OrdinalIgnoreCase)))
            _settings.ActiveUserProfileId = standard.Id;
    }
}
