namespace Orynivo.Audio;

/// <summary>
/// Technical metadata for an audio file as probed by <c>ffprobe</c>.
/// </summary>
/// <param name="CodecName">Codec identifier reported by ffprobe, e.g. <c>flac</c>, <c>mp3</c>, or <c>dsd_lsb</c>.</param>
/// <param name="SourceSampleRate">Native sample rate of the source file in Hz.</param>
/// <param name="Channels">Number of audio channels (1 = mono, 2 = stereo).</param>
/// <param name="OutputSampleRate">Sample rate normalised for output in Hz.</param>
/// <param name="IsDsd"><see langword="true"/> when the file is a DSD stream (DSF/DFF).</param>
/// <param name="ContainerName">Lowercase file extension of the container, e.g. <c>flac</c>, <c>dsf</c>.</param>
/// <param name="Duration">Total duration of the audio file.</param>
public sealed record AudioFileInfo(
    string CodecName,
    int SourceSampleRate,
    int Channels,
    int OutputSampleRate,
    bool IsDsd,
    string ContainerName,
    TimeSpan Duration);
