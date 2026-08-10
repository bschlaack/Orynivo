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

    /// <summary>Verifies supplemental genres and only sufficiently confirmed positive tags are retained.</summary>
    [Fact]
    public async Task MusicBrainzRatingService_FiltersSupplementalGenresAndTags()
    {
        using var httpClient = new HttpClient(new JsonHandler(
            """{"rating":{"value":4.0,"votes-count":4},"genres":[{"name":"Synth-pop","count":3},{"name":"Rejected","count":0}],"tags":[{"name":"80s","count":5},{"name":"weak","count":1},{"name":"negative","count":-2}]}"""));
        var service = new MusicBrainzRatingService(httpClient);

        var result = await service.GetRatingAsync(
            "c410a773-c6eb-4bc0-9df8-042fe6645c63",
            "Artist",
            "Track",
            180);

        Assert.NotNull(result);
        Assert.Equal(["Synth-pop"], result.Genres);
        Assert.Equal(["80s"], result.Tags);
    }

    /// <summary>Verifies metadata fallback resolves an MBID and then uses its reliable direct rating lookup.</summary>
    [Fact]
    public async Task MusicBrainzRatingService_MetadataFallbackReturnsMbidAndRatingFromSearch()
    {
        const string mbid = "c410a773-c6eb-4bc0-9df8-042fe6645c63";
        var handler = new JsonHandler(
            $$$"""{"recordings":[{"id":"{{{mbid}}}","title":"Track","length":180000,"artist-credit":[{"name":"Artist"}]}]}""",
            """{"rating":{"value":4.4,"votes-count":22}}""");
        using var httpClient = new HttpClient(handler);
        var service = new MusicBrainzRatingService(httpClient);

        var result = await service.GetRatingAsync(null, "Artist", "Track", 180);

        Assert.NotNull(result);
        Assert.Equal(mbid, result.RecordingMbid);
        Assert.Equal(4.4, result.Rating);
        Assert.Equal(22, result.Votes);
        Assert.Equal(2, handler.RequestCount);
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
                    1234567890,
                    MusicBrainzGenreMetadata.Serialize(["Synth-pop"]),
                    MusicBrainzGenreMetadata.Serialize(["80s"]));
                database.Upsert(CreateTrack(trackPath));
            }

            using var reopened = new AudioDatabase(databasePath);
            var persisted = Assert.Single(reopened.GetTrackList());
            Assert.Equal(5, persisted.UserRating);
            Assert.Equal(4.2, persisted.MusicBrainzRating);
            Assert.Equal(37, persisted.MusicBrainzRatingVotes);
            Assert.Equal("c410a773-c6eb-4bc0-9df8-042fe6645c63", persisted.MusicBrainzTrackId);
            Assert.Equal(1234567890, persisted.MusicBrainzRatingFetchedAt);
            Assert.Equal("Synth-pop", Assert.Single(System.Text.Json.JsonSerializer.Deserialize<List<string>>(persisted.MusicBrainzGenres!)!));
            Assert.Contains("Synth-pop", Assert.Single(reopened.GetTrackFacets()).Genre);
            Assert.Contains("80s", Assert.Single(reopened.GetTrackFacets()).Genre);
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

    private sealed class JsonHandler(params string[] jsonResponses) : HttpMessageHandler
    {
        private readonly Queue<string> _jsonResponses = new(jsonResponses);

        public int RequestCount { get; private set; }

        public string LastRequestUri { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri?.ToString() ?? string.Empty;
            var json = _jsonResponses.Count > 1
                ? _jsonResponses.Dequeue()
                : _jsonResponses.Peek();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
