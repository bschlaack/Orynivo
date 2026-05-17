using Player.Audio;

namespace Player;

public sealed class AppSettings
{
    public OutputBackend OutputBackend { get; set; } = OutputBackend.Asio;
    public string? SelectedDriverName { get; set; }
    public string? SelectedWasapiDeviceId { get; set; }
    public string? SelectedWasapiDeviceName { get; set; }
    public List<string> LibraryPaths { get; set; } = [];
}
