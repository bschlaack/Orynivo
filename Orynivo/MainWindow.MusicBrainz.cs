using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Streaming;

namespace Orynivo;

public partial class MainWindow
{
    private static readonly TimeSpan MusicBrainzUnresolvedRetryLifetime = TimeSpan.FromDays(90);

    /// <summary>Starts the single low-priority MusicBrainz enrichment worker on first playback.</summary>
    private void StartMusicBrainzBackgroundEnrichment()
    {
        if (Interlocked.Exchange(ref _musicBrainzBackgroundStarted, 1) != 0)
            return;
        _ = Task.Run(() => RunMusicBrainzBackgroundEnrichmentAsync(_musicBrainzBackgroundCts.Token));
    }

    /// <summary>Enriches stale local and configured-server tracks while audio is actively playing.</summary>
    /// <param name="cancellationToken">Application-lifetime cancellation token.</param>
    /// <returns>A task representing the worker lifetime.</returns>
    private async Task RunMusicBrainzBackgroundEnrichmentAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await WaitForMusicBrainzBackgroundWindowAsync(cancellationToken);
                var candidates = await LoadMusicBrainzBackgroundCandidatesAsync(cancellationToken);
                foreach (var row in candidates)
                {
                    await WaitForMusicBrainzBackgroundWindowAsync(cancellationToken);
                    if (NeedsMusicBrainzRefresh(row))
                        await RefreshMusicBrainzTrackRatingAsync(row, cancellationToken);
                }

                await Task.Delay(TimeSpan.FromMinutes(15), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
            }
        }
    }

    private async Task WaitForMusicBrainzBackgroundWindowAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var player = _player;
            if (player is not null &&
                !player.IsPaused &&
                Volatile.Read(ref _musicBrainzForegroundRequests) == 0)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private static bool NeedsMusicBrainzRefresh(ContentRow row)
    {
        if (!row.MusicBrainzRatingFetchedAt.HasValue)
            return true;
        var lifetime = Guid.TryParse(row.MusicBrainzTrackId, out _)
            ? MusicBrainzRatingCacheLifetime
            : MusicBrainzUnresolvedRetryLifetime;
        return row.MusicBrainzRatingFetchedAt.Value < DateTimeOffset.UtcNow
            .Subtract(lifetime)
            .ToUnixTimeSeconds();
    }

    private async Task<IReadOnlyList<ContentRow>> LoadMusicBrainzBackgroundCandidatesAsync(
        CancellationToken cancellationToken)
    {
        var candidates = new List<ContentRow>();
        var local = await Task.Run(
            async () => await _localCatalogProvider.GetTracksAsync(cancellationToken: cancellationToken),
            cancellationToken);
        candidates.AddRange(local.Where(IsStale).Select(track => CreateMusicBrainzCandidate(track)));

        foreach (var server in _settings.OrynivoServers.ToList())
        {
            var provider = new OrynivoServerLibraryCatalogProvider(server, _orynivoClient, (_, _) => false);
            for (var page = 0; ; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitForMusicBrainzBackgroundWindowAsync(cancellationToken);
                var batch = await provider.GetTracksAsync(page, 500, cancellationToken);
                candidates.AddRange(batch
                    .Where(IsStale)
                    .Select(track => CreateMusicBrainzCandidate(track, server)));
                if (batch.Count < 500)
                    break;
            }
        }

        return candidates
            .OrderBy(track => track.MusicBrainzRatingFetchedAt ?? 0)
            .ThenByDescending(track => Guid.TryParse(track.MusicBrainzTrackId, out _))
            .ToList();

        static bool IsStale(LibraryCatalogTrack track)
        {
            var row = CreateMusicBrainzCandidate(track);
            return NeedsMusicBrainzRefresh(row);
        }
    }

    private static ContentRow CreateMusicBrainzCandidate(
        LibraryCatalogTrack track,
        OrynivoServerSettings? server = null) => new()
    {
        Id = track.Id,
        Title = track.Title?.Trim() ?? track.FileName.Trim(),
        Artist = track.Artist,
        KnownDuration = track.KnownDuration ?? (track.Duration is double seconds
            ? TimeSpan.FromSeconds(seconds)
            : null),
        MusicBrainzTrackId = track.MusicBrainzTrackId,
        MusicBrainzRating = track.MusicBrainzRating,
        MusicBrainzRatingVotes = track.MusicBrainzRatingVotes,
        MusicBrainzRatingFetchedAt = track.MusicBrainzRatingFetchedAt,
        EntityType = server is null ? "Track" : "OrynivoTrack",
        OrynivoServer = server
    };

    /// <summary>Persists an unsuccessful MusicBrainz resolution for a delayed retry.</summary>
    /// <param name="row">Track whose lookup completed without a unique recording.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing persistence.</returns>
    private async Task PersistMusicBrainzLookupAttemptAsync(ContentRow row, CancellationToken cancellationToken)
    {
        if (row.Id is not long trackId)
            return;
        var fetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var saved = row.OrynivoServer is { } server
            ? await _orynivoClient.UpdateTrackRatingAsync(
                server,
                trackId,
                new OrynivoTrackRatingUpdate(MusicBrainzRatingFetchedAt: fetchedAt),
                cancellationToken)
            : await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.SetTrackMusicBrainzLookupAttempt(trackId, fetchedAt);
                return true;
            }, cancellationToken);
        if (saved)
            await Dispatcher.UIThread.InvokeAsync(() => row.MusicBrainzRatingFetchedAt = fetchedAt);
    }
}
