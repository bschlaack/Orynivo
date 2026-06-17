namespace Orynivo.Audio;

/// <summary>
/// Feeds a continuous sine wave into a <see cref="SteinbergAsioStream"/> for ASIO device testing.
/// Dispose to stop the feed.
/// </summary>
public sealed class SineWaveFeeder : IDisposable
{
    private readonly SteinbergAsioStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private double _phase;

    /// <summary>Starts feeding a sine wave into <paramref name="stream"/>.</summary>
    /// <param name="stream">The ASIO stream to write into.</param>
    /// <param name="frequencyHz">Sine wave frequency in Hz.</param>
    /// <param name="gain">Amplitude scale factor (0.0–1.0).</param>
    public SineWaveFeeder(SteinbergAsioStream stream, double frequencyHz = 440, float gain = 0.05f)
    {
        _stream = stream;
        FrequencyHz = frequencyHz;
        Gain = gain;
        _worker = Task.Run(FeedLoopAsync);
    }

    /// <summary>Sine wave frequency in Hz.</summary>
    public double FrequencyHz { get; }

    /// <summary>Amplitude scale factor.</summary>
    public float Gain { get; }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
    }

    private async Task FeedLoopAsync()
    {
        var framesPerChunk = Math.Max(64, _stream.PreferredBufferSize);
        var samples = new float[framesPerChunk * _stream.Channels];
        var phaseStep = 2 * Math.PI * FrequencyHz / _stream.SampleRate;

        while (!_cts.IsCancellationRequested)
        {
            for (var frame = 0; frame < framesPerChunk; frame++)
            {
                var value = MathF.Sin((float)_phase) * Gain;
                _phase += phaseStep;
                if (_phase >= 2 * Math.PI)
                {
                    _phase -= 2 * Math.PI;
                }

                for (var channel = 0; channel < _stream.Channels; channel++)
                {
                    samples[(frame * _stream.Channels) + channel] = value;
                }
            }

            _stream.WriteInterleaved(samples);
            await Task.Delay(5, _cts.Token).ConfigureAwait(false);
        }
    }
}
