namespace Orynivo.Audio.CoreAudio;

/// <summary>Controls one UID-bound Core Audio device, independently of the PCM renderer.</summary>
internal interface ICoreAudioDeviceControl
{
    /// <summary>Gets the stable device UID used for process-local admission.</summary>
    string Uid { get; }

    /// <summary>Gets whether the original live device still owns this UID.</summary>
    bool IsAvailable { get; }

    /// <summary>Gets the device's current nominal sample rate in hertz.</summary>
    double SampleRate { get; }

    /// <summary>Gets the PID owning hog mode, or minus one when unowned.</summary>
    int HogOwner { get; }

    /// <summary>Checks the device's advertised nominal rate ranges.</summary>
    /// <param name="rate">Requested PCM sample rate in hertz.</param>
    /// <returns>Whether the device advertises the exact requested rate.</returns>
    bool SupportsRate(double rate);

    /// <summary>Requests a nominal sample rate without implying completion.</summary>
    /// <param name="rate">Requested rate in hertz.</param>
    void SetSampleRate(double rate);

    /// <summary>Requests or releases exclusive device ownership.</summary>
    /// <param name="pid">Current process ID to acquire, or minus one to release.</param>
    void SetHogOwner(int pid);

    /// <summary>Waits boundedly for a requested rate to become observable.</summary>
    /// <param name="rate">Expected nominal rate in hertz.</param>
    /// <param name="cancellationToken">Cancellation of the wait.</param>
    /// <returns>A task completing after confirmation, or failing on timeout or removal.</returns>
    Task WaitForSampleRateAsync(double rate, CancellationToken cancellationToken);
}
