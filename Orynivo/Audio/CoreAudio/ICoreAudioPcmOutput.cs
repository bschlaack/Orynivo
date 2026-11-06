namespace Orynivo.Audio.CoreAudio;

/// <summary>Worker-owned PCM output boundary with a nonblocking native consumer.</summary>
internal interface ICoreAudioPcmOutput : IDisposable
{
    /// <summary>Gets consumed media frames, excluding underflow and paused silence.</summary>
    long RenderedFrames { get; }
    /// <summary>Gets queued media frames.</summary>
    long QueuedFrames { get; }
    /// <summary>Gets the conservative device-buffer tail needed after the final callback.</summary>
    TimeSpan DrainTail { get; }
    /// <summary>Gets whether callbacks have stopped sufficiently to release device ownership.</summary>
    bool IsQuiescent { get; }
    /// <summary>Gets or sets silence-without-consumption pause state.</summary>
    bool Paused { get; set; }
    /// <summary>Starts the native output after prefill.</summary>
    void Start();
    /// <summary>Stops callbacks and discards queued media for a seek.</summary>
    void Reset();
    /// <summary>Copies frames without blocking when the ring is full.</summary>
    /// <param name="pcm">Stereo S32 PCM.</param>
    /// <returns>The accepted byte count.</returns>
    int Write(ReadOnlySpan<byte> pcm);
    /// <summary>Reports callback or native-output failure on the preparation/pump worker.</summary>
    void CheckHealth();
}
