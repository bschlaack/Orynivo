using Orynivo.Audio;
using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the hand-off between the audio players and the visualizer, which is what makes
/// the window show a picture at all.
/// </summary>
public sealed class VisualizerAudioHubTests
{
    /// <summary>An inactive hub ignores everything and analyses nothing.</summary>
    [Fact]
    public void InactiveHub_DoesNothing()
    {
        var hub = VisualizerAudioHub.Shared;
        hub.IsActive = false;
        hub.Clear();

        hub.PushPcm(new byte[4096], "s16le", 48_000);

        Assert.False(hub.TryAnalyze(out var source));
        Assert.Null(source);
    }

    /// <summary>Float PCM pushed while active is analysed and reported.</summary>
    [Fact]
    public void ActiveHub_AnalysesPushedFloats()
    {
        var hub = VisualizerAudioHub.Shared;
        hub.Clear();
        hub.IsActive = true;
        try
        {
            hub.PushFloats(Tone(1000f, 0.5f), 48_000);

            Assert.True(hub.TryAnalyze(out var source));
            Assert.NotNull(source);
            Assert.True(source!.Volume > 0f);
            Assert.Equal(AudioSpectrumAnalyzer.BandCount, source.Bands.Length);
        }
        finally
        {
            hub.IsActive = false;
            hub.Clear();
        }
    }

    /// <summary>Raw PCM in the format FFmpeg produced is converted and analysed.</summary>
    [Theory]
    [InlineData("f32le")]
    [InlineData("s16le")]
    [InlineData("s32le")]
    public void ActiveHub_AnalysesRawPcm(string format)
    {
        var hub = VisualizerAudioHub.Shared;
        hub.Clear();
        hub.IsActive = true;
        try
        {
            var bytes = BuildRaw(format);
            hub.PushPcm(bytes, format, 48_000);

            Assert.True(hub.TryAnalyze(out var source));
            Assert.True(source!.Volume > 0f);
        }
        finally
        {
            hub.IsActive = false;
            hub.Clear();
        }
    }

    /// <summary>Packed 24-bit PCM is converted as well.</summary>
    [Fact]
    public void ActiveHub_AnalysesPacked24Bit()
    {
        var hub = VisualizerAudioHub.Shared;
        hub.Clear();
        hub.IsActive = true;
        try
        {
            var floats = Tone(1000f, 0.5f);
            var bytes = new byte[floats.Length * 3];
            for (var index = 0; index < floats.Length; index++)
            {
                var value = (int)(floats[index] * 8_388_607f);
                bytes[(index * 3)] = (byte)(value & 0xFF);
                bytes[(index * 3) + 1] = (byte)((value >> 8) & 0xFF);
                bytes[(index * 3) + 2] = (byte)((value >> 16) & 0xFF);
            }

            hub.PushPcm(bytes, "s24le", 48_000);

            Assert.True(hub.TryAnalyze(out var source));
            Assert.True(source!.Volume > 0f);
        }
        finally
        {
            hub.IsActive = false;
            hub.Clear();
        }
    }

    /// <summary>An unsupported sample format is ignored instead of failing.</summary>
    [Fact]
    public void ActiveHub_IgnoresUnsupportedFormats()
    {
        var hub = VisualizerAudioHub.Shared;
        hub.Clear();
        hub.IsActive = true;
        try
        {
            hub.PushPcm(new byte[4096], "u8", 48_000);

            Assert.False(hub.TryAnalyze(out _));
        }
        finally
        {
            hub.IsActive = false;
            hub.Clear();
        }
    }

    private static float[] Tone(float frequency, float amplitude)
    {
        const int frames = 4096;
        var samples = new float[frames * 2];
        for (var index = 0; index < frames; index++)
        {
            var value = MathF.Sin(2f * MathF.PI * frequency * index / 48_000f) * amplitude;
            samples[index * 2] = value;
            samples[(index * 2) + 1] = value;
        }

        return samples;
    }

    private static byte[] BuildRaw(string format)
    {
        var floats = Tone(1000f, 0.5f);
        return format switch
        {
            "f32le" => System.Runtime.InteropServices.MemoryMarshal.AsBytes(floats.AsSpan()).ToArray(),
            "s16le" => BuildInt16(floats),
            _ => BuildInt32(floats)
        };
    }

    private static byte[] BuildInt16(float[] floats)
    {
        var bytes = new byte[floats.Length * 2];
        for (var index = 0; index < floats.Length; index++)
            BitConverter.TryWriteBytes(bytes.AsSpan(index * 2), (short)(floats[index] * 32767f));
        return bytes;
    }

    private static byte[] BuildInt32(float[] floats)
    {
        var bytes = new byte[floats.Length * 4];
        for (var index = 0; index < floats.Length; index++)
            BitConverter.TryWriteBytes(bytes.AsSpan(index * 4), (int)(floats[index] * 2_147_483_647f));
        return bytes;
    }
}
