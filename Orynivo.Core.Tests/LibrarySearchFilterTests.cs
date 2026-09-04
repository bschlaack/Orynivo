using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies structured temporal and release-year library filtering.</summary>
public sealed class LibrarySearchFilterTests
{
    /// <summary>Verifies inclusive year filtering and newest-addition ordering.</summary>
    [Fact]
    public void Apply_FiltersYearAndSortsNewestAdditionFirst()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Create(1, 1998, 100),
            Create(2, 1997, 300),
            Create(3, 1998, 200)
        };

        var result = LibrarySearchFilter.Apply(candidates, new LibrarySearchFilterOptions(
            MinimumYear: 1998,
            MaximumYear: 1998,
            SortOrder: LibrarySearchSortOrder.AddedNewest));

        Assert.Equal([3L, 1L], result.Select(static track => track.Id));
    }

    /// <summary>Verifies inclusive lower and exclusive upper added-date boundaries.</summary>
    [Fact]
    public void Apply_UsesHalfOpenAddedDateRange()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Create(1, 2000, 99),
            Create(2, 2000, 100),
            Create(3, 2000, 199),
            Create(4, 2000, 200)
        };

        var result = LibrarySearchFilter.Apply(candidates, new LibrarySearchFilterOptions(
            AddedFrom: 100,
            AddedBefore: 200));

        Assert.Equal([2L, 3L], result.Select(static track => track.Id));
    }

    private static SmartPlaylistTrackInfo Create(long id, int year, long addedAt) => new(
        id, false, null, "flac", null, year, "Artist", "Album", 180, addedAt,
        0, null, $"Track {id}", AlbumId: 1);
}
