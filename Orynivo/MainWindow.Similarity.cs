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
    private sealed record PresetMixActionTag(string Path, SimilarityPreset Preset);

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
        await StartRankedSimilarityMixAsync(
            action.Path,
            vectors => SimilarityFeatureService.RankMood(action.Mood, vectors),
            "Play mood mix").ConfigureAwait(true);
    }

    /// <summary>Builds and starts a curated mood/activity preset mix from one selected track.</summary>
    private async void PlayPresetMixMenuItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.MenuItem { Tag: PresetMixActionTag action })
            return;
        await StartRankedSimilarityMixAsync(
            action.Path,
            vectors => SimilarityFeatureService.RankPreset(action.Preset, vectors),
            "Play activity mix").ConfigureAwait(true);
    }

    /// <summary>
    /// Starts a similarity continuation queue from a pre-ranked vector list,
    /// reusing the shared Infinite Mix queue, persistence, and navigation path.
    /// </summary>
    /// <param name="path">Reference track path whose playback context is preserved.</param>
    /// <param name="rank">Deterministic ranking applied to the loaded feature vectors.</param>
    /// <param name="logContext">Sanitized crash-log context label.</param>
    /// <returns>A task representing queue construction and optional playback start.</returns>
    private async Task StartRankedSimilarityMixAsync(
        string path,
        Func<IReadOnlyList<SimilarityFeatureVector>, IReadOnlyList<SimilarityFeatureMatch>> rank,
        string logContext)
    {
        StatusTextBlock.Text = LocalizationManager.Current.SimilarTracksLoading;
        try
        {
            var vectors = await LoadAvailableSimilarityFeaturesAsync().ConfigureAwait(true);
            var ranked = (await Task.Run(() => rank(vectors)))
                .Select(match => match.Vector)
                .ToList();
            var seedIdentity = await ResolveSimilaritySeedAsync(path).ConfigureAwait(true);
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
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.SimilarTracksQueued, _queue.Count - 1);
        }
        catch (Exception exception)
        {
            CrashLogger.Log(exception, logContext);
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
            // Harmonic mixing: order each appended batch along the Camelot wheel.
            var vectors = HarmonicOrdering.Order(
                    _similarityMixCandidates.Skip(_similarityMixCursor).Take(take).ToList(),
                    vector => CamelotKey.TryParse(vector.CamelotKey, out var camelot) ? camelot : null)
                .ToList();
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

    /// <summary>
    /// Creates a smart playlist that keeps the tracks most similar to the
    /// clicked local or Orynivo Server reference track.
    /// </summary>
    private async void CreateSimilarSmartPlaylistMenuItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.MenuItem { Tag: string path })
            return;

        var dialog = new NewPlaylistDialog();
        if (await dialog.ShowDialog<bool>(this) == false || string.IsNullOrWhiteSpace(dialog.PlaylistName))
            return;

        var name = dialog.PlaylistName.Trim();
        if (await CreateSimilarPlaylistByPathAsync(name, path, minimumScore: null).ConfigureAwait(true) is null)
        {
            StatusTextBlock.Text = LocalizationManager.Current.SimilarTracksUnavailable;
            return;
        }

        LoadNavPlaylists();
        StatusTextBlock.Text = string.Format(LocalizationManager.Current.SmartPlaylistSaved, name);
    }

    /// <summary>
    /// Creates a similarity smart playlist from a reference track. Shared by the
    /// track context menu and the MCP/AI Chat tool. Must be called on the UI
    /// thread because the reference lookup uses UI-owned track registrations.
    /// </summary>
    /// <param name="name">Playlist name.</param>
    /// <param name="path">Local path or opaque remote reference of the reference track.</param>
    /// <param name="minimumScore">Optional inclusive minimum similarity score.</param>
    /// <returns>The new playlist ID, or <see langword="null"/> when the reference cannot be resolved.</returns>
    internal async Task<long?> CreateSimilarPlaylistByPathAsync(string name, string path, double? minimumScore)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            return null;

        var seedIdentity = await ResolveSimilaritySeedAsync(path).ConfigureAwait(true);
        if (seedIdentity is null)
            return null;

        // Smart-playlist criteria use the same provider convention as the source
        // facet, so a remote reference is stored as its server source key.
        var sourceKey = seedIdentity.Value.SourceKey;
        if (sourceKey.StartsWith("orynivo:", StringComparison.Ordinal))
            sourceKey = GetServerSourceKey(sourceKey["orynivo:".Length..]);

        var criteria = new SmartPlaylistCriteria
        {
            SimilaritySourceKey = sourceKey,
            SimilarityTrackId = seedIdentity.Value.TrackId,
            SimilarityMinimumScore = minimumScore
        };
        var trimmed = name.Trim();

        return await Task.Run(() =>
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                return (long?)db.CreateSmartPlaylist(
                    trimmed,
                    System.Text.Json.JsonSerializer.Serialize(criteria));
            }
            catch
            {
                return null;
            }
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Loads the similarity vectors used to resolve a similarity smart playlist.
    /// Remote vectors are re-keyed onto the smart-playlist candidate provider key
    /// and pseudo-IDs so the shared Core resolver can match them; unavailable
    /// servers are skipped. Blocks on the cached feature load, so it must run off
    /// the UI thread.
    /// </summary>
    /// <param name="criteria">Criteria being resolved; vectors load only for a configured reference.</param>
    /// <param name="remoteTracks">Pseudo-ID to (server, track) map produced by the candidate build.</param>
    /// <returns>The translated vectors, or <see langword="null"/> when none are available.</returns>
    private IReadOnlyList<SimilarityFeatureVector>? BuildSmartPlaylistSimilarityFeatures(
        SmartPlaylistCriteria criteria,
        Dictionary<long, (Orynivo.Streaming.OrynivoServerSettings Server, LibraryCatalogTrack Track)> remoteTracks)
    {
        if (string.IsNullOrWhiteSpace(criteria.SimilaritySourceKey) || criteria.SimilarityTrackId is null)
            return null;

        try
        {
            var vectors = LoadAvailableSimilarityFeaturesAsync().GetAwaiter().GetResult();
            if (vectors.Count == 0)
                return null;

            var pseudoIdByRealTrack = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var (pseudoId, entry) in remoteTracks)
                pseudoIdByRealTrack[$"{entry.Server.Id}\u001f{entry.Track.Id}"] = pseudoId;

            var translated = new List<SimilarityFeatureVector>(vectors.Count);
            foreach (var vector in vectors)
            {
                if (!vector.SourceKey.StartsWith("orynivo:", StringComparison.Ordinal))
                {
                    translated.Add(vector);
                    continue;
                }

                var serverId = vector.SourceKey["orynivo:".Length..];
                if (!pseudoIdByRealTrack.TryGetValue($"{serverId}\u001f{vector.TrackId}", out var pseudoId))
                    continue;
                translated.Add(vector with
                {
                    SourceKey = GetServerSourceKey(serverId),
                    TrackId = pseudoId
                });
            }

            return translated.Count > 0 ? translated : null;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Smart playlist similarity features failed: {exception.GetType().Name}");
            return null;
        }
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
