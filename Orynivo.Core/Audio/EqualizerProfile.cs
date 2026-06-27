namespace Orynivo.Audio;

/// <summary>
/// Describes a persisted parametric equalizer profile imported from an
/// Equalizer APO or AutoEQ text file.
/// </summary>
public sealed class EqualizerProfile
{
    /// <summary>Gets or sets the display name of the profile.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the profile preamplification in decibels.</summary>
    public double PreampDb { get; set; }

    /// <summary>Gets or sets the parametric filters in processing order.</summary>
    public List<EqualizerFilter> Filters { get; set; } = [];

    /// <summary>Creates an independent copy of the profile.</summary>
    /// <returns>A deep copy of this profile.</returns>
    public EqualizerProfile Clone() => new()
    {
        Name = Name,
        PreampDb = PreampDb,
        Filters = Filters.Select(static filter => filter.Clone()).ToList()
    };
}

/// <summary>Describes one biquad equalizer filter.</summary>
public sealed class EqualizerFilter
{
    /// <summary>Gets or sets the filter type.</summary>
    public EqualizerFilterType Type { get; set; }

    /// <summary>Gets or sets the center or cutoff frequency in hertz.</summary>
    public double Frequency { get; set; }

    /// <summary>Gets or sets the gain in decibels for peak and shelf filters.</summary>
    public double GainDb { get; set; }

    /// <summary>Gets or sets the filter quality factor.</summary>
    public double Q { get; set; } = 0.7071067811865476;

    /// <summary>Creates an independent copy of the filter.</summary>
    /// <returns>A copy of this filter.</returns>
    public EqualizerFilter Clone() => new()
    {
        Type = Type,
        Frequency = Frequency,
        GainDb = GainDb,
        Q = Q
    };
}

/// <summary>Identifies the supported parametric equalizer filter shapes.</summary>
public enum EqualizerFilterType
{
    /// <summary>Peaking equalizer filter.</summary>
    Peak,

    /// <summary>Low-shelf equalizer filter.</summary>
    LowShelf,

    /// <summary>High-shelf equalizer filter.</summary>
    HighShelf,

    /// <summary>Second-order low-pass filter.</summary>
    LowPass,

    /// <summary>Second-order high-pass filter.</summary>
    HighPass
}
