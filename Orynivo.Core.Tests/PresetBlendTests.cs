using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the pure preset-blend rules and the renderer support that eases one preset into the
/// next, so a preset switch cannot silently degrade back into a hard cut.
/// </summary>
public sealed class PresetBlendTests
{
    /// <summary>The eased curve is zero, one half, and one at the ends and midpoint.</summary>
    [Fact]
    public void CosineInterp_EasesTheEnds()
    {
        Assert.Equal(0f, PresetBlend.CosineInterp(0f), 5);
        Assert.Equal(0.5f, PresetBlend.CosineInterp(0.5f), 5);
        Assert.Equal(1f, PresetBlend.CosineInterp(1f), 5);
    }

    /// <summary>Progress outside the zero-to-one range clamps instead of overshooting.</summary>
    [Fact]
    public void CosineInterp_ClampsProgress()
    {
        Assert.Equal(0f, PresetBlend.CosineInterp(-3f), 5);
        Assert.Equal(1f, PresetBlend.CosineInterp(7f), 5);
    }

    /// <summary>An even mix returns the midpoint of the two values.</summary>
    [Fact]
    public void Interpolate_ReturnsTheMidpointAtHalfMix()
    {
        Assert.Equal(4f, PresetBlend.Interpolate(2f, 6f, 0.5f), 5);
        Assert.Equal(2f, PresetBlend.Interpolate(2f, 6f, 0f), 5);
        Assert.Equal(6f, PresetBlend.Interpolate(2f, 6f, 1f), 5);
    }

    /// <summary>The three variable sets are disjoint and classify the reference's examples.</summary>
    [Fact]
    public void VariableContract_ClassifiesTheReferenceVariables()
    {
        Assert.True(PresetBlend.IsInterpolated("decay"));
        Assert.True(PresetBlend.IsInterpolated("wave_r"));
        Assert.True(PresetBlend.IsInterpolated("ob_a"));
        Assert.True(PresetBlend.IsInterpolated("blur1_min"));

        Assert.True(PresetBlend.IsSnapped("wrap"));
        Assert.True(PresetBlend.IsSnapped("echo_orient"));
        Assert.True(PresetBlend.IsSnapped("solarize"));

        Assert.True(PresetBlend.IsMotion("zoom"));
        Assert.True(PresetBlend.IsMotion("warp"));

        foreach (var name in PresetBlend.InterpolatedVariables)
        {
            Assert.False(PresetBlend.IsSnapped(name), name);
            Assert.False(PresetBlend.IsMotion(name), name);
        }

        foreach (var name in PresetBlend.SnappedVariables)
            Assert.False(PresetBlend.IsMotion(name), name);
    }

    /// <summary>
    /// A renderer without a blend keeps the incoming preset's own decay; starting a blend at zero
    /// shows the outgoing preset's, and an even progress shows the average of the two.
    /// </summary>
    [Fact]
    public void SetBlend_EasesAnInterpolatedVariable()
    {
        var outgoing = VisualizerPreset.Parse("decay=0.9");
        var incoming = VisualizerPreset.Parse("decay=0.1");
        var renderer = new PresetRenderer(incoming, 40, 40);

        renderer.RenderFrame(new FakeAudio(), 1d / 60d);
        Assert.False(renderer.IsBlending);
        Assert.Equal(0.1f, renderer.ReadVariable("decay"), 5);

        renderer.SetBlend(outgoing, null, 0f);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);
        Assert.True(renderer.IsBlending);
        Assert.Equal(0.9f, renderer.ReadVariable("decay"), 5);

        renderer.SetBlend(outgoing, null, 0.5f);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);
        Assert.Equal(0.5f, renderer.ReadVariable("decay"), 5);

        renderer.SetBlend(null, null, 1f);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);
        Assert.False(renderer.IsBlending);
        Assert.Equal(0.1f, renderer.ReadVariable("decay"), 5);
    }

    /// <summary>A snapped switch keeps the outgoing value until the eased mix passes the midpoint.</summary>
    [Fact]
    public void SetBlend_SnapsABooleanAtTheMidpoint()
    {
        var outgoing = VisualizerPreset.Parse("wrap=1");
        var incoming = VisualizerPreset.Parse("wrap=0");
        var renderer = new PresetRenderer(incoming, 40, 40);

        renderer.SetBlend(outgoing, null, 0.4f);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);
        Assert.True(renderer.ReadVariable("wrap") >= 0.5f);

        renderer.SetBlend(outgoing, null, 0.6f);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);
        Assert.True(renderer.ReadVariable("wrap") < 0.5f);
    }

    /// <summary>The motion variables come from the incoming preset and are never eased.</summary>
    [Fact]
    public void SetBlend_DoesNotInterpolateMotion()
    {
        var outgoing = VisualizerPreset.Parse("zoom=3");
        var incoming = VisualizerPreset.Parse("zoom=1");
        var renderer = new PresetRenderer(incoming, 40, 40);

        renderer.SetBlend(outgoing, null, 0.5f);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        Assert.Equal(1f, renderer.ReadVariable("zoom"), 5);
    }

    /// <summary>
    /// The captured state carries the outgoing preset's user variables into the blend, so a preset
    /// that keeps a counter in <c>q1</c> continues from where it left off instead of restarting.
    /// </summary>
    [Fact]
    public void CaptureFrameState_CarriesTheOutgoingUserVariables()
    {
        var outgoing = VisualizerPreset.Parse("per_frame_1=q1 = q1 + 1; decay = q1 * 0.1;");
        var outgoingRenderer = new PresetRenderer(outgoing, 40, 40);
        outgoingRenderer.RenderFrame(new FakeAudio(), 1d / 60d);
        outgoingRenderer.RenderFrame(new FakeAudio(), 1d / 60d);
        var state = outgoingRenderer.CaptureFrameState();
        // After two outgoing frames q1 is two, so the carried state must continue that count.
        Assert.Equal(2d, state[outgoing.Layout.IndexOf("q1")], 5);

        var incoming = VisualizerPreset.Parse("decay=0.05");
        var renderer = new PresetRenderer(incoming, 40, 40);
        renderer.SetBlend(outgoing, state, 0f);
        renderer.RenderFrame(new FakeAudio(), 1d / 60d);

        // The outgoing block ran once more during the blend frame, so q1 became three and decay 0.3;
        // a fresh start from q1 zero would have produced 0.1.
        Assert.Equal(0.3f, renderer.ReadVariable("decay"), 5);
    }

    /// <summary>A source with fixed levels and a sine waveform.</summary>
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
