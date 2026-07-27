using Orynivo.Library;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies album-centered artist attribution and stable MusicBrainz identity matching.</summary>
public sealed class ArtistAttributionTests : IDisposable
{
    private static readonly string TestDataRoot =
        Path.Combine(Path.GetTempPath(), $"orynivo-artist-tests-{Guid.NewGuid():N}");

    static ArtistAttributionTests()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirEnvironmentVariable, TestDataRoot);
    }

    /// <summary>Initializes a clean isolated library database for one test.</summary>
    public ArtistAttributionTests()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(Path.Combine(TestDataRoot, "library.db")))
            File.Delete(Path.Combine(TestDataRoot, "library.db"));
    }

    /// <summary>Verifies that an untagged compilation is represented by one album artist.</summary>
    [Fact]
    public void ReconcileAlbumArtists_GroupsUntaggedCompilationUnderVariousArtists()
    {
        using var database = AudioDatabase.OpenDefault();
        database.Upsert(CreateTrack("one.flac", "Guest One", albumArtist: null, compilation: true));
        database.Upsert(CreateTrack("two.flac", "Guest Two", albumArtist: null, compilation: true));

        database.ReconcileAlbumArtists();

        var artists = database.GetArtistsLite();
        Assert.Contains(artists, artist => artist.Artist == "Various Artists");
        Assert.DoesNotContain(artists, artist => artist.Artist == "Guest One");
        Assert.DoesNotContain(artists, artist => artist.Artist == "Guest Two");
        Assert.Equal("Guest One", database.GetByPath(TrackPath("one.flac"))!.Artist);
        Assert.Equal("Various Artists", database.GetByPath(TrackPath("one.flac"))!.AlbumArtist);
    }

    /// <summary>Verifies that explicit album artists win over featured track credits.</summary>
    [Fact]
    public void ReconcileAlbumArtists_PreservesExplicitAlbumArtistAndRemovesFeaturedSuffix()
    {
        using var database = AudioDatabase.OpenDefault();
        database.Upsert(CreateTrack("feature.flac", "Main Artist feat. Guest", "Main Artist"));

        database.ReconcileAlbumArtists();

        var track = database.GetByPath(TrackPath("feature.flac"));
        Assert.Equal("Main Artist", track!.Artist);
        Assert.Equal("Main Artist", track.AlbumArtist);
        Assert.Single(database.GetArtistsLite(), artist => artist.Artist == "Main Artist");
    }

    /// <summary>Verifies that a shared MusicBrainz ID unifies differing artist spellings.</summary>
    [Fact]
    public void Upsert_UsesMusicBrainzArtistIdAcrossNameVariants()
    {
        const string artistId = "11111111-2222-3333-4444-555555555555";
        using var database = AudioDatabase.OpenDefault();
        database.Upsert(CreateTrack("first.flac", "Canonical Name", "Canonical Name", artistId));
        database.Upsert(CreateTrack("second.flac", "Alternate Spelling", "Alternate Spelling", artistId, "Other Album"));

        database.ReconcileAlbumArtists();

        var artists = database.GetArtistsLite();
        Assert.Single(artists);
        Assert.Equal("Canonical Name", artists[0].Artist);
        Assert.Equal("Canonical Name", database.GetByPath(TrackPath("second.flac"))!.Artist);
    }

    /// <summary>Completes the test fixture lifetime.</summary>
    public void Dispose()
    {
    }

    private static TrackRecord CreateTrack(
        string fileName,
        string artist,
        string? albumArtist,
        string? musicBrainzArtistId = null,
        string album = "Compilation",
        bool compilation = false) =>
        new()
        {
            Path = TrackPath(fileName),
            SourcePath = TrackPath(fileName),
            FileName = fileName,
            ModifiedAt = 1,
            AddedAt = 1,
            Title = Path.GetFileNameWithoutExtension(fileName),
            Artist = artist,
            AlbumArtist = albumArtist,
            AlbumArtistInferred = string.IsNullOrWhiteSpace(albumArtist),
            Album = album,
            Compilation = compilation,
            MusicBrainzArtistId = musicBrainzArtistId
        };

    private static string TrackPath(string fileName) =>
        Path.Combine(TestDataRoot, "Music", "Album", fileName);
}
