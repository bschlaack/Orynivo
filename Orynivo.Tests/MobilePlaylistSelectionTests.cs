using Orynivo.Remote;
using Xunit;

namespace Orynivo.Tests;

/// <summary>Checks source-aware playlist starts without file access or playback.</summary>
public sealed class MobilePlaylistSelectionTests
{
    /// <summary>Keeps later occurrences and preserves provider order when starting on a duplicate.</summary>
    [Fact]
    public void SelectedOccurrenceKeepsItsOrderedSuffix()
    {
        var rows = new[] { "local:1", "server:one:1", "local:1", "unavailable", "local:2", "local:1" };
        var suffix = MobilePlaylistSelection.From(rows, 2, "local:1", x => x, x => x != "unavailable");
        Assert.Equal(new[] { "local:1", "local:2", "local:1" }, suffix.Where(x => x != "unavailable"));
        Assert.Empty(MobilePlaylistSelection.From(rows, 1, "local:1", x => x, _ => true));
        Assert.Empty(MobilePlaylistSelection.From(rows, 3, "unavailable", x => x, x => x != "unavailable"));
        Assert.Empty(MobilePlaylistSelection.From(rows, 9, "local:1", x => x, _ => true));
    }
}
