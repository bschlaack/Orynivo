namespace Orynivo.Audio;

public sealed class SineWaveFeeder : IDisposable
{
    private readonly SteinbergAsioStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private double _phase;

    public SineWaveFeeder(SteinbergAsioStream stream, double frequencyHz = 440, float gain = 0.05f)
    {
        _stream = stream;
        FrequencyHz = frequencyHz;
        Gain = gain;
        _worker = Task.Run(FeedLoopAsync);
    }

    public double FrequencyHz { get; }
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
