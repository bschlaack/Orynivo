using Avalonia.Interactivity;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

public partial class MainWindow
{
    private const int InfiniteMixBatchSize = 20;
    private const int InfiniteMixRefillThreshold = 5;
    private bool _infiniteMixEnabled;
    private bool _infiniteMixPaused;
    private bool _infiniteMixLoading;
    private readonly Dictionary<string, InfiniteMixCandidateIdentity> _infiniteMixIdentitiesByPath =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record InfiniteMixCandidateIdentity(string TrackKey, string GenreKey);

    /// <summary>Starts or stops the history-informed, automatically replenished queue.</summary>
    private async void InfiniteMixButton_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_infiniteMixEnabled)
        {
            _infiniteMixPaused = !_infiniteMixPaused;
            UpdateInfiniteMixUi();
            if (!_infiniteMixPaused)
                EnsureInfiniteMixQueue();
            return;
        }

        if (!await EditInfiniteMixSettingsAsync())
            return;

        _infiniteMixEnabled = true;
        _infiniteMixPaused = false;
        _infiniteMixIdentitiesByPath.Clear();
        UpdateInfiniteMixUi();
        if (_currentTopLevelTag == "Queue")
            UpdateRestoreQueueButtonState("Queue");
        _queue.Clear();
        _queueIndex = -1;
        await RefillInfiniteMixAsync(force: true);
        if (_queue.Count == 0)
        {
            StopInfiniteMix();
            StatusTextBlock.Text = LocalizationManager.Current.NoData;
            return;
        }

        _queueIndex = 0;
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        try { await StartPlaybackAsync(_queue[0].FilePath); }
        catch (Exception exception) { CrashLogger.Log(exception, "Infinite Mix playback"); }
    }

    /// <summary>Stops automatic replenishment without clearing the tracks already queued.</summary>
    private void StopInfiniteMix()
    {
        _infiniteMixEnabled = false;
        _infiniteMixPaused = false;
        UpdateInfiniteMixUi();
    }

    /// <summary>Updates the shared Infinite Mix buttons and queue status.</summary>
    private void UpdateInfiniteMixUi()
    {
        InfiniteMixButton.Content = !_infiniteMixEnabled
            ? LocalizationManager.Current.InfiniteMixStart
            : _infiniteMixPaused
                ? LocalizationManager.Current.InfiniteMixResume
                : LocalizationManager.Current.InfiniteMixPause;
        InfiniteMixActionsPanel.IsVisible = _currentTopLevelTag == "Queue" && _infiniteMixEnabled;
        InfiniteMixStatusTextBlock.IsVisible = _currentTopLevelTag == "Queue" && _infiniteMixEnabled;
        InfiniteMixStatusTextBlock.Text = _infiniteMixPaused
            ? LocalizationManager.Current.InfiniteMixPaused
            : LocalizationManager.Current.InfiniteMixActive;
        if (_infiniteMixEnabled)
            StatusTextBlock.Text = _infiniteMixPaused
                ? LocalizationManager.Current.InfiniteMixPaused
                : LocalizationManager.Current.InfiniteMixActive;
    }

    /// <summary>Schedules a refill when the active mix is approaching the end of its queue.</summary>
    private void EnsureInfiniteMixQueue()
    {
        if (!_infiniteMixEnabled || _infiniteMixPaused || _infiniteMixLoading)
            return;
        var remaining = _queueIndex < 0 ? _queue.Count : _queue.Count - _queueIndex - 1;
        if (remaining <= InfiniteMixRefillThreshold)
            _ = RefillInfiniteMixAsync(force: false);
    }

    /// <summary>Ranks unified local/server candidates and appends a diverse batch to the queue.</summary>
    /// <param name="force">Whether to refill regardless of the current remaining count.</param>
    private async Task RefillInfiniteMixAsync(bool force, int batchSize = InfiniteMixBatchSize)
    {
        if (_infiniteMixLoading || !_infiniteMixEnabled || _infiniteMixPaused)
            return;
        var remaining = _queueIndex < 0 ? _queue.Count : _queue.Count - _queueIndex - 1;
        if (!force && remaining > InfiniteMixRefillThreshold)
            return;

        _infiniteMixLoading = true;
        var showProgress = force && batchSize == InfiniteMixBatchSize;
        if (showProgress)
            ShowInfiniteMixProgress(5);
        try
        {
            var profile = _settings.InfiniteMix;
            GenreCloudSource? local = null;
            if (profile.IncludeLocalLibrary)
            {
                local = await Task.Run(() =>
                {
                    using var db = AudioDatabase.OpenDefault();
                    return new GenreCloudSource(null, GenreCloudService.BuildSnapshot(db.GetTrackFacets(), null, 1000));
                });
            }
            if (showProgress)
                ShowInfiniteMixProgress(25);
            var enabledServers = _settings.OrynivoServers.Where(server =>
                !profile.ServerSelectionConfigured || profile.EnabledServerIds.Contains(server.Id));
            var remote = await Task.WhenAll(enabledServers.Select(LoadInfiniteMixSourceAsync));
            var sources = (local is null ? Enumerable.Empty<GenreCloudSource>() : new[] { local })
                .Concat(remote.Where(source => source is not null).Cast<GenreCloudSource>()).ToList();
            if (showProgress)
                ShowInfiniteMixProgress(45);
            var listeningWeights = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(profile.HistoryDays, 3, 90)).ToUnixTimeSeconds();
                var weights = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var (genre, seconds) in db.GetTopGenres(100, since))
                    foreach (var key in GenreCloudService.ResolveGenreKeys(genre))
                        weights[key] = weights.GetValueOrDefault(key) + seconds;
                return (IReadOnlyDictionary<string, double>)weights;
            });
            if (showProgress)
                ShowInfiniteMixProgress(60);
            var playCounts = await Task.Run(BuildInfiniteMixPlayCounts);
            var ranked = sources.SelectMany(source => source.Snapshot.Candidates
                    .Where(candidate => InfiniteMixGenreAllowed(candidate.GenreKey, profile))
                    .Where(candidate => !profile.ExcludedTrackKeys.Contains(BuildInfiniteMixTrackKey(source.Server, candidate.TrackId)))
                    .Select(candidate => new GenreCloudCandidate(source.Server, candidate,
                        CalculateInfiniteMixScore(source.Server, candidate, listeningWeights, playCounts, profile))))
                .OrderByDescending(candidate => candidate.Score)
                .ToList();
            var rows = await ResolveGenreCandidateRowsAsync(ranked, CancellationToken.None);
            if (showProgress)
                ShowInfiniteMixProgress(82);
            var queued = _queue.Select(item => item.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var recentArtists = _queue.Skip(Math.Max(0, _queue.Count - 4))
                .Select(item => item.Artist).Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            var selectedAlbums = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            var sourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var sourceLimit = Math.Max(1, (int)Math.Ceiling(batchSize / (double)Math.Max(1, sources.Count)) + 2);
            var selected = new List<ContentRow>();
            foreach (var row in rows)
            {
                if (selected.Count >= batchSize || string.IsNullOrWhiteSpace(row.FilePath) || queued.Contains(row.FilePath))
                    continue;
                if (sourceCounts.GetValueOrDefault(row.SourceKey) >= sourceLimit)
                    continue;
                if (!string.IsNullOrWhiteSpace(row.Artist) && recentArtists.Contains(row.Artist) && selected.Count < 8)
                    continue;
                var albumKey = $"{row.SourceKey}|{row.AlbumId}|{row.Album}";
                if (!string.IsNullOrWhiteSpace(row.Album) && !selectedAlbums.Add(albumKey))
                    continue;
                selected.Add(row);
                queued.Add(row.FilePath);
                sourceCounts[row.SourceKey] = sourceCounts.GetValueOrDefault(row.SourceKey) + 1;
            }
            foreach (var row in selected)
            {
                _queue.Add(ToPlaylistItem(row));
                var candidate = ranked.FirstOrDefault(item =>
                    item.Server?.Id == row.OrynivoServer?.Id && item.Track.TrackId == row.Id);
                if (candidate is not null)
                    _infiniteMixIdentitiesByPath[row.FilePath] = new(
                        BuildInfiniteMixTrackKey(candidate.Server, candidate.Track.TrackId), candidate.Track.GenreKey);
            }
            if (showProgress)
                ShowInfiniteMixProgress(100);
            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            await RefreshActiveGaplessQueueAsync();
        }
        catch (Exception exception)
        {
            CrashLogger.Log(exception, "Infinite Mix refill");
        }
        finally
        {
            _infiniteMixLoading = false;
            if (showProgress)
                HideInfiniteMixProgress();
        }
    }

    /// <summary>Shows the blocking initial-mix progress overlay at the supplied percentage.</summary>
    /// <param name="value">Progress from zero through one hundred.</param>
    private void ShowInfiniteMixProgress(double value)
    {
        InfiniteMixButton.IsEnabled = false;
        InfiniteMixProgressBar.Value = Math.Clamp(value, 0, 100);
        InfiniteMixProgressTextBlock.Text = $"{InfiniteMixProgressBar.Value:0} %";
        InfiniteMixLoadingOverlay.IsVisible = true;
    }

    /// <summary>Hides the initial-mix overlay and restores its start button.</summary>
    private void HideInfiniteMixProgress()
    {
        InfiniteMixLoadingOverlay.IsVisible = false;
        InfiniteMixButton.IsEnabled = true;
    }

    /// <summary>Loads one remote mix source without allowing an unavailable server to suppress other libraries.</summary>
    /// <param name="server">Configured remote library.</param>
    /// <returns>The source snapshot, or <see langword="null"/> when the server is unavailable.</returns>
    private async Task<GenreCloudSource?> LoadInfiniteMixSourceAsync(OrynivoServerSettings server)
    {
        try
        {
            return await LoadRemoteGenreCloudAsync(server, null, CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }

    private async void InfiniteMixAdjustButton_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await EditInfiniteMixSettingsAsync();
        UpdateInfiniteMixUi();
    }

    private async Task<bool> EditInfiniteMixSettingsAsync()
    {
        var dialog = new InfiniteMixDialog(_settings.InfiniteMix, _settings.OrynivoServers);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Result is null)
            return false;
        _settings.InfiniteMix = dialog.Result;
        await Task.Run(() => new SettingsStore().Save(_settings));
        return true;
    }

    private double CalculateInfiniteMixScore(
        OrynivoServerSettings? server,
        GenreCloudTrackCandidate candidate,
        IReadOnlyDictionary<string, double> listeningWeights,
        IReadOnlyDictionary<string, int> playCounts,
        InfiniteMixSettings profile)
    {
        var affinity = listeningWeights
            .Where(pair => GenreCloudService.IsDescendantOrSelf(candidate.GenreKey, pair.Key) ||
                           GenreCloudService.IsDescendantOrSelf(pair.Key, candidate.GenreKey))
            .Sum(pair => pair.Value);
        var familiar = 1d - Math.Clamp(profile.DiscoveryLevel, 0, 100) / 100d;
        var score = Math.Log10(1 + affinity) * (35 + familiar * 90);
        var isFavorite = server is null
            ? candidate.IsFavorite
            : IsOrynivoFavorite(server, "Track", candidate.TrackId);
        if (profile.WeightFavorites && isFavorite)
            score += 35;
        if (profile.PreferRareTracks)
        {
            var playCount = playCounts.GetValueOrDefault(BuildInfiniteMixTrackKey(server, candidate.TrackId));
            score += 50d / (1d + Math.Log2(1d + playCount));
        }
        score += profile.GenreFeedback.GetValueOrDefault(candidate.GenreKey) * 28;
        score += InfiniteMixMoodScore(profile.Mood, candidate.GenreKey);
        score += Random.Shared.NextDouble() * (8 + profile.DiscoveryLevel * 0.72);
        return score;
    }

    private static double InfiniteMixMoodScore(InfiniteMixMood mood, string genreKey)
    {
        if (mood == InfiniteMixMood.Balanced) return 0;
        var energetic = genreKey.Contains("dance", StringComparison.OrdinalIgnoreCase) ||
                        genreKey.Contains("rock", StringComparison.OrdinalIgnoreCase) ||
                        genreKey.Contains("metal", StringComparison.OrdinalIgnoreCase) ||
                        genreKey.Contains("punk", StringComparison.OrdinalIgnoreCase) ||
                        genreKey.Contains("hip-hop", StringComparison.OrdinalIgnoreCase);
        var calm = genreKey.Contains("ambient", StringComparison.OrdinalIgnoreCase) ||
                   genreKey.Contains("classical", StringComparison.OrdinalIgnoreCase) ||
                   genreKey.Contains("folk", StringComparison.OrdinalIgnoreCase) ||
                   genreKey.Contains("jazz", StringComparison.OrdinalIgnoreCase) ||
                   genreKey.Contains("new-age", StringComparison.OrdinalIgnoreCase);
        return mood == InfiniteMixMood.Energetic ? (energetic ? 45 : calm ? -18 : 0) : (calm ? 45 : energetic ? -18 : 0);
    }

    private static bool InfiniteMixGenreAllowed(string genreKey, InfiniteMixSettings profile)
    {
        static bool Matches(string key, string filter)
        {
            var normalized = filter.Trim();
            if (key.Contains(normalized, StringComparison.CurrentCultureIgnoreCase))
                return true;
            return GenreCloudService.ResolveGenreKeys(normalized).Any(filterKey =>
                GenreCloudService.IsDescendantOrSelf(key, filterKey) ||
                GenreCloudService.IsDescendantOrSelf(filterKey, key));
        }
        if (profile.IncludedGenres.Count > 0 && !profile.IncludedGenres.Any(filter => Matches(genreKey, filter)))
            return false;
        return !profile.ExcludedGenres.Any(filter => Matches(genreKey, filter));
    }

    private Dictionary<string, int> BuildInfiniteMixPlayCounts()
    {
        using var db = AudioDatabase.OpenDefault();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in db.GetRecentHistory(5000))
        {
            if (entry.TrackId is long localId)
            {
                var key = $"local:{localId}";
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
            if (!string.IsNullOrWhiteSpace(entry.ExternalId) && entry.ExternalId.StartsWith("orynivo:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = entry.ExternalId.Split(':');
                if (parts.Length >= 4)
                {
                    var key = $"server:{parts[1]}:{parts[3]}";
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }
        }
        return counts;
    }

    private static string BuildInfiniteMixTrackKey(OrynivoServerSettings? server, long trackId) =>
        server is null ? $"local:{trackId}" : $"server:{server.Id}:{trackId}";

    private async void InfiniteMixReplaceNextButton_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!_infiniteMixEnabled) return;
        var index = Math.Clamp(_queueIndex + 1, 0, _queue.Count);
        if (index < _queue.Count)
            _queue.RemoveAt(index);
        var previousCount = _queue.Count;
        await RefillInfiniteMixAsync(force: true, batchSize: 1);
        if (_queue.Count > previousCount && index < _queue.Count - 1)
            _queue.Move(_queue.Count - 1, index);
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
    }

    private async void InfiniteMixMoreButton_OnClick(object? sender, RoutedEventArgs e) =>
        await ApplyInfiniteMixFeedbackAsync(sender, 1, false);

    private async void InfiniteMixLessButton_OnClick(object? sender, RoutedEventArgs e) =>
        await ApplyInfiniteMixFeedbackAsync(sender, -1, false);

    private async void InfiniteMixExcludeButton_OnClick(object? sender, RoutedEventArgs e) =>
        await ApplyInfiniteMixFeedbackAsync(sender, 0, true);

    private async Task ApplyInfiniteMixFeedbackAsync(object? sender, int genreDelta, bool excludeTrack)
    {
        if (sender is not Avalonia.Controls.Button { Tag: ContentRow { QueueItem: not null } row } ||
            !_infiniteMixIdentitiesByPath.TryGetValue(row.QueueItem.FilePath, out var identity))
            return;
        if (excludeTrack)
            _settings.InfiniteMix.ExcludedTrackKeys.Add(identity.TrackKey);
        else
            _settings.InfiniteMix.GenreFeedback[identity.GenreKey] =
                Math.Clamp(_settings.InfiniteMix.GenreFeedback.GetValueOrDefault(identity.GenreKey) + genreDelta, -5, 5);
        await Task.Run(() => new SettingsStore().Save(_settings));
    }
}
