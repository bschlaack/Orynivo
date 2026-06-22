namespace Orynivo.Audio;

/// <summary>
/// Applies a stereo parametric equalizer and crossfades profile changes to
/// avoid discontinuities during PCM playback.
/// </summary>
internal sealed class ParametricEqualizer
{
    private const int TransitionMilliseconds = 50;
    private readonly int _sampleRate;
    private EqualizerChain _current;
    private EqualizerChain? _previous;
    private long _transitionFramesRemaining;
    private long _transitionFramesTotal;

    /// <summary>Initializes the equalizer for a stereo PCM sample rate.</summary>
    /// <param name="sampleRate">Output sample rate in hertz.</param>
    /// <param name="enabled">Whether the supplied profile is active.</param>
    /// <param name="profile">Initial equalizer profile.</param>
    internal ParametricEqualizer(int sampleRate, bool enabled, EqualizerProfile? profile)
    {
        _sampleRate = sampleRate;
        _current = EqualizerChain.Create(sampleRate, enabled ? profile : null);
    }

    /// <summary>Crossfades to a new equalizer configuration.</summary>
    /// <param name="enabled">Whether the supplied profile is active.</param>
    /// <param name="profile">New equalizer profile.</param>
    internal void Update(bool enabled, EqualizerProfile? profile)
    {
        _previous = _current;
        _current = EqualizerChain.Create(_sampleRate, enabled ? profile : null);
        _transitionFramesTotal = Math.Max(1, _sampleRate * TransitionMilliseconds / 1000);
        _transitionFramesRemaining = _transitionFramesTotal;
    }

    /// <summary>Clears filter histories after a discontinuous seek.</summary>
    internal void Reset()
    {
        _current.Reset();
        _previous = null;
        _transitionFramesRemaining = 0;
    }

    /// <summary>Processes one stereo frame.</summary>
    /// <param name="left">Left input sample.</param>
    /// <param name="right">Right input sample.</param>
    /// <returns>The processed stereo frame.</returns>
    internal (float Left, float Right) Process(float left, float right)
    {
        var current = _current.Process(left, right);
        if (_previous is null || _transitionFramesRemaining <= 0)
            return current;

        var previous = _previous.Process(left, right);
        var currentWeight = 1.0 - _transitionFramesRemaining / (double)_transitionFramesTotal;
        _transitionFramesRemaining--;
        if (_transitionFramesRemaining == 0)
            _previous = null;
        return (
            (float)(previous.Left + ((current.Left - previous.Left) * currentWeight)),
            (float)(previous.Right + ((current.Right - previous.Right) * currentWeight)));
    }

    private sealed class EqualizerChain
    {
        private readonly double _preamp;
        private readonly StereoBiquad[] _filters;

        private EqualizerChain(double preamp, StereoBiquad[] filters)
        {
            _preamp = preamp;
            _filters = filters;
        }

        internal static EqualizerChain Create(int sampleRate, EqualizerProfile? profile)
        {
            if (profile is null)
                return new EqualizerChain(1, []);
            var filters = profile.Filters
                .Where(filter => filter.Frequency > 0 && filter.Frequency < sampleRate / 2.0)
                .Select(filter => StereoBiquad.Create(sampleRate, filter))
                .ToArray();
            return new EqualizerChain(Math.Pow(10, profile.PreampDb / 20), filters);
        }

        internal (float Left, float Right) Process(float left, float right)
        {
            var output = (Left: left * _preamp, Right: right * _preamp);
            foreach (var filter in _filters)
                output = filter.Process(output.Left, output.Right);
            return ((float)output.Left, (float)output.Right);
        }

        internal void Reset()
        {
            foreach (var filter in _filters)
                filter.Reset();
        }
    }

    private sealed class StereoBiquad
    {
        private readonly double _b0;
        private readonly double _b1;
        private readonly double _b2;
        private readonly double _a1;
        private readonly double _a2;
        private double _leftX1;
        private double _leftX2;
        private double _leftY1;
        private double _leftY2;
        private double _rightX1;
        private double _rightX2;
        private double _rightY1;
        private double _rightY2;

        private StereoBiquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
        }

