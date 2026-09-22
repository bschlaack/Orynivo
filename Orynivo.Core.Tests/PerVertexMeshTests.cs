using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the per-vertex mesh of the warp stage: Milkdrop evaluates the per-pixel program once per
/// mesh vertex and interpolates the motion across the quad, so a program that writes a constant has
/// to produce exactly the per-pixel result while a program that varies with the position is the
/// coarser, interpolated picture.
/// </summary>
public sealed class PerVertexMeshTests
{
    /// <summary>A constant motion value interpolates to itself, so both paths agree exactly.</summary>
    [Theory]
    [InlineData("zoom = 1.05;")]
    [InlineData("zoom = 1.02; rot = 0.03;")]
    [InlineData("cx = 0.1; cy = -0.05; sx = 1.1; sy = 0.95;")]
    [InlineData("zoom = 1.03; dx = 0.01; dy = -0.02;")]
    public void RenderFrame_MeshMatchesThePerPixelPathForAConstantMotion(string perPixel)
    {
        var mesh = Render(perPixel, mesh: true);
        var perPixelPath = Render(perPixel, mesh: false);

        Assert.Equal(perPixelPath, mesh);
    }

    /// <summary>A motion that varies with the position is interpolated, so the mesh differs.</summary>
    [Theory]
    [InlineData("zoom = 1.02 + 0.04 * x;")]
    [InlineData("cx = 0.1 * x; cy = 0.1 * y;")]
    [InlineData("rot = 0.2 * x;")]
    public void RenderFrame_MeshInterpolatesAVaryingMotion(string perPixel)
    {
        var mesh = Render(perPixel, mesh: true);
        var perPixelPath = Render(perPixel, mesh: false);

        Assert.NotEqual(perPixelPath, mesh);
    }

    /// <summary>
    /// A program that writes the sample position has no interpolated meaning, so it keeps the
    /// per-pixel path and the mesh setting cannot change its picture.
    /// </summary>
    [Fact]
    public void RenderFrame_PositionWritingProgramKeepsThePerPixelPath()
    {
        const string perPixel = "x = x + 0.01; y = y + 0.02;";
        var mesh = Render(perPixel, mesh: true);
        var perPixelPath = Render(perPixel, mesh: false);

        Assert.Equal(perPixelPath, mesh);
    }

    /// <summary>Renders a preset with a visible overlay so the feedback carries a picture.</summary>
    /// <param name="perPixel">Per-pixel program text.</param>
    /// <param name="mesh">Whether the mesh path is enabled.</param>
    /// <returns>The rendered pixels of the last frame.</returns>
    private static float[] Render(string perPixel, bool mesh)
    {
        // The mesh is 64 x 48, so the frame has to be larger than it for the interpolation to
        // show; at the default render size it is.
        var text = "decay = 1;\nper_frame_1=wave_a = 1;\nper_pixel_1=" + perPixel;
        var renderer = new PresetRenderer(VisualizerPreset.Parse(text), 200, 150)

        {
            MeshPerPixelEnabled = mesh,
            ParallelismEnabled = false,
        };
        for (var frame = 0; frame < 4; frame++)
            renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        return renderer.Output.Pixels.ToArray();
    }

    /// <summary>A source that reports fixed levels and a sine waveform.</summary>
    private sealed class FakeAudio : IVisualizerAudioSource
    {
        private readonly float[] _bands = [0.6f, 0.5f, 0.4f, 0.3f];
        private readonly float[] _waveform = new float[64];

        /// <summary>Creates the source.</summary>
        public FakeAudio()
        {
            for (var index = 0; index < _waveform.Length; index++)
                _waveform[index] = (float)Math.Sin(index * 0.2);
        }

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _waveform;

        /// <inheritdoc/>
        public float Volume => _bands[0];

        /// <inheritdoc/>
        public float Bass => _bands[0];

        /// <inheritdoc/>
        public float Mid => _bands[1];

        /// <inheritdoc/>
        public float Treble => _bands[2];
    }
}

