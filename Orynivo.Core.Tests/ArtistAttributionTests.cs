using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies album-centered artist attribution and stable MusicBrainz identity matching.</summary>
public sealed class ArtistAttributionTests
{
    /// <summary>Verifies that an untagged compilation is represented by one album artist.</summary>
    [Fact]
    public void ReconcileAlbumArtists_GroupsUntaggedCompilationUnderVariousArtists()
    {
        using var test = CoreTestDatabase.Create("orynivo-artist-attribution");
        using var database = test.Open();
        database.Upsert(CreateTrack(test, "one.flac", "Guest One", albumArtist: null, compilation: true));
        database.Upsert(CreateTrack(test, "two.flac", "Guest Two", albumArtist: null, compilation: true));

        database.ReconcileAlbumArtists();

        var artists = database.GetArtistsLite();
        Assert.Contains(artists, artist => artist.Artist == "Various Artists");
        Assert.DoesNotContain(artists, artist => artist.Artist == "Guest One");
        Assert.DoesNotContain(artists, artist => artist.Artist == "Guest Two");
        Assert.Equal("Guest One", database.GetByPath(test.PathOf("Music", "Album", "one.flac"))!.Artist);
        Assert.Equal("Various Artists", database.GetByPath(test.PathOf("Music", "Album", "one.flac"))!.AlbumArtist);
    }

    /// <summary>Verifies that explicit album artists win over featured track credits.</summary>
    [Fact]
    public void ReconcileAlbumArtists_PreservesExplicitAlbumArtistAndRemovesFeaturedSuffix()
    {
        using var test = CoreTestDatabase.Create("orynivo-artist-attribution");
        using var database = test.Open();
        database.Upsert(CreateTrack(test, "feature.flac", "Main Artist feat. Guest", "Main Artist"));

        database.ReconcileAlbumArtists();

        var track = database.GetByPath(test.PathOf("Music", "Album", "feature.flac"));
        Assert.Equal("Main Artist", track!.Artist);
        Assert.Equal("Main Artist", track.AlbumArtist);
        Assert.Single(database.GetArtistsLite(), artist => artist.Artist == "Main Artist");
    }

    /// <summary>Verifies that a shared MusicBrainz ID unifies differing artist spellings.</summary>
    [Fact]
    public void Upsert_UsesMusicBrainzArtistIdAcrossNameVariants()
    {
        const string artistId = "11111111-2222-3333-4444-555555555555";
        using var test = CoreTestDatabase.Create("orynivo-artist-attribution");
        using var database = test.Open();
        database.Upsert(CreateTrack(test, "first.flac", "Canonical Name", "Canonical Name", artistId));
        database.Upsert(CreateTrack(test, "second.flac", "Alternate Spelling", "Alternate Spelling", artistId, "Other Album"));

        database.ReconcileAlbumArtists();

        var artists = database.GetArtistsLite();
        Assert.Single(artists);
        Assert.Equal("Canonical Name", artists[0].Artist);
        Assert.Equal(
            "Canonical Name",
            database.GetByPath(test.PathOf("Music", "Album", "second.flac"))!.Artist);
    }

    private static TrackRecord CreateTrack(
        CoreTestDatabase test,
        string fileName,
        string artist,
        string? albumArtist,
        string? musicBrainzArtistId = null,
        string album = "Compilation",
        bool compilation = false) =>
        new()
        {
            Path = test.PathOf("Music", "Album", fileName),
            SourcePath = test.PathOf("Music", "Album", fileName),
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
}
