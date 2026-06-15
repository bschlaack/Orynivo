using Orynivo.Audio;

namespace Orynivo;

public sealed class AppSettings
{
    public OutputBackend OutputBackend { get; set; } = OutputBackend.Wasapi;
    public string? SelectedDriverName { get; set; }
    public string? SelectedWasapiDeviceId { get; set; }
    public string? SelectedWasapiDeviceName { get; set; }
    public List<string> LibraryPaths { get; set; } = [];
    public string LastMainView { get; set; } = "Tracks";
    public bool AlbumArtworkView { get; set; }
    public bool ArtistArtworkView { get; set; }
    public double Volume { get; set; } = 1.0;
    public string? LastTrackPath { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public Localization.Language Language { get; set; } = Localization.Language.German;
    public ArtistInfoSource ArtistInfoSource { get; set; } = ArtistInfoSource.Wikipedia;
    public string LastFmApiKey { get; set; } = string.Empty;
    public string QobuzApplicationId { get; set; } = string.Empty;
    public List<PlexServerSettings> PlexServers { get; set; } = [];
    public bool ShowLocalLibrarySection { get; set; } = true;
    public bool ShowOwnRadiosSection { get; set; } = true;
    public bool ShowMyPodcastsSection { get; set; } = true;
    public bool ShowPlexSection { get; set; } = true;
    public bool ShowPlaylistsSection { get; set; } = true;
    public bool IsLocalLibrarySectionExpanded { get; set; } = true;
    public bool IsOwnRadiosSectionExpanded { get; set; }
    public bool IsMyPodcastsSectionExpanded { get; set; }
    public bool IsPlexSectionExpanded { get; set; }
    public bool IsPlaylistsSectionExpanded { get; set; }
}

public sealed class PlexServerSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}

public enum AppTheme
{
    Light,
    Dark
}

public enum ArtistInfoSource
{
    Wikipedia,
    LastFm
}
