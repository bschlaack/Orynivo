namespace Orynivo.Audio;

/// <summary>
/// Selects the strength of the headphone crossfeed blend.
/// </summary>
public enum CrossfeedStrength
{
    /// <summary>A subtle blend with the least coloration.</summary>
    Light,

    /// <summary>A balanced blend suitable for most headphones.</summary>
    Medium,

    /// <summary>A strong blend for heavily separated recordings.</summary>
    Strong
}
