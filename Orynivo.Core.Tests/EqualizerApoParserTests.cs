using Orynivo.Audio;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Checks bounded APO imports against synthetic profiles without audio devices.</summary>
public sealed class EqualizerApoParserTests
{
    /// <summary>AutoEQ graphic points become a baseline gain and ordered shelf differences.</summary>
    [Fact]
    public void GraphicEq_OrdersPointsAndPreservesGainChanges()
    {
        var profile = EqualizerApoParser.Parse("Synthetic", ["GraphicEQ: 1000 -2; 20 -4; 100 -4; 10000 1"]);
        Assert.Equal(-4, profile.PreampDb);
        Assert.Equal(2, profile.Filters.Count);
        Assert.All(profile.Filters, filter => Assert.Equal(EqualizerFilterType.HighShelf, filter.Type));
        Assert.Equal(Math.Sqrt(100 * 1000), profile.Filters[0].Frequency, 8);
        Assert.Equal(2, profile.Filters[0].GainDb);
        Assert.Equal(3, profile.Filters[1].GainDb);
    }

    /// <summary>Filter aliases, disabled rows, comments and decimal commas retain their intended values.</summary>
    [Fact]
    public void Parametric_ParsesAliasesAndComments()
    {
        var profile = EqualizerApoParser.Parse("Synthetic", ["", "# heading", "; heading",
            "Preamp: -3,5 dB", "Filter 1: ON PEQ Fc 1000 Hz Gain 2,5 dB Q 1,2 # comment",
            "Filter 2: OFF PK Fc 200 Hz Gain 9 dB Q 1", "Filter: ON LPQ Fc 18000 Hz Q 0.7"]);
        Assert.Equal(-3.5, profile.PreampDb);
        Assert.Equal(2, profile.Filters.Count);
        Assert.Equal(EqualizerFilterType.Peak, profile.Filters[0].Type);
        Assert.Equal(2.5, profile.Filters[0].GainDb);
        Assert.Equal(1.2, profile.Filters[0].Q);
        Assert.Equal(EqualizerFilterType.LowPass, profile.Filters[1].Type);
    }

    /// <summary>Explicit unity profiles are valid imports rather than missing supported content.</summary>
    /// <param name="line">A supported neutral profile directive.</param>
    [Theory]
    [InlineData("Preamp: 0 dB")]
    [InlineData("GraphicEQ: 20 0; 1000 0; 20000 0")]
    public void NeutralProfile_IsAccepted(string line) =>
        Assert.Empty(EqualizerApoParser.Parse("Synthetic", [line]).Filters);

    /// <summary>Malformed recognized directives cannot hide behind another valid preamp line.</summary>
    /// <param name="line">A malformed or unsupported equalizer directive.</param>
    [Theory]
    [InlineData("Preamp: invalid dB")]
    [InlineData("Filter 1: ON PK Fc invalid Hz Gain 2 dB Q 1")]
    [InlineData("Filter 1: ON PK Fc 1000 Hz Gain 2 dB Q invalid")]
    [InlineData("Filter 1: ON PK Fc 1000 Hz Gain 2 dB BW Oct 1")]
    [InlineData("Filter 1: ON PK Fc -1 Hz Gain 2 dB Q 1")]
    [InlineData("Filter 1: ON PK Fc 1000 Hz Gain 2 dB Q 0")]
    [InlineData("Filter 1: ON UNKNOWN Fc 1000 Hz Gain 2 dB Q 1")]
    [InlineData("GraphicEQ:")]
    [InlineData("GraphicEQ: 20 0; malformed; 100 2")]
    [InlineData("GraphicEQ: -20 1; 100 2")]
    [InlineData("GraphicEQ: 20 NaN; 100 2")]
    [InlineData("GraphicEQ: 20 0 extra; 100 2")]
    public void MalformedDirective_RejectsPartialImport(string line) =>
        Assert.Throws<FormatException>(() => EqualizerApoParser.Parse("Synthetic", ["Preamp: -1 dB", line]));

    /// <summary>Exactly 512 active filters are allowed and the next one is rejected.</summary>
    [Fact]
    public void Filters_EnforceCountBoundary()
    {
        const string line = "Filter: ON PK Fc 1000 Hz Gain 1 dB Q 1";
        Assert.Equal(512, EqualizerApoParser.Parse("Synthetic", Enumerable.Repeat(line, 512)).Filters.Count);
        Assert.Throws<FormatException>(() => EqualizerApoParser.Parse("Synthetic", Enumerable.Repeat(line, 513)));
        var points = string.Join("; ", Enumerable.Range(1, 514).Select(i => $"{i} {i % 2}"));
        Assert.Throws<FormatException>(() => EqualizerApoParser.Parse("Synthetic", ["GraphicEQ: " + points]));
        var allowedPoints = string.Join("; ", Enumerable.Range(1, 513).Select(i => $"{i} {i % 2}"));
        Assert.Equal(512, EqualizerApoParser.Parse("Synthetic", ["GraphicEQ: " + allowedPoints]).Filters.Count);
    }

    /// <summary>Real file admission preserves CRLF syntax and rejects files exceeding four MiB.</summary>
    [Fact]
    public void File_HandlesCrLfAndRejectsOversize()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllText(path, "# synthetic\r\nPreamp: -2 dB\r\nFilter: ON PK Fc 1000 Hz Gain 1 dB Q 1\r\n");
            Assert.Single(EqualizerApoParser.ParseFile(path).Filters);
            File.WriteAllText(path, "Preamp: -1 dB\n#" + new string(' ', 4 * 1024 * 1024 - 15));
            Assert.Equal(4 * 1024 * 1024, new FileInfo(path).Length);
            Assert.Equal(-1, EqualizerApoParser.ParseFile(path).PreampDb);
            using (var file = File.OpenWrite(path)) file.SetLength(4 * 1024 * 1024 + 1);
            Assert.Throws<FormatException>(() => EqualizerApoParser.ParseFile(path));
        }
        finally { File.Delete(path); }
    }
}
