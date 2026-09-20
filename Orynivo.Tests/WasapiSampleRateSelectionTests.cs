using Orynivo.Audio;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies the exclusive-mode sample-rate probe order. A DSD source must prefer an exact
/// division of its rate instead of the device's maximum rate, which resamples DSD by a
/// fractional ratio and produced nothing but noise on a Sound BlasterX AE-5.
/// </summary>
public sealed class WasapiSampleRateSelectionTests
{
    /// <summary>DSD64 with the DSD/16 hint prefers its exact divisions, highest first.</summary>
    [Fact]
    public void OrderCandidateSampleRates_PrefersExactDsdDivisions()
    {
        var info = Dsd(sourceSampleRate: 352_800, hint: 176_400);

        var rates = WasapiAudioPlayer.OrderCandidateSampleRates(info);

        Assert.Equal([176_400, 88_200, 44_100, 22_050, 11_025], rates.Take(5));
    }

    /// <summary>A fractional rate such as 192000 is never offered before an exact division.</summary>
    [Fact]
    public void OrderCandidateSampleRates_KeepsFractionalRatesAfterTheDivisions()
    {
        var info = Dsd(sourceSampleRate: 352_800, hint: 176_400);

        var rates = WasapiAudioPlayer.OrderCandidateSampleRates(info).ToList();

        Assert.True(rates.IndexOf(88_200) < rates.IndexOf(192_000));
        Assert.True(rates.IndexOf(44_100) < rates.IndexOf(192_000));
    }

    /// <summary>DSD128 still prefers the divisions of its own rate.</summary>
    [Fact]
    public void OrderCandidateSampleRates_HandlesDsd128()
    {
        var info = Dsd(sourceSampleRate: 5_644_800, hint: 176_400);

        var rates = WasapiAudioPlayer.OrderCandidateSampleRates(info);

        Assert.Equal(176_400, rates[0]);
    }

    /// <summary>A PCM source keeps the highest rate it can fill first.</summary>
    [Theory]
    [InlineData(44_100, 44_100)]
    [InlineData(96_000, 96_000)]
    [InlineData(192_000, 192_000)]
    public void OrderCandidateSampleRates_PrefersTheHighestFillablePcmRate(int sourceRate, int expectedFirst)
    {
        var info = new AudioFileInfo(
            "flac",
            sourceRate,
            2,
            sourceRate,
            IsDsd: false,
            "flac",
            TimeSpan.FromMinutes(1));

        var rates = WasapiAudioPlayer.OrderCandidateSampleRates(info);

        Assert.Equal(expectedFirst, rates[0]);
    }

    /// <summary>Every offered rate is positive and appears once.</summary>
    [Fact]
    public void OrderCandidateSampleRates_IsDistinctAndPositive()
    {
        var info = Dsd(sourceSampleRate: 352_800, hint: 176_400);

        var rates = WasapiAudioPlayer.OrderCandidateSampleRates(info);

        Assert.All(rates, rate => Assert.True(rate > 0));
        Assert.Equal(rates.Count, rates.Distinct().Count());
    }

    private static AudioFileInfo Dsd(int sourceSampleRate, int hint) =>
        new("dsd_lsbf_planar", sourceSampleRate, 2, hint, IsDsd: true, "dsf", TimeSpan.FromMinutes(1));
}
