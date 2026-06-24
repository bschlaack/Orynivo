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

    /// <summary>Gets or sets the MMDevice ID of the selected WASAPI render device.</summary>
    public string? SelectedWasapiDeviceId { get; set; }

    /// <summary>Gets or sets the display name of the selected WASAPI render device.</summary>
    public string? SelectedWasapiDeviceName { get; set; }
}
