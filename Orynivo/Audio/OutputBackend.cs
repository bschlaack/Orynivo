namespace Orynivo.Audio;

/// <summary>
/// Identifies the audio output backend to use for playback.
/// </summary>
public enum OutputBackend
{
    /// <summary>Steinberg ASIO via <c>AsioBridge.dll</c>.</summary>
    Asio = 0,
    /// <summary>Windows Audio Session API (WASAPI) in exclusive mode.</summary>
    Wasapi = 1,
    /// <summary>Windows Kernel Streaming (selectable but not yet implemented as a playback backend).</summary>
    KernelStreaming = 2,
    /// <summary>cwASIO via <c>CwAsioBridge.dll</c>.</summary>
    CwAsio = 3
}
