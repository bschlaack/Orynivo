namespace Orynivo.Audio;

/// <summary>Defines validated, device-independent music delivery choices.</summary>
/// <param name="Id">Stable API choice.</param>
/// <param name="Rate">PCM output rate.</param>
/// <param name="Bits">PCM precision.</param>
/// <param name="Kbps">MP3 bitrate, or zero for FLAC.</param>
internal sealed record BrowserAudioEncoding(string Id, int Rate = 48000, int Bits = 16, int Kbps = 192)
{
    /// <summary>Gets the supported immutable music choices.</summary>
    internal static IReadOnlyList<BrowserAudioEncoding> All { get; } = [
        new("original"), new("dsd-pcm", 88200, 24, 0),
        new("flac-16-44100",44100,16,0), new("flac-16-48000",48000,16,0),
        new("flac-24-88200",88200,24,0), new("flac-24-96000",96000,24,0),
        new("flac-24-176400",176400,24,0), new("flac-24-192000",192000,24,0),
        new("mp3-128",48000,16,128), new("mp3-192"),
        new("mp3-256",48000,16,256), new("mp3-320",48000,16,320)];
    /// <summary>Finds a validated format without accepting arbitrary encoder arguments.</summary>
    /// <param name="id">Requested stable choice.</param>
    /// <returns>The matching choice, or null.</returns>
    internal static BrowserAudioEncoding? Find(string? id) => All.FirstOrDefault(p => p.Id == id);
    /// <summary>Gets the encoded content type.</summary>
    internal string Mime => Kbps == 0 ? "audio/flac" : "audio/mpeg";
    /// <summary>Gets the actual encoded signal description.</summary>
    internal string Description => Kbps == 0 ? $"FLAC · {Bits} bit · {Rate / 1000d:0.###} kHz"
        : $"MP3 · 48 kHz · {Kbps} kbps";
}
