namespace Orynivo.Audio.CoreAudio;

/// <summary>
/// Bounded single-producer/single-consumer stereo S32 ring. Reset requires a
/// stopped callback. Neither side allocates or waits when moving PCM.
/// </summary>
internal sealed class CoreAudioPcmRing
{
    private readonly byte[] _data;
    private long _read;
    private long _written;

    /// <summary>Allocates the complete ring before rendering starts.</summary>
    /// <param name="frames">Capacity in stereo frames.</param>
    internal CoreAudioPcmRing(int frames = 32768)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
        _data = new byte[checked(frames * 8)];
    }

    /// <summary>Gets the number of consumed media frames, excluding underflow silence.</summary>
    internal long RenderedFrames => Volatile.Read(ref _read) / 8;

    /// <summary>Gets the current queued media frame count.</summary>
    internal long QueuedFrames
    {
        get
        {
            var read = Volatile.Read(ref _read);
            return (Volatile.Read(ref _written) - read) / 8;
        }
    }

    /// <summary>Copies complete frames into currently available ring space.</summary>
    /// <param name="pcm">Stereo S32 little-endian media bytes.</param>
    /// <returns>The accepted byte count; the producer retains any remainder.</returns>
    internal int Write(ReadOnlySpan<byte> pcm)
    {
        var written = _written;
        var count = (int)Math.Min(pcm.Length, _data.Length - (written - Volatile.Read(ref _read)));
        count -= count % 8;
        CopyInto(pcm[..count], (int)(written % _data.Length));
        Volatile.Write(ref _written, written + count);
        return count;
    }

    /// <summary>Consumes complete frames and zero-fills any underflow without advancing media time.</summary>
    /// <param name="destination">Native interleaved stereo output buffer.</param>
    /// <returns>The number of media bytes copied before zero filling.</returns>
    internal int Read(Span<byte> destination)
    {
        var read = _read;
        var count = (int)Math.Min(destination.Length, Volatile.Read(ref _written) - read);
        count -= count % 8;
        var offset = (int)(read % _data.Length);
        var first = Math.Min(count, _data.Length - offset);
        _data.AsSpan(offset, first).CopyTo(destination);
        _data.AsSpan(0, count - first).CopyTo(destination[first..]);
        destination[count..].Clear();
        Volatile.Write(ref _read, read + count);
        return count;
    }

    /// <summary>Discards queued frames after the native callback has stopped.</summary>
    internal void Reset()
    {
        _read = 0;
        _written = 0;
    }

    private void CopyInto(ReadOnlySpan<byte> source, int offset)
    {
        var first = Math.Min(source.Length, _data.Length - offset);
        source[..first].CopyTo(_data.AsSpan(offset));
        source[first..].CopyTo(_data);
    }
}