        internal static StereoBiquad Create(int sampleRate, EqualizerFilter filter)
        {
            var frequency = Math.Clamp(filter.Frequency, 1, sampleRate * 0.499);
            var q = Math.Clamp(filter.Q, 0.05, 100);
            var omega = 2 * Math.PI * frequency / sampleRate;
            var cosine = Math.Cos(omega);
            var sine = Math.Sin(omega);
            var alpha = sine / (2 * q);
            var amplitude = Math.Pow(10, filter.GainDb / 40);

            return filter.Type switch
            {
                EqualizerFilterType.Peak => new StereoBiquad(
                    1 + alpha * amplitude,
                    -2 * cosine,
                    1 - alpha * amplitude,
                    1 + alpha / amplitude,
                    -2 * cosine,
                    1 - alpha / amplitude),
                EqualizerFilterType.LowShelf => CreateLowShelf(amplitude, cosine, sine, Math.Min(q, 1)),
                EqualizerFilterType.HighShelf => CreateHighShelf(amplitude, cosine, sine, Math.Min(q, 1)),
                EqualizerFilterType.LowPass => new StereoBiquad(
                    (1 - cosine) / 2,
                    1 - cosine,
                    (1 - cosine) / 2,
                    1 + alpha,
                    -2 * cosine,
                    1 - alpha),
                EqualizerFilterType.HighPass => new StereoBiquad(
                    (1 + cosine) / 2,
                    -(1 + cosine),
                    (1 + cosine) / 2,
                    1 + alpha,
                    -2 * cosine,
                    1 - alpha),
                _ => throw new ArgumentOutOfRangeException(nameof(filter))
            };
        }

        internal (double Left, double Right) Process(double left, double right) =>
            (ProcessChannel(left, ref _leftX1, ref _leftX2, ref _leftY1, ref _leftY2),
             ProcessChannel(right, ref _rightX1, ref _rightX2, ref _rightY1, ref _rightY2));

        internal void Reset() =>
            _leftX1 = _leftX2 = _leftY1 = _leftY2 =
            _rightX1 = _rightX2 = _rightY1 = _rightY2 = 0;

        private double ProcessChannel(
            double input,
            ref double x1,
            ref double x2,
            ref double y1,
            ref double y2)
        {
            var output = (_b0 * input) + (_b1 * x1) + (_b2 * x2) - (_a1 * y1) - (_a2 * y2);
            x2 = x1;
            x1 = input;
            y2 = y1;
            y1 = output;
            return output;
        }

        private static StereoBiquad CreateLowShelf(double amplitude, double cosine, double sine, double q)
        {
            var alpha = sine / 2 * Math.Sqrt((amplitude + 1 / amplitude) * (1 / q - 1) + 2);
            var root = 2 * Math.Sqrt(amplitude) * alpha;
            return new StereoBiquad(
                amplitude * ((amplitude + 1) - (amplitude - 1) * cosine + root),
                2 * amplitude * ((amplitude - 1) - (amplitude + 1) * cosine),
                amplitude * ((amplitude + 1) - (amplitude - 1) * cosine - root),
                (amplitude + 1) + (amplitude - 1) * cosine + root,
                -2 * ((amplitude - 1) + (amplitude + 1) * cosine),
                (amplitude + 1) + (amplitude - 1) * cosine - root);
        }

        private static StereoBiquad CreateHighShelf(double amplitude, double cosine, double sine, double q)
        {
            var alpha = sine / 2 * Math.Sqrt((amplitude + 1 / amplitude) * (1 / q - 1) + 2);
            var root = 2 * Math.Sqrt(amplitude) * alpha;
            return new StereoBiquad(
                amplitude * ((amplitude + 1) + (amplitude - 1) * cosine + root),
                -2 * amplitude * ((amplitude - 1) + (amplitude + 1) * cosine),
                amplitude * ((amplitude + 1) + (amplitude - 1) * cosine - root),
                (amplitude + 1) - (amplitude - 1) * cosine + root,
                2 * ((amplitude - 1) - (amplitude + 1) * cosine),
                (amplitude + 1) - (amplitude - 1) * cosine - root);
        }
    }
}
