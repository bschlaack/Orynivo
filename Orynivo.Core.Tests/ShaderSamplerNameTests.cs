using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the Milkdrop sampler qualifiers: <c>fc_</c>/<c>fw_</c>/<c>pc_</c>/<c>pw_</c> select the
/// sampling mode of a texture, not a different moment in time, and the frame sampler must honour the
/// wrap and filter they request.
/// </summary>
public sealed class ShaderSamplerNameTests
{
    /// <summary>The four qualifiers map onto their wrap and filter combinations.</summary>
    [Theory]
    [InlineData("sampler_main", "main", VisualizerTextureWrap.Repeat, false)]
    [InlineData("sampler_fw_main", "main", VisualizerTextureWrap.Repeat, false)]
    [InlineData("sampler_wf_main", "main", VisualizerTextureWrap.Repeat, false)]
    [InlineData("sampler_fc_main", "main", VisualizerTextureWrap.Clamp, false)]
    [InlineData("sampler_cf_main", "main", VisualizerTextureWrap.Clamp, false)]
    [InlineData("sampler_pw_main", "main", VisualizerTextureWrap.Repeat, true)]
    [InlineData("sampler_wp_main", "main", VisualizerTextureWrap.Repeat, true)]
    [InlineData("sampler_pc_main", "main", VisualizerTextureWrap.Clamp, true)]
    [InlineData("sampler_cp_main", "main", VisualizerTextureWrap.Clamp, true)]
    [InlineData("pc_main", "main", VisualizerTextureWrap.Clamp, true)]
    [InlineData("sampler_pw_noise_lq", "noise_lq", VisualizerTextureWrap.Repeat, true)]
    [InlineData("sampler_fc_noisevol_hq", "noisevol_hq", VisualizerTextureWrap.Clamp, false)]
    public void Parse_ResolvesTheBaseTextureAndMode(
        string name, string baseName, VisualizerTextureWrap wrap, bool nearest)
    {
        var parsed = ShaderSamplerName.Parse(name);

        Assert.Equal(baseName, parsed.BaseName);
        Assert.Equal(wrap, parsed.Wrap);
        Assert.Equal(nearest, parsed.Nearest);
    }

    /// <summary>Clamp holds the edge colour; repeat wraps to the other side.</summary>
    [Fact]
    public void SampleShader_ClampAndRepeatDifferOutsideTheFrame()
    {
        var buffer = TwoPixelRow();
        var sample = new float[4];

        buffer.SampleShader(1.5f, 0f, VisualizerTextureWrap.Clamp, nearest: false, sample);
        Assert.Equal(0f, sample[0], 5);
        Assert.Equal(1f, sample[1], 5);

        buffer.SampleShader(1.5f, 0f, VisualizerTextureWrap.Repeat, nearest: false, sample);
        Assert.Equal(0.5f, sample[0], 5);
        Assert.Equal(0.5f, sample[1], 5);
    }

    /// <summary>Nearest picks one texel; linear blends the two.</summary>
    [Fact]
    public void SampleShader_NearestAndLinearDifferInsideTheFrame()
    {
        var buffer = TwoPixelRow();
        var sample = new float[4];

        buffer.SampleShader(0.25f, 0f, VisualizerTextureWrap.Clamp, nearest: true, sample);
        Assert.Equal(1f, sample[0], 5);
        Assert.Equal(0f, sample[1], 5);

        buffer.SampleShader(0.75f, 0f, VisualizerTextureWrap.Clamp, nearest: true, sample);
        Assert.Equal(0f, sample[0], 5);
        Assert.Equal(1f, sample[1], 5);

        buffer.SampleShader(0.25f, 0f, VisualizerTextureWrap.Clamp, nearest: false, sample);
        Assert.Equal(0.75f, sample[0], 5);
        Assert.Equal(0.25f, sample[1], 5);
    }

    /// <summary>Builds a two-pixel row: red on the left, green on the right.</summary>
    /// <returns>The buffer.</returns>
    private static PixelBuffer TwoPixelRow()
    {
        var buffer = new PixelBuffer(2, 1);
        var pixels = buffer.Pixels;
        pixels[0] = 1f;
        pixels[1] = 0f;
        pixels[2] = 0f;
        pixels[3] = 1f;
        pixels[4] = 0f;
        pixels[5] = 1f;
        pixels[6] = 0f;
        pixels[7] = 1f;
        return buffer;
    }
}
