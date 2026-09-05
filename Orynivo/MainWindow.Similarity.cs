using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Audio;
using System.Diagnostics;

namespace Orynivo;

public partial class MainWindow
{
    private const int SimilarityPageSize = 1000;
    private const int SimilarityQueueSize = 40;
    private IReadOnlyList<SimilarityFeatureVector> _similarityMixCandidates = [];
    private int _similarityMixCursor;
    private readonly SemaphoreSlim _similarityFeatureCacheGate = new(1, 1);
    private IReadOnlyList<SimilarityFeatureVector>? _similarityFeatureCache;
    private DateTimeOffset _similarityFeatureCacheExpiresAt;
    private int _similarityFeatureCacheGeneration;
    private int _audioFeatureWarmupRunning;
    private readonly CancellationTokenSource _audioFeatureWarmupCts = new();
    private sealed record MoodMixActionTag(string Path, SimilarityMood Mood);

    /// <summary>Gets whether Infinite Mix is currently continuing a similarity-based queue.</summary>
    private bool HasActiveSimilarityMix => _similarityMixCandidates.Count > 0;

    /// <summary>Determines whether a queue path belongs to a similarity-capable local or Orynivo catalog.</summary>
    /// <param name="path">Playback path represented by a track action.</param>
    /// <returns><see langword="true"/> for local and Orynivo Server library tracks.</returns>
    private bool CanOfferTrackSimilarity(string path)
    {
        if (_orynivoTracksByUrl.ContainsKey(path) || TryResolveOrynivoPlaylistReference(path, out _, out _))
            return true;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
            return false;
        using var db = AudioDatabase.OpenDefault();
        return db.GetTrackIdByPath(path).HasValue;
    }

