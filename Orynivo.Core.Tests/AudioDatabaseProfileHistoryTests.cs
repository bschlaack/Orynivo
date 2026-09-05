using Microsoft.Data.Sqlite;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies that playback history is isolated between user profiles.</summary>
public sealed class AudioDatabaseProfileHistoryTests
{
    /// <summary>Only entries recorded for the active profile are returned by recent history queries.</summary>
    [Fact]
    public void RecentHistory_IsolatedPerProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-profile-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        try
        {
            using var database = new AudioDatabase(databasePath);

            AudioDatabase.SetActiveProfile("profile-a");
            var first = database.RecordPlaybackStart("a.flac", null, 120, title: "A", subtitle: "Artist A");
            database.RecordPlaybackEnd(first, 120, completed: true);

            AudioDatabase.SetActiveProfile("profile-b");
            var second = database.RecordPlaybackStart("b.flac", null, 120, title: "B", subtitle: "Artist B");
            database.RecordPlaybackEnd(second, 120, completed: true);

            Assert.Equal("B", Assert.Single(database.GetRecentHistory()).Title);

            AudioDatabase.SetActiveProfile("profile-a");
            Assert.Equal("A", Assert.Single(database.GetRecentHistory()).Title);
        }
        finally
        {
            AudioDatabase.SetActiveProfile("standard");
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

}
