namespace Orynivo.Visualization;

/// <summary>
/// The default waveform's geometries, translated from the reference's <c>Waveforms</c> classes. Each
/// mode turns the scaled PCM data into vertices in the engine's minus-one-to-one space; the renderer
/// then smooths the polyline and draws it. The mode numbers are the reference's <c>nWaveMode</c>
/// values, which its idle preset confirms (mode six is the single line).
/// </summary>
public static class MilkdropWaveform
{
    /// <summary>Number of PCM samples the geometries read.</summary>
    public const int SampleCount = 512;

    /// <summary>
    /// The reference's <c>NUM_WAVEFORM_SAMPLES</c>: the number of vertices the default wave draws.
    /// The prepared buffer keeps <see cref="SampleCount"/> samples so the modes that peek
    /// <c>i + 32</c> ahead still read valid data, but the drawn vertex count is 480, not 512.
    /// </summary>
    public const int VertexCount = 480;

    /// <summary>The Milkdrop wave modes, in the reference's numbering.</summary>
    public enum Mode
    {
        /// <summary>A ring whose radius follows the right channel.</summary>
        Circle = 0,

        /// <summary>A spiral whose radius and angle follow the two channels.</summary>
        XyOscillationSpiral = 1,

        /// <summary>A centred spirograph from both channels.</summary>
        CenteredSpiro = 2,

        /// <summary>The centred spirograph with a volume-tied opacity.</summary>
        CenteredSpiroVolume = 3,

        /// <summary>A horizontal derivative line from both channels.</summary>
        DerivativeLine = 4,

        /// <summary>The explosive hash figure from both channels.</summary>
        ExplosiveHash = 5,

        /// <summary>The angle-adjustable single line.</summary>
        Line = 6,

        /// <summary>Two lines, one per channel.</summary>
        DoubleLine = 7,

        /// <summary>The single line drawn from the spectrum.</summary>
        SpectrumLine = 8,
    }

    /// <summary>Gets whether a mode reads the spectrum instead of the PCM waveform.</summary>
    /// <param name="mode">Wave mode.</param>
    /// <returns><see langword="true"/> when the mode is the spectrum line.</returns>
    public static bool IsSpectrumMode(Mode mode) => mode == Mode.SpectrumLine;

    /// <summary>Gets whether a mode draws a closed loop instead of an open strip.</summary>
    /// <param name="mode">Wave mode.</param>
    /// <returns><see langword="true"/> for the ring and spiral modes.</returns>
    public static bool IsLoop(Mode mode) =>
        mode is Mode.Circle or Mode.XyOscillationSpiral;

    /// <summary>
    /// Scales and smooths one channel into the buffer the geometries read. The reference scales the
    /// eight-bit sample data by <c>waveScale / 128</c> and then runs an IIR filter with
    /// <c>waveSmoothing</c>; our samples are normalized, so the 128 cancels.
    /// </summary>
    /// <param name="source">Normalized source samples.</param>
    /// <param name="destination">Destination buffer of <see cref="SampleCount"/> samples.</param>
    /// <param name="waveScale">Preset wave scale.</param>
    /// <param name="waveSmoothing">Preset wave smoothing, zero to one.</param>
    /// <param name="count">Receives the number of valid samples.</param>
    public static void Prepare(ReadOnlySpan<float> source, Span<float> destination, float waveScale, float waveSmoothing, out int count)
    {
        count = Math.Min(source.Length, Math.Min(destination.Length, SampleCount));
        for (var index = 0; index < count; index++)
            destination[index] = source[index];

        var smoothing = Math.Clamp(waveSmoothing, 0f, 1f);
        if (count > 0)
            destination[0] *= waveScale;
        var mix1 = waveScale * (1f - smoothing);
        for (var index = 1; index < count; index++)
            destination[index] = (destination[index] * mix1) + (destination[index - 1] * smoothing);
    }

