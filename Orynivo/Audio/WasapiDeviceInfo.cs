namespace Orynivo.Audio;

/// <summary>
/// Identifies an active WASAPI render device by its unique device ID and display name.
/// </summary>
/// <param name="Id">Unique Windows device identifier (MMDevice ID).</param>
/// <param name="Name">Friendly name of the device as shown in the Windows Sound control panel.</param>
public sealed record WasapiDeviceInfo(string Id, string Name);
