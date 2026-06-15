namespace Orynivo.Audio;

public enum OutputBackend
{
    Asio = 0,
    Wasapi = 1,
    KernelStreaming = 2,
    CwAsio = 3
}
