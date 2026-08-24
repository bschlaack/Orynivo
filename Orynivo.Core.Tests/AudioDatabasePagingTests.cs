using Microsoft.Data.Sqlite;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies SQL-backed paging of compact track rows.</summary>
public sealed class AudioDatabasePagingTests
{
    /// <summary>Ensures paging is ordered, bounded, and does not repeat adjacent rows.</summary>
    [Fact]
    public void GetTrackListPage_ReturnsRequestedOrderedSlice()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-paging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var database = new AudioDatabase(Path.Combine(root, "library.db"));
            foreach (var title in new[] { "Delta", "Alpha", "Charlie", "Bravo", "Echo" })
            {
                var path = Path.Combine(root, $"{title}.flac");
                database.Upsert(new TrackRecord
                {
                    Path = path,
                    SourcePath = path,
                    FileName = Path.GetFileName(path),
                    ModifiedAt = 1,
                    AddedAt = 1,
                    Title = title,
                    Artist = "Artist",
                    AlbumArtist = "Artist",
                    Album = "Album"
                });
            }

            Assert.Equal(["Alpha", "Bravo"], database.GetTrackListPage(0, 2).Select(track => track.Title));
            Assert.Equal(["Charlie", "Delta"], database.GetTrackListPage(1, 2).Select(track => track.Title));
            Assert.Equal(["Echo"], database.GetTrackListPage(2, 2).Select(track => track.Title));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