    /// <summary>Builds and starts a diverse cross-library queue around one selected track.</summary>
    private async void PlayMoreLikeThisMenuItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.MenuItem { Tag: string path })
            return;

        StatusTextBlock.Text = LocalizationManager.Current.SimilarTracksLoading;
        try
        {
            var seedIdentity = await ResolveSimilaritySeedAsync(path).ConfigureAwait(true);
            if (seedIdentity is null)
            {
                StatusTextBlock.Text = LocalizationManager.Current.SimilarTracksUnavailable;
                return;
            }

            var vectors = await LoadAvailableSimilarityFeaturesAsync().ConfigureAwait(true);
            StatusTextBlock.Text = $"{LocalizationManager.Current.SimilarTracksLoading} ({vectors.Count:N0})";
            var seed = vectors.FirstOrDefault(vector =>
                vector.SourceKey == seedIdentity.Value.SourceKey && vector.TrackId == seedIdentity.Value.TrackId);
            if (seed is null)
            {
                StatusTextBlock.Text = LocalizationManager.Current.SimilarTracksUnavailable;
                return;
            }

            // Ranking can traverse tens of thousands of local and remote
            // vectors. Keep the CPU-heavy calculation off Avalonia's UI thread
            // so playback controls and navigation remain responsive.
            var matches = await Task.Run(() => SimilarityFeatureService.RankSimilar(
                seed,
                vectors,
                maximumResults: 500,
                maximumPerArtist: 10,
                maximumPerAlbum: 5));
            StatusTextBlock.Text = $"{LocalizationManager.Current.SimilarTracksLoading} ({matches.Count:N0})";
            var initialVectors = matches.Take(SimilarityQueueSize).Select(match => match.Vector).ToList();
            // Keep provider mapping, DTO conversion and any synchronous cache
            // work off the UI thread as well; remote providers may perform
            // substantial JSON/materialization even after the HTTP await.
            var rows = await Task.Run(
                () => ResolveSimilarityRowsAsync(initialVectors));
            if (rows.Count == 0)
            {
                StatusTextBlock.Text = LocalizationManager.Current.SimilarTracksNoMatches;
                return;
            }

            var activePlaybackPath = _player is not null ? _currentFilePath : null;
            var keepCurrentPlayback = !string.IsNullOrWhiteSpace(activePlaybackPath);
            StopInfiniteMix();
            _similarityMixCandidates = matches.Select(match => match.Vector).ToList();
            _similarityMixCursor = initialVectors.Count;
            _infiniteMixEnabled = true;
            _infiniteMixPaused = false;
            _lastInfiniteMixRefillAttempt = DateTimeOffset.MinValue;
            _queue.Clear();
            // Keep an already playing title at the head of the queue. Replacing
            // the queue must never tear down the active player.
            if (!string.IsNullOrWhiteSpace(activePlaybackPath))
                _queue.Add(CreatePlaylistItem(activePlaybackPath));
            if (!string.Equals(activePlaybackPath, path, StringComparison.OrdinalIgnoreCase))
                _queue.Add(CreatePlaylistItem(path));
            foreach (var row in rows.Where(row => !string.Equals(row.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                _queue.Add(ToPlaylistItem(row));
            _queueIndex = _queue.Count > 0 ? 0 : -1;
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            UpdateInfiniteMixUi();
            if (!keepCurrentPlayback && _queue.Count > 0)
                await StartPlaybackAsync(path);
            await ShowTopLevelViewAsync("Queue");
            StatusTextBlock.Text = string.Format(
                LocalizationManager.Current.SimilarTracksQueued,
                _queue.Count - 1);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped;
        }
        catch (Exception exception)
        {
            CrashLogger.Log(exception, "Play more like this");
            StatusTextBlock.Text = LocalizationManager.Current.SimilarTracksUnavailable;
        }
    }

    /// <summary>Builds and starts a metadata-ranked mood mix from one selected track.</summary>
    private async void PlayMoodMixMenuItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.MenuItem { Tag: MoodMixActionTag action })
            return;
        StatusTextBlock.Text = LocalizationManager.Current.SimilarTracksLoading;
        try
        {
            var vectors = await LoadAvailableSimilarityFeaturesAsync().ConfigureAwait(true);
            var ranked = (await Task.Run(() => SimilarityFeatureService.RankMood(action.Mood, vectors)))
                .Select(match => match.Vector)
                .ToList();
            var seedIdentity = await ResolveSimilaritySeedAsync(action.Path).ConfigureAwait(true);
            if (seedIdentity is { } identity)
                ranked.RemoveAll(vector => vector.SourceKey == identity.SourceKey && vector.TrackId == identity.TrackId);
            var initialVectors = ranked.Take(SimilarityQueueSize).ToList();
            var rows = await Task.Run(
                () => ResolveSimilarityRowsAsync(initialVectors));
            if (rows.Count == 0)
            {
                StatusTextBlock.Text = LocalizationManager.Current.SimilarTracksNoMatches;
                return;
            }

            var activePlaybackPath = _player is not null ? _currentFilePath : null;
            var keepCurrentPlayback = !string.IsNullOrWhiteSpace(activePlaybackPath);
            StopInfiniteMix();
            _similarityMixCandidates = ranked;
            _similarityMixCursor = initialVectors.Count;
            _infiniteMixEnabled = true;
            _infiniteMixPaused = false;
            _lastInfiniteMixRefillAttempt = DateTimeOffset.MinValue;
            _queue.Clear();
            if (!string.IsNullOrWhiteSpace(activePlaybackPath))
                _queue.Add(CreatePlaylistItem(activePlaybackPath));
            if (!string.Equals(activePlaybackPath, action.Path, StringComparison.OrdinalIgnoreCase))
                _queue.Add(CreatePlaylistItem(action.Path));
            foreach (var row in rows.Where(row => !string.Equals(row.FilePath, action.Path, StringComparison.OrdinalIgnoreCase)))
                _queue.Add(ToPlaylistItem(row));
            _queueIndex = _queue.Count > 0 ? 0 : -1;
            ResetQueuePlaybackState();
            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            UpdateInfiniteMixUi();
            if (!keepCurrentPlayback && _queue.Count > 0)
                await StartPlaybackAsync(action.Path);
            await ShowTopLevelViewAsync("Queue");
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.SimilarTracksQueued, _queue.Count - 1);
        }
        catch (Exception exception)
        {
            CrashLogger.Log(exception, "Play mood mix");
            StatusTextBlock.Text = LocalizationManager.Current.SimilarTracksUnavailable;
        }
    }

    private async Task<List<SimilarityFeatureVector>> LoadAvailableSimilarityFeaturesAsync()
    {
        if (_similarityFeatureCache is { } cached && DateTimeOffset.UtcNow < _similarityFeatureCacheExpiresAt)
        {
            return cached.ToList();
        }
        await _similarityFeatureCacheGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_similarityFeatureCache is { } refreshed && DateTimeOffset.UtcNow < _similarityFeatureCacheExpiresAt)
                return refreshed.ToList();
            var generation = Volatile.Read(ref _similarityFeatureCacheGeneration);
            var timer = Stopwatch.StartNew();
            var localTask = Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetSimilarityTrackProfiles().Select(SimilarityFeatureService.Create).ToList();
            });
            var remoteTasks = (_settings.OrynivoServers ?? []).Select(LoadAllSimilarityFeaturesAsync).ToArray();
            await Task.WhenAll(remoteTasks.Cast<Task>().Append(localTask)).ConfigureAwait(false);
            var loaded = localTask.Result.Concat(remoteTasks.SelectMany(task => task.Result)).ToList();
            if (generation == Volatile.Read(ref _similarityFeatureCacheGeneration))
            {
                _similarityFeatureCache = loaded;
                _similarityFeatureCacheExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
            }
            timer.Stop();
            Debug.WriteLine($"Similarity features loaded: {loaded.Count:N0} vectors in {timer.ElapsedMilliseconds:N0} ms.");
            _ = Task.Run(WarmAudioFeaturesAsync);
            return loaded.ToList();
        }
        finally
        {
            _similarityFeatureCacheGate.Release();
        }
    }

    /// <summary>Preloads similarity vectors after startup without delaying the UI.</summary>
    private async Task WarmSimilarityFeatureCacheAsync()
    {
        try
        {
            await LoadAvailableSimilarityFeaturesAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Similarity cache warm-up failed: {exception.GetType().Name}");
        }
    }

    private async Task WarmAudioFeaturesAsync()
    {
        if (Interlocked.CompareExchange(ref _audioFeatureWarmupRunning, 1, 0) != 0)
            return;
        try
        {
            var localTask = AudioFeatureMaintenanceService.AnalyzeMissingAsync(
                AudioDatabase.OpenDefault,
                maximumTracks: 4,
                delay: TimeSpan.FromMilliseconds(250),
                cancellationToken: _audioFeatureWarmupCts.Token);
            var remoteTasks = (_settings.OrynivoServers ?? [])
                .Select(server => _orynivoClient.TriggerAudioFeatureAnalysisAsync(
                    server,
                    4,
                    _audioFeatureWarmupCts.Token))
                .ToArray();
            var localResult = await localTask.ConfigureAwait(false);
            await Task.WhenAll(remoteTasks).ConfigureAwait(false);
            if (localResult.Stored > 0)
                InvalidateSimilarityFeatureCache();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Optional audio-feature warm-up failed: {exception.GetType().Name}");
        }
        finally
        {
            Interlocked.Exchange(ref _audioFeatureWarmupRunning, 0);
        }
    }

    /// <summary>Cancels optional local descriptor work during application shutdown.</summary>
    private void CancelAudioFeatureWarmup() => _audioFeatureWarmupCts.Cancel();

    /// <summary>Invalidates compact similarity vectors after catalog or preference mutations.</summary>
    private void InvalidateSimilarityFeatureCache()
    {
        Interlocked.Increment(ref _similarityFeatureCacheGeneration);
        _similarityFeatureCache = null;
        _similarityFeatureCacheExpiresAt = DateTimeOffset.MinValue;
    }

    /// <summary>Clears the transient similarity continuation profile.</summary>
    private void ClearSimilarityMix()
    {
        _similarityMixCandidates = [];
        _similarityMixCursor = 0;
    }

    /// <summary>Appends the next distinct resolved similarity candidates to the active queue.</summary>
    /// <param name="batchSize">Maximum number of tracks to append.</param>
    /// <param name="refreshActivePlayback">Whether an active gapless player must receive the revised queue.</param>
    private async Task RefillSimilarityMixAsync(int batchSize, bool refreshActivePlayback)
    {
        var queued = _queue.Select(item => item.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        while (_similarityMixCursor < _similarityMixCandidates.Count && added < batchSize)
        {
            var take = Math.Min(batchSize, _similarityMixCandidates.Count - _similarityMixCursor);
            var vectors = _similarityMixCandidates.Skip(_similarityMixCursor).Take(take).ToList();
            _similarityMixCursor += take;
            var rows = await ResolveSimilarityRowsAsync(vectors);
            foreach (var row in rows)
            {
                if (added == batchSize)
                    break;
                if (string.IsNullOrWhiteSpace(row.FilePath) || !queued.Add(row.FilePath))
                    continue;
                _queue.Add(ToPlaylistItem(row));
                added++;
            }
        }

        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        if (refreshActivePlayback && added > 0)
            await RefreshActiveGaplessQueueAsync();
        if (_similarityMixCursor >= _similarityMixCandidates.Count && added == 0)
            StopInfiniteMix();
    }

    private async Task<(string SourceKey, long TrackId)?> ResolveSimilaritySeedAsync(string path)
    {
        if (_orynivoTracksByUrl.TryGetValue(path, out var row) &&
            row.OrynivoServer is { } rowServer && row.Id is long rowId)
        {
            return ($"orynivo:{rowServer.Id}", rowId);
        }
        if (TryResolveOrynivoPlaylistReference(path, out var server, out var trackId))
            return ($"orynivo:{server.Id}", trackId);
        return await Task.Run<(string, long)?>(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetTrackIdByPath(path) is long id ? ("local", id) : null;
        });
    }

    private async Task<List<SimilarityFeatureVector>> LoadAllSimilarityFeaturesAsync(Orynivo.Streaming.OrynivoServerSettings server)
    {
        var result = new List<SimilarityFeatureVector>();
        for (var page = 0; ; page++)
        {
            var vectors = await _orynivoClient.GetSimilarityFeaturesAsync(server, page, SimilarityPageSize)
                .ConfigureAwait(false);
            result.AddRange(vectors);
            if (vectors.Count < SimilarityPageSize)
                return result;
        }
    }

    private async Task<List<ContentRow>> ResolveSimilarityRowsAsync(IReadOnlyList<SimilarityFeatureVector> vectors)
    {
        var resolved = new Dictionary<(string SourceKey, long TrackId), ContentRow>();
        var localIds = vectors.Where(vector => vector.SourceKey == "local").Select(vector => vector.TrackId).ToList();
        foreach (var track in await _localCatalogProvider.GetTracksByIdsAsync(localIds).ConfigureAwait(false))
            resolved[("local", track.Id)] = ToCatalogTrackContentRow(track);

        foreach (var group in vectors.Where(vector => vector.SourceKey.StartsWith("orynivo:", StringComparison.Ordinal))
                     .GroupBy(vector => vector.SourceKey, StringComparer.Ordinal))
        {
            var serverId = group.Key["orynivo:".Length..];
            var server = (_settings.OrynivoServers ?? []).FirstOrDefault(candidate => candidate.Id == serverId);
            if (server is null)
                continue;
            var tracks = await CreateOrynivoCatalogProvider(server)
                .GetTracksByIdsAsync(group.Select(vector => vector.TrackId).ToList())
                .ConfigureAwait(false);
            foreach (var track in tracks)
                resolved[(group.Key, track.Id)] = ToCatalogTrackContentRow(track, server);
        }

        return vectors
            .Select(vector => resolved.GetValueOrDefault((vector.SourceKey, vector.TrackId)))
            .OfType<ContentRow>()
            .ToList();
    }
}
