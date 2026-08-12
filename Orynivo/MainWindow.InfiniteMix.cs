using Avalonia.Interactivity;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

public partial class MainWindow
{
    private const int InfiniteMixBatchSize = 20;
    private const int InfiniteMixRefillThreshold = 5;
    private const int InfiniteMixCandidateWindowSize = 1000;
    private bool _infiniteMixEnabled;
    private bool _infiniteMixPaused;
    private bool _infiniteMixLoading;
    private int _infiniteMixCandidateOffset;
    private DateTimeOffset _lastInfiniteMixRefillAttempt = DateTimeOffset.MinValue;
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

        await StartInfiniteMixAsync(editSettings: true);
    }

    /// <summary>Starts a newly configured Infinite Mix and preserves an already audible item.</summary>
    /// <param name="editSettings">Whether the profile editor must be confirmed before generation.</param>
    private async Task StartInfiniteMixAsync(bool editSettings)
    {
        if (editSettings && !await EditInfiniteMixSettingsAsync())
            return;

        _infiniteMixEnabled = true;
        _infiniteMixPaused = false;
        _infiniteMixCandidateOffset = 0;
        _lastInfiniteMixRefillAttempt = DateTimeOffset.MinValue;
        _infiniteMixIdentitiesByPath.Clear();
        var hasActivePlayback = _player is not null && !string.IsNullOrWhiteSpace(_currentFilePath);
        var activeItem = hasActivePlayback
            ? GetPlaylistMetadata(_currentFilePath) ?? CreatePlaylistItem(_currentFilePath)
            : null;
        UpdateInfiniteMixUi();
        if (_currentTopLevelTag == "Queue")
            UpdateRestoreQueueButtonState("Queue");
        _queue.Clear();
        if (activeItem is not null)
            _queue.Add(activeItem);
        _queueIndex = activeItem is null ? -1 : 0;
        await RefillInfiniteMixAsync(force: true, refreshActivePlayback: false);
        var recommendationCount = _queue.Count - (activeItem is null ? 0 : 1);
        if (recommendationCount == 0)
        {
            StopInfiniteMix();
            StatusTextBlock.Text = LocalizationManager.Current.NoData;
            return;
        }

        if (_queueIndex < 0)
            _queueIndex = 0;
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        try
        {
            if (hasActivePlayback)
                await RefreshActiveGaplessQueueAsync();
            else
                await StartPlaybackAsync(_queue[0].FilePath);
        }
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
        if (remaining <= InfiniteMixRefillThreshold &&
            DateTimeOffset.UtcNow - _lastInfiniteMixRefillAttempt >= TimeSpan.FromSeconds(5))
            _ = RefillInfiniteMixAsync(force: false);
    }

    /// <summary>Ranks unified local/server candidates and appends a diverse batch to the queue.</summary>
    /// <param name="force">Whether to refill regardless of the current remaining count.</param>
    /// <param name="batchSize">Maximum number of recommendations to append.</param>
    /// <param name="refreshActivePlayback">Whether an active immutable gapless session must adopt the revised queue immediately.</param>
    private async Task RefillInfiniteMixAsync(
        bool force,
        int batchSize = InfiniteMixBatchSize,
        bool refreshActivePlayback = true)
    {
        if (_infiniteMixLoading || !_infiniteMixEnabled || _infiniteMixPaused)
            return;
        var remaining = _queueIndex < 0 ? _queue.Count : _queue.Count - _queueIndex - 1;
        if (!force && remaining > InfiniteMixRefillThreshold)
            return;

        _lastInfiniteMixRefillAttempt = DateTimeOffset.UtcNow;
        _infiniteMixLoading = true;
        var showProgress = force && batchSize == InfiniteMixBatchSize;
        if (showProgress)
            ShowInfiniteMixProgress(5);
        try
        {
            var profile = _settings.InfiniteMix;
            var includedGenreKeys = profile.IncludedGenres
                .SelectMany(GenreCloudService.ResolveGenreKeys)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var requestedGenreKeys = GenreCloudService.ResolveDescendantGenreKeys(includedGenreKeys);
            var sources = new List<GenreCloudSource>();
            if (profile.IncludeLocalLibrary)
            {
                sources.AddRange(await Task.Run(() =>
                {
                    using var db = AudioDatabase.OpenDefault();
                    var facets = db.GetTrackFacets();
                    IEnumerable<string?> keys = requestedGenreKeys.Count == 0
                        ? new string?[] { null }
                        : requestedGenreKeys.Cast<string?>();
                    return keys.Select(key => new GenreCloudSource(
                        null,
                        GenreCloudService.BuildSnapshot(
                            facets,
                            key,
                            InfiniteMixCandidateWindowSize,
                            _infiniteMixCandidateOffset))).ToList();
                }));
            }
            if (showProgress)
                ShowInfiniteMixProgress(25);
            var enabledServers = _settings.OrynivoServers.Where(server =>
                !profile.ServerSelectionConfigured || profile.EnabledServerIds.Contains(server.Id));
            var remoteTasks = enabledServers.SelectMany(server =>
                (requestedGenreKeys.Count == 0
                    ? (IEnumerable<string?>)new string?[] { null }
                    : requestedGenreKeys.Cast<string?>())
                .Select(key => LoadInfiniteMixSourceAsync(server, key, _infiniteMixCandidateOffset)));
            var remote = await Task.WhenAll(remoteTasks);
            sources.AddRange(remote.Where(source => source is not null).Cast<GenreCloudSource>());
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
                .GroupBy(candidate => BuildInfiniteMixTrackKey(candidate.Server, candidate.Track.TrackId), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
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
            var physicalSourceCount = sources
                .Select(source => source.Server?.Id ?? "local")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var sourceLimit = Math.Max(1, (int)Math.Ceiling(batchSize / (double)Math.Max(1, physicalSourceCount)) + 2);
            var selected = new List<ContentRow>();

            bool TrySelect(ContentRow row, bool requireNewAlbum, bool avoidRecentArtist)
            {
                if (selected.Count >= batchSize || string.IsNullOrWhiteSpace(row.FilePath) || queued.Contains(row.FilePath))
                    return false;
                if (sourceCounts.GetValueOrDefault(row.SourceKey) >= sourceLimit)
                    return false;
                if (avoidRecentArtist && !string.IsNullOrWhiteSpace(row.Artist) && recentArtists.Contains(row.Artist))
                    return false;
                var albumKey = $"{row.SourceKey}|{row.AlbumId}|{row.Album}";
                if (requireNewAlbum && !string.IsNullOrWhiteSpace(row.Album) && !selectedAlbums.Add(albumKey))
                    return false;
                selected.Add(row);
                queued.Add(row.FilePath);
                sourceCounts[row.SourceKey] = sourceCounts.GetValueOrDefault(row.SourceKey) + 1;
                if (!string.IsNullOrWhiteSpace(row.Artist))
                    recentArtists.Add(row.Artist);
                return true;
            }

            foreach (var row in rows)
                TrySelect(row, requireNewAlbum: true, avoidRecentArtist: true);
            foreach (var row in rows)
                TrySelect(row, requireNewAlbum: true, avoidRecentArtist: false);
            foreach (var row in rows)
                TrySelect(row, requireNewAlbum: false, avoidRecentArtist: false);
            foreach (var row in selected)
            {
                _queue.Add(ToPlaylistItem(row));
                var candidate = ranked.FirstOrDefault(item =>
                    item.Server?.Id == row.OrynivoServer?.Id && item.Track.TrackId == row.Id);
                if (candidate is not null)
                    _infiniteMixIdentitiesByPath[row.FilePath] = new(
                        BuildInfiniteMixTrackKey(candidate.Server, candidate.Track.TrackId), candidate.Track.GenreKey);
            }
            _infiniteMixCandidateOffset =
                (_infiniteMixCandidateOffset + InfiniteMixCandidateWindowSize) % 2_000_000_000;
            if (showProgress)
                ShowInfiniteMixProgress(100);
            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            if (refreshActivePlayback)
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
    /// <param name="genreKey">Selected taxonomy branch, or <see langword="null"/> for all genres.</param>
    /// <param name="candidateOffset">Offset into the provider's stable candidate order.</param>
    /// <returns>The source snapshot, or <see langword="null"/> when the server is unavailable.</returns>
    private async Task<GenreCloudSource?> LoadInfiniteMixSourceAsync(
        OrynivoServerSettings server,
        string? genreKey,
        int candidateOffset)
    {
        try
        {
            var snapshot = await _orynivoClient.GetGenreCloudAsync(
                server,
                genreKey,
                InfiniteMixCandidateWindowSize,
                candidateOffset,
                CancellationToken.None);
            var returnedWrongLevel = !string.Equals(snapshot.ParentKey, genreKey, StringComparison.Ordinal);
            if (returnedWrongLevel || snapshot.Nodes.Count == 0 && snapshot.Candidates.Count == 0)
            {
                var facets = await _orynivoClient.GetTrackFacetsAsync(server, CancellationToken.None);
                snapshot = GenreCloudService.BuildSnapshot(
                    facets,
                    genreKey,
                    InfiniteMixCandidateWindowSize,
                    candidateOffset);
            }
            return new GenreCloudSource(server, snapshot);
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

    private async Task<bool> EditInfiniteMixSettingsAsync(InfiniteMixSettings? initialSettings = null)
    {
        var dialog = new InfiniteMixDialog(initialSettings ?? _settings.InfiniteMix, _settings.OrynivoServers);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Result is null)
            return false;
        _settings.InfiniteMix = dialog.Result;
        await Task.Run(() => new SettingsStore().Save(_settings));
        return true;
    }

    /// <summary>Creates an independent editable copy of an Infinite Mix profile.</summary>
    /// <param name="source">Persisted profile to copy.</param>
    /// <returns>A profile whose mutable collections are independent from the source.</returns>
    private static InfiniteMixSettings CloneInfiniteMixSettings(InfiniteMixSettings source) => new()
    {
        Mood = source.Mood,
        DiscoveryLevel = source.DiscoveryLevel,
        HistoryDays = source.HistoryDays,
        IncludeLocalLibrary = source.IncludeLocalLibrary,
        EnabledServerIds = new HashSet<string>(source.EnabledServerIds, StringComparer.OrdinalIgnoreCase),
        ServerSelectionConfigured = source.ServerSelectionConfigured,
        WeightFavorites = source.WeightFavorites,
        PreferRareTracks = source.PreferRareTracks,
        IncludedGenres = source.IncludedGenres.ToList(),
        ExcludedGenres = source.ExcludedGenres.ToList(),
        GenreFeedback = new Dictionary<string, int>(source.GenreFeedback, StringComparer.OrdinalIgnoreCase),
        ExcludedTrackKeys = new HashSet<string>(source.ExcludedTrackKeys, StringComparer.OrdinalIgnoreCase)
    };

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
        // Personal ratings are the strongest explicit signal. Community ratings are
        // deliberately weaker because their vote count is not part of the compact cloud payload.
        score += candidate.UserRating switch
        {
            5 => 90,
            4 => 48,
            3 => 8,
            2 => -55,
            1 => -120,
            _ => 0
        };
        if (candidate.MusicBrainzRating is double communityRating)
        {
            var voteConfidence = Math.Min(1d, Math.Log10(1d + candidate.MusicBrainzRatingVotes.GetValueOrDefault()) / 2d);
            score += (communityRating - 3d) * 12d * voteConfidence;
        }
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
            if (key.Contains(normalized, StringComparison.CurrentCultureIgnoreCase) ||
                GenreCloudService.IsDescendantOrSelf(key, normalized))
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
