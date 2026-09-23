using Orynivo.Audio;
using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Reference-contract regressions for custom elements and GPU frame inputs.</summary>
public sealed class MilkdropFidelityRegressionTests
{
    private const string Wave = "wave_a=0\nwavecode_0_enabled=1\nwavecode_0_samples=2\nwavecode_0_g=0\nwavecode_0_b=0\nwave_0_per_point_1=x=0.25+sample*0.5;y=0.5;\n";

    /// <summary>Each frame increments private user state once, while T values restart at init.</summary>
    [Fact]
    public void WaveFrameStateRunsOnceAndRestoresInitT()
    {
        var actual = Create(Wave + "wave_0_init1=t1=2;counter=0;\nwave_0_per_frame1=counter+=1;t1+=1;r=counter*0.1;a=t1/3;");
        for (var frame = 1; frame <= 3; frame++)
        {
            actual.RenderFrame(new Audio(), 1d / 60d);
            var expected = Create(Wave + $"wave_0_per_frame1=r={frame}*0.1;a=1;");
            expected.RenderFrame(new Audio(), 1d / 60d);
            Assert.Equal(expected.OverlayFrame.Pixels.ToArray(), actual.OverlayFrame.Pixels.ToArray());
        }
    }

    /// <summary>Wave init, frame and point writes cannot corrupt preset Q or another wave.</summary>
    [Fact]
    public void WaveContextsAreIsolated()
    {
        var renderer = Create(Wave + "per_frame_1=q1=0.75;\nwave_0_init1=q1=99;private_var=1;\n" +
            "wave_0_per_frame1=q1=9;private_var=1;\nwave_0_per_point_2=q1=17;\n" +
            "wavecode_1_enabled=1\nwavecode_1_samples=2\nwave_1_per_frame1=a=private_var;\n" +
            "wave_1_per_point1=x=sample;y=0.9;r=0;g=1;b=0;");
        renderer.RenderFrame(new Audio(), 1d / 60d);
        Assert.Equal(0.75f, renderer.ReadVariable("q1"));
        Assert.Equal(0f, renderer.ReadVariable("private_var"));
        var pixels = renderer.OverlayFrame.Pixels;
        for (var i = 1; i < pixels.Length; i += 4) Assert.Equal(0f, pixels[i]);
    }

    /// <summary>A real shapecode shape uses zero-to-one coordinates and its own equation block.</summary>
    [Fact]
    public void ShapeNamespaceEnabledAndCoordinatesAreRespected()
    {
        var text = "wave_a=0\nshapecode_2_enabled=1\nshapecode_2_x=0.25\nshapecode_2_y=0.75\n" +
            "shapecode_2_rad=0.3\nshapecode_2_a=1\nshapecode_2_a2=1\nshapecode_2_border_a=0\n" +
            "shape_2_per_frame1=r=1;g=0;b=0;r2=1;g2=0;b2=0;";
        var renderer = Create(text);
        renderer.RenderFrame(new Audio(), 1d / 60d);
        Assert.True(renderer.OverlayFrame.GetPixel(16, 16, 0) > 0.9f);
        Assert.Equal(0f, renderer.OverlayFrame.GetPixel(16, 48, 0));
        var disabled = Create(text.Replace("enabled=1", "enabled=0"));
        disabled.RenderFrame(new Audio(), 1d / 60d);
        Assert.Equal(0f, disabled.OverlayFrame.MeanBrightness());
    }

    /// <summary>Custom waveform PCM reaches per-point code in Milkdrop's 128-times amplitude units.</summary>
    [Fact]
    public void WavePcmScaleMatchesReference()
    {
        var renderer = Create(Wave + "wavecode_0_smoothing=0\nwavecode_0_scaling=1\nfWaveScale=1\n" +
            "wave_0_per_point_2=r=value1;g=value2;b=0;");
        renderer.RenderFrame(new Audio(), 1d / 60d);
        Assert.InRange(renderer.OverlayFrame.GetPixel(24, 31, 0), 0.255f, 0.257f);
    }

    /// <summary>Warp constants and finite FPS reach the published frame on every iteration.</summary>
    [Fact]
    public void FrameParametersCarryWarpSpeedScaleAndWrap()
    {
        var renderer = Create("wave_a=0\nfWarpAnimSpeed=2\nfWarpScale=3\nbTexWrap=1");
        for (var frame = 1; frame <= 3; frame++)
        {
            renderer.RenderFrame(new Audio(), 0.02);
            var parameters = renderer.ReadFrameParameters();
            Assert.Equal(frame * 0.04f, parameters.WarpTime, 5);
            Assert.Equal(3f, parameters.WarpScale);
            Assert.True(parameters.TextureWrap);
            Assert.Equal(50f, renderer.ReadVariable("fps"));
        }
    }

    /// <summary>The stereo trace contains a complete contiguous 512-sample window.</summary>
    [Fact]
    public void StereoWaveformIsNotDecimated()
    {
        var analyzer = new AudioSpectrumAnalyzer(44100);
        var pcm = new float[4096];
        for (var i = 0; i < 2048; i++) { pcm[i * 2] = i / 2048f; pcm[i * 2 + 1] = -pcm[i * 2]; }
        analyzer.Analyze(pcm);
        Assert.Equal(512, analyzer.WaveformLeft.Length);
        for (var i = 0; i < 512; i++)
        {
            Assert.Equal((1536 + i) / 2048f, analyzer.WaveformLeft[i]);
            Assert.Equal(-(1536 + i) / 2048f, analyzer.WaveformRight[i]);
        }
    }

    private static PresetRenderer Create(string text) => new(VisualizerPreset.Parse(text), 64, 64) { ExpressionsOnly = true, MeshRequested = true };

    /// <summary>A constant stereo PCM input, independent of frame time.</summary>
    private sealed class Audio : IVisualizerAudioSource
    {
        private readonly float[] _samples = Enumerable.Repeat(0.5f, 512).ToArray();
        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _samples;
        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _samples;
        /// <inheritdoc/>
        public float Volume => 0.5f;
        /// <inheritdoc/>
        public float Bass => 0.5f;
        /// <inheritdoc/>
        public float Mid => 0.5f;
        /// <inheritdoc/>
        public float Treble => 0.5f;
    }
}