    /// <summary>
    /// Generates the vertices of one mode. Only the double line fills the second list, so its
    /// <paramref name="secondCount"/> is zero for every other mode.
    /// </summary>
    /// <param name="mode">Wave mode.</param>
    /// <param name="pcmLeft">Prepared left samples.</param>
    /// <param name="pcmRight">Prepared right samples.</param>
    /// <param name="sampleCount">Number of valid samples in both buffers.</param>
    /// <param name="mystery">Preset wave mystery parameter.</param>
    /// <param name="waveX">Preset wave x in minus-one-to-one space.</param>
    /// <param name="waveY">Preset wave y in minus-one-to-one space.</param>
    /// <param name="aspectX">Horizontal aspect factor.</param>
    /// <param name="aspectY">Vertical aspect factor.</param>
    /// <param name="time">Preset time in seconds.</param>
    /// <param name="frameWidth">Frame width, which caps the line sample count.</param>
    /// <param name="verticesX">Receives the first list's x coordinates.</param>
    /// <param name="verticesY">Receives the first list's y coordinates.</param>
    /// <param name="secondX">Receives the second list's x coordinates.</param>
    /// <param name="secondY">Receives the second list's y coordinates.</param>
    /// <param name="count">Receives the first list's vertex count.</param>
    /// <param name="secondCount">Receives the second list's vertex count.</param>
    public static void Generate(
        Mode mode,
        ReadOnlySpan<float> pcmLeft,
        ReadOnlySpan<float> pcmRight,
        int sampleCount,
        float mystery,
        float waveX,
        float waveY,
        float aspectX,
        float aspectY,
        float time,
        int frameWidth,
        Span<float> verticesX,
        Span<float> verticesY,
        Span<float> secondX,
        Span<float> secondY,
        out int count,
        out int secondCount)
    {
        secondCount = 0;
        var buffer = Math.Max(2, Math.Min(sampleCount, VertexCount));
        switch (mode)
        {
            case Mode.Circle:
                count = GenerateCircle(pcmRight, buffer, mystery, waveX, waveY, aspectX, aspectY, time, verticesX, verticesY);
                return;
            case Mode.XyOscillationSpiral:
                count = GenerateSpiral(pcmLeft, pcmRight, buffer, mystery, waveX, waveY, aspectX, aspectY, time, verticesX, verticesY);
                return;
            case Mode.CenteredSpiro:
            case Mode.CenteredSpiroVolume:
                count = GenerateCenteredSpiro(pcmLeft, pcmRight, buffer, waveX, waveY, aspectX, aspectY, verticesX, verticesY);
                return;
            case Mode.DerivativeLine:
                count = GenerateDerivativeLine(pcmLeft, pcmRight, buffer, mystery, waveX, waveY, frameWidth, verticesX, verticesY);
                return;
            case Mode.ExplosiveHash:
                count = GenerateExplosiveHash(pcmLeft, pcmRight, buffer, waveX, waveY, aspectX, aspectY, time, verticesX, verticesY);
                return;
            case Mode.DoubleLine:
                count = GenerateDoubleLine(pcmLeft, buffer, waveX, waveY, frameWidth, verticesX, verticesY, mystery, separationSign: 1f);
                secondCount = GenerateDoubleLine(pcmRight, buffer, waveX, waveY, frameWidth, secondX, secondY, mystery, separationSign: -1f);
                return;
            case Mode.SpectrumLine:
                count = GenerateSpectrumLine(pcmLeft, buffer, mystery, waveX, waveY, verticesX, verticesY);
                return;
            default:
                count = GenerateLine(pcmLeft, buffer, mystery, waveX, waveY, frameWidth, verticesX, verticesY);
                return;
        }
    }

