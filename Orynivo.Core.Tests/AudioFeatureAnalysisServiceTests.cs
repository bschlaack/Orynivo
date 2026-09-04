using Orynivo.Audio;
using Orynivo.Library;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies deterministic bounded acoustic descriptor extraction.</summary>
public sealed class AudioFeatureAnalysisServiceTests
{
    /// <summary>Distinguishes low-frequency smooth audio from frequent zero crossings.</summary>
    [Fact]
    public void AnalyzePcm_ReportsHigherBrightnessForFrequentCrossings()
    {
        const int sampleRate = 8000;
        var smooth = Enumerable.Range(0, sampleRate * 2)
            .Select(index => (float)(0.25 * Math.Sin(2 * Math.PI * 10 * index / sampleRate)))
            .ToArray();
        var alternating = Enumerable.Range(0, sampleRate * 2)
            .Select(index => index % 2 == 0 ? 0.25f : -0.25f)
            .ToArray();

        var smoothResult = AudioFeatureAnalysisService.AnalyzePcm(smooth, sampleRate);
        var alternatingResult = AudioFeatureAnalysisService.AnalyzePcm(alternating, sampleRate);

        Assert.NotNull(smoothResult);
        Assert.NotNull(alternatingResult);
        Assert.True(alternatingResult.Brightness > smoothResult.Brightness);
        Assert.InRange(alternatingResult.Energy, 0d, 1d);
    }

    /// <summary>Detects variation between quiet and loud one-second windows.</summary>
    [Fact]
    public void AnalyzePcm_ReportsDynamicVariation()
    {
        const int sampleRate = 8000;
        var samples = Enumerable.Repeat(0.02f, sampleRate)
            .Concat(Enumerable.Repeat(0.5f, sampleRate))
            .ToArray();

        var result = AudioFeatureAnalysisService.AnalyzePcm(samples, sampleRate);

        Assert.NotNull(result);
        Assert.True(result.Dynamics > 0.5d);
    }

    /// <summary>Persists current descriptors separately and exposes them through similarity profiles.</summary>
    [Fact]
    public void AudioDatabase_RoundTripsCachedAudioFeatures()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-audio-features-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var database = new AudioDatabase(Path.Combine(root, "library.db"));
            var path = Path.Combine(root, "track.flac");
            database.Upsert(new TrackRecord
            {
                Path = path,
                SourcePath = path,
                FileName = "track.flac",
                ModifiedAt = 1,
                AddedAt = 1,
                Title = "Track",
                Artist = "Artist",
                AlbumArtist = "Artist",
                Album = "Album"
            });
            var candidate = Assert.Single(database.GetTracksMissingAudioFeatures());
            database.SetTrackAudioFeatures(candidate.TrackId, new AudioFeatureDescriptor(1, 0.2, 0.4, 0.6, 123));

            Assert.Empty(database.GetTracksMissingAudioFeatures());
            var profile = Assert.Single(database.GetSimilarityTrackProfiles());
            Assert.Equal(0.2, profile.Energy);
            Assert.Equal(0.4, profile.Brightness);
            Assert.Equal(0.6, profile.Dynamics);

            database.Upsert(new TrackRecord
            {
                Path = Path.Combine(root, "broken.flac"),
                SourcePath = Path.Combine(root, "broken.flac"),
                FileName = "broken.flac",
                ModifiedAt = 1,
                AddedAt = 1,
                Title = "Broken",
                Artist = "Artist",
                AlbumArtist = "Artist",
                Album = "Album"
            });
            var failedCandidate = Assert.Single(database.GetTracksMissingAudioFeatures());
            database.SetTrackAudioFeatureFailure(failedCandidate.TrackId);
            Assert.Empty(database.GetTracksMissingAudioFeatures());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
