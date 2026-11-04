using Orynivo.Audio;
using Orynivo.Remote;
using Xunit;

namespace Orynivo.Tests;

/// <summary>Checks search relevance and validated streaming choices without hardware or a library.</summary>
public sealed class MobileMusicDeliveryTests
{
    /// <summary>Rejects inner-word noise and missing query terms, and promotes exact titles across sources.</summary>
    [Fact]
    public void SearchRequiresEveryLiteralWordAndRanksExactTitlesFirst()
    {
        var exact = new MobileRemoteTrack("server:one:1", "Take on Me", "A-ha", "Hunting High and Low", 1985, "Server");
        var extra = exact with { Id = "local:2", Title = "Take on Me Live" };
        Assert.True(MobileSearchRanking.Score("Take on Me", exact) > MobileSearchRanking.Score("Take on Me", extra));
        Assert.True(MobileSearchRanking.Score("A-ha Take on Me", exact) > 0);
        Assert.Equal(0, MobileSearchRanking.Score("A-ha missing", exact));
        Assert.Equal(0, MobileSearchRanking.Score("ha", exact with { Artist = "Shadows" }));
        Assert.True(MobileSearchRanking.Score("Beyonce", exact with { Artist = "Beyoncé" }) > 0);
        Assert.True(MobileSearchRanking.Score("Take on Me", exact) == MobileSearchRanking.Score("Take on Me", exact with { Id = "local:1", Source = "Local" }));
    }

    /// <summary>All encoder choices retain seek bounds, avoid realtime throttling and use whitelisted codecs.</summary>
    [Fact]
    public void EncodingChoicesPreserveSeekAndNeverThrottleToRealtime()
    {
        Assert.Null(BrowserAudioEncoding.Find("-i private-source"));
        Assert.Equal(12, BrowserAudioEncoding.All.Count);
        foreach (var choice in BrowserAudioEncoding.All.Where(c => c.Id is not ("original" or "dsd-pcm")))
        {
            var args = BrowserAudioTranscoder.CreateStartInfo(new("cue://test", 1, "synthetic.dsf",
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)), TimeSpan.FromSeconds(3), 1, choice).ArgumentList.ToArray();
            Assert.DoesNotContain("-re", args);
            Assert.DoesNotContain("-readrate", args);
            Assert.Equal("13", args[Array.IndexOf(args, "-ss") + 1]);
            Assert.True(Array.IndexOf(args, "-ss") < Array.IndexOf(args, "-i"));
            Assert.Equal("17", args[Array.IndexOf(args, "-t") + 1]);
            Assert.Contains(choice.Kbps == 0 ? "flac" : "libmp3lame", args);
            Assert.Equal(choice.Kbps == 0 ? "audio/flac" : "audio/mpeg", choice.Mime);
        }
    }

    /// <summary>Original delivery must not silently become lossy when the browser rejects its codec.</summary>
    /// <returns>The observed faulted player lifetime.</returns>
    [Fact]
    public async Task OriginalDeliveryDoesNotSilentlyChangeFormats()
    {
        using var player = new BrowserAudioPlayer("fixture", new("synthetic.dsf", 1), TimeSpan.FromSeconds(1),
            direct: true, directMime: "audio/x-dsf", allowFallback: false);
        var state = player.Snapshot();
        Assert.Equal("DSD", player.OutputFormat);
        Assert.True(player.Report(new("fixture", state.StreamId!, state.ControlRevision, 0, false, Failed: true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => player.WaitForCompletionAsync());
        Assert.True(player.Snapshot().Direct);
    }
}
