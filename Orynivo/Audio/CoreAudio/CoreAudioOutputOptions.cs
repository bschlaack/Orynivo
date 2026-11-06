namespace Orynivo.Audio.CoreAudio;

/// <summary>Immutable profile snapshot for one native output preparation.</summary>
/// <param name="MatchRate">Whether to request the file's PCM rate instead of the current device rate.</param>
/// <param name="Exclusive">Whether hog ownership is mandatory.</param>
/// <param name="RestoreRate">Whether successful playback restores its previous nominal rate.</param>
/// <param name="Bypass">Whether software volume and all PCM processing are bypassed.</param>
internal sealed record CoreAudioOutputOptions(bool MatchRate, bool Exclusive, bool RestoreRate, bool Bypass)
{
    /// <summary>Copies native options before asynchronous preparation.</summary>
    /// <param name="profile">The selected saved profile, if present.</param>
    /// <returns>The immutable option snapshot.</returns>
    internal static CoreAudioOutputOptions FromProfile(OutputProfile? profile) => new(
        profile?.CoreAudioMatchRate ?? true, profile?.CoreAudioExclusive ?? false,
        profile?.CoreAudioRestoreRate ?? true, profile?.CoreAudioBypass ?? false);

    /// <summary>Selects an explicit rate while respecting a strict positive PCM ceiling.</summary>
    /// <param name="info">Original source metadata and DSD conversion hint.</param>
    /// <param name="deviceRate">Current nominal device rate.</param>
    /// <param name="maximumRate">Positive output ceiling or zero for unrestricted.</param>
    /// <returns>The exact rate to request; unsupported configurations fail before rendering.</returns>
    internal int SelectRate(AudioFileInfo info, double deviceRate, int maximumRate)
    {
        var rate = MatchRate ? info.IsDsd ? 176400 : info.SourceSampleRate : checked((int)deviceRate);
        if (!MatchRate && deviceRate != rate) throw new NotSupportedException();
        if (MatchRate && maximumRate > 0 && rate > maximumRate)
            rate = info.IsDsd
                ? new[] { 176400, 88200, 44100, 22050, 11025 }.FirstOrDefault(r => r <= maximumRate)
                : maximumRate;
        if (rate is < 8000 or > 768000 || maximumRate > 0 && rate > maximumRate)
            throw new NotSupportedException();
        if (Bypass && (!MatchRate || !Exclusive || info.IsDsd || info.Channels != 2 || rate != info.SourceSampleRate))
            throw new NotSupportedException();
        return rate;
    }
}
