using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies physical-folder problem detection and persistent library-only corrections.</summary>
public sealed class LibraryMetadataRepairServiceTests
{
    /// <summary>The quick review neither probes missing sources nor labels them as missing files.</summary>
    [Fact]
    public void Analyze_QuickReviewSkipsPhysicalChecksAndReportsProgress()
    {
        var track = new MetadataRepairTrack(1, "missing.flac", Path.Combine(Path.GetTempPath(), "absent", "missing.flac"),
            "Title", "Artist", "Album", "Artist", 180, 1, 1);
        var progress = new CapturedProgress();
        var row = Assert.Single(LibraryMetadataRepairService.Analyze([track], inspectFiles: false, progress: progress));
        Assert.Equal(0, row.MissingSourceFileCount);
        Assert.Contains(row.Findings, finding => finding.Code == "musicbrainz-id");
        Assert.Equal(new MetadataReviewProgress("folders", 1, 1), progress.Values.Last());
        Assert.DoesNotContain(progress.Values, value => value.Phase == "hashes");
    }

    /// <summary>Search and correction preserve disc-first ordering and refuse partial mappings.</summary>
    [Fact]
    public void CreateOverrides_PreservesSharedDiscOrderAndRejectsPartialMatch()
    {
        var tracks = new[]
        {
            new MetadataRepairTrack(3, "c", "c.flac", "C", "A", "B", "A", 180, 1, 2),
            new MetadataRepairTrack(2, "b", "b.flac", "B", "A", "B", "A", 180, 2, 1),
            new MetadataRepairTrack(1, "a", "a.flac", "A", "A", "B", "A", 180, 1, 1)
        };
        var candidate = new MetadataFolderCandidate("folder", tracks, 1, 1, 0, 0, false);
        var match = new MetadataReleaseMatch("release", "Album", "Artist", null, 2000, 1, 1,
            [new(1, "One", "Artist", null, null), new(2, "Two", "Artist", null, null), new(3, "Three", "Artist", null, null)], 1);
        Assert.Equal(new long[] { 1, 2, 3 }, LibraryMetadataRepairService.OrderTracks(tracks).Select(t => t.Id));
        Assert.Equal(new[] { "a", "b", "c" }, LibraryMetadataRepairService.CreateOverrides(candidate, match).Select(t => t.Path));
        Assert.Throws<ArgumentException>(() => LibraryMetadataRepairService.CreateOverrides(candidate, match with { Tracks = match.Tracks.Take(2).ToList() }));
    }

    /// <summary>Captures synchronous progress for deterministic assertions.</summary>
    private sealed class CapturedProgress : IProgress<MetadataReviewProgress>
    {
        /// <summary>Gets reported phase counters.</summary>
        public List<MetadataReviewProgress> Values { get; } = [];
        /// <summary>Records one progress update.</summary>
        /// <param name="value">Phase counter to retain.</param>
        public void Report(MetadataReviewProgress value) => Values.Add(value);
    }

    /// <summary>Honours cancellation before inspecting a folder.</summary>
    [Fact]
    public void Analyze_HonoursCancellation()
    {
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var track = new MetadataRepairTrack(1, "track.flac", "track.flac", "Track", "Artist",
            "Album", "Artist", 180, 1, 1);

        Assert.Throws<OperationCanceledException>(() =>
            LibraryMetadataRepairService.Analyze([track], cancellationToken: cancellation.Token));
    }

