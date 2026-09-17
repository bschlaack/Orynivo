using Orynivo.Server.Endpoints;
using Xunit;

namespace Orynivo.Server.Tests;

/// <summary>
/// Verifies validation and FFmpeg argument mapping for requested lossy stream
/// transcodes.
/// </summary>
public sealed class StreamTranscodeOptionsTests
{
    /// <summary>Supported formats parse case-insensitively with an explicit bitrate.</summary>
    /// <param name="format">Requested format.</param>
    /// <param name="bitrate">Requested bitrate.</param>
    /// <param name="expectedFormat">Normalized format.</param>
    [Theory]
    [InlineData("opus", 96, "opus")]
    [InlineData("OPUS", 128, "opus")]
    [InlineData("aac", 192, "aac")]
    [InlineData(" AAC ", 320, "aac")]
    public void TryParse_AcceptsSupportedFormats(string format, int bitrate, string expectedFormat)
    {
        Assert.True(StreamTranscodeOptions.TryParse(format, bitrate, out var options));
        Assert.NotNull(options);
        Assert.Equal(expectedFormat, options!.Format);
        Assert.Equal(bitrate, options.BitrateKbps);
    }

    /// <summary>An absent bitrate falls back to the per-format default.</summary>
    /// <param name="format">Requested format.</param>
    /// <param name="expectedBitrate">Expected default bitrate.</param>
    [Theory]
    [InlineData("opus", 128)]
    [InlineData("aac", 192)]
    public void TryParse_UsesDefaultBitrate(string format, int expectedBitrate)
    {
        Assert.True(StreamTranscodeOptions.TryParse(format, null, out var options));
        Assert.Equal(expectedBitrate, options!.BitrateKbps);
    }

    /// <summary>Unsupported formats are rejected.</summary>
    /// <param name="format">Requested format.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mp3")]
    [InlineData("flac")]
    [InlineData("wav")]
    public void TryParse_RejectsUnsupportedFormats(string? format)
        => Assert.False(StreamTranscodeOptions.TryParse(format, 128, out _));

    /// <summary>Bitrates outside the supported range are rejected.</summary>
    /// <param name="bitrate">Requested bitrate.</param>
    [Theory]
    [InlineData(32)]
    [InlineData(63)]
    [InlineData(321)]
    [InlineData(1024)]
    public void TryParse_RejectsOutOfRangeBitrates(int bitrate)
        => Assert.False(StreamTranscodeOptions.TryParse("opus", bitrate, out _));

    /// <summary>The response content type and FFmpeg arguments match the format.</summary>
    [Fact]
    public void Format_MapToContentTypeAndFfmpegArguments()
    {
        StreamTranscodeOptions.TryParse("opus", 96, out var opus);
        StreamTranscodeOptions.TryParse("aac", 192, out var aac);

        Assert.Equal("audio/ogg", opus!.ContentType);
        Assert.Contains("libopus", opus.FfmpegOutputArguments);
        Assert.Contains("-b:a 96k", opus.FfmpegOutputArguments);

        Assert.Equal("audio/aac", aac!.ContentType);
        Assert.Contains("aac", aac.FfmpegOutputArguments);
        Assert.Contains("-b:a 192k", aac.FfmpegOutputArguments);
    }
}
