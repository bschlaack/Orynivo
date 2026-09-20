using Microsoft.Data.Sqlite;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the calendar-year aggregation behind the year-in-review export,
/// including that a year never picks up listening time from another year.
/// </summary>
public sealed class YearInReviewTests
{
    /// <summary>Aggregates totals, active days, months, and the leading lists for one year.</summary>
    [Fact]
    public void GetYearInReview_AggregatesOneCalendarYear()
    {
        RunWithDatabase((database, databasePath) =>
        {
            var track = database.GetTrackList().Single();
            InsertHistory(databasePath, track.Id, "Album A", "Artist A", "Rock", 600, LocalTime(2024, 3, 15, 12));
            InsertHistory(databasePath, track.Id, "Album A", "Artist A", "Rock", 300, LocalTime(2024, 3, 15, 18));
            InsertHistory(databasePath, track.Id, "Album B", "Artist B", "Jazz", 900, LocalTime(2024, 7, 2, 9));
            InsertHistory(databasePath, track.Id, "Album A", "Artist A", "Rock", 5000, LocalTime(2025, 1, 5, 9));

            var summary = database.GetYearInReview(2024);

            Assert.NotNull(summary);
            Assert.Equal(2024, summary.Year);
            Assert.Equal(1800d, summary.TotalListeningSeconds);
            Assert.Equal(2, summary.ActiveDays);
            Assert.Equal(12, summary.MonthlySeconds.Count);
            Assert.Equal(900d, summary.MonthlySeconds[2]);
            Assert.Equal(900d, summary.MonthlySeconds[6]);
            Assert.Equal(0d, summary.MonthlySeconds[0]);
            Assert.Equal("Rock", summary.TopGenres[0].Genre);
            Assert.Equal(1800d, summary.TopGenres[0].Seconds);
            Assert.Equal("Album A", summary.TopAlbums[0].Title);
            Assert.Equal(1800d, summary.TopAlbums[0].Seconds);
            Assert.Equal("Artist A", summary.TopArtists[0].Name);
            Assert.Equal(1800d, summary.TopArtists[0].Seconds);
        });
    }

    /// <summary>The leading lists are scoped to the requested year.</summary>
    [Fact]
    public void GetYearInReview_ExcludesOtherYears()
    {
        RunWithDatabase((database, databasePath) =>
        {
            var track = database.GetTrackList().Single();
            InsertHistory(databasePath, null, "Old", "Old Artist", "Rock", 10_000, LocalTime(2023, 6, 1, 9));
            InsertHistory(databasePath, null, "New", "New Artist", "Jazz", 120, LocalTime(2024, 6, 1, 9));

            // Both rows are written through a separate connection, so prove first that the
            // AudioDatabase connection sees them. Without this precondition an invisible
            // insert looks exactly like a year-filter bug, which is what made an earlier
            // failure of this test hard to attribute.
            Assert.Equal(2, database.GetRecentHistory().Count);

            var summary = database.GetYearInReview(2024);

            Assert.NotNull(summary);
            Assert.True(
                summary.TotalListeningSeconds == 120d,
                $"Expected 120 s for 2024, got {summary.TotalListeningSeconds} s "
                + $"(active profile '{AudioDatabase.ActiveProfileId}', "
                + $"{database.GetRecentHistory().Count} history rows visible).");
            Assert.Equal("Jazz", summary.TopGenres[0].Genre);
            Assert.Equal("New", summary.TopAlbums[0].Title);
            Assert.Equal("New Artist", summary.TopArtists[0].Name);
        });
    }

    /// <summary>An unsupported year returns no summary.</summary>
    [Fact]
    public void GetYearInReview_RejectsUnsupportedYears()
    {
        RunWithDatabase((database, _) =>
        {
            Assert.Null(database.GetYearInReview(1500));
            Assert.Null(database.GetYearInReview(10_000));
        });
    }

    private static long LocalTime(int year, int month, int day, int hour) =>
        new DateTimeOffset(new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Local)).ToUnixTimeSeconds();

    private static void InsertHistory(
        string databasePath,
        long? trackId,
        string album,
        string artist,
        string genre,
        double seconds,
        long startedAt)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO play_history (
                profile_id, track_id, path, started_at, position_seconds,
                completed, media_type, title, subtitle, album, genre)
            VALUES (
                'standard', $trackId, $path, $startedAt, $seconds,
                1, 'track', $title, $artist, $album, $genre);
            """;
        command.Parameters.AddWithValue("$trackId", (object?)trackId ?? DBNull.Value);
        command.Parameters.AddWithValue("$path", $"/music/{album}.flac");
        command.Parameters.AddWithValue("$startedAt", startedAt);
        command.Parameters.AddWithValue("$seconds", seconds);
        command.Parameters.AddWithValue("$title", album);
        command.Parameters.AddWithValue("$artist", artist);
        command.Parameters.AddWithValue("$album", album);
        command.Parameters.AddWithValue("$genre", genre);
        command.ExecuteNonQuery();
    }

    private static void RunWithDatabase(Action<AudioDatabase, string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-year-review-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        // The year summary filters by AudioDatabase.ActiveProfileId, which is
        // process-wide AsyncLocal state. Pin it for the test and restore whatever the
        // surrounding context had, so a leaked profile from another test can never turn
        // this into a silent "zero listening time" result.
        var previousProfile = AudioDatabase.ActiveProfileId;
        try
        {
            AudioDatabase.SetActiveProfile("standard");
            using var database = new AudioDatabase(databasePath);
            var path = Path.Combine(root, "track.flac");
            database.Upsert(new TrackRecord
            {
                Path = path,
                SourcePath = path,
                FileName = "track.flac",
                ModifiedAt = 1,
                AddedAt = 1,
                Title = "Track",
                Artist = "Artist A",
                AlbumArtist = "Artist A",
                Album = "Album A",
                Genre = "Rock"
            });
            action(database, databasePath);
        }
        finally
        {
            AudioDatabase.SetActiveProfile(previousProfile);
            CoreTestDatabase.ClearPool(databasePath);
            Directory.Delete(root, recursive: true);
        }
    }
}