    /// <summary>Reports enrichment gaps even when the basic album metadata is internally consistent.</summary>
    [Fact]
    public void Analyze_DetectsMissingReplayGainAndMusicBrainzIdentity()
    {
        var folder = Path.Combine(Path.GetTempPath(), "orynivo-doctor-enrichment");
        var tracks = new[]
        {
            new MetadataRepairTrack(1, Path.Combine(folder, "one.flac"), typeof(LibraryMetadataRepairServiceTests).Assembly.Location,
                "One", "Artist", "Album", "Artist", 180, 1, 1, null, null),
            new MetadataRepairTrack(2, Path.Combine(folder, "two.flac"), typeof(LibraryMetadataRepairServiceTests).Assembly.Location,
                "Two", "Artist", "Album", "Artist", 200, 2, 1, "-7.2 dB", "recording-id")
        };

        var candidate = Assert.Single(LibraryMetadataRepairService.Analyze(tracks));

        Assert.Equal(1, candidate.MissingReplayGainCount);
        Assert.Equal(1, candidate.MissingMusicBrainzIdCount);
        Assert.Equal(LibraryDoctorSeverity.Warning, candidate.HighestSeverity);
        Assert.Contains(candidate.Findings, finding =>
            finding.Code == "replaygain" &&
            finding.RepairCapability == LibraryDoctorRepairCapability.MaintenanceAction);
    }

    /// <summary>Uses declared per-disc totals to report actual missing positions.</summary>
    [Fact]
    public void Analyze_DetectsProvablyIncompleteDisc()
    {
        var folder = Path.Combine(Path.GetTempPath(), "orynivo-doctor-incomplete");
        var tracks = new[]
        {
            new MetadataRepairTrack(1, Path.Combine(folder, "one.flac"), typeof(LibraryMetadataRepairServiceTests).Assembly.Location,
                "One", "Artist", "Album", "Artist", 180, 1, 1, "-7 dB", "id-1", 3, 1),
            new MetadataRepairTrack(2, Path.Combine(folder, "three.flac"), typeof(LibraryMetadataRepairServiceTests).Assembly.Location,
                "Three", "Artist", "Album", "Artist", 180, 3, 1, "-7 dB", "id-3", 3, 1)
        };

        var candidate = Assert.Single(LibraryMetadataRepairService.Analyze(tracks));

        Assert.Equal(1, candidate.MissingExpectedTrackCount);
        Assert.Contains(candidate.Findings, finding => finding.Code == "incomplete-album");
    }

