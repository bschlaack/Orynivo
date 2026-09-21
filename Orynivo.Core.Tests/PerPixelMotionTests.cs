using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies that a per-pixel program may change the motion variables for the following pixel, which
/// is how Milkdrop behaves: they are per-vertex values there, not per-frame constants. A preset that
/// does not write one of them keeps the cheaper per-frame path.
/// </summary>
public sealed class PerPixelMotionTests
{
    /// <summary>Writing zoom inside the per-pixel program changes the picture.</summary>
    [Fact]
    public void RenderFrame_PerPixelZoomTakesEffect()
    {
        var plain = Render("fDecay=1\nwave_a=0\nper_pixel_1=x = x + 0.001;");
        var zoomed = Render("fDecay=1\nwave_a=0\nper_pixel_1=zoom = 1.4;\nper_pixel_2=x = x + 0.001;");

        Assert.True(MeanDifference(plain, zoomed) > 0.001f);
    }

    /// <summary>Writing the rotation inside the per-pixel program changes the picture.</summary>
    [Fact]
    public void RenderFrame_PerPixelRotationTakesEffect()
    {
        var plain = Render("fDecay=1\nwave_a=0\nper_pixel_1=x = x + 0.001;");
        var rotated = Render("fDecay=1\nwave_a=0\nper_pixel_1=rot = 0.5;\nper_pixel_2=x = x + 0.001;");

        Assert.True(MeanDifference(plain, rotated) > 0.001f);
    }

    /// <summary>A per-frame zoom still works, and is not affected by the new path.</summary>
    [Fact]
    public void RenderFrame_PerFrameZoomStillWorks()
    {
        var plain = Render("fDecay=1\nwave_a=0\nper_pixel_1=x = x + 0.001;");
        var zoomed = Render("fDecay=1\nwave_a=0\nper_frame_1=zoom = 1.4;\nper_pixel_1=x = x + 0.001;");

        Assert.True(MeanDifference(plain, zoomed) > 0.001f);
    }

    /// <summary>Renders a preset for a few frames and returns the pixels.</summary>
    /// <param name="presetText">Preset to render.</param>
    /// <returns>The rendered pixels.</returns>
    private static float[] Render(string presetText)
    {
        var renderer = new PresetRenderer(VisualizerPreset.Parse(presetText), 64, 36);
        var audio = new Silent();
        for (var frame = 0; frame < 3; frame++)
            renderer.RenderFrame(audio, 1d / 60d);

        return renderer.Output.Pixels.ToArray();
    }

    /// <summary>Computes the mean absolute difference of two frames.</summary>
    /// <param name="left">First frame.</param>
    /// <param name="right">Second frame.</param>
    /// <returns>The mean difference per pixel.</returns>
    private static float MeanDifference(float[] left, float[] right)
    {
        var total = 0f;
        for (var index = 0; index < left.Length; index += 4)
        {
            total += Math.Abs(left[index] - right[index])
                     + Math.Abs(left[index + 1] - right[index + 1])
                     + Math.Abs(left[index + 2] - right[index + 2]);
        }

        return total / Math.Max(1, (left.Length / 4) * 3);
    }

    /// <summary>An audio source with content on every band and a waveform.</summary>
    private sealed class Silent : IVisualizerAudioSource
    {
        private readonly float[] _bands = [0.7f, 0.55f, 0.4f, 0.25f];
        private readonly float[] _waveform = Enumerable.Range(0, 128)
            .Select(index => MathF.Sin(index * 0.2f) * 0.6f)
            .ToArray();

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _waveform;

        /// <inheritdoc/>
        public float Bass => 0.7f;

        /// <inheritdoc/>
        public float Mid => 0.55f;

        /// <inheritdoc/>
        public float Treble => 0.4f;

        /// <inheritdoc/>
        public float Volume => 0.6f;
    }
}
