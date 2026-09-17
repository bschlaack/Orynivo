using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the shared smart-playlist filtering, ordering, and limiting logic
/// used by both the desktop client and the server.
/// </summary>
public sealed class SmartPlaylistCriteriaTests
{
    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static SmartPlaylistTrackInfo Track(
        long id,
        bool favorite = false,
        string? genre = null,
        string? format = null,
        int? bitrate = null,
        int? year = null,
        string? artist = null,
        string? album = null,
        double? duration = null,
        long? addedAt = null,
        int playCount = 0,
        long? lastPlayedAt = null,
        string? sortTitle = null,
        string sourceKey = "local")
        => new(
            id, favorite, genre, format, bitrate, year, artist, album, duration,
            addedAt ?? Now, playCount, lastPlayedAt, sortTitle ?? $"Track {id}", sourceKey);

    /// <summary>Favorites-only keeps only favorite tracks.</summary>
    [Fact]
    public void Resolve_FiltersByFavorites()
    {
        var result = new SmartPlaylistCriteria { FavoritesOnly = true }
            .Resolve([Track(1, favorite: true), Track(2)]);

        Assert.Equal([1L], result.Select(track => track.Id));
    }

    /// <summary>Genre matching is case-insensitive and any listed genre matches.</summary>
    [Fact]
    public void Resolve_FiltersByGenre()
    {
        var result = new SmartPlaylistCriteria { Genres = ["rock"] }
            .Resolve([Track(1, genre: "Rock; Pop"), Track(2, genre: "Jazz")]);

        Assert.Equal([1L], result.Select(track => track.Id));
    }

