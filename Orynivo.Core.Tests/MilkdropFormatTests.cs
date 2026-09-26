using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the <c>.milk</c> file format handling: the <c>[presetNN]</c> sections, the declared
/// preset version, and a small corpus of presets written in the real format. The corpus is
/// hand-written here because third-party presets are licensed by their authors and are never
/// bundled; it uses the real key set, a multi-section file, and shader source, so a change that
/// breaks real presets breaks these tests.
/// </summary>
public sealed class MilkdropFormatTests
{
    /// <summary>A multi-preset file is split at its section headers.</summary>
    [Fact]
    public void ParseSections_SplitsAtTheHeaders()
    {
        var sections = VisualizerPreset.ParseSections(
            "[preset00]\nname=First\nfDecay=0.9\n[preset01]\nname=Second\nfDecay=0.8\n");

        Assert.Equal(2, sections.Count);
        Assert.Contains("First", sections[0], StringComparison.Ordinal);
        Assert.Contains("Second", sections[1], StringComparison.Ordinal);
        Assert.DoesNotContain("[preset", sections[0], StringComparison.Ordinal);
    }

    /// <summary>A file without headers is one section, and an empty file has none.</summary>
    [Fact]
    public void ParseSections_HandlesSingleAndEmptyFiles()
    {
        Assert.Single(VisualizerPreset.ParseSections("name=Only\nfDecay=0.9\n"));
        Assert.Empty(VisualizerPreset.ParseSections(string.Empty));
        Assert.Empty(VisualizerPreset.ParseSections(null));
    }

    /// <summary>The declared preset version is reported.</summary>
    [Theory]
    [InlineData("MILKDROP_PRESET_VERSION=201", 201)]
    [InlineData("PSVERSION=200", 200)]
    [InlineData("preset_version=100", 100)]
    [InlineData("name=NoVersion", 0)]
    public void Parse_ReadsThePresetVersion(string line, int expected)
    {
        var preset = VisualizerPreset.Parse(line + "\nname=Version");

        Assert.Equal(expected, preset.Version);
    }

    /// <summary>A preset written the way Milkdrop writes it parses completely.</summary>
    [Fact]
    public void Parse_ReadsARealisticPreset()
    {
        var preset = VisualizerPreset.Parse(Corpus[0]);

        Assert.Equal("Fixture - Spectrum Cloud", preset.Name);
        Assert.Equal(201, preset.Version);
        Assert.Equal(1f, preset.Defaults["wave_mode"]);
        Assert.Equal(0.5f, preset.Defaults["ob_a"]);
        Assert.False(preset.PerFrame.IsEmpty);
        Assert.Single(preset.WarpShaders);
        Assert.NotEmpty(preset.Shapes);
    }

    /// <summary>Every preset of the corpus renders a picture with audio.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RenderFrame_DrawsEveryCorpusPreset(int index)
    {
        var preset = VisualizerPreset.Parse(Corpus[index]);
        var renderer = new PresetRenderer(preset, 48, 27);

        for (var frame = 0; frame < 3; frame++)
            renderer.RenderFrame(new CorpusAudio(), 1d / 60d);

        Assert.True(Mean(renderer) > 0f, $"preset {preset.Name} rendered an empty frame");
    }

    /// <summary>Hand-written presets in the real Milkdrop format.</summary>
    private static readonly string[] Corpus =
    [
        """
        [preset00]
        MILKDROP_PRESET_VERSION=201
        name=Fixture - Spectrum Cloud
        fDecay=0.95
        zoom=1.01
        rot=0.02
        nWaveMode=1
        bWaveDots=0
        ob_a=0.5
        ob_r=0.1
        ob_g=0.2
        ob_b=0.4
        wave_r=0.8
        wave_g=0.9
        wave_b=1.0
        per_frame_1=q1 = q1 + 0.01;
        per_frame_2=zoom = 1.0 + bass * 0.05;
        per_frame_3=rot = 0.02 + mid * 0.01;
        per_pixel_1=x = x + cos(ang) * 0.01 * rad;
        per_pixel_2=y = y + sin(ang) * 0.01 * rad;
        shape_0_sides=6
        shape_0_x=0
        shape_0_y=0
        shape_0_rad=0.15
        shape_0_r=0.2
        shape_0_g=0.6
        shape_0_b=1.0
        shape_0_a=0.3
        shape_0_per_frame_1=rad = 0.15 + bass * 0.2;
        warp_1=float4 main(float2 uv : TEXCOORD0) : COLOR
        {
            float3 col = tex2D(sampler_main, uv).rgb;
            return float4(col * saturate(0.9 + bass), 1.0);
        }
        """,
        """
        [preset00]
        MILKDROP_PRESET_VERSION=200
        name=Fixture - Echo Lines
        fDecay=0.9
        nWaveMode=3
        bWaveThick=1
        fVideoEchoZoom=1.02
        fVideoEchoAlpha=0.5
        nVideoEchoOrientation=1
        darken_center=0.2
        wave_r=1
        wave_g=0.4
        wave_b=0.2
        per_frame_1=q1 = q1 + 0.02;
        per_frame_2=wave_x = 0.5 + sin(q1) * 0.1;
        wave_0_per_point_1=y = sample * (0.6 + bass);
        comp_1=float4 main(float2 uv : TEXCOORD0) : COLOR
        {
            float3 col = GetBlur1(uv).rgb;
            return float4(col, 1.0);
        }
        """,
        """
        [preset00]
        name=Fixture - Minimal
        fDecay=0.88
        per_frame_1=wave_a = 0.5 + bass;
        per_pixel_1=rad = rad * 1.05;
        """,
        """
        [preset00]
        name=Fixture - Ignored Second Section
        fDecay=0.5
        """
    ];

    /// <summary>An audio source with content on every band.</summary>
    private sealed class CorpusAudio : IVisualizerAudioSource
    {
        private readonly float[] _bands = [0.7f, 0.5f, 0.4f, 0.2f];
        private readonly float[] _waveform = Enumerable.Range(0, 128)
            .Select(index => MathF.Sin(index * 0.2f) * 0.7f)
            .ToArray();

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _waveform;

        /// <inheritdoc/>
        public float Bass => 0.7f;

        /// <inheritdoc/>
        public float Mid => 0.5f;

        /// <inheritdoc/>
        public float Treble => 0.4f;

        /// <inheritdoc/>
        public float Volume => 0.6f;
    }

    private static float Mean(PresetRenderer renderer)
    {
        var pixels = renderer.Output.Pixels;
        var total = 0f;
        for (var index = 0; index < pixels.Length; index += 4)
            total += (pixels[index] * 0.3f) + (pixels[index + 1] * 0.6f) + (pixels[index + 2] * 0.1f);

        return total / Math.Max(1, pixels.Length / 4);
    }
}