    /// <summary>The reference's edge-clipping helper for the line-shaped modes.</summary>
    /// <param name="angle">Clip angle in radians.</param>
    /// <param name="waveX">Wave centre x.</param>
    /// <param name="waveY">Wave centre y.</param>
    /// <param name="samples">Sample count the line is split into.</param>
    /// <param name="edgeX">Receives the first edge x.</param>
    /// <param name="edgeY">Receives the first edge y.</param>
    /// <param name="distanceX">Receives the per-sample x step.</param>
    /// <param name="distanceY">Receives the per-sample y step.</param>
    /// <param name="perpetualX">Receives the perpendicular x unit vector.</param>
    /// <param name="perpetualY">Receives the perpendicular y unit vector.</param>
    private static void ClipWaveformEdges(
        float angle, float waveX, float waveY, int samples,
        out float edgeX, out float edgeY, out float distanceX, out float distanceY,
        out float perpetualX, out float perpetualY)
    {
        distanceX = MathF.Cos(angle);
        distanceY = MathF.Sin(angle);
        var edgeXs = new[]
        {
            (waveX * MathF.Cos(angle + 1.57f)) - (distanceX * 3f),
            (waveX * MathF.Cos(angle + 1.57f)) + (distanceX * 3f),
        };
        var edgeYs = new[]
        {
            (waveX * MathF.Sin(angle + 1.57f)) - (distanceY * 3f),
            (waveX * MathF.Sin(angle + 1.57f)) + (distanceY * 3f),
        };

        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                var t = 0f;
                var clip = false;
                switch (j)
                {
                    case 0 when edgeXs[i] > 1.1f:
                        t = (1.1f - edgeXs[1 - i]) / (edgeXs[i] - edgeXs[1 - i]);
                        clip = true;
                        break;
                    case 1 when edgeXs[i] < -1.1f:
                        t = (-1.1f - edgeXs[1 - i]) / (edgeXs[i] - edgeXs[1 - i]);
                        clip = true;
                        break;
                    case 2 when edgeYs[i] > 1.1f:
                        t = (1.1f - edgeYs[1 - i]) / (edgeYs[i] - edgeYs[1 - i]);
                        clip = true;
                        break;
                    case 3 when edgeYs[i] < -1.1f:
                        t = (-1.1f - edgeYs[1 - i]) / (edgeYs[i] - edgeYs[1 - i]);
                        clip = true;
                        break;
                }

                if (!clip)
                    continue;

                var diffX = edgeXs[i] - edgeXs[1 - i];
                var diffY = edgeYs[i] - edgeYs[1 - i];
                edgeXs[i] = edgeXs[1 - i] + (diffX * t);
                edgeYs[i] = edgeYs[1 - i] + (diffY * t);
            }
        }

        distanceX = (edgeXs[1] - edgeXs[0]) / samples;
        distanceY = (edgeYs[1] - edgeYs[0]) / samples;
        edgeX = edgeXs[0];
        edgeY = edgeYs[0];
        var angle2 = MathF.Atan2(distanceY, distanceX);
        perpetualX = MathF.Cos(angle2 + 1.57f);
        perpetualY = MathF.Sin(angle2 + 1.57f);
    }

    /// <summary>Half the sample count, reduced on a narrow frame, the way the line modes do it.</summary>
    /// <param name="buffer">Valid sample count.</param>
    /// <param name="frameWidth">Frame width.</param>
    /// <returns>The sample count.</returns>
    private static int LineSamples(int buffer, int frameWidth)
    {
        var samples = buffer / 2;
        if (samples > frameWidth / 3)
            samples /= 3;
        return Math.Max(1, samples);
    }

    private static int GenerateLine(
        ReadOnlySpan<float> pcm, int buffer, float mystery, float waveX, float waveY, int frameWidth,
        Span<float> xs, Span<float> ys)
    {
        var samples = Math.Min(LineSamples(buffer, frameWidth), xs.Length);
        ClipWaveformEdges(1.57f * mystery, waveX, waveY, samples,
            out var edgeX, out var edgeY, out var distanceX, out var distanceY,
            out var perpetualX, out var perpetualY);
        var offset = Math.Max(0, (buffer - samples) / 2);
        for (var i = 0; i < samples; i++)
        {
            var value = 0.25f * pcm[Math.Min(pcm.Length - 1, i + offset)];
            xs[i] = edgeX + (distanceX * i) + (perpetualX * value);
            ys[i] = edgeY + (distanceY * i) + (perpetualY * value);
        }

        return samples;
    }

    private static int GenerateDoubleLine(
        ReadOnlySpan<float> pcm, int buffer, float waveX, float waveY, int frameWidth,
        Span<float> xs, Span<float> ys, float mystery, float separationSign)
    {
        var samples = Math.Min(LineSamples(buffer, frameWidth), xs.Length);
        ClipWaveformEdges(1.57f * mystery, waveX, waveY, samples,
            out var edgeX, out var edgeY, out var distanceX, out var distanceY,
            out var perpetualX, out var perpetualY);
        var separation = separationSign * MathF.Pow((waveY * 0.5f) + 0.5f, 2f);
        var offset = Math.Max(0, (buffer - samples) / 2);
        for (var i = 0; i < samples; i++)
        {
            var value = (0.25f * pcm[Math.Min(pcm.Length - 1, i + offset)]) + separation;
            xs[i] = edgeX + (distanceX * i) + (perpetualX * value);
            ys[i] = edgeY + (distanceY * i) + (perpetualY * value);
        }

        return samples;
    }

    private static int GenerateCircle(
        ReadOnlySpan<float> pcm, int buffer, float mystery, float waveX, float waveY,
        float aspectX, float aspectY, float time, Span<float> xs, Span<float> ys)
    {
        var samples = Math.Min(buffer / 2, xs.Length);
        var offset = Math.Max(0, (buffer - samples) / 2);
        for (var i = 0; i < samples; i++)
        {
            var radius = 0.5f + (0.4f * pcm[Math.Min(pcm.Length - 1, i + offset)]) + mystery;
            var angle = (i / (float)samples * 6.28f) + (time * 0.2f);
            if (i < samples / 10)
            {
                var mix = i / (samples * 0.1f);
                mix = 0.5f - (0.5f * MathF.Cos(mix * 3.1416f));
                var radius2 = 0.5f + (0.4f * pcm[Math.Min(pcm.Length - 1, i + samples + offset)]) + mystery;
                radius = (radius2 * (1f - mix)) + (radius * mix);
            }

            xs[i] = (radius * MathF.Cos(angle) * aspectY) + waveX;
            ys[i] = (radius * MathF.Sin(angle) * aspectX) + waveY;
        }

        return samples;
    }

    private static int GenerateSpiral(
        ReadOnlySpan<float> pcmLeft, ReadOnlySpan<float> pcmRight, int buffer, float mystery, float waveX, float waveY,
        float aspectX, float aspectY, float time, Span<float> xs, Span<float> ys)
    {
        var samples = Math.Min(buffer / 2, xs.Length);
        for (var i = 0; i < samples; i++)
        {
            var radius = 0.53f + (0.43f * pcmRight[Math.Min(pcmRight.Length - 1, i)]) + mystery;
            var angle = (pcmLeft[Math.Min(pcmLeft.Length - 1, i + 32)] * 1.57f) + (time * 2.3f);
            xs[i] = (radius * MathF.Cos(angle) * aspectY) + waveX;
            ys[i] = (radius * MathF.Sin(angle) * aspectX) + waveY;
        }

        return samples;
    }

    private static int GenerateCenteredSpiro(
        ReadOnlySpan<float> pcmLeft, ReadOnlySpan<float> pcmRight, int buffer, float waveX, float waveY,
        float aspectX, float aspectY, Span<float> xs, Span<float> ys)
    {
        var samples = Math.Min(buffer, xs.Length);
        for (var i = 0; i < samples; i++)
        {
            xs[i] = (pcmRight[Math.Min(pcmRight.Length - 1, i)] * aspectY) + waveX;
            ys[i] = (pcmLeft[Math.Min(pcmLeft.Length - 1, i + 32)] * aspectX) + waveY;
        }

        return samples;
    }

    private static int GenerateDerivativeLine(
        ReadOnlySpan<float> pcmLeft, ReadOnlySpan<float> pcmRight, int buffer, float mystery, float waveX, float waveY,
        int frameWidth, Span<float> xs, Span<float> ys)
    {
        var samples = Math.Min(buffer, xs.Length);
        if (samples > frameWidth / 3)
            samples /= 3;
        samples = Math.Max(1, samples);
        var offset = Math.Max(0, (buffer - samples) / 2);
        var w1 = 0.45f + (0.5f * ((mystery * 0.5f) + 0.5f));
        var w2 = 1f - w1;
        var inverse = 1f / samples;
        for (var i = 0; i < samples; i++)
        {
            var x = -1f + (2f * i * inverse) + waveX + (pcmRight[Math.Min(pcmRight.Length - 1, i + 25 + offset)] * 0.44f);
            var y = (pcmLeft[Math.Min(pcmLeft.Length - 1, i + offset)] * 0.47f) + waveY;
            if (i > 1)
            {
                x = (x * w2) + (w1 * ((xs[i - 1] * 2f) - xs[i - 2]));
                y = (y * w2) + (w1 * ((ys[i - 1] * 2f) - ys[i - 2]));
            }

            xs[i] = x;
            ys[i] = y;
        }

        return samples;
    }

    private static int GenerateExplosiveHash(
        ReadOnlySpan<float> pcmLeft, ReadOnlySpan<float> pcmRight, int buffer, float waveX, float waveY,
        float aspectX, float aspectY, float time, Span<float> xs, Span<float> ys)
    {
        var samples = Math.Min(buffer, xs.Length);
        var cosine = MathF.Cos(time * 0.3f);
        var sine = MathF.Sin(time * 0.3f);
        for (var i = 0; i < samples; i++)
        {
            var left = pcmLeft[Math.Min(pcmLeft.Length - 1, i)];
            var left32 = pcmLeft[Math.Min(pcmLeft.Length - 1, i + 32)];
            var right = pcmRight[Math.Min(pcmRight.Length - 1, i)];
            var right32 = pcmRight[Math.Min(pcmRight.Length - 1, i + 32)];
            var x0 = (right * left32) + (left * right32);
            var y0 = (right * right) - (left32 * left32);
            xs[i] = ((x0 * cosine) - (y0 * sine)) * aspectY + waveX;
            ys[i] = ((x0 * sine) + (y0 * cosine)) * aspectX + waveY;
        }

        return samples;
    }

    private static int GenerateSpectrumLine(
        ReadOnlySpan<float> pcm, int buffer, float mystery, float waveX, float waveY, Span<float> xs, Span<float> ys)
    {
        var samples = Math.Min(Math.Min(256, buffer / 2), xs.Length);
        ClipWaveformEdges(1.57f * mystery, waveX, waveY, samples,
            out var edgeX, out var edgeY, out var distanceX, out var distanceY,
            out var perpetualX, out var perpetualY);
        for (var i = 0; i < samples; i++)
        {
            var a = pcm[Math.Min(pcm.Length - 1, i * 2)];
            var b = pcm[Math.Min(pcm.Length - 1, (i * 2) + 1)];
            var f = 0.1f * MathF.Log(MathF.Max(1e-6f, a + b));
            xs[i] = edgeX + (distanceX * i) + (perpetualX * f);
            ys[i] = edgeY + (distanceY * i) + (perpetualY * f);
        }

        return samples;
    }
}
