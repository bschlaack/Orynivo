using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies the scan decision used by normal and forced metadata refreshes.</summary>
public sealed class LibraryScannerMetadataRefreshTests
{
    /// <summary>Ensures an explicit refresh re-reads a file even when no timestamp or migration refresh requires it.</summary>
    [Fact]
    public void ShouldReadMetadata_ForcedRefreshIncludesUnchangedFile()
    {
        Assert.True(LibraryScanner.ShouldReadMetadata(
            forceMetadataRefresh: true,
            refreshReplayGainMetadata: false,
            refreshArtistAttribution: false,
            metadataChanged: false));
    }

    /// <summary>Ensures a normal incremental scan continues to skip a completely unchanged file.</summary>
    [Fact]
    public void ShouldReadMetadata_NormalScanSkipsUnchangedFile()
    {
        Assert.False(LibraryScanner.ShouldReadMetadata(
            forceMetadataRefresh: false,
            refreshReplayGainMetadata: false,
            refreshArtistAttribution: false,
            metadataChanged: false));
    }
}
