using Orynivo.Library;

namespace Orynivo.Audio;

/// <summary>Result of one bounded optional acoustic-feature maintenance batch.</summary>
/// <param name="Examined">Candidates examined.</param>
/// <param name="Stored">Descriptors stored successfully.</param>
/// <param name="Failed">Sources that could not be decoded.</param>
public sealed record AudioFeatureBatchResult(int Examined, int Stored, int Failed);

/// <summary>Runs sequential, low-priority acoustic analysis without modifying source media.</summary>
public static class AudioFeatureMaintenanceService
{
    /// <summary>Analyzes one bounded database batch sequentially.</summary>
    /// <param name="databaseFactory">Factory opening a dedicated database connection.</param>
    /// <param name="maximumTracks">Maximum number of tracks in this batch.</param>
    /// <param name="delay">Optional cooperative delay between FFmpeg processes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Batch counters.</returns>
    public static async Task<AudioFeatureBatchResult> AnalyzeMissingAsync(
        Func<AudioDatabase> databaseFactory,
        int maximumTracks = 4,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(databaseFactory);
        maximumTracks = Math.Clamp(maximumTracks, 1, 100);
        List<AudioFeatureAnalysisCandidate> candidates;
        using (var database = databaseFactory())
            candidates = database.GetTracksMissingAudioFeatures(maximumTracks);
        var stored = 0;
        var failed = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AudioFeatureDescriptor? descriptor = null;
            try
            {
                if (File.Exists(candidate.SourcePath))
                {
                    descriptor = await AudioFeatureAnalysisService.AnalyzeAsync(
                        candidate.SourcePath,
                        candidate.SegmentStart,
                        candidate.SegmentEnd,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                descriptor = null;
            }

            using (var database = databaseFactory())
            {
                if (descriptor is null)
                {
                    database.SetTrackAudioFeatureFailure(candidate.TrackId);
                    failed++;
                }
                else
                {
                    database.SetTrackAudioFeatures(candidate.TrackId, descriptor);
                    stored++;
                }
            }
            if (delay is { } pause && pause > TimeSpan.Zero)
                await Task.Delay(pause, cancellationToken).ConfigureAwait(false);
        }
        return new AudioFeatureBatchResult(candidates.Count, stored, failed);
    }
}
