using Microsoft.Data.Sqlite;
using Orynivo.Library;
using System.Net;
using System.Text;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies durable personal and MusicBrainz track ratings.</summary>
public sealed class TrackRatingTests
{
    /// <summary>Verifies MusicBrainz community rating and vote-count parsing for a known recording MBID.</summary>
    [Fact]
    public async Task MusicBrainzRatingService_KnownMbidReadsCommunityRating()
    {
        using var httpClient = new HttpClient(new JsonHandler(
            """{"rating":{"value":4.35,"votes-count":42}}"""));
        var service = new MusicBrainzRatingService(httpClient);

        var result = await service.GetRatingAsync(
            "c410a773-c6eb-4bc0-9df8-042fe6645c63",
            "Artist",
            "Track",
            180);

        Assert.NotNull(result);
        Assert.Equal(4.35, result.Rating);
        Assert.Equal(42, result.Votes);
    }

    /// <summary>Verifies known recording MBIDs are retrieved through one bounded batch request.</summary>
    [Fact]
    public async Task MusicBrainzRatingService_KnownMbidsUseOneBatchRequest()
    {
        const string first = "c410a773-c6eb-4bc0-9df8-042fe6645c63";
        const string second = "b1a9c0e9-d987-4042-ae91-78d6a3267d69";
        var handler = new JsonHandler(
            $$$"""{"recordings":[{"id":"{{{first}}}","title":"First","artist-credit":[],"rating":{"value":4.1,"votes-count":12}},{"id":"{{{second}}}","title":"Second","artist-credit":[],"rating":{"value":3.8,"votes-count":9}}]}""");
        using var httpClient = new HttpClient(handler);
        var service = new MusicBrainzRatingService(httpClient);

        var ratings = await service.GetRatingsAsync([first, second]);

        Assert.Equal(2, ratings.Count);
        Assert.Equal(4.1, ratings[first].Rating);
        Assert.Equal(9, ratings[second].Votes);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains(first, handler.LastRequestUri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(second, handler.LastRequestUri, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies metadata fallback returns and preserves the resolved MBID without a second lookup.</summary>
    [Fact]
    public async Task MusicBrainzRatingService_MetadataFallbackReturnsMbidAndRatingFromSearch()
    {
        const string mbid = "c410a773-c6eb-4bc0-9df8-042fe6645c63";
        var handler = new JsonHandler(
            $$$"""{"recordings":[{"id":"{{{mbid}}}","title":"Track","length":180000,"artist-credit":[{"name":"Artist"}],"rating":{"value":4.4,"votes-count":22}}]}""");
        using var httpClient = new HttpClient(handler);
        var service = new MusicBrainzRatingService(httpClient);

        var result = await service.GetRatingAsync(null, "Artist", "Track", 180);

        Assert.NotNull(result);
        Assert.Equal(mbid, result.RecordingMbid);
        Assert.Equal(4.4, result.Rating);
        Assert.Equal(22, result.Votes);
        Assert.Equal(1, handler.RequestCount);
    }

    /// <summary>Ensures scanner upserts preserve user-entered ratings and resolved recording identities.</summary>
    [Fact]
    public void Upsert_PreservesPersonalAndResolvedMusicBrainzRatings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-rating-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        var trackPath = Path.Combine(root, "track.flac");
        try
        {
            using (var database = new AudioDatabase(databasePath))
            {
                database.Upsert(CreateTrack(trackPath));
                var track = Assert.Single(database.GetTrackList());
                database.SetTrackUserRating(track.Id, 5);
                database.SetTrackMusicBrainzRating(
                    track.Id,
                    "c410a773-c6eb-4bc0-9df8-042fe6645c63",
                    4.2,
                    37,
                    1234567890);
                database.Upsert(CreateTrack(trackPath));
            }

            using var reopened = new AudioDatabase(databasePath);
            var persisted = Assert.Single(reopened.GetTrackList());
            Assert.Equal(5, persisted.UserRating);
            Assert.Equal(4.2, persisted.MusicBrainzRating);
            Assert.Equal(37, persisted.MusicBrainzRatingVotes);
            Assert.Equal("c410a773-c6eb-4bc0-9df8-042fe6645c63", persisted.MusicBrainzTrackId);
            Assert.Equal(1234567890, persisted.MusicBrainzRatingFetchedAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static TrackRecord CreateTrack(string path) => new()
    {
        Path = path,
        SourcePath = path,
        FileName = Path.GetFileName(path),
        ModifiedAt = 1,
        AddedAt = 1,
        Title = "Track",
        Artist = "Artist",
        AlbumArtist = "Artist",
        Album = "Album"
    };

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public string LastRequestUri { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri?.ToString() ?? string.Empty;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