    /// <summary>Format and bitrate filters are applied together.</summary>
    [Fact]
    public void Resolve_FiltersByFormatAndBitrate()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Track(1, format: "FLAC", bitrate: 1000),
            Track(2, format: "MP3", bitrate: 320)
        };

        Assert.Equal([1L], new SmartPlaylistCriteria { Formats = ["flac"] }
            .Resolve(candidates).Select(track => track.Id));
        Assert.Equal([2L], new SmartPlaylistCriteria { Bitrates = [320] }
            .Resolve(candidates).Select(track => track.Id));
    }

    /// <summary>Source-key filtering is case-insensitive.</summary>
    [Fact]
    public void Resolve_FiltersBySourceKey()
    {
        var result = new SmartPlaylistCriteria { SourceKeys = ["SERVER:S1"] }
            .Resolve([Track(1), Track(2, sourceKey: "server:s1")]);

        Assert.Equal([2L], result.Select(track => track.Id));
    }

    /// <summary>Free text matches the sort title, artist, or album.</summary>
    [Fact]
    public void Resolve_FiltersBySearchText()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Track(1, sortTitle: "Hello"),
            Track(2, artist: "Hello Band"),
            Track(3, album: "Hello Album"),
            Track(4, sortTitle: "Unrelated")
        };

        var ids = new SmartPlaylistCriteria { SearchText = "hello" }
            .Resolve(candidates).Select(track => track.Id).OrderBy(id => id);

        Assert.Equal([1L, 2L, 3L], ids);
    }

    /// <summary>Inclusive year and duration ranges filter correctly.</summary>
    [Fact]
    public void Resolve_FiltersByYearAndDurationRanges()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Track(1, year: 1990, duration: 100),
            Track(2, year: 2000, duration: 200),
            Track(3, year: 2010, duration: 300)
        };

        Assert.Equal([2L], new SmartPlaylistCriteria { MinimumYear = 1995, MaximumYear = 2005 }
            .Resolve(candidates).Select(track => track.Id));
        Assert.Equal([2L], new SmartPlaylistCriteria { MinimumDurationSeconds = 150, MaximumDurationSeconds = 250 }
            .Resolve(candidates).Select(track => track.Id));
    }

    /// <summary>Never-played and play-count ranges filter correctly.</summary>
    [Fact]
    public void Resolve_FiltersByPlayHistory()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Track(1, playCount: 0),
            Track(2, playCount: 5),
            Track(3, playCount: 10)
        };

        Assert.Equal([1L], new SmartPlaylistCriteria { NeverPlayed = true }
            .Resolve(candidates).Select(track => track.Id));
        Assert.Equal([2L], new SmartPlaylistCriteria { MinimumPlayCount = 3, MaximumPlayCount = 7 }
            .Resolve(candidates).Select(track => track.Id));
    }

    /// <summary>Recently added and recently played windows filter correctly.</summary>
    [Fact]
    public void Resolve_FiltersByRecentWindows()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Track(1, addedAt: Now, lastPlayedAt: Now),
            Track(2, addedAt: Now - 10L * 86400, lastPlayedAt: Now - 10L * 86400),
            Track(3, addedAt: Now - 10L * 86400)
        };

        Assert.Equal([1L], new SmartPlaylistCriteria { AddedWithinDays = 7 }
            .Resolve(candidates).Select(track => track.Id));
        Assert.Equal([1L], new SmartPlaylistCriteria { PlayedWithinDays = 7 }
            .Resolve(candidates).Select(track => track.Id));
    }

    /// <summary>Artist and album substring filters are case-insensitive.</summary>
    [Fact]
    public void Resolve_FiltersByArtistAndAlbumContains()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Track(1, artist: "The Cure", album: "Disintegration"),
            Track(2, artist: "Other", album: "Different")
        };

        Assert.Equal([1L], new SmartPlaylistCriteria { ArtistContains = "cure" }
            .Resolve(candidates).Select(track => track.Id));
        Assert.Equal([1L], new SmartPlaylistCriteria { AlbumContains = "disint" }
            .Resolve(candidates).Select(track => track.Id));
    }

    /// <summary>The default ordering is alphabetical by sort title.</summary>
    [Fact]
    public void Resolve_OrdersByTitleByDefault()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Track(1, sortTitle: "Charlie"),
            Track(2, sortTitle: "alpha"),
            Track(3, sortTitle: "Bravo")
        };

        var result = new SmartPlaylistCriteria().Resolve(candidates);

        Assert.Equal([2L, 3L, 1L], result.Select(track => track.Id));
    }

    /// <summary>Most-recently-played ordering puts never-played tracks last.</summary>
    [Fact]
    public void Resolve_OrdersLastPlayedNewest()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Track(1),
            Track(2, lastPlayedAt: Now - 100),
            Track(3, lastPlayedAt: Now)
        };

        var result = new SmartPlaylistCriteria { SortOrder = SmartPlaylistSortOrder.LastPlayedNewest }
            .Resolve(candidates);

        Assert.Equal([3L, 2L, 1L], result.Select(track => track.Id));
    }

    /// <summary>Least-recently-played ordering puts never-played tracks first.</summary>
    [Fact]
    public void Resolve_OrdersLeastRecentlyPlayed()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Track(1),
            Track(2, lastPlayedAt: Now - 100),
            Track(3, lastPlayedAt: Now)
        };

        var result = new SmartPlaylistCriteria { SortOrder = SmartPlaylistSortOrder.LeastRecentlyPlayed }
            .Resolve(candidates);

        Assert.Equal([1L, 2L, 3L], result.Select(track => track.Id));
    }

    /// <summary>The result limit truncates the ordered result.</summary>
    [Fact]
    public void Resolve_AppliesResultLimit()
    {
        var candidates = new List<SmartPlaylistTrackInfo>
        {
            Track(1, sortTitle: "A"),
            Track(2, sortTitle: "B"),
            Track(3, sortTitle: "C")
        };

        var result = new SmartPlaylistCriteria { ResultLimit = 2 }.Resolve(candidates);

        Assert.Equal([1L, 2L], result.Select(track => track.Id));
    }
}
