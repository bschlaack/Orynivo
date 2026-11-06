using Orynivo.Audio;

namespace Orynivo;

/// <summary>
/// Named audio output configuration combining a backend type with a specific device selection.
/// Multiple profiles allow switching between output devices without reconfiguring each time.
/// </summary>
public sealed class OutputProfile
{
    /// <summary>Gets or sets the unique display name of this output profile.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the audio output backend used by this profile.</summary>
    public OutputBackend Backend { get; set; } = OutputBackend.Wasapi;

    /// <summary>Gets or sets the selected ASIO or cwASIO driver name.</summary>
    public string? SelectedDriverName { get; set; }

    /// <summary>Gets or sets the Windows endpoint, non-Windows route, or CoreAudio device UID.</summary>
    public string? SelectedWasapiDeviceId { get; set; }

    /// <summary>Gets or sets the display name of the selected platform PCM output device.</summary>
    public string? SelectedWasapiDeviceName { get; set; }

    /// <summary>Gets or sets whether CoreAudio matches the file's PCM sample rate.</summary>
    public bool CoreAudioMatchRate { get; set; } = true;
    /// <summary>Gets or sets whether CoreAudio requires exclusive hog ownership.</summary>
    public bool CoreAudioExclusive { get; set; }
    /// <summary>Gets or sets whether CoreAudio restores the prior rate on release.</summary>
    public bool CoreAudioRestoreRate { get; set; } = true;
    /// <summary>Gets or sets whether CoreAudio ignores software volume and PCM processing.</summary>
    public bool CoreAudioBypass { get; set; }

    /// <summary>Gets or sets the stable DNS-SD service name of the selected AirPlay receiver.</summary>
    public string? SelectedAirPlayDeviceId { get; set; }

    /// <summary>Gets or sets the display name of the selected AirPlay receiver.</summary>
    public string? SelectedAirPlayDeviceName { get; set; }

    /// <summary>Gets or sets the last resolved network address of the selected AirPlay receiver.</summary>
    public string? SelectedAirPlayHost { get; set; }

    /// <summary>Gets or sets the last resolved RAOP port of the selected AirPlay receiver.</summary>
    public int SelectedAirPlayPort { get; set; }
}
