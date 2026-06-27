namespace Orynivo.Library;

/// <summary>Data source for artist biography text.</summary>
public enum ArtistInfoSource
{
    /// <summary>Fetch artist biography from Wikipedia.</summary>
    Wikipedia,
    /// <summary>Fetch artist biography from Last.fm (requires an API key).</summary>
    LastFm
}
