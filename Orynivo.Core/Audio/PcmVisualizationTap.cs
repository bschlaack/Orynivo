namespace Orynivo.Audio;

/// <summary>
/// Lock-free single-producer/single-consumer hand-off between an audio pump and the
/// visualizer. The audio thread only ever copies into a ring buffer and never waits: when
/// the consumer falls behind, the oldest samples are dropped instead, so a visualization
/// can never stall playback.
/// </summary>
public sealed class PcmVisualizationTap
{
    private readonly float[] _buffer;
    private readonly int _capacity;
    private long _written;
    private long _read;

    /// <summary>Creates a tap holding the given number of stereo frames.</summary>
    /// <param name="frameCapacity">Ring buffer size in stereo frames.</param>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is not positive.</exception>
    public PcmVisualizationTap(int frameCapacity = 16_384)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCapacity, 256);
        _capacity = frameCapacity;
        _buffer = new float[frameCapacity * 2];
    }

    /// <summary>Gets the number of stereo frames currently buffered.</summary>
    public int BufferedFrames
    {
        get
        {
            var written = Interlocked.Read(ref _written);
            var read = Interlocked.Read(ref _read);
            return (int)Math.Clamp(written - read, 0, _capacity);
        }
    }

    /// <summary>
    /// Publishes interleaved stereo samples from the audio pump. Samples that do not fit
    /// are dropped by overwriting the oldest data, and this method never blocks.
    /// </summary>
    /// <param name="interleavedStereo">Interleaved left/right samples in the range -1 to 1.</param>
    public void Push(ReadOnlySpan<float> interleavedStereo)
    {
        var frames = interleavedStereo.Length / 2;
        if (frames <= 0)
            return;

        // A block larger than the ring buffer keeps only its newest part.
        if (frames > _capacity)
        {
            interleavedStereo = interleavedStereo[^(_capacity * 2)..];
            frames = _capacity;
        }

        var written = Interlocked.Read(ref _written);
        for (var index = 0; index < frames; index++)
        {
            var slot = (int)((written + index) % _capacity);
            _buffer[slot * 2] = interleavedStereo[index * 2];
            _buffer[(slot * 2) + 1] = interleavedStereo[(index * 2) + 1];
        }

        Interlocked.Add(ref _written, frames);

        // Move the read cursor forward when the producer overtook the consumer.
        var read = Interlocked.Read(ref _read);
        var overflow = (read + _capacity) - (written + frames);
        if (overflow < 0)
            Interlocked.CompareExchange(ref _read, written + frames - _capacity, read);
    }

    /// <summary>
    /// Copies the newest samples into the destination, dropping samples the destination
    /// cannot hold so the returned block always ends at the newest sample.
    /// </summary>
    /// <param name="destination">Destination for interleaved left/right samples.</param>
    /// <returns>The number of stereo frames written.</returns>
    public int ReadNewest(Span<float> destination)
    {
        var available = BufferedFrames;
        var requested = destination.Length / 2;
        var frames = Math.Min(available, requested);
        if (frames <= 0)
            return 0;

        var written = Interlocked.Read(ref _written);
        var first = written - frames;
        for (var index = 0; index < frames; index++)
        {
            var slot = (int)((first + index) % _capacity);
            destination[index * 2] = _buffer[slot * 2];
            destination[(index * 2) + 1] = _buffer[(slot * 2) + 1];
        }

        Interlocked.CompareExchange(ref _read, written, Interlocked.Read(ref _read));
        return frames;
    }

    /// <summary>Discards every buffered sample.</summary>
    public void Clear()
    {
        Interlocked.Exchange(ref _read, Interlocked.Read(ref _written));
    }
}
