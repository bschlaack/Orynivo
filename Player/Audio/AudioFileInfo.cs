namespace Player.Audio;

public sealed record AudioFileInfo(
    string CodecName,
    int SourceSampleRate,
    int Channels,
    int OutputSampleRate,
    bool IsDsd,
    string ContainerName,
    TimeSpan Duration);
