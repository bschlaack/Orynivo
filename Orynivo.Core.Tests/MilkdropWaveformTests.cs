using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the default waveform's per-mode geometry: the mode numbering the reference uses, the
/// shape each mode produces, and the valid-sample handling that keeps a short source from reading an
/// empty tail.
/// </summary>
public sealed class MilkdropWaveformTests
{
    private const int Size = 512;

    /// <summary>The reference's idle preset uses mode six for the single line.</summary>
    [Fact]
    public void ModeNumbering_MatchesTheReference()
    {
        Assert.Equal(6, (int)MilkdropWaveform.Mode.Line);
        Assert.Equal(7, (int)MilkdropWaveform.Mode.DoubleLine);
        Assert.Equal(8, (int)MilkdropWaveform.Mode.SpectrumLine);
        Assert.True(MilkdropWaveform.IsLoop(MilkdropWaveform.Mode.Circle));
        Assert.True(MilkdropWaveform.IsLoop(MilkdropWaveform.Mode.XyOscillationSpiral));
        Assert.False(MilkdropWaveform.IsLoop(MilkdropWaveform.Mode.Line));
        Assert.True(MilkdropWaveform.IsSpectrumMode(MilkdropWaveform.Mode.SpectrumLine));
    }

    /// <summary>The line spans the frame horizontally and stays inside it.</summary>
    [Fact]
    public void Generate_LineSpansTheFrame()
    {
        var (left, right) = Ramp();
        var xs = new float[Size];
        var ys = new float[Size];
        var xs2 = new float[Size];
        var ys2 = new float[Size];

        MilkdropWaveform.Generate(
            MilkdropWaveform.Mode.Line, left, right, Size, 0f, 0f, 0f, 1f, 1f, 0f, 320,
            xs, ys, xs2, ys2, out var count, out var secondCount);

        Assert.True(count > 8);
        Assert.Equal(0, secondCount);
        Assert.True(xs[0] < -0.9f);
        Assert.True(xs[count - 1] > 0.9f);
    }

    /// <summary>The double line fills both traces while every other mode fills one.</summary>
    [Fact]
    public void Generate_DoubleLineFillsBothTraces()
    {
        var (left, right) = Ramp();
        var xs = new float[Size];
        var ys = new float[Size];
        var xs2 = new float[Size];
        var ys2 = new float[Size];

        MilkdropWaveform.Generate(
            MilkdropWaveform.Mode.DoubleLine, left, right, Size, 0f, 0f, 0f, 1f, 1f, 0f, 320,
            xs, ys, xs2, ys2, out var count, out var secondCount);

        Assert.True(count > 8);
        Assert.Equal(count, secondCount);
        // The two traces sit on opposite sides of the centre.
        Assert.True(ys[count / 2] > 0f);
        Assert.True(ys2[secondCount / 2] < 0f);
    }

    /// <summary>A short source must not read an empty tail, so the geometry follows its length.</summary>
    [Fact]
    public void Generate_UsesTheValidSampleCount()
    {
        var shortLeft = new float[64];
        var shortRight = new float[64];
        for (var index = 0; index < 64; index++)
        {
            shortLeft[index] = MathF.Sin(index * 0.2f);
            shortRight[index] = shortLeft[index];
        }

        var xs = new float[Size];
        var ys = new float[Size];
        var xs2 = new float[Size];
        var ys2 = new float[Size];
        MilkdropWaveform.Generate(
            MilkdropWaveform.Mode.Line, shortLeft, shortRight, 64, 0f, 0f, 0f, 1f, 1f, 0f, 320,
            xs, ys, xs2, ys2, out var count, out _);

        // The line's samples come from the short source, so it is not a flat line.
        var spread = 0f;
        for (var index = 0; index < count; index++)
            spread = MathF.Max(spread, MathF.Abs(ys[index]));
        Assert.True(spread > 0.01f, $"spread={spread:F4}");
    }

    /// <summary>Preparation scales the first sample and then smooths the rest.</summary>
    [Fact]
    public void Prepare_ScalesAndSmooths()
    {
        var source = new float[] { 0f, 1f, 1f, 1f };
        var destination = new float[Size];

        MilkdropWaveform.Prepare(source, destination, 1f, 0.5f, out var count);

        Assert.Equal(4, count);
        Assert.Equal(0f, destination[0], 5);
        // Each later sample mixes the previous one, so it rises towards one.
        Assert.Equal(0.5f, destination[1], 5);
        Assert.Equal(0.75f, destination[2], 5);
        Assert.Equal(0.875f, destination[3], 5);
    }

    /// <summary>Builds a left/right ramp pair.</summary>
    /// <returns>The two channel buffers.</returns>
    private static (float[] Left, float[] Right) Ramp()
    {
        var left = new float[Size];
        var right = new float[Size];
        for (var index = 0; index < Size; index++)
        {
            left[index] = MathF.Sin(index * 0.05f);
            right[index] = MathF.Cos(index * 0.05f);
        }

        return (left, right);
    }
}
