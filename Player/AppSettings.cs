using Player.Audio;

namespace Player;

public sealed class AppSettings
{
    public OutputBackend OutputBackend { get; set; } = OutputBackend.Asio;
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