    /// <summary>Does not report a fully enriched and internally consistent folder.</summary>
    [Fact]
    public void Analyze_IgnoresHealthyEnrichedFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "orynivo-doctor-healthy");
        var tracks = new[]
        {
            new MetadataRepairTrack(1, Path.Combine(folder, "one.flac"), typeof(LibraryMetadataRepairServiceTests).Assembly.Location,
                "One", "Artist", "Album", "Artist", 180, 1, 1, "-7 dB", "id-1", 1, 1, true,
                typeof(LibraryMetadataRepairServiceTests).Assembly.Location)
        };

        Assert.Empty(LibraryMetadataRepairService.Analyze(tracks));
    }

    /// <summary>Checks one shared physical source only once for virtual tracks.</summary>
    [Fact]
    public void Analyze_DeduplicatesMissingPhysicalSources()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"orynivo-doctor-missing-{Guid.NewGuid():N}");
        var source = Path.Combine(folder, "missing.flac");
        var tracks = new[]
        {
            new MetadataRepairTrack(1, $"cue://one", source, "One", "Artist", "Album", "Artist", 180, 1, 1),
            new MetadataRepairTrack(2, $"cue://two", source, "Two", "Artist", "Album", "Artist", 180, 2, 1)
        };

        var candidate = Assert.Single(LibraryMetadataRepairService.Analyze(tracks));

        Assert.Equal(1, candidate.MissingSourceFileCount);
        Assert.Equal(LibraryDoctorSeverity.Error, candidate.HighestSeverity);
    }

    /// <summary>Separates likely file copies from fingerprint matches with another size.</summary>
    [Fact]
    public void Analyze_ClassifiesFingerprintDuplicateCandidates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-doctor-duplicates-{Guid.NewGuid():N}");
        var image = typeof(LibraryMetadataRepairServiceTests).Assembly.Location;
        var tracks = new[]
        {
            new MetadataRepairTrack(1, Path.Combine(root, "a", "one.flac"), Path.Combine(root, "a", "one.flac"),
                "One", "Artist", "Album A", "Artist", 180, 1, 1, "-7 dB", "id-1", 1, 1, true, image, "fp", 1000),
            new MetadataRepairTrack(2, Path.Combine(root, "b", "one.flac"), Path.Combine(root, "b", "one.flac"),
                "One", "Artist", "Album B", "Artist", 180, 1, 1, "-7 dB", "id-2", 1, 1, true, image, "fp", 1000),
            new MetadataRepairTrack(3, Path.Combine(root, "c", "one.flac"), Path.Combine(root, "c", "one.flac"),
                "One", "Artist", "Album C", "Artist", 180, 1, 1, "-7 dB", "id-3", 1, 1, true, image, "fp", 900)
        };

        var candidates = LibraryMetadataRepairService.Analyze(tracks);

        Assert.Equal(2, candidates.Count(candidate => candidate.LikelyDuplicateFileCount == 1));
        Assert.Equal(1, candidates.Count(candidate => candidate.AlternateRecordingFileCount == 1));
    }

    /// <summary>Confirms byte-identical files only after hashing their complete contents.</summary>
    [Fact]
    public void Analyze_ConfirmsExactDuplicateFilesByContentHash()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-doctor-exact-{Guid.NewGuid():N}");
        var first = Path.Combine(root, "a", "one.flac");
        var second = Path.Combine(root, "b", "one.flac");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.WriteAllBytes(first, [1, 2, 3, 4]);
        File.WriteAllBytes(second, [1, 2, 3, 4]);
        try
        {
            var image = typeof(LibraryMetadataRepairServiceTests).Assembly.Location;
            var tracks = new[]
            {
                new MetadataRepairTrack(1, first, first, "One", "Artist", "Album A", "Artist",
                    180, 1, 1, "-7 dB", "id-1", 1, 1, true, image, "fp", 4),
                new MetadataRepairTrack(2, second, second, "One", "Artist", "Album B", "Artist",
                    180, 1, 1, "-7 dB", "id-2", 1, 1, true, image, "fp", 4)
            };

            var candidates = LibraryMetadataRepairService.Analyze(tracks);

            Assert.All(candidates, candidate => Assert.Equal(1, candidate.ExactDuplicateFileCount));
            Assert.All(candidates, candidate => Assert.Equal(0, candidate.LikelyDuplicateFileCount));
            Assert.All(candidates, candidate => Assert.Contains(candidate.Findings,
                finding => finding.Code == "exact-duplicate"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Flags conservative spelling variants without merging the affected artists.</summary>
    [Fact]
    public void Analyze_DetectsArtistNameVariantsAcrossFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-doctor-artists-{Guid.NewGuid():N}");
        var tracks = new[]
        {
            new MetadataRepairTrack(1, Path.Combine(root, "a", "one.flac"), Path.Combine(root, "a", "one.flac"),
                "One", "A-Ha", "Album A", "A-Ha", 180, 1, 1),
            new MetadataRepairTrack(2, Path.Combine(root, "b", "one.flac"), Path.Combine(root, "b", "one.flac"),
                "One", "a ha", "Album B", "a ha", 180, 1, 1)
        };

        var candidates = LibraryMetadataRepairService.Analyze(tracks);

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Equal(1, candidate.ArtistNameVariantCount));
        Assert.All(candidates, candidate => Assert.Contains(candidate.Findings,
            finding => finding.Code == "artist-name-variant" &&
                       finding.RepairCapability == LibraryDoctorRepairCapability.GuidedReview));
    }

    /// <summary>Detects a folder split by inconsistent album titles and missing track numbers.</summary>
    [Fact]
    public void Analyze_DetectsFragmentedPhysicalAlbum()
    {
        var folder = Path.Combine(Path.GetTempPath(), "orynivo-repair-analysis");
        var tracks = new[]
        {
            new MetadataRepairTrack(1, Path.Combine(folder, "one.flac"), Path.Combine(folder, "one.flac"),
                "One", "Artist", "Album", "Artist", 180, 1, 1),
            new MetadataRepairTrack(2, Path.Combine(folder, "two.flac"), Path.Combine(folder, "two.flac"),
                "Two", "Artist", "Album typo", "Artist", 200, null, 1)
        };

        var candidate = Assert.Single(LibraryMetadataRepairService.Analyze(tracks));

        Assert.Equal(2, candidate.AlbumCount);
        Assert.Equal(1, candidate.MissingTrackNumberCount);
        Assert.True(candidate.HasProblems);
    }

    /// <summary>Ensures confirmed metadata remains active when the original bad tags are upserted again.</summary>
    [Fact]
    public void ApplyTrackMetadataOverrides_SurvivesLaterUpsert()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-repair-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        var trackPath = Path.Combine(root, "track.flac");
        using var database = new AudioDatabase(databasePath);
        database.Upsert(CreateTrack(trackPath, "Wrong title", "Wrong artist", "Wrong album"));
        database.ApplyTrackMetadataOverrides(
        [
            new TrackMetadataOverride(
                trackPath,
                "Correct title",
                "Correct artist",
                "Correct artist",
                "Correct album",
                1,
                1,
                1,
                1,
                "recording-id",
                "release-id",
                "artist-id")
        ]);

        database.Upsert(CreateTrack(trackPath, "Wrong title", "Wrong artist", "Wrong album"));
        var corrected = database.GetByPath(trackPath);

        Assert.NotNull(corrected);
        Assert.Equal("Correct title", corrected.Title);
        Assert.Equal("Correct artist", corrected.Artist);
        Assert.Equal("Correct album", corrected.Album);
        Assert.Equal("release-id", corrected.MusicBrainzReleaseId);
    }

    /// <summary>Retains a plausible text match when MusicBrainz supplies no track durations.</summary>
    [Fact]
    public void CalculateMatchConfidence_UsesTitlesWithoutDurations()
    {
        var local = new[]
        {
            new MetadataRepairTrack(1, "one.flac", "one.flac", "First Song", "Artist",
                "Album", "Artist", null, 1, 1),
            new MetadataRepairTrack(2, "two.flac", "two.flac", "Second Song", "Artist",
                "Album", "Artist", null, 2, 1)
        };
        var remote = new[]
        {
            new MetadataMatchTrack(1, "First Song", "Artist", null, null),
            new MetadataMatchTrack(2, "Second Song", "Artist", null, null)
        };

        var confidence = LibraryMetadataRepairService.CalculateMatchConfidence(
            local,
            remote,
            0,
            0);

        Assert.True(confidence >= 0.75);
    }

    /// <summary>Ranks close duration and title evidence above an unrelated candidate.</summary>
    [Fact]
    public void CalculateMatchConfidence_PrefersMatchingEvidence()
    {
        var local = new[]
        {
            new MetadataRepairTrack(1, "one.flac", "one.flac", "First Song", "Artist",
                "Album", "Artist", 180, 1, 1)
        };
        var matching = new[]
        {
            new MetadataMatchTrack(1, "First Song", "Artist", null, 181000)
        };
        var unrelated = new[]
        {
            new MetadataMatchTrack(1, "Completely Different", "Artist", null, 240000)
        };

        var matchingConfidence = LibraryMetadataRepairService.CalculateMatchConfidence(
            local,
            matching,
            1,
            1);
        var unrelatedConfidence = LibraryMetadataRepairService.CalculateMatchConfidence(
            local,
            unrelated,
            60,
            1);

        Assert.True(matchingConfidence > unrelatedConfidence);
    }

    private static TrackRecord CreateTrack(string path, string title, string artist, string album) =>
        new()
        {
            Path = path,
            SourcePath = path,
            FileName = Path.GetFileName(path),
            ModifiedAt = 1,
            AddedAt = 1,
            Duration = 180,
            Title = title,
            Artist = artist,
            AlbumArtist = artist,
            Album = album,
            TrackNumber = 1
        };
}
