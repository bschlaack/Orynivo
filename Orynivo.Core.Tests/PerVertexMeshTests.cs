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

    /// <summary>
    /// A GPU warp needs the mesh values while the CPU keeps evaluating per pixel, so
    /// <c>MeshRequested</c> builds the mesh and exposes it without changing the CPU picture.
    /// </summary>
    [Fact]
    public void RenderFrame_MeshRequestedExposesTheMeshWithoutChangingThePicture()
    {
        const string perPixel = "zoom = 1.02 + 0.04 * x;";
        var text = "decay = 1;\nper_frame_1=wave_a = 1;\nper_pixel_1=" + perPixel;

        var without = new PresetRenderer(VisualizerPreset.Parse(text), 200, 150) { ParallelismEnabled = false };
        var with = new PresetRenderer(VisualizerPreset.Parse(text), 200, 150)
        {
            ParallelismEnabled = false,
            MeshRequested = true,
        };
        var audio = new FakeAudio();
        for (var frame = 0; frame < 3; frame++)
        {
            without.RenderFrame(audio, 1d / 60d);
            with.RenderFrame(audio, 1d / 60d);
        }

        // The CPU picture is the per-pixel one in both cases, so the mesh build must not change it.
        Assert.Equal(without.Output.Pixels.ToArray(), with.Output.Pixels.ToArray());

        var mesh = new float[(PresetRenderer.MeshGridX + 1) * (PresetRenderer.MeshGridY + 1) * PresetRenderer.MeshValues];
        Assert.True(with.TryCopyMeshMotion(mesh, out var meshX, out var meshY));
        Assert.Equal(PresetRenderer.MeshGridX, meshX);
        Assert.Equal(PresetRenderer.MeshGridY, meshY);
        // Milkdrop hands the program the aspect-scaled vertex position in zero-to-one space, so the
        // first vertex sits left of zero and the last one right of one; a zoom that grows with x is
        // therefore below the constant term at the first vertex and above it at the last.
        Assert.True(mesh[0] < 1.02f, $"zoom at the first vertex was {mesh[0]}");
        var last = mesh.Length - PresetRenderer.MeshValues;
        Assert.True(mesh[last] > 1.02f, $"zoom at the last vertex was {mesh[last]}");
    }

    /// <summary>A preset with no per-pixel motion exposes no mesh.</summary>
    [Fact]
    public void RenderFrame_ExposesNoMeshWithoutPerPixelMotion()
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse("decay = 1;"), 40, 40)
        {
            MeshRequested = true,
        };
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        var mesh = new float[(PresetRenderer.MeshGridX + 1) * (PresetRenderer.MeshGridY + 1) * PresetRenderer.MeshValues];
        Assert.False(renderer.TryCopyMeshMotion(mesh, out _, out _));
        Assert.NotNull(renderer.MeshSource);
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

